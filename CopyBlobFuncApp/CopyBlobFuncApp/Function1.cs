using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Azure.Identity;
using System.Text;
using Azure;
using System.Linq;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Blobs.Models;

namespace CopyBlobFuncApp
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string sourceAccountName = "sadevdmz001";
            string containerName = "test1";
            string blobName = $"mylogfile{DateTime.Now.ToString("HHmmss")}.log";

            await CreateBlockBlobAsync(sourceAccountName, containerName, blobName, log);

            log.LogInformation("Source blob created");

            string destAccountName = "sadevdest001";
            await CreateBlockBlobAsync(destAccountName, containerName, Guid.NewGuid() + "-" + blobName, log);

            log.LogInformation("Dest blob created");
            /////
            ///

            // Construct the blob container endpoint from the arguments.
            string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        sourceAccountName,
                                                        containerName);

            // Get a credential and create a service client object for the blob container.
            BlobContainerClient sourceContainerClient = new BlobContainerClient(new Uri(containerEndpoint),
                                                                            new DefaultAzureCredential());

            containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        destAccountName,
                                                        containerName);

            // Get a credential and create a service client object for the blob container.
            BlobContainerClient destContainerClient = new BlobContainerClient(new Uri(containerEndpoint),
                                                                            new DefaultAzureCredential());

            await CopyBlobAsync(sourceContainerClient, destContainerClient, log);

            return new OkObjectResult("Completed");
        }

        async static Task CreateBlockBlobAsync(string accountName, string containerName, string blobName, ILogger log)
        {
            // Construct the blob container endpoint from the arguments.
            string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        accountName,
                                                        containerName);

            // Get a credential and create a service client object for the blob container.
            BlobContainerClient containerClient = new BlobContainerClient(new Uri(containerEndpoint),
                                                                            new DefaultAzureCredential());

            try
            {
                // Create the container if it does not exist.
                await containerClient.CreateIfNotExistsAsync();

                // Upload text to a new block blob.
                string blobContents = "This is a block blob.";
                byte[] byteArray = Encoding.ASCII.GetBytes(blobContents);

                using (MemoryStream stream = new MemoryStream(byteArray))
                {
                    await containerClient.UploadBlobAsync(blobName, stream);
                }
            }
            catch (Exception e)
            {
                log.LogInformation(e.Message);
                throw;
            }
        }

        static async Task CopyBlobAsync(BlobContainerClient sourceContainer, BlobContainerClient destContainer, ILogger log)
        {
            try
            {
                // Get the name of the first blob in the container to use as the source.
                string blobName = sourceContainer.GetBlobs().FirstOrDefault().Name;

                // Create a BlobClient representing the source blob to copy.
                BlobClient sourceBlob = sourceContainer.GetBlobClient(blobName);

                // Ensure that the source blob exists.
                if (await sourceBlob.ExistsAsync())
                {
                    // Lease the source blob for the copy operation 
                    // to prevent another client from modifying it.
                    BlobLeaseClient lease = sourceBlob.GetBlobLeaseClient();

                    // Specifying -1 for the lease interval creates an infinite lease.
                    await lease.AcquireAsync(TimeSpan.FromSeconds(-1));

                    // Get the source blob's properties and display the lease state.
                    BlobProperties sourceProperties = await sourceBlob.GetPropertiesAsync();
                    log.LogInformation($"Lease state: {sourceProperties.LeaseState}");

                    //Get source blob as memory stream
                    var memorystream = new MemoryStream();
                    sourceBlob.DownloadTo(memorystream);
                    memorystream.Position = 0;

                    string destBlobName = Guid.NewGuid() + "-" + sourceBlob.Name;

                    await destContainer.UploadBlobAsync(destBlobName, memorystream);

                    // Get a BlobClient representing the destination blob with a unique name.
                    BlobClient destBlob =
                        destContainer.GetBlobClient(destBlobName);

                    // Start the copy operation.
                    //await destBlob.StartCopyFromUriAsync(sourceBlob.Uri);

                    

                    // Get the destination blob's properties and display the copy status.
                    BlobProperties destProperties = await destBlob.GetPropertiesAsync();

                    log.LogInformation($"Copy status: {destProperties.CopyStatus}");
                    log.LogInformation($"Copy progress: {destProperties.CopyProgress}");
                    log.LogInformation($"Completion time: {destProperties.CopyCompletedOn}");
                    log.LogInformation($"Total bytes: {destProperties.ContentLength}");

                    // Update the source blob's properties.
                    sourceProperties = await sourceBlob.GetPropertiesAsync();

                    if (sourceProperties.LeaseState == LeaseState.Leased)
                    {
                        // Break the lease on the source blob.
                        await lease.BreakAsync();

                        // Update the source blob's properties to check the lease state.
                        sourceProperties = await sourceBlob.GetPropertiesAsync();
                        log.LogInformation($"Lease state: {sourceProperties.LeaseState}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
