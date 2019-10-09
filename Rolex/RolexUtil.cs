using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Helix.Client.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Rolex
{
    internal static class RolexUtil
    {
        internal static async Task<QueueInfo> FindQueueInfoAsync(IHelixApi helixApi, string defaultQueueId = "Windows.10.Amd64.Open")
        {
            var queueInfoList = await helixApi.Information.QueueInfoListAsync().ConfigureAwait(false);
            var windowsList = queueInfoList.Where(x => IsValid(x));

            var serverQueue = windowsList
                .OrderByDescending(x => x.ScaleMax.Value)
                .FirstOrDefault();

            if (serverQueue is object)
            {
                return serverQueue;
            }

            return await helixApi.Information.QueueInfoAsync(defaultQueueId).ConfigureAwait(false);

            static bool IsValid(QueueInfo info)
            {
                return
                    info.OperatingSystemGroup == "windows" &&
                    info.Architecture == "AMD64" &&
                    info.Purpose == "Test" &&
                    info.QueueDepth.HasValue &&
                    info.ScaleMax.HasValue &&
                    info.QueueDepth.Value < (info.ScaleMax.Value * 3) &&
                    !(info.IsInternalOnly ?? true);
            }
        }

        internal static async Task<(bool Completed, Task<T> CompletedTask)> WhenAny<T>(IEnumerable<Task<T>> tasks, TimeSpan timeout)
        {
            var delayTask = Task.Delay(timeout);
            var task = await Task.WhenAny(tasks.Append(delayTask)).ConfigureAwait(false);
            if (task == delayTask)
            {
                return (false, null);
            }
            else
            {
                return (true, (Task<T>)task);
            }
        }
    }
}