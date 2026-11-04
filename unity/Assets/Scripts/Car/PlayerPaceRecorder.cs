using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// Memória da pilotagem do jogador: maior velocidade LIMPA por trecho da pista (128 trechos por pista).
    /// A IA Difícil/Expert usa esses números em CarAI.CornerSpeedAhead (limitados pela física).
    /// Erros são descartados: só grava com 4 rodas no asfalto, fora do pit e com a corrida rodando.
    /// </summary>
    public static class PlayerPaceLearner
    {
        public const int Segments = 128;
        const string Key = "RaceX.PlayerPace.";
        // Pistas 3+ foram redesenhadas: amostras antigas não valem mais.
        // v99: a física dos monopostos mudou (pneu µ 2,9 → 1,8, apoio menor) — as voltas gravadas antes eram de um carro que
        // fazia curva rápida de pé embaixo; recomeça em todas as pistas.
        static string KeyFor(int trackId) => "RaceX.PlayerPace3." + trackId;
        static readonly System.Collections.Generic.Dictionary<int, float[]> cache = new System.Collections.Generic.Dictionary<int, float[]>();
        static readonly System.Collections.Generic.HashSet<int> dirty = new System.Collections.Generic.HashSet<int>();

        static float[] Get(int trackId)
        {
            if (cache.TryGetValue(trackId, out var arr)) return arr;
            arr = new float[Segments];
            string raw = PlayerPrefs.GetString(KeyFor(trackId), "");
            if (!string.IsNullOrEmpty(raw))
            {
                var parts = raw.Split(';');
                for (int i = 0; i < parts.Length && i < Segments; i++)
                    float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
            }
            cache[trackId] = arr;
            return arr;
        }

        public static int Seg(float along, float length)
        {
            if (length <= 1f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(along, length) / length * Segments), 0, Segments - 1);
        }

        public static void Record(int trackId, float along, float length, float speed)
        {
            var arr = Get(trackId);
            int i = Seg(along, length);
            if (speed > arr[i]) { arr[i] = speed; dirty.Add(trackId); }
        }

        static string BestKey(int trackId) => "RaceX.PlayerPaceBestLap3." + trackId;

        /// <summary>Melhor volta LIMPA já usada como referência nesta pista (0 = nenhuma).</summary>
        public static float BestCleanLap(int trackId) => PlayerPrefs.GetFloat(BestKey(trackId), 0f);

        /// <summary>v79: grava o perfil de velocidade da volta INTEIRA (só volta limpa e melhor pessoal).
        /// Trechos sem amostra mantêm o valor anterior.</summary>
        public static void CommitLap(int trackId, float[] lapSpeeds, float lapTime)
        {
            if (lapSpeeds == null || lapSpeeds.Length < Segments) return;
            var arr = Get(trackId);
            for (int i = 0; i < Segments; i++)
                if (lapSpeeds[i] > 5f && lapSpeeds[i] < 120f) arr[i] = lapSpeeds[i];
            dirty.Add(trackId);
            PlayerPrefs.SetFloat(BestKey(trackId), lapTime);
            Flush();
        }

        /// <summary>Velocidade limpa do jogador (m/s) no trecho; 0 se ainda não há dado.</summary>
        public static float SampleSpeed(int trackId, float along, float length)
        {
            return Get(trackId)[Seg(along, length)];
        }

        /// <summary>Perfil do jogador nesta pista (cópia, m/s por trecho) — null se tiver menos da metade dos trechos (sem
        /// volta limpa suficiente). ONLINE: é o "ritmo do próprio jogador" que o piloto automático usa.</summary>
        public static float[] Profile(int trackId)
        {
            var arr = Get(trackId);
            int filled = 0;
            foreach (float v in arr) if (v > 5f) filled++;
            if (filled < Segments / 2) return null;
            return (float[])arr.Clone();
        }

        /// <summary>Perfil desta pista em texto ("v;v;...") para mandar ao servidor ao carregar a corrida online ("" = sem perfil).</summary>
        public static string ExportTrack(int trackId)
        {
            var arr = Profile(trackId);
            if (arr == null) return "";
            var sb = new System.Text.StringBuilder(Segments * 5);
            for (int i = 0; i < Segments; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(arr[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Lê o perfil vindo de outro jogador (online). null se vazio/inválido.</summary>
        public static float[] ParseProfile(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var parts = raw.Split(';');
            if (parts.Length < Segments / 2) return null;
            var arr = new float[Segments];
            int filled = 0;
            for (int i = 0; i < parts.Length && i < Segments; i++)
                if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 5f && v < 120f)
                { arr[i] = v; filled++; }
            return filled >= Segments / 2 ? arr : null;
        }

        public static void Flush()
        {
            if (dirty.Count == 0) return;
            var sb = new System.Text.StringBuilder(Segments * 6);
            foreach (int id in dirty)
            {
                var arr = Get(id);
                sb.Length = 0;
                for (int i = 0; i < Segments; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(arr[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
                PlayerPrefs.SetString(KeyFor(id), sb.ToString());
            }
            dirty.Clear();
            PlayerPrefs.Save();
        }

        /// <summary>Exporta "id=v;v;...|id=..." de todas as pistas com dado (para a nuvem).</summary>
        public static string ExportAll()
        {
            Flush();
            var sb = new System.Text.StringBuilder("v3");
            foreach (int id in System.Enum.GetValues(typeof(TrackId)))
            {
                string raw = PlayerPrefs.GetString(KeyFor(id), "");
                if (string.IsNullOrEmpty(raw)) continue;
                sb.Append('|');
                sb.Append(id).Append('=').Append(raw);
            }
            return sb.Length > 2 ? sb.ToString() : "";
        }

        /// <summary>Mescla dados da nuvem: por trecho fica a MAIOR velocidade limpa (local ou nuvem).</summary>
        public static void ImportMerge(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            // Só dados da física atual (v3, v99): perfis de antes vinham do carro de pneu 2,9 e voltariam pela nuvem.
            if (!data.StartsWith("v3|")) return;
            foreach (var block in data.Split('|'))
            {
                int eq = block.IndexOf('=');
                if (eq <= 0 || !int.TryParse(block.Substring(0, eq), out int id)) continue;
                var arr = Get(id);
                var parts = block.Substring(eq + 1).Split(';');
                bool changed = false;
                for (int i = 0; i < parts.Length && i < Segments; i++)
                    if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, ic, out float v) && v > arr[i] && v < 120f)
                    { arr[i] = v; changed = true; }
                if (changed) dirty.Add(id);
            }
            Flush();
        }
    }

    /// <summary>Vai no carro do jogador. v79: amostra a 10 Hz num BUFFER DA VOLTA ATUAL; a volta é
    /// descartada se sair da pista, bater, dar ré, entrar no pit ou estiver na volta de saída. Só ao
    /// COMPLETAR uma volta limpa que seja a melhor pessoal na pista o perfil é gravado (antes gravava
    /// trechos soltos o tempo todo, inclusive de voltas com erro).</summary>
    public class PlayerPaceRecorder : MonoBehaviour
    {
        CarController car;
        WaypointSystem waypoints;
        RaceManager race;
        RaceEntry entry;
        int trackId;
        int hint;
        float sampleTimer;
        readonly float[] lapBuf = new float[PlayerPaceLearner.Segments];
        bool lapDirty = true; // primeira volta (largada/saída do box) nunca conta
        int lastLap = -1;
        CarAI aiPilot;
        // v99: ESTILO da volta atual (PlayerStyle) — traçado, freio, tirar o pé, desaceleração e volante por trecho.
        readonly float[] latSum = new float[PlayerPaceLearner.Segments];
        readonly int[] nBuf = new int[PlayerPaceLearner.Segments];
        readonly float[] brakeSum = new float[PlayerPaceLearner.Segments];
        readonly float[] coastN = new float[PlayerPaceLearner.Segments];
        readonly float[] decelBuf = new float[PlayerPaceLearner.Segments];
        readonly float[] steerSum = new float[PlayerPaceLearner.Segments];
        float prevV = -1f, prevSteer, prevT = -1f;
        int lapCompound = -1;
        bool lapWet, lapMixed;

        public void Initialize(CarController c, WaypointSystem wp, RaceManager rm, int track)
        {
            car = c; waypoints = wp; race = rm; trackId = track;
            entry = rm != null ? rm.GetEntry(c) : null;
            if (rm != null) rm.LapCompleted += OnLap;
            if (car != null) car.Collided += OnCollided;
        }

        void OnDestroy()
        {
            if (race != null) race.LapCompleted -= OnLap;
            if (car != null) car.Collided -= OnCollided;
        }

        void OnCollided(float relative, Vector3 point)
        {
            if (relative > 12f) lapDirty = true;
        }

        void ResetLap(bool dirtyStart)
        {
            System.Array.Clear(lapBuf, 0, lapBuf.Length);
            System.Array.Clear(latSum, 0, latSum.Length);
            System.Array.Clear(nBuf, 0, nBuf.Length);
            System.Array.Clear(brakeSum, 0, brakeSum.Length);
            System.Array.Clear(coastN, 0, coastN.Length);
            System.Array.Clear(decelBuf, 0, decelBuf.Length);
            System.Array.Clear(steerSum, 0, steerSum.Length);
            prevV = -1f; prevT = -1f;
            lapCompound = car != null ? RaceX.Core.PitLaneSystem.CompoundOf(car) : -1;
            lapWet = RaceX.Core.WeatherSystem.Wetness >= PlayerStyle.WetFrom;
            lapMixed = false;
            lapDirty = dirtyStart;
        }

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e == null || e.car != car) return;
            try
            {
                int filled = 0;
                for (int i = 0; i < lapBuf.Length; i++) if (lapBuf[i] > 5f) filled++;
                float prevBest = PlayerPaceLearner.BestCleanLap(trackId);
                bool personalBest = prevBest <= 0f || lapTime < prevBest;
                bool covered = filled >= PlayerPaceLearner.Segments * 0.85f && lapTime > 10f;
                // Volta precisa ter cobertura quase total do traçado (>= 85% dos trechos amostrados). Volta do piloto automático
                // do autoteste (stylerecord=1) só alimenta o arquivo de estilo do teste — nunca o perfil de ritmo do aparelho.
                if (!lapDirty && personalBest && covered && !RaceX.Core.AutoTest.StyleFromAutopilot)
                    PlayerPaceLearner.CommitLap(trackId, lapBuf, lapTime);
                // Estilo: melhor volta limpa NESTA condição (pista, tipo de carro, pneu, seco/molhado) — volta que mudou de pneu
                // ou atravessou o limite de pista molhada não conta.
                if (!lapDirty && !lapMixed && covered && lapCompound >= 0) PlayerStyle.Commit(BuildStyle(lapTime));
                // Aba GLOBAL: só volta limpa e completa vira recorde (pessoal → nuvem, com conta).
                if (!lapDirty && covered) RaceX.Core.GlobalBoard.OnCleanLap(trackId, car.spec, lapTime, lapCompound, lapWet);
            }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerPace] volta ignorada: " + ex.Message); }
            ResetLap(false);
            lastLap = race != null && entry != null && race.GetProgress(entry) != null ? race.GetProgress(entry).lap : lastLap;
        }

        PlayerStyle.Profile BuildStyle(float lapTime)
        {
            var s = car.spec;
            var p = new PlayerStyle.Profile
            {
                track = trackId,
                formula = s != null && CarSpec.IsFormula(s.model),
                compound = lapCompound,
                wet = lapWet,
                lapTime = lapTime,
                when = System.DateTime.Now.ToString("s"),
                massKg = car.Body != null ? car.Body.mass : (s != null ? s.massKg : 0f),
                downforce = s != null ? s.downforceCoefficient : 0f,
                forwardStiffness = s != null ? s.forwardStiffness : 0f,
            };
            for (int i = 0; i < PlayerStyle.Segments; i++)
            {
                p.v[i] = lapBuf[i];
                int k = nBuf[i];
                p.n[i] = k;
                if (k == 0) continue;
                p.lat[i] = latSum[i] / k;
                p.brake[i] = brakeSum[i] / k;
                p.coast[i] = coastN[i] / k;
                p.decel[i] = decelBuf[i];
                p.steerRate[i] = steerSum[i] / k;
            }
            return p;
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (car == null || waypoints == null || waypoints.Spline == null) return;
            if (race != null && entry != null)
            {
                var p = race.GetProgress(entry);
                int lap = p != null ? p.lap : 0;
                // Volta mudou sem passar pelo OnLap (volta de saída, cruzamento pós-pit): descarta.
                if (lap != lastLap) { if (lastLap >= 0) ResetLap(entry.outLap); lastLap = lap; }
                if (entry.outLap) lapDirty = true;
            }
            sampleTimer -= Time.deltaTime;
            if (sampleTimer > 0f) return;
            sampleTimer = 0.1f;
            if (race != null && race.State != RaceState.Racing) return;
            // Piloto automático (autoteste) no carro do jogador: NÃO é pilotagem do jogador — não grava (exceto stylerecord=1,
            // que testa o caminho do estilo num arquivo separado).
            if (aiPilot == null) aiPilot = car.GetComponent<CarAI>();
            if (aiPilot != null && aiPilot.enabled && !RaceX.Core.AutoTest.StyleFromAutopilot) { lapDirty = true; return; }
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car) || car.pitControlled) { lapDirty = true; return; }
            if (car.IsOffTrack || car.IsReversing) { lapDirty = true; return; }
            if (car.GroundedWheels < 4) { prevT = -1f; return; }
            float v = car.ForwardSpeed;
            if (v < 5f) { prevT = -1f; return; }
            float along = waypoints.DistanceAlong(car.transform.position, ref hint);
            int seg = PlayerPaceLearner.Seg(along, waypoints.Spline.Length);
            if (v > lapBuf[seg]) lapBuf[seg] = v;
            // Estilo: onde ele passa (lateral), freio, só tirando o pé, desaceleração na freada e ritmo do volante.
            var pose = waypoints.Spline.PoseAtDistance(along);
            latSum[seg] += Vector3.Dot(car.transform.position - pose.position, pose.rotation * Vector3.right);
            nBuf[seg]++;
            brakeSum[seg] += Mathf.Clamp01(car.brakeInput);
            if (car.throttleInput < 0.08f && car.brakeInput < 0.05f) coastN[seg]++;
            float now = Time.time, dt = prevT > 0f ? now - prevT : 0f;
            if (dt > 0.04f && dt < 0.3f)
            {
                if (car.brakeInput > 0.1f && prevV > 0f) decelBuf[seg] = Mathf.Max(decelBuf[seg], Mathf.Min(60f, (prevV - v) / dt));
                steerSum[seg] += Mathf.Abs(car.steerInput - prevSteer) / dt;
            }
            prevV = v; prevSteer = car.steerInput; prevT = now;
            if ((RaceX.Core.WeatherSystem.Wetness >= PlayerStyle.WetFrom) != lapWet || RaceX.Core.PitLaneSystem.CompoundOf(car) != lapCompound) lapMixed = true;
        }

        void OnDisable() { PlayerPaceLearner.Flush(); }
        void OnApplicationPause(bool p) { if (p) PlayerPaceLearner.Flush(); }
    }
}
