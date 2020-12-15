using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FunctionApp1
{
    [Serializable]
    public class ComputerVisionException : Exception
    {
        public string Code { get; internal set; }

        public ComputerVisionException() { }
        public ComputerVisionException(string message) : base(message) { }
        public ComputerVisionException(string message, Exception inner) : base(message, inner) { }
        protected ComputerVisionException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    static class ComputerVision
    {
        public class ErrorResponse
        {
            [JsonProperty("error")]
            public Error Error { get; set; }
        }

        public class Error
        {
            [JsonProperty("code")]
            public string Code { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }

        public class AreaOfInterestResponse
        {
            [JsonProperty("areaOfInterest")]
            public AreaOfInterest AreaOfInterest { get; set; }

            public Int32Rect GetRect()
            {
                return new Int32Rect(AreaOfInterest.X, AreaOfInterest.Y, AreaOfInterest.Width, AreaOfInterest.Height);
            }
        }

        public class AreaOfInterest
        {
            [JsonProperty("x")]
            public int X { get; set; }
            [JsonProperty("y")]
            public int Y { get; set; }
            [JsonProperty("w")]
            public int Width { get; set; }
            [JsonProperty("h")]
            public int Height { get; set; }
        }

        public static async Task<Int32Rect> GetAreaOfInterestAsync(BitmapSource bitmapSource)
        {
            var mem = new MemoryStream();

            var bitmapEncoder = new JpegBitmapEncoder();
            bitmapEncoder.QualityLevel = 75; // the computer vision API doesn't need every pixel
            bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            bitmapEncoder.Save(mem);

            mem.Position = 0;

            var req = WebRequest.CreateHttp($"https://{Config.ComputerVisionHost}/vision/v3.1/areaOfInterest");
            req.Method = "POST";
            req.Headers.Add("Ocp-Apim-Subscription-Key", Config.ComputerVisionApiKey);
            req.ContentType = "application/octet-stream";

            await mem.CopyToAsync(req.GetRequestStream());

            try
            {
                using (var res = await req.GetResponseAsync())
                {
                    var reader = new StreamReader(res.GetResponseStream());
                    var val = JsonConvert.DeserializeObject<AreaOfInterestResponse>(await reader.ReadToEndAsync());
                    return val.GetRect();
                }
            }
            catch (WebException ex) when (ex.Response != null)
            {
                var val = JsonConvert.DeserializeObject<ErrorResponse>(new StreamReader(ex.Response.GetResponseStream()).ReadToEnd());
                var err = val.Error;
                throw new ComputerVisionException(err.Message, ex) { Code = err.Code };
            }
        }

        //{
        //  "color": {
        //    "dominantColorForeground": "White",
        //    "dominantColorBackground": "White",
        //    "dominantColors": ["White"],
        //    "accentColor": "A67B25",
        //    "isBwImg": false,
        //    "isBWImg": false
        //  },
        //  "tags": [
        //    { "name": "person", "confidence": 0.9977457523345947 },
        //    { "name": "man", "confidence": 0.9904873371124268 },
        //    { "name": "indoor", "confidence": 0.9894638061523438 },
        //    { "name": "wall", "confidence": 0.9780722856521606 },
        //    { "name": "shirt", "confidence": 0.9706685543060303 },
        //    { "name": "clothing", "confidence": 0.9621132612228394 },
        //    { "name": "human face", "confidence": 0.8707483410835266 },
        //    { "name": "smile", "confidence": 0.8429427742958069 },
        //    { "name": "glasses", "confidence": 0.7695063352584839 },
        //    { "name": "watch", "confidence": 0.6778815984725952 }
        //  ],
        //  "faces": [
        //    {
        //      "age": 30,
        //      "gender": "Male",
        //      "faceRectangle": { "left": 1533, "top": 431, "width": 373, "height": 373 }
        //    }
        //  ],
        //  "requestId": "21a698a6-4054-4170-a50c-1b68db5f5c97",
        //  "metadata": { "height": 2632, "width": 3508, "format": "Jpeg" }
        //}

        public class Color
        {
            public string accentColor { get; set; }
        }

        public class Tag
        {
            public string name { get; set; }
            public double confidence { get; set; }
        }

        public class Face
        {
            public int age { get; set; }
            public string gender { get; set; }
            public FaceRect faceRectangle { get; set; }

            public Int32Rect GetRect()
            {
                return new Int32Rect(faceRectangle.left, faceRectangle.top, faceRectangle.width, faceRectangle.height);
            }
        }

        public class FaceRect
        {
            public int left { get; set; }
            public int top { get; set; }
            public int width { get; set; }
            public int height { get; set; }
        }

        public class ImageAnalysis
        {
            public Color color { get; set; }
            public List<Tag> tags { get; set; }
            public List<Face> faces { get; set; }

            public Int32Rect GetFaceRect()
            {
                var faceRect = faces[0].GetRect();

                for (int i = 0; i < faces.Count; i++)
                {
                    var rect = faces[i].GetRect();
                    faceRect = new Int32Rect(
                        Math.Min(faceRect.X, rect.X),
                        Math.Min(faceRect.Y, rect.Y),
                        Math.Max(faceRect.Width, rect.Width),
                        Math.Max(faceRect.Height, rect.Height)
                    );
                }

                return faceRect;
            }
        }

        public static async Task<ImageAnalysis> Analyze(BitmapSource bitmapSource)
        {
            var mem = new MemoryStream();

            var bitmapEncoder = new JpegBitmapEncoder();
            bitmapEncoder.QualityLevel = 75; // the computer vision API doesn't need every pixel
            bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            bitmapEncoder.Save(mem);

            mem.Position = 0;

            var req = WebRequest.CreateHttp($"https://{Config.ComputerVisionHost}/vision/v3.1/analyze?visualFeatures=Color,Faces,Tags");
            req.Method = "POST";
            req.Headers.Add("Ocp-Apim-Subscription-Key", Config.ComputerVisionApiKey);
            req.ContentType = "application/octet-stream";

            await mem.CopyToAsync(req.GetRequestStream());

            try
            {
                using (var res = await req.GetResponseAsync())
                {
                    var reader = new StreamReader(res.GetResponseStream());
                    var json = await reader.ReadToEndAsync();
                    var val = JsonConvert.DeserializeObject<ImageAnalysis>(json);
                    return val;
                }
            }
            catch (WebException ex) when (ex.Response != null)
            {
                var val = JsonConvert.DeserializeObject<ErrorResponse>(new StreamReader(ex.Response.GetResponseStream()).ReadToEnd());
                var err = val.Error;
                throw new ComputerVisionException(err.Message, ex) { Code = err.Code };
            }
        }
    }
}
