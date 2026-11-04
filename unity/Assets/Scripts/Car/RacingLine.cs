using System.Collections.Generic;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// TRAÇADO IDEAL da pista (v99): linha de MENOR CURVATURA dentro da largura — cada ponto vai para o meio dos vizinhos, preso
    /// entre as bordas (a mesma "edge" que a IA mira: meia pista − 1,25 m). É o fora-dentro-fora de verdade e encadeia curvas
    /// seguidas numa linha só. Antes a IA mirava um "C por dentro" em cada curva; com o carro de aderência real quase toda dobra
    /// do Circuito Verde virou "curva de verdade" e o carro ia de um lado para o outro (autoteste 03/11: zigue-zague 5,6/km na IA e
    /// 7,1/km no piloto automático sozinho; relato do usuário: "curva no lado errado, virando no tempo errado"). A curvatura DA
    /// LINHA (mais aberta que a do centro) alimenta o limite de velocidade. Calculado uma vez por pista (multirresolução); mesmo
    /// cálculo da simulação de 03/11 (scratchpad ia_tracado_forest.py).
    /// </summary>
    public static class RacingLine
    {
        sealed class Line { public float[] lat, k; public float spacing, edge, zs, ze, zl; public int n; }
        static readonly Dictionary<TrackSpline, Line> cache = new Dictionary<TrackSpline, Line>();
        static TrackSpline lastSpline;
        static Line last;
        // Zona do pit (v99): entre zoneStart e zoneEnd (m ao longo da pista) a linha não passa à esquerda de zoneLeft — a mureta
        // laranja e o BICO da entrada do box ficam desse lado (autoteste 03/11: 6 de 9 carros destruídos no bico, a linha colada na
        // borda esquerda na saída da última curva).
        static float zoneStart = -1f, zoneEnd = -1f, zoneLeft = -999f;

        public static void SetPitZone(float start, float end, float leftLimit)
        {
            if (Mathf.Abs(start - zoneStart) < 0.5f && Mathf.Abs(end - zoneEnd) < 0.5f && Mathf.Abs(leftLimit - zoneLeft) < 0.05f) return;
            zoneStart = start; zoneEnd = end; zoneLeft = leftLimit;
            cache.Clear(); last = null; lastSpline = null;
        }

        static bool Same(Line l, float edge) => Mathf.Abs(l.edge - edge) < 0.05f && Mathf.Abs(l.zs - zoneStart) < 0.5f && Mathf.Abs(l.ze - zoneEnd) < 0.5f && Mathf.Abs(l.zl - zoneLeft) < 0.05f;

        static Line Get(TrackSpline sp, float edge)
        {
            if (sp == null || sp.Count < 32 || sp.spacing <= 0.01f) return null;
            if (sp == lastSpline && last != null && Same(last, edge)) return last;
            if (!cache.TryGetValue(sp, out var line) || !Same(line, edge))
            {
                try { line = Build(sp, edge); }
                catch (System.Exception ex) { Debug.LogWarning("[RacingLine] " + ex.Message); line = null; }
                if (line == null) return null;
                if (cache.Count >= 4) cache.Clear();   // pistas de corridas anteriores (só uma está ativa por vez)
                cache[sp] = line;
            }
            lastSpline = sp; last = line;
            return line;
        }

        static Line Build(TrackSpline sp, float edge)
        {
            float t0 = Time.realtimeSinceStartup;
            int n = sp.Count;
            var P = sp.points.ToArray();
            var R = sp.rights.ToArray();
            var a = new float[n];
            // Limite à esquerda por ponto: −edge, ou zoneLeft na zona do pit.
            var lo = new float[n];
            float trackLen = n * sp.spacing, zLen = zoneEnd >= 0f ? Mathf.Repeat(zoneEnd - zoneStart, trackLen) : -1f;
            for (int i = 0; i < n; i++)
            {
                bool inZone = zLen > 0f && Mathf.Repeat(i * sp.spacing - zoneStart, trackLen) < zLen;
                lo[i] = inZone ? Mathf.Max(-edge, zoneLeft) : -edge;
            }
            // Multirresolução: a curva longa converge no passo grosso; o fino só acerta o detalhe (relaxação direta no fino
            // levaria dezenas de milhares de passadas).
            for (int stride = 8; stride >= 1; stride /= 2)
            {
                int m = n / stride;
                if (m < 8) continue;
                int iters = stride == 1 ? 400 : 1200;
                for (int it = 0; it < iters; it++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        int i = j * stride, ip = ((j + 1) % m) * stride, im = ((j + m - 1) % m) * stride;
                        Vector3 qi = P[i] + R[i] * a[i];
                        Vector3 mid = (P[ip] + R[ip] * a[ip] + P[im] + R[im] * a[im]) * 0.5f;
                        a[i] = Mathf.Clamp(a[i] + 0.5f * Vector3.Dot(mid - qi, R[i]), lo[i], edge);
                    }
                }
                if (stride > 1)
                {
                    for (int j = 0; j < m; j++)
                    {
                        int i0 = j * stride, i1 = ((j + 1) % m) * stride;
                        int span = j == m - 1 ? n - i0 : stride;
                        for (int t = 1; t < span; t++) a[(i0 + t) % n] = Mathf.Lerp(a[i0], a[i1], t / (float)span);
                    }
                }
            }
            // Curvatura da LINHA com a mesma janela da IA (±8 m, CarAI.Curvature).
            var L = new Vector3[n];
            for (int i = 0; i < n; i++) L[i] = P[i] + R[i] * a[i];
            var T = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = L[(i + 1) % n] - L[(i + n - 1) % n]; d.y = 0f;
                T[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
            }
            int w = Mathf.Max(1, Mathf.RoundToInt(8f / sp.spacing));
            float span2 = 2f * w * sp.spacing;
            var k = new float[n];
            for (int i = 0; i < n; i++)
                k[i] = Vector3.Angle(T[(i + n - w) % n], T[(i + w) % n]) * Mathf.Deg2Rad / span2;
            Debug.Log("[RacingLine] pista " + n + " pontos, borda " + edge.ToString("0.00") + " m, " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
            for (int i = 0; i < n; i++) a[i] = Mathf.Max(a[i], lo[i]);   // interpolação do passo grosso nunca entra na zona
            return new Line { lat = a, k = k, spacing = sp.spacing, edge = edge, n = n, zs = zoneStart, ze = zoneEnd, zl = zoneLeft };
        }

        static float Sample(float[] arr, Line l, float along)
        {
            float f = Mathf.Repeat(along, l.n * l.spacing) / l.spacing;
            int i0 = Mathf.FloorToInt(f);
            float u = f - i0;
            i0 %= l.n;
            return Mathf.Lerp(arr[i0], arr[(i0 + 1) % l.n], u);
        }

        /// <summary>Lateral do traçado ideal no ponto (m, + = direita); NaN = sem linha (pista curta/inválida).</summary>
        public static float LatAt(TrackSpline sp, float edge, float along)
        {
            var l = Get(sp, edge);
            return l != null ? Sample(l.lat, l, along) : float.NaN;
        }

        /// <summary>Curvatura (1/m) do traçado ideal no ponto; -1 = sem linha.</summary>
        public static float CurvatureAt(TrackSpline sp, float edge, float along)
        {
            var l = Get(sp, edge);
            return l != null ? Sample(l.k, l, along) : -1f;
        }
    }
}
