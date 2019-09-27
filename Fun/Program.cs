using Microsoft.DotNet.Helix.Client;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Fun
{
    internal static class Program
    {
        internal static async Task Main(string[] args)
        {
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
            var job = await jobTask.ConfigureAwait(false);
            Console.WriteLine(job.CorrelationId);
            Console.WriteLine(job.ResultsContainerUri);
            Console.WriteLine(job.ResultsContainerReadSAS);

            await WaitForJobAsync(api.Job, job.CorrelationId);
            var stream = await api.WorkItem.ConsoleLogAsync(workItemName, job.CorrelationId).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            Console.WriteLine(text);
        }

        private static async Task WaitForJobAsync(IJob job, string correlationId)
        {
            do
            {
                try
                {
                    await job.WaitAsync(correlationId).ConfigureAwait(false);
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
