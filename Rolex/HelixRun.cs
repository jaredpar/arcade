using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Rolex
{
    internal sealed class HelixRun
    {
        internal IHelixApi HelixApi { get; }
        internal string QueueId { get; }
        internal List<HelixJob> HelixJobs { get; }

        internal HelixRun(IHelixApi helixApi, string queueId, IEnumerable<HelixJob> helixJobs)
        {
            HelixApi = helixApi;
            QueueId = queueId;
            HelixJobs = helixJobs.ToList();
        }
    }
}
