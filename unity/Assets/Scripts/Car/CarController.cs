using System;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        public const int FL = 0, FR = 1, RL = 2, RR = 3;

        public CarSpec spec;
        public WheelCollider[] wheels = new WheelCollider[4];
        public Transform[] wheelMeshes = new Transform[4];

        [Header("Input (qualquer fonte: jogador, IA ou rede)")]
        [Range(-1f, 1f)] public float steerInput;
        [Range(0f, 1f)] public float throttleInput;
        [Range(0f, 1f)] public float brakeInput;
        public bool handbrake;
        public bool inputLocked;
        /// <summary>Pit lane: quando true, PitLaneSystem controla os inputs (ext*).</summary>
        public bool pitControlled;
        public float extThrottle, extBrake, extSteer;
        public float assistLevel = 1f;
        /// <summary>Perda total: o carro fica parado (sem motor, freado) até ser retirado da pista.</summary>
        public bool wrecked;
        /// <summary>IA que dirige este carro (null = jogador). Guardada pelo CarAI.Initialize — evita GetComponent por pensada.</summary>
        [System.NonSerialized] public CarAI Ai;
        /// <summary>Dano por zona (CarDamage): aderência por roda, aerodinâmica e peças arrancadas.</summary>
        public CarDamage damage;

        public event Action<float, Vector3> Collided;
        /// <summary>QUALQUER toque entre dois carros (leve ou forte, roda com roda, lateral, encostado): a direção de prova usa para
        /// não contar como limite de pista a saída de quem foi empurrado (no máximo 5 avisos/s por carro).</summary>
        public static event Action<CarController, CarController> CarTouch;
        float lastTouchEvt = -9f;

        void RaiseTouch(Collision c)
        {
            if (CarTouch == null || c.rigidbody == null || Time.time - lastTouchEvt < 0.2f) return;
            var other = c.rigidbody.GetComponent<CarController>();
            if (other == null) return;
            lastTouchEvt = Time.time;
            CarTouch(this, other);
        }

        Rigidbody rb;
        float shiftTimer;
        float engineRpm;
        float currentSteerAngle;
        readonly float[] surfaceGrip = { 1f, 1f, 1f, 1f };
        readonly TrackSurface[] surfaces = new TrackSurface[4];
        WheelFrictionCurve baseForward, baseSideways;
        readonly float[] wheelGrip = { 1f, 1f, 1f, 1f };
        readonly float[] fwdStiff = { 1f, 1f, 1f, 1f };
        readonly float[] sideStiff = { 1f, 1f, 1f, 1f };
        readonly float[] wheelLoadMul = { 1f, 1f, 1f, 1f };
        readonly float[] wheelFz = { 0f, 0f, 0f, 0f };
        /// <summary>v100: o teto lateral/de freada da categoria está limitando agora (telemetria do autoteste).</summary>
        public bool LateralCapActive { get; private set; }
        public bool BrakeCapActive { get; private set; }
        /// <summary>v100: escorregamento lateral médio (WheelHit.sidewaysSlip) das rodas da frente / de trás, e o pico da curva.</summary>
        public float FrontSideSlip { get; private set; }
        public float RearSideSlip { get; private set; }
        public float SidePeakSlip => baseSideways.extremumSlip * sideSlipScale;
        float sideSlipScale = 1f;
        const bool UseSideSlipScale = false;
        /// <summary>v100: fator médio de perda de atrito pela carga nas rodas no chão (1 = sem perda).</summary>
        public float AvgLoadMul { get { float t = 0f; int n = 0; for (int i = 0; i < 4; i++) if (wheelFz[i] > 0f) { t += wheelLoadMul[i]; n++; } return n > 0 ? t / n : 1f; } }

        public Rigidbody Body => rb;
        public float SpeedKmh => Velocity.magnitude * 3.6f;
        public float ForwardSpeed => Vector3.Dot(Velocity, transform.forward);
        public float Rpm { get; private set; }
        /// <summary>Replay: giro gravado (o som do motor segue a reprodução).</summary>
        public void SetReplayRpm(float rpm) => Rpm = rpm;
        public int Gear { get; private set; } = 1;
        public bool IsShifting => shiftTimer > 0f;
        public bool IsReversing => Gear < 0;
        public float SteerAngle => currentSteerAngle;
        public Vector3 LocalAcceleration { get; private set; }
        public float MaxWheelSlip { get; private set; }
        public float[] WheelSlip { get; } = new float[4];
        public TrackSurface[] WheelSurfaces => surfaces;
        public bool IsOffTrack { get; private set; }
        public int GroundedWheels { get; private set; }

        /// <summary>ONLINE: carro de OUTRO aparelho (OnlineGhost) — anda pela posição recebida da rede; aqui não tem física,
        /// dano, pit, mecânica nem penalidade (quem decide é o aparelho dono dele).</summary>
        [System.NonSerialized] public bool IsRemote;
        /// <summary>Velocidade do carro remoto (direção × velocidade enviadas pelo dono). O Rigidbody cinemático movido pela
        /// posição dava 0 km/h: a IA e a direção de prova o viam PARADO — fila atrás dele, amarela, sem ultrapassar
        /// ("os carros param se paramos e voltam a andar quando andamos").</summary>
        [System.NonSerialized] public Vector3 RemoteVelocity;

        public Vector3 Velocity
        {
            get
            {
                if (IsRemote) return RemoteVelocity;
#if UNITY_6000_0_OR_NEWER
                return rb.linearVelocity;
#else
                return rb.velocity;
#endif
            }
        }

        Vector3 lastVelocity;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Start()
        {
            if (spec != null) Setup(spec);
        }

        public void Setup(CarSpec carSpec)
        {
            spec = carSpec;
            rb = GetComponent<Rigidbody>();
            rb.mass = spec.massKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.centerOfMass = spec.centerOfMassOffset;
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.3f;
#else
            rb.drag = 0.02f;
            rb.angularDrag = 0.3f;
#endif

            baseForward = new WheelFrictionCurve
            {
                extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f,
                // v100: slick com perda pela carga → deslizamento longitudinal mais progressivo (0,5 → 0,7; pesquisa 04/11).
                asymptoteValue = spec.tyreLoadSensitivity > 0f ? 0.7f : 0.5f, stiffness = spec.forwardStiffness
            };
            baseSideways = new WheelFrictionCurve
            {
                extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = spec.sidewaysStiffness
            };

            foreach (var w in wheels)
            {
                if (w == null) continue;
                w.mass = spec.wheelMass;
                w.radius = spec.wheelRadius;
                w.suspensionDistance = spec.suspensionDistance;
                w.forceAppPointDistance = 0.1f;
                w.wheelDampingRate = 0.25f;
                w.suspensionSpring = new JointSpring { spring = spec.spring, damper = spec.damper, targetPosition = spec.targetPosition };
                w.forwardFriction = baseForward;
                w.sidewaysFriction = baseSideways;
                w.ConfigureVehicleSubsteps(5f, 12, 15);
            }

            Gear = 1;
            engineRpm = spec.idleRpm;
        }

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            foreach (var w in wheels) { w.motorTorque = 0f; w.brakeTorque = float.MaxValue; }
            Gear = 1;
            engineRpm = spec != null ? spec.idleRpm : 1000f;
            lastVelocity = Vector3.zero;
        }

        void FixedUpdate()
        {
            // DANO PERMANENTE (regra do usuário): bateu, é dano até o PIT — sem auto-reparo em
            // corrida. O Repair() (PitLaneSystem, ao entrar no pit) é a ÚNICA forma de voltar a 100%.
            if (spec == null || rb.isKinematic) return;
            CacheWheelPoses();

            Vector3 v = Velocity;
            LocalAcceleration = transform.InverseTransformDirection((v - lastVelocity) / Time.fixedDeltaTime);
            lastVelocity = v;

            if (shiftTimer > 0f) shiftTimer -= Time.fixedDeltaTime;

            ReadGround();
            ApplySteering();
            UpdateGearbox();
            ApplyDrive();
            ApplyBrakes();
            ApplyDownforce();
            ApplyAntiRoll();
            ApplyStability();
        }

        float[] wheelSpin = new float[4];
        readonly Vector3[] wheelLocalPos = new Vector3[4];
        readonly Quaternion[] wheelLocalRot = new Quaternion[4];
        bool wheelPoseCached;
        /// <summary>Descida só VISUAL de cada roda (m): pneu do modelo menor que o raio físico (FP04: 0,26 m contra
        /// 0,34 m) encosta no chão sem mudar a física; a carroceria importada desce junto (CarFactory).</summary>
        public readonly float[] wheelVisualDrop = new float[4];
        /// <summary>Multiplicador extra de aderência por roda (1 = normal). Hoje: pneu sujo de grama (TyreDirt).</summary>
        public readonly float[] extraGrip = { 1f, 1f, 1f, 1f };
        /// <summary>Fator de potência do motor (CarSystems: temperatura, desgaste, combustível; 0 = motor parado).</summary>
        public float powerFactor = 1f;
        /// <summary>Eficiência do freio por roda (CarSystems: disco frio, fadiga, falha).</summary>
        public readonly float[] brakeEff = { 1f, 1f, 1f, 1f };
        /// <summary>Torque de freio de pedal aplicado no último passo da física, por roda (N·m) — calor do disco.</summary>
        public readonly float[] lastBrakeTorque = new float[4];
        float extraMassKg;
        /// <summary>Massa extra (combustível) somada ao carro.</summary>
        public void SetExtraMass(float kg)
        {
            extraMassKg = Mathf.Max(0f, kg);
            if (rb != null && spec != null) rb.mass = spec.massKg + extraMassKg;
        }

        /// <summary>Pose da roda RELATIVA ao chassi, no passo da física (mesmo estado de rb.position/rotation).
        /// O Update reaplica sobre o transform INTERPOLADO: antes a roda vinha do GetWorldPose (física, sem
        /// interpolação) e a carroceria interpolada — com FPS ≠ 50 Hz a roda "tremia" subindo e descendo.</summary>
        void CacheWheelPoses()
        {
            Quaternion inv = Quaternion.Inverse(rb.rotation);
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null) continue;
                wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheelLocalPos[i] = inv * (pos - rb.position);
                wheelLocalRot[i] = inv * rot;
            }
            wheelPoseCached = true;
        }

        void Update()
        {
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null || wheelMeshes[i] == null) continue;
                Vector3 pos; Quaternion rot;
                if (wheelPoseCached && rb != null && !rb.isKinematic)
                {
                    pos = transform.TransformPoint(wheelLocalPos[i]);
                    rot = transform.rotation * wheelLocalRot[i];
                }
                else wheels[i].GetWorldPose(out pos, out rot);
                wheelSpin[i] = Mathf.Repeat(wheelSpin[i] + wheels[i].rpm * 6f * Time.deltaTime, 360f);
                if (wheelVisualDrop[i] != 0f) pos -= transform.up * wheelVisualDrop[i];
                wheelMeshes[i].SetPositionAndRotation(pos, rot * Quaternion.Euler(wheelSpin[i], 0f, 0f));
            }
        }

        void ReadGround()
        {
            int grounded = 0, offTrack = 0;
            float maxSlip = 0f;
            // Pneu POR RODA (v80): composto × desgaste × temperatura × água; dano lateral e da própria roda.
            for (int i = 0; i < 4; i++)
            {
                float g = Mathf.Clamp(RaceX.Core.PitLaneSystem.WheelGrip(this, i), 0.08f, 1.08f);
                float sideDmg = (i == FL || i == RL) ? DmgLeft : DmgRight;
                g *= 1f - 0.08f * sideDmg;
                if (damage != null) g *= damage.WheelGripMul(i);
                wheelGrip[i] = g;
            }
            float frontSide = 0f, rearSide = 0f;
            // apoio/peso agora (L/P): só monopostos com o pneu da pesquisa (GT mantém a curva de sempre)
            // DESLIGADO (medido 04/11): com a escala o treino F2 de 12 min foi de 1 PT/64 batidas (v100) para 5–8 PT/240–330 batidas
            // (v100c–e) — o pneu dá menos força com o mesmo esterço em alta e o carro abre na saída da última curva (bico da mureta).
            sideSlipScale = UseSideSlipScale && spec.tyreLoadSensitivity > 0f && rb.mass > 50f
                ? 1f + Mathf.Max(0f, spec.downforceCoefficient) * rb.linearVelocity.sqrMagnitude / (rb.mass * 9.81f) : 1f;
            float fz0 = rb.mass * 9.81f * 0.25f;   // carga estática por roda (com combustível)
            float sumLat = 0f, sumLong = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = wheels[i];
                wheelLoadMul[i] = 1f;
                wheelFz[i] = 0f;
                if (w.GetGroundHit(out WheelHit hit))
                {
                    grounded++;
                    wheelFz[i] = Mathf.Max(0f, hit.force);
                    wheelLoadMul[i] = spec.TyreLoadFactor(wheelFz[i], fz0);
                    hit.collider.TryGetComponent<TrackSurface>(out var surface);
                    surfaces[i] = surface;
                    surfaceGrip[i] = surface != null ? surface.FrictionMultiplier : 1f;
                    // GRAMA/TERRA MOLHADA: atrito do pneu cai de ~0,3–0,5 (seca) para ≤ 0,3 (molhada) — medições com pneu de
                    // rua (não há dado publicado com slick de F1). Até −35% com a pista alagada.
                    if (surface != null && surface.IsOffTrack) surfaceGrip[i] *= 1f - 0.35f * Mathf.Clamp01(RaceX.Core.WeatherSystem.Wetness);
                    // ÁREA DO PIT (corredor, faixa de trabalho, frente das garagens): piso de asfalto/concreto mesmo onde a
                    // malha visível é só a laje (por baixo há grama) — aderência normal e não conta como "fora da pista".
                    if (surface != null && surface.IsOffTrack && InPitAreaNow()) { surfaceGrip[i] = 1f; surface = null; surfaces[i] = null; }
                    if (surface != null && surface.IsOffTrack && !InPitAreaNow()) offTrack++;
                    float slip = Mathf.Max(Mathf.Abs(hit.forwardSlip), Mathf.Abs(hit.sidewaysSlip));
                    WheelSlip[i] = slip;
                    if (i <= FR) frontSide += Mathf.Abs(hit.sidewaysSlip); else rearSide += Mathf.Abs(hit.sidewaysSlip);
                    maxSlip = Mathf.Max(maxSlip, slip);
                }
                else
                {
                    surfaces[i] = null;
                    surfaceGrip[i] = 1f;
                    WheelSlip[i] = 0f;
                }

                bool rear = i >= RL;
                float sideFactor = rear && handbrake && !pitControlled ? spec.handbrakeRearStiffness : 1f;
                float tireGrip = wheelGrip[i];
                // TRAÇÃO/FREIO pela MÉDIA DO EIXO (o diferencial divide o torque entre as duas rodas):
                // antes cada roda tinha a sua — com o pneu DURO frio (janela 100–130 °C) a esquerda e a
                // direita aqueciam diferente e o carro puxava para um lado na reta. A aderência LATERAL
                // continua por roda (dianteira gasta sai de frente, traseira de traseira). Roda arrancada
                // não entra na média (o diferencial manda o torque para ela e ela patina no ar).
                int mate = i ^ 1;
                bool mateOff = damage != null && damage.Detached((int)DamageZone.WheelFL + mate);
                bool selfOff = damage != null && damage.Detached((int)DamageZone.WheelFL + i);
                float axleGrip = selfOff || mateOff ? tireGrip : (tireGrip + wheelGrip[mate]) * 0.5f;
                // v100: perda de atrito com a carga (apoio aerodinâmico + transferência), por roda — o PhysX multiplica o atrito
                // pela carga da roda; aqui a rigidez cai com (Fz/Fz0)^−0,20 (só monopostos; GT sem perda).
                float lm = wheelLoadMul[i];
                var f = baseForward; f.stiffness = spec.forwardStiffness * surfaceGrip[i] * axleGrip * extraGrip[i] * lm;
                fwdStiff[i] = f.stiffness;
                var s = baseSideways; s.stiffness = spec.sidewaysStiffness * surfaceGrip[i] * sideFactor * tireGrip * extraGrip[i] * lm;
                // v100 (pesquisa 04/11, receita da comunidade Unity — "The dark art of Unity wheel colliders", Unity Discussions):
                // o escorregamento lateral da roda cresce com a velocidade para o MESMO ângulo; em alta o pneu passava do pico com
                // pouco esterço e caía para 75% da força — o carro saía de frente e ia reto. Pico e assíntota laterais × (1 + apoio/peso).
                s.extremumSlip = baseSideways.extremumSlip * sideSlipScale;
                s.asymptoteSlip = baseSideways.asymptoteSlip * sideSlipScale;
                sideStiff[i] = s.stiffness;
                sumLat += s.stiffness * wheelFz[i];
                sumLong += f.stiffness * wheelFz[i];
                w.forwardFriction = f;
                w.sidewaysFriction = s;
            }
            // v100: TETOS da categoria (F2 3,9 g lateral / 3,5 g freada; F1 2026 6,0 / 4,0 g): a força que os 4 pneus podem
            // dar não passa de teto × peso. Atuam só em alta (F2 acima de ~290 km/h na curva, ~240 km/h na freada).
            float mg = rb.mass * 9.81f;
            float latMul = spec.lateralCapG > 0f && sumLat > spec.lateralCapG * mg ? spec.lateralCapG * mg / sumLat : 1f;
            float lonMul = spec.brakeCapG > 0f && sumLong > spec.brakeCapG * mg ? spec.brakeCapG * mg / sumLong : 1f;
            if (latMul < 1f || lonMul < 1f)
            {
                for (int i = 0; i < 4; i++)
                {
                    var w = wheels[i];
                    if (w == null) continue;
                    var f = w.forwardFriction; f.stiffness *= lonMul; fwdStiff[i] = f.stiffness; w.forwardFriction = f;
                    var sd = w.sidewaysFriction; sd.stiffness *= latMul; sideStiff[i] = sd.stiffness; w.sidewaysFriction = sd;
                }
            }
            FrontSideSlip = frontSide * 0.5f;
            RearSideSlip = rearSide * 0.5f;
            LateralCapActive = latMul < 0.999f;
            BrakeCapActive = lonMul < 0.999f;
            GroundedWheels = grounded;
            IsOffTrack = offTrack >= 2;
            MaxWheelSlip = maxSlip;

            if (IsOffTrack)
            {
                // Arrasto FORA DA PISTA: 0.04*v (era 0.25, depois 0.08 — ainda fazia governador:
                // o equilíbrio motor x arrasto limitava a velocidade final na grama). A 113 km/h
                // agora dá só 1.25 m/s² — o motor supera; a punição segue no GRIP (0.55).
                rb.AddForce(-Velocity * (spec.massKg * 0.04f), ForceMode.Force);
            }
        }

        /// <summary>Zera TODOS os estados residuais do piloto automático do pit no carro — chamado
        /// nos pontos de devolução de controle. Sem isso qualquer estado esquecido (marcha em ré,
        /// troca "em andamento" congelada, pedais ext*) limitava o carro depois do pit.</summary>
        public void PitReleaseReset()
        {
            pitControlled = false;
            extThrottle = 0f; extBrake = 0f; extSteer = 0f;
            if (Gear < 1) Gear = 1; // ré engatada pelo autopilot não vaza para o piloto
            shiftTimer = 0f;        // troca congelada não pode cortar o torque
        }

        void ApplySteering()
        {
            float speedFactor = Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, spec.topSpeedKmh));
            float maxAngle = spec.maxSteerAngle * Mathf.Lerp(1f, spec.steerAtTopSpeed, speedFactor);
            float steerIn = pitControlled ? extSteer : Mathf.Clamp(steerInput, -1f, 1f);
            float steerRate = 120f;
            if (spec.rectangularSteeringWheel && !pitControlled)
            {
                float sf = Mathf.Clamp01(SpeedKmh / 180f);
                maxAngle = spec.maxSteerAngle * Mathf.Lerp(1f, 0.23f, Mathf.Pow(sf, 0.8f));
                steerIn = Mathf.Sign(steerIn) * Mathf.Pow(Mathf.Abs(steerIn), 1.15f);
                steerRate = Mathf.Lerp(120f, 85f, sf);
            }
            float target = (inputLocked ? 0f : steerIn) * maxAngle;
            // Suspensão/convergência torta após batida lateral: o carro "puxa" para o lado danificado.
            // Era 1,6° (≈40% do esterço de um F1 em alta) e, com os lados trocados, o carro puxava sempre
            // para a esquerda depois de qualquer toque. Agora no máximo 0,4° com o lado batido 100%.
            target += (DmgRight - DmgLeft) * 0.4f;
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, target, steerRate * Time.fixedDeltaTime);
            wheels[FL].steerAngle = currentSteerAngle;
            wheels[FR].steerAngle = currentSteerAngle;
        }

        float DrivenWheelRpm()
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!IsDriven(i)) continue;
                sum += wheels[i].rpm; n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        bool IsDriven(int i)
        {
            switch (spec.drivetrain)
            {
                case Drivetrain.FWD: return i <= FR;
                case Drivetrain.RWD: return i >= RL;
                default: return true;
            }
        }

        float CurrentRatio()
        {
            if (Gear < 0) return -spec.reverseRatio;
            int idx = Mathf.Clamp(Gear - 1, 0, spec.GearCount - 1);
            return spec.gearRatios[idx] * spec.finalDrive;
        }

        void UpdateGearbox()
        {
            float throttle = inputLocked ? 0f : throttleInput;
            // Sob a rota do pit só o freio EXTERNO conta (como acelerador e direção): a IA do próprio carro segue escrevendo
            // brakeInput e, com o carro parado na garagem, engatava a ré e o segurava (v99: resgate 5 s após "liberado").
            float brake = inputLocked ? 0f : pitControlled ? extBrake : brakeInput;
            float fwd = ForwardSpeed;

            // Ré só engata com o carro PRATICAMENTE PARADO (|v| < 0,8 m/s). Antes valia "fwd < 0,8" — incluía o carro já rolando de
            // ré: um carro rodado/empurrado andando para trás com o freio pisado (estabilizador da IA) engatava a ré, o freio virava
            // acelerador da ré e ele disparava para trás até bater (relato do usuário: Ryan Walsh, perda total de traseira).
            if (Gear > 0 && brake > 0.1f && throttle < 0.1f && Mathf.Abs(fwd) < 0.8f)
            {
                Gear = -1;
                shiftTimer = 0f;
            }
            else if (Gear < 0)
            {
                // Sai da ré quando o acelerador é pressionado (pedal real, qualquer mapeamento de input).
                if (throttleInput > 0.1f) { Gear = 1; shiftTimer = 0f; }
            }
            else if (Gear > 1 && Mathf.Abs(fwd) < 2f && SpeedKmh < 8f)
            {
                // Parada brusca (colisão): cai direto para 1ª sem esperar reduzir 6 marchas sem torque.
                Gear = 1;
                shiftTimer = 0f;
            }

            float groundRpm = Mathf.Abs(fwd) / (2f * Mathf.PI * spec.wheelRadius) * 60f;
            float wheelRpm = Mathf.Min(Mathf.Abs(DrivenWheelRpm()), groundRpm * 1.1f + 30f);
            float targetRpm = Mathf.Max(spec.idleRpm, wheelRpm * Mathf.Abs(CurrentRatio()));
            if (GroundedWheels == 0 || IsShifting) targetRpm = Mathf.Lerp(spec.idleRpm, spec.rpmLimit, throttle);
            engineRpm = Mathf.Lerp(engineRpm, Mathf.Min(targetRpm, spec.rpmLimit * 1.02f), Time.fixedDeltaTime / Mathf.Max(0.02f, spec.engineInertia * 0.2f));
            Rpm = engineRpm;

            float downshiftPct = spec.downshiftRpmPercent;
            if (Gear > 0 && !IsShifting)
            {
                float rpmFromWheels = wheelRpm * Mathf.Abs(CurrentRatio());
                if (rpmFromWheels >= spec.rpmLimit * spec.upshiftRpmPercent && Gear < spec.GearCount)
                {
                    Gear++;
                    shiftTimer = spec.shiftTime;
                }
                else if (Gear > 1)
                {
                    float lowerRatio = spec.gearRatios[Gear - 2] * spec.finalDrive;
                    if (rpmFromWheels <= spec.rpmLimit * downshiftPct && wheelRpm * lowerRatio < spec.rpmLimit * spec.upshiftRpmPercent * 0.95f)
                    {
                        Gear--;
                        shiftTimer = spec.shiftTime;
                    }
                }
            }
        }

        const float MaxReverseSpeed = 5.5f;   // m/s (~20 km/h)

        void ApplyDrive()
        {
            float throttle = inputLocked ? 0f : throttleInput;
            if (Gear < 0) throttle = inputLocked ? 0f : brakeInput;
            // Ré limitada a ~20 km/h (valor de projeto; carro de corrida só usa a ré para manobrar).
            if (Gear < 0 && -ForwardSpeed > MaxReverseSpeed) throttle = 0f;
            if (pitControlled)
            {
                throttle = extThrottle;
                if (Gear < 0 && extThrottle > 0.1f) Gear = 1; // piloto automático em ré: engata a 1ª (sai da ré)
                else if (Gear < 0) throttle = 0f;
            }

            if (wrecked) throttle = 0f;
            float torque = 0f;
            bool overSpeed = SpeedKmh >= spec.topSpeedKmh || engineRpm >= spec.rpmLimit;
            if (!IsShifting && !overSpeed)
            {
                torque = spec.EvaluateTorque(engineRpm) * CurrentRatio() * throttle * 0.9f * powerFactor;
            }

            int driven = spec.drivetrain == Drivetrain.AWD ? 4 : 2;
                float tc = spec.tractionControl * assistLevel;
                for (int i = 0; i < 4; i++)
                {
                    if (!IsDriven(i)) { wheels[i].motorTorque = 0f; continue; }
                    float share;
                    if (spec.drivetrain == Drivetrain.AWD)
                        share = (i <= FR ? spec.awdFrontShare : 1f - spec.awdFrontShare) * 0.5f;
                    else
                        share = 1f / driven;

                    float wheelTorque = torque * share * DamageFactor;
                    // POTÊNCIA É A MESMA — quem limita é o PNEU: o torque que chega ao chão não passa do que a
                    // aderência daquela roda aguenta (carga × aderência × raio, com ~15% de patinada).
                    // Antes, na pista molhada com pneu de seco, o botão do celular (acelerador 100%) fazia a
                    // roda patinar além do pico, a força caía à metade e o carro "perdia toda a potência".
                    // Não há teto de velocidade: em alta, o arrasto pede pouca força e o carro anda.
                    if (wheels[i].GetGroundHit(out WheelHit gh) && gh.force > 1f)
                    {
                        // Com assistência o teto é o PICO da curva (slip 0,4): passar dele faz a roda disparar até o
                        // trecho de 50% da força (slip 0,8). Sem assistência sobra 15% para patinar de propósito.
                        float gripTorque = gh.force * fwdStiff[i] * baseForward.extremumValue * wheels[i].radius * (tc > 0f ? 1.0f : 1.15f);
                        if (Mathf.Abs(wheelTorque) > gripTorque) wheelTorque = Mathf.Sign(wheelTorque) * gripTorque;
                    }
                    if (tc > 0f && wheels[i].GetGroundHit(out WheelHit hit))
                    {
                        // TC: começa em slip 0.40 (não 0.30), rampa mais suave e piso 0.40 (não 0.15).
                        // Antes cortava até 85% do torque em aceleração de verdade (slip 0.3-0.6 é
                        // normal sob potência) — o carro engasgava no meio da reta ("sem potência").
                        // v81: o TC SEGURA a roda no pico de aderência (o torque já vem limitado a μ·carga acima) —
                        // antes cortava até 60% do torque com pouca aderência: o carro patinava, perdia força e
                        // ficava preso em ~81 km/h na reta com intermediário na pista 81% molhada. Com carga
                        // aerodinâmica a aderência disponível cresce com a velocidade, então o carro segue acelerando.
                        float excess = Mathf.Abs(hit.forwardSlip) - 0.5f;
                        if (excess > 0f) wheelTorque *= Mathf.Max(0.8f, Mathf.Clamp01(1f - excess * 1.2f * tc));
                    }
                    wheels[i].motorTorque = wheelTorque;
                }
        }

        void ApplyBrakes()
        {
            // Sob a rota do pit o freio vem SÓ da rota (extBrake). Antes era max(brakeInput, extBrake): a IA do carro (que continua
            // pensando sob controle do pit) freava por "carro parado à frente" (o vizinho da garagem seguinte) enquanto a rota
            // acelerava a 0,85 — o carro ficava parado na garagem até o resgate (autoteste e Sven Larsen, treinos R/S, 03/11).
            float brake = inputLocked || wrecked ? 1f : pitControlled ? extBrake : (Gear < 0 ? 0f : brakeInput);
            // SAÍDA DE RÉ (regra do usuário): de ré a 50 km/h, ao acelerar a 1ª engata na hora
            // e o carro PARA quase que instantaneamente (freio 100% enquanto anda para trás com
            // pedal acionado) — nada de arrastar de ré por 10-15 s antes de lançar.
            if (Gear >= 1 && ForwardSpeed < -0.5f)
            {
                bool wantsForward = pitControlled ? extThrottle > 0.1f : throttleInput > 0.1f;
                if (wantsForward) brake = 1f;
            }
            float front = spec.brakeTorque * brake * spec.frontBrakeBias * 2f;
            float rear = spec.brakeTorque * brake * (1f - spec.frontBrakeBias) * 2f;

            for (int i = 0; i < 4; i++)
            {
                float t = (i <= FR ? front : rear) * brakeEff[i];
                if (t > 0f && spec.abs > 0f && wheels[i].GetGroundHit(out WheelHit hit))
                {
                    // ABS (v99): segura a roda PERTO DO PICO de aderência — o torque de freio não passa de 95% do que o pneu
                    // transmite (carga × atrito longitudinal × raio), como o controle de tração faz na aceleração. Antes só
                    // tirava 35% do torque DEPOIS de a roda travar: o torque seguia ~4× acima da aderência, a roda ia para o
                    // trecho de 50% da curva (deslizando) e o carro freava ~1 g com o pedal no fundo (medido 03/11: mediana
                    // 9,7 m/s² acima de 100 km/h). F2 real: ~3,5 g freando (Pit Debrief).
                    if (hit.force > 1f)
                    {
                        float gripTorque = hit.force * fwdStiff[i] * baseForward.extremumValue * wheels[i].radius * 0.95f;
                        if (t > gripTorque) t = gripTorque;
                    }
                    if (Mathf.Abs(hit.forwardSlip) > 0.45f) t *= 1f - 0.5f * spec.abs;
                }
                lastBrakeTorque[i] = t;
                if (i >= RL && handbrake && !inputLocked && !pitControlled) t = Mathf.Max(t, spec.handbrakeTorque);
                // Freio de arrasto em baixa usa o acelerador EFETIVO (piloto automático incluído):
                // antes olhava só o throttleInput do jogador — com o pit autopilot acelerando e o
                // jogador sem tocar nada, esse freio ficava ligado e o carro "só andava se acelera".
                float driveThr = pitControlled ? extThrottle : throttleInput;
                if (Gear > 0 && !inputLocked && driveThr < 0.05f && brake < 0.05f && (!handbrake || pitControlled)) t = spec.massKg * 0.2f;
                wheels[i].brakeTorque = t;
            }
        }

        void ApplyStability()
        {
            // ANTI-GIRO DO PIT (regra do usuário): o piloto automático não pode rodar o carro —
            // na grama/baixa velocidade o steer cheio fazia 2 giros antes de achar o box.
            // Teto físico de yaw (~80°/s) para o carro do pit, em qualquer piso.
            if (pitControlled && GroundedWheels > 0)
            {
                Vector3 avP = rb.angularVelocity;
                if (Mathf.Abs(avP.y) > 1.4f) rb.angularVelocity = new Vector3(avP.x, Mathf.Sign(avP.y) * 1.4f, avP.z);
            }
            float speed = Mathf.Abs(ForwardSpeed);
            if (speed < 5f || GroundedWheels == 0) return;
            if (assistLevel <= 0.01f) return;
            // Assistência MAIS SUAVE (v80): deixa o carro derrapar de verdade (ângulo até ~22° antes de
            // corrigir) — pneu frio/gasto/molhado agora escorrega e a traseira sai.
            float grip = RaceX.Core.PitLaneSystem.TireGrip(this);
            Vector3 av = rb.angularVelocity;
            float yawMax = Mathf.Lerp(2.6f, 1.0f, Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, spec.topSpeedKmh))) / Mathf.Max(0.9f, grip);
            if (Mathf.Abs(av.y) > yawMax) rb.angularVelocity = new Vector3(av.x, Mathf.Sign(av.y) * Mathf.Lerp(Mathf.Abs(av.y), yawMax, 0.5f), av.z);
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Vector3 vel = Velocity; vel.y = 0f;
            float slip = Vector3.SignedAngle(fwd, vel, Vector3.up);
            if (Mathf.Abs(slip) > 22f)
                rb.AddTorque(transform.up * (-Mathf.Sign(slip) * (Mathf.Abs(slip) - 22f) * 0.022f * spec.massKg * assistLevel * Mathf.Max(0.3f, grip)), ForceMode.Force);
        }

        // ---- Vácuo (slipstream): físico, vale igual para jogador e IA ----
        static readonly System.Collections.Generic.List<CarController> allCars = new System.Collections.Generic.List<CarController>();
        void OnEnable() { if (!allCars.Contains(this)) allCars.Add(this); }
        void OnDisable() { allCars.Remove(this); }
        float slipTimer;
        /// <summary>0 = sem vácuo, 1 = vácuo máximo (redução de arrasto de 35%).</summary>
        public float Slipstream { get; private set; }
        void UpdateSlipstream()
        {
            slipTimer -= Time.fixedDeltaTime;
            if (slipTimer > 0f) return;
            slipTimer = 0.1f; // 10 Hz basta
            float best = 0f;
            float mySpeed = ForwardSpeed;
            if (mySpeed > 20f && GroundedWheels > 0)
            {
                Vector3 myPos = transform.position;
                for (int i = 0; i < allCars.Count; i++)
                {
                    var o = allCars[i];
                    if (o == null || o == this || o.ForwardSpeed < 20f) continue;
                    Vector3 d = o.transform.position - myPos;
                    if (d.sqrMagnitude > 900f) continue;
                    Vector3 local = transform.InverseTransformDirection(d);
                    if (local.z < 3f || local.z > 25f || Mathf.Abs(local.x) > 1.6f) continue;
                    if (Vector3.Dot(o.transform.forward, transform.forward) < 0.9f) continue;
                    // mais forte de perto (~5 m), some a 25 m
                    float f = 1f - Mathf.Clamp01((local.z - 5f) / 20f);
                    f *= 1f - Mathf.Abs(local.x) / 1.6f * 0.5f;
                    if (f > best) best = f;
                }
            }
            Slipstream = best;
        }

        float floorNoise, floorNoiseT;

        /// <summary>Apoio aerodinâmico por EIXO (v80) com efeitos REAIS de dano (fontes: F1/Williams/Mercedes):
        /// • asa dianteira: menos apoio na frente → sai de frente (subesterço) em curva rápida e gasta
        ///   mais o pneu dianteiro (endplate perdido ≈ 0,3 s/volta — Vowles/Williams);
        /// • traseira/asa/difusor: menos apoio atrás → sai de traseira (sobre-esterço), menos arrasto;
        /// • assoalho/laterais: perde apoio geral (50–100 "pontos" = 0,5–1 s/volta) e fica INCONSISTENTE
        ///   (Bottas/Verstappen: escorrega em curva rápida, frenagem imprevisível, balanço mudando).</summary>
        void ApplyDownforce()
        {
            Vector3 v = Velocity;
            float speedSq = v.sqrMagnitude;
            float floor = Mathf.Max(DmgLeft, DmgRight);
            if (GroundedWheels > 0)
            {
                floorNoiseT -= Time.fixedDeltaTime;
                if (floorNoiseT <= 0f) { floorNoiseT = 0.35f; floorNoise = UnityEngine.Random.Range(-1f, 1f); }
                float total = spec.downforceCoefficient * speedSq * (1f - 0.15f * floor) * (1f + floorNoise * 0.08f * floor);
                bool formula = spec.rectangularSteeringWheel || spec.downforceCoefficient >= 3.5f;
                float frontShare = formula ? 0.42f : 0.45f;
                float front = total * frontShare * (1f - 0.35f * DmgFront);
                float rear = total * (1f - frontShare) * (1f - 0.30f * DmgRear);
                // Peça ARRANCADA: bico/asa dianteira perdidos = -60% na frente (sai muito de frente);
                // asa traseira perdida = -60% atrás (traseira solta). Fonte: asa = maior parte do apoio.
                if (damage != null && damage.Detached((int)DamageZone.FrontWing)) front = total * frontShare * 0.4f;
                if (damage != null && damage.Detached((int)DamageZone.RearWing)) rear = total * (1f - frontShare) * 0.4f;
                Vector3 down = -transform.up;
                Vector3 fwd = transform.forward * (spec.wheelbase * 0.5f);
                rb.AddForceAtPosition(down * front, rb.worldCenterOfMass + fwd, ForceMode.Force);
                rb.AddForceAtPosition(down * rear, rb.worldCenterOfMass - fwd, ForceMode.Force);
            }
            UpdateSlipstream();
            float dragMul = (1f - 0.35f * Slipstream) * (1f - 0.10f * DmgRear + 0.03f * DmgFront);
            rb.AddForce(-v.normalized * 0.5f * 1.2f * spec.dragCoefficient * speedSq * 0.5f * dragMul, ForceMode.Force);
        }

        void ApplyAntiRoll()
        {
            AntiRollAxle(wheels[FL], wheels[FR]);
            AntiRollAxle(wheels[RL], wheels[RR]);
        }

        void AntiRollAxle(WheelCollider left, WheelCollider right)
        {
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out WheelHit hitL);
            if (groundedL) travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
            bool groundedR = right.GetGroundHit(out WheelHit hitR);
            if (groundedR) travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;

            float force = (travelL - travelR) * spec.antiRollStiffness;
            if (groundedL) rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        public string SurfaceName()
        {
            var s = surfaces != null && surfaces.Length > 0 && surfaces[0] != null ? surfaces[0].name : "Asphalt";
            return string.IsNullOrEmpty(s) ? "Asphalt" : s;
        }

        /// <summary>Avaria leve de desempenho: cada batida forte corta um pouco da potencia e do torque (recupera lentamente).</summary>
        public float DamageFactor { get; private set; } = 1f;

        // Dano POR ZONA (0..1, acumulado) — alimenta o painel visual do carro no HUD.
        // Frente/traseira/lados, pela direção da normal do impacto em espaço local.
        public float DmgFront => damage != null ? damage.FrontLevel : 0f;
        public float DmgRear => damage != null ? damage.RearLevel : 0f;
        public float DmgLeft => damage != null ? damage.LeftLevel : 0f;
        public float DmgRight => damage != null ? damage.RightLevel : 0f;

        /// <summary>Conserta TODO o dano (parada no pit — mecânicos trabalham enquanto o carro está no box).</summary>
        /// <summary>Parada no box: troca bico/asa dianteira e pneus (CarDamage.RepairAtPitStop). O resto do dano continua —
        /// a perda geral das batidas (DamageFactor) recupera só a metade (a parte da asa dianteira).</summary>
        /// <summary>Conserto COMPLETO na garagem (treino/classificação, ao continuar a sessão pelo hub).</summary>
        int pitAreaHint = -1, pitAreaFrame = -1;
        bool pitAreaCache;
        /// <summary>Carro dentro da área do pit (calculado no máximo uma vez por passo de física).</summary>
        bool InPitAreaNow()
        {
            if (pitAreaFrame == Time.frameCount) return pitAreaCache;
            pitAreaFrame = Time.frameCount;
            pitAreaCache = RaceX.Core.PitLaneSystem.InPitArea(transform.position, ref pitAreaHint);
            return pitAreaCache;
        }

        public void GarageRepair()
        {
            if (wrecked) return;
            DamageFactor = 1f;
            if (damage != null) damage.Repair();
        }

        public void Repair()
        {
            if (wrecked) return; // perda total não tem conserto
            DamageFactor = Mathf.Lerp(DamageFactor, 1f, 0.5f);
            if (damage != null) damage.RepairAtPitStop();
        }

        /// <summary>Contato PROLONGADO com outro carro (um encostado no outro): sem "escalar" — o carro de cima não ganha
        /// subida e recebe uma força para baixo até soltar.</summary>
        void OnCollisionStay(Collision collision)
        {
            if (IsRemote) return;
            if (rb == null || collision.rigidbody == null || collision.rigidbody.GetComponent<CarController>() == null) return;
            RaiseTouch(collision);
            Vector3 v = rb.linearVelocity;
            if (v.y > 0.5f) rb.linearVelocity = new Vector3(v.x, 0.5f, v.z);
            if (transform.position.y > collision.rigidbody.position.y + 0.25f)
                rb.AddForce(Vector3.down * rb.mass * 6f, ForceMode.Force);
        }

        void OnCollisionEnter(Collision collision)
        {
            // Carro remoto (online): o toque é tratado no aparelho dono dele — aqui não sofre dano nem correção de física.
            if (IsRemote) return;
            float relative = collision.relativeVelocity.magnitude * 3.6f;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            if (relative > 18f)
            {
                DamageFactor = Mathf.Max(0.80f, DamageFactor - Mathf.Clamp01((relative - 18f) / 90f) * 0.10f);
            }
            // Dano POR ZONA pelo PONTO do contato e pela desaceleração estimada (CarDamage): toques do
            // pelotão (< 5 g) não danificam nada; batida forte arranca a peça; ≥ 50 g no muro = perda total.
            if (damage != null) damage.OnImpact(collision);
            Collided?.Invoke(relative, point);
            RaiseTouch(collision);

            // ANTI-DECOLAR CARRO × CARRO: num toque entre carros (roda com roda, bico na traseira) a física lançava o carro
            // para cima — na F1 um carro (800 kg com piloto, mínimo FIA 2025) decolar é exceção. Corta a velocidade VERTICAL
            // de subida e o giro de capotar/empinar que o contato gera; o empurrão horizontal continua (o toque existe).
            if (collision.rigidbody != null && collision.rigidbody.GetComponent<CarController>() != null && rb != null)
            {
                Vector3 v = rb.linearVelocity;
                if (v.y > 1.2f) rb.linearVelocity = new Vector3(v.x, 1.2f, v.z);
                Vector3 avL = transform.InverseTransformDirection(rb.angularVelocity);
                avL.x = Mathf.Clamp(avL.x, -1.5f, 1.5f);   // empinar/mergulhar
                avL.z = Mathf.Clamp(avL.z, -1.5f, 1.5f);   // capotar de lado
                rb.angularVelocity = transform.TransformDirection(avL);
            }
            // ANTI-VOAR: parede/objeto estático (sem rigidbody) em velocidade média — mata o ricochete
            // que lança o carro pro alto (patrimônio do pit: muro, prédio, portão, pórtico).
            if (collision.rigidbody == null && collision.contactCount > 0)
            {
                Vector3 n = collision.GetContact(0).normal;
                Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
                float vn = Vector3.Dot(v, n);
                if (vn < -4f) // batendo contra a parede (componente normal para dentro)
                {
                    Vector3 tangent = v - n * vn;      // só desliza
                    rb.linearVelocity = tangent * 0.5f; // rebate a 50%, sem componente normal
                    Vector3 av = rb.angularVelocity;
                    if (av.magnitude > 6f) rb.angularVelocity = av.normalized * 6f; // sem capotar
                }
            }
        }


    }
}
