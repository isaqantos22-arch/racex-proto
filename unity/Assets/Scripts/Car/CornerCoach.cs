using System;
using System.Collections.Generic;
using System.IO;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// CADERNO POR CURVA (v105, pedido do usuário em 05/11: "por que eles não fazem as curvas como eu, não tentam melhorar as
    /// voltas ruins, não aprendem de verdade com os erros em vez de só diminuir a velocidade — ver o motivo de ter saído e se
    /// corrigir: tirar o pé antes, posicionar melhor o carro, considerando a curva, a velocidade, o pneu, a aerodinâmica").
    ///
    /// Cada piloto da IA guarda, para cada curva da pista (por carro × seco/molhado):
    ///  • escala  — quanto do limite que a conta de física promete ele usa nessa curva (a conta já considera pneu, temperatura,
    ///              desgaste, água, peso e apoio aerodinâmico; a escala é o que a pista ENSINOU além dela). Medido em 05/11 na
    ///              telemetria do jogador: nas curvas de 100–180 km/h ele usa até 114–115% do que a conta promete (p95) — por isso a
    ///              escala pode passar de 1;
    ///  • tirarAntes — metros a mais para tirar o pé antes da curva (o jogador não freia em curva: 0% das amostras em curva
    ///              com freio; só tirando o pé o carro perde 5,5–6,5 m/s² de 80 a 200 km/h);
    ///  • saidaDepois — metros depois do ápice com o acelerador pela metade (traseira escapando na saída);
    ///  • abrir    — metros a mais por FORA na entrada (raio maior; entrou de frente / saiu largo).
    /// Depois de cada passagem ele vê o que o carro fez (deslizamento da frente e da traseira, onde saiu, de que lado) e muda o
    /// que causou o erro. Passagem limpa com pneu sobrando: tenta um pouco mais rápido na próxima. Passagem mais lenta que a
    /// melhor: volta ao que deu o melhor tempo. Grava em ai_notebook.json (no autoteste só na memória, como a AiTrackMemory).
    /// </summary>
    public static class CornerCoach
    {
        public class Corner
        {
            public int id;
            public float start, apex, end;   // along (m); end/apex podem passar de L (curva que cruza a linha) — use Rel()
            public int dir;                  // +1 curva à direita (lado de dentro = lateral +), -1 à esquerda
            public float kMax;
        }

        public class Map
        {
            public List<Corner> corners = new List<Corner>();
            public float length, step;
            public short[] inner;            // curva em [start-10, end+10]
            public short[] zone;             // aproximação [start-150, end+60] (a memória de trecho antiga não vale aqui)
            public short[] obs;              // janela de observação [start-30, end+60]
        }

        [Serializable]
        public class Note
        {
            public int c;
            public float scale = 0.92f, bestScale = 0.92f, bestT = 999f;
            public float liftEarly, exitDelay, widen;
            public float bestLift;           // tirarAntes da melhor passagem
            public float steerHold = 1f, bestHold = 1f;   // reforço da parte de curva do volante nesta curva (aprendido/testado)
            public int exp;                  // experimento em curso (1 curva mais rápida, 2 tirar o pé depois, 3 antes e mais rápido, 4 segurar mais o volante)
            public bool confirm;             // próxima passagem repete o recorde para confirmar
            public float refRatio;           // velocidade do JOGADOR na curva ÷ limite da conta (0 = sem volta dele)
            public int passes, errors, cool;
        }

        [Serializable]
        public class Book
        {
            public string key;
            public List<Note> notes = new List<Note>();
            [NonSerialized] public Dictionary<int, Note> byCorner;
        }

        [Serializable]
        class Store
        {
            public int version = 1;
            public List<Book> books = new List<Book>();
        }

        const float Step = 2f;
        /// <summary>Raio máximo de "curva" (m): dobras mais abertas não limitam a velocidade de um fórmula.</summary>
        const float KMin = 1f / 260f;

        static readonly Dictionary<string, Map> maps = new Dictionary<string, Map>();
        static Store store;
        static Dictionary<string, Book> index;
        static bool dirty;
        static float lastFlush;

        // v105 (pedido do usuário: "faça o autoteste gravar e ter a mesma função das IAs — é um teste, para testar"): o autoteste
        // também GRAVA o caderno, num arquivo próprio (ai_notebook_autotest.json), que continua de um teste para o outro como no jogo.
        // notebook=0 no autotest.txt volta ao modo só-memória.
        static bool Persistent => !AutoTest.Active || AutoTest.GetIntPublic("notebook", 1) == 1;
        static string FilePath => Path.Combine(Application.persistentDataPath, AutoTest.Active ? "ai_notebook_autotest.json" : "ai_notebook.json");

        // ------------------------------------------------------------------ mapa de curvas

        public static float Rel(float along, float from, float L) => Mathf.Repeat(along - from, L);

        public static Map MapFor(TrackSpline sp, int trackId)
        {
            if (sp == null || sp.Length < 50f) return null;
            string key = trackId + "|" + Mathf.RoundToInt(sp.Length);
            if (maps.TryGetValue(key, out var m)) return m;
            m = Build(sp);
            maps[key] = m;
            Debug.Log("[CADERNO] pista " + trackId + ": " + m.corners.Count + " curvas mapeadas (" + sp.Length.ToString("0") + " m)");
            if (AutoTest.Active)
            {
                var sb = new System.Text.StringBuilder("CURVAS pista=" + trackId + " n=" + m.corners.Count);
                foreach (var c in m.corners)
                    sb.Append(" | C" + c.id + " " + c.start.ToString("0") + "-" + c.end.ToString("0") + " apice=" + c.apex.ToString("0")
                              + (c.dir > 0 ? " dir" : " esq") + " r=" + (1f / Mathf.Max(1e-4f, c.kMax)).ToString("0"));
                AutoTest.Log(sb.ToString());
            }
            return m;
        }

        static Map Build(TrackSpline sp)
        {
            var m = new Map { length = sp.Length };
            float L = sp.Length;
            int n = Mathf.Max(16, Mathf.FloorToInt(L / Step));
            m.step = L / n;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = CarAI.SignedCurvature(sp, i * m.step);
            var k = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int o = -2; o <= 2; o++) s += raw[(i + o + n) % n];
                k[i] = s / 5f;
            }
            // começa a varredura num ponto de reta (nenhuma curva cortada ao meio)
            int s0 = -1;
            for (int i = 0; i < n; i++) if (Mathf.Abs(k[i]) < KMin) { s0 = i; break; }
            m.inner = new short[n]; m.zone = new short[n]; m.obs = new short[n];
            for (int i = 0; i < n; i++) { m.inner[i] = -1; m.zone[i] = -1; m.obs[i] = -1; }
            if (s0 < 0) return m;   // oval/pista toda em curva: sem caderno

            // trechos contínuos acima do limite, do mesmo lado
            var runs = new List<int[]>();   // {inicio, fim, dir, apice} em índices "desenrolados" a partir de s0
            int rs = -1, rdir = 0, rap = -1; float rk = 0f;
            for (int j = 0; j <= n; j++)
            {
                int i = (s0 + j) % n;
                float a = j < n ? k[i] : 0f;
                bool on = Mathf.Abs(a) >= KMin;
                int dir = a > 0f ? 1 : -1;
                if (rs >= 0 && (!on || dir != rdir))
                {
                    runs.Add(new[] { rs, j - 1, rdir, rap });
                    rs = -1;
                }
                if (on && rs < 0) { rs = j; rdir = dir; rk = 0f; rap = j; }
                if (on && Mathf.Abs(a) > rk) { rk = Mathf.Abs(a); rap = j; }
            }
            // junta trechos do mesmo lado separados por menos de 20 m (curva longa com raio variando)
            var merged = new List<int[]>();
            foreach (var r in runs)
            {
                if (merged.Count > 0)
                {
                    var p = merged[merged.Count - 1];
                    if (p[2] == r[2] && (r[0] - p[1]) * m.step < 20f)
                    {
                        float kp = Mathf.Abs(k[(s0 + p[3]) % n]), kr = Mathf.Abs(k[(s0 + r[3]) % n]);
                        p[1] = r[1];
                        if (kr > kp) p[3] = r[3];
                        continue;
                    }
                }
                merged.Add(r);
            }
            foreach (var r in merged)
            {
                if ((r[1] - r[0] + 1) * m.step < 8f) continue;
                var c = new Corner
                {
                    id = m.corners.Count,
                    start = ((s0 + r[0]) % n) * m.step,
                    dir = r[2],
                    kMax = Mathf.Abs(k[(s0 + r[3]) % n])
                };
                c.end = c.start + (r[1] - r[0]) * m.step;
                c.apex = c.start + (r[3] - r[0]) * m.step;
                m.corners.Add(c);
            }
            // tabelas de consulta O(1): a mais próxima vence onde as janelas se cruzam (curvas em sequência)
            foreach (var c in m.corners)
            {
                Fill(m, m.zone, c, 150f, 60f);
                Fill(m, m.obs, c, 30f, 60f);   // 05/11: saída larga da última curva batia no bico a ~40 m do fim — fora da janela de 40 m
                Fill(m, m.inner, c, 10f, 10f);
            }
            return m;
        }

        static void Fill(Map m, short[] arr, Corner c, float before, float after)
        {
            int n = arr.Length;
            float from = c.start - before, len = (c.end - c.start) + before + after;
            int i0 = Mathf.FloorToInt(Mathf.Repeat(from, m.length) / m.step);
            int cnt = Mathf.CeilToInt(len / m.step);
            for (int j = 0; j <= cnt; j++)
            {
                int i = (i0 + j) % n;
                // janela de aproximação: quem vem DEPOIS (a curva que se aproxima) tem prioridade sobre a saída da anterior
                if (arr[i] < 0 || j * m.step <= before) arr[i] = (short)c.id;
            }
        }

        static Corner Look(Map m, short[] arr, float along)
        {
            if (m == null || arr == null || m.corners.Count == 0) return null;
            int i = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(along, m.length) / m.step), 0, arr.Length - 1);
            int c = arr[i];
            return c >= 0 ? m.corners[c] : null;
        }

        public static Corner Inner(Map m, float along) => Look(m, m != null ? m.inner : null, along);
        public static Corner Zone(Map m, float along) => Look(m, m != null ? m.zone : null, along);
        public static Corner ObsWindow(Map m, float along) => Look(m, m != null ? m.obs : null, along);

        // ------------------------------------------------------------------ caderno

        static void Load()
        {
            if (store != null) return;
            store = new Store();
            index = new Dictionary<string, Book>();
            try
            {
                if (Persistent && File.Exists(FilePath))
                {
                    var s = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                    if (s != null && s.books != null) store = s;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[CADERNO] falha ao ler: " + ex.Message); store = new Store(); }
            foreach (var b in store.books) if (b != null && !string.IsNullOrEmpty(b.key)) index[b.key] = b;
        }

        public static Book BookFor(string key)
        {
            Load();
            if (index.TryGetValue(key, out var b)) return b;
            b = new Book { key = key };
            store.books.Add(b);
            index[key] = b;
            return b;
        }

        public static Note Find(Book b, int corner)
        {
            if (b == null) return null;
            if (b.byCorner == null)
            {
                b.byCorner = new Dictionary<int, Note>();
                foreach (var nt in b.notes) b.byCorner[nt.c] = nt;
            }
            return b.byCorner.TryGetValue(corner, out var n) ? n : null;
        }

        public static Note Add(Book b, int corner, float scale)
        {
            var n = new Note { c = corner, scale = scale, bestScale = scale };
            b.notes.Add(n);
            if (b.byCorner == null) b.byCorner = new Dictionary<int, Note>();
            b.byCorner[corner] = n;
            dirty = true;
            return n;
        }

        /// <summary>Lição geral: em todos os cadernos com o mesmo sufixo de chave (|pista|grupo|molhado|L), a curva recebe cautela.
        /// Devolve quantos cadernos foram ajustados.</summary>
        public static int SharedLesson(string keySuffix, int corner, float scaleDrop, float liftAdd)
        {
            Load();
            int count = 0;
            foreach (var b in store.books)
            {
                if (b == null || b.key == null || !b.key.EndsWith(keySuffix)) continue;
                var n = Find(b, corner);
                if (n == null) continue;
                n.scale = Mathf.Max(0.7f, n.scale - scaleDrop);
                n.bestScale = Mathf.Min(n.bestScale, n.scale);
                n.liftEarly = Mathf.Min(80f, n.liftEarly + liftAdd);
                n.bestLift = Mathf.Max(n.bestLift, n.liftEarly);
                n.exp = 0; n.confirm = false; n.cool = 1;
                count++;
            }
            dirty = true;
            return count;
        }

        public static void MarkDirty()
        {
            dirty = true;
            if (Time.unscaledTime - lastFlush > 60f) Flush();
        }

        public static void Flush()
        {
            lastFlush = Time.unscaledTime;
            if (!dirty || store == null) return;
            dirty = false;
            if (!Persistent) return;
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(store));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Debug.LogWarning("[CADERNO] falha ao gravar: " + ex.Message); }
        }

        // ------------------------------------------------------------------ desaceleração só tirando o pé

        /// <summary>Por grupo de física: regressão desacel = a0 + c·v² com as amostras MEDIDAS da própria IA (pé fora do
        /// acelerador e do freio). Inclui o arrasto, o freio-motor e o pneu esfregando na curva — é o que o carro perde de verdade.</summary>
        static readonly Dictionary<int, double[]> coast = new Dictionary<int, double[]>();

        public static void CoastSample(int group, float v, float decel)
        {
            if (v < 15f || decel < -3f || decel > 20f) return;
            if (!coast.TryGetValue(group, out var s)) { s = new double[5]; coast[group] = s; }
            const double keep = 0.998;
            double x = v * v;
            for (int i = 0; i < 5; i++) s[i] *= keep;
            s[0] += 1; s[1] += x; s[2] += decel; s[3] += x * x; s[4] += x * decel;
        }

        /// <summary>Desaceleração (m/s²) só tirando o pé a v. Sem amostras: 4,5 m/s² (telemetria do jogador de 04/11, F2 seco:
        /// medianas 5,6 a 80–120 km/h e 6,5 a 120–200 km/h — valor menor por margem).</summary>
        public static float CoastDecel(int group, float v)
        {
            if (coast.TryGetValue(group, out var s) && s[0] >= 40)
            {
                double n = s[0], den = n * s[3] - s[1] * s[1];
                double c = den > 1e-6 ? (n * s[4] - s[1] * s[2]) / den : 0;
                c = Math.Max(0, Math.Min(0.004, c));
                double a0 = (s[2] - c * s[1]) / n;
                return Mathf.Clamp((float)(a0 + c * v * v), 1.5f, 9f);
            }
            return 4.5f;
        }
    }
}
