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
        internal static async Task<int> Main(string[] args)
        {
            // args = new[] { "queue", "-a", @"P:\roslyn\artifacts\bin\Microsoft.CodeAnalysis.CSharp.Syntax.UnitTests\Debug\net472\Microsoft.CodeAnalysis.CSharp.Syntax.UnitTests.dll" };
            // args = new[] { "list" };
            args = new[] { "queue" };
            // args = new[] { "analyze", "2019-10-09 21:11:51" };
            // args = new[] { "wait", "2019-10-09 23:10:25" };
            // args = new[] { "wait", "2019-10-22 17:01:29" };
            // TODO: this is a hack to let me experiment quickly
            await Scratch().ConfigureAwait(false);
            var rolex = new Rolex();
            return await rolex.Run(args).ConfigureAwait(false);
        }

        internal static async Task Scratch()
        {
            await Task.Yield();
        }
    }
}
