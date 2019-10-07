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
            internal List<XUnitAssemblySummary> SummaryList;
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

            var start = DateTime.UtcNow;
            var list = helixJobs.Select(x => CompleteJobAsync(x)).ToList();
            var displayInfoList = new List<DisplayInfo>();
            while (list.Count > 0)
            {
                var (completed, completedTask) = await RolexUtil.WhenAny(list, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (!completed)
                {
                    await PrintStatusAsync(helixJobs).ConfigureAwait(false);
                }
                else
                {
                    list.Remove(completedTask);
                    var displayInfo = await completedTask.ConfigureAwait(false);
                    PrintSummaries(displayInfo);
                    displayInfoList.Add(displayInfo);
                }
            }

            var total = TimeSpan.FromMilliseconds(displayInfoList.Sum(x => (long)x.DownloadTime.TotalMilliseconds));
            var all = displayInfoList.SelectMany(x => x.SummaryList);
            Console.WriteLine(new string('=', 80));
            Console.WriteLine($"Tests passed: {all.Sum(x => x.TestsPassed)}");
            Console.WriteLine($"Tests skipped: {all.Sum(x => x.TestsSkipped)}");
            Console.WriteLine($"Tests failed: {all.Sum(x => x.TestsFailed)}");
            Console.WriteLine($"Total tests: {all.Sum(x => x.TestsTotal)}");
            Console.WriteLine($"Total test execution time: {all.Sum(x => x.ExecutionTime)}");
            Console.WriteLine($"Local execution time: {DateTime.UtcNow - start}");
            Console.WriteLine($"Total download time: {total}");
        }

        private async Task<DisplayInfo> CompleteJobAsync(HelixJob helixJob)
        {
            await helixJob.HelixApi.Job.WaitForJobAsync(helixJob.CorrelationId, pollingIntervalMs: 5000).ConfigureAwait(false);

            var downloadStart = DateTime.UtcNow;
            var util = new TestResultUtil(helixJob.ContainerUri);
            var helixJobDirectory = Path.Combine(TestResultsDirectory, helixJob.DisplayName);
            await util.DownloadAsync(helixJobDirectory).ConfigureAwait(false);
            var summaryList = await GetSummaries(helixJobDirectory).ConfigureAwait(false);

            return new DisplayInfo()
            {
                HelixJob = helixJob,
                TestResultDirectory = helixJobDirectory,
                DownloadTime = DateTime.UtcNow - downloadStart,
                SummaryList = summaryList
            };

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

        internal async Task PrintStatusAsync(IEnumerable<HelixJob> helixJobs)
        {
            const int width = 8;
            Console.WriteLine($"{"Job Name",-70} {"Unsched",width} {"Waiting",width} {"Running",width} {"Finished",width}");
            Console.WriteLine(new string('=', 120));
            foreach (var helixJob in helixJobs)
            {
                try
                {
                    var details = await helixJob.HelixApi.Job.DetailsAsync(helixJob.CorrelationId).ConfigureAwait(false);
                    var wi = details.WorkItems;
                    Console.WriteLine($"{helixJob.DisplayName,-70} {wi.Unscheduled,width} {wi.Waiting,width} {wi.Running,width} {wi.Finished,width}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error printing job status");
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private void PrintSummaries(DisplayInfo info)
        {
            var job = info.HelixJob;
            var list = info.SummaryList;
            Console.WriteLine(job.CorrelationId);
            Console.WriteLine(job.ContainerUri);
            if (list.Count == 0)
            {
                Console.WriteLine("Empty test run");
                return;
            }

            Console.WriteLine($"Tests run: {list.Sum(x => x.TestsTotal)}");
            Console.WriteLine($"Tests passed: {list.Sum(x => x.TestsPassed)}");
            Console.WriteLine($"Tests skipped: {list.Sum(x => x.TestsSkipped)}");
            Console.WriteLine($"Tests failed: {list.Sum(x => x.TestsFailed)}");
            Console.WriteLine($"Max test run time: {list.Max(x => x.ExecutionTime)}");
            Console.WriteLine($"Min test run time: {list.Min(x => x.ExecutionTime)}");
            Console.WriteLine($"DownloadTime: {info.DownloadTime}");
        }
    }
}
