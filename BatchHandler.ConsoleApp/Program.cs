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
            var batchProcessor = new BatchProcessor(new BatchConverter(), new Batcher(), 5, 10);

            int rangeTo = 1000;
            var handlers = Enumerable.Range(1, rangeTo)
                .Select(x => new { Number = x, CalculateTask = new BatchingHandler(batchProcessor).Handle(x) })
                .ToList();

            foreach (var h in handlers)
            {
                Result hexResult = null;
                try
                {
                    hexResult = await h.CalculateTask;

                    // Throw more handlers into the game...
                    var handlers2 = Enumerable.Range(rangeTo+1 * hexResult.SourceDto, 100)
                        .Select(x => new {Number = x, CalculateTask = new BatchingHandler(batchProcessor).Handle(x)})
                        .Select(async x =>
                        {
                            try
                            {
                                return await x.CalculateTask;
                            }
                            catch (ItemFailedException e)
                            {
                                Console.WriteLine("ItemFailedException (inner):" + e);
                                return new Result(x.Number, e);
                            }
                        })
                        .ToList();
                }
                catch (ItemFailedException ex)
                {
                    Console.WriteLine("ItemFailedException:" + ex);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unexpected exception:" + ex);
                }

                Console.WriteLine(hexResult);
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
