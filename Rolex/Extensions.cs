using Microsoft.Azure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue.Protocol;
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

        public static async Task DownloadAsync(
            this CloudBlobContainer container,
            string destinationDirectory,
            Func<IListBlobItem, bool> predicate = null,
            CancellationToken cancellationToken = default)
        {
            predicate ??= _ => true;
            var doneTasks = new List<Task>();
            var queue = new Queue<(IListBlobItem item, string itemDirectory)>();
            Directory.CreateDirectory(destinationDirectory);
            foreach (var item in await container.ListBlobsAsync(cancellationToken).ConfigureAwait(false))
            {
                queue.Enqueue((item, destinationDirectory));
            }

            while (queue.Count != 0)
            {
                var (item, itemDirectory) = queue.Dequeue();
                if (!predicate(item))
                {
                    continue;
                }

                switch (item)
                {
                    case CloudBlockBlob blockBlob:
                        {
                            doneTasks.Add(DownloadBlobAsync(blockBlob, itemDirectory, cancellationToken));
                            break;
                        }
                    case CloudBlobDirectory directory:
                        {
                            var directoryPath = Path.Combine(destinationDirectory, directory.GetName());
                            Directory.CreateDirectory(directoryPath);
                            foreach (var childItem in await directory.ListBlobsAsync(cancellationToken).ConfigureAwait(false))
                            {
                                queue.Enqueue((childItem, directoryPath));
                            }
                        }
                        break;
                    default:
                        throw new Exception($"Did not recognize blob type {item.GetType()}");
                    }
            }

            static async Task DownloadBlobAsync(CloudBlockBlob blockBlob, string targetDirectory, CancellationToken cancellationToken)
            {
                var content = await blockBlob.DownloadTextAsync(cancellationToken).ConfigureAwait(false);
                var name = Path.GetFileName(blockBlob.Name);
                var filePath = Path.Combine(targetDirectory, name);
                await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
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
