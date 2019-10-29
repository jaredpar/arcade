using Microsoft.DotNet.Helix.Client;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class Rolex
    {
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;

        internal RolexStorage RolexStorage { get; }

        internal Rolex(RolexStorage rolexStorage = null)
        {
            RolexStorage = rolexStorage ?? new RolexStorage();
        }

        internal async Task<int> Run(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    ShowHelp();
                    return ExitFailure;
                }

                var command = args[0].ToLower();
                var commandArgs = args.Skip(1);
                switch (command)
                {
                    case "list":
                        await RunList(commandArgs).ConfigureAwait(false);
                        break;
                    case "queue":
                        await RunQueue(commandArgs).ConfigureAwait(false);
                        break;
                    case "wait":
                        await RunWait(commandArgs).ConfigureAwait(false);
                        break;
                    case "analyze":
                        await RunAnalyze(commandArgs).ConfigureAwait(false);
                        break;
                    default:
                        ShowHelp();
                        break;
                };
                return ExitSuccess;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return ExitFailure;
            }
        }

        private void ShowHelp()
        {
            // TODO: implement
        }

        private async Task RunList(IEnumerable<string> args)
        {
            var all = await RolexStorage.ListRolexRunInfosAsync().ConfigureAwait(false);
            foreach (var rolexRunInfo in all)
            {
                Console.WriteLine(rolexRunInfo.Id);
            }

            Console.WriteLine($"{all.Count} jobs");
        }

        private async Task RunQueue(IEnumerable<string> args)
        {
            var wait = false;
            var serial = false;
            string assemblyFilePath = null;
            string queueId = null;
            var optionSet = new OptionSet()
            {
                { "w|wait", "wait for test run to complete", w => wait = w is object },
                { "s|serial", "serial test runs (no partitioning)", s => serial = s is object },
                { "a|assembly=", "assembly to run", p => assemblyFilePath = p },
                { "q|queueId=", "queue to run on", q => queueId = q },
            };

            ParseAll(optionSet, args);

            var rolexRunInfo = await RolexStorage.CreateRolexRunInfo().ConfigureAwait(false);
            using var rolexLogger = new RolexLogger(rolexRunInfo);
            var helixApi = ApiFactory.GetAnonymous();
            if (queueId is null)
            {
                var queueInfo = await RolexUtil.FindQueueInfoAsync(helixApi).ConfigureAwait(false);
                queueId = queueInfo.QueueId;
            }

            rolexLogger.LogAndConsole($"Using queue {queueId}");

            var start = DateTime.UtcNow;
            var util = new RoslynHelixUtil(helixApi, queueId, rolexLogger.Log);
            var uploadStart = DateTime.UtcNow;
            Task<HelixRun> helixRunTask;
            if (assemblyFilePath is object)
            {
                helixRunTask = util.QueueAssemblyRunAsync(assemblyFilePath, partition: !serial);
            }
            else
            {
                // TODO: don't hard code this vaule
                helixRunTask = util.QueueAllRunAsync(@"p:\roslyn", "Debug");
            }

            await RolexUtil.WriteWithSpinner("Uploading", helixRunTask).ConfigureAwait(false);
            var helixRun = await helixRunTask.ConfigureAwait(false);
            rolexLogger.LogAndConsole($"Upload took {DateTime.UtcNow - uploadStart}");

            await RolexStorage.SaveAsync(rolexRunInfo, helixRun).ConfigureAwait(false);
            rolexLogger.LogAndConsole($"Saved as {rolexRunInfo.Id}");
            if (wait)
            {
                var display = new RolexRunDisplay(RolexStorage);
                await display.DisplayAsync(rolexRunInfo).ConfigureAwait(false);
                rolexLogger.LogAndConsole($"Total time {DateTime.UtcNow - start}");
            }
        }

        private async Task RunWait(IEnumerable<string> args)
        {
            var waitArgs = args.ToList();
            if (waitArgs.Count != 1)
            {
                throw new Exception("wait must be provided an id / name to wait on");
            }

            var rolexRunInfo = await RolexStorage.GetRolexRunInfo(waitArgs[0]).ConfigureAwait(false);
            var display = new RolexRunDisplay(RolexStorage);
            await display.DisplayAsync(rolexRunInfo).ConfigureAwait(false);
        }

        private async Task RunAnalyze(IEnumerable<string> args)
        {
            var partitions = false;
            var verbose = false;
            var optionSet = new OptionSet()
            {
                { "p|partitions", "analyze partitions", p => partitions = p is object },
                { "v|verbose", "verbose output", v => verbose = v is object },
            };

            ParseAll(optionSet, args, out var rolexRunId);
            var rolexRunInfo = await RolexStorage.GetRolexRunInfo(rolexRunId).ConfigureAwait(false);
            var analyzer = new RolexAnalyzer(RolexStorage);

            if (partitions)
            {
                await analyzer.AnalyzePartitionsAsync(rolexRunInfo, verbose).ConfigureAwait(false);
            }
            else
            {
                await analyzer.AnalyzeAsync(rolexRunInfo).ConfigureAwait(false);
            }
        }

        private void ParseAll(OptionSet optionSet, IEnumerable<string> args)
        {
            var extra = optionSet.Parse(args);
            if (extra.Count != 0)
            {
                var text = string.Join(' ', extra);
                throw new Exception($"Extra arguments: {text}");
            }
        }

        private void ParseAll(OptionSet optionSet, IEnumerable<string> args, out string remainingArg)
        {
            var extra = optionSet.Parse(args);
            if (extra.Count != 1)
            {
                var text = string.Join(' ', extra);
                throw new Exception($"Extra arguments: {text}");
            }

            remainingArg = extra[0];
        }
    }
}
