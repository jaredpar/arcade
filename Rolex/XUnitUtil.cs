using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Rolex
{
    internal sealed class XUnitAssemblySummary
    {
        internal string ResultsFilePath { get; }
        internal string AssemblyPath { get; }
        internal int TestsPassed { get; }
        internal int TestsSkipped { get; }
        internal int TestsFailed { get; }
        internal TimeSpan ExecutionTime { get; }
        internal int Errors { get; }

        internal int TestsTotal => TestsPassed + TestsSkipped + TestsFailed;

        internal XUnitAssemblySummary(
            string resultsFilePath,
            string assemblyPath,
            int testsPassed,
            int testsSkipped,
            int testsFailed,
            TimeSpan executionTime,
            int errors)
        {
            AssemblyPath = assemblyPath;
            ResultsFilePath = resultsFilePath;
            TestsPassed = testsPassed;
            TestsSkipped = testsSkipped;
            TestsFailed = testsFailed;
            ExecutionTime = executionTime;
            Errors = errors;
        }

        public override string ToString() => Path.GetFileName(AssemblyPath);
    }

    internal sealed class TypeSummary
    {
        internal string ResultsFilePath { get; set; }
        internal string FullTypeName { get; set; }
        internal TimeSpan ExecutionTime { get; set; }
        internal int Methods { get; set; } 

        internal string TypeName => FullTypeName?.Split('.').Last();
    }

    internal sealed class XUnitUtil
    {
        internal static async Task<List<XUnitAssemblySummary>> ReadSummariesAsync(string xmlFilePath, CancellationToken cancellationToken = default)
        {
            using var textReader = new StreamReader(xmlFilePath);
            var document = await XDocument.LoadAsync(textReader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            var list = new List<XUnitAssemblySummary>();
            foreach (var element in document.Root.Elements("assembly"))
            {
                if (element.Attribute("passed") is object)
                {
                    var summary = new XUnitAssemblySummary(
                        xmlFilePath,
                        element.Attribute("name").Value,
                        int.Parse(element.Attribute("passed").Value),
                        int.Parse(element.Attribute("skipped").Value),
                        int.Parse(element.Attribute("failed").Value),
                        TimeSpan.FromSeconds(double.Parse(element.Attribute("time").Value)),
                        int.Parse(element.Attribute("errors").Value));
                    list.Add(summary);
                }
            }

            return list;
        }

        internal static async Task<List<TypeSummary>> ReadTypeSummariesAsync(string xmlFilePath, CancellationToken cancellationToken = default)
        {
            using var textReader = new StreamReader(xmlFilePath);
            var document = await XDocument.LoadAsync(textReader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            var map = new Dictionary<string, TypeSummary>();
            foreach (var element in document.Root.Descendants("test"))
            {
                var type = element.Attribute("type").Value;
                if (!map.TryGetValue(type, out var typeSummary))
                {
                    typeSummary = new TypeSummary()
                    {
                        ResultsFilePath = xmlFilePath,
                        FullTypeName = type,
                    };
                    map[type] = typeSummary;
                }

                var executionTime = TimeSpan.FromSeconds(double.Parse(element.Attribute("time").Value));
                typeSummary.Methods++;
                typeSummary.ExecutionTime += executionTime;
            }

            return map.Values.OrderBy(x => x.FullTypeName).ToList();
        }

        internal static async Task<List<XUnitAssemblySummary>> ListSummariesAsync(string testResultDirectory)
        {
            var list = new List<XUnitAssemblySummary>();
            foreach (var xmlFilePath in Directory.EnumerateFiles(testResultDirectory, "*.xml", SearchOption.AllDirectories))
            {
                var fileList = await XUnitUtil.ReadSummariesAsync(xmlFilePath).ConfigureAwait(false);
                list.AddRange(fileList);
            }

            return list;
        }

        internal static async Task<List<TypeSummary>> ListTypeSummariesAsync(string testResultDirectory)
        {
            var list = new List<TypeSummary>();
            foreach (var xmlFilePath in Directory.EnumerateFiles(testResultDirectory, "*.xml", SearchOption.AllDirectories))
            {
                var fileList = await XUnitUtil.ReadTypeSummariesAsync(xmlFilePath).ConfigureAwait(false);
                list.AddRange(fileList);
            }

            return list;
        }
    }
}
