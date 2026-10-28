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
        static string KeyFor(int trackId) => trackId >= 3 ? "RaceX.PlayerPace2." + trackId : Key + trackId;
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

        static string BestKey(int trackId) => "RaceX.PlayerPaceBestLap2." + trackId;

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
            var sb = new System.Text.StringBuilder("v2");
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
            bool v2 = data.StartsWith("v2|");
            foreach (var block in data.Split('|'))
            {
                int eq = block.IndexOf('=');
                if (eq <= 0 || !int.TryParse(block.Substring(0, eq), out int id)) continue;
                if (!v2 && id >= 3) continue; // dado de traçado antigo
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
                // Volta precisa ter cobertura quase total do traçado (>= 85% dos trechos amostrados).
                if (!lapDirty && personalBest && filled >= PlayerPaceLearner.Segments * 0.85f && lapTime > 10f)
                    PlayerPaceLearner.CommitLap(trackId, lapBuf, lapTime);
            }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerPace] volta ignorada: " + ex.Message); }
            ResetLap(false);
            lastLap = race != null && entry != null && race.GetProgress(entry) != null ? race.GetProgress(entry).lap : lastLap;
        }

        void Update()
        {
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
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car) || car.pitControlled) { lapDirty = true; return; }
            if (car.IsOffTrack || car.IsReversing) { lapDirty = true; return; }
            if (car.GroundedWheels < 4) return;
            float v = car.ForwardSpeed;
            if (v < 5f) return;
            float along = waypoints.DistanceAlong(car.transform.position, ref hint);
            int seg = PlayerPaceLearner.Seg(along, waypoints.Spline.Length);
            if (v > lapBuf[seg]) lapBuf[seg] = v;
        }

        void OnDisable() { PlayerPaceLearner.Flush(); }
        void OnApplicationPause(bool p) { if (p) PlayerPaceLearner.Flush(); }
    }
}
