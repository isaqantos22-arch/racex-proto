using System;
using System.Collections.Generic;
using RaceX.Car;
using UnityEngine;

namespace RaceX.Track
{
    public class CheckpointTrigger : MonoBehaviour
    {
        public int index;
        public int splineIndex;
        public CheckpointManager manager;

        void OnTriggerEnter(Collider other)
        {
            var car = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<CarController>() : null;
            if (car != null && manager != null) manager.CarPassed(car, index);
        }

        public Pose RespawnPose => new Pose(transform.position - Vector3.up * 2.4f, transform.rotation);
    }

    public class RaceProgress
    {
        public int lap = 1;
        public int lastCheckpoint;
        public int nextCheckpoint;
        public bool crossedStart;
        public int splineHint = -1;
        public float wrongWayTimer;
        public bool WrongWay => wrongWayTimer >= 2f;
        public Pose respawn;
        public Pose spawnPose; // pose do spawn inicial (box no treino com pit) — p/ saber se o respawn já virou checkpoint de pista
        public bool leftPit;   // TRUE = carro já saiu do pit e correu na pista → respawn NUNCA mais é o box (regra do usuário)
        public float distanceInLap;
    }

    public class CheckpointManager : MonoBehaviour
    {
        public const float WrongWaySeconds = 2f;

        readonly Dictionary<CarController, RaceProgress> progress = new Dictionary<CarController, RaceProgress>();
        List<CheckpointTrigger> triggers;
        TrackBuilder track;

        public event Action<CarController, int> LapCompleted;
        public event Action<CarController, int> CheckpointPassed;

        public int Count => triggers.Count;

        public void Initialize(TrackBuilder builder, List<CheckpointTrigger> list)
        {
            track = builder;
            triggers = list;
            foreach (var t in triggers) t.manager = this;
        }

        public RaceProgress Register(CarController car, Pose spawn)
        {
            var p = new RaceProgress { respawn = spawn, spawnPose = spawn };
            progress[car] = p;
            return p;
        }

        public void Unregister(CarController car) => progress.Remove(car);

        public RaceProgress Get(CarController car) => progress.TryGetValue(car, out var p) ? p : null;

        public void CarPassed(CarController car, int index)
        {
            if (!progress.TryGetValue(car, out var p) || index != p.nextCheckpoint) return;

            if (index == 0)
            {
                if (p.crossedStart)
                {
                    int completed = p.lap;
                    p.lap++;
                    LapCompleted?.Invoke(car, completed);
                }
                p.crossedStart = true;
            }
            p.lastCheckpoint = index;
            p.nextCheckpoint = (index + 1) % triggers.Count;
            p.respawn = triggers[index].RespawnPose;
            p.leftPit = true; // cruzou checkpoint na pista: já saiu do pit (se estava nele)
            CheckpointPassed?.Invoke(car, index);
        }

        /// <summary>Marca que o carro JÁ CORREU na pista (saiu do pit). Uma vez marcado, o respawn
        /// de saída de pista NUNCA mais volta ao box — renasce na pista, perto de onde estava.</summary>
        public void MarkOnTrack(CarController car)
        {
            if (progress.TryGetValue(car, out var p)) p.leftPit = true;
        }

        /// <summary>REGRA DO JOGADOR: depois da 1ª saída do pit, o respawn de saída de pista é
        /// SEMPRE na pista — nunca a pose do box. Se os checkpoints ainda não atualizaram o respawn
        /// (ele ainda é o spawn inicial do box), gera um respawn no ponto do spline da pista mais
        /// próximo do carro: renasce perto de onde saiu, sem voltar ao pit.</summary>
        public void EnsureTrackRespawn(CarController car)
        {
            if (!progress.TryGetValue(car, out var p) || track == null || !p.leftPit) return;
            if (p.respawn.position != p.spawnPose.position) return; // já é checkpoint da pista
            var spl = track.Waypoints.Spline;
            int idx = spl.ClosestIndex(car.transform.position, -1);
            p.respawn = spl.PoseAtDistance(idx * spl.spacing);
        }

        public float RaceDistance(CarController car)
        {
            if (!progress.TryGetValue(car, out var p)) return 0f;
            float length = track.Waypoints.Length;
            float d = track.Waypoints.DistanceAlong(car.transform.position, ref p.splineHint);
            if ((!p.crossedStart || p.lastCheckpoint == 0) && d > length * 0.5f) d -= length;

            p.distanceInLap = d;
            return (p.lap - 1) * length + d;
        }

        /// <summary>REALINHA o progresso de checkpoints com a posição ATUAL do carro (após a saída
        /// do pit). Sem isso, o carro saía do pit com nextCheckpoint velho: o cruzamento da linha
        /// não disparava LapCompleted e o cronômetro de volta acumulava 2 voltas antes de zerar.
        /// Marca crossedStart (a linha passa a valer) e aponta nextCheckpoint para o trigger À FRENTE.</summary>
        public void ReSyncAfterPit(CarController car)
        {
            if (!progress.TryGetValue(car, out var p) || triggers == null || triggers.Count == 0) return;
            float length = track.Waypoints.Length;
            int hint = p.splineHint;
            float d = track.Waypoints.DistanceAlong(car.transform.position, ref hint);
            int best = 0; float bestGap = float.MaxValue;
            for (int i = 0; i < triggers.Count; i++)
            {
                int h = -1;
                float dt = track.Waypoints.DistanceAlong(triggers[i].transform.position, ref h);
                float gap = Mathf.Repeat(dt - d, length);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            p.nextCheckpoint = best;
            p.lastCheckpoint = (best - 1 + triggers.Count) % triggers.Count;
            p.crossedStart = true;
        }

        void Update()
        {
            foreach (var kv in progress)
            {
                var car = kv.Key;
                if (car == null) continue;
                var p = kv.Value;
                Vector3 v = car.Velocity;
                if (p.splineHint < 0 || v.magnitude < 3f) { p.wrongWayTimer = 0f; continue; }
                float dot = Vector3.Dot(v.normalized, track.Waypoints.DirectionAt(p.splineHint));
                p.wrongWayTimer = dot < -0.3f ? p.wrongWayTimer + Time.deltaTime : 0f;
            }
        }
    }
}
