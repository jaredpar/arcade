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

        internal TestResultUtil(Uri uri)
        {
            Container = new CloudBlobContainer(uri);
        }

        internal TestResultUtil(CloudBlobContainer container)
        {
            Container = container;
        }

        internal async Task DownloadAsync(string directory)
        {
            bool Predicate(IListBlobItem item) => item switch
            {
                CloudBlobDirectory _ => true,
                CloudBlockBlob blob => blob.Name.EndsWith("xml") || blob.Name.EndsWith("html") || blob.Name.EndsWith("log"),
                _ => false
            };
            await Container.DownloadAsync(
                directory,
                Predicate).ConfigureAwait(false);
        }
    }
}
