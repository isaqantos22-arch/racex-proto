using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Core
{
    public static class MaterialLibrary
    {
        static Material baseMaterial;
        static Material emissiveMaterial;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        static Material Base
        {
            get
            {
                if (baseMaterial == null) baseMaterial = Resources.Load<Material>("RaceX_Lit");
                if (baseMaterial == null)
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    baseMaterial = temp.GetComponent<Renderer>().sharedMaterial;
                    Object.DestroyImmediate(temp);
                }
                return baseMaterial;
            }
        }

        public static Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f, Color? emission = null, Texture texture = null)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            if (emission.HasValue && emissiveMaterial == null) emissiveMaterial = Resources.Load<Material>("RaceX_LitEmissive");
            m = new Material(emission.HasValue && emissiveMaterial != null ? emissiveMaterial : Base) { name = key };
            SetColor(m, color);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smoothness);
            if (m.HasProperty(Glossiness)) m.SetFloat(Glossiness, smoothness);
            if (m.HasProperty(Metallic)) m.SetFloat(Metallic, metallic);
            if (texture != null)
            {
                if (m.HasProperty(BaseMap)) m.SetTexture(BaseMap, texture);
                if (m.HasProperty(MainTex)) m.SetTexture(MainTex, texture);
            }
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, emission.Value);
            }
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        public static Material GetPbr(string key, Color albedo, float metal, float smooth, Color? emission = null)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            if (emission.HasValue && emissiveMaterial == null) emissiveMaterial = Resources.Load<Material>("RaceX_LitEmissive");
            m = new Material(emission.HasValue && emissiveMaterial != null ? emissiveMaterial : Base) { name = key };
            SetColor(m, albedo);
            if (m.HasProperty(Metallic)) m.SetFloat(Metallic, metal);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smooth);
            if (m.HasProperty(Glossiness)) m.SetFloat(Glossiness, smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, emission.Value);
            }
            m.enableInstancing = true;
            cache[key] = m;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        /// <summary>Pinta a carroceria. Em modelos importados o material de pintura pode vir transparente/opaco
        /// (ex.: Mustang) — aqui o material e trocado por uma copia opaca antes de aplicar a cor.</summary>
        static void MakeOpaque(Renderer renderer, int materialIndex)
        {
            var mats = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= mats.Length) return;
            var m = mats[materialIndex];
            if (m == null) return;
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f)
            {
                var copy = new Material(m) { name = m.name + "_Paint" };
                copy.SetFloat("_Surface", 0f);
                copy.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                copy.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                copy.SetInt("_ZWrite", 1);
                copy.renderQueue = -1;
                mats[materialIndex] = copy;
                renderer.sharedMaterials = mats;
            }
        }

        public static void ApplyPaint(Renderer renderer, Color color)
        {
            MakeOpaque(renderer, 0);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        public static void ApplyPaint(Renderer renderer, Color color, int materialIndex)
        {
            MakeOpaque(renderer, materialIndex);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            block.SetColor(BaseColor, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block, materialIndex);
        }

        public static Texture2D Checker(int cells, Color a, Color b, int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            int cell = size / cells;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, ((x / cell) + (y / cell)) % 2 == 0 ? a : b);
            tex.Apply();
            return tex;
        }
    }
}
