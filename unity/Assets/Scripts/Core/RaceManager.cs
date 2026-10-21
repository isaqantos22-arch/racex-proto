using System;
using System.Collections;
using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    public enum RaceState { Setup, Countdown, Racing, Finished }

    public class RaceEntry
    {
        public CarController car;
        public string name;
        public bool isHuman;
        public int playerIndex = -1;
        public int gridSlot;
        public CarModel model;
        public int colorIndex;
        public readonly List<float> lapTimes = new List<float>();
        public float lapStartTime;
        public float bestLap;
        public bool finished;
        public float totalTime;
        public int position;
        public float raceDistance;
        public int CurrentLap(int laps, RaceProgress p) => Mathf.Clamp(p != null ? p.lap : 1, 1, laps);
    }

    public class RaceManager : MonoBehaviour
    {
        public const float CountdownStep = 1f;
        public const float FinishScreenDelay = 3f;

        public TrackBuilder Track { get; private set; }
        public RaceSettings Settings { get; private set; }
        public RaceState State { get; private set; } = RaceState.Setup;
        public int CountdownValue { get; private set; }
        public float RaceTime => State == RaceState.Racing || State == RaceState.Finished ? Time.time - raceStartTime : 0f;
        public readonly List<RaceEntry> entries = new List<RaceEntry>();
        public readonly List<RaceEntry> finishOrder = new List<RaceEntry>();

        public event Action<int> CountdownTick;
        public event Action RaceStarted;
        public event Action<RaceEntry, float, bool> LapCompleted;
        public event Action<RaceEntry> EntryFinished;
        public event Action RaceFinished;

        float raceStartTime;
        readonly Dictionary<CarController, RaceEntry> byCar = new Dictionary<CarController, RaceEntry>();

        public void Initialize(TrackBuilder track, RaceSettings settings)
        {
            Track = track;
            Settings = settings;
            track.Checkpoints.LapCompleted += OnLapCompleted;
        }

        public RaceEntry SpawnCar(CarSpec spec, int colorIndex, string driverName, int slot, bool human, int playerIndex)
        {
            Pose pose = Track.GetGridPose(slot);
            var car = CarFactory.Create(spec, colorIndex, (human ? "Player_" : "NPC_") + driverName, pose.position, pose.rotation);
            car.inputLocked = true;
            var bounds = car.gameObject.AddComponent<TrackBounds>();
            bounds.checkpoints = Track.Checkpoints;
            Track.Checkpoints.Register(car, pose);

            var entry = new RaceEntry
            {
                car = car, name = driverName, isHuman = human, playerIndex = playerIndex, gridSlot = slot,
                model = spec.model, colorIndex = colorIndex, position = slot + 1
            };
            entries.Add(entry);
            byCar[car] = entry;
            return entry;
        }

        public RaceEntry GetEntry(CarController car) => car != null && byCar.TryGetValue(car, out var e) ? e : null;

        public RaceProgress GetProgress(RaceEntry e) => Track.Checkpoints.Get(e.car);

        public void BeginCountdown() => StartCoroutine(CountdownRoutine());

        IEnumerator CountdownRoutine()
        {
            State = RaceState.Countdown;
            yield return new WaitForSeconds(0.5f);
            for (int i = 3; i >= 1; i--)
            {
                CountdownValue = i;
                CountdownTick?.Invoke(i);
                yield return new WaitForSeconds(CountdownStep);
            }
            CountdownValue = 0;
            CountdownTick?.Invoke(0);
            StartRace();
        }

        void StartRace()
        {
            State = RaceState.Racing;
            raceStartTime = Time.time;
            foreach (var e in entries)
            {
                e.lapStartTime = raceStartTime;
                e.car.inputLocked = false;
            }
            RaceStarted?.Invoke();
        }

        void OnLapCompleted(CarController car, int lapNumber)
        {
            if (State != RaceState.Racing || !byCar.TryGetValue(car, out var e) || e.finished) return;
            float now = Time.time;
            float lapTime = now - e.lapStartTime;
            e.lapStartTime = now;
            e.lapTimes.Add(lapTime);
            bool best = e.bestLap <= 0f || lapTime < e.bestLap;
            if (best) e.bestLap = lapTime;
            if (e.isHuman) SettingsManager.TrySaveBestLap((int)Settings.track, (int)e.model, lapTime);
            LapCompleted?.Invoke(e, lapTime, best);

            if (lapNumber >= Settings.laps) Finish(e, now);
        }

        void Finish(RaceEntry e, float now)
        {
            e.finished = true;
            e.totalTime = now - raceStartTime;
            finishOrder.Add(e);
            EntryFinished?.Invoke(e);
            if (e.isHuman && entries.TrueForAll(x => !x.isHuman || x.finished)) StartCoroutine(EndRaceRoutine());
        }

        IEnumerator EndRaceRoutine()
        {
            yield return new WaitForSeconds(FinishScreenDelay);
            State = RaceState.Finished;
            foreach (var e in entries)
            {
                if (!e.isHuman || e.car == null) continue;
                var input = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                if (input != null) input.enabled = false;
                if (e.car.GetComponent<CarAI>() == null)
                    e.car.gameObject.AddComponent<CarAI>().Initialize(Track.Waypoints, this, Settings.difficulty);
                e.car.inputLocked = false;
            }
            RaceFinished?.Invoke();
        }

        void Update()
        {
            if (State != RaceState.Racing) return;
            foreach (var e in entries) e.raceDistance = Track.Checkpoints.RaceDistance(e.car);
            entries.Sort((a, b) =>
            {
                if (a.finished && b.finished) return finishOrder.IndexOf(a).CompareTo(finishOrder.IndexOf(b));
                if (a.finished != b.finished) return a.finished ? -1 : 1;
                return b.raceDistance.CompareTo(a.raceDistance);
            });
            for (int i = 0; i < entries.Count; i++) entries[i].position = i + 1;
        }

        public List<RaceEntry> FinalStandings()
        {
            var list = new List<RaceEntry>(finishOrder);
            foreach (var e in entries) if (!list.Contains(e)) list.Add(e);
            return list;
        }
    }
}
