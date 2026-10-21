using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarAI : MonoBehaviour
    {
        public const float StuckSeconds = 2.5f;
        public const float ReverseSeconds = 1f;
        public const float MaxAvoidOffset = 2.2f;
        public const float RubberBandDistance = 200f;
        public const float RubberBandAmount = 0.05f;

        static readonly float[] SpeedPercent = { 0.62f, 0.80f, 1.00f, 1.00f };
        static readonly float[] LineDeviation = { 1.6f, 0.9f, 0.35f, 0.05f };
        static readonly float[] CornerGrip = { 0.85f, 0.98f, 1.15f, 1.35f };

        public CarController car;
        public WaypointSystem waypoints;
        public RaceManager race;
        public Difficulty difficulty = Difficulty.Medium;

        public string DebugState { get; private set; }

        int hint = -1;
        float noiseSeed;
        float avoidOffset;
        float lane;
        float skill;
        float unlockTime = -999f;
        float lateralAtUnlock;
        bool wasLocked = true;
        CarController aheadCar;
        float aheadGap = float.MaxValue;

        public void SetLane(float value) => lane = Mathf.Clamp(value, -1.7f, 1.7f);
        float stuckTimer;
        float reverseTimer;

        public void Initialize(WaypointSystem wp, RaceManager raceManager, Difficulty diff)
        {
            car = GetComponent<CarController>();
            waypoints = wp;
            race = raceManager;
            difficulty = diff;
            noiseSeed = Random.value * 100f;
            skill = Random.Range(0.95f, 1.04f);
        }

        void FixedUpdate()
        {
            if (car == null || waypoints == null || car.spec == null) return;
            if (car.inputLocked)
            {
                car.throttleInput = 0f;
                car.brakeInput = 1f;
                return;
            }

            int d = (int)difficulty;
            float speed = Mathf.Max(0f, car.ForwardSpeed);
            float along = waypoints.DistanceAlong(transform.position, ref hint);
            if (wasLocked && !car.inputLocked)
            {
                unlockTime = Time.time;
                var p0 = waypoints.Spline.PoseAtDistance(along);
                lateralAtUnlock = Vector3.Dot(transform.position - p0.position, p0.rotation * Vector3.right);
            }
            wasLocked = car.inputLocked;
            float deviation = LineDeviation[d] * (Mathf.PerlinNoise(noiseSeed, Time.time * 0.15f) * 2f - 1f);

            UpdateAvoidance(speed);
            float baseLateral = Mathf.Clamp(deviation + lane + avoidOffset, -MaxAvoidOffset, MaxAvoidOffset);
            float launchBlend = Mathf.Clamp01((Time.time - unlockTime) / 6f);
            float lateral = Mathf.Lerp(Mathf.Clamp(lateralAtUnlock, -MaxAvoidOffset, MaxAvoidOffset), baseLateral, launchBlend);

            float lookAhead = 4f + 0.3f * speed;
            Vector3 target = waypoints.Spline.PoseAtDistance(along + lookAhead, lateral).position;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(angle / Mathf.Max(1f, car.spec.maxSteerAngle), -1f, 1f);

            float maxSpeed = car.spec.topSpeedKmh / 3.6f * SpeedPercent[d] * skill * RubberBand();
            float targetSpeed = Mathf.Min(maxSpeed, CornerSpeedAhead(along, speed, d) * skill);
            if (aheadCar != null && Mathf.Abs(avoidOffset) < 1.2f && launchBlend >= 1f)
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, aheadCar.ForwardSpeed) + 1.5f);

            float throttle, brake;
            float error = targetSpeed - speed;
            if (error > 0f)
            {
                throttle = Mathf.Clamp01(error / 3f + 0.3f);
                brake = 0f;
            }
            else
            {
                // Levantada: sem freio na curva, só a desaceleração do motor
                throttle = 0f;
                brake = -error > 12f ? 0.35f : 0f;
            }

            if (UpdateRecovery(speed, ref steer, ref throttle, ref brake)) { }

            DebugState = $"along={along:F0} tgt={targetSpeed * 3.6f:F0} max={maxSpeed * 3.6f:F0} v={speed * 3.6f:F0} thr={throttle:F2} brk={brake:F2} steer={steer:F2} avoid={avoidOffset:F1} rev={reverseTimer:F1} stuck={stuckTimer:F1} gear={car.Gear}";
            car.steerInput = steer;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrake = false;
        }

        float CornerSpeedAhead(float along, float speed, int d)
        {
            const float decel = 9.0f;
            float horizon = speed * speed / (2f * decel) + 18f;
            float best = float.MaxValue;
            var spline = waypoints.Spline;
            for (float s = 0f; s <= horizon; s += waypoints.Spacing)
            {
                float k = Curvature(spline, along + s);
                if (k < 1e-4f) continue;
                float vCorner = Mathf.Sqrt(CornerGrip[d] * 9.81f / k);
                float vAllowed = Mathf.Sqrt(vCorner * vCorner + 2f * decel * s);
                best = Mathf.Min(best, vAllowed);
            }
            return best;
        }

        static float Curvature(TrackSpline spline, float distance)
        {
            const float span = 8f;
            Vector3 a = spline.PoseAtDistance(distance - span).rotation * Vector3.forward;
            Vector3 b = spline.PoseAtDistance(distance + span).rotation * Vector3.forward;
            float radians = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            return radians / (2f * span);
        }

        void UpdateAvoidance(float speed)
        {
            float range = 8f + 0.5f * speed;
            Vector3 origin = transform.position + transform.up * 0.6f + transform.forward * (car.spec.bodySize.z * 0.5f + 0.2f);
            float push = 0f;
            float[] angles = { 0f, -20f, 20f };
            aheadCar = null;
            aheadGap = float.MaxValue;
            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.AngleAxis(a, transform.up) * transform.forward;
                if (!Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore)) continue;
                var other = hit.collider.GetComponentInParent<CarController>();
                if (other == null || other == car) continue;
                float weight = 1f - hit.distance / range;
                if (a == 0f)
                {
                    float side = transform.InverseTransformPoint(other.transform.position).x;
                    push += (side >= 0f ? -1f : 1f) * weight;
                    if (hit.distance < aheadGap)
                    {
                        aheadGap = hit.distance;
                        aheadCar = other;
                    }
                }
                else push += (a < 0f ? 1f : -1f) * weight;
            }
            float aggression = 0.5f + 0.15f * (int)difficulty;
            float desired = Mathf.Clamp(push * MaxAvoidOffset * aggression, -MaxAvoidOffset, MaxAvoidOffset);
            avoidOffset = Mathf.MoveTowards(avoidOffset, desired, Time.fixedDeltaTime * (Mathf.Abs(push) > 0f ? 2.5f : 0.8f));
        }

        bool UpdateRecovery(float speed, ref float steer, ref float throttle, ref float brake)
        {
            if (reverseTimer > 0f)
            {
                reverseTimer -= Time.fixedDeltaTime;
                steer = -steer;
                throttle = 0f;
                brake = 1f;
                return true;
            }
            if (race != null && race.State != RaceState.Racing) { stuckTimer = 0f; return false; }
            stuckTimer = speed < 1f && throttle > 0f ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > StuckSeconds)
            {
                stuckTimer = 0f;
                reverseTimer = ReverseSeconds;
            }
            return false;
        }

        float RubberBand()
        {
            if (race == null || difficulty > Difficulty.Medium) return 1f;
            var self = race.GetEntry(car);
            if (self == null) return 1f;
            float leaderHuman = float.MinValue;
            foreach (var e in race.entries)
                if (e.isHuman) leaderHuman = Mathf.Max(leaderHuman, e.raceDistance);
            if (leaderHuman == float.MinValue) return 1f;
            float gap = self.raceDistance - leaderHuman;
            if (gap > RubberBandDistance) return 1f - RubberBandAmount;
            if (gap < -RubberBandDistance) return 1f + RubberBandAmount;
            return 1f;
        }
    }
}
