using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarAI : MonoBehaviour
    {
        public const float StuckSeconds = 1.5f;
        public const float ReverseSeconds = 1f;
        public const float MaxAvoidOffset = 2.2f;
        public const float RubberBandDistance = 260f;
        public const float RubberBandAmount = 0.02f;

        static readonly float[] SpeedPercent = { 0.72f, 0.88f, 1.00f, 1.06f };
        static readonly float[] LineDeviation = { 1.4f, 0.75f, 0.25f, 0.04f };
        static readonly float[] CornerGrip = { 0.95f, 1.15f, 1.38f, 1.72f };

        /// <summary>Fração do limite físico REAL medido que cada dificuldade usa nas curvas.</summary>
        static readonly float[] DiffVMaxFraction = { 0.70f, 0.85f, 1.00f, 1.14f };

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
        float overtakeSide; // compromisso lateral de ultrapassagem/desvio (0 = desligado)
        float skill;
        float unlockTime = -999f;
        float lateralAtUnlock;
        bool wasLocked = true;
        CarController aheadCar;
        float aheadGap = float.MaxValue;
        float scanRange = 10f; // alcance da visão frontal (m), depende da velocidade

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
                float minSk = diff == Difficulty.Expert ? 0.99f : diff == Difficulty.Hard ? 0.95f : 0.90f;
                float spanSk = diff == Difficulty.Expert ? 0.09f : 0.13f;
                skill = minSk + (h % 100) / 100f * spanSk;
            }
            // Carreira: vantagem por força da equipe (carros melhores/pilotos melhores andam mais).
            skill = Mathf.Clamp(skill + skillBias, diff == Difficulty.Expert ? 0.98f : 0.85f, 1.16f);
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
            if (thinkTimer <= 0f)
            {
                float now = Time.time;
                thinkDt = Mathf.Clamp(now - lastThinkTime, 0.02f, 0.3f);
                lastThinkTime = now;
                thinkTimer = ThinkInterval;
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
        float thinkDt = ThinkInterval;
        float lastThinkTime;
        CarController passCar;
        float passSide, passTimer;
        float escapeTimer;
        float escapeSide;
        float committedLateral;
        float aheadOtherLat;
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
                committedLateral = lateralAtUnlock;
            }
            wasLocked = car.inputLocked;
            float deviation = LineDeviation[d] * (Mathf.PerlinNoise(noiseSeed, Time.time * 0.15f) * 2f - 1f);

            UpdateAvoidance(speed, waypoints.Spline, along);
            float kNow = Mathf.Max(Curvature(waypoints.Spline, along + 6f), Curvature(waypoints.Spline, along + 18f));
            float safeMaxLat = kNow < 0.003f ? 2.65f : kNow < 0.012f ? 2.05f : 1.55f;
            var myPoseNow = waypoints.Spline.PoseAtDistance(along);
            float myLatNow = Vector3.Dot(transform.position - myPoseNow.position, myPoseNow.rotation * Vector3.right);

            // Traçado ideal em alta velocidade (Hard/Expert): tangencia o ápice interno da curva seguinte.
            float apexBias = 0f;
            if (d >= 2)
            {
                float signedTurn = SignedCurvature(waypoints.Spline, along + Mathf.Clamp(speed * 0.45f, 10f, 24f));
                apexBias = Mathf.Clamp(signedTurn * 140f, -1.55f, 1.55f);
            }
            float baseLateral = Mathf.Clamp(deviation + lane * 0.65f + avoidOffset + apexBias, -safeMaxLat, safeMaxLat);
            float launchBlend = Mathf.Clamp01((Time.time - unlockTime) / 5f);
            float desiredLateral = Mathf.Lerp(Mathf.Clamp(lateralAtUnlock, -safeMaxLat, safeMaxLat), baseLateral, launchBlend);

            float maxSpeed = car.spec.topSpeedKmh / 3.6f * SpeedPercent[d] * skill * RubberBand();
            float targetSpeed = Mathf.Min(maxSpeed, CornerSpeedAhead(along, speed, d) * skill);

            // INTELIGÊNCIA DE CORRIDA (regra do usuário): percebe obstáculo a 20+ m → ESCOLHE
            // O LADO COM ESPAÇO NO ASFALTO (nunca joga na grama!) e contorna com agressividade.
            overtakeSide = 0f;
            bool AvoidingObstacle = false;
            if (aheadCar != null && !RaceX.Core.PitLaneSystem.IsInPitFlow(aheadCar))
            {
                float aheadVel = Mathf.Max(0f, aheadCar.ForwardSpeed);
                float side = transform.InverseTransformPoint(aheadCar.transform.position).x;
                bool slowOrStoppedAhead = aheadGap < scanRange && aheadVel < Mathf.Max(4f, maxSpeed * 0.45f);
                if (slowOrStoppedAhead)
                {
                    if (passCar != aheadCar || passTimer <= 0f)
                    {
                        passCar = aheadCar;
                        passSide = ChooseAsphaltSide(aheadOtherLat, side);
                    }
                    passTimer = 2.8f;
                    overtakeSide = Mathf.Clamp(aheadOtherLat + passSide * 2.8f, -safeMaxLat, safeMaxLat);
                    AvoidingObstacle = true;
                    float brakeLimited = aheadVel + Mathf.Sqrt(2f * 7.5f * Mathf.Max(0f, aheadGap - 3.5f)) + 3.5f;
                    targetSpeed = Mathf.Min(targetSpeed, brakeLimited);
                }
                else if (launchBlend >= 0.35f && aheadGap < scanRange && speed > aheadVel + 0.8f)
                {
                    float sideChoice = ChooseAsphaltSide(aheadOtherLat, side);
                    overtakeSide = Mathf.Clamp(aheadOtherLat + sideChoice * 2.6f, -safeMaxLat, safeMaxLat);
                    AvoidingObstacle = true;
                    targetSpeed = maxSpeed; // vácuo é físico (CarController), igual para todos
                }
                else if (Mathf.Abs(avoidOffset) < 1.1f)
                {
                    targetSpeed = Mathf.Min(targetSpeed, aheadVel + (aheadGap < 5.5f ? 0.5f : 2.0f));
                }
            }
            else if (passTimer > 0f && passCar != null)
            {
                passTimer -= thinkDt;
                Vector3 lp = transform.InverseTransformPoint(passCar.transform.position);
                if (lp.z > -3.0f)
                {
                    overtakeSide = Mathf.Clamp(aheadOtherLat + passSide * 2.7f, -safeMaxLat, safeMaxLat);
                    AvoidingObstacle = true;
                    float vPass = Mathf.Max(0f, passCar.ForwardSpeed);
                    targetSpeed = Mathf.Min(targetSpeed, vPass + Mathf.Sqrt(2f * 7.5f * Mathf.Max(0f, lp.z - 2.8f)) + 3.5f);
                }
                else passTimer = 0f;
            }

            if (escapeTimer > 0f)
            {
                escapeTimer -= thinkDt;
                if (reverseTimer <= 0f)
                {
                    overtakeSide = Mathf.Clamp(aheadOtherLat + escapeSide * 2.7f, -safeMaxLat, safeMaxLat);
                    AvoidingObstacle = true;
                }
            }

            // PROTEÇÃO ANTI-GRAMA: se o carro já está perto da borda ou com roda fora da pista, puxa de volta pro asfalto!
            if (car.IsOffTrack || Mathf.Abs(myLatNow) > 2.95f)
            {
                committedLateral = Mathf.MoveTowards(committedLateral, Mathf.Clamp(-myLatNow * 0.35f, -1.2f, 1.2f), thinkDt * 12f);
            }
            else if (AvoidingObstacle || Mathf.Abs(overtakeSide) > 0.1f)
            {
                committedLateral = Mathf.Clamp(Mathf.MoveTowards(committedLateral, overtakeSide, thinkDt * 8.5f), -safeMaxLat, safeMaxLat);
            }
            else
            {
                committedLateral = Mathf.Clamp(Mathf.MoveTowards(committedLateral, desiredLateral, thinkDt * 4.5f), -safeMaxLat, safeMaxLat);
            }

            float lookAhead = AvoidingObstacle ? Mathf.Clamp(8.5f + 0.22f * speed, 8.5f, 15f) : Mathf.Clamp(7f + 0.32f * speed, 8f, 20f);
            Vector3 target = waypoints.Spline.PoseAtDistance(along + lookAhead, committedLateral).position;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(angle / Mathf.Max(1f, car.spec.maxSteerAngle), -1f, 1f);

            float throttle, brake;
            float error = targetSpeed - speed;
            if (error > 0f || targetSpeed >= maxSpeed * 0.96f)
            {
                // Retas e saídas de curva: em Hard/Expert pisa 100% no acelerador!
                throttle = (d >= 2 && error > 0.4f) || targetSpeed >= maxSpeed * 0.96f
                    ? 1f
                    : Mathf.Clamp01(error / 2.2f + 0.45f);
                brake = 0f;
            }
            else
            {
                throttle = 0f;
                float overSpeed = -error;
                brake = overSpeed > 4.5f ? Mathf.Clamp01((overSpeed - 3.5f) / 10f) : 0f;
            }

            bool pastLaunch = (Time.time - unlockTime) > 12f;
            if (pastLaunch && UpdateRecovery(speed, ref steer, ref throttle, ref brake)) { }
            else if (!pastLaunch) { reverseTimer = 0f; stuckTimer = 0f; }

            // ANTI-EMPURRÃO (regra do usuário): encostou em carro mais lento/parado → para de
            // acelerar e freia; se estiver de frente para um carro parado a curta distância e o nariz
            // ainda apontar para ele, esterça para o lado livre em vez de acelerar contra ele.
            // (Nunca aciona ré durante os primeiros 12s da largada!)
            if (reverseTimer <= 0f && aheadCar != null && !RaceX.Core.PitLaneSystem.IsInPitFlow(aheadCar))
            {
                Vector3 localBlk = transform.InverseTransformPoint(aheadCar.transform.position);
                float aheadVel = Mathf.Max(0f, aheadCar.ForwardSpeed);
                if (pastLaunch && aheadVel < 1.2f && localBlk.z > 0f && localBlk.z < 7.5f && Mathf.Abs(localBlk.x) < 2.2f)
                {
                    float sideFree = Mathf.Abs(aheadOtherLat) > 0.5f ? (aheadOtherLat > 0f ? -1f : 1f) : (localBlk.x >= 0f ? -1f : 1f);
                    steer = Mathf.Clamp(sideFree * 0.95f, -1f, 1f);
                    if (localBlk.z < 4.2f)
                    {
                        throttle = 0f;
                        brake = speed > 0.5f ? 0.8f : 0f;
                    }
                    else
                    {
                        throttle = Mathf.Min(throttle, 0.35f);
                        brake = speed > 4f ? 0.5f : 0f;
                    }
                }
                else if (aheadGap < (launchBlend >= 1f ? 8f : 5.5f) && speed > aheadVel + 1f)
                {
                    throttle = 0f;
                    brake = Mathf.Max(brake, 0.55f);
                }
            }

            #if UNITY_EDITOR
            // Diagnóstico só no Editor: no device essa string era lixo de GC 240x/s (24 carros × 10 Hz)
            // e alimenta o travamento — nada lê DebugState no build.
            DebugState = $"along={along:F0} tgt={targetSpeed * 3.6f:F0} max={maxSpeed * 3.6f:F0} v={speed * 3.6f:F0} thr={throttle:F2} brk={brake:F2} steer={steer:F2} avoid={avoidOffset:F1} rev={reverseTimer:F1} stuck={stuckTimer:F1} gear={car.Gear}";
            #endif
            cachedSteer = steer;
            cachedThrottle = throttle;
            cachedBrake = brake;
        }

        static float ChooseAsphaltSide(float otherLat, float localX)
        {
            if (otherLat > 0.35f) return -1f;
            if (otherLat < -0.35f) return 1f;
            return localX >= 0f ? -1f : 1f;
        }

        float CornerSpeedAhead(float along, float speed, int d)
        {
            float decel = d >= 3 ? 13.5f : d == 2 ? 11.2f : 9.5f;
            float horizon = speed * speed / (2f * decel) + 18f;
            float best = float.MaxValue;
            var spline = waypoints.Spline;
            int trackId = race != null && race.Settings != null ? (int)race.Settings.track : 0;
            for (float s = 4f; s <= horizon; s += waypoints.Spacing)
            {
                float k = Curvature(spline, along + s);
                if (k < 1e-4f) continue;
                float vMeasured = MeasuredVMax(car != null ? car.spec.model : CarModel.Falcon, 1f / k);
                float vCorner = vMeasured > 0f
                    ? vMeasured * DiffVMaxFraction[d] * FractionOverride
                    : Mathf.Sqrt(CornerGrip[d] * 9.81f / k);
                if (d >= 2)
                {
                    float playerCleanV = PlayerPaceLearner.SampleSpeed(trackId, along + s, spline.Length);
                    if (playerCleanV > 5f)
                    {
                        float mult = d >= 3 ? 1.045f : 0.98f;
                        float physicsCap = Mathf.Sqrt(2.05f * 9.81f / k);
                        vCorner = Mathf.Max(vCorner, Mathf.Min(playerCleanV * mult, physicsCap));
                    }
                }
                vCorner *= PitLaneSystem.TireGrip(car);
                float vAllowed = Mathf.Sqrt(vCorner * vCorner + 2f * decel * Mathf.Max(0f, s - 4f));
                best = Mathf.Min(best, vAllowed);
            }
            return best;
        }

        public static float IdealSpeedAt(TrackSpline spline, float distance, CarController refCar, int trackId = 0)
        {
            float k = Curvature(spline, distance);
            float top = refCar != null && refCar.spec != null ? refCar.spec.topSpeedKmh / 3.6f : 80f;
            if (k < 1e-4f) return top;
            float vCorner = Mathf.Sqrt(1.55f * 9.81f / k);
            float learned = PlayerPaceLearner.SampleSpeed(trackId, distance, spline.Length);
            if (learned > 5f) vCorner = Mathf.Max(vCorner, Mathf.Min(learned * 1.03f, Mathf.Sqrt(2.0f * 9.81f / k)));
            return Mathf.Min(top, vCorner);
        }

        public static float SignedCurvature(TrackSpline spline, float distance)
        {
            const float span = 8f;
            Vector3 a = spline.PoseAtDistance(distance - span).rotation * Vector3.forward;
            Vector3 b = spline.PoseAtDistance(distance + span).rotation * Vector3.forward;
            float signedDeg = Vector3.SignedAngle(a, b, Vector3.up);
            return (signedDeg * Mathf.Deg2Rad) / (2f * span);
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
            // ~0,4 s de reação: 10 m devagar, ~16 m a 108 km/h, 30 m a ~250 km/h
            float range = Mathf.Clamp(4f + 0.4f * speed, 10f, 30f);
            scanRange = range;
            Vector3 origin = transform.position + transform.up * 0.6f + transform.forward * (car.spec.bodySize.z * 0.5f + 0.2f);
            float push = 0f;
            float[] angles = { 0f, -15f, 15f, -28f, 28f };
            aheadCar = null;
            aheadGap = float.MaxValue;
            aheadOtherLat = 0f;
            var myPose = spline.PoseAtDistance(along);
            float myLat = Vector3.Dot(transform.position - myPose.position, myPose.rotation * Vector3.right);

            if (race != null && race.entries != null)
            {
                for (int i = 0; i < race.entries.Count; i++)
                {
                    var e = race.entries[i];
                    if (e == null || e.car == null || e.car == car) continue;
                    if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) continue;
                    Vector3 toOther = e.car.transform.position - transform.position;
                    if (toOther.sqrMagnitude > (range + 5f) * (range + 5f)) continue;
                    Vector3 localO = transform.InverseTransformPoint(e.car.transform.position);
                    if (localO.z <= -0.5f || localO.z > range + 3.5f) continue;
                    var pO = spline.PoseAtDistance(Mathf.Repeat(along + Mathf.Max(0f, localO.z), spline.Length));
                    float oLat = Vector3.Dot(e.car.transform.position - pO.position, pO.rotation * Vector3.right);
                    if (Mathf.Abs(oLat) > 7.5f) continue; // fora da pista/no box
                    float latDiff = oLat - myLat;
                    if (Mathf.Abs(latDiff) < 3.0f || Mathf.Abs(localO.x) < 2.8f)
                    {
                        float gap = Mathf.Max(0.1f, localO.z - 3.8f);
                        float w = Mathf.Clamp01(1f - gap / range);
                        float dirPush = Mathf.Abs(oLat) > 0.5f ? (oLat > 0f ? -1f : 1f) : (localO.x >= 0f ? -1f : 1f);
                        push += dirPush * w * 1.4f;
                        if (gap < aheadGap)
                        {
                            aheadGap = gap;
                            aheadCar = e.car;
                            aheadOtherLat = oLat;
                        }
                    }
                }
            }

            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.AngleAxis(a, transform.up) * transform.forward;
                if (!Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore)) continue;
                var other = hit.collider.GetComponentInParent<CarController>();
                if (other == null || other == car) continue;
                float weight = 1f - hit.distance / range;
                if (Mathf.Abs(a) <= 15f)
                {
                    float side = transform.InverseTransformPoint(other.transform.position).x;
                    push += (side >= 0f ? -1f : 1f) * weight;
                    if (hit.distance < aheadGap)
                    {
                        aheadGap = hit.distance;
                        aheadCar = other;
                        var pHit = spline.PoseAtDistance(Mathf.Repeat(along + hit.distance, spline.Length));
                        aheadOtherLat = Vector3.Dot(other.transform.position - pHit.position, pHit.rotation * Vector3.right);
                    }
                }
                else push += (a < 0f ? 1f : -1f) * weight;
            }
            float kAhead = Curvature(spline, along + speed * 1.2f);
            float zone;
            if (kAhead < 0.002f) zone = 1.35f;
            else if (kAhead < 0.012f) zone = 1.0f;
            else zone = 0.75f;
            float aggression = (0.5f + 0.15f * (int)difficulty) * zone;
            float desired = Mathf.Clamp(push * MaxAvoidOffset * aggression, -MaxAvoidOffset, MaxAvoidOffset);
            avoidOffset = Mathf.MoveTowards(avoidOffset, desired, thinkDt * (Mathf.Abs(push) > 0f ? 3.5f : 0.8f));
        }

        bool UpdateRecovery(float speed, ref float steer, ref float throttle, ref float brake)
        {
            if (reverseTimer > 0f)
            {
                // Se já abriu distância suficiente e o nariz já aponta para fora do carro parado, sai da ré.
                if (aheadCar != null)
                {
                    Vector3 lb = transform.InverseTransformPoint(aheadCar.transform.position);
                    if (lb.z > 6.0f && Mathf.Abs(lb.x) > 2.3f) { reverseTimer = 0f; return false; }
                }
                // Só desconta o tempo de ré quando o carro já parou de avançar e está dando ré de verdade.
                if (car.ForwardSpeed <= 0.35f) reverseTimer -= thinkDt;
                // Virar as rodas para -escapeSide durante a ré faz a FRENTE girar para +escapeSide (lado livre).
                steer = car.ForwardSpeed <= 0.35f ? -escapeSide * 0.9f : 0f;
                throttle = 0f;
                brake = 1f;
                committedLateral = Mathf.Clamp(aheadOtherLat + escapeSide * 2.65f, -2.65f, 2.65f);
                return true;
            }
            if (race != null && race.State != RaceState.Racing) { stuckTimer = 0f; return false; }
            bool contact = aheadCar != null && aheadGap < 3.6f && aheadCar.ForwardSpeed < 1.0f && speed < 1.5f;
            stuckTimer = speed < 2.0f && (throttle > 0f || contact) ? stuckTimer + thinkDt * (contact ? 1.6f : 1f) : 0f;
            if (stuckTimer > StuckSeconds)
            {
                stuckTimer = 0f;
                reverseTimer = 2.0f;
                escapeTimer = 5.0f;
                if (aheadCar != null)
                {
                    float localX = transform.InverseTransformPoint(aheadCar.transform.position).x;
                    escapeSide = Mathf.Abs(aheadOtherLat) > 0.5f ? (aheadOtherLat > 0f ? -1f : 1f) : (localX >= 0f ? -1f : 1f);
                }
                else escapeSide = committedLateral >= 0f ? -1f : 1f;
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
