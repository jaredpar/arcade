using Microsoft.Azure.Storage.Shared.Protocol;
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
    internal sealed partial class RolexStorage
    {
        private const string HelixRunFileName = "helixrun.json";
        private const string RolexRunInfoFileName = "rolexruninfo.json";

        internal string RolexDataDirectory { get; }

        internal RolexStorage(string rolexDataDirectory = null)
        {
            RolexDataDirectory = rolexDataDirectory ?? GetDefaultRolexDataDirectory();
            Directory.CreateDirectory(RolexDataDirectory);
        }

        internal static string GetDefaultRolexDataDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rolex");

        /// <summary>
        /// Create a rolex run storage directory and return the id
        /// </summary>
        /// <returns></returns>
        internal async Task<RolexRunInfo> CreateRolexRunInfo()
        {
            var dateTime = DateTime.UtcNow;
            var name = dateTime.ToString("yyyy-MM-dd_HH-mm-ss");
            var id = dateTime.ToString("yyyy-MM-dd HH:mm:ss");

            var directory = Path.Combine(RolexDataDirectory, name);
            Directory.CreateDirectory(directory);
            var rolexRunInfo = new RolexRunInfo(id, directory, hasTestResults: false);
            await SaveRolexRunInfoAsync(rolexRunInfo).ConfigureAwait(false);
            return rolexRunInfo;
        }

        internal async Task SaveAsync(RolexRunInfo rolexRunInfo, HelixRun helixRun)
        {
            await SaveAsJsonAsync(Path.Combine(rolexRunInfo.DataDirectory, HelixRunFileName), StorageHelixRun.Create(helixRun)).ConfigureAwait(false);
        }

        internal async Task<RolexRunInfo> GetRolexRunInfo(string runId)
        {
            var list = await ListRolexRunInfosAsync().ConfigureAwait(false);
            var runInfo = list.FirstOrDefault(x => x.Id == runId);
            if (runInfo is null)
            {
                throw new Exception($"No run with id {runId}");
            }

            return runInfo;
        }

        internal async Task<List<RolexRunInfo>> ListRolexRunInfosAsync()
        {
            var list = new List<RolexRunInfo>();
            foreach (var directory in Directory.EnumerateDirectories(RolexDataDirectory))
            {
                var runInfo = await LoadRolexRunInfoAsync(directory).ConfigureAwait(false);
                list.Add(runInfo);
            }
            return list;
        }

        internal async Task<HelixRun> GetHelixRunAsync(RolexRunInfo rolexRunInfo)
        {
            var filePath = Path.Combine(rolexRunInfo.DataDirectory, HelixRunFileName);
            var storage = await LoadAsJsonAsync<StorageHelixRun>(filePath).ConfigureAwait(false);
            return storage.Convert(ApiFactory.GetAnonymous());
        }

        private static async Task<RolexRunInfo> LoadRolexRunInfoAsync(string dataDirectory)
        {
            var filePath = Path.Combine(dataDirectory, RolexRunInfoFileName);
            var storage = await LoadAsJsonAsync<StorageRolexRunInfo>(filePath);
            return storage.Convert(dataDirectory);
        }

        public Task SaveRolexRunInfoAsync(RolexRunInfo runInfo) =>
            SaveAsJsonAsync(Path.Combine(runInfo.DataDirectory, RolexRunInfoFileName), StorageRolexRunInfo.Create(runInfo));

        private static async Task SaveAsJsonAsync(string filePath, object data)
        {
            var contents = JsonConvert.SerializeObject(data);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
            using var streamWriter = new StreamWriter(fileStream);
            await streamWriter.WriteAsync(contents).ConfigureAwait(false);
        }

        private static async Task<T> LoadAsJsonAsync<T>(string filePath)
        {
            var contents = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<T>(contents);
        }
    }
}
