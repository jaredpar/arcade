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
    internal sealed class RolexRunDisplay
    {
        private struct DisplayInfo
        {
            internal HelixJob HelixJob;
            internal string TestResultDirectory;
            internal List<XUnitAssemblySummary> SummaryList;
        }

        private readonly Action<string> _logger;
        internal RolexStorage RolexStorage { get; }

        internal RolexRunDisplay(RolexStorage rolexStorage, Action<string> logger = null)
        {
            RolexStorage = rolexStorage;
            _logger = logger ?? delegate { };
        }

        internal async Task DisplayAsync(RolexRunInfo rolexRunInfo)
        {
            var helixRun = await RolexStorage.GetHelixRunAsync(rolexRunInfo).ConfigureAwait(false);
            if (!rolexRunInfo.HasTestResults)
            {
                var completedTask = CompleteAndDownloadAsync(rolexRunInfo);
                await PrintStatusAsync(helixRun, completedTask).ConfigureAwait(false);
            }

            var displayInfoList = await LoadDisplayInfoAsync(rolexRunInfo, helixRun);
            var all = displayInfoList.SelectMany(x => x.SummaryList);
            Console.WriteLine(new string('=', 80));
            Console.WriteLine($"Tests passed: {all.Sum(x => x.TestsPassed):n0}");
            Console.WriteLine($"Tests skipped: {all.Sum(x => x.TestsSkipped):n0}");
            Console.WriteLine($"Tests failed: {all.Sum(x => x.TestsFailed):n0}");
            Console.WriteLine($"Total tests: {all.Sum(x => x.TestsTotal):n0}");
            Console.WriteLine($"Total test execution time: {all.Sum(x => x.ExecutionTime)}");

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

        internal async Task AnalyzeAsync(RolexRunInfo rolexRunInfo)
        {
            if (!rolexRunInfo.HasTestResults)
            {
                throw new Exception("Cannot analyze unless test results are available");
            }

            var helixRun = await RolexStorage.GetHelixRunAsync(rolexRunInfo).ConfigureAwait(false);
            const int width = 8;

            Console.WriteLine($"{"Assembly",-70} {"Partitions",10} {"Max Time",width} {"Min Time",width} {"Total Time",width}");
            Console.WriteLine(new string('=', 120));
            foreach (var helixJob in helixRun.HelixJobs)
            {
                var testResultDirectory = GetTestResultDirectory(rolexRunInfo, helixJob);
                if (helixJob.IsPartitioned)
                {
                    // TDOO: do this
                }
                else
                {
                    // TODO: The fact a WorkItem name / test dir name is an assembly name in the non-partitioned case is 
                    // convention. should consider sub classing or another technique to make this more first class
                    foreach (var directory in Directory.EnumerateDirectories(testResultDirectory))
                    {
                        var name = Path.GetFileName(directory);
                        var xmlFilePath = Directory.EnumerateFiles(directory, "*.xml").Single();
                        var xunitResults = (await XUnitUtil.ReadSummariesAsync(xmlFilePath).ConfigureAwait(false)).Single();

                        Console.WriteLine($"{name,-70} {"N/A",width} {"N/A",width} {"N/A",width} {xunitResults.ExecutionTime,width}");
                    }
                }

            }
        }

        private async Task CompleteAndDownloadAsync(RolexRunInfo rolexRunInfo)
        {
            if (Directory.Exists(rolexRunInfo.TestResultDirectory))
            {
                Directory.Delete(rolexRunInfo.TestResultDirectory, recursive: true);
            }
            Directory.CreateDirectory(rolexRunInfo.TestResultDirectory);

            var helixRun = await RolexStorage.GetHelixRunAsync(rolexRunInfo).ConfigureAwait(false);
            var helixJobs = helixRun.HelixJobs;

            var list = helixJobs.Select(x => CompleteAndDownloadAsync(rolexRunInfo, helixRun.HelixApi, x)).ToList();
            await Task.WhenAll(list).ConfigureAwait(false);

            await RolexStorage.SaveRolexRunInfoAsync(rolexRunInfo.WithTestResults()).ConfigureAwait(false);
                Console.WriteLine(new string('=', 120));

            static async Task CompleteAndDownloadAsync(RolexRunInfo rolexRunInfo, IHelixApi helixApi, HelixJob helixJob)
            { 
                await helixApi.Job.WaitForJobAsync(helixJob.CorrelationId, pollingIntervalMs: 5000).ConfigureAwait(false);

                var downloadStart = DateTime.UtcNow;
                var util = new TestResultUtil(helixJob.ContainerUri);
                var testResultDirectory = GetTestResultDirectory(rolexRunInfo, helixJob);
                await util.DownloadAsync(testResultDirectory).ConfigureAwait(false);
            }
        }

        private async Task<List<DisplayInfo>> LoadDisplayInfoAsync(RolexRunInfo rolexRunInfo, HelixRun helixRun)
        {
            var list = new List<DisplayInfo>();
            foreach (var helixJob in helixRun.HelixJobs)
            {
                var testResultDirectory = GetTestResultDirectory(rolexRunInfo, helixJob);
                var summaries = await GetSummariesAsync(testResultDirectory).ConfigureAwait(false);
                list.Add(new DisplayInfo()
                {
                    HelixJob = helixJob,
                    TestResultDirectory = testResultDirectory,
                    SummaryList = summaries
                });
            }

            return list;
        }

        private static async Task<List<XUnitAssemblySummary>> GetSummariesAsync(string testResultDirectory)
        {
            var list = new List<XUnitAssemblySummary>();
            foreach (var xmlFilePath in Directory.EnumerateFiles(testResultDirectory, "*.xml", SearchOption.AllDirectories))
            {
                var fileList = await XUnitUtil.ReadSummariesAsync(xmlFilePath).ConfigureAwait(false);
                list.AddRange(fileList);
            }

            return list;
        }

        private static string GetTestResultDirectory(RolexRunInfo rolexRunInfo, HelixJob helixJob) =>
            Path.Combine(rolexRunInfo.TestResultDirectory, helixJob.DisplayName);

        /// <summary>
        /// Print the status of the actively executing HelixRun until the given task completes
        /// </summary>
        private async Task PrintStatusAsync(HelixRun helixRun, Task untilTask)
        {
            const int width = 8;
            var consoleLeft = Console.CursorLeft;
            var consoleTop = Console.CursorTop;
            Console.CursorVisible = false;
            do
            {
                var delayTask = Task.Delay(TimeSpan.FromSeconds(1));
                try
                {
                    await PrintCore().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error printing status");
                    Console.WriteLine(ex.Message);
                }

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
    }
}
