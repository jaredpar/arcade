using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        internal async Task<HelixRun> QueueAllRunAsync(string roslynRoot, string configuration)
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
            var helixJobs = list.Select(x => x.Result).ToList();
            return new HelixRun(HelixApi, _queueId, helixJobs);

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
                    unitTestFileName == "Microsoft.CodeAnalysis.VisualBasic.EditorFeatures.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.CSharp.Symbol.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.VisualBasic.Emit.UnitTests.dll" ||
                    unitTestFileName == "Microsoft.CodeAnalysis.VisualBasic.Semantic.UnitTests.dll")
                {
                    return true;
                }

                return false;
            }
        }

        internal async Task<HelixRun> QueueAssemblyRunAsync(string unitTestFilePath, bool partition)
        {
            var task = partition
                ? QueuePartitionedAsync(unitTestFilePath)
                : QueueStandardAsync(unitTestFilePath);
            var helixJob = await task.ConfigureAwait(false);
            return new HelixRun(HelixApi, _queueId, new[] { helixJob });
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

            // TODO: the correlation directory is hackily created in the same folder to ensure hard linking
            // will work. May be a bad idea.
            var correlationDirectory = Path.Combine(
                Path.GetDirectoryName(unitTestFilePaths.First()),
                Path.GetRandomFileName());
            var zipDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(zipDirectory);
            var workItemNames = new List<string>();
            try
            {
                var correlationUtil = new CorrelationUtil();
                correlationUtil.ProcessAll(unitTestFilePaths.Select(Path.GetDirectoryName).ToArray());
                correlationUtil.CreateCorrelationPayload(correlationDirectory);

                job = job.WithCorrelationPayloadDirectory(correlationDirectory);

                foreach (var unitTestFilePath in unitTestFilePaths)
                {
                    await QueueOneAsync(correlationUtil, unitTestFilePath).ConfigureAwait(false);
                }

                return await SendAsync(HelixApi, job, "Multiple", isPartitioned: false, workItemNames).ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(correlationDirectory, recursive: true);
                Directory.Delete(zipDirectory, recursive: true);
            }

            static string EnsureBatchFile(string unitTestFilePath, string copyCorrelationContent)
            {
                var unitTestFileName = Path.GetFileName(unitTestFilePath);
                var uploadEnvironmentName = "%HELIX_WORKITEM_UPLOAD_ROOT%";
                var xmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.xml";
                var htmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.html";

                var batchFileName = $"xunit.cmd";
                var batchFilePath = Path.Combine(Path.GetDirectoryName(unitTestFilePath), batchFileName);
                var batchContent = copyCorrelationContent;
                batchContent += Environment.NewLine;
                batchContent += @$".\xunit.console.exe {unitTestFileName} -html {htmlFilePath} -xml {xmlFilePath}";

                WriteFileContentIfDifferent(batchFilePath, batchContent);
                return batchFilePath;
            }

            async Task QueueOneAsync(CorrelationUtil correlationUtil, string unitTestFilePath)
            {
                var displayName = Path.GetFileNameWithoutExtension(Path.GetFileName(unitTestFilePath));
                var unitTestDirectory = Path.GetDirectoryName(unitTestFilePath);
                PrepXunit(unitTestDirectory);

                _logger($"Preparing {displayName}");
                var filePaths = new List<string>();
                var copyContent = "";
                var comparer = RolexUtil.FileSystemComparer;
                foreach (var filePath in Directory.EnumerateFiles(unitTestDirectory, "*", SearchOption.AllDirectories))
                {
                    // Limiting the correlation caching to DLLs in the root folder. Caching ones from sub
                    // folders is possible but we'd have to add logic to create the containing directories
                    // when copying them back out again. Not hard just work.
                    var fileDirectory = Path.GetDirectoryName(filePath);
                    if (comparer.Equals(fileDirectory, unitTestDirectory) &&
                        correlationUtil.TryGetCorrelationFileName(filePath, out var correlationFileName))
                    {
                        var fileName = Path.GetFileName(filePath);
                        copyContent += @$"copy %HELIX_CORRELATION_PAYLOAD%\{correlationFileName} {fileName}" + Environment.NewLine;
                    }
                    else
                    {
                        filePaths.Add(filePath);
                    }
                }

                var batchFilePath = EnsureBatchFile(unitTestFilePath, copyContent);
                filePaths.Add(batchFilePath);

                var zipFilePath = Path.Combine(zipDirectory, $"{Guid.NewGuid()}.zip");
                await ZipDirectoryFilesAsync(zipFilePath, unitTestDirectory, filePaths).ConfigureAwait(false);

                workItemNames.Add(displayName);

                job = job
                    .DefineWorkItem(displayName)
                    .WithCommand(@$"cmd /c {Path.GetFileName(batchFilePath)}")
                    .WithArchivePayload(zipFilePath)
                    .WithTimeout(TimeSpan.FromMinutes(15))
                    .AttachToJob();
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

            return await SendAsync(HelixApi, job, unitTestFileName, isPartitioned: true, workItemNames).ConfigureAwait(false);

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

        private async Task<HelixJob> SendAsync(IHelixApi helixApi, IJobDefinition job, string displayName, bool isPartitioned, List<string> workItemNames)
        {
            var sentJob = await job.SendAsync(_logger).ConfigureAwait(false);
            return new HelixJob(
                displayName,
                sentJob.CorrelationId,
                new Uri(sentJob.ResultsContainerUri + sentJob.ResultsContainerReadSAS),
                isPartitioned,
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

        internal static async Task ZipDirectoryFilesAsync(string destFilePath, string directory, IEnumerable<string> filePaths)
        {
            using var destStream = new FileStream(destFilePath, FileMode.Create, FileAccess.ReadWrite);
            using var archive = new ZipArchive(destStream, ZipArchiveMode.Create);

            if (directory.EndsWith(Path.DirectorySeparatorChar))
            {
                directory = directory.Substring(0, directory.Length - 1);
            }

            foreach (var filePath in filePaths)
            {
                Debug.Assert(filePath.StartsWith(directory, RolexUtil.FileSystemComparison));
                var entryName = filePath.Substring(directory.Length + 1).Replace('\\', '/');

                using var entryStream = archive.CreateEntry(entryName).Open();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
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
