using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel;

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
                var (completedTask, downloadedTask) = await CompleteAndDownloadAsync(rolexRunInfo).ConfigureAwait(false);
                await PrintStatusAsync(helixRun, completedTask).ConfigureAwait(false);
                await RolexUtil.WriteWithSpinner("Downloading results", downloadedTask);
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
            var list = new List<(string Name, int? Partions, TimeSpan? MinTime, TimeSpan? MaxTime, TimeSpan TotalTime)>();

            foreach (var helixJob in helixRun.HelixJobs)
            {
                var testResultDirectory = GetTestResultDirectory(rolexRunInfo, helixJob);
                if (helixJob.IsPartitioned)
                {
                    var partitions = helixJob.WorkItemNames.Count;
                    var summaryList = await GetSummariesAsync(testResultDirectory).ConfigureAwait(false);
                    var name = helixJob.DisplayName;
                    var min = summaryList.Min(x => x.ExecutionTime);
                    var max = summaryList.Max(x => x.ExecutionTime);
                    var sum = summaryList.Sum(x => x.ExecutionTime);
                    list.Add((name, partitions, min, max, sum));
                }
                else
                {
                    // TODO: The fact a WorkItem name / test dir name is an assembly name in the non-partitioned case is 
                    // convention. should consider sub classing or another technique to make this more first class
                    foreach (var directory in Directory.EnumerateDirectories(testResultDirectory))
                    {
                        var name = Path.GetFileName(directory);
                        var xmlFilePath = Directory.EnumerateFiles(directory, "*.xml").Single();
                        var xunitResults = (await XUnitUtil.ReadSummariesAsync(xmlFilePath).ConfigureAwait(false)).SingleOrDefault();
                        if (xunitResults is object)
                        {
                            list.Add((name, null, null, null, xunitResults.ExecutionTime));
                        }
                    }
                }
            }

            const int width = 8;
            Console.WriteLine($"{"Assembly",-70} {"Partitions",10} {"Min Time",width} {"Max Time",width} {"Total Time",width}");
            Console.WriteLine(new string('=', 120));
            foreach (var tuple in list)
            {
                var max = Format(tuple.MaxTime);
                var min = Format(tuple.MinTime);
                var total = Format(tuple.TotalTime);
                Console.WriteLine($"{tuple.Name,-70} {tuple.Partions,width} {min} {max} {total}");
            }

            static string Format(TimeSpan? ts) => ts?.ToString(@"h\:mm\:ss") ?? "        ";
        }

        private async Task<(Task Completed, Task Downloaded)> CompleteAndDownloadAsync(RolexRunInfo rolexRunInfo)
        {
            if (Directory.Exists(rolexRunInfo.TestResultDirectory))
            {
                Directory.Delete(rolexRunInfo.TestResultDirectory, recursive: true);
            }
            Directory.CreateDirectory(rolexRunInfo.TestResultDirectory);

            var helixRun = await RolexStorage.GetHelixRunAsync(rolexRunInfo).ConfigureAwait(false);
            var helixJobs = helixRun.HelixJobs;
            var helixApi = helixRun.HelixApi;
            var completedList = new List<Task>();
            var downloadedList = new List<Task>();
            foreach (var helixJob in helixJobs)
            {
                var completedTask = helixApi.Job.WaitForJobAsync(helixJob.CorrelationId, pollingIntervalMs: 5000);
                var downloadedTask = DownloadAsync(rolexRunInfo, helixApi, helixJob, completedTask, _logger);
                completedList.Add(completedTask);
                downloadedList.Add(downloadedTask);
            }

            return (Task.WhenAll(completedList), AllDownloadAsync());

            async Task AllDownloadAsync()
            {
                await Task.WhenAll(downloadedList).ConfigureAwait(false);
                await RolexStorage.SaveRolexRunInfoAsync(rolexRunInfo.WithTestResults()).ConfigureAwait(false);
            }

            static async Task DownloadAsync(RolexRunInfo rolexRunInfo, IHelixApi helixApi, HelixJob helixJob, Task completedTask, Action<string> logger)
            {
                await completedTask.ConfigureAwait(false);
                var downloadStart = DateTime.UtcNow;
                var util = new TestResultUtil(helixJob.ContainerUri);
                var testResultDirectory = GetTestResultDirectory(rolexRunInfo, helixJob);
                await util.DownloadAsync(testResultDirectory).ConfigureAwait(false);
                logger($"Downloaded {helixJob.DisplayName} in {DateTime.UtcNow - downloadStart}");
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

                var bufferList = new List<string>();
                await BuildBufferList().ConfigureAwait(false);

                var blankLine = new string(' ', Console.WindowWidth);
                foreach (var line in bufferList)
                {
                    WriteLine(line);
                }

                void WriteLine(string message)
                {
                    var left = Console.CursorLeft;
                    var top = Console.CursorTop;
                    var width = Console.WindowWidth;
                    Console.WriteLine(new string(' ', width));
                    Console.CursorLeft = left;
                    Console.CursorTop = top;
                    Console.WriteLine(message);
                }

                async Task BuildBufferList()
                {
                    var helixApi = helixRun.HelixApi;
                    var queueInfo = await helixApi.Information.QueueInfoAsync(helixRun.QueueId).ConfigureAwait(false);
                    var banner = new string('=', 120);

                    bufferList.Add($"{"Job Name",-70} {"Unsched",width} {"Waiting",width} {"Running",width} {"Finished",width}");
                    bufferList.Add(banner);

                    int unscheduled = 0;
                    int waiting = 0;
                    int running = 0;
                    int finished = 0;
                    var helixJobs = helixRun.HelixJobs;
                    foreach (var helixJob in helixJobs)
                    {
                        var details = await helixApi.Job.DetailsAsync(helixJob.CorrelationId).ConfigureAwait(false);
                        var wi = details.WorkItems;
                        bufferList.Add($"{helixJob.DisplayName,-70} {wi.Unscheduled,width} {wi.Waiting,width} {wi.Running,width} {wi.Finished,width}");
                        unscheduled += wi.Unscheduled;
                        waiting += wi.Waiting;
                        running += wi.Running;
                        finished += wi.Finished;
                    }
                    bufferList.Add(banner);
                    bufferList.Add($"{"Total",-70} {unscheduled,width} {waiting,width} {running,width} {finished,width}");
                    bufferList.Add("");
                    bufferList.Add($"Queue {helixRun.QueueId} current depth {queueInfo.QueueDepth}");
                    bufferList.Add($"{DateTime.UtcNow.ToLocalTime()}");
                }
            }
        }
    }
}
