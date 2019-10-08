using Microsoft.DotNet.Helix.Client;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Metadata;
using Microsoft.WindowsAzure.Storage.File.Protocol;
using Microsoft.Data.Edm.Library;
using System.Runtime.CompilerServices;
using Microsoft.Azure.Storage.Blob.Protocol;
using Microsoft.Azure.Storage.Blob;
using Microsoft.DotNet.Helix.Client.Models;
using Mono.Options;

namespace Rolex
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;

        internal static async Task<int> Main(string[] args)
        {
            await Scratch().ConfigureAwait(false);

            var optionSet = new OptionSet()
            {
            };

            List<string> extraOptions;
            try
            {
                extraOptions = optionSet.Parse(args);
            }
            catch (OptionException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Try --help for more information");
                return ExitFailure;
            }

            var rolexStorage = new RolexStorage();
            try
            {
                if (extraOptions.Count == 0)
                {
                    extraOptions = new List<string>(new[] { "queue" });
                }

                var option = extraOptions[0].ToLower();
                switch (option)
                {
                    case "list": 
                        List(rolexStorage);
                        break;
                    default:
                        throw new Exception($"Unrecognized option {option}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                ShowHelp(optionSet);
                return ExitFailure;
            }

            return ExitSuccess;

            static void List(RolexStorage rolexStorage)
            {
                var all = rolexStorage.ListNames();
                foreach (var name in all)
                {
                    Console.WriteLine(name);
                }

                Console.WriteLine($"{all.Count} jobs");
            }
        }

        private static void ShowHelp(OptionSet optionSet)
        {
            // TODO: implement
        }

        internal static async Task Scratch()
        {
            await Task.Yield();
        }

        internal static async Task Scratch2()
        {
            var helixApi = ApiFactory.GetAnonymous();
            var queueInfo = await RolexUtil.FindQueueInfoAsync(helixApi).ConfigureAwait(false);
            Console.WriteLine($"Using {queueInfo.QueueId}");

            var start = DateTime.UtcNow;
            var util = new RoslynHelixUtil(helixApi, queueInfo.QueueId, Console.WriteLine);
            var uploadStart = DateTime.UtcNow;
            var list = await util.QueueAllAsync(@"p:\roslyn", "Debug").ConfigureAwait(false);
            Console.WriteLine($"Upload took {DateTime.UtcNow - uploadStart}");

            // TODO this is a hack, pick a real directory
            var display = new HelixJobDisplay(@"p:\temp\helix");
            await display.Display(list).ConfigureAwait(false);
            Console.WriteLine($"Total time {DateTime.UtcNow - start}");

            /*
            RoslynHelixUtil.ZipDirectory(@"p:\temp\test.zip", @"P:\roslyn\artifacts\bin\Microsoft.Build.Tasks.CodeAnalysis.UnitTests\Debug\net472", RoslynHelixUtil.TestResourceDllName);
            await PrintSummaries();
            var list = await XUnitUtil.ReadSummariesAsync(@"P:\temp\helix\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.001\Microsoft.CodeAnalysis.CSharp.Emit.UnitTests.dll.001.xml");

            var client = new CloudBlobContainer(new Uri("https://helixre107v0xdeko0k025g8.blob.core.windows.net/results-ff35de0c6adb4b4cb6?sv=2018-03-28&sr=c&sig=F2pS5%2BF5XUB6FaJBurbbip4UHu5OqJ5Zd3lyeBdvCzc%3D&se=2019-10-10T20%3A48%3A42Z&sp=rl"));
            var util = new TestResultUtil(client);
            await util.DownloadAsync(@"p:\temp\helix");
            */
        }
    }
}
