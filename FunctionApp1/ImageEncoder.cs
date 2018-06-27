using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace FunctionApp1
{
    abstract class ImageEncoder
    {
        public List<BitmapFrame> Frames { get; set; } = new List<BitmapFrame>();

        public abstract void Save(Stream outputStream);
    }
}
