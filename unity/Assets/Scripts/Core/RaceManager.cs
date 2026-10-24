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

        /// <summary>Limite REAL de tempo da sessão (0 = sem limite/por distância). Expira encerra a sessão com a ordem atual.</summary>
        public float SessionTimeLeft
        {
            get
            {
                if (sessionTimeLimit <= 0f || State != RaceState.Racing) return -1f;
                return Mathf.Max(0f, sessionTimeLimit - (Time.time - sessionStart));
            }
        }
        float sessionTimeLimit;
        float sessionStart;
        bool timeExpired;
        int lapAtExpiry;

        /// <summary>Volta atual do humano (0 se não há).</summary>
        int HumanLapNow()
        {
            foreach (var e in entries)
            {
                if (!e.isHuman) continue;
                var p = GetProgress(e);
                return p?.lap ?? 0;
            }
            return 0;
        }

        /// <summary>Volta atual de um carro qualquer (IA/treino).</summary>
        public int LapOf(CarController c)
        {
            foreach (var e in entries)
            {
                if (e.car != c) continue;
                var p = GetProgress(e);
                return p?.lap ?? 0;
            }
            return 0;
        }

        bool HumanFinished()
        {
            foreach (var e in entries)
                if (e.isHuman && e.finished) return true;
            return false;
        }
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

        public RaceEntry SpawnCar(CarSpec spec, int colorIndex, string driverName, int slot, bool human, int playerIndex, Pose? customPose = null)
        {
            Pose pose = customPose ?? Track.GetGridPose(slot);
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
            // Treino da carreira: restaura a melhor volta JÁ gravada no save — sair pro menu/hub
            // não apaga o que foi feito (não precisa refazer as voltas).
            if (Settings.laps >= 900 && Settings.careerPhase >= 0)
                entry.bestLap = RaceX.Career.CareerMode.LoadPracticeBest(driverName);
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
            ConfigureSessionTime(Settings.sessionTimeLimit);
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
            // Treino da carreira: grava a melhor volta no SAVE — sair pro hub/menu não perde as voltas feitas.
            if (best && Settings.laps >= 900 && Settings.careerPhase >= 0)
                Career.CareerMode.SavePracticeBest(e.name, e.bestLap);
            if (e.isHuman) SettingsManager.TrySaveBestLap((int)Settings.track, (int)e.model, lapTime);
            LapCompleted?.Invoke(e, lapTime, best);

            if (lapNumber >= Settings.laps) Finish(e, now);
        }

        /// <summary>Configura o limite de tempo da sessão (chamado no início da corrida).</summary>
        public void ConfigureSessionTime(float limitSeconds)
        {
            sessionTimeLimit = Mathf.Max(0f, limitSeconds);
            sessionStart = Time.time;
        }

        /// <summary>Fim de sessão por tempo: congela a ordem atual e mostra resultados.</summary>
        void EndByTime()
        {
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
            // Relógio REAL da carreira: tempo passado em menu/hub também desconta (deadline UTC).
            if (Settings.careerPhase >= 0 && sessionTimeLimit > 0f)
            {
                int careerLeft = RaceX.Career.CareerMode.SessionSecondsLeft();
                if (careerLeft > 0)
                {
                    float realLimit = (Time.time - sessionStart) + careerLeft;
                    if (realLimit < sessionTimeLimit) sessionTimeLimit = realLimit;
                }
            }
            // Fim POR TEMPO com volta de cortesia: expirou? quem está na volta completa, então encerra.
            if (sessionTimeLimit > 0f && Time.time - sessionStart >= sessionTimeLimit)
            {
                if (!timeExpired)
                {
                    timeExpired = true;
                    lapAtExpiry = HumanLapNow();
                }
                if (HumanLapNow() > lapAtExpiry || HumanFinished()) { EndByTime(); }
                return;
            }
            foreach (var e in entries) e.raceDistance = Track.Checkpoints.RaceDistance(e.car);
            if (Settings.laps >= 900)
            {
                // SESSÃO CRONOMETRADA (treino/classi): placar AO VIVO é por MELHOR VOLTA —
                // quem tem tempo sobe na hora; quem está com 0 mantém a ordem INICIAL (grid).
                // Não espera ultrapassagem nem distância — isso é corrida, não sessão de tempo.
                entries.Sort((a, b) =>
                {
                    bool ha = a.bestLap > 0f, hb = b.bestLap > 0f;
                    if (ha && hb) return a.bestLap.CompareTo(b.bestLap);
                    if (ha != hb) return ha ? -1 : 1;
                    return a.gridSlot.CompareTo(b.gridSlot);
                });
            }
            else
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
            // Sessão só por tempo (treinos/classificação): ordem SEMPRE por MELHOR VOLTA,
            // nunca por distância/ultrapassagem (o quem andou mais tempo não é o mais rápido).
            if (Settings.laps >= 900)
                list.Sort((a, b) =>
                {
                    bool ha = a.bestLap > 0f, hb = b.bestLap > 0f;
                    if (ha && hb) return a.bestLap.CompareTo(b.bestLap);
                    if (ha != hb) return ha ? -1 : 1;
                    return a.gridSlot.CompareTo(b.gridSlot);
                });
            return list;
        }
    }
}
