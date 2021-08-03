using System;
using System.Collections.Generic;
using Azure.Identity;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace ReportGenerationAsyncPoC
{
    public static class KustoTableClientFactory
    {
        private const string K_CLUSTER = "https://pocreportgenasync.westus2.kusto.windows.net";
        public const string K_DATABASE = "storm events";
                
        public static ICslAdminProvider GetClient()
        {
            KustoConnectionStringBuilder kcsb = new KustoConnectionStringBuilder(K_CLUSTER)
                .WithAadApplicationKeyAuthentication("<app client id>", "<app key>", "<authority id>");


            return KustoClientFactory.CreateCslAdminProvider(kcsb);
        }
    }
}
