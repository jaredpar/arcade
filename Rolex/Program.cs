using Microsoft.DotNet.Helix.Client;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Metadata;
using RunTests;
using Microsoft.WindowsAzure.Storage.File.Protocol;
using Microsoft.Data.Edm.Library;
using System.Runtime.CompilerServices;
using Microsoft.Azure.Storage.Blob.Protocol;
using Microsoft.Azure.Storage.Blob;
using Rolex;

namespace Fun
{
    internal static class Program
    {
        // private static readonly string TestResourceDllName = "Microsoft.CodeAnalysis.Test.Resources.Proprietary.dll";
        // TODO: pick a better name
        private static readonly string TestResultsDirectory = @"p:\temp\helix";

        internal static async Task Main(string[] args)
        {
            await Scratch();
            // await QueueSemantics().ConfigureAwait(false);


            /*
            var (helixApi, job) = QueueUnitTests();
            var sentJobTask = job.SendAsync(Console.WriteLine);
            var sentJob = await sentJobTask.ConfigureAwait(false);
            Console.WriteLine(sentJob.CorrelationId);
            Console.WriteLine(sentJob.ResultsContainerUri);
            Console.WriteLine(sentJob.ResultsContainerReadSAS);
            Console.WriteLine(sentJob.ResultsContainerUri + sentJob.ResultsContainerReadSAS);

            var start = DateTime.UtcNow;
            await helixApi.Job.WaitForJobAsync(sentJob.CorrelationId).ConfigureAwait(false);

            Console.WriteLine($"Execution Took {DateTime.UtcNow - start}");

            var token = await GetToken("helix").ConfigureAwait(false);
            var api = ApiFactory.GetAuthenticated(token);
            var source = "test/payload";
            var workItemName = "hello-world";
            var jobTask = api
                .Job
                .Define()
                .WithType("test/helloworld")
                .WithTargetQueue("Windows.10.Amd64.ClientRS5")
                .WithSource(source)
                .DefineWorkItem(workItemName)
                .WithCommand("cmd /c command.bat")
                .WithDirectoryPayload(@"p:\temp\arcade")
                //.WithEmptyPayload()
                .AttachToJob()
                .WithCreator("jaredpar")
                .SendAsync();
            var stream = await api.WorkItem.ConsoleLogAsync(workItemName, job.CorrelationId).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            Console.WriteLine(text);
            */
        }

        internal static async Task Scratch()
        {
            var list = await XUnitUtil.ReadSummariesAsync(@"P:\temp\helix\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.001\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll.001.xml");

            var client = new CloudBlobContainer(new Uri("https://helixre107v0xdeko0k025g8.blob.core.windows.net/results-ff35de0c6adb4b4cb6?sv=2018-03-28&sr=c&sig=F2pS5%2BF5XUB6FaJBurbbip4UHu5OqJ5Zd3lyeBdvCzc%3D&se=2019-10-10T20%3A48%3A42Z&sp=rl"));
            var util = new TestResultUtil(client);
            await util.DownloadAsync(@"p:\temp\helix");
        }

        private static async Task Run(IHelixApi helixApi, IJobDefinition job)
        {
            var sendStart = DateTime.UtcNow; 
            var sentJobTask = job.SendAsync(Console.WriteLine);
            var sentJob = await sentJobTask.ConfigureAwait(false);
            var sendEnd = DateTime.UtcNow;
            Console.WriteLine(sentJob.CorrelationId);
            Console.WriteLine(sentJob.ResultsContainerUri);
            Console.WriteLine(sentJob.ResultsContainerReadSAS);
            Console.WriteLine(sentJob.ResultsContainerUri + sentJob.ResultsContainerReadSAS);

            var executionStart = DateTime.UtcNow;
            await helixApi.Job.WaitForJobAsync(sentJob.CorrelationId, pollingIntervalMs: 5000).ConfigureAwait(false);
            var executionEnd = DateTime.UtcNow;

            Console.WriteLine("Downloading result files");
            var util = new TestResultUtil(sentJob);

            if (Directory.Exists(TestResultsDirectory))
            {
                Directory.Delete(TestResultsDirectory, recursive: true);
            }
            Directory.CreateDirectory(TestResultsDirectory);
            await util.DownloadAsync(TestResultsDirectory).ConfigureAwait(false);



            Console.WriteLine($"Upload Took {sendEnd - sendStart}");
            Console.WriteLine($"Execution Took {executionEnd - executionStart}");
            Console.WriteLine($"Total time {executionEnd - sendStart}");
        }

        private static async Task QueueSemantics()
        {
            // const string unitTestFilePath = @"P:\roslyn\artifacts\bin\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests\Debug\net472\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll";
            const string unitTestFilePath = @"P:\roslyn\artifacts\bin\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests\Debug\net472\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll";
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

            PrepXunit();

            var api = ApiFactory.GetAnonymous();
            var job = api
                .Job
                .Define()
                .WithType("test/unit")
                // .WithTargetQueue("Windows.10.Amd64.Open")
                .WithTargetQueue("Windows.10.Amd64.ClientRS5.Open")
                .WithSource("RoslynUnitTests")
                .WithCreator("jaredpar")
                .WithCorrelationPayloadDirectory(unitTestDirectory);

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
            }

            Console.WriteLine($"Partition count is {batchFileNames.Count}");
            await Run(api, job).ConfigureAwait(false);

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

            void PrepXunit()
            {
                var xunitToolsDirectory = @"P:\nuget\xunit.runner.console\2.4.1\tools\net472";
                foreach (var sourceFilePath in Directory.EnumerateFiles(xunitToolsDirectory))
                {
                    var destFileName = Path.GetFileName(sourceFilePath);
                    var destFilePath = Path.Combine(unitTestDirectory, destFileName);
                    File.Copy(sourceFilePath, destFilePath, overwrite: true);
                }
            }
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

        /*
         * 
        internal static (IHelixApi HelixApi, IJobDefinition JobDefinition) QueueUnitTests()
        {
            var api = ApiFactory.GetAnonymous();
            var job = api
                .Job
                .Define()
                .WithType("test/unit")
                // .WithTargetQueue("Windows.10.Amd64.Open")
                .WithTargetQueue("Windows.10.Amd64.ClientRS5.Open")
                .WithSource("RoslynUnitTests")
                .WithCreator("jaredpar");

            var scheduler = new AssemblyScheduler(methodLimit: 500);
            foreach (var directoryRoot in Directory.EnumerateDirectories(@"P:\roslyn\artifacts\bin", "*CSharp*.UnitTests"))
            {
                var directory = Path.Combine(directoryRoot, @"Debug\net472");
                if (Directory.Exists(directory))
                {
                    Console.Write($"Queueing {directory} ... ");
                    job = QueueUnitTest(job, scheduler, directory);
                    Console.WriteLine("Done");
                }
            }

            return (api, job);
        }

        internal static IJobDefinition QueueUnitTest(IJobDefinition job, AssemblyScheduler scheduler, string unitTestDirectory)
        {
            PrepXunit();

            var unitTestFilePath = Directory.EnumerateFiles(unitTestDirectory, "*.UnitTests.dll").Single();
            var unitTestFileName = Path.GetFileName(unitTestFilePath);
            IEnumerable<AssemblyPartitionInfo> partitions;

            if (unitTestFileName == "Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll" ||
                unitTestFileName == "Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.dll" ||
                unitTestFileName == "Microsoft.CodeAnalysis.EditorFeatures.UnitTests.dll" ||
                unitTestFileName == "Microsoft.CodeAnalysis.EditorFeatures2.UnitTests.dll" ||
                unitTestFileName == "Microsoft.VisualStudio.LanguageServices.UnitTests.dll" ||
                unitTestFileName == "Microsoft.CodeAnalysis.CSharp.EditorFeatures.UnitTests.dll" ||
                unitTestFileName == "Microsoft.CodeAnalysis.VisualBasic.EditorFeatures.UnitTests.dll")
            {
                partitions = scheduler.Schedule(unitTestFilePath);
            }
            else
            {
                partitions = new[] { new AssemblyPartitionInfo(unitTestFilePath) };
            }

            var partitionId = 0;
            foreach (var info in partitions)
            {
                job = QueueOne(job, info, partitionId);
                partitionId++;
            }

            return job;

            IJobDefinition QueueOne(IJobDefinition job, AssemblyPartitionInfo info, int partitionId)
            {
                var displayName = Path.GetFileNameWithoutExtension(unitTestFileName) + $".{partitionId}";
                var batchFileName = EnsureBatchFile(info, partitionId);
                return job
                    .DefineWorkItem(displayName)
                    .WithCommand(@$"cmd /c {batchFileName}")
                    .WithDirectoryPayload(unitTestDirectory)
                    .WithTimeout(TimeSpan.FromMinutes(15))
                    .AttachToJob();
            }

            string EnsureBatchFile(AssemblyPartitionInfo info, int partitionId)
            {
                var uploadEnvironmentName = "%HELIX_WORKITEM_UPLOAD_ROOT%";
                var partitionDirectoryName = $"Partition{partitionId}";
                var xmlFilePath = @$"{uploadEnvironmentName}\{partitionDirectoryName}\{unitTestFileName}.xml";
                var htmlFilePath = @$"{uploadEnvironmentName}\{partitionDirectoryName}\{unitTestFileName}.html";

                var name = Path.GetDirectoryName(unitTestDirectory);
                var batchFileName = $"xunit-{partitionId}.cmd";
                var batchFilePath = Path.Combine(unitTestDirectory, batchFileName);
                var batchContent = @$".\xunit.console.exe {unitTestFileName} -html {htmlFilePath} -xml {xmlFilePath} {info.ClassListArgumentString}";

                try
                {
                    if (File.Exists(batchFilePath))
                    {
                        var currentContet = File.ReadAllText(batchFilePath);
                        if (currentContet == batchContent)
                        {
                            // don't screw up payload caching by modifying the directory if the batch file has already
                            // been laid down
                            return batchFileName;
                        }
                    }
                }
                catch
                {
                    // Error reading the file, no problem, just rewrite it with current content
                }

                File.WriteAllText(Path.Combine(unitTestDirectory, batchFileName), batchContent);
                return batchFileName;
            }

            void PrepXunit()
            {
                var xunitToolsDirectory = @"P:\nuget\xunit.runner.console\2.4.1\tools\net472";
                foreach (var sourceFilePath in Directory.EnumerateFiles(xunitToolsDirectory))
                {
                    var destFileName = Path.GetFileName(sourceFilePath);
                    var destFilePath = Path.Combine(unitTestDirectory, destFileName);
                    File.Copy(sourceFilePath, destFilePath, overwrite: true);
                }
            }
        }

    */
    }
}
