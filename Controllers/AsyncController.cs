using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ReportGenerationAsyncPoC
{
    public class AsyncController
    {
        private ILogger<AsyncController> logger;
        private IAsyncReportGenerator reportGenerator;

        public AsyncController(ILogger<AsyncController> logger)
        {
            this.logger = logger;
            this.reportGenerator = new AsyncReportGenerator(logger);
        }



        [FunctionName("GetAsyncStatus")]
        public async Task<IActionResult> GetAsyncStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "async/{token}")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient client,
            string token)
        {
            logger.LogInformation("GetAsyncStatus function processed a request.");

            string[] tokenParts = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(token).Split("|");
            string instanceId = tokenParts[0];
            string reportGenerationOperationId = tokenParts[1];
            int tryCount = int.Parse(tokenParts[2]);

            DurableOrchestrationStatus status = await client.GetStatusAsync(instanceId);

            switch (status.RuntimeStatus)
            {
                case OrchestrationRuntimeStatus.Pending:
                case OrchestrationRuntimeStatus.Running:
                case OrchestrationRuntimeStatus.ContinuedAsNew:
                    // every 6th try (over a minute if client adheres to retry-after of 10s)
                    if (tryCount % 6 == 0)
                    {
                        await CheckReportGenerationStatus(client, instanceId, reportGenerationOperationId);
                    }

                    // not complete yet, increment tryCount and respond with 302 with new location URI
                    tryCount++;
                    req.HttpContext.Response.Headers.Add("Retry-After", "10");
                    token = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode($"{instanceId}|{reportGenerationOperationId}|{tryCount}");
                    var response = new AcceptedResult()
                    {
                        Location = $"{req.HttpContext.Request.Scheme}://{req.Host.ToUriComponent()}/api/async/{token}"
                    };
                    return response;

                case OrchestrationRuntimeStatus.Completed:
                    string resourceUri = status.Output.ToString();
                    if (!String.IsNullOrEmpty(resourceUri))
                        return new RedirectResult(resourceUri);
                    // if orchestration returned empty string, orchestration timed out
                    return new StatusCodeResult(StatusCodes.Status404NotFound);
                #region error cases
                case OrchestrationRuntimeStatus.Failed:
                    logger.LogError($"Orchestration id {instanceId} status = failed", instanceId);
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);

                case OrchestrationRuntimeStatus.Canceled:
                    logger.LogWarning($"Orchestration id {instanceId} status = cancelled", instanceId);
                    return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);

                case OrchestrationRuntimeStatus.Terminated:
                    logger.LogWarning($"Orchestration id {instanceId} status = terminated", instanceId);
                    return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);

                default:
                    logger.LogWarning($"Orchestration id {instanceId} status is unknown", instanceId);
                    return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                    #endregion
            }
        }

        /// <summary>
        /// Handles Event Grid trigger for file added to report blob storage
        /// </summary>
        [FunctionName("RunOnReportFileCreatedEvent")]
        public async Task RunOnReportFileCreatedEvent(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [DurableClient] IDurableOrchestrationClient client)
        {
            logger.LogInformation("RunOnReportFileCreated event:\n" + JsonConvert.SerializeObject(eventGridEvent));

            string instanceId = eventGridEvent.Subject.Split('/').Last().Split('_').First();

            logger.LogInformation("RunOnReportFileCreated instanceId: " + instanceId);
            
            await CheckReportGenerationStatus(client, instanceId);
        }


        private async Task CheckReportGenerationStatus(IDurableOrchestrationClient client, string instanceId)
        {
            // get report generation operation id from orchestration input
            var dfStatus = await client.GetStatusAsync(instanceId);
            if (dfStatus == null)
            {
                logger.LogError("No orchestration found for instanceId " + instanceId);
                return;
            }
            logger.LogDebug("dfStatus:\n" + JsonConvert.SerializeObject(dfStatus));
            string reportGenerationOperationId = dfStatus.Input.ToString();

            await CheckReportGenerationStatus(client, instanceId, reportGenerationOperationId);
        }

        private async Task CheckReportGenerationStatus(IDurableOrchestrationClient client, string instanceId, string reportGenerationOperationId)
        {
            ReportGenerationStatus reportGenerationStatus = await reportGenerator.CheckOperationComplete(reportGenerationOperationId);

            switch (reportGenerationStatus)
            {
                case ReportGenerationStatus.Succeeded:
                    // raise event to orchestration to check report generation status
                    await client.RaiseEventAsync(instanceId, "ReportGenerationEnded", new ReportGenerationEndedEvent()
                    {
                        OutputFileUri = await reportGenerator.GetOutputFileName(reportGenerationOperationId),
                        Success = true
                    });
                    break;
                case ReportGenerationStatus.Failed:
                    // raise event to orchestration to check report generation status
                    await client.RaiseEventAsync(instanceId, "ReportGenerationEnded", new ReportGenerationEndedEvent()
                    {
                        Success = false
                    });
                    break;
            }
        }
    }
}
