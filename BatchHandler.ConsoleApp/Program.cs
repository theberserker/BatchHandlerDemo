using System;
using System.Linq;
using System.Threading.Tasks;

namespace BatchHandler.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            /* Simple Handler - demonstrates TaskCompletion one by one */
            //await InvokeSimpleHandler();


            /* Batching Handler - demonstrates using events and batching handlers */
            await InvokeBatchingHandler();

            Console.WriteLine("Done, press key.");
            Console.ReadKey();
        }

        private static async Task InvokeBatchingHandler()
        {
            var timer = new MyTimer(1000);
            var batchProcessor = new BatchProcessor(new BatchConverter(), new Batcher(timer), 5, 10);

            int rangeTo = 1008;
            var handlers = Enumerable.Range(1, rangeTo)
                .Select(x => new { Number = x, CalculateTask = new BatchingHandler(batchProcessor).Handle(x) })
                .ToList();

            // Throw some more work, fire and forget
            //await Task.Run(async () =>
            //{
            //    Enumerable.Range(rangeTo+1, 10000)
            //        .Select(x => new {Number = x, CalculateTask = new BatchingHandler(batchProcessor).Handle(x)})
            //        //.Select(async x => Console.WriteLine(await x.CalculateTask))
            //        .Select(async x => await x.CalculateTask)
            //        .ToList();
            //});

            foreach (var h in handlers)
            {
                Result hexResult = null;
                try
                {
                    hexResult = await h.CalculateTask;
                }
                catch (ItemFailedException ex)
                {
                    Console.WriteLine("ItemFailedException:" + ex);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unexpected exception:" + ex);
                }

                Console.WriteLine($"{h.Number}: {h.CalculateTask.Result}");
            }
        }

        private static async Task InvokeSimpleHandler()
        {
            var handlers = Enumerable.Range(1, 30)
                .Select(x => new { Number = x, CalculateTask = new SimpleHandler().Handle(x) })
                .ToList();

            foreach (var h in handlers)
            {
                string hexResult = null;
                try
                {
                    hexResult = await h.CalculateTask;
                }
                catch (Exception ex)
                {
                    hexResult = $"Error message: {ex.Message}";
                }

                Console.WriteLine($"{h.Number}:{hexResult}");
            }
        }
    }
}
