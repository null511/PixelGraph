﻿using PixelGraph.Common.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelGraph.Common.IO
{
    public interface IImageWriter
    {
        Task WriteAsync(Image image, ImageChannels type, string localFile, CancellationToken token);
    }

    internal class ImageWriter : IImageWriter
    {
        private static readonly Dictionary<string, Func<ImageChannels, IImageEncoder>> map;
        private readonly IOutputWriter writer;


        static ImageWriter()
        {
            map = new Dictionary<string, Func<ImageChannels, IImageEncoder>>(StringComparer.InvariantCultureIgnoreCase) {
                ["bmp"] = GetBitmapEncoder,
                ["png"] = GetPngEncoder,
                ["tga"] = GetTgaEncoder,
                ["jpg"] = GetJpegEncoder,
                ["jpeg"] = GetJpegEncoder,
                ["gif"] = GetGifEncoder,
            };
        }

        public ImageWriter(IOutputWriter writer)
        {
            this.writer = writer;
        }

        public async Task WriteAsync(Image image, ImageChannels type, string localFile, CancellationToken token)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var ext = Path.GetExtension(localFile)?.TrimStart('.');

            var encoder = GetEncoder(ext, type);
            await using var stream = writer.Open(localFile);
            await image.SaveAsync(stream, encoder, token);
        }

        private static IImageEncoder GetEncoder(string ext, ImageChannels type)
        {
            if (map.TryGetValue(ext, out var encoderFunc)) return encoderFunc(type);
            throw new ApplicationException($"Unsupported image encoding '{ext}'!");
        }

        private static BmpEncoder GetBitmapEncoder(ImageChannels type)
        {
            var hasAlpha = type == ImageChannels.ColorAlpha;

            return new BmpEncoder {
                SupportTransparency = hasAlpha,
                BitsPerPixel = hasAlpha
                    ? BmpBitsPerPixel.Pixel32
                    : BmpBitsPerPixel.Pixel24,
            };
        }

        private static PngEncoder GetPngEncoder(ImageChannels type)
        {
            return new() {
                FilterMethod = PngFilterMethod.Adaptive,
                CompressionLevel = PngCompressionLevel.BestCompression,
                BitDepth = PngBitDepth.Bit8,
                TransparentColorMode = type == ImageChannels.ColorAlpha
                    ? PngTransparentColorMode.Preserve
                    : PngTransparentColorMode.Clear,
                ColorType = type switch {
                    ImageChannels.ColorAlpha => PngColorType.RgbWithAlpha,
                    ImageChannels.Color => PngColorType.Rgb,
                    _ => PngColorType.Grayscale
                },
            };
        }

        private static TgaEncoder GetTgaEncoder(ImageChannels type)
        {
            return new() {
                Compression = TgaCompression.RunLength,
                BitsPerPixel = TgaBitsPerPixel.Pixel32,
            };
        }

        private static JpegEncoder GetJpegEncoder(ImageChannels type)
        {
            return new() {
                Subsample = JpegSubsample.Ratio444,
                Quality = 100,
            };
        }

        private static GifEncoder GetGifEncoder(ImageChannels type)
        {
            return new() {
                ColorTableMode = GifColorTableMode.Global,
            };
        }
    }
}
