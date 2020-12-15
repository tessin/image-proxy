using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace FunctionApp1
{
    enum ImageType
    {
        /// <summary>
        /// As source, i.e. no processing.
        /// </summary>
        None,
        Jpg,
        Webp,
        Png,
    }

    enum FittingType
    {
        None,
        Min,
        Max,
    }

    enum ErrorCode
    {
        None = 0,
        SourceIsRequired,
        SourceUriFormatError,
        PixelWidthParseError,
        PixelWidthOutOfRange,
        PixelHeightParseError,
        PixelHeightOutOfRange,
        QualityParseError,
        QualityOutOfRange,
        ImageTypeParseError,
        ImageTypeOutOfRange,
        CropRectParseError,
        CropRectOutOfRange,
        FittingTypeParseError,
        BackgroundColorParseError,
        SourceRequestError,
        ImageDecoderError,
    }

    class Parameters
    {
        public Uri Source;
        public int PixelWidth;
        public int PixelHeight;
        public int Quality = 80;
        public ImageType Type = ImageType.None;
        /// <summary>
        /// Use the Azure Computer Vision API - if supported (configured)
        /// </summary>
        public bool CropSmart;
        public Int32Rect Crop;
        public FittingType Fit = FittingType.Max;
        public Color BackgroundColor = Colors.Black;
        public bool IsDebug;

        public static bool TryConvert<T>(string value, out T targetValue, T defaultValue = default(T))
        {
            try
            {
                targetValue = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                targetValue = defaultValue;
                return false;
            }
        }

        private static readonly Regex HexPattern = new Regex("[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8}");

        public ErrorCode ReadFrom(Uri requestUri, IEnumerable<KeyValuePair<string, string>> queryNameValuePairs)
        {
            foreach (var item in queryNameValuePairs)
            {
                switch (item.Key)
                {
                    case "s":
                    {
                        var s = item.Value;
                        if (s.StartsWith("//"))
                        {
                            s = requestUri.Scheme + ":" + s;
                        }
                        if (!Uri.TryCreate(s, UriKind.Absolute, out Source))
                        {
                            return ErrorCode.SourceUriFormatError;
                        }
                    }
                    break;
                    case "q":
                        int quality;
                        if (!TryConvert(item.Value, out quality))
                        {
                            return ErrorCode.QualityParseError;
                        }
                        if (!(0 <= quality && quality <= 100))
                        {
                            return ErrorCode.QualityOutOfRange;
                        }
                        Quality = quality;
                        break;
                    case "w":
                        int pixelWidth;
                        if (!TryConvert(item.Value, out pixelWidth))
                        {
                            return ErrorCode.PixelWidthParseError;
                        }
                        if (pixelWidth < 0)
                        {
                            return ErrorCode.PixelWidthOutOfRange;
                        }
                        PixelWidth = pixelWidth;
                        break;
                    case "h":
                        int pixelHeight;
                        if (!TryConvert(item.Value, out pixelHeight))
                        {
                            return ErrorCode.PixelHeightParseError;
                        }
                        if (pixelHeight < 0)
                        {
                            return ErrorCode.PixelHeightOutOfRange;
                        }
                        PixelHeight = pixelHeight;
                        break;
                    case "t":
                        ImageType type;
                        if (!Enum.TryParse(item.Value, true, out type))
                        {
                            return ErrorCode.ImageTypeParseError;
                        }
                        if (type == 0)
                        {
                            return ErrorCode.ImageTypeOutOfRange;
                        }
                        Type = type;
                        break;
                    case "c":
                        if (item.Value == "smart")
                        {
                            CropSmart = true;
                            break;
                        }
                        // the crop rectange is in percentages, 
                        // based on the source image dimension 
                        // we use this to get the actual crop
                        Int32Rect crop;
                        try
                        {
                            crop = Int32Rect.Parse(item.Value);
                        }
                        catch (Exception)
                        {
                            return ErrorCode.CropRectParseError;
                        }
                        if (!((0 <= (crop.X + crop.Width)) & (crop.X + crop.Width) <= 100))
                        {
                            return ErrorCode.CropRectOutOfRange;
                        }
                        if (!((0 <= (crop.Y + crop.Height)) & (crop.Y + crop.Height) <= 100))
                        {
                            return ErrorCode.CropRectOutOfRange;
                        }
                        Crop = crop;
                        break;
                    case "f":
                        FittingType fit;
                        if (!Enum.TryParse(item.Value, true, out fit))
                        {
                            return ErrorCode.FittingTypeParseError;
                        }
                        Fit = fit;
                        break;
                    case "bg-color":
                        try
                        {
                            if (HexPattern.IsMatch(item.Value))
                            {
                                var s = item.Value;
                                if (s.Length == 3)
                                {
                                    // shorthand #fc0 -> #ffcc00
                                    s = s.Substring(0, 1) + s.Substring(0, 1)
                                      + s.Substring(1, 1) + s.Substring(1, 1)
                                      + s.Substring(2, 1) + s.Substring(2, 1)
                                      ;
                                }
                                BackgroundColor = (Color)ColorConverter.ConvertFromString("#" + item.Value);
                            }
                            else
                            {
                                BackgroundColor = (Color)ColorConverter.ConvertFromString(item.Value);
                            }
                        }
                        catch (Exception)
                        {
                            return ErrorCode.BackgroundColorParseError;
                        }
                        break;
                    case "debug":
                    {
                        IsDebug = item.Value == "1";
                        break;
                    }
                }
            }

            if (Source == null)
            {
                return ErrorCode.SourceIsRequired;
            }

            return ErrorCode.None;
        }
    }
}
