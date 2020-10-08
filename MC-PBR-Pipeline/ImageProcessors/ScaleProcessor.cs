﻿using McPbrPipeline.Internal.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;
using System;

namespace McPbrPipeline.ImageProcessors
{
    internal class ScaleProcessor : IImageProcessor
    {
        private readonly Options options;


        public ScaleProcessor(Options options)
        {
            this.options = options;
        }

        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle) where TPixel : unmanaged, IPixel<TPixel>
        {
            return new Processor<TPixel>(configuration, source, sourceRectangle, options);
        }

        public class Options
        {
            public float Red = 1f;
            public float Green = 1f;
            public float Blue = 1f;
            public float Alpha = 1f;


            public bool Any {
                get {
                    if (Math.Abs(Red - 1) > float.Epsilon) return true;
                    if (Math.Abs(Green - 1) > float.Epsilon) return true;
                    if (Math.Abs(Blue - 1) > float.Epsilon) return true;
                    if (Math.Abs(Alpha - 1) > float.Epsilon) return true;
                    return false;
                }
            }
        }

        private class Processor<TPixel> : ImageProcessor<TPixel> where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly Options options;


            public Processor(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle, Options options)
                : base(configuration, source, sourceRectangle)
            {
                this.options = options;
            }

            protected override void OnFrameApply(ImageFrame<TPixel> source)
            {
                var operation = new RowOperation<TPixel>(source, options);
                ParallelRowIterator.IterateRows(Configuration, SourceRectangle, in operation);
            }
        }

        private readonly struct RowOperation<TPixel> : IRowOperation where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly ImageFrame<TPixel> sourceFrame;
            private readonly Options options;


            public RowOperation(
                ImageFrame<TPixel> sourceFrame,
                in Options options)
            {
                this.sourceFrame = sourceFrame;
                this.options = options;
            }

            public void Invoke(int y)
            {
                var row = sourceFrame.GetPixelRowSpan(y);

                var pixel = new Rgba32();
                for (var x = 0; x < sourceFrame.Width; x++) {
                    row[x].ToRgba32(ref pixel);

                    pixel.R = Filter(in pixel.R, in options.Red);
                    pixel.G = Filter(in pixel.G, in options.Green);
                    pixel.B = Filter(in pixel.B, in options.Blue);
                    pixel.A = Filter(in pixel.A, in options.Alpha);

                    row[x].FromRgba32(pixel);
                }
            }

            private static byte Filter(in byte value, in float scale)
            {
                return MathEx.Saturate(value / 255f * scale);
            }
        }
    }
}
