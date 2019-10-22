using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Helix.Client.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;

namespace Rolex
{
    internal static class RolexUtil
    {
        internal static readonly StringComparer FileSystemComparer = StringComparer.OrdinalIgnoreCase;
        internal static readonly StringComparison FileSystemComparison = StringComparison.OrdinalIgnoreCase;

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

        internal static Guid? ReadMvid(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var reader = new PEReader(stream);
                var metadataReader = reader.GetMetadataReader();
                var mvidHandle = metadataReader.GetModuleDefinition().Mvid;
                var mvid = metadataReader.GetGuid(mvidHandle);
                return mvid;
            }
            catch
            {
                return null;
            }
        }

        internal static async Task WriteWithSpinner(string message, Task until)
        {
            var visible = Console.CursorVisible;
            var consoleLeft = Console.CursorLeft;
            var consoleTop = Console.CursorTop;
            var spinner = 0;

            Console.CursorVisible = false;
            try
            {
                do
                {
                    var delay = Task.Delay(TimeSpan.FromSeconds(.5));
                    var completed = await Task.WhenAny(delay, until).ConfigureAwait(false);
                    if (completed == until)
                    {
                        break;
                    }

                    Console.CursorLeft = consoleLeft;
                    Console.CursorTop = consoleTop;
                    Console.WriteLine($"{message} {GetSpinner()}");
                    UpdateSpinner();

                } while (true);
            }
            finally
            {
                Console.CursorVisible = visible;
            }

            void UpdateSpinner()
            {
                spinner++;
                if (spinner == 8)
                {
                    spinner = 0;
                }
            }

            char GetSpinner() => spinner switch
            {
                0 => '|',
                1 => '/',
                2 => '-',
                3 => '\\',
                4 => '|',
                5 => '/',
                6 => '-',
                7 => '\\',
                _ => ' '
            };
        }
    }
}
