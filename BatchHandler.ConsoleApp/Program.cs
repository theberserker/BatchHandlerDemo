using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            /* Simple Handler - demonstrates TaskCompletion one by one */

            //var handlers = Enumerable.Range(1, 30)
            //    .Select(x => new SimpleHandler(x))
            //    .Select(x => new { x.Number, CalculateTask = x.Handle()})
            //    .ToList();

            //foreach (var h in handlers)
            //{
            //    string hexResult = null;
            //    try
            //    {
            //        hexResult = await h.CalculateTask;
            //    }
            //    catch (Exception ex)
            //    {
            //        hexResult = $"Error message: {ex.Message}";
            //    }
            //    Console.WriteLine($"{h.Number}:{hexResult}");
            //}


            /* Batching Handler - demonstrates using events and batching handlers */
            var handlers = Enumerable.Range(1, 30)
                .Select(x => new BatchingHandler(x))
                .Select(x => x.Handle());

            await Task.WhenAll(handlers);

            foreach (var result in handlers.SelectMany(x => x.Result))
            {
                Console.WriteLine(result);
            }

            Console.ReadKey();
        }
    }
}
