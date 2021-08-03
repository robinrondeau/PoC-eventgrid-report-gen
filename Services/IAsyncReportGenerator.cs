using System.Threading.Tasks;
using System;

namespace ReportGenerationAsyncPoC
{
    public interface IAsyncReportGenerator
    {
        Task<ReportGenerationStatus> CheckOperationComplete(string operationId);
        Task<string> GenerateReportAsync(string instanceId);
        Task<string> GetOutputFileName(string operationId);
    }
}