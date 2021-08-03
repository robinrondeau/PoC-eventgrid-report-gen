using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Kusto.Data.Common;

namespace ReportGenerationAsyncPoC
{
    public enum ReportGenerationStatus
    {
        Running,
        Succeeded,
        Failed
    }


    public class AsyncReportGenerator : IAsyncReportGenerator
    {
        private readonly ICslAdminProvider kustoClient;
        private ILogger logger;

        public AsyncReportGenerator(
            ILogger logger)
        {
            this.logger = logger;
            try
            {
                kustoClient = KustoTableClientFactory.GetClient();
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to create Kusto query client:\n " + ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Starts async report generation which writes report file(s) to blob storage
        /// </summary>
        /// <returns>report generation operation id string</returns>
        public async Task<string> GenerateReportAsync(string filePrefix)
        {
            logger.LogInformation($"GenerateReport started.");

            // call kusto to start async report export, return Kusto operation id

            // includeHeader = first file to handle multiple files
            // add BOM after manually

            // test generating more than 1 file

            string operationId = "";
            string query = ".export async to csv ("
                + "h@\"https://storagegeneratedreports.blob.core.windows.net/report-files;<secret>\""
                + ") with("
                + $"includeHeaders = \"all\", distributed = false, namePrefix = \"{filePrefix}\", encoding = \"UTF8BOM\")"
                + "<| GetStormData(1)";

            try
            {
                using (var dataReader = await kustoClient.ExecuteControlCommandAsync(KustoTableClientFactory.K_DATABASE, query, new ClientRequestProperties()))
                {
                    if (dataReader.Read())
                    {
                        operationId = dataReader.GetGuid(0).ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GenerateReport failed: \n" + ex.Message);
                throw;
            }

            logger.LogInformation($"GenerateReport complete, operationId: {operationId}.");

            return operationId;
        }


        public async Task<string> GetOutputFileName(string operationId)
        {
            string query = $".show operation {operationId} details";

            // multiple rows for multiple files (100 MB each)
            using (var dataReader = await kustoClient.ExecuteControlCommandAsync(KustoTableClientFactory.K_DATABASE, query, new ClientRequestProperties()))
            {
                if (dataReader.Read())
                {
                    return dataReader.GetString(0);
                }
            }

            throw new ReportGenerationFailedException("ReportGeneration operation did not return expected file path");
        }

        public async Task<ReportGenerationStatus> CheckOperationComplete(string operationId)
        {
            string query = $".show operations | where  OperationId == \"{operationId}\" | where State in (\"Completed\",\"Failed\") | sort by LastUpdatedOn desc | project State, Status";

            using (var dataReader = await kustoClient.ExecuteControlCommandAsync(KustoTableClientFactory.K_DATABASE, query, new ClientRequestProperties()))
            {
                if (dataReader.Read())
                {
                    string status = dataReader.GetString(0);
                    switch (status)
                    {
                        case "Completed":
                            return ReportGenerationStatus.Succeeded;
                        case "Failed":
                            // to do: log error
                            //errorMsg = dataReader.GetString(1);
                            return ReportGenerationStatus.Failed;
                    }
                }
            }

            return ReportGenerationStatus.Running;
        }
    }
}
