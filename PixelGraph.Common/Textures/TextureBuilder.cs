﻿using PixelGraph.Common.Encoding;
using PixelGraph.Common.Extensions;
using PixelGraph.Common.ImageProcessors;
using PixelGraph.Common.IO;
using PixelGraph.Common.ResourcePack;
using PixelGraph.Common.Samplers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelGraph.Common.Textures
{
    public interface ITextureBuilder
    {
        ResourcePackChannelProperties[] InputChannels {get; set;}
        ResourcePackChannelProperties[] OutputChannels {get; set;}
        bool HasMappedSources {get;}
        int FrameCount {get;}
        int? TargetFrame {get; set;}

        Task MapAsync(bool createEmpty, CancellationToken token = default);
        Task<Image<TPixel>> BuildAsync<TPixel>(bool createEmpty, CancellationToken token = default) where TPixel : unmanaged, IPixel<TPixel>;
    }

    internal class TextureBuilder : ITextureBuilder
    {
        private readonly IInputReader reader;
        private readonly ITextureGraphContext context;
        private readonly ITextureRegionEnumerator regions;
        private readonly ITextureSourceGraph sourceGraph;
        private readonly ITextureNormalGraph normalGraph;
        private readonly ITextureOcclusionGraph occlusionGraph;
        private readonly List<TextureChannelMapping> mappings;
        private Rgba32 defaultValues;
        private Size bufferSize;
        private bool isGrayscale;

        public ResourcePackChannelProperties[] InputChannels {get; set;}
        public ResourcePackChannelProperties[] OutputChannels {get; set;}
        public bool HasMappedSources {get; private set;}
        public int FrameCount {get; private set;}
        public int? TargetFrame {get; set;}


        public TextureBuilder(
            IInputReader reader,
            ITextureGraphContext context,
            ITextureRegionEnumerator regions,
            ITextureSourceGraph sourceGraph,
            ITextureNormalGraph normalGraph,
            ITextureOcclusionGraph occlusionGraph)
        {
            this.reader = reader;
            this.context = context;
            this.regions = regions;
            this.sourceGraph = sourceGraph;
            this.normalGraph = normalGraph;
            this.occlusionGraph = occlusionGraph;

            mappings = new List<TextureChannelMapping>();
            defaultValues = new Rgba32();
        }

        public async Task MapAsync(bool createEmpty, CancellationToken token = default)
        {
            defaultValues.R = defaultValues.G = defaultValues.B = defaultValues.A = 0;
            mappings.Clear();

            HasMappedSources = false;
            FrameCount = 1;

            foreach (var channel in OutputChannels) {
                if (channel.Color == ColorChannel.Magnitude) continue;

                if (TryBuildMapping(channel, out var mapping) || createEmpty) {
                    mappings.Add(mapping);

                    if (mapping.SourceFilename != null) {
                        var info = await sourceGraph.GetOrCreateAsync(mapping.SourceFilename, token);
                        if (info != null) {
                            HasMappedSources = true;
                            if (info.FrameCount > FrameCount)
                                FrameCount = info.FrameCount;
                        }
                    }

                    if (TextureTags.Is(mapping.SourceTag, TextureTags.NormalGenerated)) {
                        HasMappedSources = true;
                        if (normalGraph.FrameCount > FrameCount)
                            FrameCount = normalGraph.FrameCount;
                    }

                    if (TextureTags.Is(mapping.SourceTag, TextureTags.OcclusionGenerated)) {
                        HasMappedSources = true;
                        if (occlusionGraph.FrameCount > FrameCount)
                            FrameCount = occlusionGraph.FrameCount;
                    }

                    ApplyDefaultValue(mapping);
                }
            }
        }
        
        public async Task<Image<TPixel>> BuildAsync<TPixel>(bool createEmpty, CancellationToken token = default)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (!createEmpty && mappings.Count == 0) return null;

            isGrayscale = mappings.All(x => x.OutputColor == ColorChannel.Red);
            if (isGrayscale) defaultValues.B = defaultValues.G = defaultValues.R;

            bufferSize = await GetBufferSizeAsync(token);
            var width = bufferSize.Width;
            var height = bufferSize.Height;

            if (!TargetFrame.HasValue && context.MaxFrameCount > 1)
                height *= context.MaxFrameCount;

            var pixel = new TPixel();
            pixel.FromRgba32(defaultValues);
            var imageResult = new Image<TPixel>(Configuration.Default, width, height, pixel);

            var mappingsWithSources = mappings
                .Where(m => m.SourceFilename != null)
                .GroupBy(m => m.SourceFilename);

            var mappingsWithoutSources = mappings
                .Where(m => m.SourceFilename == null);

            var mappingsWithOutputOcclusion = mappings
                .Where(m => m.OutputApplyOcclusion).ToArray();

            try {
                foreach (var mappingGroup in mappingsWithSources)
                    await ApplySourceMappingAsync(imageResult, mappingGroup.Key, mappingGroup.ToArray(), token);

                foreach (var mapping in mappingsWithoutSources) {
                    if (TextureTags.Is(mapping.SourceTag, TextureTags.NormalGenerated))
                        ApplyNormalMapping(imageResult, mapping);

                    if (TextureTags.Is(mapping.SourceTag, TextureTags.OcclusionGenerated))
                        await ApplyOcclusionMappingAsync(imageResult, mapping, token);
                }

                if (mappingsWithOutputOcclusion.Any())
                    await ApplyOutputOcclusionAsync(imageResult, mappingsWithOutputOcclusion, token);

                return imageResult;
            }
            catch {
                imageResult.Dispose();
                throw;
            }
        }

        private bool TryBuildMapping(ResourcePackChannelProperties outputChannel, out TextureChannelMapping mapping)
        {
            var samplerName = outputChannel.Sampler ?? context.DefaultSampler;

            mapping = new TextureChannelMapping {
                OutputColor = outputChannel.Color ?? ColorChannel.None,
                OutputMinValue = (float?)outputChannel.MinValue ?? 0f,
                OutputMaxValue = (float?)outputChannel.MaxValue ?? 1f,
                OutputRangeMin = outputChannel.RangeMin ?? 0,
                OutputRangeMax = outputChannel.RangeMax ?? 255,
                OutputShift = outputChannel.Shift ?? 0,
                OutputPower = (float?)outputChannel.Power ?? 1,
                OutputInverted = outputChannel.Invert ?? false,
                OutputSampler = samplerName,

                ValueShift = context.Material.GetChannelShift(outputChannel.ID),
                ValueScale = context.Material.GetChannelScale(outputChannel.ID),
            };

            var inputChannel = InputChannels.FirstOrDefault(i
                => EncodingChannel.Is(i.ID, outputChannel.ID));

            if (context.Material.TryGetChannelValue(outputChannel.ID, out var value)) {
                mapping.InputValue = (float)value;
                mapping.ApplyInputChannel(inputChannel);
                return true;
            }

            if (inputChannel?.Texture != null) {
                if (TextureTags.Is(inputChannel.Texture, TextureTags.NormalGenerated)) {
                    mapping.SourceTag = TextureTags.NormalGenerated;
                    mapping.ApplyInputChannel(inputChannel);
                    return true;
                }

                if (TryGetSourceFilename(inputChannel.Texture, out mapping.SourceFilename)) {
                    mapping.ApplyInputChannel(inputChannel);
                    return true;
                }

                if (context.AutoGenerateOcclusion && TextureTags.Is(inputChannel.Texture, TextureTags.Occlusion)) {
                    mapping.SourceTag = TextureTags.OcclusionGenerated;
                    mapping.InputColor = ColorChannel.Red;
                    mapping.InputMinValue = 0f;
                    mapping.InputMaxValue = 1f;
                    mapping.InputRangeMin = 0;
                    mapping.InputRangeMax = 255;
                    mapping.InputShift = 0;
                    mapping.InputPower = 1;
                    mapping.InputInverted = true;
                    return true;
                }
            }

            var isOutputDiffuseRed = EncodingChannel.Is(outputChannel.ID, EncodingChannel.DiffuseRed);
            var isOutputDiffuseGreen = EncodingChannel.Is(outputChannel.ID, EncodingChannel.DiffuseGreen);
            var isOutputDiffuseBlue = EncodingChannel.Is(outputChannel.ID, EncodingChannel.DiffuseBlue);
            var isOutputSmooth = EncodingChannel.Is(outputChannel.ID, EncodingChannel.Smooth);
            var isOutputRough = EncodingChannel.Is(outputChannel.ID, EncodingChannel.Rough);

            // Albedo Red > Diffuse Red
            if (isOutputDiffuseRed && TryGetInputChannel(EncodingChannel.AlbedoRed, out var albedoRedChannel)) {
                TryGetSourceFilename(albedoRedChannel.Texture, out mapping.SourceFilename);
                if (context.Material.TryGetChannelValue(EncodingChannel.AlbedoRed, out value)) mapping.InputValue = (float)value;

                mapping.ApplyInputChannel(albedoRedChannel);
                mapping.OutputApplyOcclusion = true;
                return true;
            }

            // Albedo Green > Diffuse Green
            if (isOutputDiffuseGreen && TryGetInputChannel(EncodingChannel.AlbedoGreen, out var albedoGreenChannel)) {
                TryGetSourceFilename(albedoGreenChannel.Texture, out mapping.SourceFilename);
                if (context.Material.TryGetChannelValue(EncodingChannel.AlbedoGreen, out value)) mapping.InputValue = (float)value;

                mapping.ApplyInputChannel(albedoGreenChannel);
                mapping.OutputApplyOcclusion = true;
                return true;
            }

            // Albedo Blue > Diffuse Blue
            if (isOutputDiffuseBlue && TryGetInputChannel(EncodingChannel.AlbedoBlue, out var albedoBlueChannel)) {
                TryGetSourceFilename(albedoBlueChannel.Texture, out mapping.SourceFilename);
                if (context.Material.TryGetChannelValue(EncodingChannel.AlbedoBlue, out value)) mapping.InputValue = (float)value;

                mapping.ApplyInputChannel(albedoBlueChannel);
                mapping.OutputApplyOcclusion = true;
                return true;
            }

            // Rough > Smooth
            if (isOutputSmooth && TryGetInputChannel(EncodingChannel.Rough, out var roughChannel)
                               && TryGetSourceFilename(roughChannel.Texture, out mapping.SourceFilename)) {
                mapping.ApplyInputChannel(roughChannel);
                mapping.InputInverted = true;
                return true;
            }

            // Smooth > Rough
            if (isOutputRough && TryGetInputChannel(EncodingChannel.Smooth, out var smoothChannel)) {
                if (TryGetSourceFilename(smoothChannel.Texture, out mapping.SourceFilename)) {
                    mapping.ApplyInputChannel(smoothChannel);
                    mapping.InputInverted = true;
                    return true;
                }
            }

            //var isOutputF0 = EncodingChannel.Is(outputChannel.ID, EncodingChannel.F0);
            //var isOutputMetal = EncodingChannel.Is(outputChannel.ID, EncodingChannel.Metal);

            //// Metal > F0
            //if (isOutputF0 && TryGetInputChannel(EncodingChannel.Metal, out var metalChannel)) {
            //    if (TryGetSourceFilename(metalChannel.Texture, out mapping.SourceFilename)) {
            //        mapping.ApplyInputChannel(metalChannel);
            //        //mapping.Threshold = 0.5f;
            //        mapping.IsMetalToF0 = true;
            //        return true;
            //    }
            //}

            //// F0 > Metal
            //if (isOutputMetal && TryGetInputChannel(EncodingChannel.F0, out var f0Channel)) {
            //    if (TryGetSourceFilename(f0Channel.Texture, out mapping.SourceFilename)) {
            //        mapping.ApplyInputChannel(f0Channel);
            //        //mapping.Threshold = 0.5f;
            //        mapping.IsF0ToMetal = true;
            //        return true;
            //    }
            //}

            return false;
        }

        private bool TryGetInputChannel(string id, out ResourcePackChannelProperties channel)
        {
            channel = InputChannels.FirstOrDefault(i => EncodingChannel.Is(i.ID, id));
            return channel != null;
        }

        private void ApplyNormalMapping<TPixel>(Image<TPixel> image, TextureChannelMapping mapping)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var sampler = normalGraph.GetSampler();
            sampler.RangeX = (float)normalGraph.Texture.Width / bufferSize.Width;
            sampler.RangeY = (float)normalGraph.Texture.Height / bufferSize.Height;

            var options = new OverlayProcessor<Rgb24>.Options {
                IsGrayscale = isGrayscale,
                SamplerMap = new Dictionary<TextureChannelMapping, ISampler<Rgb24>> {
                    [mapping] = sampler,
                },
            };

            var processor = new OverlayProcessor<Rgb24>(options);

            foreach (var frame in regions.GetAllRenderRegions(TargetFrame, context.MaxFrameCount)) {
                var srcFrame = regions.GetRenderRegion(frame.Index, normalGraph.FrameCount);

                foreach (var tile in frame.Tiles) {
                    sampler.Bounds = srcFrame.Tiles[tile.Index].Bounds;

                    var outBounds = GetOutBounds(tile, frame).ScaleTo(image.Width, image.Height);
                    image.Mutate(c => c.ApplyProcessor(processor, outBounds));
                }
            }
        }

        private async Task ApplyOcclusionMappingAsync<TPixel>(Image<TPixel> image, TextureChannelMapping mapping, CancellationToken token)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var occlusionSampler = await occlusionGraph.GetSamplerAsync(token);
            if (occlusionSampler == null) return;

            var options = new OverlayProcessor<Rgba32>.Options {
                IsGrayscale = isGrayscale,
                SamplerMap = new Dictionary<TextureChannelMapping, ISampler<Rgba32>> {
                    [mapping] = occlusionSampler,
                },
            };
            
            var processor = new OverlayProcessor<Rgba32>(options);

            foreach (var frame in regions.GetAllRenderRegions(TargetFrame, context.MaxFrameCount)) {
                var srcFrame = regions.GetRenderRegion(frame.Index, occlusionGraph.FrameCount);

                foreach (var tile in frame.Tiles) {
                    occlusionSampler.Bounds = srcFrame.Tiles[tile.Index].Bounds;

                    var outBounds = GetOutBounds(tile, frame).ScaleTo(image.Width, image.Height);
                    image.Mutate(c => c.ApplyProcessor(processor, outBounds));
                }
            }
        }

        private void ApplyDefaultValue(TextureChannelMapping mapping)
        {
            if (!mapping.InputValue.HasValue && mapping.OutputShift == 0 && !mapping.OutputInverted) return;

            var value = mapping.InputValue ?? 0f;
            if (value < mapping.InputMinValue || value > mapping.InputMaxValue) return;

            // Common Processing
            value = (value + mapping.ValueShift) * mapping.ValueScale;

            if (!mapping.OutputPower.Equal(1f))
                value = MathF.Pow(value, mapping.OutputPower);

            if (mapping.OutputInverted) MathEx.Invert(ref value, mapping.OutputMinValue, mapping.OutputMaxValue);

            MathEx.Clamp(ref value, mapping.OutputMinValue, mapping.OutputMaxValue);

            var valueRange = mapping.OutputMaxValue - mapping.OutputMinValue;
            var pixelRange = mapping.OutputRangeMax - mapping.OutputRangeMin;
            var outputScale = pixelRange / valueRange;

            var valueOut = mapping.OutputRangeMin + (value - mapping.OutputMinValue) * outputScale;
            var finalValue = MathEx.ClampRound(valueOut, mapping.OutputRangeMin, mapping.OutputRangeMax);

            if (mapping.OutputShift != 0)
                MathEx.Cycle(ref finalValue, in mapping.OutputShift, in mapping.OutputRangeMin, in mapping.OutputRangeMax);

            if (isGrayscale) {
                defaultValues.R = finalValue;
                defaultValues.G = finalValue;
                defaultValues.B = finalValue;
            }
            else {
                defaultValues.SetChannelValue(in mapping.OutputColor, in finalValue);
            }
        }

        private async Task ApplySourceMappingAsync<TPixel>(Image<TPixel> image, string sourceFilename, TextureChannelMapping[] mappingGroup, CancellationToken token)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (sourceFilename == null) throw new ArgumentNullException(nameof(sourceFilename));

            var info = await sourceGraph.GetOrCreateAsync(sourceFilename, token);
            if (info == null) return;

            await using var sourceStream = reader.Open(sourceFilename);
            using var sourceImage = await Image.LoadAsync<Rgba32>(Configuration.Default, sourceStream, token);

            var options = new OverlayProcessor<Rgba32>.Options {
                IsGrayscale = isGrayscale,
                SamplerMap = mappingGroup.ToDictionary(m => m, m => {
                    var samplerName = m.OutputSampler ?? context.DefaultSampler;
                    var sampler = Sampler<Rgba32>.Create(samplerName);
                    sampler.Image = sourceImage;
                    sampler.WrapX = context.MaterialWrapX;
                    sampler.WrapY = context.MaterialWrapY;
                    sampler.RangeX = (float)sourceImage.Width / bufferSize.Width;
                    sampler.RangeY = (float)sourceImage.Height / bufferSize.Height;
                    return sampler;
                }),
            };

            var processor = new OverlayProcessor<Rgba32>(options);

            foreach (var frame in regions.GetAllRenderRegions(TargetFrame, context.MaxFrameCount)) {
                var srcFrame = regions.GetRenderRegion(frame.Index, info.FrameCount);

                foreach (var tile in frame.Tiles) {
                    foreach (var sampler in options.SamplerMap.Values)
                        sampler.Bounds = srcFrame.Tiles[tile.Index].Bounds;

                    var outBounds = GetOutBounds(tile, frame).ScaleTo(image.Width, image.Height);
                    image.Mutate(c => c.ApplyProcessor(processor, outBounds));
                }
            }
        }

        private async Task ApplyOutputOcclusionAsync<TPixel>(Image<TPixel> image, IEnumerable<TextureChannelMapping> mappingGroup, CancellationToken token)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var occlusionSampler = await occlusionGraph.GetSamplerAsync(token);
            if (occlusionSampler == null) return;

            var options = new PostOcclusionProcessor<Rgba32, Rgba32>.Options {
                MappingColors = mappingGroup.Select(m => m.OutputColor).ToArray(),
                //OcclusionColor = occlusionGraph.Channel.Color ?? ColorChannel.Red,
                OcclusionSampler = occlusionSampler,
                OcclusionMapping = new TextureChannelMapping(),
            };

            options.OcclusionMapping.ApplyInputChannel(occlusionGraph.Channel);

            Image<Rgba32> emissiveImage = null;
            TextureSource emissiveInfo = null;
            ISampler<Rgba32> emissiveSampler = null;
            try {
                if (TryGetInputChannel(EncodingChannel.Emissive, out var emissiveChannel)
                    && TryGetSourceFilename(emissiveChannel.Texture, out var emissiveFile)) {
                    emissiveInfo = await sourceGraph.GetOrCreateAsync(emissiveFile, token);
                    if (emissiveInfo != null) {
                        await using var stream = reader.Open(emissiveFile);
                        emissiveImage = await Image.LoadAsync<Rgba32>(Configuration.Default, stream, token);

                        var samplerName = context.Profile?.Encoding?.Emissive?.Sampler ?? context.DefaultSampler;
                        options.EmissiveSampler = emissiveSampler = Sampler<Rgba32>.Create(samplerName);
                        emissiveSampler.Image = emissiveImage;
                        emissiveSampler.WrapX = context.MaterialWrapX;
                        emissiveSampler.WrapY = context.MaterialWrapY;

                        // TODO: set these properly
                        emissiveSampler.RangeX = 1;
                        emissiveSampler.RangeY = 1;

                        options.EmissiveMapping = new TextureChannelMapping();
                        options.EmissiveMapping.ApplyInputChannel(emissiveChannel);

                        if (context.Material.TryGetChannelValue(EncodingChannel.Emissive, out var value))
                            options.EmissiveMapping.InputValue = (float)value;
                    }
                }

                var processor = new PostOcclusionProcessor<Rgba32, Rgba32>(options);

                foreach (var frame in regions.GetAllRenderRegions(TargetFrame, context.MaxFrameCount)) {
                    var occlusionFrame = regions.GetRenderRegion(frame.Index, occlusionGraph.FrameCount);
                    var emissiveFrame = regions.GetRenderRegion(frame.Index, emissiveInfo?.FrameCount ?? 1);

                    foreach (var tile in frame.Tiles) {
                        occlusionSampler.Bounds = occlusionFrame.Tiles[tile.Index].Bounds;

                        if (emissiveSampler != null)
                            emissiveSampler.Bounds = emissiveFrame.Tiles[tile.Index].Bounds;

                        var outBounds = GetOutBounds(tile, frame).ScaleTo(image.Width, image.Height);
                        image.Mutate(c => c.ApplyProcessor(processor, outBounds));
                    }
                }
            }
            finally {
                emissiveImage?.Dispose();
            }
        }

        private RectangleF GetOutBounds(TextureRenderTile tile, TextureRenderFrame frame)
        {
            var frameBounds = tile.Bounds;
            if (TargetFrame.HasValue) {
                frameBounds.Offset(frame.Bounds.X, frame.Bounds.Y);
                frameBounds.Height *= FrameCount;
            }
            return frameBounds;
        }

        private async Task<Size> GetBufferSizeAsync(CancellationToken token = default)
        {
            var scale = context.TextureScale;
            var blockSize = context.Profile?.BlockTextureSize;

            // Use multi-part bounds if defined
            if (context.Material.TryGetSourceBounds(in blockSize, in scale, out var partBounds)) {
                //if (scale.Equal(1f)) return partBounds;

                //var width = (int)MathF.Ceiling(partBounds.Width * scale);
                //var height = (int)MathF.Ceiling(partBounds.Height * scale);
                return partBounds;
            }

            var actualBounds = await GetActualBoundsAsync(token);

            float? aspect = null;
            if (actualBounds.HasValue) aspect = (float)actualBounds.Value.Height / actualBounds.Value.Width;

            var textureSize = context.GetTextureSize(aspect);

            // Use texture-size
            if (textureSize.HasValue)
                return textureSize.Value;

            if (actualBounds.HasValue) {
                var size = new Size(actualBounds.Value.Width, actualBounds.Value.Height);

                if (scale.HasValue) {
                    size.Width = (int)MathF.Ceiling(size.Width * scale.Value);
                    size.Height = (int)MathF.Ceiling(size.Height * scale.Value);
                }

                return size;
            }

            return new Size(1);
        }

        private async Task<Size?> GetActualBoundsAsync(CancellationToken token = default)
        {
            var hasBounds = false;
            var maxWidth = 1;
            var maxHeight = 1;

            foreach (var mappingGroup in mappings.GroupBy(m => m.SourceFilename)) {
                if (mappingGroup.Key == null) continue;
                if (!sourceGraph.TryGet(mappingGroup.Key, out var source)) continue;

                if (!hasBounds) {
                    maxWidth = source.Width;
                    maxHeight = source.Height;
                    hasBounds = true;
                    continue;
                }

                if (source.Width != maxWidth) {
                    var scale = (float)source.Width / maxWidth;
                    var scaledWidth = (int)MathF.Ceiling(source.Width * scale);
                    var scaledHeight = (int)MathF.Ceiling(source.Height * scale);

                    if (scaledWidth > maxWidth || scaledHeight > maxHeight) {
                        maxWidth = scaledWidth;
                        maxHeight = scaledHeight;
                    }
                }
                else {
                    if (source.Height > maxHeight) {
                        maxHeight = source.Height;
                    }
                }
            }

            foreach (var mapping in mappings.Where(m => m.SourceFilename == null)) {
                if (TextureTags.Is(mapping.SourceTag, TextureTags.NormalGenerated)) {
                    if (!hasBounds) {
                        maxWidth = normalGraph.FrameWidth;
                        maxHeight = normalGraph.FrameHeight;
                        hasBounds = true;
                        continue;
                    }

                    if (normalGraph.FrameWidth == maxWidth && normalGraph.FrameHeight == maxHeight) continue;

                    if (normalGraph.FrameWidth >= maxWidth) {
                        maxWidth = normalGraph.FrameWidth;

                        if (normalGraph.FrameHeight > maxHeight)
                            maxHeight = normalGraph.FrameHeight;
                    }
                    else {
                        var scale = (float)maxWidth / normalGraph.FrameWidth;
                        var scaledHeight = (int)MathF.Ceiling(normalGraph.FrameHeight * scale);

                        if (scaledHeight > maxHeight)
                            maxHeight = scaledHeight;
                    }
                }

                if (TextureTags.Is(mapping.SourceTag, TextureTags.OcclusionGenerated)) {
                    var occlusionTex = await occlusionGraph.GetTextureAsync(token);

                    if (occlusionTex != null) {
                        if (!hasBounds) {
                            maxWidth = occlusionGraph.FrameWidth;
                            maxHeight = occlusionGraph.FrameHeight;
                            hasBounds = true;
                            continue;
                        }

                        if (occlusionGraph.FrameWidth == maxWidth && occlusionGraph.FrameHeight == maxHeight) continue;

                        if (occlusionGraph.FrameWidth >= maxWidth) {
                            maxWidth = occlusionGraph.FrameWidth;

                            if (occlusionGraph.FrameHeight > maxHeight)
                                maxHeight = occlusionGraph.FrameHeight;
                        }
                        else {
                            var scale = (float)maxWidth / occlusionGraph.FrameWidth;
                            var scaledHeight = (int)MathF.Ceiling(occlusionGraph.FrameHeight * scale);

                            if (scaledHeight > maxHeight)
                                maxHeight = scaledHeight;
                        }
                    }
                }
            }

            if (!hasBounds) return null;
            return new Size(maxWidth, maxHeight);
        }

        private bool TryGetSourceFilename(string tag, out string filename)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            foreach (var file in reader.EnumerateTextures(context.Material, tag)) {
                if (!reader.FileExists(file)) continue;

                filename = file;
                return true;
            }

            filename = null;
            return false;
        }
    }
}
