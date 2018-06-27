using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Tessin;

namespace FunctionApp1
{
    class WebP : ImageEncoder
    {
        private static readonly string _cwebp;

        static WebP()
        {
            var xs = new[] {
                @"libwebp-0.6.0-windows-x64\bin\cwebp.exe",
                @"D:\home\site\wwwroot\libwebp-0.6.0-windows-x64\bin\cwebp.exe", // Azure
            };

            var searchedPaths = new List<string>();

            foreach (var x in xs)
            {
                var y = Environment.ExpandEnvironmentVariables(x);

                var fn = Path.GetFullPath(y);
                if (File.Exists(fn))
                {
                    _cwebp = fn;
                    break;
                }

                searchedPaths.Add(fn);
            }

            if (_cwebp == null)
            {
                throw new InvalidOperationException($"cwebp.exe not found. (searchedPaths={string.Join(", ", searchedPaths)})");
            }
        }

        public int QualityLevel { get; set; }

        public override void Save(Stream outputStream)
        {
            using (var webpIn = new TempFileTransaction())
            {
                using (var webpOut = new TempFileTransaction())
                {
                    var pngEncoder = new PngBitmapEncoder();

                    // todo: what does the PngBitmapEncoder.Interlace option do?

                    foreach (var f in Frames)
                    {
                        pngEncoder.Frames.Add(f);
                    }

                    using (var inputStream = File.Create(webpIn.FileName))
                    {
                        pngEncoder.Save(inputStream);
                    }

                    var result = Shell.Exec($"\"\"{_cwebp}\" -q {QualityLevel} \"{webpIn.FileName}\" -o \"{webpOut.FileName}\"\"", echoOutputToConsole: true);

                    using (var inputStream = File.OpenRead(webpOut.FileName))
                    {
                        inputStream.CopyTo(outputStream);
                    }
                }
            }
        }
    }
}
