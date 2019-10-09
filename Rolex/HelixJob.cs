using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class HelixJob
    {
        internal string DisplayName { get; }
        internal string CorrelationId { get; }
        internal Uri ContainerUri { get; }
        internal List<string> WorkItemNames { get; }

        internal HelixJob(
            string displayName,
            string correlationId,
            Uri containerUri,
            List<string> workItemNames)
        {
            DisplayName = displayName;
            CorrelationId = correlationId;
            ContainerUri = containerUri;
            WorkItemNames = workItemNames;
        }

        public override string ToString() => DisplayName;
    }
}
