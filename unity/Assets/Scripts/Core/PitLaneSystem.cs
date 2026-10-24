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
        }

        RaceManager race;
        TrackSpline spline;
        readonly List<CarState> cars = new List<CarState>();
        float laneStart;
        float laneLength = 140f;
        Vector3 boxPos;
        public bool Enabled { get; private set; }

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

        /// <summary>Posição de largada dentro do pit lane (treinos): fila em DUAS COLUNAS ao longo da faixa,
        /// com espaço real entre os carros — 24 carros cabem sem ficarem colados.</summary>
        public Pose PitSpawnPose(int slot)
        {
            float s = laneStart + laneLength * 0.10f + (slot / 2) * 9f;
            var p = spline.PoseAtDistance(s);
            p.position += -(p.rotation * new Vector3(laneOffset, 0f, 0f));
            // Coluna alternada: ±2,8 m do centro do lane (faixa de 12 m comporta 2 colunas).
            p.position += p.rotation * Vector3.right * ((slot % 2 == 0) ? 2.8f : -2.8f);
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
                    // Saída EM FILA: cada carro solto com atraso (nunca todos de uma vez). O humano sai em 5 s.
                    st.releaseTimer = e.isHuman ? 5f : 5f + ++slot * 14f;
                    st.holding = true;
                    ApplyHold(st);
                }
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

        /// <summary>Libera o carro para a pista (fim da espera no pit).</summary>
        void Release(CarState st)
        {
            st.holding = false;
            st.car.pitControlled = false;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
            var ai = st.car != null ? st.car.GetComponent<CarAI>() : null;
            if (ai != null) ai.OnPracticeRelease();
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
                verts.Add(p.position - right * (laneOffset + 6f) + Vector3.up * 0.06f);
                verts.Add(p.position - right * (laneOffset - 6f) + Vector3.up * 0.06f);
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
            int boxCount = 10;
            float boxSize = 11f;
            for (int i = 0; i < boxCount; i++)
            {
                float s = Mathf.Repeat(laneStart + laneLength * 0.5f + (i - boxCount / 2f) * boxSize, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                Vector3 laneCenter = p.position - right * laneOffset;

                // Piso demarcado do box (carpet), NA FRENTE da garagem (fora do lane — não fica placa no meio do caminho).
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = "PitFloor" + i;
                f.transform.SetParent(transform, false);
                f.transform.position = laneCenter - right * 8f + Vector3.up * 0.02f;
                f.transform.rotation = p.rotation;
                f.transform.localScale = new Vector3(8f, 0.04f, 8f);
                f.GetComponent<MeshRenderer>().sharedMaterial = floor;

                // Prédio de garagens atrás do pit lane, porta para o pit (garagem ~7x10 m por equipe, FIA).
                // O prédio fica TOTALMENTE fora do lane (borda externa −20 m; antes invadia metade da faixa).
                Vector3 bld = laneCenter - right * 11f;
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "PitBuilding" + i;
                g.transform.SetParent(transform, false);
                g.transform.position = bld + Vector3.up * 2.4f;
                g.transform.rotation = p.rotation;
                g.transform.localScale = new Vector3(boxSize - 1f, 4.8f, 10f);
                g.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Portão na face do prédio VIRADA PARA O LANE (−20 m): fino no transversal, largo ao longo da pista.
                var dr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dr.name = "PitDoor" + i;
                dr.transform.SetParent(transform, false);
                dr.transform.position = bld + right * 5.05f + Vector3.up * 1.7f;
                dr.transform.rotation = p.rotation;
                dr.transform.localScale = new Vector3(0.25f, 3.4f, boxSize - 2f);
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
                post1.GetComponent<MeshRenderer>().sharedMaterial = building;
                var post2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post2.name = "GantryPost" + k + "b";
                post2.transform.SetParent(transform, false);
                post2.transform.position = gp.position - gr * 20.5f + Vector3.up * 2.5f;
                post2.transform.rotation = gp.rotation;
                post2.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                post2.GetComponent<MeshRenderer>().sharedMaterial = building;
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = "GantryBeam" + k;
                beam.transform.SetParent(transform, false);
                beam.transform.position = gp.position - gr * 14f + Vector3.up * 5.2f;
                beam.transform.rotation = gp.rotation;
                // Viga TRANSVERSAL ao lane (13,5 m de −7,25 a −20,75) — antes estava 25 m AO LONGO da pista.
                beam.transform.localScale = new Vector3(13.5f, 0.6f, 0.6f);
                beam.GetComponent<MeshRenderer>().sharedMaterial = building;
            }
            // SETAS grandes no chão da entrada e da saída (para achar e entrar no pit).
            for (int k = 0; k < 2; k++)
            {
                float as_ = Mathf.Repeat(k == 0 ? entryS + 8f : exitS - 8f, spline.Length);
                var ap = spline.PoseAtDistance(as_);
                Vector3 ar = ap.rotation * Vector3.right;
                Vector3 center = ap.position - ar * laneOffset + Vector3.up * 0.06f;
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
                // Fila do treino: parado no pit até chegar a vez de sair.
                if (st.holding)
                {
                    st.releaseTimer -= Time.deltaTime;
                    if (st.releaseTimer <= 0f) Release(st);
                    else ApplyHold(st);
                    continue;
                }
                if (!st.inPit) UpdateWear(st);
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
            if (!st.inPit)
            {
                float dist = race.Track.Checkpoints.RaceDistance(st.car);
                float rel = Mathf.Repeat(dist - laneStart, spline.Length);
                if (!st.isHuman && !st.requested && race.Settings.laps >= 4 && race.GetProgress(e).lap >= st.plannedLap && rel < laneLength)
                    st.requested = true;
                if (st.requested && rel < 40f)
                {
                    st.inPit = true;
                    st.requested = false;
                    st.car.pitControlled = true;
                }
                return;
            }

            // Piloto automático do pit: desce para a faixa, limita velocidade, para na caixa.
            Vector3 toBox = boxPos - st.car.transform.position; toBox.y = 0f;
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
                    st.inPit = false;
                    st.car.pitControlled = false;
                    st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
                    if (!st.isHuman)
                    {
                        // Estratégia: troca de composto (mais duro se desgastou rápido) e planeja a PRÓXIMA parada — corridas longas = múltiplos pits.
                        if (st.compound < 2 && Random.value < 0.6f) st.compound++;
                        st.plannedLap = PlanNextStop(st, Random.Range(0, 3));
                    }
                    if (PracticeSession && !st.isHuman)
                    {
                        // Treino: recicla — fica no box um tempo e sai de novo tentar volta melhor.
                        st.holding = true;
                        st.releaseTimer = Random.Range(15f, 30f);
                        ApplyHold(st);
                    }
                }
            }
        }
    }
}
