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

        public event Action<float, Vector3> Collided;

        Rigidbody rb;
        float shiftTimer;
        float engineRpm;
        float currentSteerAngle;
        readonly float[] surfaceGrip = { 1f, 1f, 1f, 1f };
        readonly TrackSurface[] surfaces = new TrackSurface[4];
        WheelFrictionCurve baseForward, baseSideways;

        public Rigidbody Body => rb;
        public float SpeedKmh => Velocity.magnitude * 3.6f;
        public float ForwardSpeed => Vector3.Dot(Velocity, transform.forward);
        public float Rpm { get; private set; }
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

        public Vector3 Velocity
        {
            get
            {
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
                extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.5f, stiffness = spec.forwardStiffness
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

        void Update()
        {
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null || wheelMeshes[i] == null) continue;
                wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
                wheelSpin[i] = Mathf.Repeat(wheelSpin[i] + wheels[i].rpm * 6f * Time.deltaTime, 360f);
                wheelMeshes[i].SetPositionAndRotation(pos, rot * Quaternion.Euler(wheelSpin[i], 0f, 0f));
            }
        }

        void ReadGround()
        {
            int grounded = 0, offTrack = 0;
            float maxSlip = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = wheels[i];
                if (w.GetGroundHit(out WheelHit hit))
                {
                    grounded++;
                    hit.collider.TryGetComponent<TrackSurface>(out var surface);
                    surfaces[i] = surface;
                    surfaceGrip[i] = surface != null ? surface.FrictionMultiplier : 1f;
                    if (surface != null && surface.IsOffTrack) offTrack++;
                    float slip = Mathf.Max(Mathf.Abs(hit.forwardSlip), Mathf.Abs(hit.sidewaysSlip));
                    WheelSlip[i] = slip;
                    maxSlip = Mathf.Max(maxSlip, slip);
                }
                else
                {
                    surfaces[i] = null;
                    surfaceGrip[i] = 1f;
                    WheelSlip[i] = 0f;
                }

                bool rear = i >= RL;
                float sideFactor = rear && handbrake ? spec.handbrakeRearStiffness : 1f;
                var f = baseForward; f.stiffness = spec.forwardStiffness * surfaceGrip[i];
                var s = baseSideways; s.stiffness = spec.sidewaysStiffness * surfaceGrip[i] * sideFactor;
                w.forwardFriction = f;
                w.sidewaysFriction = s;
            }
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
                maxAngle = spec.maxSteerAngle * Mathf.Lerp(1f, 0.32f, Mathf.Pow(sf, 0.8f));
                steerIn = Mathf.Sign(steerIn) * Mathf.Pow(Mathf.Abs(steerIn), 1.15f);
                steerRate = Mathf.Lerp(120f, 85f, sf);
            }
            float target = (inputLocked ? 0f : steerIn) * maxAngle;
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
            float brake = inputLocked ? 0f : brakeInput;
            float fwd = ForwardSpeed;

            if (Gear > 0 && brake > 0.1f && throttle < 0.1f && fwd < 0.8f)
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

        void ApplyDrive()
        {
            float throttle = inputLocked ? 0f : throttleInput;
            if (Gear < 0) throttle = inputLocked ? 0f : brakeInput;
            if (pitControlled)
            {
                throttle = extThrottle;
                if (Gear < 0 && extThrottle > 0.1f) Gear = 1; // piloto automático em ré: engata a 1ª (sai da ré)
                else if (Gear < 0) throttle = 0f;
            }

            float torque = 0f;
            bool overSpeed = SpeedKmh >= spec.topSpeedKmh || engineRpm >= spec.rpmLimit;
            if (!IsShifting && !overSpeed)
            {
                torque = spec.EvaluateTorque(engineRpm) * CurrentRatio() * throttle * 0.9f;
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
                    if (tc > 0f && wheels[i].GetGroundHit(out WheelHit hit))
                    {
                        // TC: começa em slip 0.40 (não 0.30), rampa mais suave e piso 0.40 (não 0.15).
                        // Antes cortava até 85% do torque em aceleração de verdade (slip 0.3-0.6 é
                        // normal sob potência) — o carro engasgava no meio da reta ("sem potência").
                        float excess = Mathf.Abs(hit.forwardSlip) - 0.4f;
                        if (excess > 0f) wheelTorque *= Mathf.Max(0.4f, Mathf.Clamp01(1f - excess * 2.5f * tc));
                    }
                    wheels[i].motorTorque = wheelTorque;
                }
        }

        void ApplyBrakes()
        {
            float brake = inputLocked ? 1f : (Gear < 0 ? 0f : brakeInput);
            if (pitControlled) brake = Mathf.Max(brake, extBrake);
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
                float t = i <= FR ? front : rear;
                if (t > 0f && spec.abs > 0f && wheels[i].GetGroundHit(out WheelHit hit) && Mathf.Abs(hit.forwardSlip) > 0.45f)
                {
                    t *= 1f - 0.5f * spec.abs;
                }
                if (i >= RL && handbrake && !inputLocked) t = Mathf.Max(t, spec.handbrakeTorque);
                // Freio de arrasto em baixa usa o acelerador EFETIVO (piloto automático incluído):
                // antes olhava só o throttleInput do jogador — com o pit autopilot acelerando e o
                // jogador sem tocar nada, esse freio ficava ligado e o carro "só andava se acelera".
                float driveThr = pitControlled ? extThrottle : throttleInput;
                if (Gear > 0 && !inputLocked && driveThr < 0.05f && brake < 0.05f && !handbrake) t = spec.massKg * 0.2f;
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
            // Pneus gastos/duros: correção lateral proporcionalmente mais fraca (o carro "escorrega" mais).
            float grip = RaceX.Core.PitLaneSystem.TireGrip(this);
            Vector3 av = rb.angularVelocity;
            float yawMax = Mathf.Lerp(2.4f, 0.9f, Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, spec.topSpeedKmh))) / Mathf.Max(0.9f, grip);
            if (assistLevel > 0.01f && Mathf.Abs(av.y) > yawMax) rb.angularVelocity = new Vector3(av.x, Mathf.Sign(av.y) * yawMax, av.z);
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Vector3 vel = Velocity; vel.y = 0f;
            float slip = Vector3.SignedAngle(fwd, vel, Vector3.up);
            if (Mathf.Abs(slip) > 16f)
                rb.AddTorque(transform.up * (-Mathf.Sign(slip) * (Mathf.Abs(slip) - 16f) * 0.03f * spec.massKg * assistLevel * grip), ForceMode.Force);
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

        void ApplyDownforce()
        {
            Vector3 v = Velocity;
            float speedSq = v.sqrMagnitude;
            if (GroundedWheels > 0) rb.AddForce(-transform.up * spec.downforceCoefficient * speedSq, ForceMode.Force);
            UpdateSlipstream();
            float dragMul = 1f - 0.35f * Slipstream;
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
        public float DmgFront { get; private set; }
        public float DmgRear { get; private set; }
        public float DmgLeft { get; private set; }
        public float DmgRight { get; private set; }

        /// <summary>Conserta TODO o dano (parada no pit — mecânicos trabalham enquanto o carro está no box).</summary>
        public void Repair()
        {
            DamageFactor = 1f;
            DmgFront = 0f; DmgRear = 0f; DmgLeft = 0f; DmgRight = 0f;
        }

        void OnCollisionEnter(Collision collision)
        {
            float relative = collision.relativeVelocity.magnitude * 3.6f;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            if (relative > 18f)
            {
                DamageFactor = Mathf.Max(0.80f, DamageFactor - Mathf.Clamp01((relative - 18f) / 90f) * 0.10f);
                // Dano POR ZONA para o painel visual (normal do impacto em espaço local:
                // normal apontando para trás = bateu a FRENTE; para frente = bateu a TRASEIRA).
                if (collision.contactCount > 0)
                {
                    Vector3 nl = transform.InverseTransformDirection(collision.GetContact(0).normal);
                    float sev = Mathf.Clamp01((relative - 18f) / 60f);
                    if (Mathf.Abs(nl.z) >= Mathf.Abs(nl.x)) { if (nl.z < 0f) DmgFront = Mathf.Clamp01(DmgFront + sev); else DmgRear = Mathf.Clamp01(DmgRear + sev); }
                    else { if (nl.x < 0f) DmgLeft = Mathf.Clamp01(DmgLeft + sev); else DmgRight = Mathf.Clamp01(DmgRight + sev); }
                }
            }
            Collided?.Invoke(relative, point);

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
