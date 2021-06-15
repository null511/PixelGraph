﻿using System.Runtime.CompilerServices;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Render;
using HelixToolkit.SharpDX.Core.ShaderManager;
using HelixToolkit.SharpDX.Core.Shaders;
using HelixToolkit.SharpDX.Core.Utilities;
using PixelGraph.UI.Internal.Preview.Shaders;
using SharpDX.Direct3D11;
using PixelShader = HelixToolkit.SharpDX.Core.Shaders.PixelShader;

namespace PixelGraph.UI.Internal.Preview.Materials
{
    internal class CustomDiffuseMaterialVariable : MaterialVariable
    {
        private const int NUMTEXTURES = 2;
        private const int NUMSAMPLERS = 3;

        private const int
            DiffuseAlphaMapIdx = 0,
            EmissiveMapIdx = 1;

        private const int
            SurfaceSamplerIdx = 0,
            CubeSamplerIdx = 1,
            ShadowSamplerIdx = 2;

        private readonly CustomDiffuseMaterialCore material;

        private readonly ITextureResourceManager textureManager;
        private readonly IStatePoolManager statePoolManager;
        private readonly ShaderResourceViewProxy[] textureResources;
        private readonly SamplerStateProxy[] samplerResources;

        private int texDiffuseAlphaSlot, texEmissiveSlot, texCubeSlot, texShadowSlot;
        private int samplerSurfaceSlot, samplerCubeSlot, samplerShadowSlot;
        private uint textureIndex;

        public ShaderPass MaterialPass { private set; get; }
        public ShaderPass MaterialOITPass { private set; get; }
        public ShaderPass ShadowPass { get; }
        public ShaderPass WireframePass { get; } 
        public ShaderPass WireframeOITPass { get; }
        public ShaderPass DepthPass { get; }

        private bool HasTextures => textureIndex != 0;


        public CustomDiffuseMaterialVariable(IEffectsManager manager, IRenderTechnique technique, CustomDiffuseMaterialCore core,
            string materialPassName = CustomPassNames.Diffuse, string wireframePassName = DefaultPassNames.Wireframe,
            string materialOITPassName = CustomPassNames.DiffuseOIT, string wireframeOITPassName = DefaultPassNames.WireframeOITPass,
            string shadowPassName = DefaultPassNames.ShadowPass,
            string depthPassName = DefaultPassNames.DepthPrepass)
            : base(manager, technique, DefaultMeshConstantBufferDesc, core)
        {
            textureResources = new ShaderResourceViewProxy[NUMTEXTURES];
            samplerResources = new SamplerStateProxy[NUMSAMPLERS];

            textureManager = manager.MaterialTextureManager;
            statePoolManager = manager.StateManager;
            material = core;

            MaterialPass = technique[materialPassName];
            MaterialOITPass = technique[materialOITPassName];
            WireframePass = technique[wireframePassName];
            WireframeOITPass = technique[wireframeOITPassName];
            ShadowPass = technique[shadowPassName];
            DepthPass = technique[depthPassName];

            UpdateMappings(MaterialPass);
            CreateTextureViews();
            CreateSamplers();
        }

        protected override void OnInitialPropertyBindings()
        {
            AddPropertyBinding(nameof(CustomDiffuseMaterialCore.DiffuseAlphaMap), () => {
                CreateTextureView(material.DiffuseAlphaMap, DiffuseAlphaMapIdx);
            });

            AddPropertyBinding(nameof(CustomDiffuseMaterialCore.EmissiveMap), () => {
                CreateTextureView(material.EmissiveMap, EmissiveMapIdx);
            });

            AddPropertyBinding(nameof(CustomPbrMaterialCore.SurfaceMapSampler), () => {
                CreateSampler(material.SurfaceMapSampler, SurfaceSamplerIdx);
            });

            AddPropertyBinding(nameof(CustomPbrMaterialCore.CubeMapSampler), () => {
                CreateSampler(material.CubeMapSampler, CubeSamplerIdx);
            });

            AddPropertyBinding(nameof(CustomDiffuseMaterialCore.RenderShadowMap), () => {
                WriteValue(PhongPBRMaterialStruct.RenderShadowMapStr, material.RenderShadowMap ? 1 : 0);
            });

            AddPropertyBinding(nameof(CustomPbrMaterialCore.RenderEnvironmentMap), () => {
                WriteValue(PhongPBRMaterialStruct.HasCubeMapStr, material.RenderEnvironmentMap ? 1 : 0);
            });
        }

        public override bool BindMaterialResources(RenderContext context, DeviceContextProxy deviceContext, ShaderPass shaderPass)
        {
            if (HasTextures) {
                OnBindMaterialTextures(context, deviceContext, shaderPass.PixelShader);
            }

            if (material.RenderShadowMap && context.IsShadowMapEnabled) {
                shaderPass.PixelShader.BindTexture(deviceContext, texShadowSlot, context.SharedResource.ShadowView);
                shaderPass.PixelShader.BindSampler(deviceContext, samplerShadowSlot, samplerResources[ShadowSamplerIdx]);
            }

            if (material.RenderEnvironmentMap && material.EnvironmentCube != null) {
                shaderPass.PixelShader.BindTexture(deviceContext, texCubeSlot, material.EnvironmentCube.CubeMap);
                shaderPass.PixelShader.BindSampler(deviceContext, samplerCubeSlot, samplerResources[CubeSamplerIdx]);
            }

            return true;
        }

        public override ShaderPass GetPass(RenderType renderType, RenderContext context)
        {
            return renderType == RenderType.Transparent && context.IsOITPass
                ? MaterialOITPass : MaterialPass;
        }

        public override ShaderPass GetShadowPass(RenderType renderType, RenderContext context)
        {
            return ShadowPass;
        }

        public override ShaderPass GetWireframePass(RenderType renderType, RenderContext context)
        {
            return renderType == RenderType.Transparent && context.IsOITPass
                ? WireframeOITPass : WireframePass;
        }

        public override ShaderPass GetDepthPass(RenderType renderType, RenderContext context)
        {
            return DepthPass;
        }

        public override void Draw(DeviceContextProxy deviceContext, IAttachableBufferModel bufferModel, int instanceCount)
        {
            DrawIndexed(deviceContext, bufferModel.IndexBuffer.ElementCount, instanceCount);
        }

        private void CreateTextureViews()
        {
            if (material != null) {
                CreateTextureView(material.DiffuseAlphaMap, DiffuseAlphaMapIdx);
                CreateTextureView(material.EmissiveMap, EmissiveMapIdx);
            }
            else {
                for (var i = 0; i < NUMTEXTURES; ++i)
                    RemoveAndDispose(ref textureResources[i]);

                textureIndex = 0;
            }
        }

        private void CreateSamplers()
        {
            var newSurfaceSampler = statePoolManager.Register(material.SurfaceMapSampler);
            var newShadowSampler = statePoolManager.Register(DefaultSamplers.ShadowSampler);
            var newCubeSampler = statePoolManager.Register(material.CubeMapSampler);

            RemoveAndDispose(ref samplerResources[SurfaceSamplerIdx]);
            RemoveAndDispose(ref samplerResources[ShadowSamplerIdx]);
            RemoveAndDispose(ref samplerResources[CubeSamplerIdx]);

            if (material != null) {
                samplerResources[SurfaceSamplerIdx] = Collect(newSurfaceSampler);
                samplerResources[ShadowSamplerIdx] = Collect(newShadowSampler);
                samplerResources[CubeSamplerIdx] = Collect(newCubeSampler);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateTextureView(TextureModel texture, int index)
        {
            var newTexture = texture == null
                ? null : textureManager.Register(texture);

            RemoveAndDispose(ref textureResources[index]);
            textureResources[index] = Collect(newTexture);

            if (textureResources[index] != null) {
                textureIndex |= 1u << index;
            }
            else {
                textureIndex &= ~(1u << index);
            }
        }

        private void CreateSampler(SamplerStateDescription desc, int index)
        {
            var newRes = statePoolManager.Register(desc);
            RemoveAndDispose(ref samplerResources[index]);
            samplerResources[index] = Collect(newRes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateMappings(ShaderPass shaderPass)
        {
            texDiffuseAlphaSlot = shaderPass.PixelShader.ShaderResourceViewMapping.TryGetBindSlot(CustomBufferNames.DiffuseAlphaTB);
            texEmissiveSlot = shaderPass.PixelShader.ShaderResourceViewMapping.TryGetBindSlot(CustomBufferNames.EmissiveTB);
            texShadowSlot = shaderPass.PixelShader.ShaderResourceViewMapping.TryGetBindSlot(CustomBufferNames.ShadowMapTB);
            texCubeSlot = shaderPass.PixelShader.ShaderResourceViewMapping.TryGetBindSlot(CustomBufferNames.CubeMapTB);

            samplerSurfaceSlot = shaderPass.PixelShader.SamplerMapping.TryGetBindSlot(CustomSamplerStateNames.SurfaceSampler);
            samplerShadowSlot = shaderPass.PixelShader.SamplerMapping.TryGetBindSlot(CustomSamplerStateNames.ShadowMapSampler);
            samplerCubeSlot = shaderPass.PixelShader.SamplerMapping.TryGetBindSlot(CustomSamplerStateNames.CubeMapSampler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnBindMaterialTextures(RenderContext context, DeviceContextProxy deviceContext, PixelShader shader)
        {
            if (shader.IsNULL) return;
            
            shader.BindTexture(deviceContext, texDiffuseAlphaSlot, textureResources[DiffuseAlphaMapIdx]);
            shader.BindTexture(deviceContext, texEmissiveSlot, textureResources[EmissiveMapIdx]);

            shader.BindSampler(deviceContext, samplerSurfaceSlot, samplerResources[SurfaceSamplerIdx]);
            shader.BindSampler(deviceContext, samplerCubeSlot, samplerResources[CubeSamplerIdx]);
        }
    }
}
