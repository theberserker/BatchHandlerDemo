using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    public class BatchingHandler
    {
        private readonly int i;
        private static readonly int maxCount = 4;
        //private static BindingList<int> l;
        private static List<int> l = new List<int>(maxCount);
        private static readonly BatchConverter batchConverter = new BatchConverter();
        private bool timeToRun = false; // TODO: Implement scheduling

        public BatchingHandler(int i)
        {
            this.i = i;
        }

        private void ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType != ListChangedType.ItemAdded)
            {
                return;
            }
        }

        public Task<Result[]> Handle()
        {
            var tcs = new TaskCompletionSource<Result[]>();
            
            // TODO: Thread safety

            batchConverter.OnCompleted += results =>
            {
                tcs.SetResult(results);
            };

            batchConverter.OnErrored += ex =>
            {
                tcs.TrySetException(ex);
            };

            if (l.Count < maxCount)
            {
                l.Add(i);
            }
            if (l.Count == maxCount || timeToRun)
            {
                try
                {
                    var all = l.ToArray();
                    batchConverter.Convert(all); // TODO: Converter could/should be void
                    l.Clear();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            // TODO: Some logic
            //if (result == "something")
            //{
            //    tcs.TrySetException()
            //}

            return tcs.Task;

        }
    }


    /// <summary>
    /// Calculates items in batch.
    /// </summary>
    public class BatchConverter
    {
        //public event EventHandler OnBatchProcessed;
        public event Action<Result[]> OnCompleted; 
        public event Action<Exception> OnErrored;

        readonly Random rand = new Random();

        public async void Convert(int[] array)
        {
            try
            {
                var result = await Task.Factory.StartNew(arg =>
                {
                    // TODO: Test throw exception!
                    return ((int[]) arg)
                        .Select(i => i % 10 == 0
                            ? new Result(new Exception($"Error occoured at {i}."))
                            : new Result(i.ToString("X2")))
                        .ToArray();
                }, array);

                OnCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                OnErrored?.Invoke(ex);
            }
        }
}

    public class Result
    {
        public Result(string hex)
        {
            Hex = hex;
        }

        public Result(Exception exception)
        {
            Exception = exception;
        }

        public string Hex { get; }
        public Exception Exception { get; }

        public override string ToString()
        {
            return Exception == null ? Hex : Exception.Message;
        }
    }
}
