using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rolex
{
    internal static class Extensions
    {
        internal static string GetName(this CloudBlobDirectory directory) => directory.Prefix.TrimEnd('\\', '/');

        private static async Task<List<IListBlobItem>> ListBlobResultCoreAsync(Func<BlobContinuationToken, Task<BlobResultSegment>> func)
        {
            BlobContinuationToken continuationToken = null;
            var list = new List<IListBlobItem>();
            do
            {
                var result = await func(continuationToken).ConfigureAwait(false);
                list.AddRange(result.Results);
                continuationToken = result.ContinuationToken;
            } while (continuationToken is object);

            return list;
        }

        internal static Task<List<IListBlobItem>> ListBlobsAsync(this CloudBlobContainer container, CancellationToken cancellationToken = default) =>
            ListBlobResultCoreAsync(token => container.ListBlobsSegmentedAsync(token, cancellationToken));

        internal static Task<List<IListBlobItem>> ListBlobsAsync(this CloudBlobDirectory directory, CancellationToken cancellationToken = default) =>
            ListBlobResultCoreAsync(token => directory.ListBlobsSegmentedAsync(token, cancellationToken));

        internal static async Task DownloadAsync(this CloudBlobContainer container, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var item in await container.ListBlobsAsync(cancellationToken).ConfigureAwait(false))
            {
                await item.DownloadAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task DownloadAsync(this CloudBlobDirectory cloudDirectory, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var item in await cloudDirectory.ListBlobsAsync(cancellationToken).ConfigureAwait(false))
            {
                await item.DownloadAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task DownloadAsync(this IListBlobItem item, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            switch (item)
            {
                case CloudBlockBlob blockBlob:
                    {
                        var content = await blockBlob.DownloadTextAsync(cancellationToken).ConfigureAwait(false);
                        var name = Path.GetFileName(blockBlob.Name);
                        var filePath = Path.Combine(destinationDirectory, name);
                        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
                    }
                    break;
                case CloudBlobDirectory directory:
                    {
                        var directoryPath = Path.Combine(destinationDirectory, directory.GetName());
                        await directory.DownloadAsync(directoryPath, cancellationToken).ConfigureAwait(false);
                    }
                    break;
                default:
                    throw new Exception($"Did not recognize blob type {item.GetType()}");
            }
        }

        internal static TimeSpan Sum<T>(this IEnumerable<T> e, Func<T, TimeSpan> func) =>
            e.Select(func).Sum();

        internal static TimeSpan Sum(this IEnumerable<TimeSpan> e)
        {
            var sum = TimeSpan.Zero;
            foreach (var timespan in e)
            {
                sum += timespan;
            }

            return sum;
        }
    }
}
