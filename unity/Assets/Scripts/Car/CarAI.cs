using System.Collections.Generic;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// MEMÓRIA DE PISTA da IA (v80): teto de velocidade APRENDIDO por trecho, por modelo de carro e
    /// pista. Quando um carro sai da pista ou derrapa forte SEM ter batido em ninguém, o trecho (e os
    /// dois anteriores, onde devia ter freado) ganha um teto 7% abaixo da velocidade do erro; passagens
    /// limpas no teto sobem o limite devagar (0,15%). A IA encontra o limite REAL da física do jogo em
    /// vez de confiar só numa fórmula. Persiste em PlayerPrefs (vale para as próximas sessões).
    /// </summary>
    public static class AiTrackMemory
    {
        public const int Segments = 160;
        static readonly Dictionary<int, float[]> caps = new Dictionary<int, float[]>();
        static readonly HashSet<int> dirty = new HashSet<int>();
        /// <summary>Saídas de pista/derrapagens sem contato registradas nesta execução (telemetria).</summary>
        public static int Incidents { get; private set; }

        static int Key(CarModel m, int track) => (int)m * 1000 + track;

        /// <summary>Grupo da memória: carros com a MESMA física aprendem juntos (v99, pedido do usuário: "analisarem onde erraram
        /// para parar de errar ali"). Na F2 (carro único, CarSpec.ApplyF2Spec) os 12 modelos 3D aprendiam cada um sozinho; agora o
        /// erro de um ensina todos. Fora disso, um grupo por modelo (física diferente).</summary>
        public static CarModel MemoryModel(CarSpec s) => s != null && s.displayName == CarSpec.F2Name ? (CarModel)999 : (s != null ? s.model : CarModel.Falcon);
        // AiCap2 (v80): modelos reconvertidos (RB18 tinha colisor gigante e errava muito) — descarta os tetos velhos.
        // AiCap3 (v82): os tetos gravados aprendiam também com chuva, pneu furado, carro batido e pneu frio (e com as horas
        // de autoteste com batidas forçadas) — ficaram baixos no seco. Descarta e recomeça com o filtro novo.
        // AiCap4 (v99): o limite de curva passou a ser a física REAL do carro (antes uma fórmula ~40% abaixo nas curvas
        // fechadas) — tetos aprendidos no regime antigo seguravam a IA na velocidade velha.
        static string PrefKey(int key) => "RaceX.AiCap5." + key;   // v100: pneu novo — tetos aprendidos com o pneu antigo descartados

        /// <summary>Piso do teto aprendido: nunca abaixo de 80% do limite físico do ponto (valor de projeto). A memória
        /// corrige o excesso da fórmula, mas um teto que só desce (sobe 0,15% por volta limpa) não pode prender a IA para sempre.</summary>
        public const float MinShareOfPhysics = 0.8f;
        /// <summary>Piso onde a fórmula não limita (vPhys = velocidade final): 40% (v99, 03/11).</summary>
        public const float MinShareUnlimited = 0.4f;

        static bool Persistent => !RaceX.Core.AutoTest.Active || RaceX.Core.AutoTest.GetIntPublic("keepmemory", 0) == 1;

        static float[] Get(CarModel m, int track)
        {
            int key = Key(m, track);
            if (caps.TryGetValue(key, out var arr)) return arr;
            arr = new float[Segments];
            try
            {
                // v100 (04/11): no AUTOTESTE a memória vive só na RAM — cada teste começa do zero e os erros das baterias de teste
                // (batidas forçadas, versões descartadas) não ensinam a IA do jogo do usuário (mesmo PlayerPrefs). keepmemory=1 mantém.
                string raw = Persistent ? PlayerPrefs.GetString(PrefKey(key), "") : "";
                if (!string.IsNullOrEmpty(raw))
                {
                    var parts = raw.Split(';');
                    for (int i = 0; i < parts.Length && i < Segments; i++)
                        float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
                }
            }
            catch (System.Exception) { }
            caps[key] = arr;
            return arr;
        }

        public static int Seg(float along, float length)
        {
            if (length <= 1f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(along, length) / length * Segments), 0, Segments - 1);
        }

        /// <summary>Teto aprendido (m/s) no ponto; float.MaxValue = sem teto.</summary>
        public static float Cap(CarModel m, int track, float along, float length)
        {
            if (track < 0) return float.MaxValue;
            float c = Get(m, track)[Seg(along, length)];
            return c > 1f ? c : float.MaxValue;
        }

        public static void Incident(CarModel m, int track, float along, float length, float speed)
        {
            if (track < 0) return;
            var arr = Get(m, track);
            int s = Seg(along, length);
            float cap = Mathf.Max(12f, speed * 0.93f);
            // Alcance para trás pela VELOCIDADE do erro (v99): a 45 m/s o carro devia ter freado ~60 m antes — 2 trechos (22 m)
            // não chegavam na curva de onde ele saiu largo (bico da mureta, 03/11). 2 a 8 trechos, teto subindo 3,5% por trecho.
            int back = Mathf.Clamp(Mathf.RoundToInt(speed / 8f), 2, 8);
            for (int o = -back; o <= 0; o++)
            {
                int i = (s + o + Segments) % Segments;
                float c = cap * (1f + 0.035f * -o);
                if (arr[i] <= 1f || c < arr[i]) arr[i] = c;
            }
            dirty.Add(Key(m, track));
            Incidents++;
        }

        public static void Clean(CarModel m, int track, float along, float length, float speed)
        {
            if (track < 0) return;
            var arr = Get(m, track);
            int s = Seg(along, length);
            if (arr[s] > 1f && speed >= arr[s] * 0.97f)
            {
                arr[s] = Mathf.Min(arr[s] * 1.0015f, 120f);
                dirty.Add(Key(m, track));
            }
        }

        public static void Flush()
        {
            if (dirty.Count == 0) return;
            try
            {
                var sb = new System.Text.StringBuilder(Segments * 6);
                foreach (int key in dirty)
                {
                    if (!caps.TryGetValue(key, out var arr)) continue;
                    sb.Length = 0;
                    for (int i = 0; i < Segments; i++)
                    {
                        if (i > 0) sb.Append(';');
                        sb.Append(arr[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (Persistent) PlayerPrefs.SetString(PrefKey(key), sb.ToString());
                }
                dirty.Clear();
                if (Persistent) PlayerPrefs.Save();
            }
            catch (System.Exception) { }
        }
    }

    /// <summary>
    /// IA de corrida v80. Personalidade FIXA por piloto (ritmo, agressividade, consistência, defesa,
    /// cuidado com pneu). Traçado fora → dentro (ápice) → fora; direção por "pure pursuit" usando o
    /// ângulo de esterço REALMENTE disponível na velocidade; limite de curva físico limitado + memória
    /// de pista; freada planejada com margem (antes planejava 15 m/s² e chegava rápido demais).
    /// Tráfego: segue com distância segura (sem freada seca), ultrapassa pelo lado livre (preferindo o
    /// lado de dentro da próxima curva) mantendo o compromisso até completar, respeita o carro ao lado
    /// (espaço de um carro), defende uma vez por curva e NUNCA mira fora do asfalto.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarAI : MonoBehaviour
    {
        public const float StuckSeconds = 2.5f;
        public const float ReverseSeconds = 1.5f;
        public const float MaxReverseSeconds = 2.5f;
        const int MaxRecoveryAttempts = 2;
        public const float RubberBandDistance = 260f;
        public const float RubberBandAmount = 0.02f;

        static readonly float[] SpeedPercent = { 0.72f, 0.88f, 1.00f, 1.06f };
        static readonly float[] LineDeviation = { 1.2f, 0.6f, 0.22f, 0.05f };
        /// <summary>Fração do limite físico usada nas curvas por dificuldade (Fácil 80% … Expert 98%).</summary>
        static readonly float[] DiffVMaxFraction = { 0.80f, 0.88f, 0.94f, 0.98f };
        /// <summary>v99: teto de uso do limite físico REAL nas curvas (sem volta do jogador no trecho). Com a física de verdade
        /// (antes a conta da IA era ~40% abaixo do carro), habilidade > 1 punha quase todo Expert a 100% do limite exato — e o
        /// controle da IA (perseguição pura a 10 Hz) erra alguns % no traçado: no 1º teste (03/11) 13 saídas e 6 perdas totais
        /// em 6,5 min. Valor de projeto, conferido no autoteste.</summary>
        // 03/11, tarde: 0,96 → 0,92 — com 0,96 ainda 1–3 perdas totais por treino saindo por FORA das curvas longas à direita (até o
        // piloto automático, mergulhando por dentro a 137 km/h); medir o tempo de volta antes de subir de novo.
        const float AiMaxUsable = 0.92f;
        /// <summary>v99: o limite pela curvatura do TRAÇADO IDEAL fica no máximo 8% acima do limite pela curvatura do centro (apertada
        /// pela lateral). O controle (perseguição a 10 Hz, lateral limitada) não segue a linha ao centímetro: só com a linha a IA
        /// tratava a última curva do Circuito Verde como reta e saía por fora (03/11: todas as 8 perdas totais em curva foram por
        /// FORA de curvas à direita). Valor de projeto.</summary>
        const float LineGain = 1.0f;   // 03/11: 1,08 → 1,0 (a linha nunca libera mais que o limite do centro apertado pela lateral)
        float lastMyLat;

        /// <summary>Ajuste fino por corrida (calibração ao vivo usa para busca binária).</summary>
        public static float FractionOverride = 1f;
        /// <summary>Respawns feitos pelo cão de guarda nesta execução (telemetria do teste automático).</summary>
        public static int RespawnCount { get; private set; }

        static readonly float[] CalRadii = { 30f, 60f, 120f, 200f };

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
                if (sqR <= b) return Mathf.Lerp(table[mi][i], table[mi][i + 1], (sqR - a) / (b - a));
            }
            return table[mi][CalRadii.Length - 1];
        }

        public CarController car;
        public WaypointSystem waypoints;
        public RaceManager race;
        public Difficulty difficulty = Difficulty.Medium;

        public string DebugState { get; private set; }

        // ---- Personalidade do piloto (fixa por nome) ----
        public float Aggression { get; private set; } = 0.5f;
        public float Consistency { get; private set; } = 0.75f;
        public float Defending { get; private set; } = 0.5f;
        public float TyreManagement { get; private set; } = 0.5f;
        /// <summary>Fator de aderência na chuva pelo atributo "Chuva" do piloto (1 = neutro).</summary>
        public float WetSkill { get; private set; } = 1f;
        /// <summary>v100: fator sobre a velocidade limpa do jogador em cada trecho (Expert 1,01–1,10; Difícil 1,00–1,05).</summary>
        public float playerPaceMul = 1f;

        /// <summary>Perfil vindo de fora (carreira: atributos do MUNDO VIVO). Sem provedor = perfil fixo pelo nome.</summary>
        public delegate bool AiProfileFn(string driverName, out float skill, out float aggression, out float consistency, out float tyre, out float wet);
        public static AiProfileFn ProfileProvider;

        int hint = -1;
        float noiseSeed;
        float lane;
        float skill;
        float unlockTime = -999f;
        float lateralAtUnlock;
        bool lateralSeeded;          // 1º pensamento: lateral parte de onde o carro está (piloto automático ligado em movimento)
        bool blueHold; float blueLat; // bandeira azul: abriu na reta e segura a faixa na curva
        float playerLineEdge = 4.75f; // limite da lateral quando segue a linha do jogador (até 0,6 m da borda)
        bool wasLocked = true;
        float committedLateral;

        public void SetLane(float value) => lane = Mathf.Clamp(value, -1.7f, 1.7f);
        float stuckTimer;
        float reverseTimer;
        int recoveryAttempts;
        float escapeTimer;
        float diagLogT = -99f;
        float escapeSide;
        float slideHold;

        // Cão de guarda por progresso.
        float progressRef = float.NaN;
        float progressTimer;
        float wrongWayTimer;
        int respawnFails;

        // Tráfego.
        CarController aheadCar;
        float aheadGap = float.MaxValue, aheadLat, aheadSpeed;
        float leftLimit, rightLimit; // limites laterais impostos por carros AO LADO
        CarController behindCar;
        float behindGap = float.MaxValue, behindSpeed;
        CarController passCar;
        float passSide, passTimer, passSwitchCooldown;
        float followTimer; // tempo preso atrás do mesmo carro (pressão para tentar a ultrapassagem)
        CarController followCar;
        bool passFound;
        float passDz, passLat;
        bool defendUsed;
        float defendLat;
        float mergeTimer;

        // Aprendizado.
        bool wasOffTrack;
        float lastContactTime = -99f;
        float cleanSampleTimer;

        const float ThinkInterval = 0.1f;
        float thinkTimer = -1f;
        float thinkDt = ThinkInterval;
        float lastThinkTime;
        float cachedSteer, cachedThrottle, cachedBrake;

        // ESTILO DO JOGADOR (v99, PlayerStyle — Difícil/Expert): perfil da MESMA condição (pista, tipo de carro, pneu desta IA,
        // seco/molhado) — traçado, freada, onde ele só tira o pé e o ritmo do volante. null = sem volta limpa dele nessa condição.
        PlayerStyle.Profile style;
        float styleGrip = 1f, styleBrakeRatio = 1f, steerSlew;
        int styleComp = -99, styleVer = -1;
        bool styleWet;

        // MELHOR VOLTA DO PRÓPRIO PILOTO (v99, pedido do usuário: "pegarem a melhor volta, o melhor setor deles mesmos"): velocidade
        // por trecho da melhor volta LIMPA dele nesta pista e condição (enquanto o jogo está aberto: treino → classificação → corrida).
        // Onde ele já passou limpo mais rápido, pode repetir (até 98,5% do limite físico); onde errou, a memória de pista
        // (AiTrackMemory) segura — ela vale por cima disto.
        static readonly Dictionary<string, float[]> ownBest = new Dictionary<string, float[]>();
        static readonly Dictionary<string, float> ownBestTime = new Dictionary<string, float>();
        readonly float[] ownLap = new float[PlayerPaceLearner.Segments];
        int ownPrevSeg = -1;
        bool ownLapDirty = true;
        float[] ownRef;

        string OwnKey()
        {
            var e = race != null ? race.GetEntry(car) : null;
            bool wet = WeatherSystem.Wetness >= PlayerStyle.WetFrom || TyreModel.IsWetTyre(PitLaneSystem.CompoundOf(car));
            return (e != null ? e.name : name) + "|" + TrackId + "|" + (int)AiTrackMemory.MemoryModel(car.spec) + "|" + (wet ? 1 : 0);
        }

        void OnOwnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e == null || e.car != car) return;
            // v104: tempo de volta de CADA carro da IA no log (pedido do usuário: "diferença discrepante de habilidade" — na corrida
            // de 04/11 ele deu 3 voltas no 2º colocado e não havia como medir o ritmo das IAs: só a telemetria dele era gravada).
            if (!e.isHuman && lapTime > 10f && !AutoTest.Active)
                Debug.Log("[VOLTA-IA] " + e.name + " " + lapTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "s suja=" + (ownLapDirty ? 1 : 0)
                          + " pneu=" + TyreModel.Letter(PitLaneSystem.CompoundOf(car)) + " agua=" + WeatherSystem.Wetness.ToString("0.00") + " bandeira=" + RaceControl.FlagOf(car));
            int filled = 0;
            foreach (float v in ownLap) if (v > 5f) filled++;
            if (!ownLapDirty && filled >= PlayerPaceLearner.Segments * 0.85f && lapTime > 10f)
            {
                string key = OwnKey();
                if (!ownBestTime.TryGetValue(key, out float bt) || lapTime < bt)
                {
                    ownBest[key] = (float[])ownLap.Clone();
                    ownBestTime[key] = lapTime;
                }
            }
            System.Array.Clear(ownLap, 0, ownLap.Length); ownPrevSeg = -1;
            ownLapDirty = false;
            ownRef = ownBest.TryGetValue(OwnKey(), out var r) ? r : null;
        }

        void RefreshStyle(int d)
        {
            if (d < 2 || paceCap != null || isHumanCar || TrackId < 0 || car.spec == null) { style = null; steerSlew = 0f; return; }
            int comp = PitLaneSystem.CompoundOf(car);
            bool wet = WeatherSystem.Wetness >= PlayerStyle.WetFrom;
            if (comp == styleComp && wet == styleWet && styleVer == PlayerStyle.Version) return;
            styleComp = comp; styleWet = wet; styleVer = PlayerStyle.Version;
            style = PlayerStyle.ForAI(TrackId, CarSpec.IsFormula(car.spec.model), comp, wet, out styleGrip);
            styleBrakeRatio = PlayerStyle.BrakeRatio(style, car.spec, car.Body != null ? car.Body.mass : car.spec.massKg);
            steerSlew = PlayerStyle.SteerSlew(style);
        }

        /// <summary>Velocidade limpa do jogador no ponto (m/s; 0 = sem dado): do estilo da mesma condição; sem ele, o perfil antigo
        /// (só velocidade, sem pneu/clima) vale apenas no seco e de slick — perfil de pista seca nunca vale na chuva.</summary>
        float PlayerSpeedAt(float at, float L)
        {
            if (style != null) { float v = style.v[PlayerStyle.Seg(at, L)]; return v > 5f ? v * styleGrip : 0f; }
            if (WeatherSystem.Wetness >= PlayerStyle.WetFrom || TyreModel.IsWetTyre(PitLaneSystem.CompoundOf(car))) return 0f;
            return PlayerPaceLearner.SampleSpeed(TrackId, at, L);
        }

        /// <summary>Freada planejada para a curva no ponto: onde o jogador freou mais forte que o plano padrão, a IA freia como ele
        /// (desaceleração MEDIDA dele, ajustada se o carro dela freia menos) — o ponto de freada sai disso.</summary>
        float StyleDecel(float at, float L, float baseDecel)
        {
            if (style == null) return baseDecel;
            float pd = PlayerStyle.BrakeZoneDecel(style, at, L) * styleBrakeRatio;
            return pd > baseDecel ? pd : baseDecel;
        }

        int practiceBaseLap = -1;

        /// <summary>Saiu do box para a pista: recomeça a contagem do treino e entra em modo MERGE (mantém a
        /// lateral atual e só depois procura o traçado — antes cruzava a pista na hora e era atingido).</summary>
        public void OnPracticeRelease()
        {
            practiceBaseLap = -1;
            mergeTimer = 4f;
            if (waypoints != null && waypoints.Spline != null)
            {
                float al = waypoints.DistanceAlong(transform.position, ref hint);
                var p = waypoints.Spline.PoseAtDistance(al);
                committedLateral = Vector3.Dot(transform.position - p.position, p.rotation * Vector3.right);
            }
            progressRef = float.NaN;
            passCar = null; passSide = 0f; passTimer = 0f;
        }

        public void Initialize(WaypointSystem wp, RaceManager raceManager, Difficulty diff, float skillBias = 0f)
        {
            car = GetComponent<CarController>();
            waypoints = wp;
            race = raceManager;
            difficulty = diff;
            noiseSeed = Random.value * 100f;
            skill = Random.Range(0.90f, 1.06f);
            var entry = raceManager != null ? raceManager.GetEntry(car) : null;
            int h;
            if (entry != null && !string.IsNullOrEmpty(entry.name))
            {
                h = 0;
                foreach (char ch in entry.name) h = (h * 31 + ch) & 0x7fffffff;
                // Piloto automático no carro do JOGADOR (autoteste): ritmo cheio, sem sorteio 0,90–1,06.
                if (entry.isHuman) skill = 1f;
                else
                {
                    // Carreira: ritmo fixo por piloto (identidade consistente na temporada).
                    float minSk = diff == Difficulty.Expert ? 0.99f : diff == Difficulty.Hard ? 0.95f : 0.90f;
                    float spanSk = diff == Difficulty.Expert ? 0.09f : 0.13f;
                    skill = minSk + (h % 100) / 100f * spanSk;
                }
            }
            else h = Random.Range(1, int.MaxValue);
            isHumanCar = entry != null && entry.isHuman;
            watchdogPhase = (h % 31) / 10f;   // cão de guarda defasado por carro (0–3 s)
            // Perfil FIXO por piloto: mesmo nome = mesmo estilo a temporada inteira.
            Aggression = 0.25f + ((h >> 3) % 100) / 100f * 0.70f;
            Consistency = 0.55f + ((h >> 7) % 100) / 100f * 0.45f;
            Defending = 0.30f + ((h >> 11) % 100) / 100f * 0.60f;
            TyreManagement = 0.20f + ((h >> 13) % 100) / 100f * 0.70f;
            // Carreira (mundo vivo): ritmo, agressividade, consistência, pneus e chuva saem dos ATRIBUTOS do piloto
            // (que evoluem, envelhecem e mudam com forma/moral) — antes era sorteio fixo pelo nome.
            if (entry != null && !entry.isHuman && ProfileProvider != null
                && ProfileProvider(entry.name, out float pSkill, out float pAgg, out float pCons, out float pTyre, out float pWet))
            {
                float diffShift = diff == Difficulty.Expert ? 0.03f : diff == Difficulty.Hard ? 0.0f : diff == Difficulty.Easy ? -0.06f : -0.03f;
                skill = pSkill + diffShift;
                Aggression = pAgg;
                Consistency = pCons;
                TyreManagement = pTyre;
                WetSkill = pWet;
            }
            // Carreira: vantagem por força da equipe (carros/pilotos melhores andam mais).
            skill = Mathf.Clamp(skill + skillBias, diff == Difficulty.Expert ? 0.98f : 0.85f, 1.16f);
            // v100 (pedido do usuário, 04/11): ritmo sobre a VOLTA LIMPA do jogador, trecho a trecho. Expert: o pior 1,01× e o
            // melhor 1,10× a velocidade dele; Difícil: o pior 1,00× (teto 1,05×, escolha de projeto). Ordem = nível do piloto
            // (carreira: atributos + equipe; fora dela: fixo pelo nome). Nunca acima do limite físico do carro (vPhysFull).
            float rank = entry != null && !entry.isHuman && ProfileProvider != null && RaceX.Career.CareerData.raceActive
                ? Mathf.InverseLerp(0.95f, 1.12f, skill)
                : (h % 100) / 100f;
            playerPaceMul = diff == Difficulty.Expert ? Mathf.Lerp(1.01f, 1.10f, rank)
                          : diff == Difficulty.Hard ? Mathf.Lerp(1.00f, 1.05f, rank) : 0.98f;
            if (AutoTest.Active && entry != null && !entry.isHuman) AutoTest.Log("RITMO-ALVO " + entry.name + " mult=" + playerPaceMul.ToString("0.000"));
            if (car != null) { car.Collided -= OnCollided; car.Collided += OnCollided; car.Ai = this; }
            if (race != null) { race.LapCompleted -= OnOwnLap; race.LapCompleted += OnOwnLap; }
            ownLapDirty = true;
            ownRef = car != null && car.spec != null && ownBest.TryGetValue(OwnKey(), out var r0) ? r0 : null;
        }

        void OnDestroy()
        {
            if (race != null) race.LapCompleted -= OnOwnLap;
            if (car != null) { car.Collided -= OnCollided; if (car.Ai == this) car.Ai = null; }
            AiTrackMemory.Flush();
        }

        void OnCollided(float relative, Vector3 point)
        {
            if (relative > 3f) lastContactTime = Time.time;
            if (relative > 25f) lastHardHitTime = Time.time;
        }

        int TrackId => race != null && race.Settings != null ? (int)race.Settings.track : -1;

        float RoadHalf()
        {
            var def = race != null && race.Track != null ? race.Track.Definition : null;
            return def != null ? def.roadWidth * 0.5f : 6f;
        }

        /// <summary>Borda do TRAÇADO IDEAL (RacingLine): centro do carro a ≥ 2 m da borda da pista (lateral do carro a ~1 m dela).
        /// Com a borda de mira normal (meia pista − 1,25 m) a linha encostava o carro a 25 cm do limite e o erro do controle jogava o
        /// carro para fora — no autoteste de 03/11 bateu no bico da mureta do pit a ~120 km/h saindo da última curva.</summary>
        float LineEdge() => Mathf.Max(1.5f, RoadHalf() - 2.0f);

        void FixedUpdate()
        {
            if (car == null || waypoints == null || car.spec == null) return;
            if (car.inputLocked)
            {
                car.throttleInput = 0f;
                car.brakeInput = 1f;
                return;
            }
            // OTIMIZAÇÃO (mobile): IA pensa a 10 Hz escalonada por carro; entre pensadas reutiliza os pedais.
            if (thinkTimer <= 0f)
            {
                float now = Time.time;
                thinkDt = Mathf.Clamp(now - lastThinkTime, 0.02f, 0.3f);
                lastThinkTime = now;
                thinkTimer = ThinkInterval;
                Think();
            }
            else thinkTimer -= Time.fixedDeltaTime;
            // Volante no ritmo do JOGADOR (estilo): quem vira de uma vez → IA vira rápido; quem vai dando toques suaves → IA mais
            // suave. Sem estilo, o comando entra direto (o CarController já limita a velocidade da roda).
            car.steerInput = steerSlew > 0f ? Mathf.MoveTowards(car.steerInput, cachedSteer, steerSlew * Time.fixedDeltaTime) : cachedSteer;
            car.throttleInput = cachedThrottle;
            car.brakeInput = cachedBrake;
            car.handbrake = false;
            if (UseFrontSlipControl && !car.pitControlled && reverseTimer <= 0f) LimitFrontSlip();   // v100c–e medido pior: desligado
            if (!car.pitControlled && reverseTimer <= 0f) StabilizeSlide();
            if (!car.pitControlled && reverseTimer <= 0f && slideHold <= 0f) ControlUndersteer();
        }

        float lastSteerOut;
        /// <summary>v100c–e: limitador de esterço e reação à saída de frente MEDIDOS PIORES (treino F2 12 min: 5–8 PT e 240–330 batidas
        /// contra 1 PT/64 no v100 — segurando o volante o carro não fechava a saída da última curva). Desligados; o registro continua.</summary>
        const bool UseFrontSlipControl = false;

        /// <summary>v100d (medido 04/11: 20 saídas de frente em 4 curvas lentas, dianteira escorregando ~1,7× a traseira com 8–14° de
        /// volante; 5 de 8 perdas totais no mesmo ponto): o piloto SENTE o volante ficar leve quando o pneu da frente passa do pico
        /// e não vira mais — virar mais só arrasta o pneu e o carro vai reto. Com o pneu dianteiro no pico, o volante não aumenta e
        /// o acelerador alivia; passou do pico, devolve um pouco. Mesmo carro do jogador: é pilotagem, não aderência extra.</summary>
        void LimitFrontSlip()
        {
            if (car.ForwardSpeed < 8f || car.GroundedWheels < 3) { lastSteerOut = car.steerInput; return; }
            float pk = Mathf.Max(0.05f, car.SidePeakSlip);
            bool frontLimited = car.FrontSideSlip > pk * 0.97f && car.FrontSideSlip > car.RearSideSlip * 1.1f;
            if (frontLimited)
            {
                float cmd = car.steerInput;
                bool sameSide = Mathf.Sign(cmd) == Mathf.Sign(lastSteerOut) && Mathf.Abs(lastSteerOut) > 0.02f;
                if (sameSide && Mathf.Abs(cmd) > Mathf.Abs(lastSteerOut)) cmd = lastSteerOut;          // não vira mais
                if (car.FrontSideSlip > pk * 1.05f && sameSide) cmd = lastSteerOut * 0.97f;              // passou do pico: devolve
                car.steerInput = cmd;
                car.throttleInput = Mathf.Min(car.throttleInput, 0.5f);
            }
            lastSteerOut = car.steerInput;
        }

        float understeerT;
        float lastThinkAlong;
        bool understeerLearned;
        /// <summary>Saídas de frente (v100) — autoteste.</summary>
        public static int UndersteerEvents;

        /// <summary>v100 (pedido do usuário: "várias andam de frente, sem curvar, e batem de bico"): SAÍDA DE FRENTE. O volante
        /// pede uma rotação (v·tan δ / entre-eixos) e o carro gira bem menos, com a dianteira além do pico de escorregamento: o
        /// pneu da frente saturou e o carro vai reto. O piloto tira o pé, freia de leve (a carga vai para a frente e o pneu volta
        /// a morder) e desvira um pouco até o pico — virar mais só arrasta o pneu. Persistindo, o trecho entra na memória de
        /// erros (chega mais devagar na próxima volta). Antes só existia o controle da traseira (StabilizeSlide).</summary>
        void ControlUndersteer()
        {
            var rb = car.Body;
            if (rb == null || car.GroundedWheels < 3) { understeerT = 0f; return; }
            float spd = car.ForwardSpeed;
            float delta = Mathf.Abs(car.SteerAngle) * Mathf.Deg2Rad;
            float peak = Mathf.Max(0.05f, car.SidePeakSlip);
            bool pushing = false;
            if (spd > 12f && delta > 1.5f * Mathf.Deg2Rad)
            {
                float wb = Mathf.Max(1.5f, car.spec.wheelbase);
                float yawWanted = spd * Mathf.Tan(delta) / wb;
                float yaw = Mathf.Abs(rb.angularVelocity.y);
                pushing = yaw < yawWanted * 0.72f && car.FrontSideSlip > peak * 0.95f && car.FrontSideSlip > car.RearSideSlip * 1.15f;
            }
            if (!pushing) { understeerT = Mathf.Max(0f, understeerT - Time.fixedDeltaTime * 2f); if (understeerT <= 0f) understeerLearned = false; return; }
            understeerT += Time.fixedDeltaTime;
            if (understeerT < 0.08f) return;
            // Só tira o pé e desvira até o pico — sem frear: a freada no meio da curva (v100c) fez quem vinha atrás acertar a
            // traseira (2 perdas totais a 118–124 km/h).
            if (UseFrontSlipControl) car.throttleInput = 0f;
            if (UseFrontSlipControl && car.FrontSideSlip > peak * 1.1f) car.steerInput *= 0.9f;
            if (!understeerLearned && understeerT > 0.35f)
            {
                understeerLearned = true;
                UndersteerEvents++;
                var sp = waypoints != null ? waypoints.Spline : null;
                if (sp != null && TrackId >= 0)
                {
                    float L = sp.Length, at = lastThinkAlong;
                    if (UseFrontSlipControl) AiTrackMemory.Incident(AiTrackMemory.MemoryModel(car.spec), TrackId, at, L, spd);
                    if (RaceX.Core.AutoTest.Active)
                        RaceX.Core.AutoTest.Log("SAIDA DE FRENTE " + name + " along=" + at.ToString("0") + " v=" + (spd * 3.6f).ToString("0") + "km/h esterco="
                            + car.SteerAngle.ToString("0.0") + " fslip=" + car.FrontSideSlip.ToString("0.00") + " rslip=" + car.RearSideSlip.ToString("0.00")
                            + " pico=" + peak.ToString("0.00"));
                }
            }
        }

        // Derrapou/rodou: contra-esterça na direção do movimento até alinhar (a 100 Hz).
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
            float maxAngle = Mathf.Max(3f, EffectiveMaxSteer());
            car.steerInput = Mathf.Clamp((beta - yawDeg * 0.08f) / maxAngle, -1f, 1f);
            // Um fio de acelerador estabiliza a traseira em derrapagem leve; forte = solta tudo.
            car.throttleInput = Mathf.Abs(beta) < 16f ? 0.15f : 0f;
            car.brakeInput = Mathf.Abs(beta) > 25f ? 0.45f : 0f;
        }

        /// <summary>Ângulo de esterço REALMENTE disponível na velocidade atual (mesma regra do
        /// CarController.ApplySteering — o volante retangular da F2 cai para 23% a 180 km/h).</summary>
        float EffectiveMaxSteer() => MaxSteerAt(car.spec, car.SpeedKmh);

        void UpdatePracticeCycle()
        {
            int lap = race.LapOf(car);
            if (practiceBaseLap < 0) { practiceBaseLap = lap; return; }
            int runLaps = Mathf.Max(1, RaceX.Core.PitLaneSystem.PracticeRunLaps(car));
            if (lap - practiceBaseLap >= runLaps)
            {
                practiceBaseLap = int.MinValue / 2; // não pede de novo até a próxima liberação
                RaceX.Core.PitLaneSystem.RequestPitOn(car);   // sem alternar: não cancela um box já pedido (dano)
            }
        }

        void Think()
        {
            if (car.wrecked) { cachedSteer = 0f; cachedThrottle = 0f; cachedBrake = 1f; return; }
            int d = (int)difficulty;
            RefreshStyle(d);
            float speed = Mathf.Max(0f, car.ForwardSpeed);
            var spline = waypoints.Spline;
            float L = spline.Length;
            float along = waypoints.DistanceAlong(transform.position, ref hint);
            lastThinkAlong = along;
            if (race != null && race.Settings.laps >= 900 && !car.pitControlled) UpdatePracticeCycle();
            if (wasLocked && !car.inputLocked)
            {
                unlockTime = Time.time;
                launchCornerSeen = false;
                var p0 = spline.PoseAtDistance(along);
                lateralAtUnlock = Vector3.Dot(transform.position - p0.position, p0.rotation * Vector3.right);
                committedLateral = lateralAtUnlock;
            }
            wasLocked = car.inputLocked;
            if (mergeTimer > 0f) mergeTimer -= thinkDt;

            var myPose = spline.PoseAtDistance(along);
            float myLat = Vector3.Dot(transform.position - myPose.position, myPose.rotation * Vector3.right);
            lastMyLat = myLat;
            float half = RoadHalf();
            // Traçado ideal: na zona do pit a linha fica a ≥ 3 m da borda esquerda (mureta laranja e o bico da entrada).
            if (RaceX.Core.PitLaneSystem.LaneInfo(out float pzs, out float pzl)) RacingLine.SetPitZone(pzs - 150f, pzs + pzl + 20f, -(half - 3f));
            float edge = Mathf.Max(1.5f, half - 1.25f); // centro do carro NUNCA mirado além disto
            // MODO CALMO (v104, pedido do usuário em 04/11 depois de perder a corrida com o piloto automático rodando na chuva:
            // "todos devem andar no traçado, respeitando a velocidade da bandeira — sem ultrapassar, sem zigue-zague; só acelerar,
            // virar e tirar o pé"). Vale na fila do SC, sob amarela/VSC e no piloto automático do jogador: linha ideal da pista (não a
            // do jogador, que usa a zebra) a ≥ 1,6 m da borda, lateral devagar, volante sem serra, acelerador no máximo 60% (45% com
            // pista molhada ou pneu frio). Telemetria da batida: pneu de chuva a 0,30 de temperatura, água 0,80, acelerador 1,00 a
            // 100–109 km/h e volante −1,00 → +0,91 → −0,89 → +1,00 em 3 s: rodou e bateu na mureta da saída do box a 76 km/h.
            var flagEarly = RaceX.Core.RaceControl.FlagOf(car);
            bool calm = !car.pitControlled && (RaceX.Core.SafetyCar.QueueMode(car)
                        || flagEarly == RaceFlag.Yellow || flagEarly == RaceFlag.DoubleYellow || flagEarly == RaceFlag.VSC || flagEarly == RaceFlag.SafetyCar
                        || (isHumanCar && RaceX.Core.SafetyCar.AutopilotOn(car)));
            float calmEdge = Mathf.Max(1.5f, half - 1.6f);
            // DIAGNÓSTICO (piloto automático no carro do jogador): fora da pista, registra o porquê (1x/s).
            if (Mathf.Abs(myLat) > half + 0.5f && Time.time - diagLogT > 1f && race != null && race.GetEntry(car) != null && race.GetEntry(car).isHuman)
            {
                diagLogT = Time.time;
                Debug.Log($"[AUTO-DIAG] lat={myLat:0.0} half={half:0.0} kmh={car.SpeedKmh:0} ahead={(aheadCar != null ? aheadCar.name : "-")} gap={aheadGap:0.0} rev={reverseTimer:0.0} pass={passSide} stuck={stuckTimer:0.0} pit={RaceX.Core.PitLaneSystem.IsInPitFlow(car)} sc={RaceX.Core.SafetyCar.State}");
            }

            // ---------- TRAÇADO (pedido do usuário: "nas retas o carro anda reto; curvas em C, não em S") ----------
            float sNear = SignedCurvature(spline, along + Mathf.Clamp(speed * 0.35f, 8f, 20f));
            float sFar = SignedCurvature(spline, along + Mathf.Clamp(speed * 1.5f, 35f, 95f));
            // Só CURVA DE VERDADE conta: dobra feita de pé embaixo (limite físico perto da velocidade final) não mexe na lateral.
            float vTop = car.spec.topSpeedKmh / 3.6f;
            float near01 = Mathf.Clamp(sNear * 170f, -1f, 1f) * CornerSeverity(Mathf.Abs(sNear), vTop);
            float far01 = Mathf.Clamp(sFar * 170f, -1f, 1f) * CornerSeverity(Mathf.Abs(sFar), vTop);
            float lineAmt = d >= 2 ? 1f : d == 1 ? 0.7f : 0.4f;
            // RETA x CURVA: cada reta ganha um número (tentativa de ultrapassagem contra o mesmo carro e defesa: UMA por reta).
            bool inCorner = Mathf.Abs(near01) > 0.5f;
            if (wasInCorner && !inCorner) straightId++;
            wasInCorner = inCorner;
            // Lado de DENTRO da próxima curva de verdade (+1 direita, -1 esquerda, 0 nenhuma).
            float insideSide = Mathf.Abs(far01) > 0.2f ? Mathf.Sign(sFar) : Mathf.Abs(near01) > 0.2f ? Mathf.Sign(sNear) : 0f;
            // Antes: fora → dentro → fora com volta ao MEIO da pista em toda reta — o carro abria para um lado, cortava para o outro
            // e voltava: o "S" e o zigue-zague (autoteste de 02/11: 189 das 277 reversões na reta vinham do traçado).
            // Agora: na RETA o alvo é a própria lateral (segue reto onde está); chegando numa curva de verdade vai, num movimento só,
            // para DENTRO dela (arco em C); depois do ápice segura a lateral em que está (não volta ao meio).
            float cornerK = Mathf.Abs(near01) >= Mathf.Abs(far01) ? near01 : far01;
            float insideAmt = Mathf.Clamp01((Mathf.Abs(cornerK) - 0.2f) / 0.5f) * lineAmt;
            if (insideAmt <= lastInsideAmt + 0.001f) lineAnchor = committedLateral;   // reta ou saindo da curva: âncora = onde estou
            lastInsideAmt = insideAmt;
            float deviation = LineDeviation[d] * (1.25f - Consistency) * (Mathf.PerlinNoise(noiseSeed, Time.time * 0.15f) * 2f - 1f);
            float insideLat = Mathf.Clamp(Mathf.Sign(cornerK) * edge * InsideLineShare + deviation + lane * 0.3f, -edge, edge);
            float desiredLateral = Mathf.Clamp(Mathf.Lerp(lineAnchor, insideLat, insideAmt), -edge, edge);
            // TRAÇADO DO JOGADOR (v99, pedido do usuário: "se souberem como e onde faço as curvas"): com volta limpa dele nesta
            // condição, a base da lateral é onde ELE passa — abre, tangencia, sai. Tráfego, box, bandeira e obstáculo continuam
            // mandando por cima (targetLat); metade do desvio de consistência e um fio da faixa do pelotão (ninguém copia ao centímetro).
            float lineHere = style != null && !calm ? PlayerStyle.LatAt(style, along, L) : float.NaN;   // modo calmo: linha da pista
            bool playerLine = !float.IsNaN(lineHere);
            // Sem volta do jogador nesta condição: o TRAÇADO IDEAL da pista (RacingLine, menor curvatura dentro da pista). Antes o
            // "C por dentro" de cada curva — zigue-zague nas seções sinuosas com o carro de aderência real.
            if (!playerLine) lineHere = RacingLine.LatAt(spline, LineEdge(), along);
            bool learnedLine = !float.IsNaN(lineHere);
            // Linha do JOGADOR: ele usa a zebra (volta de 04/11: lateral até ±6,3 m com meia pista de 6,0). Cortar a linha dele a
            // 1,25 m da borda e manter a VELOCIDADE dele = curva mais fechada na mesma velocidade → o carro sai de frente e bate
            // (corrida de 04/11: 30 das 38 perdas totais com motivo "tracado" e alvo −4,8 = o corte). A lateral dele vale até 0,6 m da
            // borda (o centro do carro; as rodas ficam na zebra, dentro da regra das 4 rodas) e, onde ainda assim é cortada, o
            // CornerSpeedAhead tira o fator de ritmo (LineClampedAt).
            float lineEdge = playerLine ? Mathf.Max(edge, half - 0.6f) : edge;
            if (learnedLine) desiredLateral = Mathf.Clamp(lineHere + deviation * 0.5f + lane * 0.15f, -lineEdge, lineEdge);
            if (playerLine) playerLineEdge = lineEdge;
            if (calm) desiredLateral = Mathf.Clamp(desiredLateral, -calmEdge, calmEdge);
            // Primeiro pensamento (piloto automático ligado com o carro ANDANDO, no meio da curva): parte de onde o carro está —
            // antes partia de lateral 0 e puxava o volante para o meio da pista a 110 km/h (jogador fora da pista, 04/11).
            if (!lateralSeeded) { lateralSeeded = true; committedLateral = myLat; lineAnchor = myLat; lateralAtUnlock = myLat; latVel = 0f; }
            float launchBlend = Mathf.Clamp01((Time.time - unlockTime) / 5f);
            desiredLateral = Mathf.Lerp(Mathf.Clamp(lateralAtUnlock, -edge, edge), desiredLateral, launchBlend);
            // Primeiros segundos depois da largada (pelotão junto): sem defesa nem troca de lado e mudança de faixa devagar.
            bool startPhase = Time.time - unlockTime < StartCalmSeconds;
            // MODO LARGADA (pedido do usuário: "OBRIGUE TODOS OS CARROS a acelerar; se chegar perto do da frente, use a pista mais
            // livre"): da largada até a 1ª freada de curva (no máximo LaunchSeconds) ninguém tira o pé por TRÁFEGO — chegando no da
            // frente, desvia para o lado livre. Só a curva manda frear (senão o pelotão passa reto na curva 1) e um último recurso
            // contra batida certa. Só na largada parada da corrida (treino/classificação não têm grid).
            bool launchMode = !launchCornerSeen && Time.time - unlockTime < LaunchSeconds && race != null
                              && race.State == RaceState.Racing && race.Settings.laps < 900 && !car.pitControlled;

            // ---------- VELOCIDADE ALVO (física limitada + memória de pista) ----------
            float maxSpeed = car.spec.topSpeedKmh / 3.6f * SpeedPercent[d] * skill * RubberBand();
            float targetSpeed = Mathf.Min(maxSpeed, CornerSpeedAhead(along, speed, d));
            if (launchMode && speed > 15f && CornerSpeedAheadCached < speed - 1.5f) launchCornerSeen = true;   // chegou a 1ª freada
            // BANDEIRAS (RaceControl): amarela −5% e dupla −12% sobre a velocidade DAQUELE trecho (medido em corrida real); VSC/SC
            // teto pela velocidade final (o delta pesa nas retas: 45–60% da normal, curva lenta quase igual); azul tira o pé; carro
            // danificado (preta e laranja) indo ao box e devolução de posição andam mais devagar.
            var flagNow = RaceX.Core.RaceControl.FlagOf(car);
            bool localYellow = flagNow == RaceFlag.Yellow || flagNow == RaceFlag.DoubleYellow;
            float flagFactor = RaceX.Core.RaceControl.SpeedFactor(car);
            float flagCap = localYellow ? targetSpeed * flagFactor : flagFactor < 0.999f ? maxSpeed * flagFactor : float.PositiveInfinity;
            // SC/VSC (pedido do usuário: "não frear todo mundo"): o teto NÃO entra de uma vez. Na F1 o piloto tem de ficar
            // acima de um TEMPO MÍNIMO (delta) pelo menos uma vez por setor de fiscais — reduz ao longo do trecho, não
            // crava o freio. O teto parte da velocidade atual e desce a NeutralDecel (tirar o pé + freio leve).
            bool neutral = RaceX.Core.RaceControl.Neutralized(car) && !float.IsPositiveInfinity(flagCap);
            if (neutral)
            {
                if (!wasNeutral) neutralCap = Mathf.Max(speed, flagCap);
                neutralCap = Mathf.MoveTowards(neutralCap, flagCap, NeutralDecel * thinkDt);
                flagCap = neutralCap;
            }
            wasNeutral = neutral;
            if (flagCap < targetSpeed) targetSpeed = flagCap;
            float scCap = RaceX.Core.SafetyCar.SpeedCapFor(car);
            if (scCap < targetSpeed) targetSpeed = scCap;
            // DEVOLVER POSIÇÃO (ultrapassou sob amarela/SC ou fora da pista): tira o pé até 70% da velocidade do outro e
            // abre para fora do traçado até ele passar. Antes só andava 15% mais devagar e seguia na frente (+5 s).
            bool givingBack = false;
            var gbCar = RaceX.Core.RaceControl.GiveBackTo(car);
            if (gbCar != null)
            {
                var gbE = race != null ? race.GetEntry(gbCar) : null;
                var meE = race != null ? race.GetEntry(car) : null;
                if (gbE != null && meE != null)
                {
                    float dzg = gbE.raceDistance - meE.raceDistance;
                    if (dzg < 4f && dzg > -150f)
                    {
                        targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(6f, gbCar.ForwardSpeed * 0.7f));
                        float side = myLat >= 0f ? 1f : -1f;
                        desiredLateral = Mathf.Clamp(side * edge * 0.7f, -edge, edge);
                        givingBack = true;
                    }
                }
            }
            bool straightAhead = Mathf.Abs(near01) < 0.15f && Mathf.Abs(far01) < 0.25f;

            // ---------- PREPARAÇÃO PARA O BOX (pedido do usuário: "se ele vai pro pit, já deve se preparar") ----------
            // Box pedido: vai para o LADO DO PIT (lateral negativa) e freia (~1,1 g) para chegar a ~95 km/h onde a rota do pit
            // assume (75 m antes do corredor). A zona começa mais cedo quanto mais rápido o carro (≥ 320 m; ~500 m a 330 km/h).
            // Pedido TARDE (rápido demais ou longe do lado do pit para a distância que falta): não tenta — fica para a próxima
            // volta, sem guinada nem freada à toa. Antes mergulhava a 145 km/h a 40 m da entrada e batia no bico da mureta
            // laranja (teste 02/11, relato do usuário).
            bool pitPrep = false;
            if (!car.pitControlled && RaceX.Core.PitLaneSystem.IsPitRequested(car) && !RaceX.Core.PitLaneSystem.EntryMissed(car))
            {
                float dEntry = RaceX.Core.PitLaneSystem.DistanceToPitEntry(car);
                float prepDist = Mathf.Max(320f, 75f + (speed * speed - PitPrepV * PitPrepV) / (2f * PitPrepDecel) + 40f);
                if (dEntry > 0f && dEntry < prepDist)
                {
                    // SEGUE O TRACEJADO AZUL (pedido do usuário): o mesmo caminho que o jogador vê (PitLaneSystem.EntryGuideLat) — em cima
                    // do tracejado até 100 m antes do pit lane e daí a curva suave da entrada; a rota do box assume em cima dele. Antes a
                    // IA ia para a borda do lado do pit ~300 m antes, fora do caminho desenhado.
                    float guideHere = RaceX.Core.PitLaneSystem.EntryGuideLat(along);
                    float vWindow = Mathf.Sqrt(PitNoseV * PitNoseV + 2f * PitPrepDecel * Mathf.Max(0f, Mathf.Min(dEntry, 75f) - 12f));
                    bool speedOk = dEntry > 75f ? speed * speed <= 44.6f * 44.6f + 2f * PitBrakeMax * (dEntry - 75f) : speed <= vWindow;
                    bool sideOk = Mathf.Abs(myLat - guideHere) <= Mathf.Max(0f, dEntry - 40f) / 3f + 2f;
                    if (dEntry < 40f || !speedOk || !sideOk) RaceX.Core.PitLaneSystem.MarkEntryMissed(car);
                    else
                    {
                        pitPrep = true;
                        // Do lugar onde estava até o tracejado ao longo dos primeiros ~170 m da preparação; depois em cima dele (mirando
                        // o ponto do caminho à frente, onde a direção aponta).
                        float kSide = Mathf.InverseLerp(prepDist, Mathf.Max(110f, prepDist - 170f), dEntry);
                        float guideAhead = RaceX.Core.PitLaneSystem.EntryGuideLat(along + Mathf.Clamp(7f + 0.42f * speed, 9f, 36f));
                        desiredLateral = Mathf.Lerp(desiredLateral, guideAhead, kSide);
                        targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(PitPrepV * PitPrepV + 2f * PitPrepDecel * Mathf.Max(0f, dEntry - 75f)));
                    }
                }
            }

            // ---------- TREINO / CLASSIFICAÇÃO: CEDER A VEZ (pedido do usuário) ----------
            // Quem NÃO está em volta rápida (volta de saída/lenta) olha de LONGE (até 250 m) quem vem atrás em volta rápida
            // ou muito mais rápido; se ele chega em menos de ~8 s (ou já está a 60 m), vai para o lado, tira o pé e não
            // disputa — mais forte que a bandeira azul. Dois em volta rápida: o mais lento cede se o outro vem bem mais
            // rápido (+4 m/s). Isolado: só em sessão por tempo (laps ≥ 900), sem SC e fora do pit.
            wasYielding = lastPracYield;
            bool pracYield = false; float pracLat = 0f;
            if (race != null && race.Settings.laps >= 900 && !car.pitControlled && !RaceX.Core.SafetyCar.Deployed)
            {
                var meY = race.GetEntry(car);
                if (meY != null)
                {
                    bool myFlying = !meY.outLap;
                    float closest = float.MaxValue;
                    for (int i = 0; i < race.entries.Count; i++)
                    {
                        var o = race.entries[i];
                        if (o == null || o == meY || o.car == null || RaceX.Core.PitLaneSystem.IsInPitFlow(o.car)) continue;
                        float back = Mathf.Repeat(meY.raceDistance - o.raceDistance, L);
                        if (back < 3f || back > 250f) continue;
                        bool theirFlying = !o.outLap;
                        float oV = Mathf.Max(0f, o.car.ForwardSpeed);
                        bool faster = oV > speed + 4f;
                        if (!theirFlying || (myFlying && !faster)) continue;
                        // Cede a quem está CHEGANDO (mais rápido agora): em menos de 8 s ou já a 60 m. Mais lento que eu: só se já
                        // estiver colado (20 m). Antes cedia a qualquer um em volta rápida a 60 m, mesmo mais lento — os dois
                        // andavam devagar por centenas de metros (lateral ±4 m no log do Q3).
                        bool closingIn = oV > speed + 0.5f;
                        float tCatch = closingIn ? back / (oV - speed) : float.MaxValue;
                        if ((tCatch < 8f || back < (closingIn ? 60f : 20f)) && back < closest) closest = back;
                    }
                    // v99: só na RETA (como na F1: sai da linha onde o outro passa). No meio da curva ir para o lado = curva pelo
                    // lado errado — com o carro de aderência real o carro escapava (autoteste 03/11: 53 "cedeu" em 9 min).
                    if (closest < float.MaxValue && straightAhead)
                    {
                        if (!wasYielding) PracticeYields++;
                        pracYield = true;
                        // v99: para o lado OPOSTO ao traçado (onde quem vem em volta rápida passa). Antes ia para o lado em que já
                        // estava — em cima da linha (que cola na borda de dentro), a 78% da velocidade: Luca Romano foi acertado por
                        // trás 4 vezes num treino (03/11).
                        float side = learnedLine && Mathf.Abs(lineHere) > 0.3f ? -Mathf.Sign(lineHere)
                                   : Mathf.Abs(myLat) > 0.6f ? Mathf.Sign(myLat) : -Mathf.Sign(desiredLateral == 0f ? 1f : desiredLateral);
                        pracLat = side * edge * 0.85f;
                        if (closest < 90f) targetSpeed = Mathf.Min(targetSpeed, maxSpeed * 0.78f);
                    }
                }
            }

            // ---------- TRÁFEGO ----------
            // MODO FILA DO SAFETY CAR (isolado — só com o SC acionado; na corrida nada disto vale).
            bool scQueue = RaceX.Core.SafetyCar.QueueMode(car) && !car.pitControlled;
            queueScan = scQueue;
            ScanTraffic(along, myLat, L);
            // Carro PARADO à frente (destruído, abandonou ou parado há 3 s): não é "a fila" — sob SC contorna-se (carro
            // com problema óbvio). Antes a fila inteira parava atrás dele (log: 16 carros parados com o SC na pista).
            if (aheadCar != null && aheadCar == stoppedRef && aheadSpeed < 5f)
            {
                aheadSlowT += thinkDt;
                aheadStoppedT = aheadSpeed < 2f ? aheadStoppedT + thinkDt : 0f;
            }
            else { stoppedRef = aheadCar; aheadStoppedT = 0f; aheadSlowT = 0f; }
            bool queueObstacle = scQueue && QueueObstacle(aheadCar);
            var myFlag = RaceX.Core.RaceControl.FlagOf(car);
            bool noPassFlag = (myFlag == RaceFlag.Yellow || myFlag == RaceFlag.DoubleYellow || myFlag == RaceFlag.VSC || myFlag == RaceFlag.SafetyCar)
                              && !RaceX.Core.SafetyCar.Unlapping(car);
            // SEM ULTRAPASSAR sob amarela/VSC/SC (relato do usuário: sob VSC um carro passou o dele e não devolveu): fica atrás de
            // QUALQUER carro à frente, em qualquer lateral. Antes só contava o carro no corredor de 2,1 m à frente — quem vinha
            // por fora passava reto. Carro com problema óbvio (destruído, parado, < 40 km/h) não entra na conta: contorna-se.
            if (noPassFlag && nAheadCar != null && !car.pitControlled)
            {
                // Chega nele pela DISTÂNCIA (freada suave, ~0,36 g) e, perto, anda um pouco mais devagar que ele: nunca passa. v89
                // limitava a "velocidade dele + 6 m/s" a QUALQUER distância — com ele a 250 m, fora da tela, o carro freava forte na
                // reta sozinho (relato do usuário: "frear na reta sem motivo", atrás da fila do SC depois do box).
                float wantGap = 5f + speed * 0.12f;
                float lim = nAheadGap > wantGap
                    ? nAheadSpeed + Mathf.Sqrt(2f * QueueDecel * (nAheadGap - wantGap))
                    : nAheadSpeed - Mathf.Min(4f, (wantGap - nAheadGap) * 0.5f);
                if (lim < targetSpeed) targetSpeed = Mathf.Max(0f, lim);
            }
            bool brakingAhead = CornerSpeedAheadCached < speed - 1.5f;   // a curva à frente já pede para reduzir
            // Parado à frente = obstáculo que se CONTORNA pelo vão livre — só depois de 4 s da largada (antes disso todo carro do
            // grid está abaixo de 5 m/s e o pelotão inteiro entraria no desvio).
            bool afterLaunch = Time.time - unlockTime > 4f;
            bool aheadStationary = aheadCar != null && afterLaunch && IsStationary(aheadCar);
            float trafficLat = float.NaN;
            bool passing = false;
            // Movimento lateral OBRIGATÓRIO (box, devolver posição, obstáculo, carro ao lado, saiu da pista) é rápido; o
            // voluntário (traçado, ultrapassar, defender) é suave em alta (perfil em S lá embaixo).
            bool forcedLat = pitPrep || givingBack;
            latReason = pitPrep ? LatPit : givingBack ? LatGiveBack : LatLine;
            if (aheadCar != null && !car.pitControlled)
            {
                float closing = speed - aheadSpeed;
                bool slowObstacle = aheadSpeed < 4f && aheadGap < 35f;
                // Vantagem de carro: potência/peso e velocidade final contra o da frente (-1..1).
                float edgeCar = CarEdge(aheadCar);
                float trigger = 12f + 24f * Aggression + 8f * Mathf.Max(0f, edgeCar) + speed * 0.25f;
                // Carro PARADO à frente: começa o desvio de longe (muda de faixa sem freada seca nem guinada).
                if (aheadStationary) trigger = Mathf.Max(trigger, 25f + speed * 1.5f);
                // Pressão: preso atrás do MESMO carro, a paciência acaba (agressivo e carro melhor: antes).
                if (aheadCar == followCar && aheadGap < 25f) followTimer += thinkDt; else { followCar = aheadCar; followTimer = 0f; }
                float patience = Mathf.Lerp(7f, 2.5f, Aggression) * (1f - 0.35f * Mathf.Max(0f, edgeCar));
                bool pressure = followTimer > patience;
                // Oportunidade: o da frente errou/travou/saiu de traçado — QUALQUER carro tenta, até o mais fraco.
                bool opportunity = aheadSpeed < targetSpeed - 1.5f || slowObstacle;
                bool bigOpportunity = slowObstacle || aheadStationary || aheadSpeed < speed - 6f;
                // Carro mais forte na reta: usa a aceleração/velocidade final para emparelhar.
                bool powerMove = straightAhead && edgeCar > 0.08f && aheadGap < 20f;
                // ATAQUE NA CURVA (pedido do usuário): colado antes de uma curva de verdade, o piloto agressivo/habilidoso não fica
                // freando atrás — põe o carro POR DENTRO (o lado que abre na saída para a reta) e freia junto; os melhores só
                // aliviam o pé. Só se o lado de dentro couber (borda e carros ao lado) — sem colisão.
                bool cornerAttack = !straightAhead && insideSide != 0f && (Aggression > 0.45f || skill >= 1.0f)
                                    && aheadGap < 10f + 12f * Aggression && aheadSpeed > 12f && closing > -1.5f
                                    && SideFits(insideSide, aheadLat, edge);
                // Modo largada: chegou perto do da frente (mais rápido que ele) → desvia pelo lado livre em vez de frear. Fora dele,
                // nos 6 s da largada só passa quem largou mal de verdade — o resto mantém a faixa (cada guinada era um toque).
                bool reasonOk = launchMode ? (closing > 0.5f && aheadGap < 15f) || bigOpportunity
                              : startPhase ? bigOpportunity
                              : (closing > 0.4f || opportunity || pressure || powerMove || cornerAttack);
                // UMA tentativa por carro por reta: desistiu, só tenta de novo depois da próxima curva (ou se ele tiver problema de
                // verdade). Antes a tentativa expirava em 4,5 s, o carro voltava para trás dele e já abria de novo — o vaivém.
                bool retryBlocked = aheadCar == lastPassCar && lastPassStraight == straightId && !bigOpportunity;
                bool wantsPass = ((!scQueue && !calm) || queueObstacle) && !pracYield && !pitPrep && launchBlend > 0.3f && aheadGap < trigger
                                 && reasonOk && !retryBlocked
                                 && (queueObstacle || RaceX.Core.RaceControl.MayOvertake(car, aheadCar)); // amarela/VSC/devolução
                if (wantsPass && (passCar != aheadCar || passSide == 0f))
                {
                    float side;
                    if (aheadStationary)
                    {
                        // CONTORNO: o lado do VÃO LIVRE (pegada real dos carros parados, largura toda da pista).
                        float gl = ObstacleGapLat(along, aheadGap + 4.5f, myLat, half);
                        side = float.IsNaN(gl) ? 0f : gl >= aheadLat ? 1f : -1f;
                    }
                    else if (cornerAttack && !(closing > 0.4f || pressure || powerMove)) side = insideSide;
                    else
                    {
                        side = ChoosePassSide(along, myLat, edge);
                        // Nova tentativa contra o MESMO carro logo depois da anterior: mantém o lado de antes (se couber).
                        if (aheadCar == lastPassCar && Time.time - lastPassEndT < 4f && lastPassSide != 0f && side != 0f && SideFits(lastPassSide, aheadLat, edge))
                            side = lastPassSide;
                    }
                    // Abrir para o lado onde já vem alguém mais rápido de trás = fechar esse carro: não tenta agora.
                    if (side != 0f && !aheadStationary && CutsOff(aheadLat + side * PassOffset(aheadCar), myLat, speed)) side = 0f;
                    if (side != 0f)
                    {
                        if (passCar != aheadCar) passSwitched = false;   // uma troca de lado por tentativa
                        passCar = aheadCar;
                        passSide = side;
                        passDz = aheadGap + 4.5f; passLat = aheadLat; passFound = true;
                        passDzMin = passDz; passAge = 0f; passTimer = 4.5f;
                        passHoldLat = aheadLat + side * PassOffset(aheadCar);
                        cornerAttackActive = cornerAttack && side == insideSide;
                    }
                }
                // Vácuo na reta: carro colado atrás de outro ganha ~1,5% de velocidade final.
                if (aheadGap < 30f && straightAhead) targetSpeed = Mathf.Min(targetSpeed * 1.015f, CornerSpeedAheadCached);
            }
            // Ultrapassagem em andamento ANULADA pela bandeira (SC/VSC/amarela saiu no meio): volta para trás dele. Antes completava a
            // manobra e tinha de devolver (log de 02/11: "ultrapassou sob SAFETY CAR" 2 s depois de o SC entrar).
            if (passCar != null && noPassFlag && passDz > -4f && !IsStationary(passCar) && !RaceX.Core.RaceControl.ObviousProblem(passCar)) EndPass();
            // Fila: nenhuma ultrapassagem em andamento (só contornar carro parado/destruído). Cancela assim que o carro contornado
            // VOLTA A ANDAR (> 1 m/s) e não está destruído/fora: é carro da fila de novo.
            if (scQueue && passCar != null && !passCar.wrecked && !IsRetired(passCar) && passCar.ForwardSpeed > 1f) EndPass();
            if (passCar != null && passSide != 0f && passFound && passDz > -7f && passDz < 45f)
            {
                passDzMin = Mathf.Min(passDzMin, passDz);
                passAge += thinkDt;
                bool contour = afterLaunch && IsStationary(passCar);
                bool inside = insideSide != 0f && passSide == insideSide;
                // FIM DA TENTATIVA (sem relógio na reta): perdendo terreno, ou chegando na curva sem estar emparelhado — por dentro
                // basta o bico na altura da roda traseira dele; por fora tem de estar na frente. Aí encaixa atrás e freia junto.
                bool losing = !contour && passDz > passDzMin + 10f;
                bool notAlongside = !contour && ((inCorner && passDz > 6.5f) || (brakingAhead && !inCorner && !inside && passDz > 2.5f));
                if (losing || notAlongside || passAge > 15f) EndPass();
                else
                {
                    passing = true;
                    float passOff = PassOffset(passCar);
                    if (contour)
                    {
                        // CONTORNO de carro parado: mira o vão livre (recalculado a cada pensada — pode estar atravessado, pode haver
                        // dois lado a lado). Sem vão: espera onde está (os fiscais retiram o carro).
                        float gl = ObstacleGapLat(along, passDz, myLat, half);
                        if (!float.IsNaN(gl)) { trafficLat = gl; passSide = gl >= passLat ? 1f : -1f; }
                        else trafficLat = Mathf.Clamp(myLat, -edge, edge);
                        ContourDiag(myLat, gl, speed);
                        forcedLat = true;
                        latReason = LatObstacle;
                    }
                    else
                    {
                        // Lado escolhido FECHOU (carro ao lado/borda) e o outro abriu: troca UMA vez por tentativa (antes trocava a
                        // cada 1,5 s enquanto o lado fechava e abria — o carro ziguezagueava atrás do da frente e batia).
                        passSwitchCooldown -= thinkDt;
                        if (passSwitchCooldown <= 0f && passDz > 3f && !passSwitched && !startPhase)
                        {
                            float again = ChoosePassSide(along, myLat, edge);
                            float sideLat = passLat + passSide * passOff;
                            bool blocked = passSide > 0f ? sideLat > Mathf.Min(edge + 0.3f, rightLimit + 0.2f) : sideLat < Mathf.Max(-edge - 0.3f, leftLimit - 0.2f);
                            if (blocked && again != 0f && again != passSide) { passSide = again; passSwitchCooldown = 1.5f; passSwitched = true; passHoldLat = passLat + again * passOff; }
                        }
                        // Só se AFASTA do outro: se ele vem para o meu lado, eu abro; se ele se afasta, eu sigo reto (antes espelhava cada
                        // movimento do carro da frente — metade das reversões na reta medidas no v90 era da ultrapassagem).
                        float wantPass = passLat + passSide * passOff;
                        passHoldLat = passSide > 0f ? Mathf.Max(passHoldLat, wantPass) : Mathf.Min(passHoldLat, wantPass);
                        trafficLat = Mathf.Clamp(passHoldLat, -edge, edge);
                        latReason = LatPass;
                        // Mergulho por DENTRO na freada: o agressivo freia um pouco mais tarde (até o limite físico).
                        if (inside && passDz < 14f) targetSpeed = Mathf.Min(targetSpeed * (1f + 0.035f * Aggression), CornerSpeedAheadCached);
                    }
                }
            }
            else if (passCar != null) EndPass();

            if (aheadCar != null && !car.pitControlled)
            {
                bool isPassTarget = passing && aheadCar == passCar;
                if (isPassTarget)
                {
                    // Indo para o lado: pode encostar, mas sempre com freio suficiente para não acertar a traseira.
                    targetSpeed = Mathf.Min(targetSpeed, aheadSpeed + Mathf.Sqrt(2f * 6f * Mathf.Max(0f, aheadGap - 2f)));
                    // Contorno de carro parado ainda sem a lateral livre e já perto: devagar (dá para esterçar), mas NUNCA parado — o limite
                    // de cima zera a menos de 2 m e o carro ficava encostado no destroço sem sair (v91: Tomás Ribeiro a 0,2 m, Oscar
                    // Lindqvist a 0,6 m, fila parada atrás).
                    if (aheadStationary && aheadGap < 10f) targetSpeed = Mathf.Max(Mathf.Min(targetSpeed, Mathf.Max(1.5f, aheadGap * 0.7f)), 1.5f);
                }
                else
                {
                    // SEGUE com distância segura — sem freada seca (era o que causava as batidas em fila).
                    // Colado (vácuo) mas sem tocar: o agressivo fica mais perto para atacar na próxima freada. v89: ~0,2–0,3 s a
                    // 250 km/h (antes ~0,15–0,2 s: quando o da frente batia, o de trás não tinha como reagir — engavetamento de 02/11,
                    // 61 g na traseira). Mais folga se o da frente está colado em outro (risco de batida em cadeia) ou acabou de bater.
                    // Pelotão devagar (largada, curva lenta) segue colado como antes; a folga maior só vale em alta (72→162 km/h).
                    // v89 aplicava tudo também na largada: cada carro freava para abrir espaço e o pelotão inteiro quase parou
                    // (relato do usuário; log: 50→17 km/h aos 4–6 s).
                    float aggrK = 1.25f - 0.5f * Aggression;
                    // Depois da largada a distância maior entra DEVAGAR (de 10 a 40 s): de uma vez, cada carro do pelotão freava para
                    // abrir espaço e a sanfona corria a fila inteira.
                    float hiK = Mathf.InverseLerp(20f, 45f, speed) * Mathf.InverseLerp(10f, 40f, Time.time - unlockTime);
                    float desiredGap = Mathf.Lerp(3.0f + speed * 0.14f * aggrK, 4.0f + speed * 0.2f * aggrK, hiK);
                    var aAi = aheadCar.Ai;
                    bool settled = Time.time - unlockTime > 10f;
                    if (settled && aAi != null && aAi.enabled && aAi.aheadCar != null && aAi.aheadCar != car && aAi.aheadGap < 12f) desiredGap += 5f * hiK;
                    bool aheadHit = settled && speed > 20f && aAi != null && Time.time - aAi.lastHardHitTime < 1.2f;
                    if (aheadHit) desiredGap += 12f;
                    float follow = aheadSpeed + (aheadGap - desiredGap) * 0.5f;
                    if (scQueue)
                    {
                        // FILA: mesma velocidade do carro da frente com ~2–4 carros de distância; freada pela distância
                        // que sobra (sem o piso de 75% da velocidade dele, que fazia encostar na traseira).
                        // Chegando na cauda da fila: planeja a freada a QueueDecel (~0,36 g) — vai reduzindo enquanto anda,
                        // em vez de chegar embalado e cravar o freio a 6 m/s² (o "todo mundo freia").
                        desiredGap = 9f + speed * 0.22f;
                        follow = aheadSpeed + (aheadGap - desiredGap) * 0.35f;
                        // Parado à frente: para a ~6,5 m (sobra espaço para esterçar e contornar se ele não sair).
                        float stopGap = aheadSpeed < 1.5f ? 6.5f : 4f;
                        float stopV = aheadSpeed + Mathf.Sqrt(2f * QueueDecel * Mathf.Max(0f, aheadGap - stopGap));
                        targetSpeed = Mathf.Min(targetSpeed, Mathf.Min(follow, stopV));
                    }
                    // v99 (pedido do usuário: "em reta, batida leve, continuam acelerando"): atrás de um carro que acabou de bater mas
                    // SEGUE andando, nunca abaixo de metade da velocidade dele — com piso 0 o de trás parava de vez.
                    else targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(follow, aheadHit ? aheadSpeed * 0.5f : aheadSpeed * 0.75f));
                    // VELOCIDADE SEGURA pela distância (qualquer carro à frente, não só o parado): com a diferença de velocidade
                    // que sobra, ainda dá para frear a ~0,8 g antes de encostar. O "follow" acima é proporcional e, com o da frente
                    // MUITO mais lento (danificado, voltando de rodada), deixava chegar rápido demais. Em disputa roda a roda (pouca
                    // diferença) não pesa: só limita quando a diferença é grande para a distância.
                    targetSpeed = Mathf.Min(targetSpeed, aheadSpeed + Mathf.Sqrt(2f * FollowDecel * Mathf.Max(0f, aheadGap - 2.5f)));
                    // LARGADA (3 s após apagar as luzes): o carro da frente no grid está parado a ~3,5 m e arranca junto —
                    // tratar como "obstáculo parado" fazia a IA FREAR antes de arrancar (relato do usuário). Na largada só
                    // a distância mínima (1,5 m) conta; a emergência abaixo continua valendo.
                    bool launching = Time.time - unlockTime < 3f && aheadGap > 1.5f;
                    if (launching) targetSpeed = Mathf.Max(targetSpeed, Mathf.Min(maxSpeed, aheadSpeed + (aheadGap - 1.5f) * 1.2f + 3f));
                    else if (aheadSpeed < 4f) targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, aheadGap - 3f)));
                }
            }

            // FILA DO SC EM FILA INDIANA (como na F1): colado (até 25 m) no carro da frente, segue a MESMA linha dele — quando ele
            // contorna um destroço, quem vem atrás passa pelo mesmo caminho. Antes cada carro só via o destroço quando o da frente saía
            // do caminho, já perto: freava, desviava devagar e o de trás repetia (v93: a fila arrastou 25 s abaixo de 10 km/h).
            // v104: só quando o da frente está CONTORNANDO um destroço — copiar a lateral dele o tempo todo era uma fonte de
            // zigue-zague na fila (o alvo pulava entre a linha e a lateral do carro da frente; jogador a ±7 m a 100 km/h, 04/11).
            if (scQueue && !passing && aheadCar != null && !RaceX.Core.SafetyCar.Is(aheadCar) && aheadGap < 25f
                && !aheadCar.wrecked && !IsRetired(aheadCar) && aheadCar.Ai != null && aheadCar.Ai.latReason == LatObstacle)
            {
                trafficLat = aheadLat;
                latReason = LatObstacle; forcedLat = true;
            }

            // DEFESA: carro mais rápido colado atrás antes de uma curva de verdade → fecha por dentro (UM movimento POR RETA, como
            // na F1). Na fila e nos primeiros segundos da largada não há defesa. Feito o movimento, SEGURA a linha até a curva —
            // antes voltava ao traçado depois de 2 s sem ameaça (o segundo movimento: o "zague"). Nunca fecha quem já está saindo
            // de trás para o lado (CutsOff).
            if (inCorner) { defendUsed = false; defendActive = false; }   // na curva: libera
            if (behindCar != null && !passing && !scQueue && !calm && !startPhase && behindGap < 16f && behindSpeed > speed + 0.5f && !defendUsed
                && Defending > 0.45f && insideSide != 0f && launchBlend >= 1f)
            {
                defendUsed = true;
                float dl = Mathf.Clamp(insideSide * edge * 0.55f, -edge, edge);
                if (!CutsOff(dl, myLat, speed)) { defendActive = true; defendLat = dl; }
            }
            if (defendActive && !passing) { trafficLat = defendLat; latReason = LatDefend; }

            // BANDEIRA AZUL: retardatário abre o traçado para o líder (vai para o lado em que já está).
            // v103 (relato do usuário, 04/11: "na corrida os retardatários saem da PISTA para eu passar"): o movimento era feito em
            // qualquer ponto, inclusive no meio da curva, na velocidade de corrida → carro fora. Como na F1: sai da LINHA na reta
            // (lado oposto ao traçado, até 70% da borda), tira o pé (SpeedFactor 0,94) e, na curva, SEGURA a faixa em que já abriu
            // — sem novo movimento. Continua competitivo: nada disso vale sem carro a até 40 m atrás (BlueFor).
            if (RaceX.Core.RaceControl.BlueFlag(car) && !passing && !calm)
            {
                if (straightAhead || !blueHold)
                {
                    float sideB = learnedLine && Mathf.Abs(lineHere) > 0.3f ? -Mathf.Sign(lineHere) : (myLat >= 0f ? 1f : -1f);
                    if (straightAhead) { blueLat = sideB * edge * 0.7f; blueHold = true; }
                }
                if (blueHold) { trafficLat = blueLat; latReason = LatBlue; }
                defendActive = false;   // encerra a defesa (sem liberar outra até a curva)
            }
            else blueHold = false;
            lastPracYield = pracYield;
            if (pracYield && !passing) { trafficLat = pracLat; defendActive = false; latReason = LatYield; }
            float targetLat = float.IsNaN(trafficLat) ? desiredLateral : trafficLat;
            // Contornando carro parado o alvo pode chegar à zebra ou à área de escape (ObstacleGapLat); o resto, até "edge".
            float latBound = latReason == LatObstacle ? half + 2.6f : latReason == LatLine && playerLine ? playerLineEdge : edge;
            // FILA DO SAFETY CAR / neutralização: ninguém anda na zebra atrás do SC (na fila a precisão cai e cada passada com as rodas
            // de fora era advertência — corrida de 04/11: a fila inteira a ±6,5 m com meia pista de 6,0). Alvo a ≥ 1,6 m da borda.
            if ((scQueue || noPassFlag) && latReason != LatObstacle) latBound = Mathf.Min(latBound, Mathf.Max(1.5f, half - 1.6f));
            // ESPAÇO DE UM CARRO: nunca espreme quem está ao lado.
            float limitedLat = leftLimit <= rightLimit ? Mathf.Clamp(targetLat, leftLimit, rightLimit) : targetLat;
            if (Mathf.Abs(limitedLat - targetLat) > 0.3f)
            {
                forcedLat = true;
                limitHoldT = 1.2f; limitHoldLat = limitedLat;
                if (latReason == LatLine) latReason = LatLimit;
            }
            else if (limitHoldT > 0f)
            {
                // O carro ao lado acabou de passar: segura a faixa ~1,2 s antes de voltar ao traçado (sem o "vai e volta" a cada
                // carro que passa).
                limitHoldT -= thinkDt;
                if (latReason == LatLine && straightAhead) { limitedLat = limitHoldLat; latReason = LatLimit; }
            }
            targetLat = Mathf.Clamp(limitedLat, -latBound, latBound);
            // NÃO FECHA NINGUÉM: na reta, mudança de faixa voluntária que cortaria a frente de quem vem atrás (mais rápido, já naquela
            // faixa) espera — parte das batidas por trás vinha da volta ao traçado depois de uma ultrapassagem desistida.
            if (straightAhead && !forcedLat && Mathf.Abs(targetLat - committedLateral) > 0.3f && CutsOff(targetLat, myLat, speed))
            {
                targetLat = committedLateral;
                // Era a ultrapassagem: sem poder abrir, a tentativa acaba (não fica colado atrás "tentando" sem sair para o lado).
                if (latReason == LatPass) { EndPass(); passing = false; }
            }
            // MURETA LARANJA (entrada/saída do pit, lado do pit = lateral negativa): quem NÃO vai ao box fica a ~1 m da
            // borda desse lado no trecho do pit (pedido do usuário: "perceberem a mureta laranja").
            // v99: vale para TODO carro que não está na preparação do box (quem pediu o box mas perdeu a entrada, ou cede a vez,
            // seguia sem a trava e entrava no funil a 120 km/h — Hiro Tanaka, 03/11).
            if (!car.pitControlled && !pitPrep && RaceX.Core.PitLaneSystem.LaneInfo(out float pls, out float pll))
            {
                float relP = Mathf.Repeat(along - pls + 60f, L) - 60f;
                if (relP > -40f && relP < pll + 15f) targetLat = Mathf.Max(targetLat, -(half - 1.1f));
                // BICO da mureta (12 m antes do corredor): v99 — com o carro de aderência real e o traçado ideal colado na borda
                // esquerda da reta dos boxes, 6 dos 9 carros destruídos nos treinos de 03/11 ficaram em 36–54 m, lateral −7 a −11 (um
                // deles ultrapassando pela esquerda a 154 km/h). No trecho do bico: ≥ 2 m da borda; traçado a ≥ 3 m.
                // Trecho do bico: ≥ 3 m da borda para TODOS (quem cede a vez também — Hiro Tanaka chegou a −7 m com alvo −4).
                if (relP > -60f && relP < 20f) targetLat = Mathf.Max(targetLat, -(half - 3.0f));
            }

            // ---------- SAIU DA PISTA ----------
            bool offTrack = car.IsOffTrack || Mathf.Abs(myLat) > half + 0.6f;
            // Contornando carro parado pela zebra/área de escape (de propósito) não é "saiu da pista": não puxa de volta no meio do desvio.
            if (offTrack && latReason == LatObstacle) offTrack = false;
            if (offTrack)
            {
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(15f, speed * 0.85f));
                targetLat = Mathf.Clamp(myLat * 0.4f, -edge, edge);
                forcedLat = true;
            }
            LearnFromTrack(along, speed, L, offTrack);
            ZigzagSample(myLat, straightAhead && speed > 30f && !scQueue && !offTrack && !car.pitControlled, speed);

            // Lateral com PERFIL EM S (velocidade e aceleração laterais limitadas — entra e sai do movimento sem tranco). Movimento
            // obrigatório: rápido. Voluntário em alta: suave (1,4 m/s a 270 km/h) — com a rampa seca de antes o carro passava do
            // alvo em alta e voltava (simulação de 02/11: troca de 2,6 m chegava a 3,9 m com 4–7 reversões; em S, ~3,0 m).
            float latRate = Mathf.Clamp((passing ? 3.6f : 4.0f) - speed * 0.02f, 2.0f, 4.0f);
            float latAcc = 8f;
            if (!forcedLat)
            {
                float hi = Mathf.InverseLerp(30f, 75f, speed);
                latRate = Mathf.Min(latRate, Mathf.Lerp(3.2f, 1.4f, hi));
                latAcc = Mathf.Lerp(5f, 1.0f, hi);
            }
            // Traçado do jogador: a linha é suave por construção (média da volta dele) — a lateral acompanha sem o freio do perfil
            // em S, senão o carro ficava metros atrás da linha nas curvas encadeadas.
            if (learnedLine && latReason == LatLine && !forcedLat) { latRate = 4.5f; latAcc = 8f; }
            // Modo calmo: lateral devagar (1,2 m/s, 0,8 m/s²) — o movimento de 1 m leva ~1,5 s, sem serra no volante.
            if (calm && !forcedLat && latReason != LatObstacle) { latRate = Mathf.Min(latRate, 1.2f); latAcc = Mathf.Min(latAcc, 0.8f); }
            if (startPhase) latRate = Mathf.Min(latRate, passing && launchMode ? 2.5f : 1.2f);   // desvio da largada: mais rápido
            // LARGADA: tráfego não segura ninguém (sem distância de seguimento, sem esperar atrás) — o alvo volta ao limite da curva, mas
            // SEMPRE dá para parar atrás do da frente (~0,8 g). v91 liberava tudo: o pelotão chegou de pé embaixo na curva 1, o da
            // frente freou e os de trás acertaram (perda total aos 12 s, SC). Na prática esse limite só pesa quando o da frente FREIA.
            if (launchMode)
            {
                float launchCap = Mathf.Min(maxSpeed, CornerSpeedAheadCached);
                if (aheadCar != null) launchCap = Mathf.Min(launchCap, aheadSpeed + Mathf.Sqrt(2f * FollowDecel * Mathf.Max(0f, aheadGap - 2f)));
                targetSpeed = Mathf.Max(targetSpeed, launchCap);
            }
            // FREADA DE PELOTÃO: colado (até 25 m) num carro da IA que já freia para a curva (o plano dele pede menos velocidade que o
            // meu), freio JUNTO com ele — mantém a distância em vez de frear no MEU ponto e chegar em cima dele (como o piloto que vê a
            // luz de freio do da frente). Cada IA freia num ponto (habilidade); com os carros juntos (largada) quem vinha atrás acertava
            // quem freou antes — v91/v92: engavetamento e perda total na curva 1.
            if (aheadCar != null && !car.pitControlled && aheadGap < 25f)
            {
                var aPack = aheadCar.Ai;
                if (aPack != null && aPack.enabled && aPack.CornerSpeedAheadCached < targetSpeed)
                    targetSpeed = Mathf.Min(targetSpeed, aPack.CornerSpeedAheadCached + Mathf.Max(0f, aheadGap - 3f) * 0.15f);
            }
            if (mergeTimer > 0f) latRate = 0.8f;
            if (offTrack) latRate = 3f;
            float remLat = targetLat - committedLateral;
            float vWant = Mathf.Clamp(Mathf.Sign(remLat) * Mathf.Sqrt(2f * latAcc * Mathf.Abs(remLat)), -latRate, latRate);
            latVel = Mathf.MoveTowards(latVel, vWant, latAcc * thinkDt);
            float latStep = latVel * thinkDt;
            if (latStep * remLat >= 0f && Mathf.Abs(latStep) >= Mathf.Abs(remLat)) { committedLateral = targetLat; latVel = 0f; }
            else committedLateral += latStep;

            // ---------- DIREÇÃO (pure pursuit) ----------
            // Na RETA mira mais longe (até 60 m): o carro segue reto sem "costurar" as ondulações do desenho da pista; nas curvas
            // (e contornando carro parado, que pede resposta curta) a mira de antes.
            float straightness = latReason == LatObstacle || latReason == LatPit ? 0f : 1f - Mathf.Clamp01(Mathf.Max(Mathf.Abs(near01), Mathf.Abs(far01)) / 0.3f);
            float ld = Mathf.Lerp(Mathf.Clamp(7f + 0.42f * speed, 9f, 36f), Mathf.Clamp(10f + 0.6f * speed, 12f, 60f), straightness);
            if (calm && latReason != LatObstacle) ld = Mathf.Max(ld, 12f + 0.7f * speed);   // modo calmo: mira longa, volante suave
            float pursuitLat = committedLateral;
            // Seguindo o traçado do jogador: o ponto de mira fica NA LINHA dele lá na frente (a curvatura da linha entra na direção).
            if (learnedLine && latReason == LatLine)
            {
                float lineAhead = playerLine ? PlayerStyle.LatAt(style, along + ld, L) : RacingLine.LatAt(spline, LineEdge(), along + ld);
                if (!float.IsNaN(lineAhead)) pursuitLat = Mathf.Clamp(committedLateral + (lineAhead - lineHere), -half, half);
            }
            Vector3 target = spline.PoseAtDistance(along + ld, pursuitLat).position;
            Vector3 local = transform.InverseTransformPoint(target);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(0.5f, local.z));
            float dist = Mathf.Max(4f, Mathf.Sqrt(local.x * local.x + local.z * local.z));
            float deltaDeg = Mathf.Atan(2f * car.spec.wheelbase * Mathf.Sin(alpha) / dist) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(deltaDeg / EffectiveMaxSteer(), -1f, 1f);
            // O CarController eleva o comando a 1,15 no volante retangular: compensa para o ângulo sair certo.
            if (car.spec.rectangularSteeringWheel) steer = Mathf.Sign(steer) * Mathf.Pow(Mathf.Abs(steer), 1f / 1.15f);
            // Modo calmo: o comando do volante muda no máximo 2,5/s (de batente a batente em 0,8 s) — sem a serra −1 → +1 → −1
            // que rodou o carro do jogador na chuva.
            if (calm && latReason != LatObstacle) steer = Mathf.MoveTowards(cachedSteer, steer, 2.5f * thinkDt);

            // ---------- PEDAIS ----------
            float throttle, brake;
            float error = targetSpeed - speed;
            if (error > 0f)
            {
                throttle = (error > 3f || straightAhead) ? 1f : Mathf.Clamp01(0.35f + error / 3f);
                brake = 0f;
                // Controle de tração do piloto: patinou atrás, alivia o pé.
                float rearSlip = Mathf.Max(car.WheelSlip[2], car.WheelSlip[3]);
                if (rearSlip > 0.6f) throttle *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((rearSlip - 0.6f) / 0.6f));
                // Modo calmo: acelerador no máximo 60%; pista molhada ou pneu frio (saída do box) 45% — nada de pé embaixo na fila.
                if (calm)
                {
                    bool slippery = WeatherSystem.Wetness > 0.15f || RaceX.Core.PitLaneSystem.TireTemp(car) < 0.6f;
                    throttle = Mathf.Min(throttle, slippery ? 0.45f : 0.6f);
                }
            }
            else
            {
                throttle = 0f;
                float over = -error;
                brake = over > 0.4f ? Mathf.Clamp01((over - 0.3f) / 2.5f) : 0f;
                // ATAQUE NA CURVA, piloto habilidoso: sobra pequena de velocidade = só alivia o pé (sem tocar no freio) — entra
                // mais limpo, sem travar roda ao lado do outro (pedido do usuário). Sobra maior: freia como todo mundo.
                // v99: sobra até 0,6 m/s (era 1,2) — com a IA a 96% do limite real, 1,2 m/s sem frear tirava o carro da pista.
                if (cornerAttackActive && passing && skill >= 1.0f && over < 0.6f) brake = 0f;
                // ESTILO: onde o jogador só tira o pé (sem freio), a IA também — sobra pequena de velocidade sai no motor.
                if (style != null && over < 2.5f)
                {
                    int sg = PlayerStyle.Seg(along, L);
                    if (style.n[sg] > 0 && style.coast[sg] > 0.5f && style.brake[sg] < 0.1f) brake = 0f;
                }
            }

            // CONTORNANDO carro parado devagar (até ~10 km/h): os freios de "perto demais" abaixo não valem — eles travavam o carro
            // colado no destroço e a fila inteira esperava os fiscais (teste de 02/11: 16 parados a menos de 10 km/h).
            bool contourCreep = passing && aheadCar != null && aheadCar == passCar && aheadStationary && speed < 3f;
            // FILA: perto demais (< 6 m) e ainda fechando → freada firme já.
            if (scQueue && aheadCar != null && aheadGap < 6f && speed > aheadSpeed + 0.3f && !contourCreep)
            {
                throttle = 0f;
                brake = Mathf.Max(brake, Mathf.Clamp01(0.5f + (speed - aheadSpeed) / 5f));
            }
            // EMERGÊNCIA: perto demais de quem está na frente e fechando rápido (vale também na ultrapassagem).
            // Na largada só o último recurso: a menos de 1,5 m chegando 5 m/s mais rápido (batida certa).
            if (aheadCar != null && aheadGap < 2.5f + Mathf.Max(0f, speed - aheadSpeed) * 0.5f && speed > aheadSpeed + 1.5f && !contourCreep
                && (!launchMode || (aheadGap < 1.5f && speed > aheadSpeed + 5f)))
            {
                throttle = 0f;
                brake = Mathf.Max(brake, Mathf.Clamp01((speed - aheadSpeed) / 6f));
            }

            bool pastLaunch = (Time.time - unlockTime) > 12f;
            if (pastLaunch && UpdateRecovery(speed, ref steer, ref throttle, ref brake)) { }
            else if (!pastLaunch) { reverseTimer = 0f; stuckTimer = 0f; }
            if (pastLaunch && ProgressWatchdog(along, speed)) return;

#if UNITY_EDITOR
            DebugState = $"along={along:F0} tgt={targetSpeed * 3.6f:F0} v={speed * 3.6f:F0} thr={throttle:F2} brk={brake:F2} steer={steer:F2} lat={myLat:F1}->{committedLateral:F1} pass={passSide} gap={aheadGap:F0}";
#endif
            cachedSteer = steer;
            cachedThrottle = throttle;
            cachedBrake = brake;
        }

        bool queueScan;   // ScanTraffic no modo fila do SC (corredor "à frente" mais largo)
        /// <summary>Desaceleração da neutralização e da chegada na fila (m/s², ~0,36 g): valor de projeto — tirar o pé com
        /// freio leve. A regra (delta por setor de fiscais) dá ao piloto o setor inteiro para reduzir.</summary>
        const float NeutralDecel = 3.5f, QueueDecel = 3.5f;
        /// <summary>Online: ritmo de UM jogador (PlayerPaceLearner, 128 trechos, m/s) para o automático que pilota o carro dele
        /// (menu aberto ou queda de conexão) — a IA nunca passa da velocidade dele em cada trecho. null = IA normal.</summary>
        [System.NonSerialized] public float[] paceCap;
        /// <summary>Freada planejada da preparação para o box (~1,1 g) e a máxima considerada para ainda dar tempo (~1,2 g, a
        /// mesma ordem do teto de freada que a IA usa nas curvas — 12,5 m/s²). Valores de projeto.</summary>
        const float PitPrepDecel = 9.5f, PitBrakeMax = 11f;
        /// <summary>v99: velocidade de chegada na ENTRADA do box — 75 m antes do corredor (PitPrepV) e no bico da mureta, 12 m antes
        /// (PitNoseV) — 22 m/s (~79 km/h); antes 26–27 m/s (~95 km/h), calibrado no carro de aderência 2,9: com 2,3 o carro escorregava
        /// na curva de entrada e batia no bico (autoteste 03/11). Freadas 9,5 e 11 m/s² ficam abaixo da freada medida (mediana ~12).</summary>
        const float PitPrepV = 22f, PitNoseV = 22f;
        /// <summary>Freada planejada atrás de carro mais lento (~0,8 g — abaixo do teto da IA nas curvas, 12,5 m/s², para
        /// sobrar freio se o da frente frear mais). Valor de projeto.</summary>
        const float FollowDecel = 8f;
        /// <summary>Abaixo disto (m/s) o carro à frente é "lento/parado": pode estar atravessado — corredor e desvio maiores.</summary>
        const float SlowCarSpeed = 5f;

        /// <summary>Distância lateral para passar ao lado do carro: 2,6 m normal; 3,0 m se ele está lento/parado (pode estar
        /// torto — mais folga). Teste 02/11: com 3,6 m (e corredor de 3 m) a fila do SC não cabia para contornar o destroço e
        /// parava ~8 s atrás dele; 3,0/2,6 dá folga sem travar a fila.</summary>
        static float PassOffset(CarController target) => target != null && target.ForwardSpeed < SlowCarSpeed ? 3.0f : 2.6f;

        /// <summary>Segundos depois da largada sem defesa/troca de lado e com mudança de faixa devagar (valor de projeto).</summary>
        const float StartCalmSeconds = 6f;

        /// <summary>O lado (±1) ainda cabe para passar o carro que está na lateral otherLat (borda e carros ao lado)?</summary>
        bool SideFits(float side, float otherLat, float edge)
        {
            float lat = otherLat + side * PassOffset(aheadCar);
            return side > 0f ? lat <= Mathf.Min(edge + 0.3f, rightLimit + 0.2f) : lat >= Mathf.Max(-edge - 0.3f, leftLimit - 0.2f);
        }

        // Memória da última tentativa de ultrapassagem e da defesa (ver Think): evita o vaivém na reta.
        CarController lastPassCar;
        float lastPassSide, lastPassEndT = -99f;
        bool passSwitched;
        bool defendActive;
        float neutralCap; bool wasNeutral;
        CarController stoppedRef; float aheadStoppedT, aheadSlowT;
        // RETA x CURVA (v89): cada reta tem um número — tentativa de ultrapassagem contra o mesmo carro e defesa: UMA por reta.
        int straightId; bool wasInCorner;
        int lastPassStraight = -1;
        float passDzMin, passAge;
        bool cornerAttackActive;
        // Traçado em C: lateral "de reta" (âncora) e quanto a curva à frente já puxou para dentro na pensada anterior.
        float lineAnchor, lastInsideAmt;
        /// <summary>Quanto do lado de dentro o arco da curva usa (fração de "edge"; valor de projeto — longe da zebra).</summary>
        const float InsideLineShare = 0.45f;
        /// <summary>Última batida FORTE (> 25 km/h de diferença) — o de trás abre distância; toque leve de pelotão não conta.</summary>
        float lastHardHitTime = -99f;
        /// <summary>Modo largada: termina na 1ª freada de curva ou após este tempo (s; valor de projeto).</summary>
        const float LaunchSeconds = 15f;
        bool launchCornerSeen;
        /// <summary>Lateral da ultrapassagem: só se AFASTA do carro alvo (não espelha quando ele se afasta).</summary>
        float passHoldLat;
        // Velocidade lateral do alvo (perfil em S: entra e sai do movimento sem tranco).
        float latVel;
        // Carro ao lado empurrou a lateral: segura um instante antes de voltar (sem vaivém quando ele passa).
        float limitHoldT, limitHoldLat;
        // Sob amarela/VSC/SC: o carro mais próximo À FRENTE em QUALQUER lateral (ninguém é ultrapassado).
        CarController nAheadCar; float nAheadGap = float.MaxValue, nAheadSpeed;
        // Carros logo ATRÁS (até 30 m): para não fechar ninguém ao mudar de faixa.
        const int RearMax = 8;
        readonly float[] rearDz = new float[RearMax], rearLat = new float[RearMax], rearV = new float[RearMax];
        int rearN;
        bool isHumanCar;
        float watchdogPhase;
        // Vão livre (ObstacleGapLat): intervalos laterais ocupados por carros parados (reuso, sem alocação).
        readonly float[] gapLo = new float[12], gapHi = new float[12];
        // Motivo da lateral desta pensada (autoteste: a que se deve cada zigue-zague).
        public const int LatLine = 0, LatPass = 1, LatDefend = 2, LatLimit = 3, LatBlue = 4, LatGiveBack = 5, LatPit = 6, LatObstacle = 7, LatYield = 8;
        public static readonly string[] LatReasonNames = { "tracado", "ultrapassagem", "defesa", "carroAoLado", "azul", "devolucao", "box", "obstaculo", "ceder" };
        int latReason;
        /// <summary>Autoteste: reversões da lateral (≥ 0,5 m) em reta acima de 108 km/h, fora da fila do SC, e quanto se andou
        /// nessas retas (m) — jogador (piloto automático) e IAs; por motivo da lateral na hora da reversão.</summary>
        public static int ZigPlayer, ZigNpc;
        public static float ZigDistPlayer, ZigDistNpc;
        public static readonly int[] ZigByReason = new int[9];
        const float ZigAmp = 0.5f;
        bool zigInit; int zigDir; float zigExt, zigMin, zigMax;

        bool IsRetired(CarController c)
        {
            var e = race != null && c != null ? race.GetEntry(c) : null;
            return e != null && e.retired;
        }

        /// <summary>Carro que a fila do SC CONTORNA: destruído, abandonou, ou parado há 3 s (o SC nunca).</summary>
        bool QueueObstacle(CarController c)
        {
            if (c == null || RaceX.Core.SafetyCar.Is(c)) return false;
            if (c.wrecked || IsRetired(c)) return true;
            if (c != stoppedRef) return false;
            // Parado com PISTA LIVRE à frente dele (não está esperando ninguém) = carro com problema: contorna em 1,5 s.
            // Parado atrás de outro (fila esperando) só depois de 8 s: o primeiro da fila contorna pelo vão livre e a fila anda
            // atrás dele (v89). Com 3 s a fila inteira se ultrapassava enquanto ele contornava.
            var oai = c.Ai;
            bool freeAhead = oai == null || !oai.enabled || oai.aheadCar == null || oai.aheadGap > 12f;
            // Com pista LIVRE à frente dele: parado há 1,5 s, ou ARRASTANDO (< 5 m/s) há 3 s — carro com problema evidente (furo,
            // dano indo ao box), que a regra deixa passar. v92: a fila do SC andou a ~8 km/h atrás de um carro furado.
            if (freeAhead) return aheadStoppedT > 1.5f || aheadSlowT > 3f;
            return aheadStoppedT > 8f;
        }
        bool wasYielding, lastPracYield;
        /// <summary>Autoteste: quantas vezes um carro começou a ceder no treino / respawns na lateral.</summary>
        public static int PracticeYields, LateralRespawns;

        /// <summary>Última velocidade de curva calculada (o vácuo/mergulho nunca passa do limite físico).</summary>
        float CornerSpeedAheadCached = float.MaxValue;

        /// <summary>Carros perto (pela distância NA PISTA, com volta): quem está à frente na minha faixa, quem está ao lado
        /// (limites laterais), quem vem colado atrás, o mais próximo à frente em QUALQUER lateral (sem ultrapassar sob bandeira)
        /// e quem está logo atrás até 30 m (para não fechar ninguém ao mudar de faixa).</summary>
        void ScanTraffic(float along, float myLat, float L)
        {
            aheadCar = null; aheadGap = float.MaxValue; aheadLat = 0f; aheadSpeed = 0f;
            behindCar = null; behindGap = float.MaxValue; behindSpeed = 0f;
            nAheadCar = null; nAheadGap = float.MaxValue; nAheadSpeed = 0f;
            rearN = 0;
            leftLimit = -100f; rightLimit = 100f;
            passFound = false;
            if (race == null || race.entries == null) return;
            var me = race.GetEntry(car);
            if (me == null) return;
            var spline = waypoints.Spline;
            float roadLimit = RoadHalf() + 4f;
            float myHalfW = car.spec != null ? car.spec.bodySize.x * 0.5f : 0.95f;
            // ALCANCE À FRENTE pela velocidade: o bastante para PARAR a ~0,8 g, mais 40 m (75 m devagar, até 280 m na reta).
            // Antes era fixo em 75 m: a 216 km/h um carro rodado/parado só aparecia quando já não dava para parar — engavetamento
            // de 3 carros no autoteste de 02/11 (volta 4: 3 perdas totais).
            float mySpeed = Mathf.Max(0f, car.ForwardSpeed);
            float aheadRange = Mathf.Clamp(mySpeed * mySpeed / (2f * FollowDecel) + 40f, 75f, 280f);
            bool scOut = RaceX.Core.SafetyCar.Deployed;
            for (int i = 0; i < race.entries.Count; i++)
            {
                var e = race.entries[i];
                if (e == null || e.car == null || e.car == car) continue;
                if (!e.car.gameObject.activeInHierarchy) continue; // carro destruído já retirado
                float dz = Mathf.Repeat(e.raceDistance - me.raceDistance + L * 0.5f, L) - L * 0.5f;
                if (dz < -30f || dz > aheadRange) continue;
                // Confere pela posição real (raceDistance pode atrasar um quadro): distância em linha reta coerente.
                if ((e.car.transform.position - transform.position).sqrMagnitude > (aheadRange + 15f) * (aheadRange + 15f)) continue;
                var p = spline.PoseAtDistance(along + dz);
                Vector3 rgt = p.rotation * Vector3.right;
                float oLat = Vector3.Dot(e.car.transform.position - p.position, rgt);
                if (Mathf.Abs(oLat) > roadLimit) continue; // fora da pista / no box
                // Carro do fluxo do pit só some do radar quando JÁ está no pit (rota assumiu ou fora da pista). v99: antes sumia assim
                // que pedia o box — indo devagar para a entrada, ainda NA pista, ninguém o via: Luca Romano (preta e laranja) foi
                // acertado por trás 4 vezes na reta dos boxes a 166–182 km/h ("frente=-" nos 4 registros).
                if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car) && (e.car.pitControlled || Mathf.Abs(oLat) > RoadHalf() + 0.6f)) continue;
                float oSpeed = Mathf.Max(0f, e.car.ForwardSpeed);
                float latDiff = oLat - myLat;
                // Fila do SC: retardatário desdobrando passa "por fora" (sem colisão) — não é o carro da frente da fila.
                if (queueScan && RaceX.Core.SafetyCar.Unlapping(e.car)) continue;
                if (e.car == passCar) { passFound = true; passDz = dz; passLat = oLat; }
                // Carro PARADO/lento (destruído, rodado) pode estar ATRAVESSADO: a pegada lateral real (comprimento × ângulo) define o
                // espaço ao lado e o corredor — antes valia 2,3 m do centro dele em qualquer orientação.
                bool stationary = e.car.wrecked || e.retired || oSpeed < SlowCarSpeed;
                float sideClear = 2.3f, corridorS = 0f;
                if (stationary)
                {
                    var os = e.car.spec;
                    float hl = os != null ? os.bodySize.z * 0.5f : 2.4f, hw = os != null ? os.bodySize.x * 0.5f : 0.95f;
                    float ext = Mathf.Abs(Vector3.Dot(rgt, e.car.transform.forward)) * hl + Mathf.Abs(Vector3.Dot(rgt, e.car.transform.right)) * hw;
                    sideClear = Mathf.Max(2.3f, ext + myHalfW + 0.35f);
                    corridorS = ext + myHalfW + 0.25f;
                }
                // Atacante que JÁ SAIU DE TRÁS e está emparelhando (deslocado ≥ 2 m para o lado, bico na altura da roda
                // traseira, mais rápido): conta como "ao lado" — não se joga o carro em cima de quem já está ao lado.
                // A DEFESA continua: atacante ainda atrás, na mesma linha (desvio < 2 m), NÃO entra aqui — o "fechar a
                // porta" antes da freada (um movimento) segue igual. Linha tênue de propósito (pedido do usuário).
                bool closingSide = dz < -5.5f && dz > -8f && Mathf.Abs(latDiff) >= 2f && oSpeed > mySpeed + 1.5f;
                if (Mathf.Abs(dz) < 5.5f || closingSide)
                {
                    // AO LADO: guarda o espaço de um carro (2,3 m de separação; mais com carro parado atravessado).
                    float reach = 4.5f + (sideClear - 2.3f);
                    if (latDiff > 0f && latDiff < reach) rightLimit = Mathf.Min(rightLimit, oLat - sideClear);
                    else if (latDiff <= 0f && latDiff > -reach) leftLimit = Mathf.Max(leftLimit, oLat + sideClear);
                }
                // Corredor "à frente": carro DESTRUÍDO/fora (ou o que estou contornando) = a pegada dele, com a mesma folga do vão livre
                // (ObstacleGapLat) — com 2,6–3,6 m o carro já posicionado no vão continuava vendo o destroço "à frente" e freava (fila
                // parada no teste de 02/11). Carro da fila do SC: corredor da fila (3,6 m — espera atrás, não passa); lento: mais largo.
                float corridor;
                if (stationary && (e.car.wrecked || e.retired || e.car == passCar)) corridor = corridorS;
                else corridor = queueScan && e.car != passCar ? 3.6f : oSpeed < SlowCarSpeed ? Mathf.Max(2.6f, corridorS) : 2.1f;
                if (dz > 2f && Mathf.Abs(latDiff) < corridor)
                {
                    float gap = dz - 4.5f;
                    if (gap < aheadGap) { aheadGap = Mathf.Max(0.1f, gap); aheadCar = e.car; aheadLat = oLat; aheadSpeed = oSpeed; }
                }
                else if (dz < -2f && Mathf.Abs(latDiff) < 2.6f)
                {
                    float gap = -dz - 4.5f;
                    if (gap < behindGap) { behindGap = Mathf.Max(0.1f, gap); behindCar = e.car; behindSpeed = oSpeed; }
                }
                // Sem ultrapassar sob bandeira: o mais próximo À FRENTE em qualquer lateral — fora quem tem problema óbvio (mesma
                // regra da direção de prova: contorna-se) e quem me deve uma posição (ele tem de me deixar passar).
                bool obvious = stationary || e.car.SpeedKmh < (scOut ? 18f : 40f);
                if (dz > 0f && dz - 4.5f < nAheadGap && !obvious && !RaceX.Core.SafetyCar.Unlapping(e.car)
                    && RaceX.Core.RaceControl.GiveBackTo(e.car) != car)
                { nAheadGap = dz - 4.5f; nAheadCar = e.car; nAheadSpeed = oSpeed; }
                // Logo atrás (até 30 m): para não fechar ninguém na troca de faixa.
                if (dz < -1f && rearN < RearMax) { rearDz[rearN] = dz; rearLat[rearN] = oLat; rearV[rearN] = oSpeed; rearN++; }
            }
            // SAFETY CAR na pista: entra no radar como carro à frente (velocidade real dele) — sem isso a IA que
            // desdobra (ou o carro logo atrás) não o enxergava.
            if (RaceX.Core.SafetyCar.OnTrack && RaceX.Core.SafetyCar.Car != null)
            {
                var scT = RaceX.Core.SafetyCar.Car.transform;
                float dzs = Mathf.Repeat(RaceX.Core.SafetyCar.TrackDistance - along + L * 0.5f, L) - L * 0.5f;
                if (dzs > -30f && dzs < 75f)
                {
                    var ps = spline.PoseAtDistance(along + dzs);
                    float sLat = Vector3.Dot(scT.position - ps.position, ps.rotation * Vector3.right);
                    float latDiffS = sLat - myLat;
                    // SC saindo do traçado para recolher (entrada do box): não é mais tráfego da pista — sem isso o
                    // líder atrás dele o tratava como carro à frente fora da pista e podia ir na direção da mureta.
                    if (Mathf.Abs(sLat) > 7f) return;
                    if (RaceX.Core.SafetyCar.Car == passCar) { passFound = true; passDz = dzs; passLat = sLat; } // ultrapassando o SC (desdobramento)
                    if (Mathf.Abs(dzs) < 5.5f)
                    {
                        if (latDiffS > 0f && latDiffS < 4.5f) rightLimit = Mathf.Min(rightLimit, sLat - 2.3f);
                        else if (latDiffS <= 0f && latDiffS > -4.5f) leftLimit = Mathf.Max(leftLimit, sLat + 2.3f);
                    }
                    if (dzs > 2f && Mathf.Abs(latDiffS) < 2.1f && dzs - 4.5f < aheadGap)
                    {
                        aheadGap = Mathf.Max(0.1f, dzs - 4.5f); aheadCar = RaceX.Core.SafetyCar.Car; aheadLat = sLat; aheadSpeed = RaceX.Core.SafetyCar.Speed;
                    }
                }
            }
        }

        /// <summary>Vantagem do MEU carro sobre outro (-1..1): potência/peso (torque/massa) e velocidade final.
        /// Carro mais potente tenta mais cedo; o mais fraco só espera a oportunidade.</summary>
        float CarEdge(CarController other)
        {
            if (other == null || other.spec == null || car.spec == null) return 0f;
            float mine = car.spec.maxTorqueNm / Mathf.Max(300f, car.spec.massKg);
            float theirs = other.spec.maxTorqueNm / Mathf.Max(300f, other.spec.massKg);
            float pw = (mine - theirs) / Mathf.Max(0.01f, theirs);
            float vmax = (car.spec.topSpeedKmh - other.spec.topSpeedKmh) / Mathf.Max(50f, other.spec.topSpeedKmh);
            return Mathf.Clamp(pw * 2f + vmax * 3f, -1f, 1f);
        }

        /// <summary>Lado da ultrapassagem por PONTUAÇÃO: espaço livre de cada lado (asfalto e carros ao lado, e a
        /// porta que o carro da frente deixa aberta), lado de DENTRO da próxima curva (vale mais quanto mais perto),
        /// o lado em que já estou (menos volante, sai do vácuo) e uma preferência fixa por piloto. Antes, com os dois
        /// lados livres, ia SEMPRE por dentro da curva 60 m à frente — todos tentavam pelo mesmo lado, mesmo quando o
        /// outro estava bem mais aberto. 0 = nenhum lado cabe.</summary>
        float ChoosePassSide(float along, float myLat, float edge)
        {
            float off = PassOffset(aheadCar);   // carro parado/lento: precisa de mais espaço do lado
            float rLat = aheadLat + off, lLat = aheadLat - off;
            float freeR = Mathf.Min(edge + 0.3f, rightLimit + 0.2f) - rLat; // >= 0: cabe
            float freeL = lLat - Mathf.Max(-edge - 0.3f, leftLimit - 0.2f);
            bool rRoom = freeR >= 0f, lRoom = freeL >= 0f;
            if (!rRoom && !lRoom) return 0f;
            if (rRoom != lRoom) return rRoom ? 1f : -1f;
            float scoreR = Mathf.Min(freeR, 4f), scoreL = Mathf.Min(freeL, 4f);
            // Curva mais próxima à frente (40/90/150 m): dentro dela é o lado bom na freada.
            float[] look = { 40f, 90f, 150f };
            float[] weight = { 2.4f, 1.6f, 0.8f };
            for (int i = 0; i < look.Length; i++)
            {
                float turn = SignedCurvature(waypoints.Spline, along + look[i]);
                if (Mathf.Abs(turn) <= 0.002f) continue;
                float w = weight[i] * Mathf.Clamp01(Mathf.Abs(turn) / 0.02f);
                if (turn > 0f) scoreR += w; else scoreL += w;
                break;
            }
            // Já estou desse lado do carro da frente: menos volante para sair do vácuo.
            if (myLat > aheadLat + 0.4f) scoreR += 0.8f; else if (myLat < aheadLat - 0.4f) scoreL += 0.8f;
            // Estilo do piloto (fixo pelo nome): desempata sem todos irem para o mesmo lado.
            float bias = ((car.name.GetHashCode() & 0xff) / 255f - 0.5f) * 0.6f;
            scoreR += bias;
            return scoreR >= scoreL ? 1f : -1f;
        }

        /// <summary>Saída de pista/derrapagem SEM contato = erro de velocidade → a memória baixa o teto do
        /// trecho. Passagem limpa no teto sobe o limite aos poucos.</summary>
        void LearnFromTrack(float along, float speed, float L, bool offTrack)
        {
            int trackId = TrackId;
            if (trackId < 0 || car.pitControlled) { wasOffTrack = offTrack; ownLapDirty = true; return; }
            bool contact = Time.time - lastContactTime < 1.5f;
            var rb = car.Body;
            float beta = 0f;
            if (rb != null)
            {
                Vector3 lv = transform.InverseTransformDirection(rb.linearVelocity);
                beta = Mathf.Abs(Mathf.Atan2(lv.x, Mathf.Max(0.5f, lv.z)) * Mathf.Rad2Deg);
            }
            bool bigSlide = beta > 14f && speed > 20f;
            // Só aprende o limite em condição NORMAL: pista seca, pneu sem furo e na temperatura, carro sem dano relevante e
            // sem neutralização. O teto é por trecho e vale no seco — erro na chuva/com furo/pneu frio/carro batido não é
            // "limite da curva" e antes derrubava o teto para sempre.
            bool normal = WeatherSystem.Wetness < 0.05f && !RaceX.Core.PitLaneSystem.HasPuncture(car)
                          && RaceX.Core.PitLaneSystem.TireTemp(car) > 0.85f
                          && (car.damage == null || car.damage.MaxLevel < 0.25f)
                          && !RaceX.Core.RaceControl.Neutralized(car) && mergeTimer <= 0f;
            if (((offTrack && !wasOffTrack) || bigSlide) && normal && !contact && speed > 15f && Time.time - unlockTime > 12f)
            {
                AiTrackMemory.Incident(AiTrackMemory.MemoryModel(car.spec), trackId, along, L, speed);
                cleanSampleTimer = 3f; // não "aprende limpo" logo depois do erro
                // Autoteste: onde e como a IA errou (build 16: 27 incidentes em dois surtos sem registro de lugar).
                if (RaceX.Core.AutoTest.Active)
                    RaceX.Core.AutoTest.Log("INCIDENTE " + name + " along=" + along.ToString("0") + " v=" + (speed * 3.6f).ToString("0") + "km/h "
                        + (offTrack ? "saiu" : "derrapou(beta=" + beta.ToString("0") + ")") + " motivoLat=" + LatReasonNames[Mathf.Clamp(latReason, 0, LatReasonNames.Length - 1)]
                        + " frente=" + (aheadCar != null ? aheadCar.name : "-") + " pneu=" + RaceX.Core.PitLaneSystem.TireTemp(car).ToString("0.00")
                        + " esterco=" + car.SteerAngle.ToString("0.0") + " fslip=" + car.FrontSideSlip.ToString("0.00") + " rslip=" + car.RearSideSlip.ToString("0.00"));
            }
            else if (!offTrack && beta < 5f)
            {
                cleanSampleTimer -= thinkDt;
                if (cleanSampleTimer <= 0f) { cleanSampleTimer = 0.5f; AiTrackMemory.Clean(AiTrackMemory.MemoryModel(car.spec), trackId, along, L, speed); }
            }
            wasOffTrack = offTrack;
            // Melhor volta própria: velocidade por trecho; volta com saída, toque, derrapada forte, box ou fora do "normal" não vale.
            int sgOwn = PlayerPaceLearner.Seg(along, L);
            // v100: 720 trechos — preenche os trechos pulados desde a última pensada (10 Hz: até ~7 m por passo em alta).
            int nOwn = PlayerPaceLearner.Segments, fromOwn = sgOwn;
            if (ownPrevSeg >= 0) { int gap = (sgOwn - ownPrevSeg + nOwn) % nOwn; if (gap > 1 && gap <= 40) fromOwn = (ownPrevSeg + 1) % nOwn; }
            for (int sg = fromOwn; ; sg = (sg + 1) % nOwn) { if (speed > ownLap[sg]) ownLap[sg] = speed; if (sg == sgOwn) break; }
            ownPrevSeg = sgOwn;
            if (offTrack || contact || bigSlide || !normal) ownLapDirty = true;
        }

        /// <summary>Pico da curva de atrito LATERAL das rodas (CarController.baseSideways.extremumValue).</summary>
        const float SidewaysPeak = 1f;

        /// <summary>µ lateral REAL do carro: o mesmo que o CarController aplica nas rodas (rigidez lateral × pico da curva de
        /// atrito). v99: antes a IA usava 1,35 + (rigidez − 1,6)·0,3 — 1,74 num fórmula de rigidez 2,9 e 1,35 num GT de 1,6:
        /// freava em curva que o carro fazia muito mais rápido (medido 03/11, Circuito Verde, raio 44 m: IA 105 km/h, física
        /// 178, jogador 180). O carro da IA sempre foi igual ao do jogador; só a CONTA dela estava abaixo.</summary>
        static float RealLateralMu(CarSpec s) => s != null ? s.sidewaysStiffness * SidewaysPeak : 1.6f;

        /// <summary>µ efetivo: aderência lateral real × pneu atual (composto, desgaste, temperatura e ÁGUA via
        /// PitLaneSystem.TireGrip). Faixa larga (0,3–1,08) para a chuva reduzir de verdade.</summary>
        float MuEffective()
        {
            float mu = RealLateralMu(car != null ? car.spec : null);
            float wetMul = Mathf.Lerp(1f, WetSkill, Mathf.Clamp01(WeatherSystem.Wetness * 1.4f)); // piloto bom de chuva segura mais
            return mu * wetMul * Mathf.Clamp(PitLaneSystem.TireGrip(car), 0.3f, 1.08f);
        }

        /// <summary>Freada PLANEJADA sem dado do jogador, na velocidade vRef (m/s): 70% do que o pneu transmite pela física do
        /// carro — atrito LONGITUDINAL × (peso + apoio aerodinâmico em vRef) —, no máximo BrakePlanMax. v99: o ABS passou a
        /// segurar a roda perto do pico (CarController.ApplyBrakes); com a roda travando o carro freava ~1 g.</summary>
        float BrakePlanDecel(float vRef)
        {
            var s = car != null ? car.spec : null;
            float muLong = (s != null ? s.forwardStiffness : 1.5f) * Mathf.Clamp(PitLaneSystem.TireGrip(car), 0.3f, 1.08f);
            float m = car != null && car.Body != null ? car.Body.mass : (s != null ? s.massKg : 1000f);
            if (s == null) return Mathf.Min(muLong * 9.81f * 0.7f, BrakePlanMax);
            // v100: mesma lei do pneu do CarController (perda pela carga + teto de freada da categoria)
            return Mathf.Min(s.GripAccel(muLong, vRef, m, 1f, s.brakeCapG) * BrakePlanShare, BrakePlanMax);
        }

        /// <summary>Teto da freada planejada (m/s²): 90% da freada MEDIDA com o pedal no fundo acima de 100 km/h depois do ABS
        /// novo (autoteste 03/11, F2: mediana 12,3 m/s², p90 15,6). Planejar acima do que o carro entrega = chegar rápido na
        /// curva (antes: plano 12,5 com o carro entregando 9,7).</summary>
        const float BrakePlanMax = 12.5f;
        /// <summary>v100 (04/11): 80% da física do pneu novo e teto 12,5 m/s² — freada MEDIDA com o pedal no fundo acima de 100 km/h
        /// nos 3 treinos de 04/11 (F2: medianas 12,3 / 12,7 / 12,3, p90 15,0–15,5). Antes 70% e teto 11: a IA freava ~15% antes
        /// do ponto (pedido do usuário: "está freando muito antes, em pontos que ainda é reta").</summary>
        const float BrakePlanShare = 0.8f;

        /// <summary>v² do limite de curva pela MESMA física do CarController: m·v²·k = µ·(m·g + apoio·v²) — todo o apoio
        /// empurra os pneus (ApplyDownforce aplica 100% de downforceCoefficient·v²). Denominador ≤ 0 = o apoio cresce mais
        /// rápido que a força centrífuga: a curva é de pé embaixo e quem limita é o ESTERÇO (SteerLimitSpeed) e a velocidade
        /// final. v99: antes valiam só 55% do apoio, com o ganho limitado a 1,8× (estimativas de projeto).</summary>
        static float CornerV2(float mu, float k, CarSpec s, float massKg = 0f, float aeroShare = 1f)
        {
            float kk = Mathf.Max(k, 1e-4f);
            float denom = kk;
            // massa REAL quando conhecida (com combustível — CarSystems): tanque cheio = apoio aerodinâmico vale menos
            float m = massKg > 50f ? massKg : (s != null ? s.massKg : 0f);
            if (s != null && (s.tyreLoadSensitivity > 0f || s.lateralCapG > 0f) && m > 50f)
            {
                // v100: pneu com perda pela carga (CarSpec.GripAccel, a MESMA lei das rodas). Sem forma fechada: busca a maior
                // velocidade em que k·v² ≤ aderência(v). Se ainda sobra aderência a 120 m/s, a curva não limita.
                const float VMaxSearch = 120f;
                if (kk * VMaxSearch * VMaxSearch <= s.GripAccel(mu, VMaxSearch, m, aeroShare, s.lateralCapG)) return float.MaxValue;
                float lo = 0f, hi = VMaxSearch;
                for (int i = 0; i < 16; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (kk * mid * mid <= s.GripAccel(mu, mid, m, aeroShare, s.lateralCapG)) lo = mid; else hi = mid;
                }
                return lo * lo;
            }
            if (s != null && s.downforceCoefficient > 0.01f && m > 50f) denom = kk - mu * s.downforceCoefficient * aeroShare / m;
            return denom > 1e-6f ? mu * 9.81f / denom : float.MaxValue;
        }

        /// <summary>Parcela do apoio aerodinâmico que a IA CONTA ao planejar sem dado medido (03/11, calibrado pelo autoteste): com
        /// 100% a fórmula prometia ~3,7 g a 175 km/h na última curva do Circuito Verde e o carro não entrega — o próprio piloto
        /// automático sai da curva de +5,5 m para −2,4 m em 60 m, e os NPCs vão a −7 m e batem no bico da mureta (4 perdas totais
        /// em 12 min). Com 60%: ~150 km/h no mesmo ponto. Dado MEDIDO (volta do jogador, melhor volta própria) pode passar disto,
        /// até o limite com 100% do apoio.</summary>
        // 03/11, medido no treino de 12 min: 0,6 = 0 PT, 20 batidas, 6 incidentes, 46,9 s; 0,8 = 2 PT (pelotão da saída + bico), 44
        // batidas, 15 incidentes, 46,1 s. O usuário escolheu 0,7 (meio-termo).
        // v100 (04/11, pedido do usuário: "freia em curva onde era só tirar o pé"): a conta da IA passou a ser a MESMA lei do pneu
        // do carro (perda pela carga + tetos, CarSpec.GripAccel) — a muleta de 70% do apoio era para a fórmula antiga, que
        // superestimava o pneu de 2,3. Com ela, a IA freava para curvas médias que o carro faz de pé. Volta a 100%; o teto de
        // uso (AiMaxUsable 0,92) continua sendo a margem.
        const float AeroShareAI = 1.0f;

        /// <summary>Ângulo máximo de esterço numa velocidade (mesma regra do CarController.ApplySteering — o volante retangular
        /// da F2 cai para 23% a 180 km/h).</summary>
        static float MaxSteerAt(CarSpec s, float kmh)
        {
            float maxAngle = s.maxSteerAngle * Mathf.Lerp(1f, s.steerAtTopSpeed, Mathf.Clamp01(kmh / Mathf.Max(1f, s.topSpeedKmh)));
            if (s.rectangularSteeringWheel)
            {
                float sf = Mathf.Clamp01(kmh / 180f);
                maxAngle = s.maxSteerAngle * Mathf.Lerp(1f, 0.23f, Mathf.Pow(sf, 0.8f));
            }
            return Mathf.Max(2f, maxAngle);
        }

        /// <summary>Maior velocidade (m/s) em que o esterço disponível ainda faz a curvatura k (bicicleta: k ≈ tan δ / entre-eixos).
        /// Com o apoio real, curva rápida quase não tem limite de aderência: o que segura é o volante, igual para o jogador.</summary>
        static float SteerLimitSpeed(CarSpec s, float k)
        {
            float top = s.topSpeedKmh / 3.6f;
            float wb = Mathf.Max(1.5f, s.wheelbase);
            if (Mathf.Tan(MaxSteerAt(s, s.topSpeedKmh) * Mathf.Deg2Rad) / wb >= k) return top;
            float lo = 0f, hi = top;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Mathf.Tan(MaxSteerAt(s, mid * 3.6f) * Mathf.Deg2Rad) / wb >= k) lo = mid; else hi = mid;
            }
            return lo;
        }

        float PhysicalCornerLimit(float k, float aeroShare = AeroShareAI)
        {
            var s = car != null ? car.spec : null;
            float v = Mathf.Sqrt(CornerV2(MuEffective(), k, s, car != null && car.Body != null ? car.Body.mass : 0f, aeroShare));
            if (s != null) v = Mathf.Min(v, Mathf.Min(s.topSpeedKmh / 3.6f, SteerLimitSpeed(s, k)));
            float vMeasured = MeasuredVMax(s != null ? s.model : CarModel.Falcon, 1f / Mathf.Max(k, 1e-4f));
            if (vMeasured > 0f) v = Mathf.Min(v, vMeasured);
            return v;
        }

        float CornerSpeedAhead(float along, float speed, int d)
        {
            float decel = BrakePlanDecel(0f);   // sem apoio: horizonte mais longo (o plano de cada curva usa a velocidade dela)
            float horizon = Mathf.Max(60f, speed * speed / (2f * Mathf.Max(4f, decel)) + 30f);
            float best = float.MaxValue;
            var spline = waypoints.Spline;
            int trackId = TrackId;
            float L = spline.Length;
            // Arco em C por DENTRO: raio ~InsideLineShare·edge menor que o do centro da pista — o limite usa a curvatura desse arco
            // (k/(1 − k·d)); antes o traçado fora-dentro-fora abria o raio e a conta pelo centro sobrava.
            float edgeCS = Mathf.Max(1.5f, RoadHalf() - 1.25f);
            float insideOff = edgeCS * InsideLineShare * (d >= 2 ? 1f : d == 1 ? 0.7f : 0.4f);
            // v99: no TRAÇADO IDEAL (sem volta do jogador e a menos de 1,2 m da linha) o limite usa a curvatura DA LINHA. Fora dela
            // (ultrapassando, defendendo, cedendo, na linha do jogador) usa a do centro apertada pela lateral em que o carro ESTÁ do
            // lado de dentro (k/(1 − k·d)) — o arco fixo de 0,45·borda subestimava a curva de quem mergulha por dentro.
            float lineEdge = LineEdge();
            float lineNow = style == null ? RacingLine.LatAt(spline, lineEdge, along) : float.NaN;
            // "Na linha" pela posição REAL do carro (não pela intenção): escorregando para fora ele deixa de estar na linha.
            bool onLine = !float.IsNaN(lineNow) && Mathf.Abs(lastMyLat - lineNow) < 1.2f;
            float top = car.spec != null ? car.spec.topSpeedKmh / 3.6f : 80f;
            for (float s = 4f; s <= horizon; s += 3.5f)
            {
                float ks = SignedCurvature(spline, along + s);   // + = curva à direita (lado de dentro = lateral +)
                float kC = Mathf.Abs(ks);
                float inward = Mathf.Max(insideOff, ks >= 0f ? committedLateral : -committedLateral);
                if (kC >= 1e-4f) kC /= Mathf.Max(0.5f, 1f - kC * inward);
                float k = kC;
                float vPhys = kC >= 1e-4f ? PhysicalCornerLimit(kC) : top;
                if (onLine)
                {
                    float kL = RacingLine.CurvatureAt(spline, lineEdge, along + s);
                    if (kL >= 0f)
                    {
                        float vL = kL >= 1e-4f ? PhysicalCornerLimit(kL) : top;
                        vPhys = Mathf.Min(vL, vPhys * LineGain);
                        k = Mathf.Max(kL, kC);
                    }
                }
                float vCorner = float.MaxValue;
                if (k >= 1e-4f)
                {
                    float vPhysFull = PhysicalCornerLimit(k, 1f);   // teto para dado MEDIDO (jogador / melhor volta própria)
                    vCorner = vPhys * DiffVMaxFraction[d] * FractionOverride * skill;
                    vCorner = Mathf.Min(vCorner, vPhys * AiMaxUsable);
                    bool playerRef = false;
                    if (d >= 2 && trackId >= 0 && paceCap == null)
                    {
                        // v100 (pedido do usuário): com volta limpa do jogador, ela É o alvo — playerPaceMul × a velocidade dele no
                        // trecho (Expert 1,01–1,10, Difícil 1,00–1,05), para cima e para baixo, nunca acima do limite físico.
                        float playerCleanV = PlayerSpeedAt(along + s, L);
                        // Teto = o uso normal da IA (92% do limite pela conta dela): com 100% (v100b) a dianteira estourava nas curvas
                        // lentas (fslip 0,30–0,33 > pico 0,27) — 4–5 perdas totais e 200+ batidas por treino.
                        // Onde a física "não limita" (vPhys = velocidade final: retas e SAÍDAS de curva) o fator é no máximo 1,0 — a
                        // velocidade de saída gravada já é a que o carro aguenta; com 1,03–1,09 ali os carros saíam largos da última
                        // curva e 3 bateram no bico da mureta (treino v100g com estilo, 04/11). A vantagem fica onde a curva limita.
                        float mult = vPhys >= top * 0.999f ? Mathf.Min(playerPaceMul, 1f) : playerPaceMul;
                        // Onde a LINHA dele foi cortada para dentro (ele passou além do limite que a IA mira), a curva da IA é mais
                        // fechada que a dele: a velocidade dele ali não vale com fator > 1 — e perde 1,5% (corrida de 04/11, PT em série).
                        if (style != null)
                        {
                            float pl = PlayerStyle.LatAt(style, along + s, L);
                            if (!float.IsNaN(pl) && Mathf.Abs(pl) > playerLineEdge + 0.2f) mult = Mathf.Min(mult, 1f) * 0.985f;
                        }
                        if (playerCleanV > 5f) { vCorner = Mathf.Min(playerCleanV * mult, vPhys * AiMaxUsable); playerRef = true; }
                    }
                    // Melhor volta PRÓPRIA (limpa): onde ele mesmo já passou mais rápido, pode repetir — até 98,5% do limite físico.
                    // Com a volta do jogador como alvo, ela não passa da faixa pedida.
                    if (ownRef != null && paceCap == null && !playerRef)
                    {
                        float ov = ownRef[PlayerPaceLearner.Seg(along + s, L)];
                        // Só até o teto de uso normal (AiMaxUsable): acima dele a volta "limpa" era sorte — com 98,5% o treino de 12 min
                        // teve 7 perdas totais (antes 3).
                        if (ov > 5f) vCorner = Mathf.Max(vCorner, Mathf.Min(ov * 0.995f, vPhysFull * AiMaxUsable));
                    }
                }
                float cap = AiTrackMemory.Cap(AiTrackMemory.MemoryModel(car.spec), trackId, along + s, L);
                // Piso do teto aprendido: 80% da física; onde a fórmula diz "sem limite" (apoio vence a curva: vPhys = velocidade final)
                // o piso cai a 40% — senão o erro no bico da mureta (saída da última curva) nunca ensinava nada (piso 268 km/h).
                float floorShare = vPhys >= top * 0.999f ? AiTrackMemory.MinShareUnlimited : AiTrackMemory.MinShareOfPhysics;
                if (cap < float.MaxValue) vCorner = Mathf.Min(vCorner, Mathf.Max(cap * (0.97f + 0.03f * Mathf.Clamp01(skill)), vPhys * floorShare));
                // Online: carro de UM jogador no automático (menu aberto / caiu da conexão) nunca anda mais rápido do que ELE
                // naquele trecho — curva e reta.
                if (paceCap != null)
                {
                    float pc = paceCap[PlayerPaceLearner.Seg(along + s, L)];
                    if (pc > 5f) vCorner = Mathf.Min(vCorner, Mathf.Min(pc, vPhys));
                }
                if (vCorner >= float.MaxValue) continue;
                // Freada até a curva planejada com o apoio na velocidade DA CURVA (a menor do trecho: conta conservadora).
                float vAllowed = Mathf.Sqrt(vCorner * vCorner + 2f * StyleDecel(along + s, L, BrakePlanDecel(Mathf.Min(vCorner, speed))) * Mathf.Max(0f, s - 4f));
                best = Mathf.Min(best, vAllowed);
            }
            float result = best < float.MaxValue ? best : (car != null && car.spec != null ? car.spec.topSpeedKmh / 3.6f : 80f);
            CornerSpeedAheadCached = result;
            return result;
        }

        public static float IdealSpeedAt(TrackSpline spline, float distance, CarController refCar, int trackId = 0)
        {
            float k = Curvature(spline, distance);
            float top = refCar != null && refCar.spec != null ? refCar.spec.topSpeedKmh / 3.6f : 80f;
            float v = top;
            if (k >= 1e-4f)
            {
                var s = refCar != null ? refCar.spec : null;
                v = Mathf.Min(Mathf.Sqrt(CornerV2(RealLateralMu(s), k, s, refCar != null && refCar.Body != null ? refCar.Body.mass : 0f)), top);
                if (s != null) v = Mathf.Min(v, SteerLimitSpeed(s, k));
            }
            if (refCar != null && refCar.spec != null)
            {
                float cap = AiTrackMemory.Cap(AiTrackMemory.MemoryModel(refCar.spec), trackId, distance, spline.Length);
                if (cap < float.MaxValue) v = Mathf.Min(v, Mathf.Max(cap, v * AiTrackMemory.MinShareOfPhysics));
            }
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

        /// <summary>Carro PARADO ou quase (destruído, fora da corrida, abaixo de 5 m/s): obstáculo que se contorna. O SC nunca.</summary>
        bool IsStationary(CarController c)
        {
            if (c == null || RaceX.Core.SafetyCar.Is(c)) return false;
            return c.wrecked || IsRetired(c) || Mathf.Max(0f, c.ForwardSpeed) < SlowCarSpeed;
        }

        /// <summary>Ir para a lateral toLat fecharia alguém que vem ATRÁS (até 30 m), já naquela faixa e chegando (ou a menos de
        /// 6 m)? Quem já está atrás de mim, na minha faixa, não conta.</summary>
        bool CutsOff(float toLat, float myLat, float speed)
        {
            for (int i = 0; i < rearN; i++)
            {
                if (Mathf.Abs(rearLat[i] - toLat) > 2.4f) continue;   // não está na faixa de destino
                if (Mathf.Abs(rearLat[i] - myLat) < 1.2f) continue;   // já está atrás de mim
                float gap = -rearDz[i] - 4.5f;
                float closing = rearV[i] - speed;
                if (gap < 6f || (closing > 0.5f && gap / closing < 2f)) return true;
            }
            return false;
        }

        /// <summary>Encerra a tentativa de ultrapassagem/contorno e lembra contra quem e em que reta (não tenta de novo na mesma).</summary>
        void EndPass()
        {
            if (passCar != null) { lastPassCar = passCar; lastPassSide = passSide; lastPassEndT = Time.time; lastPassStraight = straightId; }
            passCar = null; passSide = 0f; passTimer = 0f; cornerAttackActive = false;
        }

        /// <summary>VÃO LIVRE para contornar carro(s) PARADO(S) (destruído, abandonou, rodado/parado) a obsDz metros à frente:
        /// junta TODOS os carros parados entre obsDz−8 e obsDz+12 m com a pegada real (atravessado ocupa mais) e devolve a
        /// lateral livre mais perto da minha. Primeiro só no asfalto; sem vão, aceita dois pneus na zebra (como o respawn).
        /// NaN = pista fechada (espera os fiscais retirarem o carro). Antes o desvio era fixo (3 m do centro do carro parado) e
        /// nunca passava de 1,25 m da borda: a fila do SC ficou 10 s parada atrás de dois destroços (teste de 02/11).</summary>
        float ObstacleGapLat(float along, float obsDz, float myLat, float half)
        {
            if (race == null || race.entries == null) return float.NaN;
            var me = race.GetEntry(car);
            if (me == null) return float.NaN;
            var spline = waypoints.Spline;
            float L = spline.Length;
            float myHalfW = car.spec != null ? car.spec.bodySize.x * 0.5f : 0.95f;
            int n = 0;
            for (int i = 0; i < race.entries.Count && n < gapLo.Length; i++)
            {
                var e = race.entries[i];
                if (e == null || e.car == null || e.car == car || !e.car.gameObject.activeInHierarchy) continue;
                if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) continue;
                if (!(e.car.wrecked || e.retired || Mathf.Max(0f, e.car.ForwardSpeed) < SlowCarSpeed)) continue;
                float dz = Mathf.Repeat(e.raceDistance - me.raceDistance + L * 0.5f, L) - L * 0.5f;
                if (dz < obsDz - 8f || dz > obsDz + 12f) continue;
                var p = spline.PoseAtDistance(along + dz);
                Vector3 rgt = p.rotation * Vector3.right;
                float oLat = Vector3.Dot(e.car.transform.position - p.position, rgt);
                var os = e.car.spec;
                float hl = os != null ? os.bodySize.z * 0.5f : 2.4f, hw = os != null ? os.bodySize.x * 0.5f : 0.95f;
                float ext = Mathf.Abs(Vector3.Dot(rgt, e.car.transform.forward)) * hl + Mathf.Abs(Vector3.Dot(rgt, e.car.transform.right)) * hw;
                float clear = ext + myHalfW + 0.45f;
                gapLo[n] = oLat - clear; gapHi[n] = oLat + clear; n++;
            }
            gapN = n;
            // Pista FECHADA de borda a borda (dois carros atravessados): usa a área de escape se ela tiver pelo menos 2,5 m — sair da
            // pista para evitar o acidente é permitido (a direção de prova não conta como limite de pista). Antes a fila esperava os
            // fiscais retirarem o carro.
            var def = race.Track != null ? race.Track.Definition : null;
            float runoff = def != null ? def.runoffWidth : 0f;
            float limAsphalt = half - 0.7f;                                                   // centro do carro no asfalto
            float limMax = runoff >= 2.5f ? half + Mathf.Min(runoff - 1.2f, 2.5f) : half + 0.5f;   // área de escape / zebra
            // Junta os trechos ocupados que se sobrepõem (em ordem): um trecho juntado é uma "parede" contínua de carros parados.
            int m = 0;
            for (int i = 0; i < n; i++) { mergeLo[i] = gapLo[i]; mergeHi[i] = gapHi[i]; }
            for (int i = 1; i < n; i++)
                for (int j = i; j > 0 && mergeLo[j] < mergeLo[j - 1]; j--)
                {
                    float tl = mergeLo[j]; mergeLo[j] = mergeLo[j - 1]; mergeLo[j - 1] = tl;
                    float th = mergeHi[j]; mergeHi[j] = mergeHi[j - 1]; mergeHi[j - 1] = th;
                }
            for (int i = 0; i < n; i++)
            {
                if (m > 0 && mergeLo[i] <= mergeHi[m - 1]) mergeHi[m - 1] = Mathf.Max(mergeHi[m - 1], mergeHi[i]);
                else { mergeLo[m] = mergeLo[i]; mergeHi[m] = mergeHi[i]; m++; }
            }
            int inside = -1;
            for (int k = 0; k < m; k++) if (myLat > mergeLo[k] && myLat < mergeHi[k]) { inside = k; break; }
            if (inside < 0) return Mathf.Clamp(myLat, -limMax, limMax);   // já estou num vão: sigo reto
            // Sai do trecho em que estou pelo lado ALCANÇÁVEL mais perto — o vão escolhido nunca exige passar por cima de outro carro
            // parado (antes podia escolher a fresta do outro lado de um carro atravessado: a fila parou atrás dele, autoteste v96).
            // Fora do asfalto custa um pouco mais (prefere a pista), sem chegar a cruzar um destroço por isso.
            float left = mergeLo[inside] - 0.05f, right = mergeHi[inside] + 0.05f;
            float best = float.NaN, bestCost = float.MaxValue;
            if (left >= -limMax) { float c = (myLat - left) + (left < -limAsphalt ? 2.5f : 0f); if (c < bestCost) { bestCost = c; best = left; } }
            if (right <= limMax) { float c = (right - myLat) + (right > limAsphalt ? 2.5f : 0f); if (c < bestCost) { bestCost = c; best = right; } }
            return best;
        }
        readonly float[] mergeLo = new float[12], mergeHi = new float[12];

        /// <summary>Quanto a curva (curvatura k) exige do carro: 0 = feita de pé embaixo (limite físico ≥ 95% da velocidade
        /// final), 1 = curva de verdade (limite ≤ 80%). Valores de projeto.</summary>
        float CornerSeverity(float k, float vTop)
        {
            if (k < 1e-4f || vTop < 1f) return 0f;
            return Mathf.Clamp01((0.95f * vTop - PhysicalCornerLimit(k)) / (0.15f * vTop));
        }

        /// <summary>Autoteste: conta cada REVERSÃO da lateral (≥ 0,5 m) em reta — o zigue-zague — e a distância andada em reta.</summary>
        void ZigzagSample(float myLat, bool zone, float speed)
        {
            if (!zone) { zigInit = false; zigDir = 0; return; }
            if (isHumanCar) ZigDistPlayer += speed * thinkDt; else ZigDistNpc += speed * thinkDt;
            if (!zigInit) { zigInit = true; zigDir = 0; zigMin = zigMax = myLat; return; }
            if (zigDir == 0)
            {
                zigMin = Mathf.Min(zigMin, myLat); zigMax = Mathf.Max(zigMax, myLat);
                if (myLat - zigMin > ZigAmp) { zigDir = 1; zigExt = myLat; }
                else if (zigMax - myLat > ZigAmp) { zigDir = -1; zigExt = myLat; }
                return;
            }
            bool reversed = false;
            if (zigDir > 0) { if (myLat > zigExt) zigExt = myLat; else if (zigExt - myLat > ZigAmp) reversed = true; }
            else { if (myLat < zigExt) zigExt = myLat; else if (myLat - zigExt > ZigAmp) reversed = true; }
            if (!reversed) return;
            zigDir = -zigDir; zigExt = myLat;
            if (isHumanCar) ZigPlayer++; else ZigNpc++;
            if (latReason >= 0 && latReason < ZigByReason.Length) ZigByReason[latReason]++;
        }

        int gapN;   // trechos ocupados na última busca de vão (diagnóstico)
        float contourDiagT = -99f;

        /// <summary>Autoteste: cena do CONTORNO de carro parado (a cada 2 s; IA só quando está quase parada): vão escolhido, trechos
        /// laterais ocupados pelos carros parados, limites dos carros ao lado — para achar por que um carro não passa um destroço.</summary>
        void ContourDiag(float myLat, float gl, float speed)
        {
            if (!RaceX.Core.AutoTest.Active || Time.time - contourDiagT < 2f) return;
            if (!isHumanCar && speed > 2f) return;
            contourDiagT = Time.time;
            var sb = new System.Text.StringBuilder(200);
            sb.Append("CONTORNO ").Append(name).Append(" v=").Append(speed.ToString("0.0")).Append(" lat=").Append(myLat.ToString("0.0"))
              .Append(" vao=").Append(float.IsNaN(gl) ? "NENHUM" : gl.ToString("0.0")).Append(" obst=").Append(passCar != null ? passCar.name : "-")
              .Append(" gap=").Append(aheadCar != null ? aheadGap.ToString("0.0") : "-").Append(" frente=").Append(aheadCar != null ? aheadCar.name : "-")
              .Append(" limites=").Append(leftLimit.ToString("0.0")).Append('/').Append(rightLimit.ToString("0.0")).Append(" ocupado=");
            for (int k = 0; k < gapN; k++) sb.Append('[').Append(gapLo[k].ToString("0.0")).Append(';').Append(gapHi[k].ToString("0.0")).Append(']');
            RaceX.Core.AutoTest.Log(sb.ToString());
        }

        /// <summary>Autoteste: estado do tráfego deste carro numa linha (por que está parado).</summary>
        public string Diag()
        {
            return "frente=" + (aheadCar != null ? aheadCar.name : "-") + " gap=" + (aheadCar != null ? aheadGap.ToString("0.0") : "-")
                + " vFrente=" + aheadSpeed.ToString("0.0") + " passe=" + passSide + " motivoLat=" + LatReasonNames[Mathf.Clamp(latReason, 0, LatReasonNames.Length - 1)]
                + " fila=" + (queueScan ? 1 : 0) + " frenteParado=" + (aheadCar != null && IsStationary(aheadCar) ? 1 : 0)
                + " limites=" + leftLimit.ToString("0.0") + "/" + rightLimit.ToString("0.0") + " alvoLat=" + committedLateral.ToString("0.0")
                + " along=" + lastThinkAlong.ToString("0") + " lat=" + lastMyLat.ToString("0.0") + " vAlvo=" + (CornerSpeedAheadCached * 3.6f).ToString("0")
                + " jogadorV=" + (PlayerSpeedAt(lastThinkAlong, waypoints != null && waypoints.Spline != null ? waypoints.Spline.Length : 1f) * 3.6f).ToString("0") + " mult=" + playerPaceMul.ToString("0.00");
        }

        bool UpdateRecovery(float speed, ref float steer, ref float throttle, ref float brake)
        {
            // FILA DO SAFETY CAR: NUNCA dá ré (print do usuário 01/11: R a 8 km/h na fila depois da batida). Andar no sentido
            // contrário só é permitido se for indispensável para tirar o carro de posição perigosa (FIA); na fila o carro
            // espera ou contorna, e o cão de guarda por progresso faz o respawn na lateral se ficar preso.
            // EXCEÇÃO: ENCOSTADO num carro DESTRUÍDO/fora (sem espaço para esterçar e contornar) há 2 s, sem ninguém a 6 m atrás → recua
            // ~1 s devagar e contorna (FIA: andar para trás só para sair de posição perigosa). v91: carros encostados nos destroços
            // travaram a fila até os fiscais retirarem.
            if (queueScan && reverseTimer <= 0f)
            {
                bool againstWreck = aheadCar != null && (aheadCar.wrecked || IsRetired(aheadCar)) && aheadGap < 1.2f && speed < 0.5f && !CarBehind(6f);
                wreckStuckT = againstWreck ? wreckStuckT + thinkDt : 0f;
                if (wreckStuckT > 2f)
                {
                    wreckStuckT = 0f;
                    reverseTimer = 1.1f;
                    escapeTimer = 3f;
                    escapeSide = ChooseEscapeSide();
                }
                else { stuckTimer = 0f; return false; }
            }
            if (reverseTimer > 0f)
            {
                if (aheadCar != null)
                {
                    Vector3 lb = transform.InverseTransformPoint(aheadCar.transform.position);
                    if (lb.z > 6.0f && Mathf.Abs(lb.x) > 2.3f) { reverseTimer = 0f; return false; }
                }
                // RÉ CONTROLADA (engavetamento do teste 01/11: todos davam ré com acelerador cheio, batiam no muro e uns nos
                // outros e davam perda total): para se tiver carro ou muro logo atrás; no máximo ~9 km/h de ré.
                if (CarBehind(4.5f) || WallBehind(2.5f)) { reverseTimer = 0f; stuckTimer = 0f; brake = 1f; throttle = 0f; return true; }
                if (car.ForwardSpeed <= 0.35f) reverseTimer -= thinkDt;
                // Rodas para -escapeSide na ré = a FRENTE gira para +escapeSide (lado livre).
                steer = car.ForwardSpeed <= 0.35f ? -escapeSide * 0.9f : 0f;
                throttle = 0f;
                brake = -car.ForwardSpeed > 2.5f ? 0f : 0.55f;
                committedLateral = Mathf.Clamp(aheadLat + escapeSide * 2.65f, -2.65f, 2.65f);
                return true;
            }
            if (race != null && race.State != RaceState.Racing) { stuckTimer = 0f; return false; }
            bool contact = aheadCar != null && aheadGap < 2.2f && aheadSpeed < 1.0f && speed < 1.5f;
            aheadStillT = aheadCar != null && aheadSpeed < 1f && aheadGap < 6f ? aheadStillT + thinkDt : 0f;
            // DOIS PRESOS um contra o outro: só o de MENOR progresso dá ré — o outro segue em frente.
            if (contact && race != null)
            {
                var myE = race.GetEntry(car);
                var otE = race.GetEntry(aheadCar);
                if (myE != null && otE != null && otE.raceDistance < myE.raceDistance)
                {
                    stuckTimer = 0f;
                    return false;
                }
            }
            stuckTimer = speed < 2.0f && (throttle > 0f || contact) ? stuckTimer + thinkDt * (contact ? 1.6f : 1f) : 0f;
            if (speed > 5f) recoveryAttempts = 0;
            if (stuckTimer > StuckSeconds)
            {
                stuckTimer = 0f;
                // FILA PARADA (carro da frente parado mas inteiro, ou fila do SC): ESPERA, não dá ré. Também não dá ré com carro
                // logo atrás. Preso de verdade contra o muro sem ninguém atrás → ré controlada; e o cão de guarda por
                // progresso (7 s sem andar 15 m) faz o respawn na lateral se nada resolver.
                bool aheadWrecked = aheadCar != null && aheadCar.wrecked;
                bool queue = aheadCar != null && !aheadWrecked && aheadGap < 6f;
                if (CarBehind(12f) || (queue && aheadStillT < 8f) || (queueScan && !aheadWrecked)) return false;
                recoveryAttempts++;
                if (recoveryAttempts > MaxRecoveryAttempts)
                {
                    TryRespawnStuck();
                    recoveryAttempts = 0;
                    return false;
                }
                reverseTimer = Random.Range(ReverseSeconds, MaxReverseSeconds);
                escapeTimer = 5f;
                escapeSide = ChooseEscapeSide();
            }
            return false;
        }

        float aheadStillT;   // há quanto tempo o carro da frente (encostado) está parado
        float wreckStuckT;   // fila do SC: há quanto tempo encostado num carro destruído (ré curta para contornar)

        /// <summary>Algum carro logo ATRÁS de mim (até range metros, na minha faixa)?</summary>
        bool CarBehind(float range)
        {
            if (race == null) return false;
            for (int i = 0; i < race.entries.Count; i++)
            {
                var o = race.entries[i].car;
                if (o == null || o == car) continue;
                Vector3 lb = transform.InverseTransformPoint(o.transform.position);
                if (lb.z < -1f && lb.z > -range && Mathf.Abs(lb.x) < 2.8f) return true;
            }
            return false;
        }

        bool WallBehind(float range)
        {
            Vector3 origin = transform.position + Vector3.up * 0.45f - transform.forward * (car.spec != null ? car.spec.bodySize.z * 0.5f : 2.4f);
            return Physics.Raycast(origin, -transform.forward, out var hit, range) && hit.rigidbody == null && hit.collider.GetComponentInParent<RaceX.Track.TrackSurface>() == null;
        }

        /// <summary>Lado com MAIS espaço livre para a ré (raycasts ±45° do capô).</summary>
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
                return Mathf.Abs(aheadLat) > 0.5f ? (aheadLat > 0f ? -1f : 1f) : (localX >= 0f ? -1f : 1f);
            }
            return committedLateral >= 0f ? -1f : 1f;
        }

        /// <summary>Cão de guarda por PROGRESSO: sem avançar 15 m em 7 s (12 s na fila do SC) ou 3 s na contramão → respawn.
        /// true = fez respawn agora (Think encerra).</summary>
        bool ProgressWatchdog(float along, float speed)
        {
            if (race == null || race.State != RaceState.Racing || car.pitControlled || RaceX.Core.PitLaneSystem.IsInPitFlow(car))
            {
                progressRef = float.NaN; progressTimer = 0f; wrongWayTimer = 0f;
                return false;
            }
            float L = waypoints.Spline.Length;
            // Janela DEFASADA por carro (0–3 s): antes todas começavam juntas e 12 carros presos na mesma fila reapareceram no
            // MESMO quadro (teste de 02/11).
            if (float.IsNaN(progressRef)) { progressRef = along; progressTimer = -watchdogPhase; }
            progressTimer += thinkDt;
            var pose = waypoints.Spline.PoseAtDistance(along);
            float dirDot = Vector3.Dot(transform.forward, pose.rotation * Vector3.forward);
            wrongWayTimer = dirDot < -0.25f && car.SpeedKmh > 8f ? wrongWayTimer + thinkDt : 0f;
            bool wrongWay = wrongWayTimer > 3f;
            // Fila do SC: paciência maior — a fila anda devagar contornando o destroço; respawn só se travou de verdade.
            float window = queueScan ? 12f : 7f;
            if (progressTimer >= window || wrongWay)
            {
                float delta = Mathf.Repeat(along - progressRef + L * 0.5f, L) - L * 0.5f;
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

        /// <summary>Respawn do carro preso/na contramão: dois pneus na zebra, no primeiro ponto LIVRE (9 m; 5 m após 3 falhas)
        /// procurando atrás e depois À FRENTE — à frente só se não passar ninguém (respawn não pode ganhar posição: sob VSC um
        /// carro reapareceu à frente do jogador e não devolveu, teste de 02/11) e nunca sob bandeira (amarela, VSC, SC).</summary>
        bool TryRespawnStuck()
        {
            if (car == null || car.pitControlled || waypoints == null || waypoints.Spline == null || race == null) return false;
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car)) return false;
            float along = waypoints.DistanceAlong(transform.position, ref hint);
            float L = waypoints.Spline.Length;
            float clearance = respawnFails >= 3 ? 5f : 9f;
            var fl = RaceX.Core.RaceControl.FlagOf(car);
            bool backOnly = RaceX.Core.SafetyCar.QueueMode(car) || fl == RaceFlag.Yellow || fl == RaceFlag.DoubleYellow || fl == RaceFlag.VSC || fl == RaceFlag.SafetyCar;
            var me = race.GetEntry(car);
            // Lateral com dois pneus na zebra, lado e ponto escolhidos pelo trânsito que vem atrás (RespawnPlanner).
            float[] latOffs = FilterAhead(backOnly ? new[] { -6f, -14f, -24f, -36f } : new[] { -6f, 12f, -14f, 20f, 28f, 36f, 48f, -24f }, me, L, backOnly);
            if (latOffs.Length > 0)
            {
                var poseS = RaceX.Track.RespawnPlanner.Choose(waypoints.Spline, RoadHalf(), transform.position, car, race.entries, latOffs, out float latS);
                bool okS = true;
                for (int i = 0; i < race.entries.Count; i++)
                {
                    var e = race.entries[i];
                    if (e == null || e.car == null || e.car == car || RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) continue;
                    if (Vector3.Distance(e.car.transform.position, poseS.position) < clearance) { okS = false; break; }
                }
                if (okS)
                {
                    car.ResetTo(poseS.position + Vector3.up * 0.3f, poseS.rotation);
                    respawnFails = 0;
                    RespawnCount++;
                    Debug.Log("[AI] respawn lateral " + name + " along=" + along.ToString("0") + " lat=" + latS.ToString("0.0") + " bandeira=" + fl);
                    LateralRespawns++;
                    stuckTimer = 0f; reverseTimer = 0f; escapeTimer = 0f; passTimer = 0f; slideHold = 0f;
                    passCar = null; passSide = 0f;
                    recoveryAttempts = 0;
                    committedLateral = latS;   // volta ao traçado DEVAGAR (como na saída do box), sem cortar ninguém
                    latVel = 0f;
                    mergeTimer = 4f;
                    progressRef = float.NaN; progressTimer = 0f; wrongWayTimer = 0f;
                    cachedSteer = 0f; cachedThrottle = 0.5f; cachedBrake = 0f;
                    return true;
                }
            }
            float[] offsets = FilterAhead(backOnly ? new[] { -6f, -14f, -24f, -36f, -48f } : new[] { -6f, -14f, 12f, 20f, 28f, 36f, 48f, 60f, 75f, -24f }, me, L, backOnly);
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
                RespawnCount++;
                Debug.Log("[AI] respawn " + name + " along=" + along.ToString("0") + " bandeira=" + fl);
                stuckTimer = 0f; reverseTimer = 0f; escapeTimer = 0f; passTimer = 0f; slideHold = 0f;
                passCar = null; passSide = 0f;
                recoveryAttempts = 0;
                committedLateral = 0f;
                latVel = 0f;
                progressRef = float.NaN; progressTimer = 0f; wrongWayTimer = 0f;
                cachedSteer = 0f; cachedThrottle = 0.6f; cachedBrake = 0f;
                return true;
            }
            respawnFails++;
            return false;
        }

        /// <summary>Tira os pontos À FRENTE que fariam o carro passar alguém (carro ativo entre mim e o ponto + 8 m) — e todos os
        /// pontos à frente sob bandeira.</summary>
        float[] FilterAhead(float[] offs, RaceEntry me, float L, bool backOnly)
        {
            var list = new List<float>(offs.Length);
            foreach (float off in offs)
            {
                if (off > 0f && (backOnly || me == null || PassesSomeone(me, off, L))) continue;
                list.Add(off);
            }
            return list.ToArray();
        }

        bool PassesSomeone(RaceEntry me, float off, float L)
        {
            for (int i = 0; i < race.entries.Count; i++)
            {
                var o = race.entries[i];
                if (o == null || o == me || o.car == null || o.retired || o.finished || !o.car.gameObject.activeInHierarchy) continue;
                if (RaceX.Core.PitLaneSystem.IsInPitFlow(o.car)) continue;
                float dz = Mathf.Repeat(o.raceDistance - me.raceDistance + L * 0.5f, L) - L * 0.5f;
                if (dz > -2f && dz < off + 8f) return true;
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
