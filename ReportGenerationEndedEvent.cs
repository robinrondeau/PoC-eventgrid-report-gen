using System;
using System.Collections.Generic;
using System.Text;

namespace ReportGenerationAsyncPoC
{
    public class ReportGenerationEndedEvent
    {
        public bool Success { get; set; }
        public string OutputFileUri { get; set; }
    }
}
