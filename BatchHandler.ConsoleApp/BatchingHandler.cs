using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer=System.Timers.Timer;

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
        private readonly SemaphoreSlim semaphore;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<Result>> dtoToCompletionSources = new ConcurrentDictionary<int, TaskCompletionSource<Result>>();
        private readonly ConcurrentDictionary<Guid, List<int>> batchIdToItems = new ConcurrentDictionary<Guid, List<int>>();

        public BatchProcessor(BatchConverter batchConverter, Batcher batcher, int semaphoreInitial, int maxConcurrentRequests)
        {
            this.batchConverter = batchConverter;
            this.batcher = batcher;
            this.semaphore = new SemaphoreSlim(semaphoreInitial, maxConcurrentRequests);

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
                        if (!batchIdToItems.TryRemove(batch.BatchId, out _))
                        {
                            throw new KeyNotFoundException($"Could not find key {batch.BatchId}");
                        }


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
                    throw new KeyNotFoundException($"The key is not present anymore: {i}.");
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

    /// <summary>
    /// Batches items in a way that ether max quantity or max elapsed time is reached.
    /// In case the timer option was reached, the timer will also stop itself, and get invoked only on next items being added,
    /// as it is expected that timer based executions will get triggered only when there are insufficient handlers that would fill up the queue.
    /// </summary>
    public class Batcher
    {
        private readonly MyTimer timer;
        private readonly State state;
        public readonly int MaxCount = 100;
        public event Action<(Guid BatchId, int[] Integers)> OnActionable;

        /// <summary>
        /// Synchronization object for any changes over state members.
        /// </summary>
        private readonly object sync = new object();

        /// <summary>
        /// State of this class. Represents the pending batch ID for <see cref="items"/>.
        /// </summary>
        //private Guid currentBatchId;

        /// <summary>
        /// State of this class. Represents items in a batch for <see cref="currentBatchId"/>.
        /// </summary>
        //private readonly List<int> items;

        public Batcher(MyTimer timer)
        {
            this.timer = timer;
            state = new State(MaxCount);
            //items = new List<int>(MaxCount);
            //currentBatchId = Guid.NewGuid();

            this.timer.Elapsed += Timer_Elapsed;
        }

        /// <summary>
        /// Handles cases on timer when batches should become actionable, because they will obviously not reach their full potential and should be served in timely-fashion.
        /// </summary>
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            (Guid, int[]) itemsToPropagate = default;
            lock (sync)
            {
                timer.Stop();
                if (!state.HasItems)
                {
                    Console.WriteLine($"Reached timer, but there is nothing to process.");
                    return;
                }

                Console.WriteLine($"Reached timer. Items in a batch: {state.ItemCount}.");
                itemsToPropagate = state.ToResult();
                state.RestartBatch();
            }

            OnActionable?.Invoke(itemsToPropagate);
            timer.StartIfNotRunning();
        }

        /// <summary>
        /// Handles
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public Guid Register(int i)
        {
            (Guid, int[]) itemsToPropagate = default;
            Guid registrationItemBatchId = default;

            lock (sync)
            {
                registrationItemBatchId = state.CurrentBatchId;
                state.Add(i);
                timer.StartIfNotRunning();

                if (!state.HasReachedMax)
                {
                    // in this case we are still processing existing batch
                    return registrationItemBatchId;
                }

                Console.WriteLine($"The batch has reached limit of {state.ItemCount}.");
                itemsToPropagate = state.ToResult();
                state.RestartBatch();

                // Stop the timer, as the batch is done. Next iterations should restart it for themselves.
                timer.Stop();
            }

            // if we have reached this point, we have items to propagate
            OnActionable?.Invoke(itemsToPropagate);

            return registrationItemBatchId;
        }

        /// <summary>
        /// Single instance of state for a <see cref="Batcher"/>.
        /// </summary>
        class State
        {
            public State(int maxItemCount)
            {
                this.items = new List<int>(maxItemCount);
                this.CurrentBatchId = Guid.NewGuid();
                this.maxItemCount = maxItemCount;
            }

            private readonly List<int> items;
            private readonly int maxItemCount;

            public Guid CurrentBatchId { get; private set; }

            public (Guid batchId, int[] items) ToResult()
            {
                return (CurrentBatchId, items.ToArray());
            }

            public void Add(int i)
            {
                items.Add(i);
            }

            public bool HasReachedMax => items.Count == maxItemCount;
            public bool HasItems => items.Any();
            public int ItemCount => items.Count;

            /// <summary>
            /// Resets the state and starts a new batch.
            /// </summary>
            public void RestartBatch()
            {
                items.Clear();
                CurrentBatchId = Guid.NewGuid();
            }
        }
    }

    /// <summary>
    /// Calculates items in batch.
    /// </summary>
    public class BatchConverter
    {
        public async Task<Result[]> Convert(int[] array)
        {
            await Task.Delay(500);
            return array.Select(i => i % 1000 == 0
                    ? new Result(i, new ItemFailedException($"Error occoured at {i}."))
                    : new Result(i, i.ToString("X2")))
                .ToArray();

        }
    }

    public class Result
    {
        public Result(int sourceDto, string hex)
        {
            SourceDto = sourceDto;
            Hex = hex;
        }

        public Result(int sourceDto, ItemFailedException exception)
        {
            SourceDto = sourceDto;
            Exception = exception;
        }

        /// <summary>
        /// Source DTO; this will represent index of a posted token in my case.
        /// </summary>
        public int SourceDto { get; }
        public string Hex { get; }
        public ItemFailedException Exception { get; }

        public override string ToString()
        {
            return Exception == null ? Hex : Exception.Message;
        }
    }

    public class MyTimer : IDisposable
    {
        private readonly Timer t;

        public MyTimer(double expirationMilliseconds)
        {
            t = new Timer(expirationMilliseconds)
            {
                Enabled = false,
                AutoReset = false, // we will control the new iteration
            };
        }

        /// <summary>
        /// Subscribe outer subscriptions directly to the Timer t.
        /// </summary>
        public event ElapsedEventHandler Elapsed
        {
            add => t.Elapsed += value;
            remove => t.Elapsed -= value;
        }

        public void StartIfNotRunning()
        {
            if (!t.Enabled)
            {
                t.Start();
            }
        }

        public void Stop()
        {
            t.Stop();
        }

        public void Dispose()
        {
            t?.Dispose();
        }
    }
}
