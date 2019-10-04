using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class HelixJobDisplay
    {
        internal struct DisplayInfo
        {
            internal HelixJob HelixJob;
            internal string TestResultDirectory;
            internal TimeSpan DownloadTime;
        }

        /// <summary>
        /// The directory where all the test result information will be downloaded to.
        /// </summary>
        internal string TestResultsDirectory { get; }

        internal HelixJobDisplay(string testResultsDirectory)
        {
            TestResultsDirectory = testResultsDirectory;
        }

        internal async Task Display(IEnumerable<HelixJob> helixJobs)
        {
            if (Directory.Exists(TestResultsDirectory))
            {
                Directory.Delete(TestResultsDirectory, recursive: true);
            }
            Directory.CreateDirectory(TestResultsDirectory);

            var list = helixJobs.Select(x => CompleteJobAsync(x)).ToList();
            while (list.Count > 0)
            {
                var completedTask = await Task.WhenAny(list).ConfigureAwait(false);
                list.Remove(completedTask);
                var displayInfo = await completedTask.ConfigureAwait(false);
                await PrintSummaries(displayInfo).ConfigureAwait(false);
            }
        }

        private async Task<DisplayInfo> CompleteJobAsync(HelixJob helixJob)
        {
            await helixJob.HelixApi.Job.WaitForJobAsync(helixJob.CorrelationId, pollingIntervalMs: 5000).ConfigureAwait(false);

            var downloadStart = DateTime.UtcNow;
            var util = new TestResultUtil(helixJob.ContainerUri);
            var helixJobDirectory = Path.Combine(TestResultsDirectory, helixJob.DisplayName);
            await util.DownloadAsync(helixJobDirectory).ConfigureAwait(false);
            return new DisplayInfo()
            {
                HelixJob = helixJob,
                TestResultDirectory = helixJobDirectory,
                DownloadTime = DateTime.UtcNow - downloadStart
            };
        }

        private async Task PrintSummaries(DisplayInfo info)
        {
            var job = info.HelixJob;
            var list = await GetSummaries(info.TestResultDirectory).ConfigureAwait(false);
            Console.WriteLine(job.CorrelationId);
            Console.WriteLine(job.ContainerUri);
            Console.WriteLine($"Tests run: {list.Sum(x => x.TestsTotal)}");
            Console.WriteLine($"Tests passed: {list.Sum(x => x.TestsPassed)}");
            Console.WriteLine($"Tests skipped: {list.Sum(x => x.TestsSkipped)}");
            Console.WriteLine($"Tests failed: {list.Sum(x => x.TestsFailed)}");
            Console.WriteLine($"Max test run time: {list.Max(x => x.ExecutionTime)}");
            Console.WriteLine($"Min test run time: {list.Min(x => x.ExecutionTime)}");
            Console.WriteLine($"DownloadTime: {info.DownloadTime}");

            static async Task<List<XUnitAssemblySummary>> GetSummaries(string testResultDirectory)
            {
                var list = new List<XUnitAssemblySummary>();
                foreach (var xmlFilePath in Directory.EnumerateFiles(testResultDirectory, "*.xml", SearchOption.AllDirectories))
                {
                    var fileList = await XUnitUtil.ReadSummariesAsync(xmlFilePath).ConfigureAwait(false);
                    list.AddRange(fileList);
                }

                return list;
            }
        }
    }
}
