using System.Collections.Generic;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Sujeira PASSAGEIRA nos pneus depois de a roda passar pela grama/terra (não é característica do pneu).
    /// Medido no vídeo de referência (pit stop F1 2012, câmera T-cam, pneu dianteiro):
    ///  - pneu limpo: borracha quase preta (RGB ~5/4/6), 0% de manchas claras;
    ///  - pneu sujo: manchas de terra RGB ~92/82/72 (desvio ~24) cobrindo em média ~18% do pneu (10-30% por quadro);
    ///  - a sujeira fica visível do começo do vídeo até a troca no box (~23 s rodando), sem sumir.
    /// No jogo: cada roda que toca grama/terra suja; fica cheia por 20 s depois da última passagem e some em 4 s
    /// (≈24 s, o que o vídeo mostra); pneus novos no box saem limpos.
    /// Aderência (pedido do usuário, 30/10): pneu sujo perde um pouco e recupera aos poucos ao longo dos 24 s — perda
    /// moderada, nunca de patinar. Não achei número confiável dessa perda (nem em F1 real nem nos jogos): GripLossMax
    /// é valor de projeto, ajustável.</summary>
    public class TyreDirt : MonoBehaviour
    {
        const float FullTime = 20f, FadeTime = 4f;
        /// <summary>Perda máxima de aderência logo ao sair da grama com o pneu todo sujo (valor de projeto, sem fonte).</summary>
        const float GripLossMax = 0.08f;
        const float GripRecoverTime = 24f;   // volta ao normal em linha reta, no mesmo tempo da sujeira do vídeo
        /// <summary>Tempo na grama até a sujeira máxima. Escolha de projeto: o vídeo começa com o pneu já sujo, então não
        /// mostra a velocidade com que a terra gruda.</summary>
        const float PickupTime = 0.5f;
        static readonly float[] StageCoverage = { 0.05f, 0.10f, 0.18f };   // leve, média, como no vídeo
        static Texture2D[] stageTex;
        static MaterialPropertyBlock mpb;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        CarController car;
        readonly List<Renderer>[] tyres = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        readonly float[] level = new float[4];
        readonly float[] sinceDirt = { 999f, 999f, 999f, 999f };
        readonly int[] shown = { -1, -1, -1, -1 };
        float lastWear = -1f;

        public static void Attach(CarController car, Material tireMat)
        {
            if (car == null || car.wheelMeshes == null || tireMat == null) return;
            var d = car.gameObject.AddComponent<TyreDirt>();
            d.car = car;
            for (int i = 0; i < 4 && i < car.wheelMeshes.Length; i++)
            {
                var pivot = car.wheelMeshes[i];
                if (pivot == null) continue;
                foreach (var r in pivot.GetComponentsInChildren<Renderer>(true))
                    if (System.Array.IndexOf(r.sharedMaterials, tireMat) >= 0) d.tyres[i].Add(r);
            }
        }

        int pitHint = -1;

        void Update()
        {
            if (car == null || ReplaySystem.IsPlaying) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // pneus novos no box (desgaste volta a zero): saem limpos
            float w = PitLaneSystem.Wear(car);
            if (lastWear >= 0f && w < lastWear - 0.05f)
                for (int i = 0; i < 4; i++) { level[i] = 0f; sinceDirt[i] = 999f; }
            lastWear = w;

            var surfaces = car.WheelSurfaces;
            bool moving = car.SpeedKmh > 3f;
            // No pit lane / box nunca suja (pedido do usuário: o piso do box nas pistas com grama sujava os pneus e todos
            // saíam com menos aderência — não existe grama no corredor do pit).
            if (moving && (PitLaneSystem.IsInPitFlow(car) || PitLaneSystem.InPitArea(car.transform.position, ref pitHint))) moving = false;
            for (int i = 0; i < 4; i++)
            {
                bool onDirt = moving && surfaces != null && i < surfaces.Length && surfaces[i] != null && surfaces[i].IsOffTrack;
                if (onDirt)
                {
                    level[i] = Mathf.Min(1f, level[i] + dt / PickupTime);
                    sinceDirt[i] = 0f;
                }
                else
                {
                    sinceDirt[i] += dt;
                    if (sinceDirt[i] > FullTime) level[i] = Mathf.Max(0f, level[i] - dt / FadeTime);
                }
                // aderência: na grama vale só a da superfície; fora dela, o pneu sujo perde até 8% e recupera em 24 s
                float loss = onDirt ? 0f : GripLossMax * level[i] * Mathf.Clamp01(1f - sinceDirt[i] / GripRecoverTime);
                car.extraGrip[i] = 1f - loss;
                int stage = level[i] >= 0.66f ? 2 : level[i] >= 0.33f ? 1 : level[i] > 0.02f ? 0 : -1;
                if (stage != shown[i]) Apply(i, stage);
            }
        }

        void Apply(int wheel, int stage)
        {
            shown[wheel] = stage;
            if (stage < 0)
            {
                foreach (var r in tyres[wheel]) if (r != null) r.SetPropertyBlock(null);
                return;
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
            var tex = Texture(stage);
            mpb.Clear();
            mpb.SetTexture(BaseMap, tex);
            mpb.SetTexture(MainTex, tex);
            mpb.SetColor(BaseColor, Color.white);
            mpb.SetColor(ColorId, Color.white);
            foreach (var r in tyres[wheel]) if (r != null) r.SetPropertyBlock(mpb);
        }

        /// <summary>Borracha (mesma cor do material de pneu) com manchas de terra na cor e cobertura medidas no vídeo.</summary>
        static Texture2D Texture(int stage)
        {
            if (stageTex == null) stageTex = new Texture2D[StageCoverage.Length];
            if (stageTex[stage] != null) return stageTex[stage];
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "TyreDirt" + stage, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            var rubber = new Color32(24, 24, 26, 255);   // 0,095/0,095/0,1 do material de pneu
            for (int i = 0; i < px.Length; i++) px[i] = rubber;
            var rng = new System.Random(77 + stage);
            int target = Mathf.RoundToInt(StageCoverage[stage] * n * n), painted = 0, guard = 0;
            var mark = new bool[n * n];
            while (painted < target && guard++ < 5000)
            {
                int cx = rng.Next(n), cy = rng.Next(n);
                float rx = 1.5f + (float)rng.NextDouble() * 5f, ry = rx * (0.5f + (float)rng.NextDouble());
                int shade = (int)((rng.NextDouble() * 2 - 1) * 24);   // desvio ~24 medido
                for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
                    for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                    {
                        float dx = (x - cx) / rx, dy = (y - cy) / ry;
                        if (dx * dx + dy * dy > 1f) continue;
                        int ix = ((x % n) + n) % n, iy = ((y % n) + n) % n, k = iy * n + ix;
                        int jitter = (int)((rng.NextDouble() * 2 - 1) * 10);
                        px[k] = new Color32((byte)Mathf.Clamp(92 + shade + jitter, 0, 255), (byte)Mathf.Clamp(82 + shade + jitter, 0, 255),
                            (byte)Mathf.Clamp(72 + shade + jitter, 0, 255), 255);
                        if (!mark[k]) { mark[k] = true; painted++; }
                    }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            stageTex[stage] = tex;
            return tex;
        }
    }
}
