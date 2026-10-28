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
        static readonly System.Collections.Generic.Dictionary<int, float[]> cache = new System.Collections.Generic.Dictionary<int, float[]>();
        static readonly System.Collections.Generic.HashSet<int> dirty = new System.Collections.Generic.HashSet<int>();

        static float[] Get(int trackId)
        {
            if (cache.TryGetValue(trackId, out var arr)) return arr;
            arr = new float[Segments];
            string raw = PlayerPrefs.GetString(Key + trackId, "");
            if (!string.IsNullOrEmpty(raw))
            {
                var parts = raw.Split(';');
                for (int i = 0; i < parts.Length && i < Segments; i++)
                    float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
            }
            cache[trackId] = arr;
            return arr;
        }

        static int Seg(float along, float length)
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
                PlayerPrefs.SetString(Key + id, sb.ToString());
            }
            dirty.Clear();
            PlayerPrefs.Save();
        }

        /// <summary>Exporta "id=v;v;...|id=..." de todas as pistas com dado (para a nuvem).</summary>
        public static string ExportAll()
        {
            Flush();
            var sb = new System.Text.StringBuilder();
            foreach (int id in System.Enum.GetValues(typeof(TrackId)))
            {
                string raw = PlayerPrefs.GetString(Key + id, "");
                if (string.IsNullOrEmpty(raw)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(id).Append('=').Append(raw);
            }
            return sb.ToString();
        }

        /// <summary>Mescla dados da nuvem: por trecho fica a MAIOR velocidade limpa (local ou nuvem).</summary>
        public static void ImportMerge(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
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

    /// <summary>Vai no carro do jogador; amostra a 10 Hz e salva a cada 15 s / ao sair.</summary>
    public class PlayerPaceRecorder : MonoBehaviour
    {
        CarController car;
        WaypointSystem waypoints;
        RaceManager race;
        int trackId;
        int hint;
        float sampleTimer, flushTimer;

        public void Initialize(CarController c, WaypointSystem wp, RaceManager rm, int track)
        {
            car = c; waypoints = wp; race = rm; trackId = track;
        }

        void Update()
        {
            if (car == null || waypoints == null || waypoints.Spline == null) return;
            sampleTimer -= Time.deltaTime;
            flushTimer -= Time.unscaledDeltaTime;
            if (flushTimer <= 0f) { flushTimer = 15f; PlayerPaceLearner.Flush(); }
            if (sampleTimer > 0f) return;
            sampleTimer = 0.1f;
            if (race != null && race.State != RaceState.Racing) return;
            if (car.IsOffTrack || car.GroundedWheels < 4 || car.IsReversing) return;
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car)) return;
            float v = car.ForwardSpeed;
            if (v < 5f) return;
            float along = waypoints.DistanceAlong(car.transform.position, ref hint);
            PlayerPaceLearner.Record(trackId, along, waypoints.Spline.Length, v);
        }

        void OnDisable() { PlayerPaceLearner.Flush(); }
        void OnApplicationPause(bool p) { if (p) PlayerPaceLearner.Flush(); }
    }
}
