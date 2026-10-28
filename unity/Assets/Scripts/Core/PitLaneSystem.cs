using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Pit stop v1: desgaste de pneu, compostos e pit lane procedural na reta principal.
    /// Criado por RaceBootstrap quando RaceSettings.pitStops = true.</summary>
    public class PitLaneSystem : MonoBehaviour
    {
        public const float LaneSpeedKmh = 80f; // FIA: limite padrão do pit lane (60 em Mônaco/Melbourne)
        public const float LaneSpeedMs = LaneSpeedKmh / 3.6f;
        public const float StopSeconds = 4f;
        // FIA: pit lane >= 12 m (fast lane + working lane), separado da pista por muro.
        // Centro do pit a 14 m do centro da pista: pista (~6 m de meia-estrada) + muro + faixa.
        const float laneOffset = 14f;

        public static readonly string[] CompoundNames = { "MACIO", "MÉDIO", "DURO" };
        static readonly float[] CompoundGrip = { 1.04f, 1.0f, 0.97f };
        static readonly float[] CompoundWearRate = { 2.0f, 1.0f, 0.55f };

        static PitLaneSystem instance;
        public static PitLaneSystem Instance => instance;

        class CarState
        {
            public CarController car;
            public bool isHuman;
            public int compound = 1;
            public float wear;
            public bool requested;
            public bool inPit;
            public float stopTimer;
            public int plannedLap = -1;
            public bool holding;
            public float releaseTimer;
            public float scanTimer;    // varredura de entrada do pit a 5 Hz (otimização)
            public bool exiting;      // saída guiada: piloto automático leva até a rampa de SAÍDA
            public Vector3 boxTarget; // caixa do PRÓPRIO carro (dentro da sua garagem)
            public float boxS;        // posição ao longo da spline da própria caixa (para rotas)
            public readonly List<Vector3> route = new List<Vector3>(); // rota por waypoints (entrada/saída)
            public int routeIdx;      // waypoint atual da rota
        public float stuckTimer;   // anti-travamento: tempo quase parado seguindo rota
        public float reverseTimer; // manobra de ré em andamento (anti-travamento)
        public float reverseCount; // ré repetidas no MESMO waypoint (quebra o loop contra estrutura)
        public float pitCtrlSince = -1f; // cão de guarda: desde quando pitControlled está ligado
        public float[] wearW = new float[4]; // desgaste POR RODA (DI, DE, TD, TE) — painel do HUD
        public float waitTimer;  // fila do pit: tempo esperando atrás de carro lento/parado (contorna após 4 s)
        public Vector3 stillPos; // posição da última amostra (anti-travamento por POSIÇÃO, não por velocidade)
        public float stillTimer; // tempo sem sair de um raio de 0,6 m (encostado no muro/arquibancada/carro)
        public bool forcedFree;  // liberado à força pelo cão de guarda: não pode ser re-engatado na fila
        public float escapeSide = 1f; // lado para onde virar durante a ré (−1 esquerda, +1 direita)
        public float lastExitTime = -999f; // instante da última saída do box (bloqueia re-captura imediata)
        }

        RaceManager race;
        TrackSpline spline;
        readonly List<CarState> cars = new List<CarState>();
        float laneStart;
        float laneLength = 200f;   // corredor esticado: boxes maiores (pedido do usuário: mais espaço entre carros)
        Vector3 boxPos;
        public bool Enabled { get; private set; }
        /// <summary>Total de boxes/garagens ao longo do lane (setado pelo bootstrap antes dos spawns).</summary>
        public int boxCount = 16;

        void Awake() { instance = this; }
        void OnDestroy() { if (instance == this) instance = null; }

        CarState Find(CarController car)
        {
            foreach (var s in cars) if (s.car == car) return s;
            return null;
        }

        /// <summary>Grip atual do pneu (multiplicador por composto e desgaste).</summary>
        /// <summary>Pit ativo (o sistema existe e a corrida tem pit habilitado).</summary>
        public static bool PitEnabled => instance != null && instance.race != null && instance.race.Settings.pitStops;

        /// <summary>Distância do carro até a ENTRADA da faixa de pit (m). <0 = dentro da janela do pit.</summary>
        public static float DistanceToPitEntry(CarController car)
        {
            if (instance == null || instance.race == null) return -1f;
            float dist = instance.race.Track.Checkpoints.RaceDistance(car);
            float rel = Mathf.Repeat(instance.laneStart - dist, instance.spline.Length);
            return rel > instance.spline.Length * 0.5f ? -1f : rel;
        }

        public static float TireGrip(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? 1f : CompoundGrip[st.compound] * (1f - 0.10f * st.wear);
        }

        /// <summary>Composto atual (0 macio, 1 médio, 2 duro). -1 = sem pit ativo.</summary>
        public static int Compound(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? -1 : st.compound;
        }

        /// <summary>Desgaste 0..1 (HUD).</summary>
        public static float Wear(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? 0f : st.wear;
        }

        public static string WearText(CarController car)
        {
            int c = Compound(car);
            if (c < 0) return "";
            var st = instance.Find(car);
            return CompoundNames[c] + " " + Mathf.RoundToInt((1f - st.wear) * 100f) + "%";
        }

        /// <summary>Jogador pede pit (HUD/tecla P). Vale na próxima passagem pela entrada.</summary>
        public static void RequestPit(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st != null && !st.inPit && !st.holding && !st.exiting)
                st.requested = !st.requested;
        }

        /// <summary>Indica se o botão PIT deve ficar marcado/destacado no HUD (pedido ativo ou já no pit).</summary>
        public static bool IsPitActiveOrRequested(CarController car)
        {
            if (instance == null || car == null) return false;
            var st = instance.Find(car);
            return st != null && (st.requested || st.inPit || st.holding);
        }

        /// <summary>Carro está no fluxo do pit (na caixa, na fila ou saindo guiado)? TrackBounds NÃO reseta.</summary>
        public static bool IsInPitFlow(CarController car)
        {
            if (instance == null) return false;
            var st = instance.Find(car);
            return st != null && (st.inPit || st.holding || st.exiting);
        }

        /// <summary>Indica se o carro está dentro/saindo da garagem para a câmera de 3ª pessoa aproximar e não atravessar a parede de fundo.</summary>
        public static bool IsInGarageView(CarController car)
        {
            if (instance == null || car == null) return false;
            var st = instance.Find(car);
            if (st == null) return false;
            if (st.holding) return true;
            if (st.exiting && st.routeIdx <= 1) return true;
            if (st.inPit && !st.exiting && Vector3.Distance(car.transform.position, st.boxTarget) < 14f) return true;
            return false;
        }

        /// <summary>Retorna o composto atual do carro (0=Macio/S, 1=Médio/M, 2=Duro/H).</summary>
        public static int CompoundOf(CarController car)
        {
            if (instance == null || car == null) return 1;
            var st = instance.Find(car);
            return st != null ? st.compound : 1;
        }

        /// <summary>Solta o JOGADOR do fluxo do pit IMEDIATAMENTE (fechou o menu da carreira, quer voltar
        /// à pista já). Sem isso o carro continuava em fila/rota com o limite de 80 km/h do pit depois
        /// do menu — a sensação de "carro limitado ao voltar". Se estiver apenas na rota de ENTRADA,
        /// deixa completar (ela termina no box e para — não há atalho seguro do meio da rota).</summary>
        public static void ReleasePlayerNow()
        {
            if (instance == null || instance.race == null) return;
            foreach (var e in instance.race.entries)
            {
                if (!e.isHuman) continue;
                var st = instance.Find(e.car);
                if (st == null) continue;
                if (st.holding) { st.holding = false; instance.Release(st); }   // fila/garagem → rota de saída JÁ
                else if (st.exiting) { /* rota de saída em andamento: v59 já devolve o controle ao pisar na pista */ }
                else if (st.inPit)
                {
                    // CASO SEM FILA: carro navegando a rota de ENTRADA / aproximando da caixa
                    // (inPit sem holding nem exiting) — antes ficava preso ao autopilot e ao
                    // limite de 80 km/h sem nenhuma forma de soltar. Solta JÁ na hora.
                    st.inPit = false;
                    st.requested = false;
                    st.car.PitReleaseReset();
                    instance.race.Track.Checkpoints.ReSyncAfterPit(st.car);
                    e.lapStartTime = Time.time;
                }
            }
        }

        public void Setup(RaceManager raceManager)
        {
            race = raceManager;
            spline = race.Track.Spline;
            Enabled = true;
            // Reta principal: maior trecho de baixa curvatura da volta.
            float best = 0f, bestAt = 0f;
            for (float s = 0f; s < spline.Length; s += 10f)
            {
                float run = 0f;
                for (float t = s; t < s + 240f; t += 10f)
                {
                    float tt = Mathf.Repeat(t, spline.Length);
                    if (CurvatureAt(tt) > 0.0015f) break;
                    run += 10f;
                }
                if (run > best) { best = run; bestAt = s; }
            }
            laneStart = bestAt;
            if (boxCount < 4) boxCount = 16;
            // Espaço REAL por box: ~9,5 m por garagem + 40 m de balanço (entrada/saída).
            // Reta curta: o lane se estica pelo spline — nunca mais espremer os carros no spawn.
            laneLength = Mathf.Max(Mathf.Clamp(best * 0.75f, 120f, 300f), boxCount * 9.5f + 40f);
            var mid = spline.PoseAtDistance(laneStart + laneLength * 0.5f);
            boxPos = mid.position - mid.rotation * new Vector3(laneOffset, 0f, 0f) + Vector3.up * 0.1f;
            BuildGeometry();
            try
            {
                // VÃO no guard-rail da pista no lado do pit: a cerca contínua (~16,7 m do centro)
                // atravessava a faixa do pit (−8 a −20 m) — quem saía da garagem batia nela e não
                // havia caminho até a pista. Sem cerca no trecho INTEIRO do pit + margens.
                race.Track.OpenWallGap(laneStart - 35f, laneLength + 95f, true);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[PitLane] vao no guard-rail ignorado: {ex.Message}"); }
            InitCars();
        }

        /// <summary>Treino livre (sex/sáb): saída escalonada + ciclos de pit. Identificado por voltas "infinitas".</summary>
        public static bool PracticeSession => instance != null && instance.race != null && instance.race.Settings.laps >= 900;

        /// <summary>Posição de largada dentro do pit lane (treinos): 2 colunas alternadas com 12,5 m
        /// entre carros da mesma fileira e ±1,8 m do centro (4,2 m de folga até cada parede).</summary>
        /// <summary>Largada do treino: cada carro nasce DENTRO DA SUA GARAGEM (casca), virado para a
        /// saída (corredor) — sai de ré nunca: frente é para fora.</summary>
        public Pose PitSpawnPose(int slot)
        {
            // MESMA fórmula das garagens e do boxTarget: box = gridSlot, centro −25.5 m, angulado 45° para a saída.
            if (boxCount < 4) boxCount = 16;
            float bs = (laneLength - 30f) / boxCount;
            float firstS = laneStart + 15f;
            var p = spline.PoseAtDistance(Mathf.Repeat(firstS + slot * bs + bs * 0.5f, spline.Length));
            Vector3 right = p.rotation * Vector3.right;
            Vector3 fwd = p.rotation * Vector3.forward;
            p.position = p.position - right * 25.5f; // garagem (abertura −20 m, fundo −36 m)
            // Angulado 45° para a saída/frente do corredor: exige metade do ângulo de giro ao sair
            // e evita abrir o arco de direção contra a mureta laranja.
            p.rotation = Quaternion.LookRotation((right + fwd * 1.15f).normalized, Vector3.up);
            // Cola no chão REAL do pit (o terreno varia; sem isso o carro nasce no ar/afundado).
            var up = Physics.Raycast(p.position + Vector3.up * 4f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : p.position;
            p.position = up + Vector3.up * 0.35f;
            return p;
        }

        void InitCars()
        {
            if (race == null || race.entries.Count == 0) return;
            bool practice = race.Settings.laps >= 900;
            int slot = cars.Count;
            foreach (var e in race.entries)
            {
                if (Find(e.car) != null) continue;
                var st = new CarState { car = e.car, isHuman = e.isHuman, compound = e.isHuman ? 1 : Random.value < 0.35f ? 0 : Random.value < 0.5f ? 1 : 2 };
                if (!e.isHuman) st.plannedLap = PlanNextStop(st, Random.Range(1, Mathf.Max(2, race.Settings.laps / 4)));
                float bs = (laneLength - 30f) / boxCount;
                int bIdx = Mathf.Clamp(e.gridSlot, 0, Mathf.Max(0, boxCount - 1));
                float firstS = laneStart + 15f;
                st.boxS = Mathf.Repeat(firstS + bIdx * bs + bs * 0.5f, spline.Length);
                var bp = spline.PoseAtDistance(st.boxS);
                Vector3 bRight = bp.rotation * Vector3.right;
                st.boxTarget = bp.position - bRight * 28f + Vector3.up * 0.1f; // centro da garagem
                if (practice)
                {
                    if (e.isHuman)
                    {
                        st.releaseTimer = 2f;
                    }
                    else
                    {
                        // SEXTA E SÁBADO (treino e qualificação): cada equipe (par de carros) tem sua
                        // própria janela estratégica de saída — não saem todos juntos para não lotar a pista,
                        // e o 2º carro da mesma equipe sai defasado do 1º.
                        int teamIdx = bIdx / 2;
                        int mateIdx = bIdx % 2;
                        int wave = teamIdx % 4; // 4 ondas de equipes
                        float teamStrategyDelay = wave * 14f + (teamIdx / 4) * 4.5f + mateIdx * 8.5f;
                        st.releaseTimer = 3.5f + teamStrategyDelay + Random.Range(0f, 3.5f);
                        ++slot;
                    }
                    st.holding = true;
                    st.inPit = true;
                    ApplyHold(st);
                }
                cars.Add(st);
            }
        }

        int CarsOnTrackCount()
        {
            int count = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != null && c.car != null && (!c.inPit || c.exiting)) count++;
            }
            return count;
        }

        int CarsInPitLaneMovingCount()
        {
            int count = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != null && c.car != null && (c.exiting || (c.inPit && !c.holding))) count++;
            }
            return count;
        }

        bool IsCorridorClearNearBox(CarState st)
        {
            var pc = spline.PoseAtDistance(st.boxS);
            Vector3 laneAtBox = pc.position - pc.rotation * new Vector3(laneOffset, 0f, 0f);
            for (int i = 0; i < cars.Count; i++)
            {
                var ot = cars[i];
                if (ot == null || ot == st || ot.car == null || ot.holding) continue;
                if (!ot.inPit && !ot.exiting) continue;
                Vector3 d = ot.car.transform.position - laneAtBox; d.y = 0f;
                if (d.magnitude < 20f) return false;
            }
            return true;
        }

        float SessionRemainingSeconds()
        {
            if (race != null && race.Settings.careerPhase >= 0 && RaceX.Career.CareerMode.SessionClockActive)
                return Mathf.Max(0f, RaceX.Career.CareerMode.SessionSecondsLeft());
            return 600f;
        }

        /// <summary>Mantém o carro parado no pit enquanto aguarda a liberação (fila do treino).</summary>
        void ApplyHold(CarState st)
        {
            if (st.car == null) return;
            st.car.pitControlled = true;
            st.car.extThrottle = 0f;
            st.car.extBrake = 1f;
            st.car.extSteer = 0f;
        }

        /// <summary>Libera o carro da caixa: SAÍDA GUIADA por ROTA limpa:
        /// sai RETO da garagem (−28 → −16 m), alinha no corredor (−14 m), segue até a rampa e entra na pista.</summary>
        void Release(CarState st)
        {
            st.holding = false;
            st.exiting = true;
            st.lastExitTime = Time.time;
            st.car.pitControlled = true;
            st.route.Clear();
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            // 1) Boca da garagem (−19 m, 3.5 m à frente): sai limpo da garagem já virando para o corredor;
            var pMouth = spline.PoseAtDistance(Mathf.Repeat(st.boxS + 3.5f, spline.Length));
            st.route.Add(pMouth.position - pMouth.rotation * new Vector3(19.0f, 0f, 0f) + Vector3.up * 0.05f);
            // 2) Alinhamento no corredor (−15.6 m, bem afastado da mureta laranja em −7.8 m);
            if (boxRel + 10f < laneLength - 10f)
            {
                var pAlign = spline.PoseAtDistance(Mathf.Repeat(st.boxS + 10f, spline.Length));
                st.route.Add(pAlign.position - pAlign.rotation * new Vector3(15.6f, 0f, 0f) + Vector3.up * 0.05f);
            }
            // 3) Waypoints de 10 em 10 metros seguindo a curvatura do corredor em −15.2 m (nunca corta pela mureta laranja!);
            for (float r = boxRel + 18f; r < laneLength - 8f; r += 10f)
            {
                var pm = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, spline.Length));
                st.route.Add(pm.position - pm.rotation * new Vector3(15.2f, 0f, 0f) + Vector3.up * 0.05f);
            }
            // 4) Fim da mureta laranja -> rampa de saída gradual até a pista;
            var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength - 4f, spline.Length));
            st.route.Add(pe.position - pe.rotation * new Vector3(12.5f, 0f, 0f) + Vector3.up * 0.05f);
            var px = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 12f, spline.Length));
            st.route.Add(px.position - px.rotation * new Vector3(5.5f, 0f, 0f) + Vector3.up * 0.05f);
            // 5) Na pista — ponto de merge adiante na reta.
            var pMerge = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 28f, spline.Length));
            st.route.Add(pMerge.position + Vector3.up * 0.05f);
            st.routeIdx = 0;
            st.stuckTimer = 0f; st.reverseTimer = 0f; st.stillTimer = 0f; st.waitTimer = 0f; st.reverseCount = 0f;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
        }

        void BuildEntryRoute(CarState st, float rel, bool approachingFromTrack)
        {
            st.route.Clear();
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            if (approachingFromTrack)
            {
                var pMouth = spline.PoseAtDistance(Mathf.Repeat(laneStart - 4f, spline.Length));
                st.route.Add(pMouth.position - pMouth.rotation * new Vector3(7.5f, 0f, 0f) + Vector3.up * 0.05f);
                var pIn = spline.PoseAtDistance(Mathf.Repeat(laneStart + 10f, spline.Length));
                st.route.Add(pIn.position - pIn.rotation * new Vector3(laneOffset, 0f, 0f) + Vector3.up * 0.05f);
            }
            float startRel = approachingFromTrack ? 26f : Mathf.Max(12f, rel + 14f);
            for (float r = startRel; r < boxRel - 8f; r += 24f)
            {
                var pStep = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, spline.Length));
                st.route.Add(pStep.position - pStep.rotation * new Vector3(laneOffset, 0f, 0f) + Vector3.up * 0.05f);
            }
            // Ponto de curva na frente da própria garagem (−18 m) antes de entrar no centro da garagem (−28 m).
            var pPreBox = spline.PoseAtDistance(Mathf.Repeat(st.boxS - 2f, spline.Length));
            st.route.Add(pPreBox.position - pPreBox.rotation * new Vector3(18.5f, 0f, 0f) + Vector3.up * 0.05f);
            st.route.Add(st.boxTarget);
            st.routeIdx = 0;
            st.stuckTimer = 0f; st.reverseTimer = 0f; st.stillTimer = 0f; st.waitTimer = 0f;
        }

        /// <summary>[sem uso atual] Distância ao longo da spline de um carro — mantido para debug/uso futuro.</summary>
        float LaneSOf(CarController c)
        {
            float d = race.Track.Checkpoints.RaceDistance(c);
            return Mathf.Repeat(d, spline.Length);
        }

        /// <summary>Pilota por ROTA (waypoints em sequência). Retorna false quando a rota acabou.
        /// Guarda anti-NaN: waypoint inválido é pulado (não congela o carro).</summary>
        bool DriveRoute(CarState st, float speedLimit)
        {
            if (st.routeIdx >= st.route.Count) return false;
            // ANTI-TRAVAMENTO POR POSIÇÃO: velocidade para frente engana (carro encostado e deslizando
            // de lado dava >0,6 km/h e o contador zerava). Se não sai de 0,6 m por 1,2 s → ré JÁ,
            // em qualquer caso (inclusive com carro parado à frente), virando para o lado oposto ao muro/carro.
            if (Vector3.Distance(st.car.transform.position, st.stillPos) > 0.6f)
            {
                st.stillPos = st.car.transform.position;
                st.stillTimer = 0f;
            }
            else st.stillTimer += Time.deltaTime;
            bool queued = false;
            Vector3 fwdNow = st.car.transform.forward; fwdNow.y = 0f; fwdNow.Normalize();
            foreach (var ot in cars)
            {
                if (ot == st || ot.car == null || ot.holding) continue;
                Vector3 dq = ot.car.transform.position - st.car.transform.position; dq.y = 0f;
                if (dq.magnitude < 4.5f && Vector3.Angle(fwdNow, dq) < 38f) { queued = true; break; }
            }
            if (st.stillTimer > (queued ? 4.0f : 1.4f) && st.reverseTimer <= 0f)
            {
                // Se for o jogador entrando no próprio box e já estiver perto da sua garagem, encaixa no box sem dar ré aleatória.
                if (st.isHuman && st.inPit && !st.holding && !st.exiting && Vector3.Distance(st.car.transform.position, st.boxTarget) < 22f)
                {
                    st.routeIdx = st.route.Count;
                    return false;
                }
                st.stillTimer = 0f;
                st.stuckTimer = 0f;
                st.reverseCount++;
                st.reverseTimer = Mathf.Min(2.6f, 1.3f + 0.35f * st.reverseCount);
                st.escapeSide = st.car.transform.InverseTransformPoint(st.route[st.routeIdx]).x >= 0f ? -1f : 1f;
            }
            Vector3 target = st.route[st.routeIdx];
            Vector3 to = target - st.car.transform.position; to.y = 0f;
            float reach = (st.exiting && st.routeIdx < 2) ? 3.2f
                        : (!st.exiting && st.routeIdx >= st.route.Count - 2) ? 3.5f
                        : 5.0f;
            if (float.IsNaN(to.x) || float.IsNaN(to.z) || (to.magnitude < reach))
            {
                st.routeIdx++;
                st.stuckTimer = 0f;
                st.reverseCount = 0f;
                return DriveRoute(st, speedLimit);
            }
            // TRÁFEGO NO PIT (visão de 20 metros): ignora carros guardados dentro das garagens (ot.holding)
            // e enxerga qualquer carro no corredor até 20 m à frente para desviar ou reduzir a tempo.
            Vector3 myFwd = st.car.transform.forward; myFwd.y = 0f; myFwd.Normalize();
            CarController blocker = null; float blockD = 20f; Vector3 blockDir = Vector3.zero;
            foreach (var ot in cars)
            {
                if (ot == st || ot.car == null || ot.holding) continue;
                Vector3 dto = ot.car.transform.position - st.car.transform.position; dto.y = 0f;
                float dLen = dto.magnitude;
                if (dLen < 0.8f || dLen > blockD) continue;
                if (Vector3.Angle(myFwd, dto) > 42f) continue;
                // Ignora carros que já estão recolhidos fundo dentro da garagem (< -21 m).
                if (Vector3.Distance(ot.car.transform.position, ot.boxTarget) < 6.5f) continue;
                blocker = ot.car; blockD = dLen; blockDir = dto.normalized;
            }
            float avoidBias = 0f;
            if (blocker != null)
            {
                float side = Vector3.Dot(st.car.transform.right, blockDir);
                float sideSign = Mathf.Abs(side) > 0.05f ? -Mathf.Sign(side) : 1f;
                if (blockD < 18f && blocker.ForwardSpeed < speedLimit * 0.85f)
                {
                    float weight = Mathf.Clamp01(1f - (blockD - 3f) / 15f);
                    avoidBias = sideSign * (0.45f + 0.35f * weight);
                }
                if (blocker.ForwardSpeed < 2.0f && blockD < 4.5f && st.waitTimer < 2.2f)
                {
                    st.stuckTimer = 0f;
                    st.waitTimer += Time.deltaTime;
                    st.car.extSteer = Mathf.Clamp(sideSign * 0.7f, -1f, 1f);
                    st.car.extThrottle = 0f;
                    st.car.extBrake = st.car.ForwardSpeed > 0.4f ? 0.85f : 0f;
                    return true;
                }
            }
            else st.waitTimer = 0f;
            if (st.reverseTimer > 0f)
            {
                st.reverseTimer -= Time.deltaTime;
                if (st.car.Gear < 0)
                {
                    st.car.extThrottle = 0.3f;
                    st.reverseTimer = Mathf.Max(st.reverseTimer, 1.1f);
                }
                else
                {
                    st.car.extThrottle = -0.55f;
                }
                st.car.extBrake = 0f;
                st.car.extSteer = st.escapeSide * 0.9f;
                return true;
            }
            if (st.car.ForwardSpeed < 0.6f) st.stuckTimer += Time.deltaTime; else st.stuckTimer = 0f;
            if (st.stuckTimer > 2.4f)
            {
                st.stuckTimer = 0f;
                if (st.isHuman && st.inPit && !st.holding && !st.exiting && Vector3.Distance(st.car.transform.position, st.boxTarget) < 24f)
                {
                    st.routeIdx = st.route.Count;
                    return false;
                }
                if (st.reverseCount >= 2f && st.exiting)
                {
                    var pFree = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 18f, spline.Length));
                    st.car.ResetTo(pFree.position + Vector3.up * 0.4f, pFree.rotation);
                    st.routeIdx = st.route.Count;
                    return false;
                }
                if (st.reverseCount >= 1f && st.routeIdx < st.route.Count)
                {
                    st.reverseCount = 0f;
                    st.routeIdx++;
                    return true;
                }
                st.reverseCount++;
                st.reverseTimer = Mathf.Min(2.2f, 1.1f + 0.35f * st.reverseCount);
                return true;
            }
            float steer = Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up) / 45f;
            if (float.IsNaN(steer)) steer = 0f;
            st.car.extSteer = Mathf.Clamp(steer + avoidBias, -1f, 1f);
            bool ultimo = st.routeIdx >= st.route.Count - 1;
            bool penultimoEntrada = !st.exiting && st.routeIdx == st.route.Count - 2;
            float targetSpeed;
            if (!st.exiting && ultimo)
                targetSpeed = Mathf.Clamp(to.magnitude * 0.40f, 2.0f, 5.0f);
            else if (penultimoEntrada)
                targetSpeed = Mathf.Clamp(to.magnitude * 0.45f, 4.0f, 7.5f);
            else
                targetSpeed = ultimo ? speedLimit : Mathf.Max(6.0f, Mathf.Min(speedLimit, to.magnitude * 0.55f));
            float turnAngle = Mathf.Abs(Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up));
            if (!ultimo && turnAngle > 22f) targetSpeed = Mathf.Min(targetSpeed, 16f / 3.6f);
            if (blocker != null)
            {
                float safeBrakeSpeed = Mathf.Max(0f, blocker.ForwardSpeed) + Mathf.Sqrt(2f * 6.5f * Mathf.Max(0f, blockD - 4.0f));
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(2.0f, safeBrakeSpeed));
            }
            float speed = st.car.ForwardSpeed;
            st.car.extThrottle = speed < targetSpeed ? 0.85f : 0f;
            st.car.extBrake = speed > targetSpeed + 2.0f ? Mathf.Clamp((speed - targetSpeed) / 6f, 0.35f, 1f) : 0f;
            return true;
        }

        /// <summary>Duração real do composto em voltas (Macio ~8, Médio ~14, Duro ~22 — clampado ao tamanho da corrida).</summary>
        static int CompoundLife(int compound) => compound == 0 ? 8 : compound == 1 ? 14 : 22;

        /// <summary>Próxima parada planejada da IA: volta atual + vida do composto × margem de segurança.
        /// Em corridas longas isso gera MÚLTIPLAS paradas (recalcula a cada pit) — como na F1 real.</summary>
        int PlanNextStop(CarState st, int margin)
        {
            int life = Mathf.Max(4, Mathf.RoundToInt(CompoundLife(st.compound) * (0.85f - 0.02f * margin)));
            return Mathf.Max(2, race.GetProgress(st.car != null ? EntryOf(st.car) : null)?.lap ?? 1) + life;
        }

        RaceEntry EntryOf(CarController c)
        {
            foreach (var e in race.entries) if (e.car == c) return e;
            return null;
        }

        float CurvatureAt(float s)
        {
            const float span = 8f;
            Vector3 a = spline.PoseAtDistance(s - span).rotation * Vector3.forward;
            Vector3 b = spline.PoseAtDistance(s + span).rotation * Vector3.forward;
            return Vector3.Angle(a, b) * Mathf.Deg2Rad / (2f * span);
        }

        void BuildGeometry()
        {
            // Material físico SEM bounce para todo o pit: encostou na parede, desliza — não ricocheteia.
            // Unity 6: PhysicMaterial virou PhysicsMaterial (o antigo é erro de compilação).
            var pitPhys = new PhysicsMaterial("PitNoBounce")
            {
                bounciness = 0f,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            try
            {
                // Shader por FALLBACK: no APK (IL2CPP) Shader.Find pode vir null para shaders
                // não referenciados em cena (mesmo bug histórico do SkidMarks). Sem fallback,
                // ArgumentNullException mata o RaceBootstrap.Start -> loading travado em 95%.
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Sprites/Default")
                          ?? Shader.Find("Unlit/Color");
                if (shader == null) return; // sem shader válido: corrida funciona sem visual do pit
                int steps = 24;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float s = Mathf.Repeat(laneStart + laneLength * i / steps, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                // Faixa asfáltica de 12 m (FIA): 8 m de FAST LANE (lado do muro/pista) + 4 m de WORKING LANE (lado das garagens).
                // LADO DO PIT: mesmo lado do laneCenter (−right). Antes estava no lado OPOSTO da pista.
                verts.Add(p.position - right * (laneOffset + 6f) + Vector3.up * 0.02f);
                verts.Add(p.position - right * (laneOffset - 6f) + Vector3.up * 0.02f);
                if (i < steps)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            var go = new GameObject("PitLane");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(shader) { color = new Color(0.13f, 0.13f, 0.15f) };
            // SUPERFÍCIE REAL: collider + asfalto — o corredor do pit tem o MESMO grip da pista
            // (antes era o Ground de grama por baixo: o carro deslizava e não virava na zona do pit).
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;

            // ROTA VISUAL: linha tracejada LARANJA no meio da faixa (da entrada ao box) — o caminho do pit.
            var dashes = new List<Vector3>();
            var dashTris = new List<int>();
            int n = 0;
            for (int i = 0; i < steps; i += 2)
            {
                float s = Mathf.Repeat(laneStart + laneLength * i / steps, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                Vector3 c = p.position - right * laneOffset + Vector3.up * 0.07f;
                dashes.Add(c + right * 0.35f); dashes.Add(c - right * 0.35f);
                dashes.Add(c + right * 0.35f + Vector3.up * 0.01f); dashes.Add(c - right * 0.35f + Vector3.up * 0.01f);
                if (i + 2 < steps)
                {
                    int b = n * 4;
                    dashTris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
                n++;
            }
            var dmesh = new Mesh { vertices = dashes.ToArray(), triangles = dashTris.ToArray() };
            dmesh.RecalculateNormals();
            var dgo = new GameObject("PitRoute");
            dgo.transform.SetParent(transform, false);
            dgo.AddComponent<MeshFilter>().sharedMesh = dmesh;
            var dmr = dgo.AddComponent<MeshRenderer>();
            dmr.sharedMaterial = new Material(shader) { color = new Color(1f, 0.55f, 0.1f) };

            // PIT COMPLETO + ACESSÍVEL: estrutura real do paddock (garagens, portas, muros,
            // gantries) e RAMPAS de asfalto ligando a PISTA ao pit lane na entrada e na saída
            // (sem rampa ninguém consegue entrar — o muro nunca cobre entrada/saída).
            var wall = new Material(shader) { color = new Color(0.85f, 0.3f, 0.12f) };
            var building = new Material(shader) { color = new Color(0.62f, 0.63f, 0.66f) };
            var door = new Material(shader) { color = new Color(0.18f, 0.2f, 0.24f) };
            var floor = new Material(shader) { color = new Color(0.24f, 0.25f, 0.28f) };
            float entryS = laneStart, exitS = laneStart + laneLength;
            // Box count UNIFICADO (campo da classe, setado pelo bootstrap ANTES do Setup com o nº de carros).
            // Nunca usar race.entries.Count aqui: na hora do Setup os carros ainda não nasceram (=0).
            if (boxCount < 4) boxCount = 16;
            float boxSize = (laneLength - 30f) / boxCount;
            float boxFirst = laneStart + 15f;
            for (int i = 0; i < boxCount; i++)
            {
                float s = Mathf.Repeat(boxFirst + (i + 0.5f) * boxSize, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                Vector3 laneCenter = p.position - right * laneOffset;

                // GARAGEM REAL (frente aberta p/ o carro ENTRAR): piso + teto + 3 paredes finas.
                // Alinhada com PitSpawnPose/boxTarget (−28 m da pista; fundo −36 m, abertura −20 m).
                // Em p.rotation, X = right (profundidade 16 m) e Z = forward (largura boxSize).
                Vector3 garageCenter = p.position - right * 28f;
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = "PitFloor" + i;
                f.transform.SetParent(transform, false);
                f.transform.rotation = p.rotation;
                f.transform.position = garageCenter + Vector3.up * 0.005f;
                f.transform.localScale = new Vector3(16f, 0.02f, boxSize - 0.4f);
                f.GetComponent<MeshRenderer>().sharedMaterial = floor;
                f.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;
                // Fundo (−36 m) — puramente visual para nunca prender o para-choque na manobra.
                var gB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gB.name = "PitBuilding" + i;
                gB.transform.SetParent(transform, false);
                gB.transform.position = p.position - right * 36f + Vector3.up * 2.4f;
                gB.transform.rotation = p.rotation;
                gB.transform.localScale = new Vector3(0.5f, 4.8f, boxSize - 0.4f);
                var c_gB = gB.GetComponent<Collider>(); if (c_gB != null) DestroyImmediate(c_gB);
                gB.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Divisórias laterais entre garagens: orientadas em X (16 m de profundidade) e sem collider.
                for (int side = -1; side <= 1; side += 2)
                {
                    var gS = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gS.name = "PitBuilding" + i + (side < 0 ? "L" : "R");
                    gS.transform.SetParent(transform, false);
                    gS.transform.position = garageCenter + p.rotation * Vector3.forward * (side * (boxSize - 0.5f) * 0.5f) + Vector3.up * 2.4f;
                    gS.transform.rotation = p.rotation;
                    gS.transform.localScale = new Vector3(16f, 4.8f, 0.35f);
                    var c_gS = gS.GetComponent<Collider>(); if (c_gS != null) DestroyImmediate(c_gS);
                    gS.GetComponent<MeshRenderer>().sharedMaterial = building;
                }
                // Teto.
                var gT = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gT.name = "PitRoof" + i;
                gT.transform.SetParent(transform, false);
                gT.transform.position = garageCenter + Vector3.up * 4.8f;
                gT.transform.rotation = p.rotation;
                gT.transform.localScale = new Vector3(16f, 0.4f, boxSize - 0.4f);
                var c_gT = gT.GetComponent<Collider>(); if (c_gT != null) DestroyImmediate(c_gT);
                gT.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Portão RECOLHIDO (no teto da abertura em −20 m) — VISUAL APENAS.
                var dr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dr.name = "PitDoor" + i;
                dr.transform.SetParent(transform, false);
                dr.transform.position = p.position - right * 20f + Vector3.up * 4.55f;
                dr.transform.rotation = p.rotation;
                dr.transform.localScale = new Vector3(0.25f, 0.5f, boxSize - 0.8f);
                var c_dr = dr.GetComponent<Collider>(); if (c_dr != null) DestroyImmediate(c_dr);
                dr.GetComponent<MeshRenderer>().sharedMaterial = door;

                // Muro interno com VÃO: só no miolo da faixa (entre 25% e 75%) — as pontas
                // (entrada e saída) ficam SEMPRE abertas para o carro poder entrar/sair.
                bool midLane = i >= 3 && i <= boxCount - 4;
                if (midLane)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    w.name = "PitWall" + i;
                    w.transform.SetParent(transform, false);
                    w.transform.position = laneCenter + right * 6.2f + Vector3.up * 0.55f;
                    w.transform.rotation = p.rotation;
                    // MURO AO LONGO do lane: fino no transversal (0,3 m), comprido na direção da pista.
                    // Antes estava INVERTIDO (10,6 m transversal) — virava cancela atravessada no lane.
                    w.transform.localScale = new Vector3(0.3f, 1.1f, boxSize - 0.4f);
                    var c_w = w.GetComponent<Collider>(); if (c_w != null) DestroyImmediate(c_w);
                w.GetComponent<MeshRenderer>().sharedMaterial = wall;
                }
            }
                // RAMPAS DE ACESSO: triângulos de asfalto da borda da pista até o pit lane (entrada e saída).
                // LADO DO PIT: borda da pista (−6,5 m) até o CENTRO do lane (−14 m) — antes atravessava a pista toda.
                for (int k = 0; k < 2; k++)
                {
                    float baseS = Mathf.Repeat(k == 0 ? entryS - 25f : exitS - 15f, spline.Length);
                    var p0 = spline.PoseAtDistance(baseS);
                    var p1 = spline.PoseAtDistance(Mathf.Repeat(baseS + 30f, spline.Length));
                    Vector3 r0 = p0.rotation * Vector3.right;
                    Vector3 r1 = p1.rotation * Vector3.right;
                    var apron = new GameObject("PitApron" + k, typeof(MeshFilter), typeof(MeshRenderer));
                    apron.transform.SetParent(transform, false);
                    var mf = apron.GetComponent<MeshFilter>();
                    var vA = p0.position - r0 * 6.5f + Vector3.up * 0.03f;   // borda da pista (lado do pit)
                    var vB = p1.position - r1 * laneOffset + Vector3.up * 0.03f; // centro do pit lane
                    var vC = p0.position - r0 * 6.5f + (p1.position - p0.position) + Vector3.up * 0.03f;
                    var vD = p1.position - r1 * laneOffset + (p0.position - p1.position) + Vector3.up * 0.03f;
                mf.sharedMesh = new Mesh { vertices = new[] { vA, vB, vC, vD }, triangles = new[] { 0, 1, 2, 1, 3, 2 } };
                mf.sharedMesh.RecalculateNormals();
                apron.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = new Color(0.16f, 0.16f, 0.18f) };
                // Collider + asfalto: rampa de entrada/saída com grip da pista (não grama).
                apron.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                apron.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;
            }
            // Gantries (pórtico SOBRE O PIT LANE) na entrada e saída: postes nas duas bordas
            // do lane (−8 e −20 m) e viga transversal ligando os dois — NUNCA sobre a pista.
            for (int k = 0; k < 2; k++)
            {
                float gs = Mathf.Repeat(k == 0 ? entryS + 4f : exitS - 4f, spline.Length);
                var gp = spline.PoseAtDistance(gs);
                Vector3 gr = gp.rotation * Vector3.right;
                var post1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post1.name = "GantryPost" + k + "a";
                post1.transform.SetParent(transform, false);
                post1.transform.position = gp.position - gr * 7.5f + Vector3.up * 2.5f;
                post1.transform.rotation = gp.rotation;
                post1.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                if (post1.GetComponent<Collider>() != null) DestroyImmediate(post1.GetComponent<Collider>()); // VISUAL: poste na borda era armadilha na travessia de entrada/saída
                post1.GetComponent<MeshRenderer>().sharedMaterial = building;
                var post2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post2.name = "GantryPost" + k + "b";
                post2.transform.SetParent(transform, false);
                post2.transform.position = gp.position - gr * 20.5f + Vector3.up * 2.5f;
                post2.transform.rotation = gp.rotation;
                post2.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                if (post2.GetComponent<Collider>() != null) DestroyImmediate(post2.GetComponent<Collider>()); // VISUAL: idem post1
                post2.GetComponent<MeshRenderer>().sharedMaterial = building;
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = "GantryBeam" + k;
                beam.transform.SetParent(transform, false);
                beam.transform.position = gp.position - gr * 14f + Vector3.up * 5.2f;
                beam.transform.rotation = gp.rotation;
                // Viga TRANSVERSAL ao lane (13,5 m de −7,25 a −20,75) — antes estava 25 m AO LONGO da pista.
                beam.transform.localScale = new Vector3(13.5f, 0.6f, 0.6f);
                var c_beam = beam.GetComponent<Collider>(); if (c_beam != null) c_beam.sharedMaterial = pitPhys;
                beam.GetComponent<MeshRenderer>().sharedMaterial = building;
            }
            // SETAS grandes no chão da entrada e da saída (para achar e entrar no pit).
            for (int k = 0; k < 2; k++)
            {
                float as_ = Mathf.Repeat(k == 0 ? entryS + 8f : exitS - 8f, spline.Length);
                var ap = spline.PoseAtDistance(as_);
                Vector3 ar = ap.rotation * Vector3.right;
                Vector3 center = ap.position - ar * laneOffset + Vector3.up * 0.02f;
                var arrow = new GameObject("PitArrow" + k, typeof(TextMesh));
                arrow.transform.SetParent(transform, false);
                arrow.transform.position = center;
                arrow.transform.rotation = Quaternion.LookRotation(ap.rotation * Vector3.forward, Vector3.up) * Quaternion.Euler(90f, 0f, k == 0 ? 0f : 180f);
                var atm = arrow.GetComponent<TextMesh>();
                atm.text = ">";
                atm.characterSize = 2.4f;
                atm.fontSize = 96;
                atm.anchor = TextAnchor.MiddleCenter;
                atm.color = new Color(1f, 0.55f, 0.1f);
            }
            }
            catch (System.Exception ex)
            {
                // Visual do pit NUNCA pode quebrar o carregamento da corrida (histórico: loading em loop 95%).
                Debug.LogWarning($"[PitLane] geometria ignorada: {ex.Message}");
            }
        }

        void Update()
        {
            // Estados dos carros criados LAZY e SEMPRE que houver carro sem estado (o bootstrap
            // spawna em lotes: humano primeiro, NPCs depois — rodar 1x só dava pit ao humano).
            if (race != null && race.entries.Count > cars.Count) InitCars();
            if (race == null || race.State != RaceState.Racing) return;
            foreach (var e in race.entries)
            {
                var st = Find(e.car);
                if (st == null) continue;
                ReEngageIfTeleported(st, e);
                // Fila do treino: parado no pit até chegar a vez de sair (o UpdatePit trata holding/exiting).
                UpdatePit(st, e);
                WatchdogPitControl(st, e);
            }
        }

        /// <summary>CÃO DE GUARDA do piloto automático: nenhuma rota de pit legítima passa de 45 s.
        /// Se pitControlled sobreviver sem estado válido (ou por tempo demais), solta o carro e
        /// registra UMA linha no log — o "carro limitado desde o início" nunca mais fica preso,
        /// e se repetir teremos o estado exato impresso (dados reais, não suposição).</summary>
        void WatchdogPitControl(CarState st, RaceEntry e)
        {
            if (st.forcedFree && !st.car.pitControlled && st.car.SpeedKmh > 20f) st.forcedFree = false;
            if (!st.car.pitControlled)
            {
                st.pitCtrlSince = -1f;
                return;
            }
            if (st.pitCtrlSince < 0f)
            {
                st.pitCtrlSince = Time.time;
                if (st.isHuman)
                    Debug.Log($"[PITWATCH] auto-piloto LIGOU no humano: holding={st.holding} exiting={st.exiting} inPit={st.inPit} kmh={st.car.SpeedKmh:0}");
                return;
            }
            // Fila do treino: esperar a vez NÃO é travamento (a fila chega a 60 s; o limite era 45 s
            // e soltava à força os últimos carros, que batiam na garagem sem rota).
            if (st.holding) { st.pitCtrlSince = Time.time; return; }
            bool semEstado = !st.holding && !st.exiting && !st.inPit;
            float dW = race.Track.Checkpoints.RaceDistance(st.car);
            var spw = spline.PoseAtDistance(Mathf.Repeat(dW, spline.Length));
            float latW = Vector3.Dot(st.car.transform.position - spw.position, spw.rotation * Vector3.right);
            if (semEstado || Time.time - st.pitCtrlSince > 45f)
            {
                if (st.isHuman && st.inPit && !st.holding && !st.exiting)
                {
                    var pose = PitSpawnPose(e.gridSlot);
                    st.car.ResetTo(pose.position, pose.rotation);
                    st.routeIdx = st.route.Count;
                    st.pitCtrlSince = Time.time;
                    return;
                }
                Debug.LogWarning($"[PITWATCH] LIBERADO FORÇADO {e.name}: holding={st.holding} exiting={st.exiting} inPit={st.inPit} lat={latW:0.0} kmh={st.car.SpeedKmh:0} t={Time.time - st.pitCtrlSince:0}s");
                st.holding = false; st.exiting = false; st.inPit = false;
                st.forcedFree = true;
                st.car.PitReleaseReset();
                st.pitCtrlSince = -1f;
            }
        }

        /// <summary>RE-ENGATE de teletransporte: um carro resetado PARA O PIT depois de já ter
        /// sido liberado (ex.: TrackBounds devolveu ao spawn do box antes de cruzar o checkpoint 0)
        /// ficava parado no box para sempre — sem holding, sem rota, sem nada. Se ele está
        /// parado dentro da área do box e fora do fluxo, re-engata a fila (holding → saída).</summary>
        void ReEngageIfTeleported(CarState st, RaceEntry e)
        {
            if (st.holding || st.exiting || st.inPit || st.forcedFree) return;
            if (st.car.pitControlled || st.car.SpeedKmh > 2f) return;
            float d = race.Track.Checkpoints.RaceDistance(st.car);
            float rel = Mathf.Repeat(d - laneStart, spline.Length);
            bool noBox = rel > 5f && rel < 30f && st.car.transform.position.y < 6f; // área da garagem
            if (!noBox) return;
            st.inPit = true;
            st.holding = true;
            st.releaseTimer = e.isHuman ? 2f : 2f + Random.value * 2f;
            ApplyHold(st);
            if (st.isHuman) Debug.Log("[PITWATCH] carro resetado no box — fila re-engatada (saída em ~2s)");
        }

        void UpdateWear(CarState st)
        {
            float rate = CompoundWearRate[st.compound];
            float slipSum = 0f;
            for (int i = 0; i < 4; i++)
            {
                // Desgaste POR RODA (alimenta o painel visual com verde/amarelo/vermelho).
                float slip = st.car.WheelSlip[i] * 0.0018f;
                slipSum += slip;
                st.wearW[i] = Mathf.Clamp01(st.wearW[i] + (0.00006f + slip) * rate * Mathf.Clamp(st.car.SpeedKmh / 120f, 0.2f, 2f) * Time.deltaTime * 20f);
            }
            st.wear = Mathf.Clamp01(st.wear + (0.00006f + slipSum * 0.25f) * rate * Mathf.Clamp(st.car.SpeedKmh / 120f, 0.2f, 2f) * Time.deltaTime * 20f); // média = física da roda (compatível)
        }

        /// <summary>Desgaste de UMA roda (0..1) para o painel do HUD. Ordem: 0=DI, 1=DE, 2=TD, 3=TE.</summary>
        public static float WearOf(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 ? st.wearW[wheel] : 0f;
        }

        void UpdatePit(CarState st, RaceEntry e)
        {
            // CAPOTADO no pit: desvirar NO LUGAR (mesma posição, sem teleport). O respawn comum
            // (TrackBounds) é propositalmente ignorado no fluxo do pit — um capotado ali ficava
            // preso para sempre; e resetar com rota velha dava carro andando na contramão sozinho.
            if (Vector3.Dot(st.car.transform.up, Vector3.up) < 0.3f && st.car.SpeedKmh < 15f)
            {
                var eu = st.car.transform.eulerAngles;
                st.car.ResetTo(st.car.transform.position, Quaternion.Euler(0f, eu.y, 0f));
                st.stuckTimer = 0f; st.reverseTimer = 0f;
                return;
            }
            // SAÍDA GUIADA por ROTA (waypoints: corredor → rampa → pista) — sem atravessar fila nem muro.
            if (st.exiting)
            {
                // De volta à PISTA (lat > −6.2 = pisou na borda da pista): solta o controle NA HORA.
                // Sem isso o carro ficava preso ao limite de 80 km/h do pit até o fim da rota
                // (o "voltou reduzido do pit" na reta).
                float dNow = race.Track.Checkpoints.RaceDistance(st.car);
                var spx = spline.PoseAtDistance(Mathf.Repeat(dNow, spline.Length));
                float latOut = Vector3.Dot(st.car.transform.position - spx.position, spx.rotation * Vector3.right);
                if (latOut > -6.2f)
                {
                    st.exiting = false;
                    st.inPit = false;
                    st.lastExitTime = Time.time;
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
                    st.forcedFree = true;
                    // VOLTA CONTA (regra do usuário): realinha os checkpoints com a posição ATUAL —
                    // sem isso o carro saía do pit com nextCheckpoint velho e o próximo cruzamento
                    // da linha não disparava (o cronômetro acumulava 2 voltas antes de zerar).
                    race.Track.Checkpoints.ReSyncAfterPit(st.car);
                    e.lapStartTime = Time.time;
                    var aiFast = st.car != null ? st.car.GetComponent<CarAI>() : null;
                    if (aiFast != null) aiFast.OnPracticeRelease();
                    return;
                }
                if (DriveRoute(st, LaneSpeedMs))
                {
                    // rota em andamento (DriveRoute já aplicou steer/throttle/brake)
                }
                else
                {
                    // Chegou NA PISTA pela rota: devolve o controle (a IA/humano segue o traçado).
                    st.exiting = false;
                    st.inPit = false;
                    st.lastExitTime = Time.time;
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
                    st.forcedFree = true;
                    race.Track.Checkpoints.ReSyncAfterPit(st.car); // volta zera na próxima linha (ver latOut)
                    e.lapStartTime = Time.time;
                    var ai = st.car != null ? st.car.GetComponent<CarAI>() : null;
                    if (ai != null) ai.OnPracticeRelease();
                }
                return;
            }

            if (!st.inPit)
            {
                // Varredura de entrada a 5 Hz (evita RaceDistance+PoseAtDistance por frame por carro:
                // 24 carros × 50 Hz = 1200 consultas/s → 120). Detecção de pit não precisa de 50 Hz.
                st.scanTimer -= Time.deltaTime;
                if (st.scanTimer > 0f) return;
                st.scanTimer = 0.2f;
                float dist = race.Track.Checkpoints.RaceDistance(st.car);
                float rel = Mathf.Repeat(dist - laneStart, spline.Length);
                // NPC: pit na volta planejada — pedido na APROXIMAÇÃO da entrada (rel > len−60),
                // nunca depois da boca (rel 0..40 passava do vão e o commit virava de volta).
                if (!st.isHuman && !st.requested && race.Settings.laps >= 4 && race.GetProgress(e).lap >= st.plannedLap && rel > spline.Length - 60f)
                    st.requested = true;
                // ENTRADA REAL (regra do usuário): quem DIRIGE para dentro do corredor (lado do
                // pit, lat < −8.5 m, na direção da pista e antes do próprio box) tem o fluxo assumido.
                if (!st.inPit && !st.holding && !st.exiting && !st.requested && st.car.SpeedKmh > 1f && Time.time - st.lastExitTime > 25f)
                {
                    var spNow = spline.PoseAtDistance(Mathf.Repeat(dist, spline.Length));
                    float latIn = Vector3.Dot(st.car.transform.position - spNow.position, spNow.rotation * Vector3.right);
                    float fwdDot = Vector3.Dot(st.car.transform.forward, spNow.rotation * Vector3.forward);
                    float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
                    if (latIn < -8.5f && latIn > -22f && fwdDot > 0.45f && rel > 4f && rel < Mathf.Min(38f, boxRel + 4f))
                    {
                        st.inPit = true;
                        st.requested = false;
                        st.car.pitControlled = true;
                        st.car.Repair();
                        BuildEntryRoute(st, rel, false);
                        if (st.isHuman) Debug.Log("[PITWATCH] carro entrou dirigindo no corredor — fluxo assumido (auto até o box)");
                        return;
                    }
                }
                if (st.requested && (rel > spline.Length - 55f || rel < 22f))
                {
                    bool approaching = rel > spline.Length - 55f;
                    st.inPit = true;
                    st.requested = false;
                    st.car.pitControlled = true;
                    st.car.Repair();
                    BuildEntryRoute(st, approaching ? 0f : rel, approaching);
                }
                return;
            }

            // Fila do treino/qualificação (SEXTA E SÁBADO): parado na PRÓPRIA CAIXA (garagem).
            // Cada equipe tem sua estratégia e ciência de quantos carros estão na pista para sair em
            // janela limpa sem lotar a pista, mas garantindo saída a tempo de fechar suas voltas.
            if (st.holding)
            {
                st.releaseTimer -= Time.deltaTime;
                if (st.releaseTimer <= 0f)
                {
                    if (!st.isHuman && PracticeSession)
                    {
                        // 1) Corredor perto do box ocupado ou muitos carros manobrando no pit lane → aguarda abrir.
                        if (!IsCorridorClearNearBox(st) || CarsInPitLaneMovingCount() >= 3)
                        {
                            st.releaseTimer = 1.6f;
                            ApplyHold(st);
                            return;
                        }
                        // 2) Ciência de tráfego na pista vs. tempo restante da sessão:
                        int onTrack = CarsOnTrackCount();
                        float rem = SessionRemainingSeconds();
                        int maxDesiredTraffic = 7 + (e.gridSlot % 5); // 7..11 carros conforme a equipe
                        bool hasTimedLap = e.bestLap < float.MaxValue && e.bestLap > 1f;
                        // Se a pista está cheia E ainda há tempo seguro para não ficar sem volta (ou já tem volta marcada),
                        // espera uma janela mais limpa na pista.
                        if (onTrack >= maxDesiredTraffic && (rem > 240f || (hasTimedLap && rem > 130f)))
                        {
                            st.releaseTimer = Random.Range(6f, 14f);
                            ApplyHold(st);
                            return;
                        }
                    }
                    Release(st);
                }
                else ApplyHold(st);
                return;
            }

            // Piloto automático do pit (na caixa/navegação por rota): limita velocidade, para NA SUA GARAGEM.
            Vector3 toBox = st.car.transform.position; toBox.y = 0f;
            if (!DriveRoute(st, LaneSpeedMs))
            {
                toBox = st.boxTarget - st.car.transform.position; toBox.y = 0f;
                float dBox = toBox.magnitude;
                float speed = st.car.ForwardSpeed;
                if (dBox > 5.5f && st.stopTimer <= 0f)
                {
                    if ((st.isHuman && st.stillTimer > 1.0f && dBox < 24f) || dBox < 8.5f)
                    {
                        var pose = PitSpawnPose(e.gridSlot);
                        st.car.ResetTo(pose.position, pose.rotation);
                        dBox = 0f;
                    }
                    else
                    {
                        float steer = Vector3.SignedAngle(st.car.transform.forward, toBox.normalized, Vector3.up) / 30f;
                        st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
                        float target = Mathf.Clamp(dBox * 0.38f, 2.0f, 5.0f);
                        st.car.extThrottle = speed < target ? 0.45f : 0f;
                        st.car.extBrake = speed > target + 0.5f ? 1f : 0f;
                        return;
                    }
                }
                if (st.stopTimer <= 0f)
                {
                    var pose = PitSpawnPose(e.gridSlot);
                    st.car.ResetTo(pose.position, pose.rotation);
                }
                ApplyHold(st);
                st.stopTimer += Time.deltaTime;
                if (st.stopTimer >= StopSeconds)
                {
                    st.wear = 0f;
                    for (int wi = 0; wi < 4; wi++) st.wearW[wi] = 0f;
                    st.car.Repair();
                    st.stopTimer = 0f;
                    st.car.extThrottle = 0f;
                    if (!st.isHuman)
                    {
                        if (st.compound < 2 && Random.value < 0.6f) st.compound++;
                        st.plannedLap = PlanNextStop(st, Random.Range(0, 3));
                    }
                    if (PracticeSession)
                    {
                        st.holding = true;
                        st.releaseTimer = st.isHuman ? 3f : Random.Range(24f, 52f) + (e.gridSlot % 4) * 5f;
                        ApplyHold(st);
                        if (st.isHuman)
                        {
                            Time.timeScale = 1f;
                            RaceX.UI.CareerFlow.Open();
                        }
                    }
                    else
                    {
                        Release(st);
                    }
                }
            }
    }
}
}
