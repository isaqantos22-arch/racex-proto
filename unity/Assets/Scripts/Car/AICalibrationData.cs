using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Tabela de limites REAIS medidos pela calibração ao vivo (-aicallb).
    /// Carrega Resources/ai_calibration.txt (JSON do AICalibrationLive) e popula VMax:
    /// para cada carro, o limite físico de EXPERT por raio × fmax MEDIDO daquele carro
    /// (busca binária: a maior fração em que a IA completa a volta limpa).
    /// Sem JSON: VMax vazio → IA usa a fórmula de grip (fallback).</summary>
    public static class AICalibrationData
    {
        /// <summary>[modelo][índice de raio] = velocidade máxima medida de entrada (m/s). Vazio = fallback.</summary>
        /// ORDEM OBRIGATÓRIA: Radii ANTES de VMax — campos estáticos inicializam na ordem do
        /// arquivo, e Build() lê Radii. VMax primeiro = Radii ainda null dentro de Build() =
        /// NullReferenceException no cctor = TypeInitializationException em TODO Think() da IA
        /// para sempre (o guard de Resources.Load não cobria isso — era a linha errada).
        public static readonly float[] Radii = { 30f, 60f, 120f, 200f };
        public static readonly float[][] VMax = Build();

        /// <summary>Grip físico de referência EXPERT (CarAI.CornerGrip[3]) — a base da medição.</summary>
        public const float GripExpert = 1.35f;

        static float[][] Build()
        {
            // Limite físico EXPERT por raio (igual ao fallback do CarAI com d=Expert).
            var phys = new float[Radii.Length];
            for (int i = 0; i < Radii.Length; i++) phys[i] = Mathf.Sqrt(GripExpert * 9.81f * Radii[i]);

            // GUARD anti-crash: NUNCA deixar o static ctor explodir (TypeInitializationException
            // mata o CarAI.Think() para sempre — carro nasce e fica parado).
            TextAsset txt = null;
            try { txt = Resources.Load<TextAsset>("ai_calibration"); } catch (Exception) { txt = null; }
            if (txt == null || string.IsNullOrEmpty(txt.text))
            {
                Debug.Log("[AICALIB] ai_calibration.txt ausente — IA usa fórmula de grip (fallback).");
                return new float[0][];
            }
            try
            {
                var json = txt.text;
                int nModels = Enum.GetValues(typeof(CarModel)).Length;
                var table = new float[nModels][];
                // fmax por modelo = MÉDIA das pistas medidas (a pista específica não entra no CarAI ainda).
                var fmax = new Dictionary<int, List<float>>();
                int i0 = json.IndexOf("\"cars\"", StringComparison.Ordinal);
                if (i0 < 0) return new float[0][];
                var seg = json.Substring(i0);
                int mi = -1;
                foreach (var chunk in seg.Split('{'))
                {
                    var m = Extract(chunk, "model");
                    var f = Extract(chunk, "fmax");
                    if (m.Length == 0 || f.Length == 0) continue;
                    if (Enum.TryParse(m, out CarModel parsed)) mi = (int)parsed;
                    if (mi < 0 || mi >= nModels) continue;
                    if (!float.TryParse(f, NumberStyles.Any, CultureInfo.InvariantCulture, out var fmaxVal)) continue;
                    if (!fmax.TryGetValue(mi, out var list)) fmax[mi] = list = new List<float>();
                    if (fmaxVal > 0.05f && fmaxVal <= 1.05f) list.Add(fmaxVal);
                }
                foreach (var kv in fmax)
                {
                    if (kv.Value.Count == 0) continue;
                    float avg = 0f; foreach (var v in kv.Value) avg += v; avg /= kv.Value.Count;
                    var row = new float[Radii.Length];
                    for (int i = 0; i < Radii.Length; i++) row[i] = phys[i] * Mathf.Clamp(avg, 0.4f, 1.0f);
                    table[kv.Key] = row;
                    Debug.Log($"[AICALIB] modelo {(CarModel)kv.Key} fmax_medio={avg:F2} (de {kv.Value.Count} pistas)");
                }
                Debug.Log($"[AICALIB] tabela carregada: {fmax.Count} carros calibrados.");
                return table;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICALIB] falha ao ler ai_calibration.txt: " + ex.Message);
                return new float[0][];
            }
        }

        static string Extract(string chunk, string key)
        {
            int k = chunk.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return "";
            int c0 = chunk.IndexOf(':', k);
            if (c0 < 0) return "";
            int c1 = chunk.IndexOf(',', c0);
            int c2 = chunk.IndexOf('}', c0);
            int end = c1 < 0 ? c2 : (c2 < 0 ? c1 : Math.Min(c1, c2));
            if (end < 0) return "";
            var v = chunk.Substring(c0 + 1, end - c0 - 1).Trim().Trim('"');
            return v;
        }
    }
}
