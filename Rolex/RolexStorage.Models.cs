using Microsoft.Azure.Storage.Shared.Protocol;
using Microsoft.DotNet.Helix.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed partial class RolexStorage
    {
        internal struct StorageRolexRunInfo
        {
            public string Id { get; set; }
            public bool HasTestResults { get; set; }

            public static StorageRolexRunInfo Create(RolexRunInfo runInfo) =>
                new StorageRolexRunInfo()
                {
                    Id = runInfo.Id,
                    HasTestResults = runInfo.HasTestResults
                };

            internal RolexRunInfo Convert(string dataDirectory) =>
                new RolexRunInfo(Id, dataDirectory, HasTestResults);
        }

        public struct StorageHelixRun
        {
            public string QueueId { get; set; }
            public List<StorageHelixJob> HelixJobs { get; set; }

            public static StorageHelixRun Create(HelixRun run) =>
                new StorageHelixRun()
                {
                    QueueId = run.QueueId,
                    HelixJobs = run.HelixJobs.Select(StorageHelixJob.Create).ToList()
                };

            public HelixRun Convert(IHelixApi helixApi) =>
                new HelixRun(helixApi, QueueId, HelixJobs.Select(x => x.Convert()).ToList());
        }

        public struct StorageHelixJob
        {
            public string DisplayName { get; set; }
            public string CorrelationId { get; set; }
            public Uri ContainerUri { get; set; }
            public bool IsPartitioned { get; set; }
            public List<string> WorkItemNames { get; set;  }

            public static StorageHelixJob Create(HelixJob job) =>
                new StorageHelixJob()
                {
                    DisplayName = job.DisplayName,
                    CorrelationId = job.CorrelationId,
                    ContainerUri = job.ContainerUri,
                    IsPartitioned = job.IsPartitioned,
                    WorkItemNames = job.WorkItemNames.ToList()
                };

            public HelixJob Convert() =>
                new HelixJob(DisplayName, CorrelationId, ContainerUri, IsPartitioned, WorkItemNames.ToList());
        }
    }
}
