using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

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

        internal async Task Display(HelixRun helixRun)
        {
            if (Directory.Exists(TestResultsDirectory))
            {
                Directory.Delete(TestResultsDirectory, recursive: true);
            }
            Directory.CreateDirectory(TestResultsDirectory);

            var start = DateTime.UtcNow;
            var helixJobs = helixRun.HelixJobs;
            var list = helixJobs.Select(x => CompleteJobAsync(helixRun.HelixApi, x)).ToList();
            await PrintStatusAsync(helixRun, Task.WhenAll(list));

            var displayInfoList = new List<DisplayInfo>();
            foreach (var task in list)
            {
                var displayInfo = await task.ConfigureAwait(false);
                PrintSummaries(displayInfo);
                displayInfoList.Add(displayInfo);
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

            DisplayFailedHtmlPages(all);

            static void DisplayFailedHtmlPages(IEnumerable<XUnitAssemblySummary> summaryList)
            {
                foreach (var summary in summaryList.Where(x => x.TestsFailed > 0))
                {
                    var htmlFilePath = Path.ChangeExtension(summary.ResultsFilePath, ".html");
                    var info = new ProcessStartInfo()
                    {
                        FileName = htmlFilePath,
                        UseShellExecute = true,
                    };
                    Process.Start(info);
                }
            }
        }

        private async Task<DisplayInfo> CompleteJobAsync(IHelixApi helixApi, HelixJob helixJob)
        {
            await helixApi.Job.WaitForJobAsync(helixJob.CorrelationId, pollingIntervalMs: 5000).ConfigureAwait(false);

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

        private async Task PrintStatusAsync(HelixRun helixRun, Task untilTask)
        {
            const int width = 8;
            var consoleLeft = Console.CursorLeft;
            var consoleTop = Console.CursorTop;
            Console.CursorVisible = false;
            do
            {
                try
                {
                    await PrintCore().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error printing status");
                    Console.WriteLine(ex.Message);
                }

                var delayTask = Task.Delay(TimeSpan.FromSeconds(1));
                var completedTask = await Task.WhenAny(untilTask, delayTask).ConfigureAwait(false);
                if (completedTask == untilTask)
                {
                    break;
                }

            } while (true);
            Console.CursorVisible = true;

            async Task PrintCore()
            {
                Console.CursorLeft = consoleLeft;
                Console.CursorTop = consoleTop;
                Console.WriteLine($"{DateTime.UtcNow}");

                var helixApi = helixRun.HelixApi;
                var queueInfo = await helixApi.Information.QueueInfoAsync(helixRun.QueueId).ConfigureAwait(false);
                Console.WriteLine($"Queue {helixRun.QueueId} current depth {queueInfo.QueueDepth}");
                Console.WriteLine("");
                Console.WriteLine($"{"Job Name",-70} {"Unsched",width} {"Waiting",width} {"Running",width} {"Finished",width}");
                Console.WriteLine(new string('=', 120));
                var helixJobs = helixRun.HelixJobs;
                foreach (var helixJob in helixJobs)
                {
                        var details = await helixApi.Job.DetailsAsync(helixJob.CorrelationId).ConfigureAwait(false);
                        var wi = details.WorkItems;
                        Console.WriteLine($"{helixJob.DisplayName,-70} {wi.Unscheduled,width} {wi.Waiting,width} {wi.Running,width} {wi.Finished,width}");
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
