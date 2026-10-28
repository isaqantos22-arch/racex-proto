using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Core
{
    public class SectorTimer : MonoBehaviour
    {
        public enum SectorColor { None, Purple, Green, Yellow }

        const int Ring = 2048;
        const float Bucket = 10f;

        class State
        {
            public int sector = -1;
            public float start;
            public bool valid;
            public readonly float[] lap = new float[3];
            public readonly float[] last = new float[3];
            public readonly float[] pb = new float[3];
            public readonly SectorColor[] color = new SectorColor[3];
            public readonly float[] delta = new float[3];
            public readonly bool[] hasDelta = new bool[3];
            public readonly int[] bucket = new int[Ring];
            public readonly float[] time = new float[Ring];
            public int lastBucket = int.MinValue;
        }

        public static SectorTimer Instance { get; private set; }

        RaceManager race;
        readonly Dictionary<RaceEntry, State> states = new Dictionary<RaceEntry, State>();
        readonly float[] sessionBest = new float[3];

        public static SectorTimer Ensure(RaceManager race)
        {
            if (Instance != null) return Instance;
            var t = new GameObject("SectorTimer").AddComponent<SectorTimer>();
            t.race = race;
            race.LapCompleted += t.OnLap;
            Instance = t;
            return t;
        }

        void OnDestroy()
        {
            if (race != null) race.LapCompleted -= OnLap;
            if (Instance == this) Instance = null;
        }

        State Get(RaceEntry e)
        {
            if (!states.TryGetValue(e, out var s))
            {
                s = new State();
                for (int i = 0; i < Ring; i++) s.bucket[i] = int.MinValue;
                states[e] = s;
            }
            return s;
        }

        float LapLength => race != null && race.Track != null && race.Track.Waypoints != null ? race.Track.Waypoints.Length : 0f;

        void Update()
        {
            if (race == null || race.State != RaceState.Racing) return;
            float L = LapLength;
            if (L <= 1f) return;
            float now = Time.time;
            foreach (var e in race.entries)
            {
                if (e == null || e.car == null) continue;
                var s = Get(e);
                RecordGap(s, e, now);
                var p = race.GetProgress(e);
                if (p == null || e.car.pitControlled) { s.valid = false; continue; }
                int k = p.distanceInLap < 0f ? -1 : Mathf.Clamp(Mathf.FloorToInt(p.distanceInLap / (L / 3f)), 0, 2);
                if (k == s.sector) continue;
                bool next = s.sector >= 0 && (k == s.sector + 1 || (s.sector == 2 && k == 0));
                if (next && s.valid) Close(s, s.sector, now - s.start);
                if (s.sector == 2 && k == 0) for (int i = 0; i < 3; i++) s.lap[i] = 0f;
                s.valid = next || (s.sector == -1 && k == 0 && p.distanceInLap < 15f);
                if (e.outLap) s.valid = false; // volta de saída do box não cronometra
                s.sector = k;
                s.start = now;
            }
        }

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e == null) return;
            var s = Get(e);
            if (s.sector != 2) return;
            float now = Time.time;
            if (s.valid) Close(s, 2, now - s.start);
            for (int i = 0; i < 3; i++) s.lap[i] = 0f;
            s.sector = 0;
            s.start = now;
            s.valid = e.car != null && !e.car.pitControlled;
        }

        void Close(State s, int k, float t)
        {
            if (t < 1f) return;
            s.lap[k] = t;
            s.last[k] = t;
            s.hasDelta[k] = s.pb[k] > 0f;
            s.delta[k] = s.hasDelta[k] ? t - s.pb[k] : 0f;
            if (sessionBest[k] <= 0f || t <= sessionBest[k]) { sessionBest[k] = t; s.color[k] = SectorColor.Purple; }
            else if (s.pb[k] <= 0f || t <= s.pb[k]) s.color[k] = SectorColor.Green;
            else s.color[k] = SectorColor.Yellow;
            if (s.pb[k] <= 0f || t < s.pb[k]) s.pb[k] = t;
        }

        void RecordGap(State s, RaceEntry e, float now)
        {
            int b = Mathf.FloorToInt(e.raceDistance / Bucket);
            if (b == s.lastBucket) return;
            if (s.lastBucket != int.MinValue && b > s.lastBucket && b - s.lastBucket < 50)
            {
                for (int i = s.lastBucket + 1; i <= b; i++) { s.bucket[i & (Ring - 1)] = i; s.time[i & (Ring - 1)] = now; }
            }
            else { s.bucket[b & (Ring - 1)] = b; s.time[b & (Ring - 1)] = now; }
            s.lastBucket = b;
        }

        /// <summary>Início da 1ª volta cronometrada após a volta de saída do box.</summary>
        public void BeginTimedLap(RaceEntry e)
        {
            if (e == null) return;
            var s = Get(e);
            for (int i = 0; i < 3; i++) s.lap[i] = 0f;
            s.sector = 0;
            s.start = Time.time;
            s.valid = e.car != null && !e.car.pitControlled;
        }

        public bool SectorDelta(RaceEntry e, int k, out float d)
        {
            d = 0f;
            if (e == null || !states.TryGetValue(e, out var s) || !s.hasDelta[k]) return false;
            d = s.delta[k];
            return true;
        }

        public int CurrentSector(RaceEntry e) => e != null && states.TryGetValue(e, out var s) ? s.sector : -1;
        public float CurrentSectorElapsed(RaceEntry e) => e != null && states.TryGetValue(e, out var s) && s.sector >= 0 ? Time.time - s.start : 0f;
        public float LapSectorTime(RaceEntry e, int k) => e != null && states.TryGetValue(e, out var s) ? s.lap[k] : 0f;
        public float LastSectorTime(RaceEntry e, int k) => e != null && states.TryGetValue(e, out var s) ? s.last[k] : 0f;
        public float PersonalBest(RaceEntry e, int k) => e != null && states.TryGetValue(e, out var s) ? s.pb[k] : 0f;
        public float SessionBest(int k) => sessionBest[k];
        public SectorColor ColorOf(RaceEntry e, int k) => e != null && states.TryGetValue(e, out var s) ? s.color[k] : SectorColor.None;
        public bool CurrentSectorValid(RaceEntry e) => e != null && states.TryGetValue(e, out var s) && s.valid;

        public float GapSeconds(RaceEntry ahead, RaceEntry behind)
        {
            if (ahead == null || behind == null) return 0f;
            int bb = Mathf.FloorToInt(behind.raceDistance / Bucket);
            if (states.TryGetValue(ahead, out var sa) && sa.bucket[bb & (Ring - 1)] == bb)
                return Mathf.Max(0f, Time.time - sa.time[bb & (Ring - 1)]);
            float v = behind.car != null ? Mathf.Max(behind.car.SpeedKmh / 3.6f, 5f) : 5f;
            return Mathf.Max(0f, (ahead.raceDistance - behind.raceDistance) / v);
        }
    }
}
