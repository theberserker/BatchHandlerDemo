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
        //private int maxCount = 4;
        //private System.Timers.Timer timer;
        private readonly BatchConverter batchConverter;
        private readonly Batcher batcher;
        //private readonly object syncLock = new object();
        private SemaphoreSlim semaphore;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<Result>> dtoToCompletionSources = new ConcurrentDictionary<int, TaskCompletionSource<Result>>();

        public BatchProcessor(BatchConverter batchConverter, Batcher batcher)
        {
            semaphore = new SemaphoreSlim(1, 1);
            this.batchConverter = batchConverter;
            this.batcher = batcher;

            this.batcher.OnActionable += integers =>
            {
                //var results = await batchConverter.Convert(integers);

                try
                {

                    //semaphore.Wait(); // TODO: WaitAsync? or AsyncEx.AsyncLock?

                    this.batchConverter.Convert(integers)
                        .ContinueWith(t =>
                        {
                            switch (t.Status)
                            {
                                case TaskStatus.Canceled:
                                    ForeachTcs(tcs => tcs.SetCanceled());
                                    break;
                                case TaskStatus.Faulted:
                                    ForeachTcs(tcs => tcs.SetException(t.Exception));
                                    break;
                                case TaskStatus.RanToCompletion:
                                    ProcessSuccess(t.Result);
                                    break;
                            }
                            dtoToCompletionSources.Clear();

                        }/*, TaskContinuationOptions.LongRunning*/);
                }
                finally
                {
                    //semaphore.Release();
                }
            };
        }

        private void ProcessSuccess(Result[] results)
        {
            if (results.Length != dtoToCompletionSources.Count)
            {
                throw new Exception($"There is a mismatch between result set length ({results.Length}) and task completion sources for per item handlers ({dtoToCompletionSources.Count}). " +
                                    $"This is a bug in synchronization implementation on external entity lost the request identifiers. More likely former than later :/");
            }

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

        public Task<Result> Convert(int i)
        {
            var tcs = new TaskCompletionSource<Result>();

            batcher.Register(i);

            if (!dtoToCompletionSources.TryAdd(i, tcs))
            {
                throw new Exception($"Key {i} was already present.");
            }

            return tcs.Task;
        }
    }

    public class Batcher
    {
        private readonly int maxCount = 1;
        private readonly List<int> items;
        public event Action<int[]> OnActionable;

        private object sync = new object();

        public Batcher()
        {
            items = new List<int>(maxCount);
        }

        public void Register(int i)
        {
            lock (sync)
            {
                items.Add(i);

                if (items.Count == maxCount /*|| timer */)
                {
                    OnActionable?.Invoke(items.ToArray());
                    items.Clear();
                }
            }
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
