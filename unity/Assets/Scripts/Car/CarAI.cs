using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarAI : MonoBehaviour
    {
        public const float StuckSeconds = 2.5f;
        public const float ReverseSeconds = 1.5f;
        public const float MaxReverseSeconds = 2.5f;
        public const float MaxAvoidOffset = 2.2f;
        const int MaxRecoveryAttempts = 2; // janelas de ré sem sucesso antes do respawn (~12 s)
        public const float RubberBandDistance = 260f;
        public const float RubberBandAmount = 0.02f;

        static readonly float[] SpeedPercent = { 0.72f, 0.88f, 1.00f, 1.06f };
        static readonly float[] LineDeviation = { 1.4f, 0.75f, 0.25f, 0.04f };

        /// <summary>Fração do limite físico REAL (grip do pneu + downforce) que cada dificuldade usa
        /// nas curvas: Fácil 80%, Médio 88%, Difícil 94%, Expert 98% — nunca acima de 100% (o antigo
        /// 1,14 do Expert fazia a IA entrar na curva acima da aderência e rodar/capotar).</summary>
        static readonly float[] DiffVMaxFraction = { 0.80f, 0.88f, 0.94f, 0.98f };

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
        int recoveryAttempts;   // janelas de ré sem sucesso — após 2, respawn na pista
        // CÃO DE GUARDA POR PROGRESSO (v79): mede o avanço REAL ao longo do traçado. Velocidade engana
        // (carro encostado no muro oscila, carro na fila "anda" 1 m e para); sem avançar 15 m em 7 s
        // ou andando na CONTRAMÃO por 3 s → respawn em ponto livre da pista.
        float progressRef = float.NaN;
        float progressTimer;
        float wrongWayTimer;
        int respawnFails;

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
            if (!car.pitControlled && reverseTimer <= 0f) StabilizeSlide();
        }

        float slideHold;

        // Derrapou/rodou: contra-esterça na direção do movimento, sem acelerar, até alinhar (a 50 Hz).
        void StabilizeSlide()
        {
            var rb = car.Body;
            if (rb == null) return;
            Vector3 lv = transform.InverseTransformDirection(rb.linearVelocity);
            float spd = Mathf.Sqrt(lv.x * lv.x + lv.z * lv.z);
            if (spd < 6f) { slideHold = 0f; return; }
            float beta = Mathf.Atan2(lv.x, Mathf.Max(0.01f, lv.z)) * Mathf.Rad2Deg;
            float yawDeg = rb.angularVelocity.y * Mathf.Rad2Deg;
            if (lv.z <= 0.5f)
            {
                car.steerInput = 0f; car.throttleInput = 0f; car.brakeInput = 1f;
                slideHold = 0.4f;
                return;
            }
            if (Mathf.Abs(beta) > 11f || (Mathf.Abs(beta) > 6f && Mathf.Abs(yawDeg) > 70f)) slideHold = 0.4f;
            if (slideHold <= 0f) return;
            slideHold -= Time.fixedDeltaTime;
            float sf = Mathf.Clamp01(car.SpeedKmh / Mathf.Max(1f, car.spec.topSpeedKmh));
            float maxAngle = Mathf.Max(4f, car.spec.maxSteerAngle * Mathf.Lerp(1f, car.spec.steerAtTopSpeed, sf));
            car.steerInput = Mathf.Clamp((beta - yawDeg * 0.08f) / maxAngle, -1f, 1f);
            car.throttleInput = 0f;
            car.brakeInput = Mathf.Abs(beta) > 25f ? 0.45f : 0.15f;
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
            // CornerSpeedAhead já aplica o fator de dificuldade × skill, limitado ao teto físico (grip real).
            float targetSpeed = Mathf.Min(maxSpeed, CornerSpeedAhead(along, speed, d));

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
                    // Ultrapassando: mantém o limite de curva (antes ia a maxSpeed e passava reto na curva).
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

            if (car.IsOffTrack) targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(16f, speed * 0.8f)); // fora do asfalto: alivia para voltar
            // PROTEÇÃO ANTI-GRAMA + RECUPERAÇÃO FORA DA PISTA: se o carro já está perto da borda ou
            // com roda fora da pista, puxa de volta pro asfalto SUAVEMENTE (limite de velocidade acima).
            if (car.IsOffTrack || Mathf.Abs(myLatNow) > 2.95f)
            {
                committedLateral = Mathf.MoveTowards(committedLateral, Mathf.Clamp(-myLatNow * 0.5f, -1.4f, 1.4f), thinkDt * 12f);
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
            bool straightAhead = kNow < 0.004f && Curvature(waypoints.Spline, along + 16f + speed * 0.9f) < 0.008f;
            if (error > 0f || targetSpeed >= maxSpeed * 0.96f)
            {
                // Retas e saídas de curva: ABAIXO do alvo em reta = ACELERA 100% (pé embaixo — regra
                // do usuário para qualquer dificuldade). Saída de curva ainda usa rampa para não rodar.
                throttle = (targetSpeed >= maxSpeed * 0.96f || (error > 0.05f && straightAhead))
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
            if (pastLaunch && ProgressWatchdog(along, speed)) return;

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

        /// <summary>µ efetivo do pneu: aderência lateral real do CarSpec (stiffness do WheelCollider)
        /// × composto/desgaste atual do pit (PitLaneSystem.TireGrip).</summary>
        float MuEffective()
        {
            var s = car != null ? car.spec : null;
            float mu = s != null ? 1.35f + (s.sidewaysStiffness - 1.6f) * 0.30f : 1.45f;
            return mu * Mathf.Clamp(PitLaneSystem.TireGrip(car), 0.78f, 1.08f);
        }

        /// <summary>Limite FÍSICO da curva (m/s) no ponto de curvatura k, do grip real do carro:
        /// m·v²·k = µ·(m·g + downforce·v²) resolvido para v → v² = µ·g/(k − µ·downforce/massa).
        /// Sem downforce (conservador): v² = µ·g/k. Nunca acima do medido pela calibração nem do topo.</summary>
        float PhysicalCornerLimit(float k)
        {
            float mu = MuEffective();
            var s = car != null ? car.spec : null;
            float v2;
            if (s != null && s.downforceCoefficient > 0.01f && s.massKg > 50f)
            {
                float denom = k - mu * s.downforceCoefficient / s.massKg;
                v2 = denom > 0f ? mu * 9.81f / denom : float.MaxValue; // downforce vence: sem teto de grip (topo limita)
            }
            else v2 = mu * 9.81f / Mathf.Max(k, 1e-4f);
            float v = Mathf.Sqrt(v2);
            if (s != null) v = Mathf.Min(v, s.topSpeedKmh / 3.6f);
            float vMeasured = MeasuredVMax(s != null ? s.model : CarModel.Falcon, 1f / Mathf.Max(k, 1e-4f));
            if (vMeasured > 0f) v = Mathf.Min(v, vMeasured);
            return v;
        }

        float CornerSpeedAhead(float along, float speed, int d)
        {
            // Frenagem REALISTA com a MESMA aderência (µ·g, cap de 15 m/s²). Horizonte = distância de
            // frenagem na velocidade atual + 30 m de folga — curva apertada nunca fica fora da amostra.
            float decel = Mathf.Min(MuEffective() * 9.81f, 15f);
            float horizon = Mathf.Max(60f, speed * speed / (2f * Mathf.Max(4f, decel)) + 30f);
            float best = float.MaxValue;
            var spline = waypoints.Spline;
            int trackId = race != null && race.Settings != null ? (int)race.Settings.track : 0;
            // Amostragem DENSA (~3,5 m): curvatura entre amostras espaçadas passava despercebida —
            // a IA entrava na curva final a 200+ km/h e rodava. Olhar adiante: frenagem + 30 m.
            for (float s = 4f; s <= horizon; s += 3.5f)
            {
                float k = Curvature(spline, along + s);
                if (k < 1e-4f) continue;
                float vPhys = PhysicalCornerLimit(k);
                float tire = PitLaneSystem.TireGrip(car);
                float vCap = vPhys * tire; // teto físico com o pneu ATUAL (composto/desgaste)
                float vCorner = Mathf.Min(vCap, vPhys * DiffVMaxFraction[d] * FractionOverride * skill);
                if (d >= 2)
                {
                    // Amostra do PILOTO (volta limpa) pode ELEVAR o alvo, mas NUNCA acima do teto físico.
                    float playerCleanV = PlayerPaceLearner.SampleSpeed(trackId, along + s, spline.Length);
                    if (playerCleanV > 5f)
                    {
                        float mult = d >= 3 ? 1.045f : 0.98f;
                        vCorner = Mathf.Max(vCorner, Mathf.Min(playerCleanV * mult, vCap));
                    }
                }
                // Velocidade permitida AGORA para conseguir frear até vCorner no ponto s:
                float vAllowed = Mathf.Sqrt(vCorner * vCorner + 2f * decel * Mathf.Max(0f, s - 4f));
                best = Mathf.Min(best, vAllowed);
            }
            return best < float.MaxValue ? best : (car != null && car.spec != null ? car.spec.topSpeedKmh / 3.6f : 80f);
        }

        public static float IdealSpeedAt(TrackSpline spline, float distance, CarController refCar, int trackId = 0)
        {
            float k = Curvature(spline, distance);
            float top = refCar != null && refCar.spec != null ? refCar.spec.topSpeedKmh / 3.6f : 80f;
            if (k < 1e-4f) return top;
            // MESMA física do CarAI: v² = µ·(g + downforce·v²/massa)/k, µ base do carro.
            float mu = 1.45f;
            float v2 = mu * 9.81f / k;
            if (refCar != null && refCar.spec != null)
            {
                var s = refCar.spec;
                mu = 1.35f + (s.sidewaysStiffness - 1.6f) * 0.30f;
                if (s.downforceCoefficient > 0.01f && s.massKg > 50f)
                {
                    float denom = k - mu * s.downforceCoefficient / s.massKg;
                    v2 = denom > 0f ? mu * 9.81f / denom : float.MaxValue;
                }
                else v2 = mu * 9.81f / k;
            }
            float v = Mathf.Min(Mathf.Sqrt(v2), top);
            float learned = PlayerPaceLearner.SampleSpeed(trackId, distance, spline.Length);
            if (learned > 5f) v = Mathf.Max(v, Mathf.Min(learned * 1.02f, v * 1.10f));
            return v;
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
            // DOIS PRESOS um contra o outro: só o de MENOR progresso na pista dá ré — o outro segue
            // em frente. Sem isso os dois invertiam ré para sempre (um contra o outro, sem sair).
            if (contact && race != null)
            {
                var myE = race.GetEntry(car);
                var otE = race.GetEntry(aheadCar);
                if (myE != null && otE != null && otE.raceDistance < myE.raceDistance)
                {
                    stuckTimer = 0f;
                    return false; // o OUTRO é o de trás: ele recua; eu sigo
                }
            }
            stuckTimer = speed < 2.0f && (throttle > 0f || contact) ? stuckTimer + thinkDt * (contact ? 1.6f : 1f) : 0f;
            if (speed > 5f) recoveryAttempts = 0; // voltou a andar: janelas de ré reativadas
            if (stuckTimer > StuckSeconds)
            {
                stuckTimer = 0f;
                recoveryAttempts++;
                if (recoveryAttempts > MaxRecoveryAttempts)
                {
                    // 2 janelas de ré não resolveram (~12 s): respawn na linha central, alguns metros
                    // atrás, só se não houver carro a 15 m do ponto (reaproveita o ResetTo do carro).
                    // Ponto livre não encontrado: volta a alternar janelas de ré (antes ficava preso em
                    // "tentar respawn" para sempre — a fila inteira travava porque sempre havia carro a 15 m).
                    if (TryRespawnStuck()) { recoveryAttempts = 0; }
                    else recoveryAttempts = 0;
                    return false;
                }
                reverseTimer = Random.Range(ReverseSeconds, MaxReverseSeconds); // ré de 1,5–2,5 s
                escapeTimer = 5f;
                escapeSide = ChooseEscapeSide();
            }
            return false;
        }

        /// <summary>Lado com MAIS espaço livre para a ré (raycasts ±45° do capô). Sem dado, usa a
        /// posição relativa do carro/obstáculo parado à frente.</summary>
        float ChooseEscapeSide()
        {
            const float Range = 8f;
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            float freeR = Physics.Raycast(origin, Quaternion.AngleAxis(45f, transform.up) * transform.forward, out _, Range) ? 0f : Range;
            float freeL = Physics.Raycast(origin, Quaternion.AngleAxis(-45f, transform.up) * transform.forward, out _, Range) ? 0f : Range;
            if (freeL < 2.5f && freeR >= freeL) return 1f;
            if (freeR < 2.5f && freeL >= freeR) return -1f;
            if (aheadCar != null)
            {
                float localX = transform.InverseTransformPoint(aheadCar.transform.position).x;
                return Mathf.Abs(aheadOtherLat) > 0.5f ? (aheadOtherLat > 0f ? -1f : 1f) : (localX >= 0f ? -1f : 1f);
            }
            return committedLateral >= 0f ? -1f : 1f;
        }

        /// <summary>Cão de guarda por PROGRESSO: true = fez respawn agora (Think encerra).</summary>
        bool ProgressWatchdog(float along, float speed)
        {
            if (race == null || race.State != RaceState.Racing || car.pitControlled || RaceX.Core.PitLaneSystem.IsInPitFlow(car))
            {
                progressRef = float.NaN; progressTimer = 0f; wrongWayTimer = 0f;
                return false;
            }
            float L = waypoints.Spline.Length;
            if (float.IsNaN(progressRef)) { progressRef = along; progressTimer = 0f; }
            progressTimer += thinkDt;
            // Contramão: nariz contra o sentido da pista e andando (não é só um giro rápido).
            var pose = waypoints.Spline.PoseAtDistance(along);
            float dirDot = Vector3.Dot(transform.forward, pose.rotation * Vector3.forward);
            wrongWayTimer = dirDot < -0.25f && car.SpeedKmh > 8f ? wrongWayTimer + thinkDt : 0f;
            bool wrongWay = wrongWayTimer > 3f;
            if (progressTimer >= 7f || wrongWay)
            {
                float delta = Mathf.Repeat(along - progressRef + L * 0.5f, L) - L * 0.5f; // avanço com sinal (volta da pista)
                bool noProgress = delta < 15f;
                progressRef = along;
                progressTimer = 0f;
                if (noProgress || wrongWay)
                {
                    wrongWayTimer = 0f;
                    if (TryRespawnStuck()) return true;
                }
            }
            return false;
        }

        /// <summary>Respawn do carro preso/na contramão: centro da pista, no primeiro ponto LIVRE
        /// (nenhum carro a menos de 9 m) procurando atrás e depois À FRENTE da fila. Depois de várias
        /// falhas seguidas aceita folga menor (5 m) — carro preso para sempre é pior que nascer perto.</summary>
        bool TryRespawnStuck()
        {
            if (car == null || car.pitControlled || waypoints == null || waypoints.Spline == null || race == null) return false;
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car)) return false;
            float along = waypoints.DistanceAlong(transform.position, ref hint);
            float L = waypoints.Spline.Length;
            float clearance = respawnFails >= 3 ? 5f : 9f;
            float[] offsets = { -6f, -14f, 12f, 20f, 28f, 36f, 48f, 60f, 75f, -24f };
            foreach (float off in offsets)
            {
                var pose = waypoints.Spline.PoseAtDistance(Mathf.Repeat(along + off, L));
                bool free = true;
                for (int i = 0; i < race.entries.Count; i++)
                {
                    var e = race.entries[i];
                    if (e == null || e.car == null || e.car == car) continue;
                    if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) continue;
                    if (Vector3.Distance(e.car.transform.position, pose.position) < clearance) { free = false; break; }
                }
                if (!free) continue;
                car.ResetTo(pose.position + Vector3.up * 0.3f, pose.rotation);
                respawnFails = 0;
                stuckTimer = 0f; reverseTimer = 0f; escapeTimer = 0f; passTimer = 0f; slideHold = 0f;
                recoveryAttempts = 0;
                committedLateral = 0f;
                progressRef = float.NaN; progressTimer = 0f; wrongWayTimer = 0f;
                cachedSteer = 0f; cachedThrottle = 0.6f; cachedBrake = 0f;
                return true;
            }
            respawnFails++;
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
