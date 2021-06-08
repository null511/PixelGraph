﻿using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Shaders;
using HelixToolkit.Wpf.SharpDX;
using SharpDX.Direct3D11;
using System.Windows;

namespace PixelGraph.UI.Internal.Preview.Scene
{
    public class CustomPbrMaterial : Material
    {
        public TextureModel AlbedoAlphaMap {
            get => (TextureModel)GetValue(AlbedoAlphaMapProperty);
            set => SetValue(AlbedoAlphaMapProperty, value);
        }

        public TextureModel NormalHeightMap {
            get => (TextureModel)GetValue(NormalHeightMapProperty);
            set => SetValue(NormalHeightMapProperty, value);
        }

        public TextureModel RoughF0OcclusionMap {
            get => (TextureModel)GetValue(RoughF0OcclusionMapProperty);
            set => SetValue(RoughF0OcclusionMapProperty, value);
        }

        public TextureModel PorositySssEmissiveMap {
            get => (TextureModel)GetValue(PorositySssEmissiveMapProperty);
            set => SetValue(PorositySssEmissiveMapProperty, value);
        }

        public bool RenderShadowMap {
            get => (bool)GetValue(RenderShadowMapProperty);
            set => SetValue(RenderShadowMapProperty, value);
        }

        public bool RenderEnvironmentMap {
            get => (bool)GetValue(RenderEnvironmentMapProperty);
            set => SetValue(RenderEnvironmentMapProperty, value);
        }

        public SamplerStateDescription SurfaceMapSampler {
            get => (SamplerStateDescription)GetValue(SurfaceMapSamplerProperty);
            set => SetValue(SurfaceMapSamplerProperty, value);
        }

        public string MaterialPassName {get; set;}


        public CustomPbrMaterial() {}

        public CustomPbrMaterial(CustomPbrMaterialCore core) : base(core)
        {
            AlbedoAlphaMap = core.AlbedoAlphaMap;
            NormalHeightMap = core.NormalHeightMap;
            RoughF0OcclusionMap = core.RoughF0OcclusionMap;
            PorositySssEmissiveMap = core.PorositySssEmissiveMap;
            SurfaceMapSampler = core.SurfaceMapSampler;

            RenderEnvironmentMap = core.RenderEnvironmentMap;
            RenderShadowMap = core.RenderShadowMap;
        }

        public virtual CustomPbrMaterial CloneMaterial()
        {
            return new() {
                AlbedoAlphaMap = AlbedoAlphaMap,
                NormalHeightMap = NormalHeightMap,
                RoughF0OcclusionMap = RoughF0OcclusionMap,
                PorositySssEmissiveMap = PorositySssEmissiveMap,
                SurfaceMapSampler = SurfaceMapSampler,
                RenderEnvironmentMap = RenderEnvironmentMap,
                RenderShadowMap = RenderShadowMap,
            };
        }

        protected override MaterialCore OnCreateCore()
        {
            return new CustomPbrMaterialCore(MaterialPassName) {
                Name = Name,
                AlbedoAlphaMap = AlbedoAlphaMap,
                NormalHeightMap = NormalHeightMap,
                RoughF0OcclusionMap = RoughF0OcclusionMap,
                PorositySssEmissiveMap = PorositySssEmissiveMap,
                SurfaceMapSampler = SurfaceMapSampler,
                RenderEnvironmentMap = RenderEnvironmentMap,
                RenderShadowMap = RenderShadowMap,
            };
        }

        protected override Freezable CreateInstanceCore()
        {
            return CloneMaterial();
        }

        public static readonly DependencyProperty AlbedoAlphaMapProperty =
            DependencyProperty.Register(nameof(AlbedoAlphaMap), typeof(TextureModel), typeof(CustomPbrMaterial), new PropertyMetadata(null, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).AlbedoAlphaMap = e.NewValue as TextureModel;
            }));

        public static readonly DependencyProperty NormalHeightMapProperty =
            DependencyProperty.Register(nameof(NormalHeightMap), typeof(TextureModel), typeof(CustomPbrMaterial), new PropertyMetadata(null, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).NormalHeightMap = e.NewValue as TextureModel;
            }));

        public static readonly DependencyProperty RoughF0OcclusionMapProperty =
            DependencyProperty.Register(nameof(RoughF0OcclusionMap), typeof(TextureModel), typeof(CustomPbrMaterial), new PropertyMetadata(null, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).RoughF0OcclusionMap = e.NewValue as TextureModel;
            }));

        public static readonly DependencyProperty PorositySssEmissiveMapProperty =
            DependencyProperty.Register(nameof(PorositySssEmissiveMap), typeof(TextureModel), typeof(CustomPbrMaterial), new PropertyMetadata(null, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).PorositySssEmissiveMap = e.NewValue as TextureModel;
            }));

        public static readonly DependencyProperty RenderShadowMapProperty =
            DependencyProperty.Register(nameof(RenderShadowMap), typeof(bool), typeof(CustomPbrMaterial), new PropertyMetadata(false, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).RenderShadowMap = (bool)e.NewValue;
            }));

        public static readonly DependencyProperty RenderEnvironmentMapProperty =
            DependencyProperty.Register(nameof(RenderEnvironmentMap), typeof(bool), typeof(CustomPbrMaterial), new PropertyMetadata(false, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).RenderEnvironmentMap = (bool)e.NewValue;
            }));

        public static readonly DependencyProperty SurfaceMapSamplerProperty =
            DependencyProperty.Register(nameof(SurfaceMapSampler), typeof(SamplerStateDescription), typeof(CustomPbrMaterial), new PropertyMetadata(DefaultSamplers.LinearSamplerWrapAni4, (d, e) => {
                ((CustomPbrMaterialCore)((Material)d).Core).SurfaceMapSampler = (SamplerStateDescription)e.NewValue;
            }));
    }
}