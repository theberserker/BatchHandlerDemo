using System;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    public class SimpleHandler
    {
        public int Number { get; }

        public SimpleHandler(int number)
        {
            this.Number = number;
        }

        public Task<string> Handle()
        {
            var tcs = new TaskCompletionSource<string>();
            try
            {
                var task = SimpleConverter.Convert(Number);
                task.ContinueWith(t => tcs.SetResult(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);
                task.ContinueWith(t => tcs.SetException(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
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
    /// Calculates item by one.
    /// </summary>
    public static class SimpleConverter
    {
        static readonly Random rand = new Random();

        public static async Task<string> Convert(int i)
        {
            await Task.Delay(rand.Next(1000));

            if (i % 10 == 0)
            {
                throw new Exception($"Error occoured at {i}.");
            }

            string hex = i.ToString("X2");
            return hex;
        }
    }
}
