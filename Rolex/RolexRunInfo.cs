using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rolex
{
    internal sealed class RolexRunInfo
    {
        internal string Id { get; }
        internal string DataDirectory { get; }
        internal string TestResultDirectory { get; }
        internal bool HasTestResults { get; }

        internal RolexRunInfo(
            string id,
            string dataDirectory,
            bool hasTestResults)
        {
            Id = id;
            DataDirectory = dataDirectory;
            TestResultDirectory = Path.Combine(DataDirectory, "TestResults");
            HasTestResults = hasTestResults;
        }

        internal RolexRunInfo WithTestResults() => new RolexRunInfo(
            Id,
            DataDirectory,
            hasTestResults: true);
    }
}
