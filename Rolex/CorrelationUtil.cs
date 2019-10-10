using Microsoft.DotNet.Helix.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Rolex
{
    internal sealed class CorrelationUtil
    {
        internal sealed class AssemblyInfo
        {
            internal Guid Mvid { get; }
            internal string FilePath { get; }
            internal int UseCount { get; set; }

            internal bool IncludeInCorrelation => UseCount > 1;
            internal string CorrelationFileName => Mvid.ToString();

            internal AssemblyInfo(Guid mvid, string filePath)
            {
                Mvid = mvid;
                FilePath = filePath;
            }
        }

        private Dictionary<Guid, AssemblyInfo> _mvidToAssemblyInfoMap = new Dictionary<Guid, AssemblyInfo>();
        private Dictionary<string, AssemblyInfo> _filePathToAssemblyInfoMap = new Dictionary<string, AssemblyInfo>(RolexUtil.FileSystemComparer);

        internal void ProcessAll(string[] directories)
        {
            foreach (var directory in directories)
            {
                Process(directory);
            }
        }

        internal void Process(string directory)
        {
            foreach (var (filePath, mvid) in GetMvids(directory))
            {
                if (!_mvidToAssemblyInfoMap.TryGetValue(mvid, out var assemblyInfo))
                {
                    assemblyInfo = new AssemblyInfo(mvid, filePath);
                    _mvidToAssemblyInfoMap[mvid] = assemblyInfo;
                }

                assemblyInfo.UseCount++;
                _filePathToAssemblyInfoMap[filePath] = assemblyInfo;
            }
        }

        internal void CreateCorrelationPayload(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var assemblyInfo in _mvidToAssemblyInfoMap.Values.Where(x => x.IncludeInCorrelation))
            {
                // TODO: hard link
                File.Copy(assemblyInfo.FilePath, Path.Combine(directory, assemblyInfo.CorrelationFileName));
            }
        }

        internal bool TryGetCorrelationFileName(string filePath, out string correlationFileName)
        {
            if (_filePathToAssemblyInfoMap.TryGetValue(filePath, out var assemblyInfo) &&
                assemblyInfo.IncludeInCorrelation)
            {
                correlationFileName = assemblyInfo.CorrelationFileName;
                return true;
            }

            correlationFileName = null;
            return false;
        }

        private IEnumerable<(string FilePath, Guid Mvid)> GetMvids(string directory)
        {
            foreach (var dllFilePath in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                var mvid = RolexUtil.ReadMvid(dllFilePath);
                if (mvid.HasValue)
                {
                    yield return (dllFilePath, mvid.Value);
                }
            }
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode )]
        private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
    }
}
