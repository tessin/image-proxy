using System;
using System.Diagnostics;
using System.Text;

namespace Tessin
{
    public static class Shell
    {
        public class ExecResult
        {
            public string Output { get; set; }
            public int ExitCode { get; set; }
        }

        public static ExecResult Exec(string command, string dir = null, bool echoOutputToConsole = false)
        {
            // see the following reference for how Kudu actually executes a command
            // https://github.com/projectkudu/kudu/blob/2ea953e532d0a44afe291dc4b9e370df9fc51686/Kudu.Core/Infrastructure/Executable.cs#L285

            // executing through the starter.cmd command has to do with enabling Unicode support
            // see https://github.com/projectkudu/kudu/commit/f785b532dd1974633e8c4e24887f90b95c04bea7#diff-f9b636b947d94fea4f57457ae904f484

            return ExecFile(Environment.GetEnvironmentVariable("COMSPEC"), $"/c {command}", dir, echoOutputToConsole);
        }

        private static ExecResult ExecFile(string fileName, string arguments, string dir = null, bool echoOutputToConsole = false)
        {
            var pStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = dir ?? Environment.CurrentDirectory,
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false,
            };

            // note: a deadlock will occur if read from both streams synchronously

            var output = new StringBuilder();

            using (var p = Process.Start(pStartInfo))
            {
                p.OutputDataReceived += (sender, e) =>
                {
                    var line = e.Data;
                    if (line == null) return; // end of file
                    if (echoOutputToConsole) Console.WriteLine(line);
                    output.AppendLine(line);
                };
                p.BeginOutputReadLine();

                // pipe stderr to console for troubleshooting
                p.ErrorDataReceived += (sender, e) =>
                {
                    var line = e.Data;
                    if (line == null) return; // end of file
                    Console.Error.WriteLine(line);
                };
                p.BeginErrorReadLine();

                p.WaitForExit();

                return new ExecResult
                {
                    Output = output.ToString(),
                    ExitCode = p.ExitCode,
                };
            }
        }
    }
}
