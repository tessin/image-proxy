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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]
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
                    // Source is not required if you post an image to the proxy
                    if (!(err == ErrorCode.SourceIsRequired && req.Method == HttpMethod.Post))
                    {
                        return req.CreateResponse(HttpStatusCode.BadRequest, err);
                    }
                }

                var fileNameHint = ps.Source != null ? Path.GetFileNameWithoutExtension(ps.Source.LocalPath) : "image";

                // if you try to use the BitmapDecoder/BitmapImage class without a seekable stream you will get an error
                // internally a call to GetUrlCacheConfigInfo is made but this API isn't supported
                //  12004       ERROR_INTERNET_INTERNAL_ERROR
                //              An internal error has occurred.
                // see https://support.microsoft.com/en-us/help/193625/info-wininet-error-codes-12001-through-12156

                // you can see this by poking around the framework source code
                // see https://referencesource.microsoft.com/#PresentationCore/Core/CSharp/System/Windows/Media/Imaging/BitmapDecoder.cs,337
                // if you do not provide a seekable stream you get a LateBoundBitmapDecoder which has the issue

                var mem = new MemoryStream(); // need a seekable stream

                if (req.Method == HttpMethod.Get)
                {
                    try
                    {
                        var sourceReq = WebRequest.CreateHttp(ps.Source);

                        using (var res = await sourceReq.GetResponseAsync())
                        {
                            using (var inputStream = res.GetResponseStream())
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
                }
                else
                {
                    // Read from body...

                    using (var inputStream = await req.Content.ReadAsStreamAsync())
                    {
                        await inputStream.CopyToAsync(mem);
                    }
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

                BitmapSource bitmapSource = bitmapDecoder.Frames[0];

                if (ps.CropSmart)
                {
                    // todo: fix smart cropping...
                    // todo: use face rectangles to find a midpoint then resize the image preserving as much of the face rect as possible
                    //
                    // 

                    // Use the area of interest from the computer vision API to crop the image

                    //var analysis = await ComputerVision.Analyze(bitmapSource);

                    var areaOfInterest = await ComputerVision.GetAreaOfInterestAsync(bitmapSource);

                    var targetWidth = ps.PixelWidth;
                    var targetHeight = ps.PixelHeight;

                    if ((targetWidth == 0) ^ (targetHeight == 0))
                    {
                        if (targetWidth == 0)
                        {
                            targetWidth = (int)(targetHeight * (double)bitmapSource.PixelWidth / bitmapSource.PixelHeight);
                        }

                        if (targetHeight == 0)
                        {
                            targetHeight = (int)(targetWidth * (double)bitmapSource.PixelHeight / bitmapSource.PixelWidth);
                        }
                    }
                    else if (targetWidth == 0 && targetHeight == 0) // edge case: use area of interest
                    {
                        targetWidth = areaOfInterest.Width;
                        targetHeight = areaOfInterest.Height;
                    }

                    // midpoints

                    var mx = areaOfInterest.X + areaOfInterest.Width / 2;
                    var my = areaOfInterest.Y + areaOfInterest.Height / 2;

                    // the size of the crop rect is found by expanding the target rect 
                    // until it encompasses the area of interest in at least both directions

                    var sx = (double)areaOfInterest.Width / targetWidth;
                    var sy = (double)areaOfInterest.Height / targetHeight;

                    var s = Math.Max(sx, sy); // unifrom to fill

                    var w = Math.Min((int)(s * targetWidth), bitmapSource.PixelWidth);
                    var h = Math.Min((int)(s * targetHeight), bitmapSource.PixelHeight);

                    // The area of interest has a bias towards some point in the image
                    // this can cause the crop rect to be off center but if the crop rect
                    // has to expand a lot, it may be placed outside the source
                    // need to adjust for that

                    var x = mx - w / 2;
                    if (x < 0)
                    {
                        w = Math.Max(1, w + x);
                        x = 0;
                    }
                    var y = my - h / 2;
                    if (y < 0)
                    {
                        h = Math.Max(1, h + y);
                        y = 0;
                    }

                    var cropRect = new Int32Rect(x, y, w, h);

                    bitmapSource = new CroppedBitmap(bitmapSource, cropRect);
                }
                else if (ps.Crop.HasArea)
                {
                    var dw = (double)bitmapSource.PixelWidth;
                    var dh = (double)bitmapSource.PixelHeight;

                    var x = (int)Math.Round((1d / 100) * ps.Crop.X * dw);
                    var y = (int)Math.Round((1d / 100) * ps.Crop.Y * dh);
                    var w = (int)Math.Round((1d / 100) * ps.Crop.Width * dw);
                    var h = (int)Math.Round((1d / 100) * ps.Crop.Height * dh);

                    var cropRect = new Int32Rect(
                        x,
                        y,
                        Math.Min(w, bitmapSource.PixelWidth - x),
                        Math.Min(h, bitmapSource.PixelHeight - y)
                    );

                    bitmapSource = new CroppedBitmap(bitmapSource, cropRect);
                }

                if (ps.PixelWidth != 0 || ps.PixelHeight != 0)
                {
                    if ((ps.PixelWidth != 0) ^ (ps.PixelHeight != 0))
                    {
                        // 1 dimension only, maintains aspect ratio

                        if (ps.PixelWidth != 0)
                        {
                            var sx = (double)ps.PixelWidth / bitmapSource.PixelWidth;
                            var sy = sx;

                            // todo: smart

                            var t = new TransformedBitmap(bitmapSource, new ScaleTransform(sx, sy, 0, 0));
                            bitmapSource = t;
                        }

                        if (ps.PixelHeight != 0)
                        {
                            var sy = (double)ps.PixelHeight / bitmapSource.PixelHeight;
                            var sx = sy;

                            // todo: smart

                            var t = new TransformedBitmap(bitmapSource, new ScaleTransform(sx, sy, 0, 0));
                            bitmapSource = t;
                        }
                    }
                    else
                    {
                        var sx = (double)ps.PixelWidth / bitmapSource.PixelWidth;
                        var sy = (double)ps.PixelHeight / bitmapSource.PixelHeight;

                        switch (ps.Fit)
                        {
                            // fit will preserve aspect ratio by reszing the image 
                            // to touch the bounds from the outside (i.e. no border) 

                            case FittingType.None:
                            {
                                var t = new TransformedBitmap(bitmapSource, new ScaleTransform(sx, sy, 0, 0));
                                bitmapSource = t;
                                break;
                            }

                            case FittingType.Min:
                            {
                                // min won't cover the whole area
                                // therefore we render the image
                                // with an optional background color
                                // in center

                                var s = Math.Min(sx, sy);

                                var t = new TransformedBitmap(bitmapSource, new ScaleTransform(s, s, 0, 0));

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

                                bitmapSource = target;
                                break;
                            }

                            case FittingType.Max:
                            {
                                var s = Math.Max(sx, sy);
                                var t = new TransformedBitmap(bitmapSource, new ScaleTransform(s, s, 0, 0));
                                var c = new CroppedBitmap(t, new Int32Rect(t.PixelWidth / 2 - ps.PixelWidth / 2, t.PixelHeight / 2 - ps.PixelHeight / 2, ps.PixelWidth, ps.PixelHeight));
                                bitmapSource = c;
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
                        bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));

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
                                    FileName = $"{fileNameHint}_{ps.PixelWidth}_{ps.PixelHeight}.png",
                                }
                            }
                        };
                        return res;
                    }

                    case ImageType.Jpg:
                    {
                        var bitmapEncoder = new JpegBitmapEncoder();
                        bitmapEncoder.QualityLevel = Math.Max(ps.Quality, 1); // lowest JPEG quality level is 1 (WebP actually has a quality level of 0)
                        bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));

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
                                    FileName = $"{fileNameHint}_{ps.PixelWidth}_{ps.PixelHeight}.jpg",
                                }
                            }
                        };
                        return res;
                    }

                    case ImageType.Webp:
                    {
                        var webp = new WebP();
                        webp.QualityLevel = ps.Quality;
                        webp.Frames.Add(BitmapFrame.Create(bitmapSource));

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
                                    FileName = $"{fileNameHint}_{ps.PixelWidth}_{ps.PixelHeight}.webp",
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
