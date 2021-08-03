using System;
using System.Collections.Generic;
using System.Text;

namespace ReportGenerationAsyncPoC
{
    public class ReportGenerationFailedException : SystemException
    {
        public ReportGenerationFailedException(string errorMessage) : base(errorMessage) { }
    }
}
