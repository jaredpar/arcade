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
        internal CloudBlobContainer Container { get; }

        internal TestResultUtil(ISentJob job)
        {
            var uriStr = job.ResultsContainerUri + job.ResultsContainerReadSAS;
            var uri = new Uri(uriStr);
            Container = new CloudBlobContainer(uri);
        }

        internal TestResultUtil(CloudBlobContainer container)
        {
            Container = container;
        }

        internal async Task DownloadAsync(string directory)
        {
            await Container.DownloadAsync(directory).ConfigureAwait(false);
        }
    }
}
