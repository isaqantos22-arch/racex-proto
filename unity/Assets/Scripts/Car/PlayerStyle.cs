using System;
using System.Collections.Generic;
using System.IO;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// ESTILO DO JOGADOR (v99, pedido do usuário: "o pneu usado, clima, onde começo a fazer a curva, se freio, se apenas
    /// desacelero, se viro o volante de uma vez ou vou dando toques"). Por pista × tipo de carro (fórmula/GT) × composto ×
    /// pista seca/molhada guarda a volta LIMPA mais rápida trecho a trecho (128): velocidade, traçado (lateral), freio, tirar
    /// o pé, desaceleração medida e ritmo do volante. A IA Difícil/Expert usa o perfil da MESMA condição (pneu dela, seco ou
    /// molhado) — nunca um perfil de pista seca na chuva. Arquivo player_style.json no persistentDataPath (o autoteste grava
    /// em outro arquivo, para não misturar com o estilo do jogador no aparelho).
    /// </summary>
    public static class PlayerStyle
    {
        public const int Segments = PlayerPaceLearner.Segments;
        /// <summary>Pista molhada a partir desta umidade (WeatherSystem.Wetness) — o limite em que o perfil muda de grupo.</summary>
        public const float WetFrom = 0.15f;

        [Serializable]
        public class Profile
        {
            public int track;
            public bool formula;
            public int compound;
            public bool wet;
            public float lapTime;
            public string when;
            // Física do carro do jogador na volta (para ajustar a freada a um carro diferente).
            public float massKg, downforce, forwardStiffness;
            public float[] v = new float[Segments];          // m/s — maior velocidade no trecho
            public float[] lat = new float[Segments];        // m — lateral média (+ = direita do sentido da pista)
            public int[] n = new int[Segments];              // amostras no trecho (0 = sem dado)
            public float[] brake = new float[Segments];      // 0–1 — pedal de freio médio
            public float[] coast = new float[Segments];      // fração do trecho SÓ tirando o pé (sem freio nem acelerador)
            public float[] decel = new float[Segments];      // m/s² — maior desaceleração medida freando
            public float[] steerRate = new float[Segments];  // volante por segundo (|Δ comando| médio; ±1 = batente)
            [NonSerialized] public float[] zoneMax;
            [NonSerialized] public float zoneL;
        }

        [Serializable]
        class Store
        {
            public int version = 1;
            public List<Profile> items = new List<Profile>();
        }

        static Store store;
        /// <summary>Muda a cada volta gravada: a IA troca de perfil no meio da sessão (o jogador melhorou no treino).</summary>
        public static int Version { get; private set; }

        static string FilePath => Path.Combine(Application.persistentDataPath, AutoTest.Active ? "player_style_autotest.json" : "player_style.json");

        static Store Data
        {
            get
            {
                if (store != null) return store;
                try
                {
                    if (File.Exists(FilePath)) store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                }
                catch (Exception ex) { Debug.LogWarning("[PlayerStyle] leitura falhou: " + ex.Message); }
                if (store == null || store.items == null) store = new Store();
                store.items.RemoveAll(p => p == null || p.v == null || p.v.Length != Segments || p.lat == null || p.n == null
                                           || p.brake == null || p.coast == null || p.decel == null || p.steerRate == null);
                return store;
            }
        }

        public static int Seg(float along, float length) => PlayerPaceLearner.Seg(along, length);

        public static Profile Find(int track, bool formula, int compound, bool wet)
        {
            foreach (var p in Data.items)
                if (p.track == track && p.formula == formula && p.compound == compound && p.wet == wet) return p;
            return null;
        }

        /// <summary>Grava a volta se for a MELHOR nesta condição (pista, tipo de carro, pneu, seco/molhado).</summary>
        public static bool Commit(Profile p)
        {
            if (p == null || p.lapTime <= 10f) return false;
            var old = Find(p.track, p.formula, p.compound, p.wet);
            if (old != null && old.lapTime <= p.lapTime) return false;
            if (old != null) Data.items.Remove(old);
            Data.items.Add(p);
            Version++;
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Data)); }
            catch (Exception ex) { Debug.LogWarning("[PlayerStyle] gravação falhou: " + ex.Message); }
            Debug.Log($"[PlayerStyle] volta gravada pista={p.track} formula={p.formula} pneu={TyreModel.Letter(p.compound)} molhada={p.wet} tempo={p.lapTime:0.000}");
            return true;
        }

        /// <summary>Perfil para a IA: o da MESMA condição; sem ele, o mais rápido de outro composto do MESMO grupo (slick com
        /// slick no seco, pneu de chuva com pneu de chuva no molhado), com a velocidade ajustada pela aderência relativa dos
        /// pneus (v ∝ √µ — TyreModel.Base). Nada de grupo diferente: perfil seco nunca vale na chuva.</summary>
        public static Profile ForAI(int track, bool formula, int compound, bool wet, out float gripScale)
        {
            gripScale = 1f;
            var exact = Find(track, formula, compound, wet);
            if (exact != null) return exact;
            Profile best = null;
            foreach (var p in Data.items)
                if (p.track == track && p.formula == formula && p.wet == wet && TyreModel.IsWetTyre(p.compound) == TyreModel.IsWetTyre(compound)
                    && (best == null || p.lapTime < best.lapTime)) best = p;
            if (best != null) gripScale = Mathf.Sqrt(TyreModel.Base(compound) / Mathf.Max(0.1f, TyreModel.Base(best.compound)));
            return best;
        }

        /// <summary>Lateral do traçado do jogador no ponto (interpolada entre os centros dos trechos); NaN = sem dado.</summary>
        public static float LatAt(Profile p, float at, float length)
        {
            if (p == null || length <= 1f) return float.NaN;
            float f = Mathf.Repeat(at, length) / (length / Segments) - 0.5f;
            int i0 = Mathf.FloorToInt(f);
            float u = f - i0;
            int a = ((i0 % Segments) + Segments) % Segments, b = (a + 1) % Segments;
            bool ha = p.n[a] > 0, hb = p.n[b] > 0;
            if (ha && hb) return Mathf.Lerp(p.lat[a], p.lat[b], u);
            if (ha) return p.lat[a];
            if (hb) return p.lat[b];
            return float.NaN;
        }

        /// <summary>Maior desaceleração do jogador nos ~100 m ANTES do ponto (a freada para a curva que está ali).</summary>
        public static float BrakeZoneDecel(Profile p, float at, float length)
        {
            if (p == null || length <= 1f) return 0f;
            if (p.zoneMax == null || Mathf.Abs(p.zoneL - length) > 1f)
            {
                p.zoneL = length;
                p.zoneMax = new float[Segments];
                int w = Mathf.Max(1, Mathf.CeilToInt(100f / (length / Segments)));
                for (int i = 0; i < Segments; i++)
                {
                    float m = 0f;
                    for (int o = 0; o <= w; o++) m = Mathf.Max(m, p.decel[(i - o + Segments) % Segments]);
                    p.zoneMax[i] = m;
                }
            }
            return p.zoneMax[Seg(at, length)];
        }

        /// <summary>Freada do carro da IA ÷ freada do carro do jogador (atrito de freio × (peso + apoio) a 180 km/h), no
        /// máximo 1: com carro de freio pior que o do jogador, a IA não copia uma freada que o carro dela não faz.</summary>
        public static float BrakeRatio(Profile p, CarSpec s, float massKg)
        {
            if (p == null || s == null || p.massKg < 50f || p.forwardStiffness < 0.1f || massKg < 50f) return 1f;
            const float v2 = 50f * 50f;
            float mine = s.forwardStiffness * (9.81f + s.downforceCoefficient * v2 / massKg);
            float his = p.forwardStiffness * (9.81f + p.downforce * v2 / p.massKg);
            return Mathf.Clamp(mine / Mathf.Max(0.1f, his), 0.5f, 1f);
        }

        /// <summary>Velocidade do volante da IA (comando por segundo) pelo jeito do jogador: média de |Δ comando|/s nos trechos
        /// em que ele mexe no volante, ×1,5 (as entradas de curva ficam acima da média), entre 2 e 12. 0 = sem dado.</summary>
        public static float SteerSlew(Profile p)
        {
            if (p == null) return 0f;
            float sum = 0f; int k = 0;
            for (int i = 0; i < Segments; i++)
                if (p.n[i] > 0 && p.steerRate[i] > 0.05f) { sum += p.steerRate[i]; k++; }
            return k < 8 ? 0f : Mathf.Clamp(sum / k * 1.5f, 2f, 12f);
        }
    }
}
