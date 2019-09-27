using Microsoft.DotNet.Helix.Client;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Metadata;

namespace Fun
{
    internal static class Program
    {
        internal static async Task Main(string[] args)
        {
            var (helixApi, job) = QueueUnitTests();
            var sentJobTask = job.SendAsync(Console.WriteLine);
            var sentJob = await sentJobTask.ConfigureAwait(false);
            Console.WriteLine(sentJob.CorrelationId);
            Console.WriteLine(sentJob.ResultsContainerUri);
            Console.WriteLine(sentJob.ResultsContainerReadSAS);
            Console.WriteLine(sentJob.ResultsContainerUri + sentJob.ResultsContainerReadSAS);

            var start = DateTime.UtcNow;
            await WaitForJobAsync(helixApi.Job, sentJob.CorrelationId);

            Console.WriteLine($"Execution Took {DateTime.UtcNow - start}");

            /*
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

        internal static (IHelixApi HelixApi, IJobDefinition JobDefinition) QueueUnitTests()
        {
            var api = ApiFactory.GetAnonymous();
            var job = api
                .Job
                .Define()
                .WithType("test/unit")
                .WithTargetQueue("Windows.10.Amd64.ClientRS5.Open")
                .WithSource("RoslynUnitTests")
                .WithCreator("jaredpar");

            foreach (var directoryRoot in Directory.EnumerateDirectories(@"P:\roslyn\artifacts\bin", "*CSharp*.UnitTests"))
            {
                var directory = Path.Combine(directoryRoot, @"Debug/net472");
                if (Directory.Exists(directory))
                {
                    Console.Write($"Queueing {directory} ... ");
                    job = QueueUnitTest(job, directory);
                    Console.WriteLine("Done");
                }
            }

            return (api, job);
        }

        internal static IJobDefinition QueueUnitTest(IJobDefinition job, string unitTestDirectory)
        {
            prepXunit();

            var unitTestFilePath = Directory.EnumerateFiles(unitTestDirectory, "*.UnitTests.dll").Single();
            var unitTestFileName = Path.GetFileName(unitTestFilePath);
            var uploadEnvironmentName = "%HELIX_WORKITEM_UPLOAD_ROOT%";
            var xmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.xml";
            var htmlFilePath = @$"{uploadEnvironmentName}\{unitTestFileName}.html";

            var name = Path.GetDirectoryName(unitTestDirectory);

            var batchFileName = "xunit.cmd";
            var batchContent = @$".\xunit.console.exe {unitTestFileName} -html {htmlFilePath} -xml {xmlFilePath}";
            File.WriteAllText(Path.Combine(unitTestDirectory, batchFileName), batchContent);

            return job
                .DefineWorkItem(unitTestFileName.Replace('.', '-'))
                .WithCommand(@$"cmd /c {batchFileName}")
                .WithDirectoryPayload(unitTestDirectory)
                .WithTimeout(TimeSpan.FromMinutes(15))
                .AttachToJob();

            void prepXunit()
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


        private static async Task WaitForJobAsync(IJob job, string correlationId)
        {
            do
            {
                try
                {
                    await job.WaitForJobAsync(correlationId).ConfigureAwait(false);
                    break;
                }
                catch (RestApiException ex)
                {
                    if (ex.Response.StatusCode == HttpStatusCode.InternalServerError ||
                        ex.Response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            while (true);
        }

        private static string GetConsoleUrl(string source, string correlationId, string accessToken)
        {
            source = Uri.EscapeDataString(source);
            if (accessToken is object)
            {
                accessToken = $"?access_token={accessToken}";
            }
            return $"https://helix.dot.net/api/2019-06-17/jobs/{correlationId}/workitems/{source}/console{accessToken}";
        }

        internal static async Task<string> GetToken(string name)
        {
            var lines = await File.ReadAllLinesAsync(@"p:\tokens.txt").ConfigureAwait(false);
            foreach (var line in lines)
            {
                var items = line.Split(new[] { ':' }, count: 2, StringSplitOptions.RemoveEmptyEntries);
                if (items[0] == name)
                {
                    return items[1];
                }
            }

            throw new Exception($"Could not find token with name {name}");
        }
    }
}
