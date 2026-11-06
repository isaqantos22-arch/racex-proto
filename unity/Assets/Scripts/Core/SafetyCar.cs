using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RaceX.Core
{
    /// <summary>
    /// SAFETY CAR (v81) — procedimento resumido da F1 (pesquisa: regulamento esportivo e cobertura das corridas):
    /// • ENTRA quando há perigo que exige fiscais na pista e não dá para resolver com amarela/VSC: carro destruído
    ///   parado NA pista ou acidente com 2+ carros (perda total fora da pista continua sendo VSC).
    /// • Sai da SUA garagem no fim do pit lane, entra na pista pela saída do pit À FRENTE do líder (espera o líder se
    ///   aproximar) e anda no ritmo de SC: volta ~60% mais longa que a melhor volta da corrida (o ritmo é da direção de prova).
    /// • Sob SC: proibido ultrapassar (inclusive o SC), todos reduzem e formam fila atrás do líder; o pit fica aberto
    ///   (parada "barata" — a IA antecipa a parada planejada se ela estava perto).
    /// • Fica na pista pelo menos 2 voltas do líder e até o perigo sumir; então "SAFETY CAR NESTA VOLTA": as luzes
    ///   apagam e, no fim da volta, ele entra no pit e volta para a garagem. A RELARGADA vale na LINHA: ninguém
    ///   ultrapassa até o líder cruzar a linha de chegada (o líder dita o ritmo).
    /// • Corrida terminando sob SC: ele entra no fim da volta e os carros recebem a bandeirada sem ultrapassar.
    /// • DESDOBRAMENTO (regra desde 2022: TODOS os retardatários): com a fila formada e o perigo controlado sai
    ///   "RETARDATÁRIOS PODEM ULTRAPASSAR"; quem está 1+ volta atrás do líder passa a fila e o SC (sem punição) e vai
    ///   para o fim da fila. O SC entra no fim da volta SEGUINTE à mensagem (art. 55.13).
    /// O carro do SC é um GT do jogo guiado pela linha da pista (movimento cinemático: nunca erra nem roda).
    /// </summary>
    public class SafetyCar : MonoBehaviour
    {
        public enum Phase { Garage, Exiting, Leading, InThisLap, Entering }

        public static SafetyCar Instance { get; private set; }
        /// <summary>Bandeira de SC valendo (inclui o "entra nesta volta" e a espera pela relargada na linha).</summary>
        public static bool Deployed => Instance != null && (Instance.phase != Phase.Garage || Instance.resumePending);
        public static bool InThisLap => Instance != null && (Instance.phase == Phase.InThisLap || Instance.phase == Phase.Entering);
        public static bool RestartPending => Instance != null && Instance.phase == Phase.Garage && Instance.resumePending;
        public static Phase State => Instance != null ? Instance.phase : Phase.Garage;

        // ---------------- Janela de TV (picture-in-picture) ----------------
        // O carro do jogador costuma estar longe do SC (fim da fila): sem isso ninguém via o SC entrar na pista nem
        // voltar para os boxes. Uma câmera atrás do SC renderiza numa textura pequena (sem sombras/pós) que o HUD
        // mostra só nesses momentos.
        static RenderTexture pipRT;
        Camera pipCam;
        bool pipOn;
        float pipUntil;
        string pipLabel = "";
        public static bool PipActive => Instance != null && Instance.pipOn && !ReplaySystem.IsPlaying;
        public static Texture PipTexture => Instance != null ? pipRT : null;
        public static string PipLabel => Instance != null ? Instance.pipLabel : "";

        void SetPip(bool on)
        {
            if (on && pipCam == null)
            {
                try
                {
                    if (pipRT == null) { pipRT = new RenderTexture(480, 270, 16) { name = "ScPip" }; pipRT.Create(); }
                    var go = new GameObject("ScPipCam");
                    go.transform.SetParent(transform, false);
                    pipCam = go.AddComponent<Camera>();
                    pipCam.targetTexture = pipRT;
                    pipCam.fieldOfView = 48f;
                    pipCam.nearClipPlane = 0.3f;
                    pipCam.farClipPlane = 400f;
                    pipCam.depth = -5f;
                    var data = pipCam.GetUniversalAdditionalCameraData();
                    if (data != null) { data.renderShadows = false; data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; }
                }
                catch (System.Exception ex) { Debug.LogWarning("[SC] janela de TV desligada: " + ex.Message); pipCam = null; }
            }
            pipOn = on && pipCam != null;
            if (pipCam != null && pipCam.enabled != pipOn) pipCam.enabled = pipOn;
        }

        void LateUpdate()
        {
            if (!pipOn || pipCam == null || scCar == null) return;
            Vector3 f = scCar.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 pos = scCar.position - f * 7.5f + r * 2.2f + Vector3.up * 2.3f;
            Vector3 look = scCar.position + f * 3f + Vector3.up * 0.6f;
            pipCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }

        RaceManager race;
        TrackSpline sp;
        float L, laneStart, laneLength;
        Transform scCar;
        CarController scCtrl;
        readonly List<Renderer> lampA = new List<Renderer>(), lampB = new List<Renderer>();
        Material lampOn, lampOff;
        Phase phase = Phase.Garage;
        bool resumePending;
        int resumeLap;
        float s, v, deployedAt, waitSince;
        int leaderLapAtTrack = -1;
        bool unlapSent;
        int unlapMsgLap = -1;
        readonly HashSet<CarController> unlapping = new HashSet<CarController>();
        float clearSince = -1f;   // desde quando a pista está sem perigo (s)

        // PILOTO AUTOMÁTICO DO JOGADOR sob SC: assume ao chegar na fila (ou no SC, se for o líder) e só devolve na
        // BANDEIRA VERDE. O carro segue com a física normal (rodas, câmbio e tração de verdade, sem modo "fantasma"
        // de movimento) e com as colisões CONTRA OS OUTROS CARROS desligadas — não há como bater em ninguém. As
        // colisões só voltam quando não houver carro a menos de 7 m. Nenhum limitador de pit é tocado.
        class Auto
        {
            public RaceEntry e;
            public CarAI ai;
            public RaceX.Input.PlayerInputHandler input;
            public bool ignoring;
            public bool released;
            public bool pitNotified;
        }
        readonly List<Auto> autos = new List<Auto>();
        public static bool AutopilotOn(CarController c)
        {
            if (Instance == null || c == null) return false;
            foreach (var a in Instance.autos) if (a.e.car == c && !a.released) return true;
            return false;
        }
        readonly List<Vector2> path = new List<Vector2>(); // (s, lateral) do caminho do pit (saída/entrada)
        float pathPos;
        float[] vCurve;
        Pose garagePose;
        string reason = "";

        const float PitSpeed = 22f;   // 80 km/h no pit
        const float MaxSpeed = 88f;   // teto físico (AMG GT Black Series: 325 km/h); o ritmo real vem de SetPace
        const float LatAcc = 11f;     // aderência lateral de um GT (~1,1 g)

        public static void Create(RaceManager race)
        {
            if (race == null || race.Settings == null || race.Settings.laps >= 900 || race.Settings.mode == GameMode.Online) return;
            if (!PitLaneSystem.LaneInfo(out float ls, out float ll)) return;
            var go = new GameObject("SafetyCar");
            var sc = go.AddComponent<SafetyCar>();
            sc.race = race;
            sc.sp = race.Track.Spline;
            sc.L = sc.sp.Length;
            sc.laneStart = ls;
            sc.laneLength = ll;
            Instance = sc;
            try { sc.Build(); }
            catch (System.Exception ex) { Debug.LogWarning("[SC] safety car desativado: " + ex.Message); Destroy(go); Instance = null; }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------- Construção: garagem (visual) + carro ----------------
        void Build()
        {
            garagePose = PoseAt(laneStart + laneLength - 8f, -26f);
            BuildGarage();
            var spec = CarSpec.CreateDefault(CarModel.R8);
            scCtrl = CarFactory.Create(spec, 0, "SafetyCar", garagePose.position + Vector3.up * 0.4f, garagePose.rotation);
            scCar = scCtrl.transform;
            var vis = scCtrl.GetComponent<CarVisuals>();
            if (vis != null) vis.SetColor(new Color(0.78f, 0.8f, 0.83f));
            scCtrl.Body.isKinematic = true;
            scCtrl.Body.detectCollisions = false;
            // Guiado por posição a cada quadro: sem interpolação (com ela o SC andava aos saltos, como os carros na
            // volta de apresentação).
            scCtrl.Body.interpolation = RigidbodyInterpolation.None;
            scCtrl.inputLocked = true;
            scCtrl.enabled = false;
            foreach (var ai in scCtrl.GetComponents<CarAI>()) Destroy(ai);
            var tb = scCtrl.GetComponent<TrackBounds>(); if (tb != null) Destroy(tb);
            // O SC não sofre dano (é guiado por posição; um toque de IA não pode "destruir" o safety car).
            var dmgSc = scCtrl.GetComponent<RaceX.Car.CarDamage>(); if (dmgSc != null) Destroy(dmgSc);
            // ALTURA DE RODAGEM: o SC é posicionado pela linha da pista; o pivô do carro fica na altura do eixo, não do
            // chão — com só +5 cm ele andava AFUNDADO no asfalto (print do usuário). Mede a distância do pivô até o
            // ponto mais baixo do modelo (pneus) e apoia o carro no chão.
            {
                var bb = new Bounds(scCar.position, Vector3.zero);
                bool any = false;
                foreach (var r in scCar.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
                    if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                }
                if (any) rideLift = Mathf.Clamp(scCar.position.y - bb.min.y, 0f, 1.2f) + 0.01f;
            }
            BuildLightBar();
            PrecomputeSpeeds();
            SetLamps(false, false);
            Debug.Log("[SC] safety car pronto na garagem (fim do pit lane)");
        }

        Pose PoseAt(float dist, float lat)
        {
            var p = sp.PoseAtDistanceSmooth(Mathf.Repeat(dist, L), lat);
            return new Pose(p.position, p.rotation);
        }

        void BuildGarage()
        {
            var root = new GameObject("SafetyCarGarage").transform;
            root.SetPositionAndRotation(garagePose.position, garagePose.rotation);
            var wall = MaterialLibrary.Get("ScGarageWall", new Color(0.82f, 0.84f, 0.88f), 0.2f);
            var roof = MaterialLibrary.Get("ScGarageRoof", new Color(0.22f, 0.24f, 0.28f), 0.3f);
            var sign = MaterialLibrary.Get("ScGarageSign", new Color(1f, 0.62f, 0.05f), 0.4f, 0f, new Color(1f, 0.55f, 0.05f));
            void Block(string n, Vector3 lp, Vector3 sc, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = n;
                var col = g.GetComponent<Collider>(); if (col != null) Destroy(col); // só visual: ninguém passa por ali
                g.transform.SetParent(root, false);
                g.transform.localPosition = lp;
                g.transform.localScale = sc;
                g.GetComponent<Renderer>().sharedMaterial = m;
            }
            // Garagem aberta para o corredor (lado +X local = pista), 9 m de frente × 12 m de fundo.
            Block("WallL", new Vector3(0f, 2f, -4.6f), new Vector3(12f, 4f, 0.3f), wall);
            Block("WallR", new Vector3(0f, 2f, 4.6f), new Vector3(12f, 4f, 0.3f), wall);
            Block("Back", new Vector3(-6f, 2f, 0f), new Vector3(0.3f, 4f, 9.4f), wall);
            Block("Roof", new Vector3(0f, 4.1f, 0f), new Vector3(12.4f, 0.25f, 9.8f), roof);
            Block("Sign", new Vector3(6.1f, 3.6f, 0f), new Vector3(0.15f, 0.7f, 6f), sign);
            // Orientação = a da pista: +X local aponta para o corredor do pit (abertura), +Z segue a pista.
        }

        void BuildLightBar()
        {
            var b = new Bounds(scCar.position, Vector3.zero);
            foreach (var r in scCar.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            float top = b.max.y - scCar.position.y;
            lampOn = MaterialLibrary.Get("ScLampOn", new Color(1f, 0.6f, 0.05f), 0.5f, 0f, new Color(1f, 0.55f, 0.05f));
            lampOff = MaterialLibrary.Get("ScLampOff", new Color(0.25f, 0.18f, 0.08f), 0.4f);
            for (int i = 0; i < 2; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "ScLamp" + i;
                var col = g.GetComponent<Collider>(); if (col != null) Destroy(col);
                g.transform.SetParent(scCar, false);
                g.transform.localPosition = new Vector3(i == 0 ? -0.32f : 0.32f, top + 0.06f, -0.2f);
                g.transform.localScale = new Vector3(0.55f, 0.1f, 0.22f);
                (i == 0 ? lampA : lampB).Add(g.GetComponent<Renderer>());
            }
        }

        void SetLamps(bool a, bool b)
        {
            foreach (var r in lampA) if (r != null) r.sharedMaterial = a ? lampOn : lampOff;
            foreach (var r in lampB) if (r != null) r.sharedMaterial = b ? lampOn : lampOff;
        }

        /// <summary>Velocidade máxima do SC em cada ponto da pista (curvatura → v = √(a·R)) com freada antecipada.</summary>
        void PrecomputeSpeeds()
        {
            int n = sp.Count;
            vCurve = new float[n];
            int look = Mathf.Max(2, Mathf.RoundToInt(12f / Mathf.Max(0.5f, sp.spacing)));
            for (int i = 0; i < n; i++)
            {
                float ang = Vector3.Angle(sp.tangents[sp.Wrap(i - look)], sp.tangents[sp.Wrap(i + look)]) * Mathf.Deg2Rad;
                float kappa = ang / Mathf.Max(1f, 2f * look * sp.spacing);
                vCurve[i] = Mathf.Min(MaxSpeed, kappa > 1e-4f ? Mathf.Sqrt(LatAcc / kappa) : MaxSpeed);
            }
            // Freada: para trás, v_i ≤ √(v_{i+1}² + 2·a·ds) (duas passadas cobrem a volta).
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--)
                {
                    float next = vCurve[sp.Wrap(i + 1)];
                    vCurve[i] = Mathf.Min(vCurve[i], Mathf.Sqrt(next * next + 2f * 8f * sp.spacing));
                }
            // ACELERAÇÃO: para frente, v_i ≤ √(v_{i−1}² + 2·a·ds) com ~4,5 m/s² (GT de rua; o AMG GT faz 0–200 km/h em ~9 s).
            // Sem isso o perfil "saía" de cada curva já no máximo e a volta do SC dava 53,4 s, MAIS RÁPIDA que a dos F1
            // (58,7 s) — a fila nunca o alcançava e ele nunca aparecia na tela (teste 30/10).
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    float prev = vCurve[sp.Wrap(i - 1)];
                    vCurve[i] = Mathf.Min(vCurve[i], Mathf.Sqrt(prev * prev + 2f * 4.5f * sp.spacing));
                }
        }

        float paceScale = 1f;
        float rideLift = 0.05f;   // pivô → chão (medido do modelo)
        float CurveSpeed(float dist) => vCurve[sp.Wrap(Mathf.RoundToInt(Mathf.Repeat(dist, L) / sp.spacing))] * paceScale;

        /// <summary>Ritmo do SC: a volta dele dura SafetyCarLapFactor × a volta de corrida — cronometragem oficial de 7 períodos de SC
        /// (2017–2025): 1,58 a 1,78, mediana ~1,70 (pesquisa de 02/11, research_notes/…/safety_car.md; antes 1,6, pela Mercedes).
        /// Referência = melhor volta da corrida; na 1ª volta (ainda sem melhor volta) = volta estimada pela velocidade média do
        /// líder até agora. Antes, sem melhor volta, o SC andava no limite físico dele — mais rápido que os F1.</summary>
        public const float SafetyCarLapFactor = 1.7f;
        void SetPace()
        {
            float best = float.MaxValue;
            foreach (var e in race.entries) if (e.bestLap > 1f && e.bestLap < best) best = e.bestLap;
            float t0 = 0f;
            for (int i = 0; i < vCurve.Length; i++) t0 += sp.spacing / Mathf.Max(1f, vCurve[i]);
            float refLap = best;
            string src = "melhor volta";
            if (best == float.MaxValue)
            {
                var ld = Leader();
                float rt = race.RaceTime;
                if (ld != null && rt > 10f && ld.raceDistance > 200f) { refLap = L * rt / ld.raceDistance; src = "média do líder"; }
                else { refLap = t0; src = "estimativa"; }
            }
            paceScale = Mathf.Clamp(t0 / (refLap * SafetyCarLapFactor), 0.3f, 1f);
            Debug.Log("[SC] ritmo: volta do SC " + (t0 / paceScale).ToString("0.0") + " s = " + SafetyCarLapFactor.ToString("0.0") + " × " + refLap.ToString("0.0") + " s (" + src + ")");
        }

        // ---------------- Acionamento ----------------
        /// <summary>Aciona o SC (chamado pela direção de prova). false = não cabe agora (já na pista, fim de corrida…).</summary>
        public static bool Deploy(string why)
        {
            var sc = Instance;
            if (sc == null || sc.race == null || sc.race.State != RaceState.Racing) return false;
            if (sc.phase != Phase.Garage || sc.resumePending) return false;
            var leader = sc.Leader();
            if (leader == null) return false;
            int lapsLeft = sc.race.Settings.laps - sc.race.LapOf(leader.car);
            if (lapsLeft < 2) return false;
            sc.reason = why;
            sc.phase = Phase.Exiting;
            sc.deployedAt = Time.time;
            sc.waitSince = Time.time;
            sc.leaderLapAtTrack = -1;
            sc.unlapSent = false; sc.unlapMsgLap = -1; sc.unlapping.Clear(); sc.clearSince = -1f;
            sc.SetPace();
            sc.BuildExitPath();
            Debug.Log("[SC] SAFETY CAR acionado: " + why);
            return true;
        }

        RaceEntry Leader()
        {
            RaceEntry best = null;
            foreach (var e in race.entries)
            {
                if (e.car == null || e.retired || e.finished) continue;
                if (best == null || e.raceDistance > best.raceDistance) best = e;
            }
            return best;
        }

        float SplineDist(Vector3 pos) => sp.ClosestIndex(pos) * sp.spacing;

        void BuildExitPath()
        {
            path.Clear();
            float g = laneStart + laneLength - 8f;
            path.Add(new Vector2(g, -26f));
            path.Add(new Vector2(g + 6f, -15f));
            path.Add(new Vector2(laneStart + laneLength + 20f, -12f));
            path.Add(new Vector2(laneStart + laneLength + 55f, -3f));
            path.Add(new Vector2(laneStart + laneLength + 75f, 0f));
            pathPos = 0f;
        }

        void BuildEntryPath()
        {
            // Distâncias SEMPRE crescentes a partir do SC (antes "s" passava do comprimento da volta e o caminho até
            // a entrada era calculado para trás: o SC andava de ré pela pista e nunca entrava no pit).
            path.Clear();
            float e1 = s + Mathf.Repeat(laneStart - 5f - s, L);
            // Pela FAIXA RÁPIDA (−10,3 m, a mesma linha dos carros): a faixa interna é onde as equipes param os carros e trabalham.
            path.Add(new Vector2(s, 0f));
            path.Add(new Vector2(e1, -9f));
            path.Add(new Vector2(e1 + 20f, -10.3f));
            path.Add(new Vector2(e1 + laneLength - 14f, -10.3f));
            path.Add(new Vector2(e1 + laneLength - 6f, -20f));
            path.Add(new Vector2(e1 + laneLength - 3f, -26f));
            pathPos = 0f;
        }

        /// <summary>Anda pelo caminho (s, lateral) e devolve true no fim.</summary>
        bool FollowPath(float speed, float dt)
        {
            pathPos += speed * dt;
            float acc = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                float seg = Mathf.Max(0.5f, Vector2.Distance(a, b));
                if (pathPos <= acc + seg)
                {
                    float f = (pathPos - acc) / seg;
                    Vector2 q = Vector2.Lerp(a, b, f);
                    var p = sp.PoseAtDistanceSmooth(Mathf.Repeat(q.x, L), q.y);
                    Vector2 q2 = Vector2.Lerp(a, b, Mathf.Min(1f, f + 0.05f));
                    var p2 = sp.PoseAtDistanceSmooth(Mathf.Repeat(q2.x + 0.5f, L), q2.y);
                    Vector3 dir = p2.position - p.position; dir.y = 0f;
                    scCar.SetPositionAndRotation(p.position + Vector3.up * rideLift, dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : p.rotation);
                    return false;
                }
                acc += seg;
            }
            return true;
        }

        void PlaceOnTrack()
        {
            var p = sp.PoseAtDistanceSmooth(Mathf.Repeat(s, L));   // interpolada: sem degraus de 2 m
            scCar.SetPositionAndRotation(p.position + Vector3.up * rideLift, p.rotation);
        }

        /// <summary>Retardatários com a colisão desligada: religa quando terminou de desdobrar e não há carro a menos de 7 m.</summary>
        readonly HashSet<CarController> ghosted = new HashSet<CarController>();
        float ghostCheckT;

        void RestoreGhosts()
        {
            if (ghosted.Count == 0) return;
            ghostCheckT -= Time.deltaTime;
            if (ghostCheckT > 0f) return;
            ghostCheckT = 0.25f;
            var done = new List<CarController>();
            foreach (var c in ghosted)
            {
                if (c == null) { done.Add(c); continue; }
                if (unlapping.Contains(c)) continue;
                bool near = false;
                foreach (var o in race.entries)
                    if (o.car != null && o.car != c && (o.car.transform.position - c.transform.position).sqrMagnitude < 49f) { near = true; break; }
                if (near) continue;
                SetIgnore(c, false);
                done.Add(c);
            }
            foreach (var c in done) ghosted.Remove(c);
        }

        void Update()
        {
            if (race == null || scCar == null) return;
            try { RestoreGhosts(); } catch (System.Exception ex) { Debug.LogWarning("[SC] colisões: " + ex.Message); }
            if (ReplaySystem.IsPlaying) { if (pipOn) SetPip(false); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Janela de TV: saindo da garagem para a pista (+3 s já na pista) e recolhendo aos boxes (+1,5 s).
            if (phase == Phase.Exiting && pathPos > 0f) { pipUntil = Time.time + 3f; pipLabel = "SAFETY CAR ENTRANDO NA PISTA"; }
            else if (phase == Phase.Entering) { pipUntil = Time.time + 1.5f; pipLabel = "SAFETY CAR RECOLHENDO AOS BOXES"; }
            bool wantPip = Time.time < pipUntil;
            if (wantPip != pipOn) SetPip(wantPip);
            try { UpdateAutopilot(); } catch (System.Exception ex) { Debug.LogWarning("[SC] piloto automático: " + ex.Message); }
            bool blink = Mathf.Repeat(Time.time, 0.6f) < 0.3f;
            // Som do motor pelo ritmo (o controlador físico fica desligado: o SC é guiado pela linha).
            if (scCtrl.spec != null) scCtrl.SetReplayRpm(Mathf.Lerp(1800f, scCtrl.spec.rpmLimit * 0.8f, Mathf.Clamp01(v / MaxSpeed)));
            scCtrl.throttleInput = phase == Phase.Garage ? 0f : 0.6f;
            switch (phase)
            {
                case Phase.Garage:
                    SetLamps(false, false);
                    if (resumePending)
                    {
                        // Relargada na LINHA: o SC já entrou; libera quando o líder cruzar a linha.
                        var ld = Leader();
                        if (ld == null || race.State != RaceState.Racing || race.LapOf(ld.car) > resumeLap)
                        {
                            resumePending = false;
                            Debug.Log("[SC] RELARGADA: corrida liberada na linha");
                        }
                    }
                    break;
                case Phase.Exiting:
                {
                    SetLamps(blink, !blink);
                    var ld = Leader();
                    if (ld == null) { ReturnToGarage(); break; }
                    // Sai quando o líder está chegando à saída do pit (entre 120 e 700 m atrás) — entra À FRENTE dele.
                    float exitS = laneStart + laneLength + 75f;
                    float behind = Mathf.Repeat(exitS - SplineDist(ld.car.transform.position), L);
                    bool go = pathPos > 0f || (behind > 120f && behind < 700f) || Time.time - waitSince > 90f;
                    if (!go) break;
                    if (FollowPath(PitSpeed, dt))
                    {
                        s = Mathf.Repeat(exitS, L); v = PitSpeed; phase = Phase.Leading;
                        leaderLapAtTrack = race.LapOf(ld.car);
                        scCtrl.Body.detectCollisions = true;
                    }
                    break;
                }
                case Phase.Leading:
                case Phase.InThisLap:
                {
                    if (phase == Phase.Leading) SetLamps(blink, !blink); else SetLamps(false, false);
                    var ld = Leader();
                    // RITMO DA FILA (pedido do usuário): entre 100 e 120 km/h; nas curvas o limite da curva manda.
                    float target = Mathf.Min(vCurve[sp.Wrap(Mathf.RoundToInt(Mathf.Repeat(s + 30f, L) / sp.spacing))], 120f / 3.6f);
                    if (ld != null)
                    {
                        // Espera o líder encostar (fila formada) antes de andar no ritmo de SC.
                        float gapLead = Mathf.Repeat(s - SplineDist(ld.car.transform.position), L);
                        // Líder longe (> 60 m): o SC tira o pé para a fila encostar. Antes só esperava acima de 150 m — entre
                        // 60 e 150 m líder e SC andavam no mesmo ritmo, a fila nunca "formava" e o SC ficou 25 voltas na pista.
                        if (gapLead > 150f && gapLead < L * 0.5f) target = Mathf.Min(target, 24f);
                        else if (gapLead > 60f && gapLead < L * 0.5f) target = Mathf.Min(target, Mathf.Max(18f, ld.car.ForwardSpeed * 0.8f));
                    }
                    v = Mathf.MoveTowards(v, target, (v < target ? 5f : 9f) * dt);
                    s = Mathf.Repeat(s + v * dt, L); // posição na volta sempre dentro de 0..L
                    PlaceOnTrack();
                    if (phase == Phase.Leading && ld != null)
                    {
                        int laps = race.LapOf(ld.car) - leaderLapAtTrack;
                        int lapsLeft = race.Settings.laps - race.LapOf(ld.car);
                        bool hazard = RaceControl.HazardActive;
                        // Fila formada (líder a até 60 m do SC) e perigo controlado: libera o desdobramento.
                        if (!hazard) { if (clearSince < 0f) clearSince = Time.time; } else clearSince = -1f;
                        // Pista limpa há 60 s e 1 volta completa atrás do SC: libera os retardatários mesmo sem a fila
                        // perfeita (na F1 o SC não fica esperando para sempre).
                        bool longClear = clearSince > 0f && Time.time - clearSince > 60f;
                        if (!unlapSent && !hazard && laps >= 1 && Time.time - deployedAt > 40f && (GapAhead(ld.car) < 80f || longClear) && lapsLeft > 2)
                        {
                            unlapSent = true;
                            unlapMsgLap = race.LapOf(ld.car);
                            foreach (var e in race.entries)
                                // Só quem está ATRÁS do SC na pista (até meia volta) precisa passar a fila e o SC.
                                if (e.car != null && !e.retired && !e.finished && ld.raceDistance - e.raceDistance >= L - 5f
                                    && Mathf.Repeat(SplineDist(e.car.transform.position) - s, L) > L * 0.5f)
                                {
                                    unlapping.Add(e.car);
                                    // Retardatário passando a fila: colisão LIGADA — ele passa PELO LADO, com a distância de um carro
                                    // (ScanTraffic/SideFits). A versão "fantasma" (sem colisão) atravessava os carros da fila
                                    // (pedido do usuário, 04/11: "passarem sem bater nos carros da fila", não por dentro deles).
                                }
                            if (unlapping.Count > 0)
                            {
                                RaceControl.Broadcast("RETARDATÁRIOS PODEM ULTRAPASSAR — passe a fila e o safety car");
                                Debug.Log("[SC] desdobramento liberado para " + unlapping.Count + " carro(s)");
                            }
                        }
                        // Quem ACABOU de passar o SC (até 30 m à frente dele) terminou de desdobrar (vai para o fim da fila sem
                        // ultrapassar mais ninguém). Antes "à frente" era qualquer ponto até meia volta: quem estava a mais de
                        // meia volta ATRÁS do SC saía da lista na hora e nunca desdobrava.
                        unlapping.RemoveWhere(c => c == null || Mathf.Repeat(SplineDist(c.transform.position) - s, L) < 30f);
                        bool unlapDone = unlapSent && race.LapOf(ld.car) >= unlapMsgLap + 1; // entra no fim da volta seguinte
                        bool noUnlap = unlapSent && unlapping.Count == 0 && laps >= 2;
                        // Teto: 4 voltas atrás do SC com a pista limpa → entra (ninguém fica 20 voltas atrás do SC).
                        bool tooLong = !hazard && laps >= 4;
                        if (((unlapDone || noUnlap) && !hazard && Time.time - deployedAt > 60f) || tooLong || lapsLeft <= 1)
                        {
                            phase = Phase.InThisLap;
                            RaceControl.Broadcast("SAFETY CAR NESTA VOLTA — relargada na linha");
                            Debug.Log("[SC] safety car entra nesta volta");
                        }
                    }
                    if (phase == Phase.InThisLap)
                    {
                        // Entra no pit pela entrada normal (fim da volta) e volta para a garagem.
                        float toEntry = Mathf.Repeat(laneStart - 60f - s, L);
                        if (toEntry < 6f || toEntry > L - 6f)
                        {
                            resumeLap = ld != null ? race.LapOf(ld.car) : 0;
                            resumePending = true;
                            scCtrl.Body.detectCollisions = false;
                            BuildEntryPath();
                            phase = Phase.Entering;
                        }
                    }
                    break;
                }
                case Phase.Entering:
                    SetLamps(false, false);
                    v = Mathf.MoveTowards(v, PitSpeed, 9f * dt);
                    if (FollowPath(v, dt)) ReturnToGarage();
                    break;
            }
        }

        void ReturnToGarage()
        {
            scCar.SetPositionAndRotation(garagePose.position + Vector3.up * rideLift, garagePose.rotation);
            scCtrl.Body.detectCollisions = false;
            phase = Phase.Garage;
            v = 0f;
        }

        // ---------------- Piloto automático do jogador ----------------
        void UpdateAutopilot()
        {
            bool sc = Deployed && race.State == RaceState.Racing;
            foreach (var e in race.entries)
            {
                if (!e.isHuman || e.car == null) continue;
                var a = autos.Find(x => x.e == e);
                if (sc && (a == null || a.released))
                {
                    if (e.finished || e.retired || e.car.wrecked) continue;
                    if (PitLaneSystem.IsInPitFlow(e.car) || e.car.pitControlled) continue;
                    if (e.car.GetComponent<CarAI>() != null) continue; // o hub já pôs a IA no carro
                    if (!NearQueue(e)) continue;
                    Engage(e, a);
                }
                else if (a != null && !a.released)
                {
                    if (sc && e.car.GetComponent<CarAI>() != a.ai) { a.released = true; continue; } // alguém trocou o controle
                    if (!sc) Disengage(a, "BANDEIRA VERDE — o carro é seu");
                    else
                    {
                        // PIT pedido com o piloto automático: CONTINUA no automático e entra no box na próxima passagem pela
                        // entrada (o pit assume o carro como faz com a IA); depois da parada volta para a fila sozinho.
                        // Antes soltava o carro na hora (pedido do usuário: "era para ter mantido o piloto automático").
                        if (PitLaneSystem.IsPitRequested(e.car) && !a.pitNotified) { a.pitNotified = true; RaceControl.Tell(e, "PIT PEDIDO — o automático leva ao box"); }
                        if (!PitLaneSystem.IsPitRequested(e.car) && !PitLaneSystem.IsInPitFlow(e.car)) a.pitNotified = false;
                        // tecla P (o controle do jogador fica desligado no automático)
                        if (UnityEngine.Input.GetKeyDown(KeyCode.P)) PitLaneSystem.RequestPit(e.car);
                    }
                }
                else if (a != null && a.released && a.ignoring) TryRestoreCollisions(a);
            }
        }

        /// <summary>Chegou na fila: carro à frente a até 45 m, ou o SC a até 70 m (jogador líder).</summary>
        bool NearQueue(RaceEntry me)
        {
            if (phase == Phase.Leading || phase == Phase.InThisLap)
            {
                float g = GapAhead(me.car);
                if (!float.IsPositiveInfinity(g) && g < 70f && FirstBehindSc(me.car)) return true;
            }
            float myS = SplineDist(me.car.transform.position);
            foreach (var o in race.entries)
            {
                if (o == me || o.car == null || o.retired || PitLaneSystem.IsInPitFlow(o.car)) continue;
                float d = Mathf.Repeat(SplineDist(o.car.transform.position) - myS, L);
                if (d > 1f && d < 45f) return true;
            }
            return false;
        }

        bool FirstBehindSc(CarController car)
        {
            float g = GapAhead(car);
            foreach (var e in race.entries)
            {
                if (e.car == null || e.car == car || e.retired) continue;
                if (GapAhead(e.car) < g - 2f) return false;
            }
            return true;
        }

        void Engage(RaceEntry e, Auto a)
        {
            if (a == null) { a = new Auto { e = e }; autos.Add(a); }
            a.released = false;
            a.input = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
            if (a.input != null) a.input.enabled = false;
            a.ai = e.car.gameObject.AddComponent<CarAI>();
            a.ai.Initialize(race.Track.Waypoints, race, race.Settings.difficulty);
            // Colisões FICAM LIGADAS (pedido do usuário, 04/11: "retardatários passavam DENTRO dos carros da fila" — era o carro dele,
            // em piloto automático, sem colisão com ninguém). A IA guarda distância; nada atravessa nada.
            a.ignoring = false;
            RaceControl.Tell(e, "PILOTO AUTOMÁTICO — até a bandeira verde");
            Debug.Log("[SC] piloto automático LIGADO no " + e.name);
        }

        void Disengage(Auto a, string msg)
        {
            var car = a.e.car;
            a.released = true;
            if (a.ai != null) Destroy(a.ai);
            a.ai = null;
            if (car != null)
            {
                car.throttleInput = 0f; car.brakeInput = 0f; car.steerInput = 0f;
                car.extThrottle = 0f; car.extBrake = 0f; car.extSteer = 0f;
                car.inputLocked = false;
            }
            if (a.input != null) a.input.enabled = true;
            RaceControl.Tell(a.e, msg);
            Debug.Log("[SC] piloto automático DESLIGADO no " + a.e.name);
            TryRestoreCollisions(a);
        }

        /// <summary>Religa as colisões só com espaço livre (nenhum carro a menos de 7 m): nada de "explosão" de física.</summary>
        void TryRestoreCollisions(Auto a)
        {
            var car = a.e.car;
            if (car == null) { a.ignoring = false; return; }
            foreach (var o in race.entries)
                if (o.car != null && o.car != car && (o.car.transform.position - car.transform.position).sqrMagnitude < 49f) return;
            if ((scCar.position - car.transform.position).sqrMagnitude < 49f) return;
            SetIgnore(car, false);
            a.ignoring = false;
        }

        void SetIgnore(CarController me, bool ignore)
        {
            var mine = new List<Collider>();
            foreach (var c in me.GetComponentsInChildren<Collider>(true)) if (c != null && !(c is WheelCollider)) mine.Add(c);
            void With(CarController other)
            {
                if (other == null || other == me) return;
                foreach (var oc in other.GetComponentsInChildren<Collider>(true))
                {
                    if (oc == null || oc is WheelCollider) continue;
                    foreach (var mc in mine) Physics.IgnoreCollision(mc, oc, ignore);
                }
            }
            foreach (var e in race.entries) With(e.car);
            With(scCtrl);
        }

        // ---------------- Consultas (IA / HUD) ----------------
        public static bool Is(CarController c) => Instance != null && c != null && c == Instance.scCtrl;
        public static CarController Car => Instance != null ? Instance.scCtrl : null;
        /// <summary>SC rodando na pista (a IA trata como trânsito).</summary>
        public static bool OnTrack => Instance != null && (Instance.phase == Phase.Leading || Instance.phase == Phase.InThisLap);
        public static float TrackDistance => Instance != null ? Instance.s : 0f;
        public static float Speed => Instance != null ? Instance.v : 0f;
        /// <summary>MODO FILA (isolado: só existe com o SC acionado): sem ultrapassar, distância maior e freada firme.
        /// Retardatário liberado para desdobrar NÃO está em modo fila. Fora do SC isto é sempre false — a corrida não
        /// herda nada deste comportamento.</summary>
        public static bool QueueMode(CarController c) => Instance != null && c != null && Deployed && c != Instance.scCtrl && !Instance.unlapping.Contains(c);

        /// <summary>Este carro está DESDOBRANDO (pode passar a fila e o SC, sem punição)?</summary>
        public static bool Unlapping(CarController c) => Instance != null && c != null && Instance.unlapping.Contains(c);

        /// <summary>Distância (m) do SC À FRENTE deste carro na pista; +∞ se o SC não está na pista.</summary>
        public static float GapAhead(CarController car)
        {
            var sc = Instance;
            if (sc == null || car == null || (sc.phase != Phase.Leading && sc.phase != Phase.InThisLap)) return float.PositiveInfinity;
            float g = Mathf.Repeat(sc.s - sc.SplineDist(car.transform.position), sc.L);
            return g < sc.L * 0.5f ? g : float.PositiveInfinity;
        }

        /// <summary>Teto de velocidade da IA (m/s): o carro LOGO ATRÁS do SC mantém ~35 m dele; os outros seguem a fila
        /// pelo tráfego normal (sem ultrapassar). +∞ = sem teto.</summary>
        public static float SpeedCapFor(CarController car)
        {
            var sc = Instance;
            if (sc == null || car == null) return float.PositiveInfinity;
            if (sc.unlapping.Contains(car)) return float.PositiveInfinity; // desdobrando: passa o SC
            float g = GapAhead(car);
            if (float.IsPositiveInfinity(g) || g > 450f) return float.PositiveInfinity;
            // Só o primeiro carro atrás do SC (ninguém entre os dois).
            foreach (var e in sc.race.entries)
            {
                if (e.car == null || e.car == car || e.retired) continue;
                float ge = GapAhead(e.car);
                if (ge < g - 2f) return float.PositiveInfinity;
            }
            if (g < 14f) return Mathf.Max(0f, sc.v - 6f);
            if (g <= 35f) return sc.v + (g - 35f) / 3f;
            // Aproximação pela distância (~0,36 g): antes o teto era SC+20 m/s a 220 m — o líder a 250 km/h cravava o freio.
            return sc.v + Mathf.Sqrt(2f * 3.5f * (g - 35f));
        }

        bool AnyAuto() { foreach (var a in autos) if (!a.released) return true; return false; }

        public static string StatusText()
        {
            if (Instance == null || !Deployed) return "";
            switch (Instance.phase)
            {
                case Phase.Exiting: return "SAFETY CAR NA PISTA — forme a fila, sem ultrapassar";
                case Phase.Leading when Instance.AnyAuto(): return "SAFETY CAR — piloto automático até a verde";
                case Phase.Leading: return Instance.unlapping.Count > 0 ? "SAFETY CAR — retardatários podem passar" : "SAFETY CAR — sem ultrapassar";
                case Phase.InThisLap: case Phase.Entering: return "SAFETY CAR NESTA VOLTA — relargada na linha";
                default: return "RELARGADA — ultrapasse só após a linha";
            }
        }
    }
}
