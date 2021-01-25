﻿using PixelGraph.Common.ImageProcessors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;

namespace PixelGraph.Common.Textures
{
    internal class NormalMapBuilder : IDisposable
    {
        public NormalMapFilters Filter {get; set;}
        public Image<Rgba32> HeightImage {get; set;}
        public ColorChannel HeightChannel {get; set;}
        public float Strength {get; set;}
        public bool WrapX {get; set;}
        public bool WrapY {get; set;}

        public float LowFreqStrength {get; set;}
        public float LowFreqDownscale {get; set;}
        public float VarianceStrength {get; set;}
        public float VarianceBlur {get; set;}

        public Image<L8> VarianceMap {get; private set;}


        public void Dispose()
        {
            VarianceMap?.Dispose();
        }

        public Image<Rgb24> Build()
        {
            return Filter == NormalMapFilters.Variance
                ? BuildVariance() : BuildSimple();
        }

        private Image<Rgb24> BuildSimple()
        {
            var options = new NormalMapProcessor.Options {
                Source = HeightImage,
                HeightChannel = HeightChannel,
                Strength = Strength,
                Filter = Filter,
                WrapX = WrapX,
                WrapY = WrapY,
            };
            
            Image<Rgb24> resultImage = null;

            try {
                var processor = new NormalMapProcessor(options);

                resultImage = new Image<Rgb24>(Configuration.Default, HeightImage.Width, HeightImage.Height);
                resultImage.Mutate(c => c.ApplyProcessor(processor));
                return resultImage;
            }
            catch {
                resultImage?.Dispose();
                throw;
            }
        }

        private Image<Rgb24> BuildVariance()
        {
            if (LowFreqDownscale <= 1f) throw new ArgumentOutOfRangeException(nameof(LowFreqDownscale), "Variance downscale must be greater than 1!");

            var lowFreqFilterDown = KnownResamplers.Box;
            var lowFreqFilterUp = KnownResamplers.Bicubic;
            var srcWidth = HeightImage.Width;
            var srcHeight = HeightImage.Height;

            VarianceMap?.Dispose();
            VarianceMap = new Image<L8>(Configuration.Default, srcWidth, srcHeight);
            
            // Make high-freq Normal map
            var normalHighFreqOptions = new NormalMapProcessor.Options {
                Source = HeightImage,
                HeightChannel = HeightChannel,
                Strength = Strength,
                WrapX = WrapX,
                WrapY = WrapY,
            };

            var normalHighFreqProcessor = new NormalMapProcessor(normalHighFreqOptions);

            using var normalHighFreqImage = new Image<Rgb24>(Configuration.Default, srcWidth, srcHeight);
            normalHighFreqImage.Mutate(c => c.ApplyProcessor(normalHighFreqProcessor));

            // Create low-freq Height image
            var lowFreqWidth = (int)Math.Ceiling((double)srcWidth / LowFreqDownscale);
            var lowFreqHeight = (int)Math.Ceiling((double)srcHeight / LowFreqDownscale);

            using var heightLowFreqImage = HeightImage.Clone();
            heightLowFreqImage.Mutate(context => context.Resize(lowFreqWidth, lowFreqHeight, lowFreqFilterDown));

            // Make low-freq Normal map
            var normalLowFreqOptions = new NormalMapProcessor.Options {
                Source = heightLowFreqImage,
                HeightChannel = HeightChannel,
                Strength = LowFreqStrength,
                WrapX = WrapX,
                WrapY = WrapY,
            };

            var normalLowFreqProcessor = new NormalMapProcessor(normalLowFreqOptions);

            using var normalLowFreqImage = new Image<Rgb24>(Configuration.Default, lowFreqWidth, lowFreqHeight);
            normalLowFreqImage.Mutate(c => c.ApplyProcessor(normalLowFreqProcessor));

            // Upscale low-freq Height and Normal map
            heightLowFreqImage.Mutate(context => context.Resize(srcWidth, srcHeight, lowFreqFilterUp));
            normalLowFreqImage.Mutate(context => context.Resize(srcWidth, srcHeight, lowFreqFilterUp));

            // Create Height low/high Variance map
            var f = 1f / (1f - VarianceStrength + float.Epsilon);

            var varianceOptions = new HeightVarianceProcessor<Rgba32>.Options {
                HighFreqHeightImage = HeightImage,
                LowFreqHeightImage = heightLowFreqImage,
                HeightChannel = HeightChannel,
                Strength = f,
            };

            var varianceProcessor = new HeightVarianceProcessor<Rgba32>(varianceOptions);

            VarianceMap.Mutate(c => {
                c.ApplyProcessor(varianceProcessor);
                c.GaussianBlur(VarianceBlur);
            });

            // Merge high/low freq normals using variance
            var blendOptions = new NormalBlendProcessor.Options {
                HighFreqNormalImage = normalHighFreqImage,
                LowFreqNormalImage = normalLowFreqImage,
                VarianceImage = VarianceMap,
            };

            Image<Rgb24> resultImage = null;

            try {
                var blendProcessor = new NormalBlendProcessor(blendOptions);

                resultImage = new Image<Rgb24>(Configuration.Default, srcWidth, srcHeight);
                resultImage.Mutate(c => c.ApplyProcessor(blendProcessor));
                return resultImage;
            }
            catch {
                resultImage?.Dispose();
                throw;
            }
        }
    }
}