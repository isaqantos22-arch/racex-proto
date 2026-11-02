using System.Collections.Generic;
using UnityEngine;

namespace RaceX.UI
{
    /// <summary>Ícones das BANDEIRAS da direção de prova (como no HUD do vídeo de referência: o ícone ao lado do texto).
    /// Desenhados no código — pano com mastro e 4 quadros de tremular (sombra das dobras + onda maior na ponta solta).
    /// Padrões das bandeiras da FIA: verde, amarela (e amarela dupla), azul, branca, amarela-e-vermelha (listras),
    /// preta, preta-e-laranja (disco laranja) e preta-e-branca (diagonal). SC e VSC usam o pano amarelo com o letreiro
    /// por cima (texto no HUD).</summary>
    public static class FlagIcons
    {
        public const int Frames = 4;
        const int W = 72, H = 50;
        static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

        static readonly Color Yellow = new Color(1f, 0.85f, 0.08f), Red = new Color(0.9f, 0.1f, 0.08f);
        static readonly Color Green = new Color(0.12f, 0.75f, 0.25f), Blue = new Color(0.25f, 0.6f, 1f);
        static readonly Color Black = new Color(0.06f, 0.06f, 0.07f), White = new Color(0.97f, 0.97f, 0.97f), Orange = new Color(1f, 0.5f, 0.05f);

        public static Sprite Get(string key, int frame)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!cache.TryGetValue(key, out var arr))
            {
                arr = new Sprite[Frames];
                for (int f = 0; f < Frames; f++) arr[f] = Build(key, f);
                cache[key] = arr;
            }
            return arr[((frame % Frames) + Frames) % Frames];
        }

        /// <summary>Cor do pano no ponto (u, v) ∈ [0,1]² do pano esticado.</summary>
        static Color Pattern(string key, float u, float v)
        {
            switch (key)
            {
                case "green": return Green;
                case "yellow": case "sc": case "vsc": return Yellow;
                case "blue": return Blue;
                case "white": return White;
                case "black": return Black;
                case "debris": return ((int)(u * 6f) % 2 == 0) ? Yellow : Red;        // listras verticais amarelo/vermelho
                case "bo":
                {
                    float dx = (u - 0.5f) * 1.45f, dy = v - 0.5f;                     // disco laranja no pano preto
                    return dx * dx + dy * dy < 0.1f ? Orange : Black;
                }
                case "bw": return u + v < 1f ? Black : White;                         // diagonal preta/branca
            }
            return White;
        }

        static Sprite Build(string key, int frame)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Flag_" + key + frame, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            float phase = frame * Mathf.PI * 0.5f;
            bool dbl = key == "yellow2";
            int flags = dbl ? 2 : 1;
            for (int fi = 0; fi < flags; fi++)
            {
                // mastro + pano; na amarela dupla, duas bandeiras lado a lado
                int x0 = dbl ? (fi == 0 ? 2 : 38) : 3;
                int poleW = 3;
                int clothW = dbl ? 30 : 64;
                int clothTop = H - 4, clothH = dbl ? 30 : 36;
                for (int y = 0; y < H; y++)
                    for (int x = x0; x < x0 + poleW; x++)
                        px[y * W + x] = new Color32(205, 205, 210, 255);
                string pat = dbl ? "yellow" : key;
                for (int x = 0; x < clothW; x++)
                {
                    float u = x / (float)(clothW - 1);
                    // onda cresce para a ponta solta; sombra pela inclinação da onda (dobras)
                    float wave = Mathf.Sin(u * Mathf.PI * 2f - phase) * (1.2f + 2.6f * u);
                    float slope = Mathf.Cos(u * Mathf.PI * 2f - phase);
                    float shade = 0.82f + 0.18f * slope;
                    int top = Mathf.RoundToInt(clothTop + wave);
                    for (int yy = 0; yy < clothH; yy++)
                    {
                        int y = top - yy;
                        int xx = x0 + poleW + x;
                        if (y < 0 || y >= H || xx >= W) continue;
                        float v = yy / (float)(clothH - 1);
                        var c = Pattern(pat, u, 1f - v) * shade;
                        px[y * W + xx] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
