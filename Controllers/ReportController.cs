using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;


namespace ReportGenerationAsyncPoC
{
    public class ReportController
    {
        private ILogger<ReportController> logger;
        private IAsyncReportGenerator reportGenerator;

        public ReportController(
            ILogger<ReportController> logger)
        {
            this.logger = logger;
            this.reportGenerator = new AsyncReportGenerator(logger);
        }

        /// <summary>
        /// API HTTP endpoint to start orchestration to generate report
        /// </summary>
        /// <param name="req"></param>
        /// <param name="starter"></param>
        /// <returns></returns>
        [FunctionName("GenerateReport")]
        public async Task<IActionResult> GenerateReport(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            // we would assemble the unique id based on the user + report type
            string instanceId = Guid.NewGuid().ToString();
            logger.LogInformation($"instanceId = '{instanceId}'");

            // TODO: check if orchestration already in progress for uniqueRequestId, if so, don't start a new one

            // start kusto export report to blob storage file
            string reportGenerationOperationId = await reportGenerator.GenerateReportAsync($"{instanceId}");

            // start orchestration, pass in kusto operation id
            await starter.StartNewAsync("ReportGeneration_Orchestration", instanceId, reportGenerationOperationId);

            logger.LogInformation($"Started orchestration with ID = '{instanceId}'.");
                        
            // create URI for checking status
            string token = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode($"{instanceId}|{reportGenerationOperationId}|0");

            var response = new AcceptedResult()
            {
                Location = string.Concat(
                    req.RequestUri.GetLeftPart(UriPartial.Authority),
                    "/api/async/",
                    token
                )
            };
            req.Headers.Add("Retry-After", "10");
            return response;
        }

        
        [FunctionName("ReportGeneration_Orchestration")]
        public async Task<string> ReportGeneration_Orchestration(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            // in reality for our purposes we could let the orchestration run for a day or two before becoming concerned with killing it
            DateTime orchestrationDeadline = context.CurrentUtcDateTime.Add(TimeSpan.FromMinutes(5));

            using (var cancellationTokenSource = new System.Threading.CancellationTokenSource())
            {
                Task orchestrationTimeoutTask = context.CreateTimer(orchestrationDeadline, cancellationTokenSource.Token);

                Task<ReportGenerationEndedEvent> reportGenerationEndedEvent = context.WaitForExternalEvent<ReportGenerationEndedEvent>("ReportGenerationEnded");

                Task taskThatCompletedFirst = await Task.WhenAny(orchestrationTimeoutTask, reportGenerationEndedEvent);

                if (taskThatCompletedFirst == reportGenerationEndedEvent)
                {
                    // report generation ended event received
                    try
                    {
                        cancellationTokenSource.Cancel();   // cancel timer
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug("cancellationTokenSource.Cancel failed: " + ex.Message);
                    }
                    if (reportGenerationEndedEvent.Result.Success) {

                        // do any post-generation processing of report files in an activity here

                        return reportGenerationEndedEvent.Result.OutputFileUri;
                    }
                    else
                    {
                        logger.LogError("IAsyncReportGenerator operation failed");
                        throw new ReportGenerationFailedException("IAsyncReportGenerator operation failed");
                    }
                }
                else
                {
                    // timeout case
                    logger.LogWarning($"ReportGeneration_Orchestration timed out");
                    return null;        // return null string if report was not successfully generated before timeout
                }
            }
        }

        
    }

}