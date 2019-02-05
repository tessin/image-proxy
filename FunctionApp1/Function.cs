using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FunctionApp1
{
  public static class Function
  {
    [FunctionName("tix")]
    public static async Task<HttpResponseMessage> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
            HttpRequestMessage req,
        TraceWriter log
    )
    {
      ErrorCode err;

      try
      {
        var ps = new Parameters();

        err = ps.ReadFrom(req.RequestUri, req.GetQueryNameValuePairs());
        if (err != 0)
        {
          return req.CreateResponse(HttpStatusCode.BadRequest, err);
        }

        // if you try to use the BitmapDecoder/BitmapImage class without a seekable stream you will get an error
        // internally a call to GetUrlCacheConfigInfo is made but this API isn't supported
        //  12004       ERROR_INTERNET_INTERNAL_ERROR
        //              An internal error has occurred.
        // see https://support.microsoft.com/en-us/help/193625/info-wininet-error-codes-12001-through-12156

        // you can see this by poking around the framework source code
        // see https://referencesource.microsoft.com/#PresentationCore/Core/CSharp/System/Windows/Media/Imaging/BitmapDecoder.cs,337
        // if you do not provide a seekable stream you get a LateBoundBitmapDecoder which has the issue

        var mem = new MemoryStream(); // need a seekable stream

        try
        {
          using (var http = new HttpClient())
          {
            using (var inputStream = await http.GetStreamAsync(ps.Source))
            {
              await inputStream.CopyToAsync(mem);
            }
          }
        }
        catch (Exception ex)
        {
          log.Error($"cannot request resource '{ps.Source}'", ex);

          return req.CreateResponse(HttpStatusCode.BadRequest, ErrorCode.SourceRequestError);
        }

        mem.Position = 0;

        BitmapDecoder bitmapDecoder;

        try
        {
          bitmapDecoder = BitmapDecoder.Create(mem, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.None);
        }
        catch (Exception ex)
        {
          log.Error($"cannot decode image resource '{ps.Source}'", ex);

          return req.CreateResponse(HttpStatusCode.BadRequest, ErrorCode.ImageDecoderError);
        }

        BitmapSource imageSource = bitmapDecoder.Frames[0];

        if (ps.Crop.HasArea)
        {
          var dw = (double)imageSource.PixelWidth;
          var dh = (double)imageSource.PixelHeight;

          var x = (int)Math.Round((1d / 100) * ps.Crop.X * dw);
          var y = (int)Math.Round((1d / 100) * ps.Crop.Y * dh);
          var w = (int)Math.Round((1d / 100) * ps.Crop.Width * dw);
          var h = (int)Math.Round((1d / 100) * ps.Crop.Height * dh);

          var cropRect = new Int32Rect(
              x,
              y,
              Math.Min(w, imageSource.PixelWidth - x),
              Math.Min(h, imageSource.PixelHeight - y)
          );

          imageSource = new CroppedBitmap(imageSource, cropRect);
        }

        if (ps.PixelWidth != 0 || ps.PixelHeight != 0)
        {
          if ((ps.PixelWidth != 0) ^ (ps.PixelHeight != 0))
          {
            // 1 dimension only, maintains aspect ratio

            if (ps.PixelWidth != 0)
            {
              var sx = (double)ps.PixelWidth / imageSource.PixelWidth;
              var sy = sx;

              var t = new TransformedBitmap(imageSource, new ScaleTransform(sx, sy, 0, 0));
              imageSource = t;
            }

            if (ps.PixelHeight != 0)
            {
              var sy = (double)ps.PixelHeight / imageSource.PixelHeight;
              var sx = sy;

              var t = new TransformedBitmap(imageSource, new ScaleTransform(sx, sy, 0, 0));
              imageSource = t;
            }
          }
          else
          {
            var sx = (double)ps.PixelWidth / imageSource.PixelWidth;
            var sy = (double)ps.PixelHeight / imageSource.PixelHeight;

            switch (ps.Fit)
            {
              // fit will preserve aspect ratio by reszing the image 
              // to touch the bounds from the outside (i.e. no border) 

              case FittingType.None:
                {
                  var t = new TransformedBitmap(imageSource, new ScaleTransform(sx, sy, 0, 0));
                  imageSource = t;
                  break;
                }
              case FittingType.Min:
                {
                  // min won't cover the whole area
                  // therefore we render the image
                  // with an optional background color
                  // in center

                  var s = Math.Min(sx, sy);

                  var t = new TransformedBitmap(imageSource, new ScaleTransform(s, s, 0, 0));

                  var r = new Int32Rect(
                      Math.Abs(t.PixelWidth / 2 - ps.PixelWidth / 2),
                      Math.Abs(t.PixelHeight / 2 - ps.PixelHeight / 2),
                      Math.Min(ps.PixelWidth, t.PixelWidth),
                      Math.Min(ps.PixelHeight, t.PixelHeight)
                  );

                  var target = new RenderTargetBitmap(ps.PixelWidth, ps.PixelHeight, 96, 96, PixelFormats.Pbgra32);

                  var drawingVisual = new DrawingVisual();

                  using (var g = drawingVisual.RenderOpen())
                  {
                    // clear
                    g.DrawRectangle(new SolidColorBrush(ps.BackgroundColor), null, new Rect(0, 0, target.PixelWidth, target.PixelHeight));

                    g.DrawImage(t, new Rect(r.X, r.Y, r.Width, r.Height));
                  }

                  target.Render(drawingVisual);

                  imageSource = target;
                  break;
                }
              case FittingType.Max:
                {
                  var s = Math.Max(sx, sy);
                  var t = new TransformedBitmap(imageSource, new ScaleTransform(s, s, 0, 0));
                  var c = new CroppedBitmap(t, new Int32Rect(t.PixelWidth / 2 - ps.PixelWidth / 2, t.PixelHeight / 2 - ps.PixelHeight / 2, ps.PixelWidth, ps.PixelHeight));
                  imageSource = c;
                  break;
                }
              default:
                return req.CreateErrorResponse(HttpStatusCode.NotImplemented, $"fitting option {ps.Fit} is not implemented");
            }
          }
        }

        var type = ps.Type;
        if (type == ImageType.None)
        {
          if (bitmapDecoder is System.Windows.Media.Imaging.PngBitmapDecoder)
          {
            type = ImageType.Png;
          }
          else
          {
            type = ImageType.Jpg;
          }
        }

        switch (type)
        {
          case ImageType.Png:
            {
              var bitmapEncoder = new PngBitmapEncoder();
              bitmapEncoder.Frames.Add(BitmapFrame.Create(imageSource));

              var outputStream = new MemoryStream();
              bitmapEncoder.Save(outputStream);
              outputStream.Position = 0;

              var res = req.CreateResponse(HttpStatusCode.OK);
              res.Headers.CacheControl = new CacheControlHeaderValue { Public = true };
              res.Content = new StreamContent(outputStream)
              {
                Headers = {
                    ContentType = new MediaTypeHeaderValue("image/png"),
                    ContentDisposition = new ContentDispositionHeaderValue("inline") {
                        FileName = $"{Path.GetFileNameWithoutExtension(ps.Source.LocalPath)}_{ps.PixelWidth}_{ps.PixelHeight}.png",
                    }
                }
              };
              return res;
            }

          case ImageType.Jpg:
            {
              var bitmapEncoder = new JpegBitmapEncoder();
              bitmapEncoder.QualityLevel = Math.Max(ps.Quality, 1); // lowest JPEG quality level is 1 (WebP actually has a quality level of 0)
              bitmapEncoder.Frames.Add(BitmapFrame.Create(imageSource));

              var outputStream = new MemoryStream();
              bitmapEncoder.Save(outputStream);
              outputStream.Position = 0;

              var res = req.CreateResponse(HttpStatusCode.OK);
              res.Headers.CacheControl = new CacheControlHeaderValue { Public = true };
              res.Content = new StreamContent(outputStream)
              {
                Headers = {
                    ContentType = new MediaTypeHeaderValue("image/jpeg"),
                    ContentDisposition = new ContentDispositionHeaderValue("inline") {
                        FileName = $"{Path.GetFileNameWithoutExtension(ps.Source.LocalPath)}_{ps.PixelWidth}_{ps.PixelHeight}.jpg",
                    }
                }
              };
              return res;
            }
          case ImageType.Webp:
            {
              var webp = new WebP();
              webp.QualityLevel = ps.Quality;
              webp.Frames.Add(BitmapFrame.Create(imageSource));

              var outputStream = new MemoryStream();
              webp.Save(outputStream);
              outputStream.Position = 0;

              var res = req.CreateResponse(HttpStatusCode.OK);
              res.Headers.CacheControl = new CacheControlHeaderValue { Public = true };
              res.Content = new StreamContent(outputStream)
              {
                Headers = {
                    ContentType = new MediaTypeHeaderValue("image/webp"),
                    ContentDisposition =new ContentDispositionHeaderValue("inline") {
                        FileName = $"{Path.GetFileNameWithoutExtension(ps.Source.LocalPath)}_{ps.PixelWidth}_{ps.PixelHeight}.webp",
                    }
                }
              };
              return res;
            }
          default:
            return req.CreateErrorResponse(HttpStatusCode.NotImplemented, $"image request type {ps.Type} is not implemented");
        }
      }
      catch (Exception ex)
      {
        var sb = new StringBuilder();

        RenderErrorToString(ex, sb);

        var res = req.CreateResponse(HttpStatusCode.InternalServerError);
        res.Content = new StringContent(sb.ToString())
        {
          Headers = {
                        ContentType = new MediaTypeHeaderValue("text/plain")
                    }
        };
        return res;
      }
    }

    private static void RenderErrorToString(Exception ex, StringBuilder sb)
    {
      sb.Append($"[{ex.GetType()}]: {ex.Message}");
      sb.Append(ex.StackTrace);
      sb.AppendLine();

      if (ex.InnerException != null)
      {
        RenderErrorToString(ex.InnerException, sb);
      }
    }
  }
}
