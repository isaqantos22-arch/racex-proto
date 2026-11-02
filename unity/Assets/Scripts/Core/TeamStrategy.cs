using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>
    /// Estratégia por EQUIPE/CARRO (v80), no formato da F1 real:
    /// • Corrida seca: obrigatório usar ao menos 2 compostos secos diferentes (regra FIA).
    /// • A equipe simula as opções (1 e 2 paradas, ordens de compostos) somando o tempo de cada stint
    ///   (ritmo do composto + degradação acumulada) + perda no pit, e escolhe o menor total — o mesmo
    ///   método descrito por estrategistas da F1 (plano A; o plano B é o 2º melhor).
    /// • Cada carro tem fator de desgaste próprio (peso, apoio aerodinâmico, estilo do piloto e força da
    ///   equipe) — por isso as estratégias NÃO são iguais para todos.
    /// • Treino livre: programa de corridas curtas (simulação de classificação, macio) e longas
    ///   (simulação de corrida, médio/duro) para medir a degradação.
    /// Valores de tempo são estimativas de projeto (não medição): perda no pit ~21 s.
    /// </summary>
    public static class TeamStrategy
    {
        public const float PitLossSeconds = 21f;
        static readonly float[] PacePerLap = { 0f, 0.6f, 1.25f }; // s/volta vs macio (Pirelli: ~0,6–0,8 s entre vizinhos)
        static readonly float[] DegPerLap = { 0.095f, 0.055f, 0.035f }; // s perdidos por volta de uso (estimativa)

        public class Plan
        {
            public readonly List<int> compounds = new List<int>(); // [0] = largada
            public readonly List<int> stopLaps = new List<int>();  // volta de cada parada (1-based)
            public float estimatedTime;
            public string Describe()
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < compounds.Count; i++) { if (i > 0) sb.Append('→'); sb.Append(TyreModel.Letter(compounds[i])); }
                return (compounds.Count - 1) + "P " + sb;
            }
        }

        /// <summary>Fator de desgaste do carro: &gt;1 gasta mais (carro pesado, muito apoio, piloto agressivo).</summary>
        public static float WearFactor(RaceX.Car.CarSpec spec, float aggression, float tyreMgmt, float teamTier01)
        {
            float f = 1f;
            if (spec != null)
            {
                f *= Mathf.Lerp(0.95f, 1.08f, Mathf.InverseLerp(600f, 1800f, spec.massKg));
                f *= Mathf.Lerp(0.97f, 1.06f, Mathf.InverseLerp(1.5f, 4.5f, spec.downforceCoefficient));
            }
            f *= 1f + 0.10f * aggression - 0.12f * tyreMgmt;
            f *= 1.04f - 0.06f * teamTier01; // equipes melhores entendem melhor o pneu
            return Mathf.Clamp(f, 0.8f, 1.25f);
        }

        static float StintTime(int compound, int laps, float wearFactor)
        {
            if (laps <= 0) return 0f;
            float life = TyreModel.LifeLaps(compound) / wearFactor;
            float t = 0f;
            for (int i = 0; i < laps; i++)
            {
                float rem = 1f - (i + 0.5f) / life;
                float cliff = rem < 0.3f ? (0.3f - Mathf.Max(rem, -0.2f)) * 9f : 0f; // penhasco do pneu
                t += PacePerLap[Mathf.Clamp(compound, 0, 2)] + DegPerLap[Mathf.Clamp(compound, 0, 2)] * wearFactor * i + cliff;
            }
            return t;
        }

        /// <summary>Melhor plano de corrida seca para este carro. jitter (−1..1) dá a personalidade da
        /// equipe (agressiva antecipa a parada — undercut; conservadora estica — overcut).</summary>
        /// <summary>Até este número de voltas a corrida é tratada como Sprint: sem troca obrigatória.</summary>
        public const int SprintMaxLaps = 12;

        public static Plan BestDryPlan(int laps, float wearFactor, float jitter, bool sprintLike = false)
        {
            laps = Mathf.Max(2, laps);
            if (sprintLike)
            {
                // Sprint: o composto mais macio que aguenta a distância inteira, sem parada.
                for (int c = 0; c <= 2; c++)
                {
                    if (laps > TyreModel.LifeLaps(c) / wearFactor * 0.95f) continue;
                    var sp = new Plan();
                    sp.compounds.Add(c);
                    sp.estimatedTime = StintTime(c, laps, wearFactor);
                    return sp;
                }
            }
            Plan best = null, second = null;
            int[][] seqs =
            {
                new[] { 1, 2 }, new[] { 2, 1 }, new[] { 0, 2 }, new[] { 0, 1 }, new[] { 1, 0 },
                new[] { 0, 1, 2 }, new[] { 1, 2, 1 }, new[] { 0, 2, 1 }, new[] { 1, 1, 2 }, new[] { 0, 0, 2 }, new[] { 2, 1, 0 },
                new[] { 0, 1, 1, 2 }, new[] { 1, 2, 2, 0 }
            };
            foreach (var seq in seqs)
            {
                int stops = seq.Length - 1;
                if (stops * 3 >= laps) continue;
                // Distribui as voltas proporcionalmente à vida de cada composto.
                float lifeSum = 0f;
                foreach (int c in seq) lifeSum += TyreModel.LifeLaps(c);
                var plan = new Plan();
                int lapAcc = 0;
                float total = 0f;
                bool ok = true;
                for (int i = 0; i < seq.Length; i++)
                {
                    int stint = i == seq.Length - 1 ? laps - lapAcc : Mathf.Max(3, Mathf.RoundToInt(laps * TyreModel.LifeLaps(seq[i]) / lifeSum));
                    if (stint < 2) { ok = false; break; }
                    // Stint além da vida útil (abaixo de 0%) é inviável (risco de furo).
                    if (stint > TyreModel.LifeLaps(seq[i]) / wearFactor * 1.02f) { ok = false; break; }
                    plan.compounds.Add(seq[i]);
                    total += StintTime(seq[i], stint, wearFactor);
                    lapAcc += stint;
                    // Janela da parada proporcional à corrida (±8% das voltas, mínimo ±1,5): espalha as
                    // equipes — antes todas paravam na mesma volta e lotavam o pit lane.
                    float window = Mathf.Max(1.5f, laps * 0.08f);
                    if (i < seq.Length - 1) plan.stopLaps.Add(Mathf.Clamp(lapAcc + Mathf.RoundToInt(-jitter * window), 2, laps - 1));
                }
                if (!ok) continue;
                total += stops * PitLossSeconds;
                plan.estimatedTime = total;
                if (best == null || total < best.estimatedTime) { second = best; best = plan; }
                else if (second == null || total < second.estimatedTime) second = plan;
            }
            if (best == null)
            {
                // Corrida curta demais: um composto só, sem parada.
                best = new Plan();
                best.compounds.Add(laps <= TyreModel.LifeLaps(0) ? 0 : laps <= TyreModel.LifeLaps(1) ? 1 : 2);
            }
            // Personalidade: equipe muito agressiva às vezes vai de plano B (mais paradas, ritmo maior).
            if (second != null && jitter > 0.75f && second.compounds.Count > best.compounds.Count) return second;
            return best;
        }

        /// <summary>Plano de corrida MOLHADA (sem obrigação de 2 compostos): composto certo para a água.</summary>
        public static Plan WetPlan(int laps, float wetness)
        {
            var p = new Plan();
            p.compounds.Add(TyreModel.BestForWetness(wetness, 1));
            return p;
        }

        /// <summary>Programa de treino livre: (composto, voltas) da i-ésima saída do box.
        /// Equipes fortes testam mais o macio (classificação); as outras fazem mais corrida longa.</summary>
        public static void PracticeRun(int runIndex, float teamTier01, bool qualifying, out int compound, out int laps)
        {
            if (qualifying)
            {
                compound = TyreModel.Soft;
                laps = 3; // volta de saída + volta lançada + volta de volta
                return;
            }
            int[] comp = teamTier01 > 0.6f ? new[] { 1, 0, 2, 0, 1 } : new[] { 1, 2, 0, 1, 2 };
            int[] lps = { 5, 3, 9, 3, 8 };
            int i = Mathf.Abs(runIndex) % comp.Length;
            compound = comp[i];
            laps = lps[i];
        }
    }
}
