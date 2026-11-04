using UnityEngine;

namespace RaceX.Car
{
    // Novos modelos SEMPRE no fim (o número é salvo na garagem/carreira/servidor).
    public enum CarModel
    {
        Falcon = 0, Titan = 1, Storm = 2, F1 = 3, Ferrari = 4, R8 = 5, Mustang = 6, Revuelto = 7, GtrNismo = 8, McLarenP1 = 9, Bugatti = 10, F2008 = 11, F1Studio = 12,
        F1Legacy = 13, F1Basic = 14, F1Concept26 = 15, F1Custom22 = 16, McLarenF1 = 17, FerrariF60 = 18,
        F1Sponsor = 19, Lotus77 = 20, FP04 = 21, F4British = 22, McLarenMP45 = 23,
        F1Photo22 = 24, F1CarConcept = 25, F1Y2026 = 26, FormulaC1 = 27, FormulaC2 = 28
    }
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
        /// <summary>v100: perda de atrito com a carga, µ = µ0·(Fz/Fz0)^−expoente, Fz0 = peso por roda (0 = desligado, GT).
        /// Slick de corrida: expoente ~0,20 (pesquisa 04/11, reports/Forças G e aderência F1 F2.md). Sem ela o apoio virava
        /// aderência 1:1 e toda curva acima de ~138 m de raio era de pé embaixo em qualquer velocidade.</summary>
        public float tyreLoadSensitivity;
        /// <summary>v100: teto de aceleração lateral e de freada (g) da categoria (0 = sem teto). F2: 3,9/3,5 g; F1 2026: 6,0/4,0 g.</summary>
        public float lateralCapG, brakeCapG;

        /// <summary>Fator de atrito de uma roda com carga fz (N), sendo fz0 a carga estática por roda (peso/4).</summary>
        public float TyreLoadFactor(float fz, float fz0)
        {
            if (tyreLoadSensitivity <= 0f || fz0 <= 1f) return 1f;
            return Mathf.Pow(Mathf.Max(fz / fz0, 0.25f), -tyreLoadSensitivity);
        }

        /// <summary>Aceleração máxima (m/s²) que o pneu dá na velocidade v: µ0 × fator de carga × (peso + apoio·v²) / massa,
        /// limitada pelo teto da categoria. MESMA lei que o CarController aplica nas rodas — a IA planeja por aqui.</summary>
        public float GripAccel(float mu0, float v, float mass, float aeroShare, float capG)
        {
            float m = mass > 50f ? mass : Mathf.Max(50f, massKg);
            float w = m * 9.81f;
            float fz = w + Mathf.Max(0f, downforceCoefficient) * aeroShare * v * v;
            float a = mu0 * TyreLoadFactor(fz * 0.25f, w * 0.25f) * fz / m;
            return capG > 0f ? Mathf.Min(a, capG * 9.81f) : a;
        }

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

        /// <summary>Carro de fórmula (monoposto) — única lista do jogo; menus, HUD, IA e carreira consultam aqui.</summary>
        public static bool IsFormula(CarModel m) =>
            m == CarModel.F1 || m == CarModel.F2008 || m == CarModel.F1Studio || m == CarModel.F1Legacy || m == CarModel.F1Basic
            || m == CarModel.F1Concept26 || m == CarModel.F1Custom22 || m == CarModel.McLarenF1 || m == CarModel.FerrariF60
            || m == CarModel.F1Sponsor || m == CarModel.Lotus77 || m == CarModel.FP04 || m == CarModel.F4British || m == CarModel.McLarenMP45
            || m == CarModel.F1Photo22 || m == CarModel.F1CarConcept || m == CarModel.F1Y2026 || m == CarModel.FormulaC1 || m == CarModel.FormulaC2;

        /// <summary>Grid da F1: 12 carros, um por equipe, do MAIS FRACO (índice 0) ao mais forte (11).
        /// Os mais antigos/menos potentes (Lotus 77, F2008, F60) desceram para a F2 em 29/10.</summary>
        public static readonly CarModel[] F1Lineup =
        {
            CarModel.F1Basic, CarModel.F1Legacy, CarModel.F1Sponsor, CarModel.F1Custom22,
            CarModel.F1Studio, CarModel.F1Photo22, CarModel.McLarenF1, CarModel.FP04,
            CarModel.F1Concept26, CarModel.F1Y2026, CarModel.F1, CarModel.F1CarConcept
        };

        /// <summary>Grid da F2 (12 equipes, mais fraca -> mais forte): 7 modelos, os 5 mais fortes repetem.</summary>
        public static readonly CarModel[] F2Lineup =
        {
            CarModel.F4British, CarModel.FormulaC1, CarModel.FormulaC2, CarModel.Lotus77,
            CarModel.Lotus77, CarModel.McLarenMP45, CarModel.McLarenMP45, CarModel.F2008,
            CarModel.F2008, CarModel.FerrariF60, CarModel.FerrariF60, CarModel.FerrariF60
        };

        /// <summary>Monopostos do grid da categoria F1 do Single Player.</summary>
        public static readonly CarModel[] FormulaModels = F1Lineup;

        /// <summary>Dados de cada monoposto: potência (cv), peso com piloto (kg), velocidade máxima (km/h),
        /// carga aerodinâmica relativa e o nome. Fontes na pesquisa de 29/10 (Claude outputs/PESQUISA_racex_v80.md):
        /// Lotus 77 465–500 bhp e 575 kg; McLaren MP4/5 685 ps (Honda) e 500 kg sem piloto; RB18 798 kg (Honda),
        /// ~1050 cv e 354 km/h só em agregador (não oficial); F2008/F60 605 kg (f1technical), ~750 bhp só em fórum;
        /// Tatuus F4-T421 175 bhp e 605 kg (British F4 / FIA F4). ESTIMATIVA (sem fonte): velocidades máximas,
        /// potência dos modelos genéricos (2022 = 1000 cv), KERS do F60 (+30 cv) e o peso do MP4/5 com piloto.</summary>
        struct FormulaData { public string name; public float hp, mass, top, downforce; }

        static FormulaData DataFor(CarModel m) => m switch
        {
            CarModel.F1 => new FormulaData { name = "Red Bull RB18 (2022)", hp = 1050f, mass = 798f, top = 350f, downforce = 4.1f },
            CarModel.F1Concept26 => new FormulaData { name = "F1 Concept 2026", hp = 1000f, mass = 768f, top = 345f, downforce = 3.8f },
            CarModel.FP04 => new FormulaData { name = "FP04 Protótipo", hp = 1000f, mass = 798f, top = 345f, downforce = 4.0f },
            CarModel.McLarenF1 => new FormulaData { name = "McLaren F1", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.F1Studio => new FormulaData { name = "Fórmula 1 (2022)", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.F1Custom22 => new FormulaData { name = "F1 Custom 2022", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.F1Sponsor => new FormulaData { name = "F1 Patrocínios", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.F1Legacy => new FormulaData { name = "Fórmula 1 2022 (pintura)", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.FerrariF60 => new FormulaData { name = "Ferrari F60 (2009)", hp = 780f, mass = 605f, top = 325f, downforce = 3.6f },
            CarModel.F2008 => new FormulaData { name = "Ferrari F2008", hp = 750f, mass = 605f, top = 325f, downforce = 3.8f },
            CarModel.F1Basic => new FormulaData { name = "Fórmula 1 Clássico", hp = 850f, mass = 700f, top = 320f, downforce = 3.4f },
            CarModel.McLarenMP45 => new FormulaData { name = "McLaren MP4/5 (1989)", hp = 675f, mass = 580f, top = 320f, downforce = 3.0f },
            CarModel.Lotus77 => new FormulaData { name = "Lotus 77 (1976)", hp = 480f, mass = 575f, top = 290f, downforce = 2.4f },
            CarModel.F4British => new FormulaData { name = "Tatuus F4-T421 (F4 britânico)", hp = 175f, mass = 605f, top = 235f, downforce = 1.6f },
            // 29/10: F1 2022 (regra 2022, ~1000 cv estimado, 798 kg); F1 2026 (regra 2026: ~400 kW a combustão +
            // 350 kW elétricos ≈ 1000 cv, 768 kg, menos carga — Honda/F1.com); F1 Car Concept é FICTÍCIO (conceito
            // do Blender): posto como o mais rápido e estável da F1 por pedido do usuário. C1/C2: carro desconhecido,
            // ESTIMATIVA de fórmula de acesso (~380 cv, 550 kg).
            CarModel.F1Photo22 => new FormulaData { name = "F1 2022", hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f },
            CarModel.F1Y2026 => new FormulaData { name = "F1 2026", hp = 1005f, mass = 768f, top = 345f, downforce = 3.7f },
            CarModel.F1CarConcept => new FormulaData { name = "F1 Car Concept", hp = 1060f, mass = 780f, top = 352f, downforce = 4.4f },
            CarModel.FormulaC1 => new FormulaData { name = "Fórmula C1", hp = 380f, mass = 550f, top = 270f, downforce = 2.6f },
            CarModel.FormulaC2 => new FormulaData { name = "Fórmula C2", hp = 390f, mass = 550f, top = 272f, downforce = 2.6f },
            _ => new FormulaData { name = m.ToString(), hp = 1000f, mass = 798f, top = 340f, downforce = 4.0f }
        };

        /// <summary>v99: escala do apoio dos monopostos (a ordem relativa de DataFor se mantém). 0,78 = a MÉDIA pedida pelo
        /// usuário entre o carro (100% do apoio) e a conta antiga da IA (55%). Referência real: F1 tem picos de ~6 g nas curvas
        /// mais rápidas (Silverstone 2016, Maggots 6,1 g — telemetria via The Drive) e ~2 g nas lentas; com pneu 2,3 o F1 2022
        /// do jogo (4,0 × 0,78) fica ACIMA disso em alta (escolha de jogabilidade, não medição).</summary>
        // v100 (04/11, pedido do usuário: "sempre tudo de 2026"): apoio do F1 2026. Referência 2022–25 em alta carga: CL·A ~5 ×
        // ½ρ → k ≈ 4,5 (Mercedes: apoio = peso a ~150 km/h; Symonds). Regra 2026: −30% de apoio e −55% de arrasto (FIA, via
        // ESPN/F1 Chronicle). 4,5 × 0,70 = 3,15 para o modelo de referência (downforce 4,0 em DataFor) → escala 0,7875. Este é o
        // nível de ALTA carga; a pista aplica o próprio nível (TrackLibrary.F1AeroLevel: Monza 0,67 ... Mônaco 1,0).
        const float FormulaAeroScale = 4.5f * 0.70f / 4.0f;
        /// <summary>Pneu do F1 2026: µ0 lateral 1,70 e longitudinal 1,65 (pesquisa 04/11). Pirelli 2026: 25 mm mais estreito na
        /// frente e 30 mm atrás "com perda mínima de aderência" (Autosport/The Race) — mantido o µ0 do relatório.</summary>
        public const float F1MuLat = 1.70f, F1MuLong = 1.65f, F1CapLatG = 6.0f, F1CapBrakeG = 4.0f;
        public const float F2MuLat = 1.60f, F2MuLong = 1.55f, F2CapLatG = 3.9f, F2CapBrakeG = 3.5f;
        public const float SlickLoadSensitivity = 0.20f;

        /// <summary>Física do monoposto a partir dos dados reais. O jogo foi calibrado no F1 2022 (340 Nm
        /// "de jogo" ≈ 1000 cv): a potência entra em PROPORÇÃO a ele; peso e velocidade máxima são diretos.</summary>
        void ApplyFormulaData(CarModel m)
        {
            var d = DataFor(m);
            displayName = d.name;
            // Potência COMPRIMIDA dentro da categoria (pedido do usuário, 29/10): o carro mais fraco do grid
            // fica com 85% da potência do mais forte — continua o pior, mas sem ser gritante. A ORDEM real
            // (dados de DataFor) se mantém. Fora dos grids (GT/F1 em GT) usa a potência real.
            maxTorqueNm = 340f * CompressedHp(m, d.hp) / 1000f;
            massKg = d.mass;
            topSpeedKmh = d.top;
            downforceCoefficient = d.downforce * FormulaAeroScale;
            gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
            zeroToHundredSeconds = Mathf.Clamp(2.6f * (d.mass / 798f) / (d.hp / 1000f), 2.4f, 4.5f); // estimativa (relação peso/potência)
        }

        // F2 = CARRO ÚNICO (pedido do usuário 01/11; como na F2 real): Dallara F2 2024 — 620 cv (462 kW) a 8.750 rpm e
        // 795 kg com o piloto (FIA F2, revelação do carro em Monza 31/08/2023 via Wikipedia "Dallara F2 2024"; o
        // combustível o jogo soma à parte, como faz com os 798 kg da F1). Velocidade máxima ~335 km/h em configuração de
        // pouco arrasto (fontes secundárias, sem ficha oficial). Carga aerodinâmica: não há número público. Referência: F2 real
        // faz ~3,9 g em curva e ~3,5 g freando (Pit Debrief, "Comparing F2 and F3 racing cars in 2025") — com pneu µ 1,8 isso
        // daria apoio ~2,0, mas o usuário achou o carro sem aderência; v99 usa a MÉDIA pedida por ele: 3,2 × 0,78 ≈ 2,5 (com o
        // pneu 2,3: ~2,8 g a 100 km/h e ~4,1 g a 180 km/h; raio de 44 m sai de ~178 para ~136 km/h e passa a pedir freada).
        // A diferença entre equipes fica só no ajuste de equipe (CareerMode.TeamPerformanceFor: 1,00–1,06).
        public const float F2Hp = 620f, F2MassKg = 795f, F2TopKmh = 335f, F2Downforce = 2.5f;
        public const string F2Name = "Dallara F2 2024";

        /// <summary>Carreira na F2: troca potência/peso/velocidade/carga do modelo pelo carro único da categoria (o modelo
        /// 3D de cada equipe continua o mesmo). Antes cada equipe tinha um carro de 175 a 780 cv.</summary>
        public void ApplyF2Spec()
        {
            if (!IsFormula(model)) return;
            displayName = F2Name;
            maxTorqueNm = 340f * F2Hp / 1000f;
            massKg = F2MassKg;
            topSpeedKmh = F2TopKmh;
            downforceCoefficient = F2Downforce;
            sidewaysStiffness = F2MuLat; forwardStiffness = F2MuLong;
            tyreLoadSensitivity = SlickLoadSensitivity; lateralCapG = F2CapLatG; brakeCapG = F2CapBrakeG;
            gearRatios = ComputeGearRatios(8, topSpeedKmh, rpmLimit, wheelRadius, 3.2f);
            zeroToHundredSeconds = Mathf.Clamp(2.6f * (F2MassKg / 798f) / (F2Hp / 1000f), 2.4f, 4.5f); // estimativa (peso/potência)
        }

        const float WeakestShare = 0.85f;

        static float CompressedHp(CarModel m, float hp)
        {
            var grid = System.Array.IndexOf(F1Lineup, m) >= 0 ? F1Lineup : System.Array.IndexOf(F2Lineup, m) >= 0 ? F2Lineup : null;
            if (grid == null) return hp;
            float lo = float.MaxValue, hi = 0f;
            foreach (var g in grid) { float h = DataFor(g).hp; lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            if (hi - lo < 1f) return hp;
            return hi * (WeakestShare + (1f - WeakestShare) * (hp - lo) / (hi - lo));
        }

        static string FormulaName(CarModel m) => m switch
        {
            CarModel.F1Legacy => "Fórmula 1 2022 (pintura)",
            CarModel.F1Basic => "Fórmula 1 Clássico",
            CarModel.F1Concept26 => "F1 Concept 2026",
            CarModel.F1Custom22 => "F1 Custom 2022",
            CarModel.McLarenF1 => "McLaren F1",
            CarModel.FerrariF60 => "Ferrari F60 (2009)",
            CarModel.F1Sponsor => "F1 Patrocínios",
            CarModel.Lotus77 => "Lotus 77 (1976)",
            CarModel.FP04 => "FP04 Protótipo",
            _ => m.ToString()
        };

        public static CarSpec CreateDefault(CarModel model)
        {
            var s = CreateInstance<CarSpec>();
            s.Apply(model);
            return s;
        }

        public void Apply(CarModel m)
        {
            // Monopostos novos: mesma física do F1 2022 (F1Studio); só o modelo 3D e o nome mudam.
            // Monopostos: base do F1 2022 (F1Studio) + potência/peso/velocidade de cada carro real.
            if (IsFormula(m) && m != CarModel.F1Studio)
            {
                Apply(CarModel.F1Studio);
                model = m;
                ApplyFormulaData(m);
                return;
            }
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
                    // v99 (pedido do usuário: "o carro não derrapa, faço a volta acelerando; F2 parece F1"): atrito LATERAL 2,9 →
                    // 2,3. O slick real fica em ~1,8 (Motor Sport Magazine, "Technofile: the slick tyre", 1999; 1,4–2,1 ao longo
                    // de uma corrida, fórum F1Technical), mas com 1,8 o carro "derrapa muito, ficou sem aderência" (usuário, 03/11)
                    // e a IA saía e batia sem parar no autoteste; 2,3 = a MÉDIA que o usuário pediu entre o carro (2,9) e a conta
                    // antiga da IA (1,74). Vale para TODOS os monopostos (herdam esta base).
                    // v100 (04/11): pneu realista — µ0 1,70/1,65 com perda pela carga (expoente 0,20) e tetos 6,0/4,0 g do F1 2026.
                    // Antes µ 2,3 fixo: 4,6 g a 200 km/h e toda curva rápida de pé (pesquisa: F2 real ~2,8 g a 200 km/h).
                    forwardStiffness = F1MuLong; sidewaysStiffness = F1MuLat;
                    tyreLoadSensitivity = SlickLoadSensitivity; lateralCapG = F1CapLatG; brakeCapG = F1CapBrakeG;
                    tractionControl = 0.35f; abs = 0.7f;
                    bodySize = new Vector3(1.9f, 0.8f, 5.0f);
                    wheelbase = 3.40f; trackWidth = 1.68f;
                    centerOfMassOffset = new Vector3(0f, -0.12f, -0.05f);
                    driverEyeLocal = new Vector3(0f, 0.8f, -0.3f);
                    rectangularSteeringWheel = true;
                    manualShifter = false;
                    torqueCurve = MakeCurve(new[] { 4000f, 6500f, 8500f, 10500f, 12000f, 12500f }, new[] { 0.5f, 0.7f, 0.88f, 1f, 0.95f, 0.88f });
                    if (model == CarModel.F1Studio) ApplyFormulaData(CarModel.F1Studio);
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

        /// <summary>Cores de EQUIPE: 12 cores distintas — os 2 carros da MESMA equipe compartilham a
        /// cor e nenhuma cor se repete entre equipes (carreira e pares virtuais do Single Player).</summary>
        public static readonly Color[] TeamColors =
        {
            new Color(0.85f, 0.08f, 0.08f),  // vermelho
            new Color(0.08f, 0.25f, 0.9f),   // azul
            new Color(0.06f, 0.62f, 0.2f),   // verde
            new Color(0.97f, 0.78f, 0.08f),  // amarelo
            new Color(1f, 0.45f, 0.04f),     // laranja
            new Color(0.56f, 0.08f, 0.8f),   // roxo
            new Color(0.04f, 0.68f, 0.85f),  // ciano
            new Color(0.95f, 0.14f, 0.55f),  // rosa
            new Color(0.55f, 0.88f, 0.12f),  // lima
            new Color(0.94f, 0.92f, 0.86f),  // branco
            new Color(0.6f, 0.64f, 0.7f),    // prata
            new Color(0.05f, 0.33f, 0.42f)   // azul-petróleo
        };

        // Índices >= TeamBase apontam para TeamColors (cor de equipe).
        public const int TeamBase = 100;

        public static Color Get(int index) => index >= TeamBase
            ? TeamColors[(index - TeamBase) % TeamColors.Length]
            : Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];

        public static int TeamColorOf(int teamIndex) => TeamBase + ((teamIndex % TeamColors.Length) + TeamColors.Length) % TeamColors.Length;

        // ---------- MÁXIMO 2 CARROS POR COR (pedido do usuário: Single Player, tela dividida e online) ----------
        // Conta por FAMÍLIA visual: o "Vermelho Corsa" do jogador (paleta de escolha) e o vermelho de equipe são índices
        // diferentes mas a mesma cor na pista. 13 famílias × 2 = 26 carros (o grid tem 24).
        // 0 vermelho · 1 azul · 2 branco · 3 preto · 4 amarelo · 5 verde · 6 laranja · 7 prata · 8 roxo · 9 ciano ·
        // 10 rosa · 11 lima · 12 azul-petróleo
        public const int FamilyCount = 13;
        static readonly int[] ColorsFamily = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly int[] TeamFamily = { 0, 1, 5, 4, 6, 8, 9, 10, 11, 2, 7, 12 };

        public static int FamilyOf(int index) => index >= TeamBase
            ? TeamFamily[(index - TeamBase) % TeamColors.Length]
            : ColorsFamily[((index % Colors.Length) + Colors.Length) % Colors.Length];

        /// <summary>Uma cor da família (prefere a cor de equipe; preto só existe na paleta de escolha).</summary>
        public static int ColorOfFamily(int fam)
        {
            for (int i = 0; i < TeamColors.Length; i++) if (TeamFamily[i] == fam) return TeamBase + i;
            for (int i = 0; i < Colors.Length; i++) if (ColorsFamily[i] == fam) return i;
            return 0;
        }

        /// <summary>Cor para <paramref name="need"/> carros (1 ou a dupla): a preferida se a família ainda tem lugar; senão a
        /// primeira família VAZIA; senão a primeira com lugar. famCount = carros por família já distribuídos.</summary>
        public static int FreeColor(int[] famCount, int need, int preferred)
        {
            if (famCount[FamilyOf(preferred)] + need <= 2) return preferred;
            for (int f = 0; f < FamilyCount; f++) if (famCount[f] == 0) return ColorOfFamily(f);
            for (int f = 0; f < FamilyCount; f++) if (famCount[f] + need <= 2) return ColorOfFamily(f);
            return preferred;
        }
    }
}
