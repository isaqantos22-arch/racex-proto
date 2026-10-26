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
        public const float RubberBandDistance = 260f;
        public const float RubberBandAmount = 0.02f;

        static readonly float[] SpeedPercent = { 0.62f, 0.80f, 1.00f, 1.00f };
        static readonly float[] LineDeviation = { 1.6f, 0.9f, 0.35f, 0.05f };
        static readonly float[] CornerGrip = { 0.85f, 0.98f, 1.15f, 1.35f };

        /// <summary>Fração do limite físico REAL medido que cada dificuldade usa nas curvas.</summary>
        static readonly float[] DiffVMaxFraction = { 0.60f, 0.72f, 0.85f, 0.97f };

        /// <summary>Ajuste fino por corrida (calibração ao vivo usa para busca binária).</summary>
        public static float FractionOverride = 1f;

        /// <summary>Raios calibrados (batem com RaceXCalibration.Radii).</summary>
        static readonly float[] CalRadii = { 30f, 60f, 120f, 200f };

        /// <summary>Limite real medido do carro na curva de raio R (interpolação em sqrt(R)); 0 = sem dado.</summary>
        static float MeasuredVMax(CarModel model, float radius)
        {
            var table = AICalibrationData.VMax;
            int mi = (int)model;
            if (table == null || table.Length <= mi || table[mi] == null || table[mi].Length < CalRadii.Length) return 0f;
            float sqR = Mathf.Sqrt(radius);
            if (sqR <= Mathf.Sqrt(CalRadii[0])) return table[mi][0];
            for (int i = 0; i < CalRadii.Length - 1; i++)
            {
                float a = Mathf.Sqrt(CalRadii[i]), b = Mathf.Sqrt(CalRadii[i + 1]);
                if (sqR <= b)
                    return Mathf.Lerp(table[mi][i], table[mi][i + 1], (sqR - a) / (b - a));
            }
            // Extrapol straight: v ~ sqrt(grip) constante → limite do maior raio.
            return table[mi][CalRadii.Length - 1];
        }

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

        public void Initialize(WaypointSystem wp, RaceManager raceManager, Difficulty diff, float skillBias = 0f)
        {
            car = GetComponent<CarController>();
            waypoints = wp;
            race = raceManager;
            difficulty = diff;
            noiseSeed = Random.value * 100f;
            skill = Random.Range(0.90f, 1.06f);
            // Carreira: ritmo fixo por piloto (identidade consistente na temporada — sem sorteio por corrida).
            var entry = raceManager != null ? raceManager.GetEntry(car) : null;
            if (entry != null && !entry.isHuman && !string.IsNullOrEmpty(entry.name))
            {
                int h = 0;
                foreach (char ch in entry.name) h = (h * 31 + ch) & 0x7fffffff;
                skill = 0.90f + (h % 100) / 100f * 0.15f;
            }
            // Carreira: vantagem por força da equipe (carros melhores/pilotos melhores andam mais).
            skill = Mathf.Clamp(skill + skillBias, 0.85f, 1.12f);
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

            // OTIMIZAÇÃO (mobile): IA pensa a 10 Hz escalonada por carro (não 50 Hz). Entre pensadas,
            // reutiliza steer/throttle/brake alvo — invisível ao jogador, 5x mais barato com 24 carros.
            if (thinkTimer <= 0f || thinkTimer > ThinkInterval)
            {
                thinkTimer = ThinkInterval * ((float)noiseSeed % 1f) * 0.5f;
                Think();
            }
            else thinkTimer -= Time.fixedDeltaTime;
            car.steerInput = cachedSteer;
            car.throttleInput = cachedThrottle;
            car.brakeInput = cachedBrake;
            car.handbrake = false;
        }

        const float ThinkInterval = 0.1f;
        float thinkTimer = -1f;
        float cachedSteer, cachedThrottle, cachedBrake;

        // Ciclo do treino: conta voltas desde a última saída e pede recolida após 3.
        int practiceBaseLap = -1;
        public void OnPracticeRelease() { practiceBaseLap = -1; }

        void UpdatePracticeCycle()
        {
            int lap = race.LapOf(car);
            if (practiceBaseLap < 0) { practiceBaseLap = lap; return; }
            if (lap - practiceBaseLap >= 3)
            {
                practiceBaseLap = int.MinValue / 2; // evita pedir de novo até a próxima liberação
                RaceX.Core.PitLaneSystem.RequestPit(car);
            }
        }

        void Think()
        {
            int d = (int)difficulty;
            float speed = Mathf.Max(0f, car.ForwardSpeed);
            float along = waypoints.DistanceAlong(transform.position, ref hint);
            // Treino livre: faz 3 voltas e recolhe ao box (PitLaneSystem recicla com pausa e nova saída).
            if (race != null && race.Settings.laps >= 900 && !car.pitControlled) UpdatePracticeCycle();
            if (wasLocked && !car.inputLocked)
            {
                unlockTime = Time.time;
                var p0 = waypoints.Spline.PoseAtDistance(along);
                lateralAtUnlock = Vector3.Dot(transform.position - p0.position, p0.rotation * Vector3.right);
            }
            wasLocked = car.inputLocked;
            float deviation = LineDeviation[d] * (Mathf.PerlinNoise(noiseSeed, Time.time * 0.15f) * 2f - 1f);

            UpdateAvoidance(speed, waypoints.Spline, along);
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
            if (aheadCar != null && Mathf.Abs(avoidOffset) < 1.2f && launchBlend >= 1f &&
                !RaceX.Core.PitLaneSystem.IsInPitFlow(aheadCar))
            {
                // Cola não existe: alinhado atrás acompanha; ao desviar para ultrapassar, mantém ritmo.
                // EXCEÇÃO: carro NO FLUXO DO PIT (entrada/saída a 80 km/h) não é "carro da pista" —
                // segui-lo derrubava o trenzão da reta inteira depois da 1ª parada (todos a ~100).
                float follow = Mathf.Abs(avoidOffset) > 0.5f ? 8f : 1.5f;
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, aheadCar.ForwardSpeed) + follow);
            }

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

            #if UNITY_EDITOR
            // Diagnóstico só no Editor: no device essa string era lixo de GC 240x/s (24 carros × 10 Hz)
            // e alimenta o travamento — nada lê DebugState no build.
            DebugState = $"along={along:F0} tgt={targetSpeed * 3.6f:F0} max={maxSpeed * 3.6f:F0} v={speed * 3.6f:F0} thr={throttle:F2} brk={brake:F2} steer={steer:F2} avoid={avoidOffset:F1} rev={reverseTimer:F1} stuck={stuckTimer:F1} gear={car.Gear}";
            #endif
            cachedSteer = steer;
            cachedThrottle = throttle;
            cachedBrake = brake;
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
                // Limite REAL medido (banco de calibração) × fração da dificuldade; fallback: fórmula de grip.
                float vMeasured = MeasuredVMax(car != null ? car.spec.model : CarModel.Falcon, 1f / k);
                float vCorner = vMeasured > 0f
                    ? vMeasured * DiffVMaxFraction[d] * FractionOverride
                    : Mathf.Sqrt(CornerGrip[d] * 9.81f / k);
                // Desgaste de pneu: pneu gasto/duro faz a curva mais devagar.
                vCorner *= PitLaneSystem.TireGrip(car);
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

        void UpdateAvoidance(float speed, TrackSpline spline, float along)
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
            // Agressividade por ZONA (setor): avalia a curvatura à frente — reta = disputa total,
            // curvão = equilibrada, curva lenta = ousadia na freada mas cautela no lado.
            float kAhead = Curvature(spline, along + speed * 1.2f);
            float zone;
            if (kAhead < 0.002f) zone = 1.35f;          // reta: vai por fora, dispute
            else if (kAhead < 0.012f) zone = 1.0f;      // curvão rápido
            else zone = 0.75f;                          // curva lenta/chicane: sem empurrão lateral
            float aggression = (0.5f + 0.15f * (int)difficulty) * zone;
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
