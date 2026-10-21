using UnityEngine;

namespace RaceX.Car
{
    public enum CarModel { Falcon = 0, Titan = 1, Storm = 2, F1 = 3, Ferrari = 4, R8 = 5, Mustang = 6, Revuelto = 7, GtrNismo = 8, McLarenP1 = 9, Bugatti = 10, F2008 = 11, F1Studio = 12 }
    public enum Drivetrain { FWD, RWD, AWD }

    [CreateAssetMenu(fileName = "CarSpec", menuName = "RaceX/Car Spec")]
    public class CarSpec : ScriptableObject
    {
        [Header("Identidade")]
        public CarModel model;
        public string displayName;
        public Drivetrain drivetrain;
        [Range(0f, 1f)] public float awdFrontShare = 0.4f;

        [Header("Motor")]
        public float massKg;
        public float maxTorqueNm;
        public float maxTorqueRpm;
        public float idleRpm = 900f;
        public float rpmLimit;
        public AnimationCurve torqueCurve;
        public float engineInertia = 0.25f;

        [Header("Desempenho alvo")]
        public float topSpeedKmh;
        public float zeroToHundredSeconds;

        [Header("Câmbio automático")]
        public float[] gearRatios;
        public float finalDrive = 1f;
        public float reverseRatio = 12f;
        [Range(0.5f, 1f)] public float upshiftRpmPercent = 0.92f;
        [Range(0.2f, 0.8f)] public float downshiftRpmPercent = 0.45f;
        public float shiftTime = 0.15f;

        [Header("Direção")]
        public float maxSteerAngle;
        [Range(0.1f, 1f)] public float steerAtTopSpeed = 0.35f;

        [Header("Suspensão (Tabela 5)")]
        public float spring;
        public float damper;
        [Range(0f, 1f)] public float targetPosition;
        public float suspensionDistance;
        public float antiRollStiffness;

        [Header("Freios")]
        public float brakeTorque;
        [Range(0f, 1f)] public float frontBrakeBias;
        public float handbrakeTorque = 3000f;
        [Range(0f, 1f)] public float handbrakeRearStiffness = 0.6f;

        [Header("Assistências")]
        [Range(0f, 1f)] public float tractionControl = 0.5f;
        [Range(0f, 1f)] public float abs = 0.5f;

        [Header("Aerodinâmica")]
        public float downforceCoefficient;
        public float dragCoefficient = 0.35f;

        [Header("Pneus")]
        public float wheelRadius = 0.33f;
        public float wheelMass = 20f;
        public float forwardStiffness = 1.5f;
        public float sidewaysStiffness = 1.6f;

        [Header("Geometria")]
        public Vector3 bodySize = new Vector3(1.8f, 1.3f, 4.3f);
        public float wheelbase = 2.6f;
        public float trackWidth = 1.6f;
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.3f, 0f);
        public Vector3 driverEyeLocal = new Vector3(-0.35f, 0.75f, -0.1f);
        /// <summary>false = câmbio de palletes no volante (sem alavanca no console).</summary>
        public bool manualShifter = true;
        public bool rectangularSteeringWheel;

        public int GearCount => gearRatios != null ? gearRatios.Length : 0;

        public float EvaluateTorque(float rpm)
        {
            float factor = torqueCurve != null && torqueCurve.length > 0 ? torqueCurve.Evaluate(rpm) : 1f;
            return Mathf.Clamp01(factor) * maxTorqueNm;
        }

        public static float[] ComputeGearRatios(int gears, float topSpeedKmh, float rpmLimit, float wheelRadius, float spread)
        {
            float wheelRpmAtTop = (topSpeedKmh / 3.6f) / (2f * Mathf.PI * wheelRadius) * 60f;
            float topRatio = rpmLimit * 0.97f / wheelRpmAtTop;
            float[] ratios = new float[gears];
            for (int i = 0; i < gears; i++)
            {
                float t = gears == 1 ? 1f : (float)i / (gears - 1);
                ratios[i] = topRatio * Mathf.Pow(spread, 1f - t);
            }
            return ratios;
        }

        public static CarSpec CreateDefault(CarModel model)
        {
            var s = CreateInstance<CarSpec>();
            s.Apply(model);
            return s;
        }

        public void Apply(CarModel m)
        {
            model = m;
            switch (m)
            {
                case CarModel.F1:
                    displayName = "F1 (Fórmula 1)";
                    drivetrain = Drivetrain.RWD;
                    massKg = 795f; maxTorqueNm = 340f; maxTorqueRpm = 10500f; rpmLimit = 12500f;
                    topSpeedKmh = 340f; zeroToHundredSeconds = 2.6f;
                    wheelRadius = 0.34f; idleRpm = 3800f;
                    gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
                    downshiftRpmPercent = 0.60f;
                    maxSteerAngle = 19f; steerAtTopSpeed = 0.30f;
                    spring = 65000f; damper = 6500f; targetPosition = 0.45f; suspensionDistance = 0.12f; antiRollStiffness = 9000f;
                    brakeTorque = 5200f; frontBrakeBias = 0.56f; handbrakeTorque = 3000f;
                    downforceCoefficient = 4.0f; dragCoefficient = 0.45f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.35f; abs = 0.7f;
                    bodySize = new Vector3(1.9f, 0.8f, 5.0f);
                    wheelbase = 3.40f; trackWidth = 1.68f;
                    centerOfMassOffset = new Vector3(0f, -0.12f, -0.05f);
                    driverEyeLocal = new Vector3(0f, 0.8f, -0.3f);
                    rectangularSteeringWheel = true;
                    manualShifter = false;
                    torqueCurve = MakeCurve(new[] { 4000f, 6500f, 8500f, 10500f, 12000f, 12500f }, new[] { 0.5f, 0.7f, 0.88f, 1f, 0.95f, 0.88f });
                    break;
                case CarModel.Falcon:
                    displayName = "Chevrolet Corvette ZR1";
                    drivetrain = Drivetrain.RWD;
                    massKg = 1510f; maxTorqueNm = 819f; maxTorqueRpm = 3800f; rpmLimit = 7000f;
                    topSpeedKmh = 330f; zeroToHundredSeconds = 3.3f;
                    wheelRadius = 0.32f;
                    gearRatios = ComputeGearRatios(6, topSpeedKmh, rpmLimit, wheelRadius, 3.3f);
                    maxSteerAngle = 35f; steerAtTopSpeed = 0.18f;
                    spring = 35000f; damper = 4500f; targetPosition = 0.50f; suspensionDistance = 0.20f; antiRollStiffness = 5000f;
                    brakeTorque = 3800f; frontBrakeBias = 0.65f; handbrakeTorque = 2500f;
                    downforceCoefficient = 2.0f; dragCoefficient = 0.34f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.9f; abs = 0.7f;
                    bodySize = new Vector3(1.78f, 1.40f, 4.05f);
                    wheelbase = 2.50f; trackWidth = 1.52f;
                    centerOfMassOffset = new Vector3(0f, -0.35f, 0.15f);
                    driverEyeLocal = new Vector3(-0.36f, 0.62f, -0.15f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 900f, 2000f, 3200f, 4500f, 5800f, 7000f }, new[] { 0.45f, 0.7f, 0.9f, 1f, 0.92f, 0.75f });
                    break;
                case CarModel.Titan:
                    displayName = "Porsche Cayman S";
                    drivetrain = Drivetrain.RWD;
                    massKg = 1395f; maxTorqueNm = 375f; maxTorqueRpm = 4400f; rpmLimit = 7800f;
                    topSpeedKmh = 283f; zeroToHundredSeconds = 4.7f;
                    wheelRadius = 0.34f;
                    gearRatios = ComputeGearRatios(6, topSpeedKmh, rpmLimit, wheelRadius, 3.5f);
                    maxSteerAngle = 30f; steerAtTopSpeed = 0.17f;
                    spring = 50000f; damper = 5500f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 7000f;
                    brakeTorque = 3200f; frontBrakeBias = 0.60f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.2f; dragCoefficient = 0.32f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.9f; abs = 0.6f;
                    bodySize = new Vector3(1.95f, 1.22f, 4.50f);
                    wheelbase = 2.70f; trackWidth = 1.66f;
                    centerOfMassOffset = new Vector3(0f, -0.38f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.52f, -0.25f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 900f, 2500f, 4000f, 5500f, 6800f, 7800f }, new[] { 0.4f, 0.68f, 0.88f, 1f, 0.93f, 0.8f });
                    break;
                case CarModel.Ferrari:
                    displayName = "Ferrari Concept";
                    manualShifter = false;
                    drivetrain = Drivetrain.RWD;
                    massKg = 1400f; maxTorqueNm = 650f; maxTorqueRpm = 6250f; rpmLimit = 8200f;
                    topSpeedKmh = 330f; zeroToHundredSeconds = 3.0f;
                    wheelRadius = 0.35f;
                    gearRatios = ComputeGearRatios(7, topSpeedKmh, rpmLimit, wheelRadius, 3.1f);
                    maxSteerAngle = 33f; steerAtTopSpeed = 0.22f;
                    spring = 60000f; damper = 6000f; targetPosition = 0.45f; suspensionDistance = 0.16f; antiRollStiffness = 8000f;
                    brakeTorque = 4200f; frontBrakeBias = 0.60f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.4f; dragCoefficient = 0.36f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.8f; abs = 0.7f;
                    bodySize = new Vector3(1.98f, 1.15f, 4.65f);
                    wheelbase = 2.72f; trackWidth = 1.70f;
                    centerOfMassOffset = new Vector3(0f, -0.34f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.55f, -0.20f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1000f, 3000f, 4750f, 6250f, 7500f, 8200f }, new[] { 0.42f, 0.72f, 0.92f, 1f, 0.93f, 0.78f });
                    break;
                case CarModel.R8:
                    displayName = "Audi R8 V10 Plus";
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.35f;
                    massKg = 1630f; maxTorqueNm = 560f; maxTorqueRpm = 6600f; rpmLimit = 8100f;
                    topSpeedKmh = 330f; zeroToHundredSeconds = 3.2f;
                    wheelRadius = 0.34f;
                    gearRatios = ComputeGearRatios(7, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
                    maxSteerAngle = 33f; steerAtTopSpeed = 0.20f;
                    spring = 55000f; damper = 5500f; targetPosition = 0.45f; suspensionDistance = 0.16f; antiRollStiffness = 7500f;
                    brakeTorque = 4000f; frontBrakeBias = 0.62f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.0f; dragCoefficient = 0.38f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.85f; abs = 0.7f;
                    bodySize = new Vector3(1.94f, 1.24f, 4.43f);
                    wheelbase = 2.65f; trackWidth = 1.64f;
                    centerOfMassOffset = new Vector3(0f, -0.36f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.58f, -0.22f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1000f, 3000f, 5000f, 6600f, 7600f, 8100f }, new[] { 0.45f, 0.72f, 0.92f, 1f, 0.94f, 0.8f });
                    break;
                case CarModel.Mustang:
                    displayName = "Ford Mustang GT";
                    drivetrain = Drivetrain.RWD;
                    massKg = 1740f; maxTorqueNm = 570f; maxTorqueRpm = 4600f; rpmLimit = 7500f;
                    topSpeedKmh = 250f; zeroToHundredSeconds = 4.3f;
                    wheelRadius = 0.35f;
                    gearRatios = ComputeGearRatios(6, topSpeedKmh, rpmLimit, wheelRadius, 3.4f);
                    maxSteerAngle = 32f; steerAtTopSpeed = 0.18f;
                    spring = 48000f; damper = 5200f; targetPosition = 0.45f; suspensionDistance = 0.17f; antiRollStiffness = 7200f;
                    brakeTorque = 3600f; frontBrakeBias = 0.62f; handbrakeTorque = 3200f;
                    downforceCoefficient = 1.8f; dragCoefficient = 0.40f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.8f; abs = 0.7f;
                    bodySize = new Vector3(1.92f, 1.38f, 4.79f);
                    wheelbase = 2.72f; trackWidth = 1.66f;
                    centerOfMassOffset = new Vector3(0f, -0.36f, 0.05f);
                    driverEyeLocal = new Vector3(-0.38f, 0.60f, -0.18f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 900f, 2500f, 3500f, 4600f, 6200f, 7500f }, new[] { 0.45f, 0.75f, 0.93f, 1f, 0.9f, 0.72f });
                    break;
                case CarModel.Revuelto:
                    displayName = "Lamborghini Revuelto";
                    manualShifter = false;
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.30f;
                    massKg = 1775f; maxTorqueNm = 725f; maxTorqueRpm = 6750f; rpmLimit = 9250f;
                    topSpeedKmh = 350f; zeroToHundredSeconds = 2.5f;
                    wheelRadius = 0.36f;
                    gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.0f);
                    maxSteerAngle = 33f; steerAtTopSpeed = 0.24f;
                    spring = 62000f; damper = 6200f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 8500f;
                    brakeTorque = 4400f; frontBrakeBias = 0.60f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.6f; dragCoefficient = 0.36f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.75f; abs = 0.7f;
                    bodySize = new Vector3(2.04f, 1.14f, 4.81f);
                    wheelbase = 2.87f; trackWidth = 1.72f;
                    centerOfMassOffset = new Vector3(0f, -0.34f, 0f);
                    driverEyeLocal = new Vector3(-0.40f, 0.55f, -0.22f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1500f, 4000f, 5600f, 6750f, 8300f, 9250f }, new[] { 0.45f, 0.75f, 0.93f, 1f, 0.93f, 0.8f });
                    break;
                case CarModel.GtrNismo:
                    displayName = "Nissan GT-R Nismo";
                    manualShifter = false;
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.40f;
                    massKg = 1720f; maxTorqueNm = 652f; maxTorqueRpm = 4800f; rpmLimit = 7600f;
                    topSpeedKmh = 315f; zeroToHundredSeconds = 2.8f;
                    wheelRadius = 0.34f;
                    gearRatios = ComputeGearRatios(6, topSpeedKmh, rpmLimit, wheelRadius, 3.3f);
                    maxSteerAngle = 32f; steerAtTopSpeed = 0.19f;
                    spring = 58000f; damper = 5800f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 8200f;
                    brakeTorque = 4200f; frontBrakeBias = 0.61f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.6f; dragCoefficient = 0.38f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.85f; abs = 0.7f;
                    bodySize = new Vector3(1.95f, 1.29f, 4.71f);
                    wheelbase = 2.78f; trackWidth = 1.68f;
                    centerOfMassOffset = new Vector3(0f, -0.36f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.56f, -0.20f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 900f, 2500f, 3600f, 4800f, 6500f, 7600f }, new[] { 0.48f, 0.78f, 0.94f, 1f, 0.9f, 0.74f });
                    break;
                case CarModel.McLarenP1:
                    displayName = "McLaren P1";
                    manualShifter = false;
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.30f;
                    massKg = 1490f; maxTorqueNm = 900f; maxTorqueRpm = 7000f; rpmLimit = 8600f;
                    topSpeedKmh = 350f; zeroToHundredSeconds = 2.8f;
                    wheelRadius = 0.35f;
                    gearRatios = ComputeGearRatios(7, topSpeedKmh, rpmLimit, wheelRadius, 3.1f);
                    maxSteerAngle = 33f; steerAtTopSpeed = 0.23f;
                    spring = 62000f; damper = 6200f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 8600f;
                    brakeTorque = 4400f; frontBrakeBias = 0.58f; handbrakeTorque = 3000f;
                    downforceCoefficient = 3.2f; dragCoefficient = 0.36f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.75f; abs = 0.7f;
                    bodySize = new Vector3(1.95f, 1.18f, 4.53f);
                    wheelbase = 2.67f; trackWidth = 1.65f;
                    centerOfMassOffset = new Vector3(0f, -0.34f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.54f, -0.20f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1200f, 3500f, 5200f, 7000f, 8000f, 8600f }, new[] { 0.42f, 0.7f, 0.9f, 1f, 0.92f, 0.8f });
                    break;
                case CarModel.F2008:
                    displayName = "Ferrari F2008";
                    drivetrain = Drivetrain.RWD;
                    massKg = 605f; maxTorqueNm = 340f; maxTorqueRpm = 10500f; rpmLimit = 12000f;
                    topSpeedKmh = 330f; zeroToHundredSeconds = 2.6f;
                    wheelRadius = 0.34f; idleRpm = 3800f;
                    gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
                    downshiftRpmPercent = 0.60f;
                    maxSteerAngle = 19f; steerAtTopSpeed = 0.30f;
                    spring = 65000f; damper = 6500f; targetPosition = 0.45f; suspensionDistance = 0.12f; antiRollStiffness = 9000f;
                    brakeTorque = 5200f; frontBrakeBias = 0.56f; handbrakeTorque = 3000f;
                    downforceCoefficient = 4.2f; dragCoefficient = 0.46f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.35f; abs = 0.7f;
                    bodySize = new Vector3(1.9f, 0.8f, 5.1f);
                    wheelbase = 3.40f; trackWidth = 1.68f;
                    centerOfMassOffset = new Vector3(0f, -0.12f, -0.05f);
                    driverEyeLocal = new Vector3(0f, 0.8f, -0.3f);
                    rectangularSteeringWheel = true;
                    manualShifter = false;
                    torqueCurve = MakeCurve(new[] { 4000f, 6500f, 8500f, 10500f, 12000f }, new[] { 0.5f, 0.7f, 0.88f, 1f, 0.95f });
                    break;
                case CarModel.F1Studio:
                    displayName = "Fórmula 1 (2022)";
                    drivetrain = Drivetrain.RWD;
                    massKg = 795f; maxTorqueNm = 340f; maxTorqueRpm = 10500f; rpmLimit = 12500f;
                    topSpeedKmh = 340f; zeroToHundredSeconds = 2.6f;
                    wheelRadius = 0.34f; idleRpm = 3800f;
                    gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
                    downshiftRpmPercent = 0.60f;
                    maxSteerAngle = 19f; steerAtTopSpeed = 0.30f;
                    spring = 65000f; damper = 6500f; targetPosition = 0.45f; suspensionDistance = 0.12f; antiRollStiffness = 9000f;
                    brakeTorque = 5200f; frontBrakeBias = 0.56f; handbrakeTorque = 3000f;
                    downforceCoefficient = 4.0f; dragCoefficient = 0.45f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.35f; abs = 0.7f;
                    bodySize = new Vector3(1.9f, 0.8f, 5.0f);
                    wheelbase = 3.40f; trackWidth = 1.68f;
                    centerOfMassOffset = new Vector3(0f, -0.12f, -0.05f);
                    driverEyeLocal = new Vector3(0f, 0.8f, -0.3f);
                    rectangularSteeringWheel = true;
                    manualShifter = false;
                    torqueCurve = MakeCurve(new[] { 4000f, 6500f, 8500f, 10500f, 12000f, 12500f }, new[] { 0.5f, 0.7f, 0.88f, 1f, 0.95f, 0.88f });
                    break;
                case CarModel.Bugatti:
                    displayName = "Bugatti Veyron 16.4";
                    manualShifter = false;
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.32f;
                    massKg = 1880f; maxTorqueNm = 1250f; maxTorqueRpm = 2200f; rpmLimit = 6900f;
                    topSpeedKmh = 407f; zeroToHundredSeconds = 2.5f;
                    wheelRadius = 0.37f;
                    gearRatios = ComputeGearRatios(7, topSpeedKmh, rpmLimit, wheelRadius, 3.4f);
                    maxSteerAngle = 31f; steerAtTopSpeed = 0.21f;
                    spring = 64000f; damper = 6400f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 8800f;
                    brakeTorque = 4800f; frontBrakeBias = 0.60f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.8f; dragCoefficient = 0.38f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.8f; abs = 0.7f;
                    bodySize = new Vector3(2.04f, 1.12f, 4.66f);
                    wheelbase = 2.71f; trackWidth = 1.72f;
                    centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
                    driverEyeLocal = new Vector3(-0.40f, 0.54f, -0.20f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1200f, 2500f, 3500f, 5000f, 6000f, 6900f }, new[] { 0.5f, 0.85f, 1f, 0.95f, 0.9f, 0.78f });
                    break;
                default:
                    displayName = "Lamborghini Temerario";
                    manualShifter = false;
                    drivetrain = Drivetrain.AWD; awdFrontShare = 0.30f;
                    massKg = 1660f; maxTorqueNm = 800f; maxTorqueRpm = 7000f; rpmLimit = 9000f;
                    topSpeedKmh = 343f; zeroToHundredSeconds = 2.7f;
                    wheelRadius = 0.35f; idleRpm = 3000f;
                    gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.0f);
                    downshiftRpmPercent = 0.60f;
                    maxSteerAngle = 30f; steerAtTopSpeed = 0.19f;
                    spring = 62000f; damper = 6200f; targetPosition = 0.45f; suspensionDistance = 0.15f; antiRollStiffness = 8500f;
                    brakeTorque = 4400f; frontBrakeBias = 0.60f; handbrakeTorque = 3000f;
                    downforceCoefficient = 2.6f; dragCoefficient = 0.36f;
                    forwardStiffness = 1.8f; sidewaysStiffness = 2.9f;
                    tractionControl = 0.8f; abs = 0.7f;
                    bodySize = new Vector3(2.04f, 1.20f, 4.70f);
                    wheelbase = 2.85f; trackWidth = 1.70f;
                    centerOfMassOffset = new Vector3(0f, -0.34f, 0f);
                    driverEyeLocal = new Vector3(-0.38f, 0.58f, -0.22f);
                    rectangularSteeringWheel = false;
                    torqueCurve = MakeCurve(new[] { 1200f, 3500f, 5500f, 7000f, 8300f, 9000f }, new[] { 0.5f, 0.75f, 0.92f, 1f, 0.93f, 0.8f });
                    break;
            }
        }

        static AnimationCurve MakeCurve(float[] rpm, float[] factor)
        {
            var keys = new Keyframe[rpm.Length];
            for (int i = 0; i < rpm.Length; i++) keys[i] = new Keyframe(rpm[i], factor[i]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }
    }

    public static class CarPalette
    {
        public static readonly string[] Names =
        {
            "Vermelho Corsa", "Azul Royal", "Branco Pérola", "Preto Ônix",
            "Amarelo Solar", "Verde Racing", "Laranja Neon", "Prata Metálico"
        };

        public static readonly Color[] Colors =
        {
            new Color(0.78f, 0.05f, 0.05f), new Color(0.07f, 0.2f, 0.75f), new Color(0.95f, 0.94f, 0.9f), new Color(0.04f, 0.04f, 0.05f),
            new Color(1f, 0.8f, 0.05f), new Color(0.0f, 0.38f, 0.18f), new Color(1f, 0.42f, 0.0f), new Color(0.72f, 0.74f, 0.76f)
        };

        public static Color Get(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
    }
}
