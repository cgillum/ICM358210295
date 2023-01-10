using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ICM358210295
{
    public static class Function1
    {
        [FunctionName(nameof(StartManyOrchestrations))]
        public static async Task<IActionResult> StartManyOrchestrations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [DurableClient] IDurableClient client,
            ILogger log)
        {
            if (!int.TryParse(req.Query["count"], out int count) || count < 1)
            {
                return new BadRequestObjectResult("A 'count' query string parameter is required and it must contain a positive number.");
            }

            string runId = DateTime.UtcNow.ToString("yyyyMMdd-hhmmss");
            EntityId counterEntityId = new(nameof(Counter), runId);

            log.LogWarning($"Scheduling {count} orchestrations with a prefix of '{runId}' and an entity ID of '{counterEntityId}...");

            // Learn more about this technoque here: https://dev.to/cgillum/scheduling-tons-of-orchestrator-functions-concurrently-in-c-1ih7
            await Enumerable.Range(0, count).ParallelForEachAsync(200, i =>
            {
                string instanceId = $"{runId}-{i:X16}";
                string entityKey = runId;
                return client.StartNewAsync(nameof(Orchestrator), instanceId, input: entityKey);
            });

            string statusMessage = $"All {count} orchestrations were scheduled successfully! Entity ID = '{counterEntityId}'.";
            log.LogWarning(statusMessage);

            return new OkObjectResult(statusMessage);
        }

        // Simple orchestrator that just calls the "Increment" method on the counter entity
        [FunctionName(nameof(Orchestrator))]
        public static void Orchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string entityKey = context.GetInput<string>();
            
            context.SignalEntity(
                entity: new EntityId(nameof(Counter), entityKey), 
                operationName: nameof(Counter.Increment));
        }

        #region Helpers
        public static async Task ParallelForEachAsync<T>(this IEnumerable<T> items, int maxConcurrency, Func<T, Task> action)
        {
            List<Task> tasks;
            if (items is ICollection<T> itemCollection)
            {
                tasks = new List<Task>(itemCollection.Count);
            }
            else
            {
                tasks = new List<Task>();
            }

            using var semaphore = new SemaphoreSlim(maxConcurrency);
            foreach (T item in items)
            {
                tasks.Add(InvokeThrottledAction(item, action, semaphore));
            }

            await Task.WhenAll(tasks);
        }

        static async Task InvokeThrottledAction<T>(T item, Func<T, Task> action, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                await action(item);
            }
            finally
            {
                semaphore.Release();
            }
        }
        #endregion
    }

    public class Counter
    {
        readonly ILogger logger;

        public Counter(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<Counter>();
        }

        [JsonProperty("value")]
        public int CurrentValue { get; set; }

        public void Increment()
        {
            this.CurrentValue += 1;
            this.logger.LogWarning("Counter value: {value}", this.CurrentValue);
        }

        public void Reset() => this.CurrentValue = 0;

        public int Get() => this.CurrentValue;

        [FunctionName(nameof(Counter))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
            => ctx.DispatchAsync<Counter>();
    }
}