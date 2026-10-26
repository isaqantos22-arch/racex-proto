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
            if (st != null && !st.inPit && !st.requested && !st.holding) st.requested = true;
        }

        /// <summary>Carro está no fluxo do pit (na caixa, na fila ou saindo guiado)? TrackBounds NÃO reseta.</summary>
        public static bool IsInPitFlow(CarController car)
        {
            if (instance == null) return false;
            var st = instance.Find(car);
            return st != null && (st.inPit || st.holding || st.exiting);
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
            // MESMA fórmula das garagens e do boxTarget: box = gridSlot, centro −28 m, de frente para a saída.
            if (boxCount < 4) boxCount = 16;
            float bs = (laneLength - 30f) / boxCount;
            float firstS = laneStart + 15f;
            var p = spline.PoseAtDistance(Mathf.Repeat(firstS + slot * bs + bs * 0.5f, spline.Length));
            Vector3 right = p.rotation * Vector3.right;
            p.position = p.position - right * 28f; // centro da garagem (abertura −20 m, fundo −36 m)
            // DE FRENTE PARA A ABERTURA: o carro olha para o corredor — nasce pronto para SAIR
            // (antes olhava ao longo da pista e o nariz ficava espremido na parede lateral: tremia e não saía).
            p.rotation = Quaternion.LookRotation(right, Vector3.up);
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
            // Cria estado para os carros QUE AINDA NÃO TÊM (o bootstrap spawna em lotes —
            // o humano primeiro, NPCs depois; rodar 1x só deixava a IA sem pit).
            int slot = cars.Count;
            foreach (var e in race.entries)
            {
                if (Find(e.car) != null) continue;
                var st = new CarState { car = e.car, isHuman = e.isHuman, compound = e.isHuman ? 1 : Random.value < 0.35f ? 0 : Random.value < 0.5f ? 1 : 2 };
                if (!e.isHuman) st.plannedLap = PlanNextStop(st, Random.Range(1, Mathf.Max(2, race.Settings.laps / 4)));
                if (practice)
                {
                    // Largada do treino: fila sai RÁPIDO (2,5 s entre carros — a sessão toda não pode esperar 5 min).
                    st.releaseTimer = e.isHuman ? 4f : 4f + ++slot * 2.5f;
                    st.holding = true;
                    st.inPit = true;
                    ApplyHold(st);
                }
                // Caixa do PRÓPRIO carro: centro da garagem (−28 m) — IGUAL ao PitSpawnPose (nascem alinhados).
                float bs = (laneLength - 30f) / boxCount;
                int bIdx = Mathf.Clamp(e.gridSlot, 0, Mathf.Max(0, boxCount - 1));
                float firstS = laneStart + 15f;
                st.boxS = Mathf.Repeat(firstS + bIdx * bs + bs * 0.5f, spline.Length);
                var bp = spline.PoseAtDistance(st.boxS);
                Vector3 bRight = bp.rotation * Vector3.right;
                st.boxTarget = bp.position - bRight * 28f + Vector3.up * 0.1f; // centro da garagem
                cars.Add(st);
            }
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

        /// <summary>Libera o carro da caixa: SAÍDA GUIADA por ROTA (nunca linha reta à toa):
        /// sai RETO da garagem pelo vão (−40 → corredor −14), segue o lane, rampa e SÓ ENTÃO a pista.</summary>
        void Release(CarState st)
        {
            st.holding = false;
            st.exiting = true;
            st.car.pitControlled = true; // mantém o controle até chegar na pista
            st.route.Clear();
            // WAYPOINTS CURTOS (a partir de ONDE O CARRO ESTÁ — nunca de ponto fixo longe):
            // 1b) LINHA RETA da garagem até o centro do corredor, na altura da PRÓPRIA caixa
            // (o carro anda reto até o lane e SÓ DEPOIS vira rumo à saída — nunca na diagonal).
            var pc = spline.PoseAtDistance(st.boxS);
            Vector3 laneAtBox = pc.position - pc.rotation * new Vector3(laneOffset, 0f, 0f);
            // 1) 6 m na DIREÇÃO DO CORREDOR (não no nariz do carro): se o carro girou/capotou
            //    na caixa, usar o forward dele criava rota para TRÁS — o piloto automático
            //    acelerava sozinho na contramão no pit (bug do respawn/contra-mão).
            Vector3 outDir = laneAtBox - st.car.transform.position; outDir.y = 0f;
            if (outDir.sqrMagnitude < 0.01f) outDir = st.car.transform.rotation * Vector3.forward;
            outDir.y = 0f; outDir.Normalize();
            st.route.Add(st.car.transform.position + outDir * 6f + Vector3.up * 0.05f);
            st.route.Add(laneAtBox);
            // 2) boca da rampa de saída no corredor;
            var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength - 6f, spline.Length));
            st.route.Add(pe.position - pe.rotation * new Vector3(laneOffset, 0f, 0f));
            // 3) meio da rampa de saída;
            var px = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 12f, spline.Length));
            st.route.Add(Vector3.Lerp(px.position - px.rotation * new Vector3(laneOffset, 0f, 0f), px.position, 0.6f) + Vector3.up * 0.05f);
            // 4) NA PISTA — ponto de merge ÚNICO por carro (derivado do boxS): antes todos
            //    convergiam para o MESMO ponto da pista e formavam o bolo/capotaria coletiva.
            float mergeDist = 10f + Mathf.Repeat(st.boxS * 0.37f, 1f) * 15f;
            st.route.Add(px.position + px.rotation * Vector3.forward * mergeDist);
            st.routeIdx = 0;
            st.stuckTimer = 0f; st.reverseTimer = 0f;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
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
            Vector3 target = st.route[st.routeIdx];
            Vector3 to = target - st.car.transform.position; to.y = 0f;
            if (float.IsNaN(to.x) || float.IsNaN(to.z) || (to.magnitude < 7f))
            {
                st.routeIdx++;
                st.stuckTimer = 0f;
                st.reverseCount = 0f;
                return DriveRoute(st, speedLimit);
            }
            // ANTI-TRAVAMENTO: encostou na cerca/parede e não anda há 3 s → manobra de RÉ (1,2 s)
            // e retoma a rota. Com a 1ª engatada, extThrottle negativo empurra para trás; se o carro
            // estava preso em Gear -1 (ré do jogador), um pulso curto de acelerador engata a 1ª.
            if (st.reverseTimer > 0f)
            {
                st.reverseTimer -= Time.deltaTime;
                if (st.car.Gear < 0)
                {
                    st.car.extThrottle = 0.3f; // engata a 1ª (ApplyDrive faz a troca); mal move o carro
                    st.reverseTimer = Mathf.Max(st.reverseTimer, 1.1f); // a ré de verdade começa depois
                }
                else
                {
                    st.car.extThrottle = -0.55f;
                }
                st.car.extBrake = 0f;
                st.car.extSteer = 0f;
                return true;
            }
            if (st.car.ForwardSpeed < 0.6f) st.stuckTimer += Time.deltaTime; else st.stuckTimer = 0f;
            if (st.stuckTimer > 3f)
            {
                st.stuckTimer = 0f;
                // Duas ré sem sair no MESMO waypoint: pula o waypoint — muda o ângulo
                // de aproximação e quebra o loop "bateu na estrutura → ré → bateu de novo".
                if (st.reverseCount >= 2f && st.routeIdx < st.route.Count)
                {
                    st.reverseCount = 0f;
                    st.routeIdx++;
                    return true;
                }
                st.reverseCount++;
                st.reverseTimer = Mathf.Min(2.5f, 1.2f + 0.4f * st.reverseCount); // ré mais longa a cada tentativa
                return true;
            }
            float steer = Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up) / 30f;
            if (float.IsNaN(steer)) steer = 0f;
            st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
            // REGRA DO USUÁRIO: no pit é para ser DEVAGAR (manobra) — devagar na PISTA é que não pode.
            // Alvo = distância até o waypoint (desacelera perto de cada um) com PISO de ~20 km/h
            // (o piso antigo era zero: waypoint curto = alvo 11-18 km/h + freio 100% = carro
            // travando/rastejando a 10 km/h e o pit não terminava nunca — provado no [PITWATCH]).
            // Freio agora é LEVE (0.4) e só com folga. Último waypoint = mesclagem na PISTA:
            // nesse o alvo é o ritmo cheio do lane (chegada na pista sem rastejar).
            bool ultimo = st.routeIdx >= st.route.Count - 1;
            float targetSpeed = ultimo ? speedLimit : Mathf.Max(5.5f, Mathf.Min(speedLimit, to.magnitude * 0.5f));
            float speed = st.car.ForwardSpeed;
            st.car.extThrottle = speed < targetSpeed ? 0.85f : 0f;
            st.car.extBrake = speed > targetSpeed + 6f ? 0.4f : 0f;
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
                // Fundo em −32 m, face aberta no −20 m; largura = boxSize, profundidade 12 m.
                Vector3 garageCenter = laneCenter - right * 28f;
                // Piso dentro da garagem (1 cm — não levanta carro). Profundidade 16 m (fundo −36 m).
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = "PitFloor" + i;
                f.transform.SetParent(transform, false);
                f.transform.rotation = p.rotation;
                f.transform.position = garageCenter + Vector3.up * 0.005f;
                f.transform.localScale = new Vector3(boxSize - 2.5f, 0.02f, 16f);
                f.GetComponent<MeshRenderer>().sharedMaterial = floor;
                f.AddComponent<TrackSurface>().type = SurfaceType.Asphalt; // grip de asfalto dentro da garagem
                // Fundo (−36 m) — folga de 2 m atrás do carro.
                var gB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gB.name = "PitBuilding" + i;
                gB.transform.SetParent(transform, false);
                gB.transform.position = laneCenter - right * 36f + Vector3.up * 2.4f;
                gB.transform.rotation = p.rotation;
                gB.transform.localScale = new Vector3(boxSize - 2.5f, 4.8f, 0.6f);
                var c_gB = gB.GetComponent<Collider>(); if (c_gB != null) c_gB.sharedMaterial = pitPhys;
                gB.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Laterais (esq/dir): (boxSize − 2,5 m)/2 − 0,3 m = folga lateral de ~1,25 m por lado (boxSize 16).
                for (int side = -1; side <= 1; side += 2)
                {
                    var gS = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gS.name = "PitBuilding" + i + (side < 0 ? "L" : "R");
                    gS.transform.SetParent(transform, false);
                    gS.transform.position = garageCenter + p.rotation * Vector3.forward * (side * (boxSize - 1.25f) * 0.5f) + Vector3.up * 2.4f;
                    gS.transform.rotation = p.rotation;
                    gS.transform.localScale = new Vector3(0.6f, 4.8f, 16f);
                    var c_gS = gS.GetComponent<Collider>(); if (c_gS != null) c_gS.sharedMaterial = pitPhys;
                    gS.GetComponent<MeshRenderer>().sharedMaterial = building;
                }
                // Teto.
                var gT = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gT.name = "PitRoof" + i;
                gT.transform.SetParent(transform, false);
                gT.transform.position = garageCenter + Vector3.up * 4.8f;
                gT.transform.rotation = p.rotation;
                gT.transform.localScale = new Vector3(boxSize - 2.5f, 0.4f, 16f);
                var c_gT = gT.GetComponent<Collider>(); if (c_gT != null) c_gT.sharedMaterial = pitPhys;
                gT.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Portão RECOLHIDO (no teto) — VISUAL APENAS: collider removido (não pode travar a saída).
                var dr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dr.name = "PitDoor" + i;
                dr.transform.SetParent(transform, false);
                dr.transform.position = laneCenter - right * 20f + Vector3.up * 4.6f;
                dr.transform.rotation = p.rotation;
                dr.transform.localScale = new Vector3(0.25f, 0.5f, boxSize - 1f);
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
                    var c_w = w.GetComponent<Collider>(); if (c_w != null) c_w.sharedMaterial = pitPhys;
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
            bool semEstado = !st.holding && !st.exiting && !st.inPit;
            float dW = race.Track.Checkpoints.RaceDistance(st.car);
            var spw = spline.PoseAtDistance(Mathf.Repeat(dW, spline.Length));
            float latW = Vector3.Dot(st.car.transform.position - spw.position, spw.rotation * Vector3.right);
            if (semEstado || Time.time - st.pitCtrlSince > 45f)
            {
                Debug.LogWarning($"[PITWATCH] LIBERADO FORÇADO {e.name}: holding={st.holding} exiting={st.exiting} inPit={st.inPit} lat={latW:0.0} kmh={st.car.SpeedKmh:0} t={Time.time - st.pitCtrlSince:0}s");
                st.holding = false; st.exiting = false; st.inPit = false;
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
            if (st.holding || st.exiting || st.inPit) return;
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
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
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
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
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
                // pit, lat < −9 m, em movimento) tem o fluxo assumido — auto até o próprio box.
                // PASSAR PERTO da boca, NA PISTA (lat ~0), NÃO captura ninguém.
                if (!st.inPit && !st.holding && !st.exiting && !st.requested && st.car.SpeedKmh > 5f)
                {
                    var spNow = spline.PoseAtDistance(Mathf.Repeat(dist, spline.Length));
                    float latIn = Vector3.Dot(st.car.transform.position - spNow.position, spNow.rotation * Vector3.right);
                    if (latIn < -9f && rel > 12f && rel < laneLength - 20f)
                    {
                        st.inPit = true;
                        st.requested = false;
                        st.car.pitControlled = true;
                        st.route.Clear();
                        var pa = spline.PoseAtDistance(Mathf.Repeat(rel + 25f, spline.Length));
                        st.route.Add(pa.position - pa.rotation * new Vector3(laneOffset, 0f, 0f)); // corredor avante
                        st.route.Add(st.boxTarget); // caixa da PRÓPRIA garagem
                        st.routeIdx = 0;
                        if (st.isHuman) Debug.Log("[PITWATCH] carro entrou dirigindo no corredor — fluxo assumido (auto até o box)");
                        return;
                    }
                }
                // REGRA DO USUÁRIO: o piloto automático da entrada SÓ é chamado quando o carro
                // ENTRA no pit (pedido via botão/tecla, ou dirigindo para dentro do corredor —
                // captura mais abaixo). PASSAR PERTO da boca NÃO liga mais o automático: isso
                // sequestrava a volta do jogador no treino (auto-request na aproximação).
                // Commit SÓ na APROXIMAÇÃO (rel > len−50: até 50 m ANTES da boca) ou JÁ DENTRO do
                // vão (rel < 8, no apron do lado do pit). Commit depois da boca (rel 8..40) era o
                // bug do loop: a rota apontava DE VOLTA à boca — o piloto automático virava à
                // direita, voltava à pista e refazia entrada/saída em loop.
                if (st.requested && (rel > spline.Length - 50f || rel < 8f))
                {
                    bool approaching = rel > spline.Length - 50f;
                    st.inPit = true;
                    st.requested = false;
                    st.car.pitControlled = true;
                    st.car.Repair(); // PIT CONSERTA: dano de batida zerado ao entrar (regra do usuário)
                    // ENTRADA por ROTA a partir de ONDE O CARRO ESTÁ (sem cortar caminho):
                    st.route.Clear();
                    var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + 8f, spline.Length));
                    Vector3 mouth = pe.position - pe.rotation * new Vector3(6.5f, 0f, 0f);
                    if (approaching)
                    {
                        // Vem pela PISTA: 6 m à frente (direção atual) → boca → corredor → box.
                        Vector3 fw = st.car.transform.rotation * Vector3.forward; fw.y = 0f; fw.Normalize();
                        st.route.Add(st.car.transform.position + fw * 6f + Vector3.up * 0.05f);
                        st.route.Add(mouth);   // boca na borda da pista
                    }
                    // (já passou da boca: SEM waypoint de boca — nunca volta; vai direto ao corredor)
                    st.route.Add(pe.position - pe.rotation * new Vector3(laneOffset, 0f, 0f)); // corredor (−14 m)
                    var pb = spline.PoseAtDistance(Mathf.Repeat(laneStart + 30f, spline.Length));
                    st.route.Add(pb.position - pb.rotation * new Vector3(laneOffset, 0f, 0f)); // corredor avante
                    st.route.Add(st.boxTarget);                                            // caixa da PRÓPRIA garagem
                    st.routeIdx = 0;
                }
                return;
            }

            // Fila do treino: parado na PRÓPRIA CAIXA (garagem) até chegar a vez de sair.
            if (st.holding)
            {
                st.releaseTimer -= Time.deltaTime;
                if (st.releaseTimer <= 0f) Release(st);
                else ApplyHold(st);
                return;
            }

            // Piloto automático do pit (na caixa/navegação por rota): limita velocidade, para NA SUA GARAGEM.
            Vector3 toBox = st.car.transform.position; toBox.y = 0f; // placeholder substituído abaixo
            if (!DriveRoute(st, LaneSpeedMs))
            {
                // Rota acabou: navega ao centro da caixa (últimos metros de aproximação).
                toBox = st.boxTarget - st.car.transform.position; toBox.y = 0f;
                float dBox = toBox.magnitude;
                float speed = st.car.ForwardSpeed;
                if (dBox > 5f)
                {
                    float steer = Vector3.SignedAngle(st.car.transform.forward, toBox.normalized, Vector3.up) / 30f;
                    st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
                    float target = Mathf.Min(LaneSpeedMs, dBox * 0.5f);
                    st.car.extThrottle = speed < target ? 0.6f : 0f;
                    st.car.extBrake = speed > target + 1f ? 1f : 0f;
                }
            else
            {
                st.car.extThrottle = 0f;
                st.car.extBrake = 1f;
                st.stopTimer += Time.deltaTime;
                if (st.stopTimer >= StopSeconds)
                {
                    st.wear = 0f;
                    for (int wi = 0; wi < 4; wi++) st.wearW[wi] = 0f; // mecânicos trocam AS QUATRO rodas
                    st.car.Repair(); // PIT CONSERTA: vale para ENTRADA DIRIGINDO também (antes só o pedido de botão chamava)
                    st.stopTimer = 0f;
                    st.car.extThrottle = 0f;
                    if (!st.isHuman)
                    {
                        // Estratégia: troca de composto (mais duro se desgastou rápido) e planeja a PRÓXIMA parada.
                        if (st.compound < 2 && Random.value < 0.6f) st.compound++;
                        st.plannedLap = PlanNextStop(st, Random.Range(0, 3));
                    }
                    if (PracticeSession)
                    {
                        // Treino: recicla — fica NA CAIXA (garagem) um tempo e sai de novo tentar volta melhor.
                        st.holding = true;
                        st.releaseTimer = st.isHuman ? 3f : Random.Range(15f, 30f);
                        ApplyHold(st);
                        if (st.isHuman)
                        {
                            // Estacionou CERTO no SEU box → abre o menu da carreira automaticamente
                            // (a sessão segue com o relógio real — como na F1).
                            Time.timeScale = 1f;
                            RaceX.UI.CareerFlow.Open();
                        }
                    }
                    else
                    {
                        // Corrida: stop concluído → SAÍDA GUIADA pela rota (igual ao treino):
                        // corredor → rampa → pista. (Antes soltava seco na garagem e a IA
                        // cortava sem guia pela grama — agora sai pelo caminho certo.)
                        Release(st);
                    }
                }
            }
        }
    }
}
}
