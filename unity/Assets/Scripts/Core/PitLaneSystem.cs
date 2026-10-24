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
            public bool exiting;      // saída guiada: piloto automático leva até a rampa de SAÍDA
            public Vector3 boxTarget; // caixa do PRÓPRIO carro (dentro da sua garagem)
            public readonly List<Vector3> route = new List<Vector3>(); // rota por waypoints (entrada/saída)
            public int routeIdx;      // waypoint atual da rota
        }

        RaceManager race;
        TrackSpline spline;
        readonly List<CarState> cars = new List<CarState>();
        float laneStart;
        float laneLength = 140f;
        Vector3 boxPos;
        public bool Enabled { get; private set; }
        /// <summary>Total de boxes/garagens ao longo do lane (setado pelo bootstrap antes dos spawns).</summary>
        public int boxCount = 24;

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
            laneLength = Mathf.Clamp(best * 0.75f, 90f, 150f);
            var mid = spline.PoseAtDistance(laneStart + laneLength * 0.5f);
            boxPos = mid.position - mid.rotation * new Vector3(laneOffset, 0f, 0f) + Vector3.up * 0.1f;
            BuildGeometry();
            InitCars();
        }

        /// <summary>Treino livre (sex/sáb): saída escalonada + ciclos de pit. Identificado por voltas "infinitas".</summary>
        public static bool PracticeSession => instance != null && instance.race != null && instance.race.Settings.laps >= 900;

        /// <summary>Posição de largada dentro do pit lane (treinos): 2 colunas alternadas com 12,5 m
        /// entre carros da mesma fileira e ±1,8 m do centro (4,2 m de folga até cada parede).</summary>
        /// <summary>Largada do treino: cada carro nasce DENTRO DO SEU PRÓPRIO BOX (garagem), um por box
        /// ao longo de todo o lane — ninguém nasce colado em ninguém (fim do amontoado na entrada).</summary>
        public Pose PitSpawnPose(int slot)
        {
            // MESMA fórmula do boxTarget (InitCars): box do carro = gridSlot — nasce exatamente na própria caixa.
            float bs = Mathf.Max(4f, (laneLength - 30f) / Mathf.Max(10, boxCount));
            float firstS = laneStart + 15f;
            var p = spline.PoseAtDistance(Mathf.Repeat(firstS + slot * bs + bs * 0.5f, spline.Length));
            Vector3 right = p.rotation * Vector3.right;
            p.position = p.position - right * (laneOffset + 12f); // dentro da garagem (frente aberta para o lane)
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
            int slot = 0;
            foreach (var e in race.entries)
            {
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
                // Caixa do PRÓPRIO carro: mesma fórmula do PitSpawnPose — nasce e tem alvo no MESMO box.
                float bs = Mathf.Max(4f, (laneLength - 30f) / Mathf.Max(10, boxCount));
                int bIdx = Mathf.Clamp(e.gridSlot, 0, Mathf.Max(0, boxCount - 1));
                float firstS = laneStart + 15f;
                var bp = spline.PoseAtDistance(Mathf.Repeat(firstS + bIdx * bs + bs * 0.5f, spline.Length));
                Vector3 bRight = bp.rotation * Vector3.right;
                st.boxTarget = bp.position - bRight * (laneOffset + 12f) + Vector3.up * 0.1f;
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

        /// <summary>Libera o carro da caixa: SAÍDA GUIADA por ROTA (nunca linha reta):
        /// corredor do lane (−14 m) até o fim, então a rampa de saída e SÓ ENTÃO a pista.</summary>
        void Release(CarState st)
        {
            st.holding = false;
            st.exiting = true;
            st.car.pitControlled = true; // mantém o controle até chegar na pista
            st.route.Clear();
            float sNow = LaneSOf(st.car);
            // 1º waypoint: centro do lane à frente (alinhado ao corredor).
            st.route.Add(spline.PoseAtDistance(Mathf.Repeat(sNow + 30f, spline.Length)).position - (spline.PoseAtDistance(Mathf.Repeat(sNow + 30f, spline.Length)).rotation * new Vector3(laneOffset, 0f, 0f)));
            // 2º: fim do lane (ainda no corredor).
            var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength - 5f, spline.Length));
            st.route.Add(pe.position - pe.rotation * new Vector3(laneOffset, 0f, 0f));
            // 3º: boca da rampa de saída (meio caminho até a pista).
            var px = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 12f, spline.Length));
            st.route.Add(Vector3.Lerp(px.position - px.rotation * new Vector3(laneOffset, 0f, 0f), px.position, 0.6f) + Vector3.up * 0.05f);
            // 4º: NA PISTA (além da saída, já no traçado).
            st.route.Add(px.position + px.rotation * Vector3.forward * 15f);
            st.routeIdx = 0;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
        }

        /// <summary>Distância ao longo da spline do carro no pit (para montar a rota).</summary>
        float LaneSOf(CarController c)
        {
            float d = race.Track.Checkpoints.RaceDistance(c);
            return Mathf.Repeat(d, spline.Length);
        }

        /// <summary>Pilota por ROTA (waypoints em sequência). Retorna false quando a rota acabou.</summary>
        bool DriveRoute(CarState st, float speedLimit)
        {
            if (st.routeIdx >= st.route.Count) return false;
            Vector3 target = st.route[st.routeIdx];
            Vector3 to = target - st.car.transform.position; to.y = 0f;
            if (to.magnitude < 7f) { st.routeIdx++; return DriveRoute(st, speedLimit); }
            float steer = Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up) / 30f;
            st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
            float targetSpeed = Mathf.Min(speedLimit, to.magnitude * 0.35f);
            float speed = st.car.ForwardSpeed;
            st.car.extThrottle = speed < targetSpeed ? 0.6f : 0f;
            st.car.extBrake = speed > targetSpeed + 2f ? 1f : 0f;
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
            // 24 boxes com garagem REAL (frente aberta), DENTRO da área livre do lane
            // (15 m de margem na entrada e na saída — rampas e pórtico nunca invadem boxes).
            int boxCount = race != null ? Mathf.Max(10, race.entries.Count) : 24;
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
                Vector3 garageCenter = laneCenter - right * 26f;
                // Piso dentro da garagem (1 cm — não levanta carro).
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = "PitFloor" + i;
                f.transform.SetParent(transform, false);
                f.transform.rotation = p.rotation;
                f.transform.position = garageCenter + Vector3.up * 0.005f;
                f.transform.localScale = new Vector3(boxSize - 1f, 0.02f, 12f);
                f.GetComponent<MeshRenderer>().sharedMaterial = floor;
                // Fundo (−32 m).
                var gB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gB.name = "PitBuilding" + i;
                gB.transform.SetParent(transform, false);
                gB.transform.position = laneCenter - right * 32f + Vector3.up * 2.4f;
                gB.transform.rotation = p.rotation;
                gB.transform.localScale = new Vector3(boxSize - 1f, 4.8f, 0.6f);
                var c_gB = gB.GetComponent<Collider>(); if (c_gB != null) c_gB.sharedMaterial = pitPhys;
                gB.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Laterais (esq/dir).
                for (int side = -1; side <= 1; side += 2)
                {
                    var gS = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gS.name = "PitBuilding" + i + (side < 0 ? "L" : "R");
                    gS.transform.SetParent(transform, false);
                    gS.transform.position = garageCenter + p.rotation * Vector3.forward * (side * (boxSize - 1f) * 0.5f) + Vector3.up * 2.4f;
                    gS.transform.rotation = p.rotation;
                    gS.transform.localScale = new Vector3(0.6f, 4.8f, 12f);
                    var c_gS = gS.GetComponent<Collider>(); if (c_gS != null) c_gS.sharedMaterial = pitPhys;
                    gS.GetComponent<MeshRenderer>().sharedMaterial = building;
                }
                // Teto.
                var gT = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gT.name = "PitRoof" + i;
                gT.transform.SetParent(transform, false);
                gT.transform.position = garageCenter + Vector3.up * 4.8f;
                gT.transform.rotation = p.rotation;
                gT.transform.localScale = new Vector3(boxSize - 1f, 0.4f, 12f);
                var c_gT = gT.GetComponent<Collider>(); if (c_gT != null) c_gT.sharedMaterial = pitPhys;
                gT.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Portão RECOLHIDO (no teto, sem bloquear a entrada) — visual do box.
                var dr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dr.name = "PitDoor" + i;
                dr.transform.SetParent(transform, false);
                dr.transform.position = laneCenter - right * 20f + Vector3.up * 4.6f;
                dr.transform.rotation = p.rotation;
                dr.transform.localScale = new Vector3(0.25f, 0.5f, boxSize - 1f);
                var c_dr = dr.GetComponent<Collider>(); if (c_dr != null) c_dr.sharedMaterial = pitPhys;
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
                var c_post1 = post1.GetComponent<Collider>(); if (c_post1 != null) c_post1.sharedMaterial = pitPhys;
                post1.GetComponent<MeshRenderer>().sharedMaterial = building;
                var post2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post2.name = "GantryPost" + k + "b";
                post2.transform.SetParent(transform, false);
                post2.transform.position = gp.position - gr * 20.5f + Vector3.up * 2.5f;
                post2.transform.rotation = gp.rotation;
                post2.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                var c_post2 = post2.GetComponent<Collider>(); if (c_post2 != null) c_post2.sharedMaterial = pitPhys;
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
            // Estados dos carros criados LAZY: o Setup roda antes de SpawnCar (entries vazio).
            if (cars.Count == 0 && race != null && race.entries.Count > 0) InitCars();
            if (race == null || race.State != RaceState.Racing) return;
            foreach (var e in race.entries)
            {
                var st = Find(e.car);
                if (st == null) continue;
                // Fila do treino: parado no pit até chegar a vez de sair (o UpdatePit trata holding/exiting).
                UpdatePit(st, e);
            }
        }

        void UpdateWear(CarState st)
        {
            float rate = CompoundWearRate[st.compound];
            float slip = st.car.MaxWheelSlip * 0.0018f;
            st.wear = Mathf.Clamp01(st.wear + (0.00006f + slip) * rate * Mathf.Clamp(st.car.SpeedKmh / 120f, 0.2f, 2f) * Time.deltaTime * 20f);
        }

        void UpdatePit(CarState st, RaceEntry e)
        {
            // SAÍDA GUIADA por ROTA (waypoints: corredor → rampa → pista) — sem atravessar fila nem muro.
            if (st.exiting)
            {
                if (DriveRoute(st, LaneSpeedKmh / 3.6f))
                {
                    // rota em andamento (DriveRoute já aplicou steer/throttle/brake)
                }
                else
                {
                    // Chegou NA PISTA pela rota: devolve o controle (a IA/humano segue o traçado).
                    st.exiting = false;
                    st.inPit = false;
                    st.car.pitControlled = false;
                    st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
                    var ai = st.car != null ? st.car.GetComponent<CarAI>() : null;
                    if (ai != null) ai.OnPracticeRelease();
                }
                return;
            }

            if (!st.inPit)
            {
                float dist = race.Track.Checkpoints.RaceDistance(st.car);
                float rel = Mathf.Repeat(dist - laneStart, spline.Length);
                // NPC: pit na volta planejada — pedido SÓ na boca da entrada (rel < 40 m), como na F1.
                // (Antes: qualquer ponto da zona do pit — poderia dar ré até a entrada em cima da pista.)
                if (!st.isHuman && !st.requested && race.Settings.laps >= 4 && race.GetProgress(e).lap >= st.plannedLap && rel < 40f)
                    st.requested = true;
                // Treino/classificação: ENTRADA AUTOMÁTICA — só se o carro está REALMENTE no corredor do pit
                // (desvio lateral > 7 m para o lado do pit), nunca para quem passa na pista principal.
                if (PracticeSession && !st.requested && !st.holding && !st.exiting && rel < laneLength && rel > 8f)
                {
                    var sp = spline.PoseAtDistance(Mathf.Repeat(dist, spline.Length));
                    float lat = Vector3.Dot(st.car.transform.position - sp.position, sp.rotation * Vector3.right);
                    if (lat < -7f) st.requested = true; // lado do pit: além da borda da pista
                }
                if (st.requested && (rel < 40f || PracticeSession))
                {
                    st.inPit = true;
                    st.requested = false;
                    st.car.pitControlled = true;
                    // ENTRADA por ROTA: boca da entrada (borda da pista) → corredor do lane → caixa.
                    st.route.Clear();
                    var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + 8f, spline.Length));
                    st.route.Add(pe.position - pe.rotation * new Vector3(6.5f, 0f, 0f));   // boca na borda da pista
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
            if (!DriveRoute(st, LaneSpeedKmh / 3.6f))
            {
                // Rota acabou: navega ao centro da caixa (últimos metros de aproximação).
                toBox = st.boxTarget - st.car.transform.position; toBox.y = 0f;
                float dBox = toBox.magnitude;
                float speed = st.car.ForwardSpeed;
                if (dBox > 5f)
                {
                    float steer = Vector3.SignedAngle(st.car.transform.forward, toBox.normalized, Vector3.up) / 30f;
                    st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
                    float target = Mathf.Min(LaneSpeedKmh / 3.6f, dBox * 0.35f);
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
                        st.inPit = false;
                        st.car.pitControlled = false;
                        st.car.extBrake = 0f; st.car.extSteer = 0f;
                    }
                }
            }
        }
    }
}
}
