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
    /// <summary>
    /// Type which efficiently queues up unit test DLLs for execution in Helix
    /// </summary>
    internal sealed class RoslynHelixUtil
    {
        internal static readonly string TestResourceDllName = "Microsoft.CodeAnalysis.Test.Resources.Proprietary.dll";
        private static readonly string SourceName = "RoslynUnitTests";
        private static readonly string TypeName = "test/unit";
        private static readonly string CreatorName = Environment.GetEnvironmentVariable("USERNAME");

        internal IHelixApi HelixApi { get; }
        private readonly string _queueId;
        private readonly Action<string> _logger;

        internal RoslynHelixUtil(IHelixApi helixApi, string queueId, Action<string> logger = null)
        {
            HelixApi = helixApi;
            _queueId = queueId;
            _logger = logger;
        }

        internal async Task<List<HelixJob>> QueueAllAsync(string roslynRoot, string configuration)
        {
            var list = new List<Task<HelixJob>>();
            var unitTestFilePaths = GetUnitTestFilePaths();
            var flatList = new List<string>();

            foreach (var unitTestFilePath in unitTestFilePaths)
            {
                if (UsePartitions(unitTestFilePath))
                {
                    list.Add(QueuePartitionedAsync(unitTestFilePath));
                }
                else
                {
                    flatList.Add(unitTestFilePath);
                }
            }

            if (flatList.Count > 0)
            {
                list.Add(QueueStandardAsync(flatList.ToArray()));
            }

            await Task.WhenAll(list).ConfigureAwait(false);
            return list.Select(x => x.Result).ToList();

            List<string> GetUnitTestFilePaths()
            {
                var binDir = Path.Combine(roslynRoot, "artifacts", "bin");
                var list = new List<string>();
                foreach (var directory in Directory.EnumerateDirectories(binDir, "*UnitTests"))
                {
                    var desktopDirectory = Path.Combine(directory, configuration, "net472");
                    if (!Directory.Exists(desktopDirectory))
                    {
                        continue;
                    }

                    var unitTestFilePath = Directory.EnumerateFiles(desktopDirectory, "*.UnitTests.dll").FirstOrDefault();
                    if (unitTestFilePath is object)
                    {
                        list.Add(unitTestFilePath);
                    }
                }

                return list;
            }

            static bool UsePartitions(string unitTestFilePath)
            {
                var unitTestFileName = Path.GetFileName(unitTestFilePath);
                if (unitTestFileName == "Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.EditorFeatures.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.EditorFeatures2.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.VisualStudio.LanguageServices.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.CSharp.EditorFeatures.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.VisualBasic.EditorFeatures.UnitTests.dll")
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// This will queue up a series of unit test DLLs as single job. Each work item will represent a 
        /// different unit test DLL. The job will then use correlation payloads to make the execution 
        /// speedier.
        /// </summary>
        internal async Task<HelixJob> QueueStandardAsync(params string[] unitTestFilePaths)
        {
            if (unitTestFilePaths.Length == 0)
            {
                throw new ArgumentException(nameof(unitTestFilePaths));
            }

            var job = HelixApi
                .Job
                .Define()
                .WithType(TypeName)
                .WithTargetQueue(_queueId)
                .WithSource(SourceName)
                .WithCreator(CreatorName);
            var hasAdddeCorrelation = false;

            // TODO: use the correlation payload resource DLL
            var workItemNames = new List<string>();
            var zipDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(zipDirectory);
            try
            {
                foreach (var unitTestFilePath in unitTestFilePaths)
                {
                    var unitTestDirectory = Path.GetDirectoryName(unitTestFilePath);
                    PrepXunit(unitTestDirectory);
                    var displayName = Path.GetFileNameWithoutExtension(Path.GetFileName(unitTestFilePath));
                    var usesTestResources = UsesTestResources(unitTestDirectory);
                    var batchFileName = EnsureBatchFile(unitTestFilePath, usesTestResources);
                    workItemNames.Add(displayName);

                    if (usesTestResources)
                    {
                        // TODO: https://github.com/dotnet/arcade/issues/4045
                        // Until that is fixed I need to zip the files locally so that they unzip properly when 
                        // run on the target machine.
                        /*
                        var comparer = StringComparer.OrdinalIgnoreCase;
                        var filePaths = Directory
                            .GetFiles(unitTestDirectory, "*", SearchOption.AllDirectories)
                            .Where(x => !comparer.Equals(TestResourceDllName, Path.GetFileName(x)))
                            .ToArray();
                            */
                        var unitTestFileName = Path.GetFileName(unitTestFilePath);
                        var zipFilePath = Path.Combine(zipDirectory, Path.ChangeExtension(unitTestFileName, ".zip"));
                        ZipDirectory(zipFilePath, unitTestDirectory, TestResourceDllName);
                        job = job
                            .DefineWorkItem(displayName)
                            .WithCommand(@$"cmd /c {batchFileName}")
                            .WithArchivePayload(zipFilePath)
                            .WithTimeout(TimeSpan.FromMinutes(15))
                            .AttachToJob();
                    }
                    else
                    {
                        job = job
                            .DefineWorkItem(displayName)
                            .WithCommand(@$"cmd /c {batchFileName}")
                            .WithDirectoryPayload(unitTestDirectory)
                            .WithTimeout(TimeSpan.FromMinutes(15))
                            .AttachToJob();
                    }
                }

                return await SendAsync(HelixApi, job, "Multiple", workItemNames).ConfigureAwait(false);
            }
            finally
            {
                // TODO: clean this up when the helix bug is fixed https://github.com/dotnet/arcade/issues/4045
                Directory.Delete(zipDirectory, recursive: true);
            }

            bool UsesTestResources(string unitTestDirectory)
            {
                var filePath = Directory
                    .EnumerateFiles(unitTestDirectory, TestResourceDllName, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (filePath is object)
                {
                    if (!hasAdddeCorrelation)
                    {
                        job = job.WithCorrelationPayloadFiles(filePath);
                        hasAdddeCorrelation = true;
                    }

                    return true;
                }

                return false;
            }

            static string EnsureBatchFile(string unitTestFilePath, bool usesUnitTestResources)
            {
                var unitTestFileName = Path.GetFileName(unitTestFilePath);
                var uploadEnvironmentName = "%HELIX_WORKITEM_UPLOAD_ROOT%";
                var xmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.xml";
                var htmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.html";

                var batchFileName = $"xunit.cmd";
                var batchFilePath = Path.Combine(Path.GetDirectoryName(unitTestFilePath), batchFileName);
                var batchContent = @$".\xunit.console.exe {unitTestFileName} -html {htmlFilePath} -xml {xmlFilePath}";

                if (usesUnitTestResources)
                {
                    batchContent = @$"copy %HELIX_CORRELATION_PAYLOAD%\{TestResourceDllName} ." + Environment.NewLine + batchContent;
                }

                WriteFileContentIfDifferent(batchFilePath, batchContent);
                return batchFileName;
            }
        }

        /// <summary>
        /// This will queue up a single unit test into a singel job. Each work item represents a partition
        /// </summary>
        internal async Task<HelixJob> QueuePartitionedAsync(string unitTestFilePath)
        {
            var unitTestFileName = Path.GetFileName(unitTestFilePath);
            var unitTestDirectory = Path.GetDirectoryName(unitTestFilePath);

            var scheduler = new AssemblyScheduler(methodLimit: 50);
            var batchDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(batchDirectory);
            var batchFileNames = new List<string>();
            int partitionId = 0;
            foreach (var info in scheduler.Schedule(unitTestFilePath))
            {
                batchFileNames.Add(EnsureBatchFile(batchDirectory, info, GetPartitionId(partitionId)));
                partitionId++;
            }

            PrepXunit(unitTestDirectory);

            var job = HelixApi
                .Job
                .Define()
                .WithType(TypeName)
                .WithTargetQueue(_queueId)
                .WithSource(SourceName)
                .WithCreator(CreatorName)
                .WithCorrelationPayloadDirectory(unitTestDirectory);

            var workItemNames = new List<string>();
            for (int i = 0; i < batchFileNames.Count; i++)
            {
                var batchFileName = batchFileNames[i];
                var displayName = Path.GetFileNameWithoutExtension(unitTestFileName) + "." + GetPartitionId(i);
                job = job
                    .DefineWorkItem(displayName)
                    .WithCommand(@$"cmd /c {batchFileName}")
                    .WithDirectoryPayload(batchDirectory)
                    .WithTimeout(TimeSpan.FromMinutes(15))
                    .AttachToJob();
                workItemNames.Add(displayName);
            }

            return await SendAsync(HelixApi, job, unitTestFileName, workItemNames).ConfigureAwait(false);

            static string GetPartitionId(int id) => id.ToString("D3");

            static string EnsureBatchFile(string batchDirectory, AssemblyPartitionInfo info, string partitionId)
            {
                var unitTestFileName = Path.GetFileName(info.AssemblyPath);
                var uploadEnvironmentName = "%HELIX_WORKITEM_UPLOAD_ROOT%";
                var xmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.{partitionId}.xml";
                var htmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.{partitionId}.html";

                var batchFileName = $"xunit-{partitionId}.cmd";
                var batchFilePath = Path.Combine(batchDirectory, batchFileName);
                var batchContent = @$"
cd %HELIX_CORRELATION_PAYLOAD%
.\xunit.console.exe {unitTestFileName} -html {htmlFilePath} -xml {xmlFilePath} {info.ClassListArgumentString}";

                WriteFileContentIfDifferent(batchFilePath, batchContent);
                return batchFileName;
            }
        }

        internal Task<HelixJob> QueueAsync(string unitTestFilePath, bool partition) =>
            partition
            ? QueuePartitionedAsync(unitTestFilePath)
            : QueueStandardAsync(unitTestFilePath);

        private async Task<HelixJob> SendAsync(IHelixApi helixApi, IJobDefinition job, string displayName, List<string> workItemNames)
        {
            var sentJob = await job.SendAsync(_logger).ConfigureAwait(false);
            return new HelixJob(
                helixApi,
                displayName,
                sentJob.CorrelationId,
                new Uri(sentJob.ResultsContainerUri + sentJob.ResultsContainerReadSAS),
                workItemNames);
        }

        /// <summary>
        /// Write <paramref name="contents"/> to <paramref name="filePath"/> if they are different from the
        /// current contents of the file. Returns true if the file was written to disk.
        /// </summary>
        private static bool WriteFileContentIfDifferent(string filePath, string content)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var currentContent = File.ReadAllText(filePath);
                    if (currentContent == content)
                    {
                        // don't screw up payload caching by modifying the directory if the batch file has already
                        // been laid down
                        return false;
                    }
                }
            }
            catch
            {
                // Error reading the file, no problem, just rewrite it with current content
            }

            File.WriteAllText(filePath, content);
            return true;
        }

        internal static void ZipDirectory(string destFilePath, string directory, string excludeFileName)
        {
            using var fileStream = new FileStream(destFilePath, FileMode.Create, FileAccess.ReadWrite);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            if (directory.EndsWith(Path.DirectorySeparatorChar))
            {
                directory = directory.Substring(0, directory.Length - 1);
            }

            foreach (var filePath in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(filePath))
                {
                    var entryName = filePath.Substring(directory.Length + 1).Replace('\\', '/');
                    _ = archive.CreateEntryFromFile(filePath, entryName);
                }
            }
        }

        /// <summary>
        /// TODO: this entire method is a hack of sorts. It's copying xunit into the unit test directory which
        /// is a destructive operation. Need to put it up as a correlation payload somehow.
        /// </summary>
        /// <param name="unitTestDirectory"></param>
        private void PrepXunit(string unitTestDirectory)
        {
            // TODO: this is a hack. Need to find the nuget path better.
            var xunitToolsDirectory = @"P:\nuget\xunit.runner.console\2.4.1\tools\net472";
            foreach (var sourceFilePath in Directory.EnumerateFiles(xunitToolsDirectory))
            {
                var destFileName = Path.GetFileName(sourceFilePath);
                var destFilePath = Path.Combine(unitTestDirectory, destFileName);
                if (!File.Exists(destFilePath))
                {
                    File.Copy(sourceFilePath, destFilePath, overwrite: true);
                }
            }
        }
    }
}
