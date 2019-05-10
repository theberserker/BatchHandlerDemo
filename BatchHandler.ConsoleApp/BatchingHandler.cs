using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    /// <summary>
    /// These are multiple instance.
    /// </summary>
    public class BatchingHandler
    {
        private readonly BatchProcessor batchProcessor;

        public BatchingHandler(BatchProcessor batchProcessor)
        {
            this.batchProcessor = batchProcessor;
        }

        public Task<Result> Handle(int number)
        {
            return batchProcessor.Convert(number);
        }
    }

    /// <summary>
    /// This is a single instance that gathers data and flushes it when required
    /// </summary>
    public class BatchProcessor
    {
        private readonly BatchConverter batchConverter;
        private readonly Batcher batcher;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<int, TaskCompletionSource<Result>> dtoToCompletionSources = new ConcurrentDictionary<int, TaskCompletionSource<Result>>();
        private readonly ConcurrentDictionary<Guid, List<int>> batchIdToItems = new ConcurrentDictionary<Guid, List<int>>();

        public BatchProcessor(BatchConverter batchConverter, Batcher batcher)
        {
            this.batchConverter = batchConverter;
            this.batcher = batcher;

            this.batcher.OnActionable += batch =>
            {
                // Allow single execution to the Batch Convert API
                semaphore.Wait();
                this.batchConverter.Convert(batch.Integers)
                    .ContinueWith(t =>
                    {
                        switch (t.Status)
                        {
                            case TaskStatus.Canceled:
                                // If the whole batch request was cancelled, set all the individual tasks as cancelled
                                ForeachTcs(tcs => tcs.SetCanceled());
                                break;
                            case TaskStatus.Faulted:
                                // If the whole batch request was faulted, set exception to all the individual tasks
                                ForeachTcs(tcs => tcs.SetException(t.Exception));
                                break;
                            case TaskStatus.RanToCompletion:
                                // If it was ok response, we have to assign result or exception per item (Api returns ether a result or exception object per item)
                                ProcessSuccess(t.Result);
                                break;
                        }

                        RemoveFinishedCompletionSources(batch.BatchId);

                    }/*, TaskContinuationOptions.LongRunning*/);
                semaphore.Release();
            };
        }

        /// <summary>
        /// Checks if the batched result was an exception or success result and sets it accordingly.
        /// </summary>
        private void ProcessSuccess(Result[] results)
        {
            foreach (var result in results)
            {
                if (result.Exception == null)
                {
                    dtoToCompletionSources[result.SourceDto].SetResult(result);
                }
                else
                {
                    dtoToCompletionSources[result.SourceDto].SetException(result.Exception);
                }
            }
        }

        private void ForeachTcs(Action<TaskCompletionSource<Result>> tcsAction)
        {
            foreach (var tcs in dtoToCompletionSources.Values)
            {
                tcsAction(tcs);
            }
        }

        /// <summary>
        /// Removes the finished TaskCompletionSources from the <see cref="dtoToCompletionSources"/> dictionary.
        /// </summary>
        /// <param name="batchId">Id of the batch that was processed.</param>
        private void RemoveFinishedCompletionSources(Guid batchId)
        {
            foreach (var i in batchIdToItems[batchId])
            {
                var tcs = dtoToCompletionSources[i];
                bool isNotInFinalState = !(tcs.Task.IsCompleted || tcs.Task.IsCanceled);
                if (isNotInFinalState)
                {
                    throw new Exception($"We should have already marked the task as completed or cancelled but status for {i} was '{tcs.Task.Status}'.");
                }

                if (!dtoToCompletionSources.TryRemove(i, out _))
                {
                    throw new Exception($"The key is not present anymore: {i}.");
                }
            }
        }

        public Task<Result> Convert(int i)
        {
            var tcs = new TaskCompletionSource<Result>();

            var batchId = batcher.Register(i);
            if (!dtoToCompletionSources.TryAdd(i, tcs))
            {
                throw new Exception($"Key {i} was already present.");
            }

            // Populate the list of integers of a current batch.
            var intList = batchIdToItems.GetOrAdd(batchId, key => new List<int>(batcher.MaxCount));
            intList.Add(i);

            return tcs.Task;
        }
    }

    public class Batcher
    {
        public readonly int MaxCount = 2;
        private readonly List<int> items;
        public event Action<(Guid BatchId, int[] Integers)> OnActionable;

        private Guid currentBatchId;
        private readonly object sync = new object();

        public Batcher()
        {
            items = new List<int>(MaxCount);
            currentBatchId = Guid.NewGuid();
        }

        
        public Guid Register(int i)
        {
            (Guid, int[]) itemsToPropagate = default;

            Guid returnBatchId = default;
            lock (sync)
            {
                items.Add(i);

                if (items.Count == MaxCount /*|| timer */)
                {
                    returnBatchId = currentBatchId;
                    itemsToPropagate = (returnBatchId, items.ToArray());

                    items.Clear();
                    currentBatchId = Guid.NewGuid();
                }
                else
                {
                    returnBatchId = currentBatchId;
                }
            }

            if (itemsToPropagate != default)
            {
                OnActionable?.Invoke(itemsToPropagate);
            }

            return returnBatchId;
        }
    }

    /// <summary>
    /// Calculates items in batch.
    /// </summary>
    public class BatchConverter
    {
        public Task<Result[]> Convert(int[] array)
        {
            return Task.Factory.StartNew(arg =>
            {
                // TODO: Test throw exception!
                return ((int[]) arg)
                    .Select(i => i % 10 == 0
                        ? new Result(i, new Exception($"Error occoured at {i}."))
                        : new Result(i, i.ToString("X2")))
                    .ToArray();
            }, array);
        }
    }

    public class Result
    {
        public Result(int sourceDto, string hex)
        {
            SourceDto = sourceDto;
            Hex = hex;
        }

        public Result(int sourceDto, Exception exception)
        {
            SourceDto = sourceDto;
            Exception = exception;
        }

        /// <summary>
        /// Source DTO; this will represent index of a posted token in my case.
        /// </summary>
        public int SourceDto { get; }
        public string Hex { get; }
        public Exception Exception { get; }

        public override string ToString()
        {
            return Exception == null ? Hex : Exception.Message;
        }
    }


    /// <summary>
    /// https://stackoverflow.com/questions/4890915/is-there-a-task-based-replacement-for-system-threading-timer
    /// Makes sure that the period is how often it is run by deducting the task's run time from the period for the next delay.
    /// </summary>
    public static class PeriodicTask
    {
        static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

        public static async Task Run(
            Func<Task> action,
            TimeSpan period,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Stopwatch.Reset();

                if (!cancellationToken.IsCancellationRequested)
                    await action();

                Stopwatch.Stop();

                await Task.Delay(period - Stopwatch.Elapsed, cancellationToken);
            }
        }
    }
}
