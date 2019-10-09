using Microsoft.DotNet.Helix.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rolex
{
    /// <summary>
    /// Manages the submitted jobs on a disk storage space. This lets us rehydrate jobs, list,
    /// wait, etc ...
    /// </summary>
    internal sealed class RolexStorage
    {
        public struct StorageHelixJob
        {
            public string DisplayName { get; set;  }
            public string CorrelationId { get; set;  }
            public Uri ContainerUri { get; set;  }
            public List<string> WorkItemNames { get; set;  }
        }

        private const string HelixJobFileName = "helixjobs.json";

        internal string RolexDataDirectory { get; }

        internal RolexStorage(string rolexDataDirectory = null)
        {
            RolexDataDirectory = rolexDataDirectory ?? GetDefaultRolexDataDirectory();
            Directory.CreateDirectory(RolexDataDirectory);
        }

        internal static string GetDefaultRolexDataDirectory() =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rolex");

        internal async Task<string> SaveAsync(IEnumerable<HelixJob> helixJobs)
        {
            var name = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var directory = Path.Combine(RolexDataDirectory, name);
            Directory.CreateDirectory(directory);

            var helixJobFilePath = Path.Combine(directory, HelixJobFileName);
            var storageHelixJobs = helixJobs.Select(Convert).ToList();
            var contents = JsonConvert.SerializeObject(storageHelixJobs);
            using var fileStream = new FileStream(helixJobFilePath, FileMode.Create, FileAccess.ReadWrite);
            using var streamWriter = new StreamWriter(fileStream);
            await streamWriter.WriteAsync(contents).ConfigureAwait(false);
            return name;

            static StorageHelixJob Convert(HelixJob helixJob) => new StorageHelixJob()
            {
                DisplayName = helixJob.DisplayName,
                CorrelationId = helixJob.CorrelationId,
                ContainerUri = helixJob.ContainerUri,
                WorkItemNames = helixJob.WorkItemNames.ToList(),
            };
        }

        internal List<string> ListNames() => Directory
            .EnumerateDirectories(RolexDataDirectory)
            .Select(Path.GetFileName)
            .OrderBy(x => x)
            .ToList();

        internal async Task<List<HelixJob>> LoadAsync(string name)
        {
            var directory = Path.Combine(RolexDataDirectory, name);
            var helixJobFilePath = Path.Combine(directory, HelixJobFileName);
            var contents = await File.ReadAllTextAsync(helixJobFilePath).ConfigureAwait(false);
            var storageHelixJobs = JsonConvert.DeserializeObject<StorageHelixJob[]>(contents);
            return storageHelixJobs.Select(Convert).ToList();

            static HelixJob Convert(StorageHelixJob helixJob) => new HelixJob(
                ApiFactory.GetAnonymous(),
                helixJob.DisplayName,
                helixJob.CorrelationId,
                helixJob.ContainerUri,
                helixJob.WorkItemNames);
        }
    }
}
