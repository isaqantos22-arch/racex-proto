using System.Collections.Generic;
using RaceX.Car;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>
    /// EQUIPE DE PIT animada no box. Estrutura medida no vídeo de referência e na pesquisa: cerca de 20 pessoas por
    /// parada. Aqui são 19: 3 por roda (pistoleiro, tira-pneu e põe-pneu), macaco dianteiro e traseiro, 2
    /// estabilizadores, operador das LUZES DE LIBERAÇÃO (semáforo, escolha do usuário no lugar do pirulito) e 2 do
    /// bico, que só entram quando a asa dianteira vai ser trocada.
    ///
    /// Onde: na corrida o carro para PARALELO na faixa interna do pit lane, em frente à garagem (regulamento da F1:
    /// faixa rápida de até 3,5 m junto ao muro; trabalho só na faixa interna — ver PitLaneSystem.FastLaneLat). A
    /// equipe fica toda dentro da faixa interna; as luzes pendem de um braço preso na frente da garagem.
    ///
    /// Tempo: a coreografia é uma LINHA DO TEMPO determinística — cada pessoa tem trechos (clipe, posição, rumo) em
    /// função do tempo t desde que o carro parou. A pose é amostrada direto do clipe (Animation.Sample), então a mesma
    /// conta serve ao vivo, em câmera lenta e no REPLAY (que grava cada parada e reproduz no relógio dele).
    /// Os pneus levam 2,2–2,9 s conforme a equipe; a troca de bico soma 9 s.
    /// Desempenho (celular): só em box ativo perto da câmera, no máximo 2 boxes, malha de 4,5 mil triângulos e 22 ossos.
    /// Personagem e animações: Mixamo "Ch20" + 16 animações (Resources/Crew/PitCrew.fbx, clipes Legacy), macacão e
    /// capacete na cor da equipe por máscara (Resources/Crew/crew_base.bytes). Sem o asset, o sistema se desliga.
    /// </summary>
    public class PitCrewSystem : MonoBehaviour
    {
        const int MaxSets = 2;
        const float ActiveRange = 140f;     // m até a câmera para um box da IA ganhar equipe
        public const float HeadAboveToe = 1.5184f;  // medido no Blender (osso Head acima da ponta do pé, 1,75 m de altura)

        static GameObject prefab;
        static bool prefabTried;
        static Texture2D baseTex;
        static readonly Dictionary<int, Material> suitMats = new Dictionary<int, Material>();
        static bool measured;
        static Quaternion modelAlign = Quaternion.identity;
        static float modelScale = 1f;

        /// <summary>Parada gravada para o replay (tempos em Time.time, a mesma base das amostras do ReplaySystem).</summary>
        public class StopRec
        {
            public CarController car;
            public float start, dur;
            public bool nose;
            public Pose pose;
            public Color team;
        }

        RaceManager race;
        readonly List<CrewSet> sets = new List<CrewSet>();
        readonly List<CarController> candidates = new List<CarController>();
        readonly List<StopRec> recs = new List<StopRec>();
        readonly List<StopRec> activeRecs = new List<StopRec>();
        readonly Dictionary<CarController, bool> wasStopped = new Dictionary<CarController, bool>();
        float scanT;
        bool replayMode;

        public static PitCrewSystem Create(RaceManager race)
        {
            if (race == null) return null;
            if (!LoadAssets()) return null;
            var go = new GameObject("PitCrewSystem");
            var sys = go.AddComponent<PitCrewSystem>();
            sys.race = race;
            // um conjunto pronto (inativo) já no carregamento: a primeira parada não engasga instanciando 19 pessoas
            // nem recolorindo a textura do macacão do jogador
            try
            {
                sys.sets.Add(new CrewSet(go.transform));
                foreach (var e in race.entries) if (e.isHuman) SuitMaterial(CarPalette.Get(e.colorIndex));
            }
            catch (System.Exception ex) { Debug.LogWarning("[PitCrew] pré-aquecimento falhou: " + ex.Message); }
            return sys;
        }

        /// <summary>Asset da pessoa animada disponível (usado também pela comemoração do pódio).</summary>
        public static bool AssetsReady() => LoadAssets();

        static bool LoadAssets()
        {
            if (prefabTried) return prefab != null && baseTex != null;
            prefabTried = true;
            try
            {
                prefab = Resources.Load<GameObject>("Crew/PitCrew");
                var png = Resources.Load<TextAsset>("Crew/crew_base");
                if (png != null)
                {
                    baseTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!baseTex.LoadImage(png.bytes, false)) baseTex = null;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[PitCrew] assets: " + ex.Message); prefab = null; }
            if (prefab == null || baseTex == null) Debug.LogWarning("[PitCrew] sem asset da equipe — box sem mecânicos");
            return prefab != null && baseTex != null;
        }

        /// <summary>Material do macacão na cor da equipe (máscara do vermelho original × cor do carro).</summary>
        public static Material SuitMaterial(Color team)
        {
            Color32 c32 = team;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b;
            if (suitMats.TryGetValue(key, out var m) && m != null) return m;
            var src = baseTex.GetPixels32();
            var dst = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var p = src[i];
                if (p.a > 127)
                    dst[i] = new Color32((byte)Mathf.Min(255f, p.r * team.r * 1.15f), (byte)Mathf.Min(255f, p.g * team.g * 1.15f), (byte)Mathf.Min(255f, p.b * team.b * 1.15f), 255);
                else dst[i] = new Color32(p.r, p.g, p.b, 255);
            }
            var tex = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGB24, true) { name = "CrewSuit_" + key, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(dst);
            tex.Apply(true, true);
            m = MaterialLibrary.Get("CrewSuit_" + key, Color.white, 0.25f, 0f, null, tex);
            suitMats[key] = m;
            return m;
        }

        /// <summary>Instancia uma pessoa; na primeira vez mede a orientação e a escala que o FBX chegou ao Unity.</summary>
        public static GameObject Spawn(Transform parent, out Transform model)
        {
            var root = new GameObject("Crew");
            root.transform.SetParent(parent, false);
            var inst = Instantiate(prefab, root.transform, false);
            model = inst.transform;
            if (!measured)
            {
                measured = true;
                var lA = FindBone(model, "LeftArm"); var rA = FindBone(model, "RightArm");
                var head = FindBone(model, "Head"); var toe = FindBone(model, "LeftToeBase");
                if (lA != null && rA != null)
                {
                    Vector3 r = rA.position - lA.position; r.y = 0f;
                    if (r.sqrMagnitude > 1e-6f)
                    {
                        Vector3 fwd = Vector3.Cross(r.normalized, Vector3.up);
                        modelAlign = Quaternion.Euler(0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), 0f);
                    }
                }
                if (head != null && toe != null)
                {
                    float h = (head.position.y - toe.position.y) / Mathf.Max(1e-4f, root.transform.lossyScale.y);   // sem a escala do pai
                    modelScale = h > 0.02f && h < 200f ? HeadAboveToe / h : 1f;
                    // perto de 1: importou em metros (a diferença é pose, não unidade) — mantém o tamanho do Blender
                    if (modelScale > 0.75f && modelScale < 1.33f) modelScale = 1f;
                }
                Debug.Log("[PitCrew] alinhamento yaw=" + modelAlign.eulerAngles.y.ToString("0") + " escala=" + modelScale.ToString("0.###"));
            }
            model.localRotation = modelAlign * model.localRotation;
            model.localScale = model.localScale * modelScale;
            // TRAVA DE ALTURA por instância (print do usuário: mecânicos GIGANTES num treino da pista 2). A escala é medida
            // uma vez só (estática) em coordenadas do MUNDO — se a primeira pessoa nasceu sob um pai com escala diferente de 1,
            // a medida saía errada para o resto do jogo. Aqui confere a altura real desta instância (cabeça → ponta do pé,
            // descontada a escala do pai) e corrige para ~1,75 m.
            try
            {
                var hd = FindBone(model, "Head"); var tt = FindBone(model, "LeftToeBase");
                if (hd != null && tt != null)
                {
                    float ps = root.transform.lossyScale.y > 1e-4f ? root.transform.lossyScale.y : 1f;
                    float hh = (hd.position.y - tt.position.y) / ps;
                    if (hh > 0.05f && (hh > HeadAboveToe * 1.25f || hh < HeadAboveToe * 0.8f))
                        model.localScale = model.localScale * (HeadAboveToe / hh);
                }
            }
            catch (System.Exception) { }
            return root;
        }

        public static Transform FindBone(Transform t, string suffix)
        {
            if (t.name.EndsWith(":" + suffix) || t.name == suffix) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindBone(t.GetChild(i), suffix);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Carros da corrida (para os mecânicos desviarem de qualquer um que passe perto).</summary>
        internal static readonly List<CarController> CarsNow = new List<CarController>();

        void RefreshCars()
        {
            CarsNow.Clear();
            foreach (var e in race.entries) if (e.car != null && e.car.gameObject.activeInHierarchy) CarsNow.Add(e.car);
        }

        void Update()
        {
            if (race == null) return;
            if (ReplaySystem.IsPlaying) { RefreshCars(); ReplayTick(); return; }
            if (replayMode)
            {
                // saiu do replay: devolve tudo e o modo ao vivo reassume no próximo varrimento
                replayMode = false;
                foreach (var s in sets) s.Release();
                scanT = 0f;
            }
            scanT -= Time.deltaTime;
            if (scanT <= 0f)
            {
                scanT = 0.25f;
                RecordStops();
                RefreshCars();
                Scan();
            }
            float dt = Time.deltaTime;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].Orphan) { sets[i].Release(); continue; }   // carro destruído no meio da parada
                if (sets[i].Car != null) sets[i].Tick(dt);
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < sets.Count; i++) if (sets[i].Car != null) sets[i].LateTick();
        }

        /// <summary>Grava o início de cada parada de TODOS os carros (o replay pode seguir qualquer um).</summary>
        void RecordStops()
        {
            foreach (var e in race.entries)
            {
                var c = e.car;
                if (c == null) continue;
                bool st = PitLaneSystem.StopInfo(c, out float t, out float d, out bool nz);
                wasStopped.TryGetValue(c, out bool was);
                if (st && !was)
                {
                    Vector3 f = c.transform.forward; f.y = 0f;
                    recs.Add(new StopRec
                    {
                        car = c, start = Time.time - t, dur = d, nose = nz,
                        pose = new Pose(c.transform.position, Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up)),
                        team = CarPalette.Get(e.colorIndex)
                    });
                }
                wasStopped[c] = st;
            }
        }

        /// <summary>Quem precisa de equipe: carros indo para o box ou parados nele. Jogador primeiro, depois o mais
        /// perto da câmera, até 2 boxes.</summary>
        void Scan()
        {
            candidates.Clear();
            Vector3 cam = CameraPos();
            CarController human = null;
            foreach (var e in race.entries)
            {
                var c = e.car;
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                bool want = PitLaneSystem.HeadingToBox(c) || PitLaneSystem.StopInfo(c, out _, out _, out _);
                if (!want) continue;
                if (!PitLaneSystem.BoxPoseOf(c, out var pose)) continue;
                if (e.isHuman) { human = c; continue; }
                if ((pose.position - cam).sqrMagnitude > ActiveRange * ActiveRange) continue;
                candidates.Add(c);
            }
            candidates.Sort((a, b) => (a.transform.position - cam).sqrMagnitude.CompareTo((b.transform.position - cam).sqrMagnitude));
            if (human != null) candidates.Insert(0, human);
            if (candidates.Count > MaxSets) candidates.RemoveRange(MaxSets, candidates.Count - MaxSets);

            // libera conjuntos cujo carro saiu da lista (exceto os que estão terminando a saída)
            foreach (var s in sets)
                if (s.Car != null && !s.Leaving && !candidates.Contains(s.Car)) s.Release();
            foreach (var c in candidates)
            {
                bool has = false;
                foreach (var s in sets) if (s.Car == c) { has = true; break; }
                if (has) continue;
                // UMA equipe por box: com os dois carros da equipe no box ("double stack"), a do 1º carro ainda saindo é
                // dispensada quando o 2º chega; se o 1º ainda está parado, o 2º espera atrás (PitLaneSystem) sem equipe
                // própria. Antes surgiam duas equipes sobrepostas, uma com a cor do outro carro.
                int box = PitLaneSystem.BoxIndexOf(c);
                bool busy = false;
                foreach (var s in sets)
                {
                    if (s.Car == null || s.Car == c || s.Box != box) continue;
                    if (s.Leaving) s.Release(); else busy = true;
                }
                if (busy) continue;
                var free = FreeSet();
                if (free == null) continue;
                var entry = race.GetEntry(c);
                free.Assign(c, entry != null ? CarPalette.Get(entry.colorIndex) : Color.gray);
            }
        }

        CrewSet FreeSet()
        {
            foreach (var s in sets) if (s.Car == null) return s;
            if (sets.Count >= MaxSets) return null;
            try { var n = new CrewSet(transform); sets.Add(n); return n; }
            catch (System.Exception ex) { Debug.LogWarning("[PitCrew] " + ex.Message); return null; }
        }

        /// <summary>REPLAY: as paradas gravadas que cobrem o relógio do replay ganham equipe (chegada ~6 s antes e
        /// saída ~4,5 s depois), na mesma linha do tempo do ao vivo — funciona voltando a fita e em câmera lenta.</summary>
        void ReplayTick()
        {
            if (!replayMode)
            {
                replayMode = true;
                foreach (var s in sets) s.Release();
            }
            float clock = ReplaySystem.PlaybackClock;
            if (clock < 0f) return;
            Vector3 cam = CameraPos();
            activeRecs.Clear();
            foreach (var r in recs)
                if (r.car != null && clock >= r.start - 6f && clock <= r.start + r.dur + 4.5f) activeRecs.Add(r);
            activeRecs.Sort((a, b) => (a.pose.position - cam).sqrMagnitude.CompareTo((b.pose.position - cam).sqrMagnitude));
            if (activeRecs.Count > MaxSets) activeRecs.RemoveRange(MaxSets, activeRecs.Count - MaxSets);
            foreach (var s in sets)
                if (s.Rec != null && !activeRecs.Contains(s.Rec)) s.Release();
            foreach (var r in activeRecs)
            {
                CrewSet owner = null;
                foreach (var s in sets) if (s.Rec == r) { owner = s; break; }
                if (owner == null)
                {
                    owner = FreeSet();
                    if (owner == null) continue;
                    owner.AssignReplay(r);
                }
                owner.Eval(clock - r.start, clock);
            }
        }

        static Vector3 CameraPos()
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++) if (cams[i] != null && cams[i].enabled) return cams[i].transform.position;
            return Vector3.zero;
        }

        void OnDestroy()
        {
            foreach (var s in sets) s.Release();
        }
    }

    /// <summary>Uma pessoa da equipe: pose amostrada do clipe (Legacy) num tempo dado; posição/rumo pelo código.</summary>
    class CrewMember
    {
        public readonly GameObject go;
        public readonly Transform root, model;
        readonly Animation anim;
        readonly Dictionary<string, AnimationState> states = new Dictionary<string, AnimationState>();
        readonly Transform hips, lHand, rHand;
        readonly Vector3 hipsRest;
        readonly SkinnedMeshRenderer[] skins;

        public CrewMember(Transform parent, int index)
        {
            go = PitCrewSystem.Spawn(parent, out model);
            go.name = "Crew" + index;
            root = go.transform;
            anim = model.GetComponent<Animation>();
            if (anim == null)
            {
                anim = model.gameObject.AddComponent<Animation>();
                foreach (var c in Resources.LoadAll<AnimationClip>("Crew/PitCrew"))
                    if (c != null && c.legacy && !c.name.StartsWith("__")) anim.AddClip(c, c.name);
            }
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            anim.Stop();
            foreach (AnimationState st in anim)
            {
                string n = st.name;
                int bar = n.LastIndexOf('|');
                st.enabled = false;
                st.speed = 0f;
                states[bar >= 0 ? n.Substring(bar + 1) : n] = st;
            }
            hips = PitCrewSystem.FindBone(model, "Hips");
            lHand = PitCrewSystem.FindBone(model, "LeftHand");
            rHand = PitCrewSystem.FindBone(model, "RightHand");
            if (hips != null) hipsRest = root.InverseTransformPoint(hips.position);
            skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool shadows = QualitySettings.shadows != ShadowQuality.Disable && QualitySettings.GetQualityLevel() >= 2;
            foreach (var s in skins)
            {
                s.quality = SkinQuality.Bone2;
                s.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                s.receiveShadows = shadows;
            }
            go.SetActive(false);
        }

        public void SetMaterial(Material m) { foreach (var s in skins) if (s != null) s.sharedMaterial = m; }

        static float WrapTime(float t, float len, bool loop)
        {
            if (len <= 1e-4f) return 0f;
            return loop ? Mathf.Repeat(t, len) : Mathf.Clamp(t, 0f, len);
        }

        /// <summary>Pose do clipe no tempo dado, misturada com o fim do trecho anterior (prevW &gt; 0) para a troca não pular.</summary>
        public void Sample(string clip, float time, bool loop, string prev, float prevTime, bool prevLoop, float prevW)
        {
            if (!go.activeInHierarchy || anim == null) return;
            if (!states.TryGetValue(clip, out var a) || a == null) return;
            AnimationState b = null;
            if (prev != null && prevW > 0.001f && prev != clip && states.TryGetValue(prev, out var pb)) b = pb;
            a.enabled = true;
            a.weight = b != null ? 1f - prevW : 1f;
            a.time = WrapTime(time, a.length, loop);
            if (b != null)
            {
                b.enabled = true;
                b.weight = prevW;
                b.time = WrapTime(prevTime, b.length, prevLoop);
            }
            anim.Sample();
            a.enabled = false;
            if (b != null) b.enabled = false;
        }

        public void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>Animações "no lugar": o quadril não sai da marca (o código é quem anda com a pessoa).</summary>
        public void LockHips()
        {
            if (hips == null || !go.activeInHierarchy) return;
            Vector3 w = root.TransformPoint(new Vector3(hipsRest.x, 0f, hipsRest.z));
            Vector3 p = hips.position;
            hips.position = new Vector3(w.x, p.y, w.z);
        }

        /// <summary>Mão direita (posição no mundo) — troféu da comemoração.</summary>
        public Vector3 RightHandPos => rHand != null ? rHand.position : root.position + Vector3.up * 1.6f;
        public Vector3 HandsCenter => lHand != null && rHand != null ? (lHand.position + rHand.position) * 0.5f : root.position + Vector3.up * 1f;
    }

    /// <summary>Pneu de mão (borracha + faixa do composto + centro escuro), como os da roda do carro.</summary>
    class TyreProp
    {
        public readonly GameObject go;
        readonly Renderer band;

        public TyreProp(Transform parent, float radius, float width)
        {
            go = new GameObject("TyreProp");
            go.transform.SetParent(parent, false);
            Part(PrimitiveType.Cylinder, new Vector3(radius * 2f, width * 0.5f, radius * 2f), MaterialLibrary.Get("PropTyre", new Color(0.095f, 0.095f, 0.1f), 0.22f));
            band = Part(PrimitiveType.Cylinder, new Vector3(radius * 1.82f, width * 0.505f, radius * 1.82f), MaterialLibrary.Get("PropTyreBand1", TyreModel.ColorOf(1), 0.22f));
            Part(PrimitiveType.Cylinder, new Vector3(radius * 1.62f, width * 0.51f, radius * 1.62f), MaterialLibrary.Get("PropRim", new Color(0.16f, 0.16f, 0.17f), 0.5f, 0.6f));
            go.SetActive(false);
        }

        Renderer Part(PrimitiveType t, Vector3 scale, Material m)
        {
            var p = GameObject.CreatePrimitive(t);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(go.transform, false);
            p.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // eixo do cilindro (Y) vira o eixo da roda (Z local)
            p.transform.localScale = scale;
            var r = p.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void SetCompound(int c) { band.sharedMaterial = MaterialLibrary.Get("PropTyreBand" + TyreModel.Clamp(c), TyreModel.ColorOf(c), 0.22f); }
    }

    /// <summary>Conjunto de um box: 19 pessoas, luzes de liberação, adereços — tudo em função do tempo t da parada.</summary>
    class CrewSet
    {
        enum Phase { Waiting, Stopping, Leaving }
        const int Gun = 0, Off = 4, On = 8, JackF = 12, JackR = 13, StabL = 14, StabR = 15, Light = 16, NoseA = 17, NoseB = 18, Count = 19;
        const float NEG = -1e6f, POS = 1e6f, Blend = 0.15f;

        /// <summary>Trecho da linha do tempo de uma pessoa: clipe (tempo inicial e velocidade), posição de/para (espaço
        /// do carro parado: x = direita, z = frente) e se anda (rumo = direção do movimento).</summary>
        struct Seg
        {
            public float t0, t1;
            public string clip;
            public float start, speed;
            public bool loop, walk;
            public Vector3 from, to;
        }

        readonly GameObject holder;
        readonly CrewMember[] m = new CrewMember[Count];
        readonly List<Seg>[] plan = new List<Seg>[Count];
        readonly TyreProp[] oldTyre = new TyreProp[4], newTyre = new TyreProp[4];
        readonly GameObject gantry, noseOld, noseNew, jackF, jackR;
        readonly Renderer[] lamps = new Renderer[3];
        readonly Vector3[] wl = new Vector3[4];     // rodas no espaço local do carro
        readonly bool[] wheelHidden = new bool[4];
        readonly Vector3[] waitPos = new Vector3[Count], workPos = new Vector3[Count];
        Phase phase;
        float D = 2.5f, tt = 2.5f, k = 1f, leaveT, idleT, lastT = -1f;
        bool nose, garageStop;
        Pose pose;          // pose do carro parado (espaço do layout)
        float groundY, frontZ, rearZ, midZ, halfTrack;
        int lightState = -1;

        public CarController Car { get; private set; }
        public int Box { get; private set; } = -1;
        public PitCrewSystem.StopRec Rec { get; private set; }
        readonly Vector3[] dodge = new Vector3[Count];   // passo para o lado de cada pessoa (desvio de carro), mundo
        public bool Leaving => phase == Phase.Leaving;
        public bool Orphan => !ReferenceEquals(Car, null) && Car == null;

        public CrewSet(Transform parent)
        {
            holder = new GameObject("CrewSet");
            holder.transform.SetParent(parent, false);
            for (int i = 0; i < Count; i++) { m[i] = new CrewMember(holder.transform, i); plan[i] = new List<Seg>(); }
            for (int i = 0; i < 4; i++)
            {
                oldTyre[i] = new TyreProp(holder.transform, 0.36f, 0.34f);
                newTyre[i] = new TyreProp(holder.transform, 0.36f, 0.34f);
            }
            noseOld = NoseProp(holder.transform);
            noseNew = NoseProp(holder.transform);
            jackF = JackProp(holder.transform);
            jackR = JackProp(holder.transform);
            gantry = Gantry(holder.transform, lamps);
            holder.SetActive(false);
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var p = GameObject.CreatePrimitive(t);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            var r = p.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return p;
        }

        static GameObject NoseProp(Transform parent)
        {
            var g = new GameObject("NoseProp");
            g.transform.SetParent(parent, false);
            var mat = MaterialLibrary.Get("PropNose", new Color(0.12f, 0.12f, 0.13f), 0.4f);
            Prim(PrimitiveType.Cube, g.transform, Vector3.zero, new Vector3(1.35f, 0.06f, 0.42f), mat);
            Prim(PrimitiveType.Cube, g.transform, new Vector3(-0.66f, 0.1f, 0f), new Vector3(0.03f, 0.24f, 0.45f), mat);
            Prim(PrimitiveType.Cube, g.transform, new Vector3(0.66f, 0.1f, 0f), new Vector3(0.03f, 0.24f, 0.45f), mat);
            Prim(PrimitiveType.Cube, g.transform, new Vector3(0f, 0.08f, 0.35f), new Vector3(0.22f, 0.14f, 0.7f), mat);
            g.SetActive(false);
            return g;
        }

        static GameObject JackProp(Transform parent)
        {
            // barra do macaco: cilindro de 1 m no eixo Z local, esticado entre a mão e o ponto de apoio
            var g = new GameObject("JackProp");
            g.transform.SetParent(parent, false);
            var bar = Prim(PrimitiveType.Cylinder, g.transform, new Vector3(0f, 0f, 0.5f), new Vector3(0.05f, 0.5f, 0.05f), MaterialLibrary.Get("PropJack", new Color(0.75f, 0.75f, 0.78f), 0.6f, 0.8f));
            bar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cube, g.transform, new Vector3(0f, -0.05f, 1f), new Vector3(0.32f, 0.12f, 0.18f), MaterialLibrary.Get("PropJackHead", new Color(0.9f, 0.75f, 0.1f), 0.4f));
            g.SetActive(false);
            return g;
        }

        /// <summary>LUZES DE LIBERAÇÃO num braço preso na frente da GARAGEM (lado esquerdo do carro), pendendo sobre o
        /// bico: nada fica na faixa por onde os outros carros passam. Vermelho parado, verde para sair.
        /// Espaço local do braço: x = direita do carro, z = frente do carro (origem no chão, à altura do bico).</summary>
        static GameObject Gantry(Transform parent, Renderer[] lamps)
        {
            var g = new GameObject("ReleaseLights");
            g.transform.SetParent(parent, false);
            var post = MaterialLibrary.Get("GantryPost", new Color(0.18f, 0.18f, 0.2f), 0.4f, 0.5f);
            Prim(PrimitiveType.Cube, g.transform, new Vector3(-4.3f, 1.45f, 0f), new Vector3(0.12f, 2.9f, 0.12f), post);   // coluna na boca da garagem
            Prim(PrimitiveType.Cube, g.transform, new Vector3(-2.0f, 2.9f, 0f), new Vector3(4.7f, 0.12f, 0.12f), post);    // braço sobre a faixa interna
            Prim(PrimitiveType.Cube, g.transform, new Vector3(0f, 2.55f, 0f), new Vector3(0.7f, 0.5f, 0.12f), post);       // caixa das luzes sobre o bico
            for (int i = 0; i < 3; i++)
            {
                var l = Prim(PrimitiveType.Sphere, g.transform, new Vector3(-0.22f + i * 0.22f, 2.55f, -0.07f), new Vector3(0.16f, 0.16f, 0.06f), LampMat(0));
                lamps[i] = l.GetComponent<Renderer>();
            }
            return g;
        }

        static Material LampMat(int s)
        {
            if (s == 1) return MaterialLibrary.Get("LampRed", new Color(1f, 0.08f, 0.05f), 0.6f, 0f, new Color(3f, 0.2f, 0.1f));
            if (s == 2) return MaterialLibrary.Get("LampGreen", new Color(0.1f, 1f, 0.2f), 0.6f, 0f, new Color(0.2f, 3f, 0.4f));
            return MaterialLibrary.Get("LampOff", new Color(0.1f, 0.1f, 0.1f), 0.6f);
        }

        void SetLights(int s)
        {
            if (s == lightState) return;
            lightState = s;
            var mat = LampMat(s);
            foreach (var l in lamps) if (l != null) l.sharedMaterial = mat;
        }

        // ---------------- Atribuição ----------------
        void Prepare(CarController car, Color team)
        {
            Car = car;
            Box = PitLaneSystem.BoxIndexOf(car);
            for (int i = 0; i < Count; i++) dodge[i] = Vector3.zero;
            leaveT = 0f; idleT = 0f; lightState = -1; lastT = -1f;
            garageStop = PitLaneSystem.PracticeSession;
            var mat = PitCrewSystem.SuitMaterial(team);
            for (int i = 0; i < Count; i++) m[i].SetMaterial(mat);
            for (int i = 0; i < 4; i++)
                wl[i] = car.wheels[i] != null ? car.transform.InverseTransformPoint(car.wheels[i].transform.position) : new Vector3(i % 2 == 0 ? -0.8f : 0.8f, 0f, i < 2 ? 1.8f : -1.8f);
            int fit = PitLaneSystem.FittingCompound(car), cur = PitLaneSystem.CompoundOf(car);
            float r = 0.36f;
            try { if (car.wheels[0] != null) r = car.wheels[0].radius; } catch { }
            for (int i = 0; i < 4; i++)
            {
                oldTyre[i].SetCompound(cur);
                oldTyre[i].go.transform.localScale = Vector3.one * (r / 0.36f);
                newTyre[i].SetCompound(fit);
                shownFit = fit;
                newTyre[i].go.transform.localScale = Vector3.one * (r / 0.36f);
            }
            holder.SetActive(true);
            gantry.SetActive(!garageStop);   // treino: dentro da garagem, sem o braço das luzes
            SetLights(0);
        }

        /// <summary>Ao vivo: o carro está indo para o box (layout na marca do box até ele parar).</summary>
        public void Assign(CarController car, Color team)
        {
            Rec = null;
            phase = Phase.Waiting;
            nose = false;
            Prepare(car, team);
            PitLaneSystem.BoxPoseOf(car, out pose);
            SetTiming(2.5f, false);
            Layout(pose);
            BuildPlan();
            Eval(-1f, Time.time);
        }

        /// <summary>Replay: parada gravada.</summary>
        public void AssignReplay(PitCrewSystem.StopRec r)
        {
            Rec = r;
            phase = Phase.Stopping;
            Prepare(r.car, r.team);
            pose = r.pose;
            SetTiming(r.dur, r.nose);
            Layout(pose);
            BuildPlan();
        }

        void SetTiming(float d, bool nz)
        {
            D = Mathf.Max(1.5f, d);
            nose = nz;
            tt = nose ? Mathf.Max(2f, D - PitLaneSystem.NoseChangeSeconds) : D;
            k = Mathf.Clamp(tt / 2.5f, 0.8f, 1.25f);
        }

        public void Release()
        {
            RestoreWheels();
            Car = null;
            Rec = null;
            holder.SetActive(false);
        }

        void RestoreWheels()
        {
            for (int i = 0; i < 4; i++)
            {
                if (!wheelHidden[i]) continue;
                wheelHidden[i] = false;
                if (Car != null && Car.wheelMeshes[i] != null) Car.wheelMeshes[i].gameObject.SetActive(true);
            }
        }

        void HideWheel(int i, bool hide)
        {
            if (Car == null || Car.wheelMeshes[i] == null || wheelHidden[i] == hide) return;
            // roda arrancada no acidente continua escondida pelo dano: só mexe na que existe
            if (hide && Car.damage != null && Car.damage.Detached((int)DamageZone.WheelFL + i)) return;
            wheelHidden[i] = hide;
            Car.wheelMeshes[i].gameObject.SetActive(!hide);
        }

        // ---------------- Layout (medidas) ----------------
        /// <summary>Marcas de cada função (x = direita do carro, z = frente). Distâncias pelas poses medidas no Blender:
        /// agachado o corpo vai até 0,71 m à frente do quadril; segurando a pistola, as mãos ficam 0,33–0,45 m à frente e
        /// o corpo até 0,48 m. Na corrida o lado direito do carro é a faixa rápida: ninguém passa de ~3 m para a direita
        /// (fica dentro da faixa interna); pneus velhos vão para a garagem pelo lado esquerdo.</summary>
        void Layout(Pose p)
        {
            groundY = p.position.y - 0.35f;
            frontZ = (wl[0].z + wl[1].z) * 0.5f;
            rearZ = (wl[2].z + wl[3].z) * 0.5f;
            midZ = (frontZ + rearZ) * 0.5f;
            halfTrack = Mathf.Max(0.6f, (Mathf.Abs(wl[0].x) + Mathf.Abs(wl[1].x) + Mathf.Abs(wl[2].x) + Mathf.Abs(wl[3].x)) * 0.25f);
            for (int i = 0; i < 4; i++)
            {
                float s = i % 2 == 0 ? -1f : 1f;
                bool front = i < 2;
                waitPos[Gun + i] = L(wl[i].x + s * 0.78f, wl[i].z);
                workPos[Gun + i] = waitPos[Gun + i];
                waitPos[Off + i] = L(wl[i].x + s * 0.95f, wl[i].z + (front ? 0.6f : -0.6f));
                // pneu velho: lado da garagem (esquerdo) vai para a boca da garagem; lado da faixa rápida vai para o meio
                // do carro, encostado — nunca para a faixa rápida
                workPos[Off + i] = s < 0f ? L(wl[i].x - 2.6f, wl[i].z + (front ? 1.0f : -1.0f)) : L(wl[i].x + 1.6f, midZ + (front ? 0.9f : -0.9f));
                waitPos[On + i] = L(wl[i].x + s * (s > 0f ? 1.45f : 1.8f), wl[i].z + (front ? -0.55f : 0.55f));
                workPos[On + i] = L(wl[i].x + s * 0.95f, wl[i].z + (front ? -0.45f : 0.45f));
            }
            waitPos[JackF] = L(0f, frontZ + 2.0f); workPos[JackF] = L(-halfTrack - 1.6f, frontZ + 1.3f);   // sai da frente para o lado da garagem
            waitPos[JackR] = L(0f, rearZ - 1.7f); workPos[JackR] = waitPos[JackR];
            waitPos[StabL] = L(-halfTrack - 0.7f, midZ + 0.25f); workPos[StabL] = waitPos[StabL];
            waitPos[StabR] = L(halfTrack + 0.7f, midZ + 0.25f); workPos[StabR] = waitPos[StabR];
            waitPos[Light] = L(-3.6f, frontZ + 1.9f); workPos[Light] = waitPos[Light];
            waitPos[NoseA] = L(-1.25f, frontZ + 1.25f); workPos[NoseA] = L(-2.9f, frontZ + 2.0f);
            waitPos[NoseB] = L(1.7f, frontZ + 1.9f); workPos[NoseB] = L(0.75f, frontZ + 1.3f);
            gantry.transform.SetPositionAndRotation(W(0f, frontZ + 1.4f), Quaternion.LookRotation(pose.rotation * Vector3.forward, Vector3.up));
        }

        Vector3 L(float x, float z) => new Vector3(x, 0f, z);
        Vector3 W(float x, float z) { var v = pose.position + pose.rotation * new Vector3(x, 0f, z); v.y = groundY; return v; }
        Vector3 W(Vector3 local) => W(local.x, local.z);
        Vector3 WheelW(int i) => W(wl[i]) + Vector3.up * Mathf.Max(0.2f, Car != null && Car.wheels[i] != null ? Car.wheels[i].radius : 0.36f);

        Vector3 FaceTarget(int i)
        {
            if (i < 12) return W(wl[i % 4]);
            if (i == JackF) return W(0f, frontZ);
            if (i == JackR) return W(0f, rearZ);
            if (i == Light) return W(0f, frontZ + 0.5f);
            if (i == NoseA || i == NoseB) return W(0f, frontZ + 0.9f);
            return W(0f, midZ);
        }

        // ---------------- Linha do tempo ----------------
        void Add(int i, float t0, float t1, string clip, float start, float speed, bool loop, Vector3 from, Vector3 to, bool walk = false)
        {
            if (t1 <= t0) return;
            plan[i].Add(new Seg { t0 = t0, t1 = t1, clip = clip, start = start, speed = speed, loop = loop, from = from, to = to, walk = walk });
        }

        /// <summary>Trechos de cada pessoa. t = 0 quando o carro para; tt = fim da troca de pneus; D = liberação.
        /// Trechos de clipe medidos no Blender (30 q/s): Picking_Up q46–150 (mãos mais baixas no q67–76), Putting_Down
        /// q91–190 (mais baixas no q166–181), Hand_Raising com a mão em cima do q16 ao q76.</summary>
        void BuildPlan()
        {
            for (int i = 0; i < Count; i++) plan[i].Clear();
            for (int w = 0; w < 4; w++)
            {
                // ESPERA FORA DO CONTORNO DO CARRO (como na F1): lado da faixa rápida (rodas direitas, por onde o carro
                // chega em diagonal) 1 m mais para fora; lado da garagem 0,5 m. Todos entram na marca nos primeiros 0,25 s.
                // Antes o lado da garagem esperava colado na linha do carro e o carro passava por cima de 2–3 pessoas.
                // Lado da faixa rápida só 0,4 m (mais para fora, a equipe ficava na rota de saída do carro do box de trás,
                // que passa em diagonal por ali — "atropelei mecânicos de outra equipe").
                Vector3 outS = new Vector3(w % 2 == 1 ? 0.4f : -0.5f, 0f, 0f);
                Vector3 g = waitPos[Gun + w];
                Add(Gun + w, NEG, 0f, "Crouch_Idle", 0f, 1f, true, g + outS, g + outS);
                Add(Gun + w, 0f, 0.25f, "Holding_Down", 0f, 1f, true, g + outS, g);
                Add(Gun + w, 0.25f, tt - 0.45f, "Holding_Down", 0.25f, 1f, true, g, g);
                Add(Gun + w, tt - 0.45f, D, "Hand_Raising", 0f, 2.5f, false, g, g);
                Add(Gun + w, D, D + 1.75f, "Standing_Up", 0f, 1.6f, false, g, g);
                Add(Gun + w, D + 1.75f, POS, "Idle", 0f, 1f, true, g, g);

                Vector3 ow = waitPos[Off + w], oo = workPos[Off + w];
                Add(Off + w, NEG, 0f, "Crouch_Idle", 0f, 0.9f, true, ow + outS, ow + outS);
                Add(Off + w, 0f, 0.15f * k, "Crouch_Idle", 0f, 0.9f, true, ow + outS, ow);
                Add(Off + w, 0.15f * k, 1.15f * k, "Picking_Up", 46f / 30f, 3.5f / k, false, ow, ow);
                Add(Off + w, 1.15f * k, 2.3f * k, "Holding_Walk", 0f, 1.3f, true, ow, oo, true);
                Add(Off + w, 2.3f * k, POS, "Box_Idle", 0f, 1f, true, oo, oo);

                Vector3 nw = waitPos[On + w], no = workPos[On + w];
                Vector3 nwOut = nw + new Vector3(w % 2 == 1 ? 0.15f : -0.4f, 0f, 0f);
                Add(On + w, NEG, 0f, "Box_Idle", 0f, 1f, true, nwOut, nwOut);
                Add(On + w, 0f, 0.5f * k, "Box_Idle", 0f, 1f, true, nwOut, nw);
                Add(On + w, 0.5f * k, 0.95f * k, "Holding_Walk", 0f, 1.4f, true, nw, no, true);
                Add(On + w, 0.95f * k, 1.95f * k, "Putting_Down", 91f / 30f, 3.3f / k, false, no, no);
                Add(On + w, 1.95f * k, POS, "Idle", 0f, 1f, true, no, no);
            }
            Vector3 jf = waitPos[JackF], jfo = workPos[JackF];
            Add(JackF, NEG, 0f, "Idle", 0f, 1f, true, jf, jf);
            Add(JackF, 0f, tt - 0.55f, "Pull_Heavy_Object", 0f, 1f, true, jf, jf);
            Add(JackF, tt - 0.55f, tt - 0.1f, "Fast_Run", 0f, 1f, true, jf, jfo, true);
            Add(JackF, tt - 0.1f, POS, "Idle", 0f, 1f, true, jfo, jfo);
            // MACACO TRASEIRO: espera ao lado da traseira, no lado da garagem (atrás do carro é por onde ele chega), e
            // entra atrás dele assim que para (0,45 s de trote).
            Vector3 jr = waitPos[JackR];
            Vector3 jrWait = L(-halfTrack - 1.5f, rearZ - 0.9f);
            Add(JackR, NEG, 0f, "Idle", 0f, 1f, true, jrWait, jrWait);
            Add(JackR, 0f, 0.45f, "Fast_Run", 0f, 0.8f, true, jrWait, jr, true);
            Add(JackR, 0.45f, D, "Pull_Heavy_Object", 0f, 1f, true, jr, jr);
            Add(JackR, D, POS, "Idle", 0f, 1f, true, jr, jr);
            for (int sIdx = StabL; sIdx <= StabR; sIdx++)
            {
                Vector3 sp = waitPos[sIdx];
                Vector3 so = sp + new Vector3(sIdx == StabR ? 0.4f : -0.6f, 0f, 0f);
                Add(sIdx, NEG, 0f, "Crouch_Idle", 0f, sIdx == StabL ? 1.1f : 1.05f, true, so, so);
                Add(sIdx, 0f, 0.25f, "Holding_Down", 0f, 0.9f, true, so, sp);
                Add(sIdx, 0.25f, D, "Holding_Down", 0.25f * 0.9f, 0.9f, true, sp, sp);
                Add(sIdx, D, POS, "Idle", 0f, 1f, true, sp, sp);
            }
            Vector3 lp = waitPos[Light];
            Add(Light, NEG, D, "Idle", 0f, 0.9f, true, lp, lp);
            Add(Light, D, D + 1.6f, "Hand_Raising", 0f, 2f, false, lp, lp);
            Add(Light, D + 1.6f, POS, "Idle", 0f, 1f, true, lp, lp);
            // bico (só aparece com troca de asa): um tira o velho, o outro encaixa o novo no fim da parada
            Vector3 na = waitPos[NoseA], nao = workPos[NoseA];
            Add(NoseA, NEG, 0.4f, "Idle", 0f, 1f, true, na, na);
            Add(NoseA, 0.4f, 2.1f, "Lifting_Object", 20f / 30f, 3f, false, na, na);
            Add(NoseA, 2.1f, 3.6f, "Holding_Walk", 0f, 1.3f, true, na, nao, true);
            Add(NoseA, 3.6f, POS, "Box_Idle", 0f, 1f, true, nao, nao);
            Vector3 nb = waitPos[NoseB], nbo = workPos[NoseB];
            float tPut = Mathf.Max(4.2f, D - 3.4f);
            Add(NoseB, NEG, tPut - 0.6f, "Box_Idle", 0f, 1f, true, nb, nb);
            Add(NoseB, tPut - 0.6f, tPut, "Holding_Walk", 0f, 1.3f, true, nb, nbo, true);
            Add(NoseB, tPut, tPut + 1.1f, "Putting_Down", 91f / 30f, 3f, false, nbo, nbo);
            Add(NoseB, tPut + 1.1f, POS, "Idle", 0f, 1f, true, nbo, nbo);
            if (garageStop) ClearExitPath();
        }

        /// <summary>GARAGEM (treino/classificação): o carro sai PARA A FRENTE pelo meio da garagem. Na liberação, todos
        /// vão para as paredes laterais (fora da largura do carro + 1,6 m) em vez de ficar parados nas marcas do pit
        /// stop — o carro passava por eles na saída.</summary>
        void ClearExitPath()
        {
            float tc = D + 0.15f, side = halfTrack + 1.9f;
            for (int i = 0; i < Count; i++)
            {
                var segs = plan[i];
                if (segs.Count == 0) continue;
                // posição na hora da liberação
                Vector3 at = segs[segs.Count - 1].to;
                for (int j = 0; j < segs.Count; j++)
                    if (tc < segs[j].t1) { var sg = segs[j]; float u = sg.t1 - sg.t0 > 1e-3f && sg.t0 > NEG + 1f ? Mathf.Clamp01((tc - sg.t0) / (sg.t1 - sg.t0)) : 0f; at = Vector3.Lerp(sg.from, sg.to, u); break; }
                // corta tudo depois de tc
                for (int j = segs.Count - 1; j >= 0; j--)
                {
                    if (segs[j].t0 >= tc) { segs.RemoveAt(j); continue; }
                    if (segs[j].t1 > tc) { var sg = segs[j]; sg.to = at; sg.from = sg.t0 > NEG + 1f ? sg.from : at; sg.t1 = tc; segs[j] = sg; }
                }
                float sx = at.x < -0.05f ? -1f : at.x > 0.05f ? 1f : -1f;
                Vector3 clear = new Vector3(sx * Mathf.Max(side, Mathf.Abs(at.x)), 0f, Mathf.Clamp(at.z, rearZ - 0.5f, frontZ + 0.5f));
                float dist = Vector3.Distance(at, clear);
                float run = Mathf.Clamp(dist / 2.2f, 0.35f, 1.4f);
                Add(i, tc, tc + run, "Fast_Run", 0f, 0.8f, true, at, clear, true);
                Add(i, tc + run, POS, "Idle", 0f, 1f, true, clear, clear);
            }
        }

        static float ClipTime(Seg sg, float t, float clock, int i)
        {
            float phase = sg.loop ? i * 0.37f : 0f;   // cada um num ponto do ciclo (ninguém se mexe em sincronia)
            if (sg.t0 <= NEG + 1f) return clock * sg.speed + phase;   // espera: anima pelo relógio
            return sg.start + (t - sg.t0) * sg.speed + phase;
        }

        /// <summary>Ao vivo: decide o tempo t pela parada real e avalia.</summary>
        int shownFit = -1;

        public void Tick(float dt)
        {
            if (Car == null) return;
            bool stopped = PitLaneSystem.StopInfo(Car, out float st, out float d, out bool nz);
            // Pneu NOVO na mão da equipe sempre do composto que vai ser montado (mudou a estratégia com o carro chegando
            // → a cor acompanha). Depois da parada não mexe (o próximo stint já é outro).
            if (phase == Phase.Waiting || stopped)
            {
                int fitNow = PitLaneSystem.FittingCompound(Car);
                if (fitNow != shownFit) { shownFit = fitNow; for (int i = 0; i < 4; i++) newTyre[i].SetCompound(fitNow); }
            }
            float t;
            switch (phase)
            {
                case Phase.Waiting:
                    if (stopped)
                    {
                        phase = Phase.Stopping;
                        SetTiming(d, nz);
                        // layout definitivo na pose REAL do carro parado
                        Vector3 f = Car.transform.forward; f.y = 0f;
                        pose = new Pose(Car.transform.position, Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up));
                        Layout(pose);
                        BuildPlan();
                        t = st;
                    }
                    else
                    {
                        if (!PitLaneSystem.HeadingToBox(Car)) { idleT += dt; if (idleT > 2f) { Release(); return; } }
                        else idleT = 0f;
                        t = -1f;
                    }
                    break;
                case Phase.Stopping:
                    if (!stopped) { phase = Phase.Leaving; leaveT = 0f; t = D; }
                    else t = st;
                    break;
                default:
                    leaveT += dt;
                    if (leaveT > 4.5f) { Release(); return; }
                    t = D + leaveT;
                    break;
            }
            Eval(t, Time.time);
        }

        /// <summary>DESVIO: ninguém fica no caminho de um carro. Para cada carro a até 8 m, a pessoa que estiver dentro do
        /// contorno dele (+0,55 m de lado, +0,6 m atrás e, à frente, mais 0,6 s do que ele anda) dá um passo para o lado em
        /// que já está (3,5 m/s) e volta quando o carro passa. Vale para o carro da própria equipe chegando/saindo (na parada,
        /// 0 ≤ t ≤ D, ela trabalha encostada nele de propósito) e para carros de outras equipes passando.</summary>
        Vector3 Dodge(int i, Vector3 wp, float t)
        {
            Vector3 want = Vector3.zero;
            var cars = PitCrewSystem.CarsNow;
            for (int k = 0; k < cars.Count; k++)
            {
                var c = cars[k];
                if (c == null) continue;
                if (c == Car && t >= 0f && t <= D) continue;
                Vector3 d = wp - c.transform.position; d.y = 0f;
                if (d.sqrMagnitude > 64f) continue;
                Vector3 local = c.transform.InverseTransformPoint(wp);
                Vector3 size = c.spec != null ? c.spec.bodySize : new Vector3(1.9f, 1f, 5.4f);
                float hx = size.x * 0.5f + 0.55f;
                float hzBack = size.z * 0.5f + 0.6f;
                float hzFront = hzBack + Mathf.Max(0f, c.ForwardSpeed) * 0.6f;
                if (local.z > hzFront || local.z < -hzBack || Mathf.Abs(local.x) >= hx) continue;
                float side = Mathf.Abs(local.x) > 0.05f ? Mathf.Sign(local.x) : 1f;
                Vector3 right = c.transform.right; right.y = 0f;
                want += right.normalized * side * (hx - Mathf.Abs(local.x));
            }
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            dodge[i] = Vector3.MoveTowards(dodge[i], want, 3.5f * dt);
            return dodge[i];
        }

        /// <summary>Pose de todos e estado dos adereços no tempo t (ao vivo ou no replay).</summary>
        public void Eval(float t, float clock)
        {
            if (Car == null) return;
            lastT = t;
            for (int i = 0; i < Count; i++)
            {
                bool vis = ((i != NoseA && i != NoseB) || (nose && t >= 0f)) && !(garageStop && i == JackR);
                if (m[i].go.activeSelf != vis) m[i].go.SetActive(vis);
                if (!vis) continue;
                var segs = plan[i];
                if (segs.Count == 0) continue;
                int si = segs.Count - 1;
                for (int j = 0; j < segs.Count; j++) if (t < segs[j].t1) { si = j; break; }
                var sg = segs[si];
                bool finite = sg.t0 > NEG + 1f && sg.t1 < POS - 1f;
                float u = finite ? Mathf.Clamp01((t - sg.t0) / (sg.t1 - sg.t0)) : 0f;
                Vector3 wp = W(Vector3.Lerp(sg.from, sg.to, u));
                wp += Dodge(i, wp, t);
                m[i].root.position = wp;
                Vector3 face = sg.walk ? W(sg.to) - W(sg.from) : FaceTarget(i) - wp;
                if (face.sqrMagnitude < 1e-4f) face = FaceTarget(i) - wp;
                m[i].Face(face);
                float ct = ClipTime(sg, t, clock, i);
                string pc = null; float pt = 0f, pw = 0f; bool pl = false;
                if (si > 0 && sg.t0 > NEG + 1f && t >= sg.t0 && t - sg.t0 < Blend)
                {
                    var pv = segs[si - 1];
                    pc = pv.clip; pl = pv.loop;
                    pt = ClipTime(pv, sg.t0, clock, i);
                    pw = 1f - (t - sg.t0) / Blend;
                }
                m[i].Sample(sg.clip, ct, sg.loop, pc, pt, pl, pw);
            }
            // adereços e rodas
            for (int w = 0; w < 4; w++)
            {
                bool oldVis = t >= 0.4f * k;
                if (oldTyre[w].go.activeSelf != oldVis) oldTyre[w].go.SetActive(oldVis);
                bool newVis = t < 1.75f * k;
                if (newTyre[w].go.activeSelf != newVis) newTyre[w].go.SetActive(newVis);
                HideWheel(w, t >= 0.4f * k && t < 1.75f * k);
            }
            bool jf = t >= 0f && t < tt - 0.55f, jr = t >= 0f && t < D;
            if (jackF.activeSelf != jf) jackF.SetActive(jf);
            if (jackR.activeSelf != jr) jackR.SetActive(jr);
            bool no = nose && t >= 0.9f, nn = nose && t >= 0f && t < Mathf.Max(4.2f, D - 3.4f) + 0.8f;
            if (noseOld.activeSelf != no) noseOld.SetActive(no);
            if (noseNew.activeSelf != nn) noseNew.SetActive(nn);
            SetLights(t < 0f ? 0 : t < D ? 1 : t < D + 1.4f ? 2 : 0);
        }

        /// <summary>Depois das poses: quadril na marca, adereços nas mãos.</summary>
        public void LateTick()
        {
            if (Car == null) return;
            for (int i = 0; i < Count; i++) m[i].LockHips();
            float t = lastT;
            for (int w = 0; w < 4; w++)
            {
                if (oldTyre[w].go.activeSelf) Hold(oldTyre[w].go.transform, m[Off + w]);
                if (newTyre[w].go.activeSelf)
                {
                    // no encaixe o pneu vai da mão para o cubo da roda
                    if (t >= 1.45f * k)
                    {
                        float u = Mathf.Clamp01((t - 1.45f * k) / (0.3f * k));
                        var tr = newTyre[w].go.transform;
                        tr.position = Vector3.Lerp(m[On + w].HandsCenter, WheelW(w), u);
                        tr.rotation = Quaternion.LookRotation(pose.rotation * Vector3.right, Vector3.up);
                    }
                    else Hold(newTyre[w].go.transform, m[On + w]);
                }
            }
            if (noseOld.activeSelf) HoldFlat(noseOld.transform, m[NoseA]);
            if (noseNew.activeSelf) HoldFlat(noseNew.transform, m[NoseB]);
            if (jackF.activeSelf) Bar(jackF.transform, m[JackF], W(0f, frontZ + 0.95f) + Vector3.up * 0.15f);
            if (jackR.activeSelf) Bar(jackR.transform, m[JackR], W(0f, rearZ - 0.75f) + Vector3.up * 0.25f);
        }

        static void Hold(Transform prop, CrewMember who)
        {
            // pneu na frente do peito, face virada para a pessoa
            prop.position = who.HandsCenter + who.root.forward * 0.18f;
            prop.rotation = Quaternion.LookRotation(who.root.forward, Vector3.up);
        }

        static void HoldFlat(Transform prop, CrewMember who)
        {
            prop.position = who.HandsCenter + who.root.forward * 0.25f;
            prop.rotation = Quaternion.LookRotation(who.root.right, Vector3.up);
        }

        static void Bar(Transform prop, CrewMember who, Vector3 end)
        {
            Vector3 start = who.HandsCenter;
            Vector3 d = end - start;
            if (d.sqrMagnitude < 0.01f) return;
            prop.position = start;
            prop.rotation = Quaternion.LookRotation(d, Vector3.up);
            prop.localScale = new Vector3(1f, 1f, d.magnitude);
        }
    }
}
