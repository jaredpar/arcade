using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Rolex
{
    internal readonly struct XUnitAssemblySummary
    {
        internal string AssemblyPath { get; }
        internal int TestsPassed { get; }
        internal int TestsSkipped { get; }
        internal int TestsFailed { get; }
        internal TimeSpan ExecutionTime { get; }
        internal int Errors { get; }

        internal int TestsTotal => TestsPassed + TestsSkipped + TestsFailed;

        internal XUnitAssemblySummary(
            string assemblyPath,
            int testsPassed,
            int testsSkipped,
            int testsFailed,
            TimeSpan executionTime,
            int errors)
        {
            AssemblyPath = assemblyPath;
            TestsPassed = testsPassed;
            TestsSkipped = testsSkipped;
            TestsFailed = testsFailed;
            ExecutionTime = executionTime;
            Errors = errors;
        }

        public override string ToString() => Path.GetFileName(AssemblyPath);
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

    }
}
