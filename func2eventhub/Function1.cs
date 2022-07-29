using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace func2eventhub1
{
    public static class Function1
    {
        // connection string to the Event Hubs namespace
        private const string connectionString = "Endpoint=sb://adx2func2eventhub1.servicebus.windows.net/;SharedAccessKeyName=demo1;SharedAccessKey=974sWFQNoePWnKtnss8NyuE2A6d9J3WPlrGtj9xW4jM=;EntityPath=demo1";

        // name of the event hub
        private const string eventHubName = "demo1";

        
        // The Event Hubs client types are safe to cache and use as a singleton for the lifetime
        // of the application, which is best practice when events are being published or read regularly.
        static EventHubProducerClient producerClient;

        const string Cluster = "https://adxclustercsa.eastasia.kusto.windows.net";
        const string Database = "mydb1";


        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // number of events to be sent to the event hub
        int numOfEvents = 0;

        log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            // Create a producer client that you can use to send events to an event hub
            producerClient = new EventHubProducerClient(connectionString, eventHubName);

            // Create a batch of events 
            using EventDataBatch eventBatch = await producerClient.CreateBatchAsync();

            // The query provider is the main interface to use when querying Kusto.
            // It is recommended that the provider be created once for a specific target database,
            // and then be reused many times (potentially across threads) until it is disposed-of.
            var kcsb = new KustoConnectionStringBuilder(Cluster, Database)
                .WithAadUserPromptAuthentication();
            using (var queryProvider = KustoClientFactory.CreateCslQueryProvider(kcsb))
            {
                // The query -- Note that for demonstration purposes, we send a query that asks for two different
                // result sets (HowManyRecords and SampleRecords).
                var query = "General | where RecordType in ('93','94','95','96','97') | project CreationTime, LocalCreateTime = datetime_add('hour',8,CreationTime),Operation,RecordType,Workload,UserId,ClientIP| order by CreationTime asc";

                // It is strongly recommended that each request has its own unique
                // request identifier. This is mandatory for some scenarios (such as cancelling queries)
                // and will make troubleshooting easier in others.
                var clientRequestProperties = new ClientRequestProperties() { ClientRequestId = Guid.NewGuid().ToString() };
                using (var reader = queryProvider.ExecuteQuery(query, clientRequestProperties))
                {
                    while (reader.Read())
                    {
                        // Important note: For demonstration purposes we show how to read the data
                        // using the "bare bones" IDataReader interface. In a production environment
                        // one would normally use some ORM library to automatically map the data from
                        // IDataReader into a strongly-typed record type (e.g. Dapper.Net, AutoMapper, etc.)
                        DateTime CreationTime = reader.GetDateTime(0);
                        DateTime type = reader.GetDateTime(1);
                        string Operation = reader.GetString(2);
                        string RecordType = reader.GetInt32(3).ToString();
                        string Workload = reader.GetString(4);
                        string UserId = reader.GetString(5);
                        string ClientIP = reader.GetString(6);

                        string adxData = $"{CreationTime},{type},{Operation},{RecordType},{Workload},{UserId},{ClientIP}";

                        log.LogInformation(adxData);

                        if (!eventBatch.TryAdd(new EventData(Encoding.UTF8.GetBytes(adxData))))
                        {
                            // if it is too large for the batch
                            throw new Exception($"Event {adxData} is too large for the batch and cannot be sent.");
                        }

                        numOfEvents++;
                    }
                }
            }

            try
            {
                // Use the producer client to send the batch of events to the event hub
                await producerClient.SendAsync(eventBatch);
                log.LogInformation($"A batch of {numOfEvents} events has been published.");
            }
            finally
            {
                await producerClient.DisposeAsync();
            }


            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }
    }
}
