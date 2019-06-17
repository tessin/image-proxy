using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
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

        public static async Task<AreaOfInterest> GetAreaOfInterestAsync(BitmapSource bitmapSource)
        {
            var mem = new MemoryStream();

            var bitmapEncoder = new JpegBitmapEncoder();
            bitmapEncoder.QualityLevel = 75; // the computer vision API doesn't need every pixel
            bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            bitmapEncoder.Save(mem);

            mem.Position = 0;

            var req = WebRequest.CreateHttp($"https://{Config.ComputerVisionHost}/vision/v2.0/areaOfInterest");
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
                    return val.AreaOfInterest;
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
