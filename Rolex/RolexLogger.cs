using Microsoft.Data.OData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rolex
{
    internal sealed class RolexLogger : IDisposable
    {
        private FileStream _fileStream;
        private StreamWriter _streamWriter;
        internal RolexRunInfo RolexRunInfo { get; }

        internal RolexLogger(RolexRunInfo rolexRunInfo)
        {
            RolexRunInfo = rolexRunInfo;
            var filePath = Path.Combine(rolexRunInfo.DataDirectory, "log.txt");
            _fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            _streamWriter = new StreamWriter(_fileStream);
        }

        internal void Log(string message) => _streamWriter.WriteLine(message);

        internal void LogAndConsole(string message)
        {
            Console.WriteLine(message);
            _streamWriter.WriteLine(message);
        }

        private void Dispose()
        {
            if (_fileStream is object)
            {
                _streamWriter.Close();
                _streamWriter = null;
                _fileStream.Close();
                _fileStream = null;
            }
        }

        #region IDisposable

        void IDisposable.Dispose() => Dispose();

        #endregion
    }
}
