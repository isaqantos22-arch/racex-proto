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
            var p = new RaceProgress { respawn = spawn };
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
            CheckpointPassed?.Invoke(car, index);
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
