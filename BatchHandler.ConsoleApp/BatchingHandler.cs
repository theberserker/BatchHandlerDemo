using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    public class BatchingHandler
    {
        private readonly int i;
        private readonly int maxCount = 10;
        BindingList<int> l;

        public BatchingHandler(int i)
        {
            this.i = i;
            this.l = new BindingList<int>();
            l.RaiseListChangedEvents = true;
            l.ListChanged += ListChanged;
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

            if (l.Count < maxCount)
            {
                l.Add(i);
            }
            else
            {
                try
                {
                    var all = l.ToArray();
                    var resultAllTask = BatchConverter.Convert(all);
                    l.Clear();
                    resultAllTask.ContinueWith(t => tcs.SetResult(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);
                    resultAllTask.ContinueWith(t => tcs.SetException(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
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
    public static class BatchConverter
    {
        static readonly Random rand = new Random();

        public static async Task<Result[]> Convert(int[] iArray)
        {
            await Task.Delay(rand.Next(1200));
            return iArray
                .Select(i => i % 10 == 0 ? new Result(new Exception($"Error occoured at {i}.")) : new Result(i.ToString("X2")))
                .ToArray();
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
    }
}
