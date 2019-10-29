using Microsoft.Azure.Storage.Blob;
using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class TestResultUtil
    {
        internal RolexRunInfo RolexRunInfo { get; }
        internal HelixJob HelixJob { get; }
        internal CloudBlobContainer Container { get; }

        internal TestResultUtil(RolexRunInfo rolexRunInfo, HelixJob helixJob)
        {
            RolexRunInfo = rolexRunInfo;
            HelixJob = helixJob;
            Container = new CloudBlobContainer(helixJob.ContainerUri);
        }

        internal async Task DownloadAsync()
        {
            bool Predicate(IListBlobItem item) => item switch
            {
                CloudBlobDirectory _ => true,
                CloudBlockBlob blob => blob.Name.EndsWith("xml") || blob.Name.EndsWith("html") || blob.Name.EndsWith("log"),
                _ => false
            };

            var directory = GetTestResultDirectory(RolexRunInfo, HelixJob);
            await Container.DownloadAsync(
                directory,
                Predicate).ConfigureAwait(false);
        }

        internal static string GetTestResultDirectory(RolexRunInfo rolexRunInfo, HelixJob helixJob) =>
            Path.Combine(rolexRunInfo.TestResultDirectory, helixJob.DisplayName);

    }
}
