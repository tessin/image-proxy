using System;
using System.IO;

namespace Tessin
{
    public class TempFileTransaction : IDisposable
    {
        /// <summary>
        /// Gets the full path of a uniquely named, zero-byte temporary file on disk.
        /// </summary>
        public string FileName { get; } = Path.GetTempFileName();

        private bool _complete;

        /// <summary>
        /// Mark the transaction as complete. i.e. do not delete the file when the transaction goes out of scope.
        /// </summary>
        public void Complete()
        {
            _complete = true;
        }

        public void Dispose()
        {
            if (!_complete)
            {
                File.Delete(FileName);
            }
        }
    }
}
