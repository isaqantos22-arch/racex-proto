using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>
    /// Comemoração de pódio na tela de resultados: garrafa inclinando e espirrando champanhe (gotas douradas),
    /// faixa VITÓRIA/PÓDIO. Só UI (sem shader/material), tempo não escalado, pool fixo de gotas.
    /// </summary>
    public class PodiumCelebration : MonoBehaviour
    {
        const int Drops = 70;
        RectTransform bottle;
        RectTransform[] drops;
        Image[] dropImgs;
        Vector2[] vel;
        float[] life;
        float t;
        Vector2 nozzle;

        public static void Play(RectTransform parent, int position)
        {
            if (parent == null || position < 1 || position > 3) return;
            var go = new GameObject("PodiumCelebration", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            go.AddComponent<PodiumCelebration>().Build(rt, position);
        }

        void Build(RectTransform root, int position)
        {
            var gold = new Color(1f, 0.82f, 0.25f);
            string title = position == 1 ? "VITÓRIA!" : position == 2 ? "PÓDIO — 2º LUGAR" : "PÓDIO — 3º LUGAR";
            var top = new Vector2(0.5f, 1f);
            var banner = UIFactory.Panel("PodiumBanner", root, top, top, new Vector2(0f, 70f), new Vector2(760f, 84f), new Color(0.05f, 0.06f, 0.09f, 0.95f));
            banner.sprite = MenuTheme.RoundedSprite(190, 21, 10);
            UIFactory.Label("PodiumTitle", banner.transform, title, 50, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 80f), position == 1 ? gold : MenuTheme.Text);

            var bl = new Vector2(0f, 0f);
            bottle = UIFactory.Rect("Bottle", root, bl, bl, new Vector2(0.5f, 0f), new Vector2(-70f, 40f), new Vector2(60f, 220f));
            var glass = UIFactory.Panel("Glass", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(60f, 150f), new Color(0.08f, 0.3f, 0.14f, 1f));
            glass.sprite = MenuTheme.RoundedSprite(30, 75, 12);
            var label = UIFactory.Panel("Label", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(60f, 46f), new Color(0.95f, 0.93f, 0.85f, 1f));
            label.sprite = MenuTheme.RoundedSprite(30, 23, 4);
            var neck = UIFactory.Panel("Neck", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 146f), new Vector2(22f, 62f), new Color(0.08f, 0.3f, 0.14f, 1f));
            neck.sprite = MenuTheme.RoundedSprite(11, 31, 5);
            var foil = UIFactory.Panel("Foil", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(26f, 30f), gold);
            foil.sprite = MenuTheme.RoundedSprite(13, 15, 5);

            var dropSprite = MenuTheme.RoundedSprite(8, 8, 4);
            drops = new RectTransform[Drops];
            dropImgs = new Image[Drops];
            vel = new Vector2[Drops];
            life = new float[Drops];
            for (int i = 0; i < Drops; i++)
            {
                var d = UIFactory.Panel("Drop" + i, root, bl, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f), gold);
                d.sprite = dropSprite;
                d.enabled = false;
                drops[i] = (RectTransform)d.transform;
                dropImgs[i] = d;
                life[i] = -Random.Range(0f, 1.2f);
            }
        }

        void Update()
        {
            if (bottle == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            t += dt;
            float tilt = Mathf.Lerp(0f, -48f, Mathf.Clamp01(t / 0.8f)) + Mathf.Sin(t * 9f) * 4f;
            bottle.localRotation = Quaternion.Euler(0f, 0f, tilt);
            nozzle = bottle.anchoredPosition + (Vector2)(bottle.localRotation * new Vector3(0f, 215f, 0f));
            Vector2 dir = bottle.localRotation * Vector2.up;
            bool spraying = t > 0.6f && t < 6f;
            for (int i = 0; i < Drops; i++)
            {
                life[i] += dt;
                if (life[i] < 0f) continue;
                if (!dropImgs[i].enabled)
                {
                    if (!spraying) continue;
                    dropImgs[i].enabled = true;
                    drops[i].anchoredPosition = nozzle;
                    float spread = Random.Range(-14f, 14f);
                    vel[i] = (Vector2)(Quaternion.Euler(0f, 0f, spread) * dir) * Random.Range(650f, 1050f);
                    life[i] = 0f;
                    float sc = Random.Range(0.6f, 1.4f);
                    drops[i].localScale = new Vector3(sc, sc, 1f);
                }
                vel[i] += new Vector2(0f, -900f) * dt;
                drops[i].anchoredPosition += vel[i] * dt;
                var c = dropImgs[i].color;
                c.a = Mathf.Clamp01(1.4f - life[i]);
                dropImgs[i].color = c;
                if (life[i] > 1.4f)
                {
                    dropImgs[i].enabled = false;
                    life[i] = spraying ? -Random.Range(0f, 0.15f) : -999f;
                }
            }
        }
    }

    /// <summary>
    /// COMEMORAÇÃO CINEMATOGRÁFICA (pedido do usuário, referências: piloto de capacete com o carro atrás; piloto em pé em
    /// cima do carro; pódio só com os 3 primeiros). Palco próprio longe da pista: cópia do carro do jogador (parada,
    /// sem física) + piloto animado (o modelo da equipe de pit, macacão na cor da equipe, clipe Hand_Raising) + pódio de
    /// 3 degraus com os 3 primeiros, troféu e champanhe. 3 cenas cortadas; PULAR vai para a próxima, PULAR TUDO encerra.
    /// A cena 2 varia (em cima do carro ou ao lado, de braços para cima). Tudo em try/catch: falhou → só a tela de
    /// resultados (nunca trava o fim da corrida).
    /// </summary>
    public class VictoryCinematic : MonoBehaviour
    {
        static readonly Vector3 Stage = new Vector3(0f, 2500f, 0f);
        Camera cam;
        GameObject stageRoot;
        Canvas ui;
        Text caption, sub;
        System.Action onDone;
        RaceX.Core.CrewMember me;
        readonly List<RaceX.Core.CrewMember> podium = new List<RaceX.Core.CrewMember>();
        Transform carT;
        float t, shotT;
        int shot;
        int variant;
        ParticleSystem champ;
        Transform trophy;
        string[] names = new string[3];
        int myPos;
        string winnerName = "";
        readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        public static bool Play(RaceX.Core.RaceEntry mine, List<RaceX.Core.RaceEntry> top3, int position, System.Action done)
        {
            try
            {
                if (mine == null || mine.car == null || top3 == null || top3.Count < 3 || !RaceX.Core.PitCrewSystem.AssetsReady()) return false;
                var go = new GameObject("VictoryCinematic");
                var vc = go.AddComponent<VictoryCinematic>();
                vc.Build(mine, top3, position, done);
                return true;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Comemoração] desativada: " + ex.Message); return false; }
        }

        void Build(RaceX.Core.RaceEntry mine, List<RaceX.Core.RaceEntry> top3, int position, System.Action done)
        {
            onDone = done;
            myPos = position;
            // Cenas 1 e 2 são do VENCEDOR (pedido do usuário: "tem que aparecer o carro que venceu, não o meu").
            var winner = top3[0] ?? mine;
            winnerName = string.IsNullOrEmpty(winner.name) ? "" : winner.name;
            variant = Mathf.Abs((mine.name ?? "").GetHashCode() + System.DateTime.Now.Second) % 2;
            stageRoot = new GameObject("CelebrationStage");
            stageRoot.transform.position = Stage;
            var floorMat = RaceX.Core.MaterialLibrary.Get("CelebFloor", new Color(0.16f, 0.17f, 0.2f), 0.35f);
            var wallMat = RaceX.Core.MaterialLibrary.Get("CelebWall", new Color(0.1f, 0.22f, 0.55f), 0.3f);
            var stripeMat = RaceX.Core.MaterialLibrary.Get("CelebStripe", new Color(0.95f, 0.95f, 0.98f), 0.4f);
            Block("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(60f, 0.1f, 60f), floorMat);
            Block("Backdrop", new Vector3(0f, 6f, 12f), new Vector3(60f, 12f, 0.3f), wallMat);
            for (int i = 0; i < 7; i++) Block("Stripe" + i, new Vector3(-24f + i * 8f, 6f, 11.8f), new Vector3(3.2f, 0.5f, 0.1f), stripeMat);
            var lg = new GameObject("CelebLight"); lg.transform.SetParent(stageRoot.transform, false);
            lg.transform.localPosition = new Vector3(0f, 8f, -6f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 40f; l.intensity = 3f; l.color = new Color(1f, 0.97f, 0.9f);

            // carro (cópia sem física)
            var winSpec = winner.car != null && winner.car.spec != null ? winner.car.spec : mine.car.spec;
            var cc = RaceX.Car.CarFactory.Create(winSpec, winner.colorIndex, "CelebCar", Stage + new Vector3(-2.2f, 0.6f, 4f), Quaternion.Euler(0f, 205f, 0f));
            if (cc != null)
            {
                if (cc.Body != null) { cc.Body.isKinematic = true; cc.Body.detectCollisions = false; }
                cc.inputLocked = true;
                foreach (var mb in cc.GetComponents<MonoBehaviour>()) if (mb != null && !(mb is RaceX.Car.CarVisuals)) mb.enabled = false;
                carT = cc.transform;
                carT.SetParent(stageRoot.transform, true);
                var bb = new Bounds(carT.position, Vector3.zero); bool any = false;
                foreach (var r in carT.GetComponentsInChildren<Renderer>()) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                if (any) carT.position += Vector3.up * (Stage.y - bb.min.y);
                // O piloto está FORA do carro comemorando: some o piloto sentado do cockpit (print do usuário: dois pilotos).
                // O lugar do piloto sentado vira o lugar de onde ele LEVANTA no cockpit (pés no assento, sem vão).
                foreach (var tf in carT.GetComponentsInChildren<Transform>(true))
                    // O nó "Driver" fica no CAPACETE (olho do piloto): assento ~0,55 m abaixo e o quadril ~0,15 m atrás
                    // (piloto reclinado) — estimativa de projeto, não medida.
                    if (tf != carT && tf.name == "Driver") { standPos = tf.position - Vector3.up * 0.55f - carT.forward * 0.15f; break; }
                foreach (var tf in carT.GetComponentsInChildren<Transform>(true))
                    if (tf != carT && tf.name.StartsWith("Driver")) tf.gameObject.SetActive(false);
            }
            // piloto
            me = new RaceX.Core.CrewMember(stageRoot.transform, 0);
            me.SetMaterial(RaceX.Core.PitCrewSystem.SuitMaterial(RaceX.Car.CarPalette.Get(winner.colorIndex)));
            // pódio (3 degraus) + 3 pilotos
            var stepMat = RaceX.Core.MaterialLibrary.Get("CelebStep", new Color(0.85f, 0.86f, 0.9f), 0.3f);
            Vector3 pc = Stage + new Vector3(30f, 0f, 0f);
            float[] hx = { 0f, -1.9f, 1.9f }, hy = { 1.1f, 0.75f, 0.5f };
            for (int i = 0; i < 3; i++)
            {
                Block("Step" + i, pc - Stage + new Vector3(hx[i], hy[i] * 0.5f, 0f), new Vector3(1.8f, hy[i], 1.6f), stepMat);
                var pm = new RaceX.Core.CrewMember(stageRoot.transform, i + 1);
                var e = top3[i];
                pm.SetMaterial(RaceX.Core.PitCrewSystem.SuitMaterial(RaceX.Car.CarPalette.Get(e.colorIndex)));
                pm.root.position = pc + new Vector3(hx[i], hy[i], 0f);
                pm.root.rotation = Quaternion.Euler(0f, 180f, 0f);
                podium.Add(pm);
                names[i] = string.IsNullOrEmpty(e.name) ? "-" : e.name;
            }
            // troféu dourado do vencedor
            var gold = RaceX.Core.MaterialLibrary.Get("CelebGold", new Color(1f, 0.8f, 0.25f), 0.85f, 0.9f);
            var tr = new GameObject("Trophy").transform; tr.SetParent(stageRoot.transform, false);
            Prim(tr, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.12f, 0.12f), gold);
            Prim(tr, PrimitiveType.Sphere, new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.22f, 0.2f), gold);
            trophy = tr;
            // champanhe (partículas douradas)
            var pgo = new GameObject("Champagne"); pgo.transform.SetParent(stageRoot.transform, false);
            pgo.transform.position = pc + new Vector3(0f, hy[0] + 1.6f, -0.3f);
            champ = pgo.AddComponent<ParticleSystem>();
            var main = champ.main; main.startSpeed = 6f; main.startSize = 0.07f; main.startLifetime = 1.4f; main.gravityModifier = 0.9f; main.startColor = new Color(1f, 0.88f, 0.45f, 0.9f);
            main.useUnscaledTime = true; main.maxParticles = 600;
            var em = champ.emission; em.rateOverTime = 0f;
            var sh = champ.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f;
            pgo.transform.rotation = Quaternion.Euler(-55f, 0f, 0f);
            var pr = pgo.GetComponent<ParticleSystemRenderer>();
            if (pr != null) pr.sharedMaterial = Resources.Load<Material>("RaceX_Particle");

            // câmera e UI
            var cgo = new GameObject("CelebCam");
            cam = cgo.AddComponent<Camera>();
            cam.depth = 50f; cam.fieldOfView = 42f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            // Esconde TODA a interface do jogo (HUD, tabela, botões) durante a comemoração — o print do teste mostrou o HUD
            // por cima da cena. Volta tudo no fim.
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv != null && cv.enabled && cv.isRootCanvas) { cv.enabled = false; hiddenCanvases.Add(cv); }
            ui = UIFactory.CreateCanvas("CelebUI", 400);
            var urt = (RectTransform)ui.transform;
            var top = new Vector2(0.5f, 1f); var bot = new Vector2(0.5f, 0f);
            UIFactory.Panel("BarTop", urt, top, top, Vector2.zero, new Vector2(4000f, 110f), Color.black).raycastTarget = false;
            UIFactory.Panel("BarBot", urt, bot, bot, Vector2.zero, new Vector2(4000f, 150f), Color.black).raycastTarget = false;
            caption = UIFactory.Label("Caption", urt, "", 46, TextAnchor.MiddleCenter, bot, bot, new Vector2(0f, 96f), new Vector2(1500f, 60f), new Color(1f, 0.82f, 0.25f));
            sub = UIFactory.Label("Sub", urt, "", 26, TextAnchor.MiddleCenter, bot, bot, new Vector2(0f, 46f), new Vector2(1500f, 40f), Color.white);
            var br = new Vector2(1f, 0f);
            MenuTheme.StyledButton(urt, "Skip", "PULAR ►", br, br, new Vector2(-330f, 40f), new Vector2(260f, 70f), 24, NextShot);
            MenuTheme.StyledButton(urt, "SkipAll", "PULAR TUDO", br, br, new Vector2(-50f, 40f), new Vector2(260f, 70f), 24, Finish);
            shot = -1;
            NextShot();
        }

        /// <summary>Posição do piloto sentado (nó "Driver" do carro): de lá ele levanta no cockpit. Antes ficava a uma
        /// altura fixa (+0,75 m) sobre o carro e flutuava, com outro piloto sentado embaixo (print do usuário, 01/11).
        /// A malha dos carros não é legível (isReadable 0), então não dá para medir a superfície com colisor de malha.</summary>
        Vector3? standPos;

        void Block(string n, Vector3 local, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n;
            var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
            g.transform.SetParent(stageRoot.transform, false);
            g.transform.localPosition = local;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void Prim(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        float ShotLength => shot == 2 ? 9f : 5.5f;

        void NextShot()
        {
            shot++;
            shotT = 0f;
            if (shot > 2) { Finish(); return; }
            string pos = "VITÓRIA" + (winnerName.Length > 0 ? " — " + winnerName.ToUpperInvariant() : "");
            me.go.SetActive(shot < 2);
            foreach (var p in podium) p.go.SetActive(shot == 2);
            if (shot == 0)
            {
                // ao lado do carro, de frente para a câmera (referência 1)
                me.root.position = Stage + new Vector3(0.6f, 0f, 1.6f);
                me.root.rotation = Quaternion.Euler(0f, 200f, 0f);
                caption.text = pos + "!";
                sub.text = "Parque fechado";
            }
            else if (shot == 1)
            {
                if (variant == 0 && carT != null && standPos.HasValue)
                {
                    // de pé NO COCKPIT, braços para cima — pés no assento do piloto
                    me.root.position = standPos.Value;
                    me.root.rotation = Quaternion.Euler(0f, 180f, 0f);
                    sub.text = "De pé no cockpit, para a torcida";
                }
                else
                {
                    me.root.position = Stage + new Vector3(-0.6f, 0f, 0.4f);
                    me.root.rotation = Quaternion.Euler(0f, 170f, 0f);
                    sub.text = "Comemorando com a equipe";
                }
                caption.text = pos + "!";
            }
            else
            {
                caption.text = "PÓDIO";
                sub.text = "1º " + names[0] + "   ·   2º " + names[1] + "   ·   3º " + names[2];
                if (trophy != null) trophy.position = podium[0].RightHandPos + Vector3.up * 0.08f;
                var em = champ.emission; em.rateOverTime = 0f;
            }
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            t += dt; shotT += dt;
            if (cam == null) { Finish(); return; }
            try
            {
                if (shot == 0)
                {
                    me.Sample("Hand_Raising", shotT * 0.9f, true, null, 0f, false, 0f); me.LockHips();
                    // câmera baixa, aproximando do rosto (piloto na frente, carro atrás)
                    Vector3 focus = me.root.position + Vector3.up * 1.45f;
                    Vector3 from = focus + me.root.forward * Mathf.Lerp(3.2f, 2.2f, shotT / ShotLength) + me.root.right * 0.8f - Vector3.up * 0.35f;
                    cam.transform.position = from; cam.transform.LookAt(focus + me.root.right * 0.3f);
                }
                else if (shot == 1)
                {
                    me.Sample("Hand_Raising", shotT * 1.1f + 0.3f, true, null, 0f, false, 0f); me.LockHips();
                    Vector3 focus = (carT != null ? carT.position : me.root.position) + Vector3.up * 1.2f;
                    float a = Mathf.Lerp(-35f, 25f, shotT / ShotLength) * Mathf.Deg2Rad;
                    Vector3 from = focus + new Vector3(Mathf.Sin(a) * 7.5f, 0.6f, -Mathf.Cos(a) * 7.5f);
                    cam.transform.position = from; cam.transform.LookAt(focus);
                }
                else
                {
                    for (int i = 0; i < podium.Count; i++) { podium[i].Sample("Hand_Raising", shotT * 0.9f + i * 0.4f, true, null, 0f, false, 0f); podium[i].LockHips(); }
                    // troféu NA MÃO do vencedor (antes ficava parado acima da cabeça)
                    if (trophy != null) trophy.position = podium[0].RightHandPos + Vector3.up * 0.08f;
                    if (shotT > 2.5f && shotT < 6.5f) { var em = champ.emission; em.rateOverTime = 180f; }
                    else { var em = champ.emission; em.rateOverTime = 0f; }
                    Vector3 focus = podium[0].root.position + Vector3.up * 0.8f;
                    Vector3 from = focus + new Vector3(Mathf.Lerp(-1.5f, 1.5f, shotT / ShotLength), 0.4f, -Mathf.Lerp(9f, 6.5f, shotT / ShotLength));
                    cam.transform.position = from; cam.transform.LookAt(focus);
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[Comemoração] " + ex.Message); Finish(); return; }
            if (shotT >= ShotLength) NextShot();
        }

        bool finished;
        void Finish()
        {
            if (finished) return;
            finished = true;
            if (cam != null) Destroy(cam.gameObject);
            if (ui != null) Destroy(ui.gameObject);
            if (stageRoot != null) Destroy(stageRoot);
            foreach (var cv in hiddenCanvases) if (cv != null) cv.enabled = true;
            hiddenCanvases.Clear();
            var cb = onDone; onDone = null;
            Destroy(gameObject);
            cb?.Invoke();
        }
    }
}
