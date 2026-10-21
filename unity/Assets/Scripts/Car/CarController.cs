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
            if (DamageFactor < 1f)
                DamageFactor = Mathf.Min(1f, DamageFactor + Time.fixedDeltaTime * 0.008f);
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
                    var surface = hit.collider.GetComponent<TrackSurface>();
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
                rb.AddForce(-Velocity * (spec.massKg * 0.25f), ForceMode.Force);
            }
        }

        void ApplySteering()
        {
            float speedFactor = Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, spec.topSpeedKmh));
            float maxAngle = spec.maxSteerAngle * Mathf.Lerp(1f, spec.steerAtTopSpeed, speedFactor);
            float target = (inputLocked ? 0f : Mathf.Clamp(steerInput, -1f, 1f)) * maxAngle;
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, target, 120f * Time.fixedDeltaTime);
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

            if (Gear > 0 && brake > 0.1f && throttle < 0.1f && fwd < 0.5f)
            {
                Gear = -1;
            }
            else if (Gear < 0 && (throttle > 0.1f || (brake < 0.1f && fwd > -0.5f)))
            {
                Gear = 1; // acelerar sai da ré imediatamente; o torque pra frente freia o recuo
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
                    float excess = Mathf.Abs(hit.forwardSlip) - 0.3f;
                    if (excess > 0f) wheelTorque *= Mathf.Max(0.15f, Mathf.Clamp01(1f - excess * 4f * tc));
                }
                wheels[i].motorTorque = wheelTorque;
            }
        }

        void ApplyBrakes()
        {
            float brake = inputLocked ? 1f : (Gear < 0 ? 0f : brakeInput);
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
                if (Gear > 0 && !inputLocked && throttleInput < 0.05f && brake < 0.05f && !handbrake) t = spec.massKg * 0.2f;
                wheels[i].brakeTorque = t;
            }
        }

        void ApplyStability()
        {
            float speed = Mathf.Abs(ForwardSpeed);
            if (speed < 5f || GroundedWheels == 0) return;
            if (assistLevel <= 0.01f) return;
            Vector3 av = rb.angularVelocity;
            float yawMax = Mathf.Lerp(2.4f, 0.9f, Mathf.Clamp01(SpeedKmh / Mathf.Max(1f, spec.topSpeedKmh)));
            if (assistLevel > 0.01f && Mathf.Abs(av.y) > yawMax) rb.angularVelocity = new Vector3(av.x, Mathf.Sign(av.y) * yawMax, av.z);
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Vector3 vel = Velocity; vel.y = 0f;
            float slip = Vector3.SignedAngle(fwd, vel, Vector3.up);
            if (Mathf.Abs(slip) > 16f)
                rb.AddTorque(transform.up * (-Mathf.Sign(slip) * (Mathf.Abs(slip) - 16f) * 0.03f * spec.massKg * assistLevel), ForceMode.Force);
        }

        void ApplyDownforce()
        {
            Vector3 v = Velocity;
            float speedSq = v.sqrMagnitude;
            if (GroundedWheels > 0) rb.AddForce(-transform.up * spec.downforceCoefficient * speedSq, ForceMode.Force);
            rb.AddForce(-v.normalized * 0.5f * 1.2f * spec.dragCoefficient * speedSq * 0.5f, ForceMode.Force);
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

        void OnCollisionEnter(Collision collision)
        {
            float relative = collision.relativeVelocity.magnitude * 3.6f;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            if (relative > 18f)
                DamageFactor = Mathf.Max(0.72f, DamageFactor - Mathf.Clamp01((relative - 18f) / 90f) * 0.14f);
            Collided?.Invoke(relative, point);
        }


    }
}
