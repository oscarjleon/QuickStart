﻿using Newtonsoft.Json.Linq;
using PowerApps.Samples;
using PowerApps.Samples.Messages;
using System.Threading.Tasks.Dataflow;

namespace TPLDataFlowParallelOperations
{
    // This sample demonstrates the use of Task Parallel Library (TPL) Dataflow components
    // See https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/dataflow-task-parallel-library

    internal class Program
    {
        // Controls the max degree of parallelism
        // Set this to match the x-ms-dop-hint response header returned from the environment.
        static readonly int maxDegreeOfParallelism = 4;
        // How many records to create and delete with this sample.
        static readonly int numberOfRecords = 100;

        static async Task Main()
        {
            Config config = App.InitializeApp();

            var service = new Service(config);

            Console.WriteLine("--Starting TPL DataFlow Parallel Operations sample--");

            #region Optimize Connection

            // Change max connections from .NET to a remote service default: 2
            System.Net.ServicePointManager.DefaultConnectionLimit = 65000;
            // Bump up the min threads reserved for this app to ramp connections faster - minWorkerThreads defaults to 4, minIOCP defaults to 4 
            ThreadPool.SetMinThreads(100, 100);
            // Turn off the Expect 100 to continue message - 'true' will cause the caller to wait until it round-trip confirms a connection to the server 
            System.Net.ServicePointManager.Expect100Continue = false;
            // Can decrease overall transmission overhead but can cause delay in data packet arrival
            System.Net.ServicePointManager.UseNagleAlgorithm = false;

            #endregion Optimize Connection

            var executionDataflowBlockOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };

            var count = 0;
            double secondsToComplete;

            //Will be populated with requests to create accounts
            List<CreateRequest> accountCreateRequests = new();

            Console.WriteLine($"Preparing to create and delete {numberOfRecords} account records using Web API.");

            while (count < numberOfRecords)
            {
                var account = new JObject
                {
                    ["name"] = $"Account {count}"
                };
                accountCreateRequests.Add(new CreateRequest("accounts", account));
                count++;
            }

            secondsToComplete = await ProcessData(service, accountCreateRequests, executionDataflowBlockOptions);

            Console.WriteLine($"Created and deleted {accountCreateRequests.Count} accounts in  {Math.Round(secondsToComplete)} seconds.");

            Console.WriteLine("--TPL DataFlow Parallel Operations sample completed--");
        }

        /// <summary>
        /// Creates and deletes a set of account records
        /// </summary>
        /// <param name="service">The service</param>
        /// <param name="accountCreateRequests">The list of create requests to execute in parallel.</param>
        /// <param name="executionDataflowBlockOptions">Specifies the behavior of the Dataflow Block Options</param>
        /// <returns></returns>
        static async Task<double> ProcessData(Service service,
            List<CreateRequest> accountCreateRequests,
            ExecutionDataflowBlockOptions executionDataflowBlockOptions)
        {
            // Create a TransformBlock of CreateRequests 
            var createAccounts = new TransformBlock<CreateRequest, CreateResponse>(
                async createRequest =>
                {

                    return await service.SendAsync<CreateResponse>(createRequest);

                },
                    executionDataflowBlockOptions
                );

            // Create an ActionBlock to process CreateResponse
            var deleteAccounts = new ActionBlock<CreateResponse>(
                async createResponse =>
                {
                    await service.SendAsync(new DeleteRequest(createResponse.EntityReference));
                },
                executionDataflowBlockOptions
              );

            // Link the ActionBlock to the TranformBlock when the operation completes.
            createAccounts.LinkTo(deleteAccounts, new DataflowLinkOptions { PropagateCompletion = true });

            var start = DateTime.Now;

            // Start sending the requests
            accountCreateRequests.ForEach(a => createAccounts.SendAsync(a));
            createAccounts.Complete();
            // Wait for the Action Block to complete
            await deleteAccounts.Completion;

            //Calculate the duration to complete
            return (DateTime.Now - start).TotalSeconds;
        }
    }
}