using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class RolexAnalyzer
    {
        internal RolexStorage RolexStorage { get; }

        internal RolexAnalyzer(RolexStorage rolexStorage)
        {
            RolexStorage = rolexStorage;
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
                var testResultDirectory = TestResultUtil.GetTestResultDirectory(rolexRunInfo, helixJob);
                if (helixJob.IsPartitioned)
                {
                    var partitions = helixJob.WorkItemNames.Count;
                    var summaryList = await XUnitUtil.ListSummariesAsync(testResultDirectory).ConfigureAwait(false);
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
        }

        internal async Task AnalyzePartitionsAsync(RolexRunInfo rolexRunInfo, bool verbose)
        {
            if (!rolexRunInfo.HasTestResults)
            {
                throw new Exception("Cannot analyze unless test results are available");
            }

            var helixRun = await RolexStorage.GetHelixRunAsync(rolexRunInfo).ConfigureAwait(false);
            var list = new List<TypeSummary>();

            foreach (var helixJob in helixRun.HelixJobs)
            {
                if (helixJob.IsPartitioned)
                {
                    var testResultDirectory = TestResultUtil.GetTestResultDirectory(rolexRunInfo, helixJob);
                    var summaryList = await XUnitUtil.ListTypeSummariesAsync(testResultDirectory).ConfigureAwait(false);
                    list.AddRange(summaryList);
                }
            }

            Console.WriteLine($"{"TypeName",-50} {"ExecutionTime",10} {"Methods",8}");
            Console.WriteLine(new string('=', 100));
            foreach (var typeSummary in list.OrderByDescending(x => x.ExecutionTime.TotalMilliseconds).Take(20))
            {
                var name = verbose ? typeSummary.FullTypeName : typeSummary.TypeName;
                Console.WriteLine($"{name,-50} {Format(typeSummary.ExecutionTime),10} {typeSummary.Methods,8}");
            }
        }

        private static string Format(TimeSpan? ts) => ts?.ToString(@"h\:mm\:ss") ?? "        ";
    }
}
