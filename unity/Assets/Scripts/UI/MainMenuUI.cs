using System;
using System.Collections;
using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Splash → Menu principal → Jogar → Configurações da corrida → Corrida.</summary>
    public class MainMenuUI : MonoBehaviour
    {
        static readonly string[] TrackNames =
        {
            "Circuito Verde", "Desfiladeiro Solar", "Metrópole Noturna", "Velocità Park",
            "Principado à Beira-Mar", "Floresta de Ardenne", "Jardim da Baía", "Golfo Real",
            "Serra do Café", "Circuito do Sol", "Parque Real", "Dragão Oriental",
            "Corniche da Noite", "Porto Sul", "Ilha das Cascatas", "Vale Dourado",
            "Colinas Verdes", "Campo do Norte", "Curvas do Danúbio", "Dunas do Norte",
            "Fortaleza Antiga", "Colinas do Texas", "Estádio Central", "Faixa de Neon"
        };
        static readonly string[] DifficultyNames = { "Fácil", "Médio", "Difícil", "Expert" };
        static readonly string[] CameraNames = { "Terceira pessoa", "Primeira pessoa", "Volante", "Frontal" };
        static readonly string[] QualityNames = { "Baixa", "Média", "Alta", "Ultra" };
        static readonly string[] ControlNames = { "Botões na tela", "Inclinação", "Joystick virtual" };

        Canvas canvas;
        RectTransform current;
        static MainMenuUI instance;
        Action back;
        RaceSettings settings;
        Transform showroomCar;
        Transform showroomCar2;
        Camera menuCam;
        int garageIndex;
        bool showroomWide;
        MenuDemo demo;
        GameObject turntable;
        Light keyLight;

        static bool splashShown;

        string onlineName = "Jogador";
        string onlineCode = "AAAAAA";
        RectTransform lobbyScreen;
        Transform lobbyList;
        Text onlineSetupStatus;
        Text lobbyStatus;
        Button lobbyReadyBtn;
        Button lobbyStartBtn;
        bool carSentForRoom;
        InputField serverUrlField;
        float lobbyPollTimer;
        InputField accEmailField;
        InputField accPassField;
        InputField accNameField;
        InputField accPhotoField;
        Text accountStatus;
        RectTransform accountBody;
        int accountTab;

        void Start()
        {
            instance = this;
            Time.timeScale = 1f;
            settings = GameManager.Settings;
            UIFactory.EnsureEventSystem();
            canvas = UIFactory.CreateCanvas("MenuCanvas");
            GameAudio.EnterMenu();
            AccountClient.RestoreSession();
            // Teste automático (autotest.txt no persistentDataPath): vai direto para a corrida.
            if (RaceX.Core.AutoTest.TryLaunchFromMenu(this)) return;
            StartCoroutine(StartupFlow());
        }

        IEnumerator StartupFlow()
        {
            bool skipSplash = splashShown;
            yield return null;
            try { BuildShowroom(); } catch (Exception ex) { Debug.LogError("[MainMenuUI] BuildShowroom failed: " + ex); }
            try { demo = MenuDemo.Create(this, canvas, menuCam); } catch (Exception ex) { Debug.LogError("[MainMenuUI] MenuDemo failed: " + ex); }
            if (skipSplash) ShowMain();
            else yield return StartCoroutine(Splash());
            var p = settings.players[0];
            yield return ImportedCarModels.PrewarmAsync(p.car);
            if (settings.mode == GameMode.LocalSplitScreen)
                yield return ImportedCarModels.PrewarmAsync(settings.players[1].car);
            RefreshShowroomCar();
        }

        /// <summary>Esconde/mostra a UI do menu principal (usado pelo modo carreira para não sobrepor telas).</summary>
        internal static void SetMenuVisible(bool on)
        {
            if (instance != null && instance.canvas != null) instance.canvas.gameObject.SetActive(on);
        }

        void Update()
        {
            if (showroomCar != null) showroomCar.Rotate(0f, 25f * Time.deltaTime, 0f, Space.World);
            if (showroomCar2 != null) showroomCar2.Rotate(0f, 25f * Time.deltaTime, 0f, Space.World);
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && back != null) back();
#endif
            if (lobbyScreen != null)
            {
                lobbyPollTimer += Time.deltaTime;
                if (lobbyPollTimer >= 0.5f)
                {
                    lobbyPollTimer = 0f;
                    RefreshLobby();
                }
            }
        }

        // ---------- Showroom 3D ----------
        void BuildShowroom()
        {
            var camGo = new GameObject("MenuCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 2.2f, -7.5f), Quaternion.Euler(10f, 0f, 0f));
            camGo.AddComponent<AudioListener>();
            menuCam = cam;

            var light = new GameObject("KeyLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            keyLight = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);

            GameObject floor = null;
            try
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var c = floor.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MainMenuUI] Cylinder primitive failed: " + ex.Message + ", creating fallback floor");
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var c = floor.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            floor.name = "Turntable";
            floor.transform.localScale = new Vector3(9f, 0.05f, 9f);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Get("Showroom", new Color(0.15f, 0.16f, 0.2f), 0.8f);
            turntable = floor;
        }

        Coroutine showroomRefreshRoutine;

        /// <summary>Troca de carro no setup/garagem: carrega o modelo (FBX) em segundo plano antes de montar,
        /// evitando o travamento visível que o Resources.Load síncrono causava ao trocar de carro.</summary>
        internal void RefreshShowroomCarAsync()
        {
            if (showroomRefreshRoutine != null) StopCoroutine(showroomRefreshRoutine);
            showroomRefreshRoutine = StartCoroutine(RefreshShowroomCarRoutine());
        }

        IEnumerator RefreshShowroomCarRoutine()
        {
            var p = settings.players[0];
            yield return ImportedCarModels.PrewarmAsync(p.car);
            if (settings.mode == GameMode.LocalSplitScreen)
                yield return ImportedCarModels.PrewarmAsync(settings.players[1].car);
            RefreshShowroomCar();
            showroomRefreshRoutine = null;
        }

        internal void RefreshShowroomCar()
        {
            float yaw = showroomCar != null ? showroomCar.eulerAngles.y : 200f;
            if (showroomCar != null) Destroy(showroomCar.gameObject);
            if (showroomCar2 != null) Destroy(showroomCar2.gameObject);
            bool split = settings.mode == GameMode.LocalSplitScreen;
            var p = settings.players[0];
            var spec = CarSpec.CreateDefault(p.car);
            float restY = Mathf.Max(0.1f, spec.wheelRadius - spec.suspensionDistance * 0.5f);
            var car = CarFactory.Create(CarSpec.CreateDefault(p.car), p.colorIndex, "Showroom_" + p.car, new Vector3(split ? -2.2f : 0f, restY, 0f), Quaternion.Euler(0f, yaw, 0f));
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            car.enabled = false;
            showroomCar = car.transform;
            if (split)
            {
                var p2 = settings.players[1];
                var spec2 = CarSpec.CreateDefault(p2.car);
                float restY2 = Mathf.Max(0.1f, spec2.wheelRadius - spec2.suspensionDistance * 0.5f);
                var car2 = CarFactory.Create(CarSpec.CreateDefault(p2.car), p2.colorIndex, "Showroom_" + p2.car, new Vector3(2.2f, restY2, 0f), Quaternion.Euler(0f, yaw, 0f));
                var rb2 = car2.GetComponent<Rigidbody>();
                if (rb2 != null) rb2.isKinematic = true;
                car2.enabled = false;
                showroomCar2 = car2.transform;
            }
            if (demo != null && demo.Running) SetShowroomVisible(false);
        }

        internal void SetShowroomVisible(bool on)
        {
            if (turntable != null) turntable.SetActive(on);
            if (keyLight != null) keyLight.enabled = on;
            if (showroomCar != null) showroomCar.gameObject.SetActive(on);
            if (showroomCar2 != null) showroomCar2.gameObject.SetActive(on);
            if (on) SetShowroomCam(new Vector3(0f, 2.5f, -8.6f), 10f);
        }

        void SetShowroomCam(Vector3 pos, float pitch)
        {
            if (menuCam == null) return;
            menuCam.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, 0f, 0f));
        }

        // ---------- Telas ----------
        RectTransform NewScreen(string title)
        {
            if (current != null) Destroy(current.gameObject);
            current = UIFactory.Stretch("Screen_" + title, canvas.transform);
            UIFactory.ApplySafeArea(current, new Rect(0f, 0f, 1f, 1f));

            var bg = UIFactory.Panel("Bg", current, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.52f));
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero;
            bg.raycastTarget = false;

            var side = UIFactory.Rect("SidePanel", current, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(430f, 0f));
            var sideImg = side.gameObject.AddComponent<Image>();
            sideImg.color = new Color(0.028f, 0.032f, 0.05f, 0.97f);
            UIFactory.Label("SideTitle", side, "RACEX PROTO", 28, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 40f), new Vector2(480f, 34f), MenuTheme.Accent).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            UIFactory.Label("SideVersion", side, "PROTO BUILD", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(320f, 30f), MenuTheme.TextDim);
            var sideLine = UIFactory.Panel("SideLine", side, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(2f, 1080f), MenuTheme.Accent);
            sideLine.raycastTarget = false;

            UIFactory.Label("Title", current, title, 64, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(ContentX, -30f), new Vector2(1400f, 100f), MenuTheme.Text);
            if (demo != null) demo.SetAllowed(false);
            SetShowroomCam(new Vector3(0f, 2.2f, -7.5f), 10f);
            return current;
        }

        IEnumerator Splash()
        {
            splashShown = true;
            var s = NewScreen("");
            var logo = UIFactory.Label("Logo", s, "RaceX Proto", 140, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 220f));
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / 0.6f);
                logo.color = new Color(1f, 1f, 1f, k);
                logo.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, k);
                yield return null;
            }
            ShowMain();
        }

        Button MenuButton(RectTransform parent, string label, int row, Action onClick)
        {
            var c = new Vector2(0.5f, 0.5f);
            return MenuTheme.StyledButton(parent, "Btn_" + label, label, c, c, new Vector2(ContentX, 220f - row * 120f), new Vector2(520f, 100f), 38, onClick);
        }

        void ShowMain()
        {
            back = Application.isMobilePlatform ? null : (Action)Application.Quit;
            var s = NewScreen("RACEX PROTO");
            showroomWide = true;
            SetShowroomCam(new Vector3(0f, 2.5f, -8.6f), 10f);
            demo.SetAllowed(true);
            // Âncora pelo TOPO da área segura (em vez do centro): garante que o primeiro item
            // nunca fique cortado, independente da proporção real da tela do aparelho.
            var top = new Vector2(0.5f, 1f);
            var bsize = new Vector2(360f, 62f);
            const float startY = -150f;   // abaixo do título "RACEX PROTO"
            const float step = 84f;
            int row = 0;

            MenuTheme.StyledButton(s, "MainCareer", "MODO CARREIRA", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, () => CareerFlow.Open());
            MenuTheme.StyledButton(s, "MainPlay", "JOGAR", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowPlay);
            MenuTheme.StyledButton(s, "MainGarage", "GARAGEM", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowGarage);
            MenuTheme.StyledButton(s, "MainProgress", "PROGRESSO", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowProgress);
            MenuTheme.StyledButton(s, "MainSettings", "CONFIGURAÇÕES", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowSettings);
            MenuTheme.StyledButton(s, "MainRecords", "RECORDES", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowRecords);
            MenuTheme.StyledButton(s, "MainAccount", "CONTA", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowAccount);
            if (!Application.isMobilePlatform) MenuTheme.StyledButton(s, "MainQuit", "SAIR", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, Application.Quit);
        }

        internal void ShowPlay()
        {
            back = ShowMain;
            var s = NewScreen("JOGAR");
            // Mesmo lugar/tamanho dos botões do menu principal (barra lateral) — antes ficavam soltos no
            // meio da tela, por cima do carro do expositor.
            var top = new Vector2(0.5f, 1f);
            var bsize = new Vector2(360f, 62f);
            int row = 0;
            Action<string, Action> side = (label, act) => MenuTheme.StyledButton(s, "Play_" + label, label, top, top, new Vector2(-745f, -150f - 84f * row++), bsize, 26, act);
            side("SINGLE PLAYER", () => { settings.mode = GameMode.SinglePlayer; ShowRaceSetup(); });
            if (!Application.isMobilePlatform) side("MULTIPLAYER LOCAL", () => { settings.mode = GameMode.LocalSplitScreen; ShowRaceSetup(); });
            side("ONLINE", ShowOnlineSetup);
            row++;
            side("VOLTAR", ShowMain);
        }

        // ---------- ONLINE (v80): hub com dois cartões (CRIAR x ENTRAR) e sala com papéis ----------
        static readonly Color HostColor = MenuTheme.Accent;                    // laranja = anfitrião
        static readonly Color GuestColor = new Color(0.27f, 0.62f, 1f);        // azul = convidado
        static readonly Color OkColor = new Color(0.32f, 0.86f, 0.46f);
        static readonly Color WarnColor = new Color(1f, 0.78f, 0.22f);
        static readonly Color BadColor = new Color(1f, 0.36f, 0.3f);
        static readonly Color CardColor = new Color(0.05f, 0.06f, 0.09f, 0.95f);

        Text serverPill;
        Coroutine healthRoutine;

        internal void ShowOnlineSetup()
        {
            back = () => { UnbindOnline(); ShowPlay(); };
            lobbyScreen = null;
            if (AccountClient.IsLoggedIn && !string.IsNullOrWhiteSpace(AccountClient.Name)) onlineName = AccountClient.Name;
            var s = NewScreen("JOGAR ONLINE");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.9f);
            var c = new Vector2(0.5f, 0.5f);

            // Linha do piloto (nome) + estado do servidor (o teste /health também ACORDA o Render).
            UIFactory.Label("NameLabel", s, "SEU NOME", 22, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 380f, 318f), new Vector2(200f, 40f), MenuTheme.TextDim);
            var nameField = MakeField(s, "NameField", c, new Vector2(ContentX - 150f, 318f), new Vector2(420f, 66f), onlineName, "Seu nome", 32);
            nameField.characterLimit = 14;
            nameField.onValueChanged.AddListener(v => onlineName = string.IsNullOrWhiteSpace(v) ? "Jogador" : v);
            serverPill = UIFactory.Label("ServerPill", s, "●  VERIFICANDO SERVIDOR...", 24, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX + 720f, 318f), new Vector2(620f, 40f), MenuTheme.TextDim);

            // Cartão ANFITRIÃO
            var host = OnlineCard(s, "HostCard", ContentX - 370f, HostColor, "CRIAR SALA", "VOCÊ SERÁ O ANFITRIÃO");
            CardBullet(host, 0, "Escolhe pista, voltas, carros e regras");
            CardBullet(host, 1, "Recebe um código para passar aos amigos");
            CardBullet(host, 2, "Inicia a corrida quando todos estiverem prontos");
            var bc = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(host, "CreateRoom", "CRIAR SALA", bc, bc, new Vector2(0f, 34f), new Vector2(600f, 96f), 36, CreateOnlineRoom);

            // Cartão CONVIDADO
            var guest = OnlineCard(s, "GuestCard", ContentX + 370f, GuestColor, "ENTRAR EM SALA", "VOCÊ SERÁ CONVIDADO");
            var tl = new Vector2(0f, 1f);
            UIFactory.Label("CodeLabel", guest, "CÓDIGO DA SALA", 22, TextAnchor.MiddleLeft, tl, tl, new Vector2(40f, -128f), new Vector2(400f, 34f), MenuTheme.TextDim);
            var code = MakeField(guest, "CodeField", new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(600f, 100f), onlineCode, "EX.: 7NKUVQ", 52);
            code.characterLimit = 6;
            code.contentType = InputField.ContentType.Alphanumeric;
            code.onValueChanged.AddListener(v => { onlineCode = NormalizeCode(v); if (code.text != onlineCode) { code.text = onlineCode; code.MoveTextEnd(false); } });
            codeInput = code;
            UIFactory.Label("CodeHint", guest, "Peça ao anfitrião o código de 6 letras", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -290f), new Vector2(600f, 32f), MenuTheme.TextDim);
            Tint(MenuTheme.StyledButton(guest, "JoinRoom", "ENTRAR", bc, bc, new Vector2(0f, 34f), new Vector2(600f, 96f), 36, JoinOnlineRoom), GuestColor);

            // Avançado: servidor (só para quem roda um servidor próprio na rede).
            UIFactory.Label("ServerLabel", s, "SERVIDOR", 20, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 470f, -262f), new Vector2(200f, 34f), MenuTheme.TextDim);
            string savedUrl = PlayerPrefs.GetString(OnlineClient.ServerUrlKey, "");
            serverUrlField = MakeField(s, "ServerField", c, new Vector2(ContentX + 40f, -262f), new Vector2(980f, 50f), savedUrl.Length > 0 ? savedUrl : OnlineClient.DefaultServerUrl, "ws://ip:3000", 20);
            MenuTheme.StyledButton(s, "ServerSave", "SALVAR", c, c, new Vector2(ContentX + 620f, -262f), new Vector2(150f, 50f), 22, () => { ApplyServerUrl(); CheckServer(); });
            UIFactory.Label("ServerHint", s, "Só altere se for usar um servidor próprio na sua rede (ex.: 192.168.0.10:3000)", 18, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX, -300f), new Vector2(1200f, 28f), MenuTheme.TextDim);

            onlineSetupStatus = UIFactory.Label("OnlineStatus", s, "", 26, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX, -350f), new Vector2(1300f, 40f), WarnColor);
            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "OnlineBack", "VOLTAR", b, b, new Vector2(ContentX, 40f), new Vector2(380f, 96f), 34, () => { UnbindOnline(); ShowPlay(); });
            BindOnline();
            CheckServer();
        }

        /// <summary>Cartão do hub online: barra colorida no topo, título e o papel (anfitrião/convidado).</summary>
        RectTransform OnlineCard(RectTransform parent, string name, float x, Color accent, string title, string role)
        {
            var c = new Vector2(0.5f, 0.5f);
            var card = UIFactory.Panel(name, parent, c, c, new Vector2(x, 20f), new Vector2(700f, 460f), CardColor);
            card.sprite = MenuTheme.RoundedSprite(140, 100, 16);
            var rt = (RectTransform)card.transform;
            var tl = new Vector2(0f, 1f);
            UIFactory.Panel(name + "_Bar", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(700f, 8f), accent);
            UIFactory.Label(name + "_Title", rt, title, 42, TextAnchor.MiddleLeft, tl, tl, new Vector2(40f, -26f), new Vector2(620f, 56f), MenuTheme.Text);
            UIFactory.Label(name + "_Role", rt, role, 22, TextAnchor.MiddleLeft, tl, tl, new Vector2(40f, -84f), new Vector2(620f, 30f), accent);
            return rt;
        }

        void CardBullet(RectTransform card, int i, string text)
        {
            var tl = new Vector2(0f, 1f);
            UIFactory.Label("Bullet" + i, card, "•  " + text, 24, TextAnchor.MiddleLeft, tl, tl, new Vector2(40f, -146f - i * 52f), new Vector2(640f, 40f), new Color(0.8f, 0.82f, 0.86f));
        }

        /// <summary>Troca a cor da linha de destaque do botão (convidado = azul).</summary>
        static void Tint(Button b, Color col)
        {
            if (b == null) return;
            var line = b.transform.Find(b.name + "_Line");
            if (line != null) line.GetComponent<Image>().color = col;
        }

        InputField MakeField(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string value, string placeholder, int font)
        {
            var bg = UIFactory.Panel(name, parent, anchor, new Vector2(0.5f, 0.5f), pos, size, new Color(1f, 1f, 1f, 0.07f));
            bg.sprite = MenuTheme.RoundedSprite(140, 60, 12);
            bg.raycastTarget = true;
            var c = new Vector2(0.5f, 0.5f);
            var inner = new Vector2(size.x - 24f, size.y - 8f);
            var ph = UIFactory.Label(name + "_Ph", bg.transform, placeholder, font, TextAnchor.MiddleCenter, c, c, Vector2.zero, inner, new Color(1f, 1f, 1f, 0.25f));
            var txt = UIFactory.Label(name + "_Text", bg.transform, value, font, TextAnchor.MiddleCenter, c, c, Vector2.zero, inner, MenuTheme.Text);
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.placeholder = ph;
            input.textComponent = txt;
            input.text = value;
            return input;
        }

        /// <summary>Consulta /health (HTTP) — mostra o estado e já ACORDA o servidor gratuito antes do clique.</summary>
        void CheckServer()
        {
            if (healthRoutine != null) StopCoroutine(healthRoutine);
            healthRoutine = StartCoroutine(CheckServerRoutine());
        }

        static string HealthUrl(string wsUrl)
        {
            try
            {
                var u = new Uri(wsUrl);
                string scheme = u.Scheme == "wss" || u.Scheme == "https" ? "https" : "http";
                return scheme + "://" + u.Authority + "/health";
            }
            catch { return null; }
        }

        IEnumerator CheckServerRoutine()
        {
            string url = HealthUrl(OnlineClient.ServerUrl);
            if (serverPill == null || url == null) yield break;
            serverPill.text = "●  ACORDANDO O SERVIDOR...";
            serverPill.color = WarnColor;
            var req = UnityWebRequest.Get(url);
            req.timeout = 75;
            yield return req.SendWebRequest();
            if (serverPill == null) yield break;
            bool ok = req.result == UnityWebRequest.Result.Success && req.downloadHandler.text.Contains("\"ok\":true");
            serverPill.text = ok ? "●  SERVIDOR ONLINE" : "●  SERVIDOR INDISPONÍVEL";
            serverPill.color = ok ? OkColor : BadColor;
            req.Dispose();
            healthRoutine = null;
        }

        void ApplyServerUrl()
        {
            string value = serverUrlField != null && !string.IsNullOrEmpty(serverUrlField.text) ? serverUrlField.text : OnlineClient.DefaultServerUrl;
            OnlineClient.ServerUrl = value;
            Debug.Log("[Online] servidor definido: " + OnlineClient.ServerUrl);
        }

        static readonly string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        InputField codeInput;

        /// <summary>Mantém apenas caracteres do alfabeto de códigos (A-Z sem I/O e 2-9), em maiúsculas, até 6.</summary>
        string NormalizeCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char chRaw in raw.ToUpperInvariant())
            {
                char ch = chRaw;
                if (ch == 'I') ch = 'J';
                if (ch == 'O') ch = '0';
                if (CodeAlphabet.IndexOf(ch) >= 0) sb.Append(ch);
                if (sb.Length >= 6) break;
            }
            return sb.ToString();
        }

        // ---------- CONTA / PERFIL (Supabase) ----------
        void ShowAccount()
        {
            back = ShowMain;
            pendingAvatar = -2; // relê o avatar salvo na conta
            var s = NewScreen(AccountClient.IsLoggedIn ? "MINHA CONTA" : "ENTRAR");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.86f);
            var c = new Vector2(0.5f, 0.5f);
            accountStatus = UIFactory.Label("AccountStatus", s, "", 26, TextAnchor.MiddleCenter, c, c, new Vector2(230f, -430f), new Vector2(1200f, 56f), MenuTheme.Accent);
            accountBody = UIFactory.Rect("AccountBody", s, c, c, c, new Vector2(230f, 30f), new Vector2(1300f, 780f));
            if (AccountClient.IsLoggedIn) ShowAccountProfile();
            else ShowAccountAuth();
            MenuTheme.StyledButton(s, "AccBack", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(ContentX, 24f), new Vector2(380f, 70f), 28, ShowMain);
        }

        void SetAccountStatus(string msg)
        {
            if (accountStatus != null) accountStatus.text = msg ?? "";
        }

        InputField AccountField(RectTransform parent, string label, int row, string value, InputField.ContentType type, string placeholder = "")
        {
            float y = 170f - row * 104f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label("AccLbl" + label, parent, label, 30, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(-670f, y), new Vector2(420f, 76f));
            var bg = UIFactory.Panel("AccBg" + label, parent, c, c, new Vector2(70f, y), new Vector2(780f, 80f), new Color(1f, 1f, 1f, 0.07f));
            bg.raycastTarget = true; // o campo PRECISA ser clicável (targetGraphic do InputField) — raycast false fazia o toque atravessar e ninguém digitava
            var ph = UIFactory.Label("AccPh" + label, bg.transform, placeholder, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 64f), new Color(1f, 1f, 1f, 0.25f));
            ph.raycastTarget = false;
            var txt = UIFactory.Label("AccTx" + label, bg.transform, value ?? "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 64f), MenuTheme.Text);
            txt.raycastTarget = false;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.placeholder = ph;
            input.textComponent = txt;
            input.text = value ?? "";
            input.contentType = type;
            return input;
        }

        void ShowAccountAuth()
        {
            accountTab = 0;
            foreach (Transform child in accountBody) Destroy(child.gameObject);
            accEmailField = AccountField(accountBody, "E-MAIL", 0, AccountClient.Email ?? "", InputField.ContentType.EmailAddress, "voce@exemplo.com");
            accPassField = AccountField(accountBody, "SENHA", 1, "", InputField.ContentType.Password, "mínimo 6 caracteres");
            accNameField = AccountField(accountBody, "NOME NO JOGO", 2, AccountClient.Name ?? "", InputField.ContentType.Standard, "");
            var c = new Vector2(0.5f, 0.5f);
            MenuTheme.StyledButton(accountBody, "AccLogin", "ENTRAR", c, c, new Vector2(-310f, -240f), new Vector2(540f, 88f), 32, AccountDoLogin);
            MenuTheme.StyledButton(accountBody, "AccSignup", "CRIAR CONTA", c, c, new Vector2(310f, -240f), new Vector2(540f, 88f), 32, AccountDoSignup);
        }

        void AccountDoLogin()
        {
            if (accEmailField == null || accPassField == null) return;
            string email = accEmailField.text.Trim(), pass = accPassField.text;
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
            {
                SetAccountStatus("Preencha o e-mail e a senha.");
                return;
            }
            SetAccountStatus("Entrando...");
            AccountClient.LogIn(email, pass, (ok, err) =>
            {
                if (ok)
                {
                    onlineName = AccountClient.Name;
                    SetAccountStatus("");
                    ShowAccountProfile();
                }
                else SetAccountStatus(err);
            });
        }

        void AccountDoSignup()
        {
            if (accEmailField == null || accPassField == null) return;
            string email = accEmailField.text.Trim(), pass = accPassField.text;
            if (string.IsNullOrEmpty(email) || pass.Length < 6)
            {
                SetAccountStatus("Preencha o e-mail e uma senha de no mínimo 6 caracteres.");
                return;
            }
            SetAccountStatus("Criando conta...");
            AccountClient.SignUp(email, pass, string.IsNullOrWhiteSpace(accNameField.text) ? "Jogador" : accNameField.text.Trim(), (ok, err) =>
            {
                if (ok)
                {
                    onlineName = AccountClient.Name;
                    SetAccountStatus("Conta criada e conectada!");
                    ShowAccountProfile();
                }
                else SetAccountStatus(err);
            });
        }

        void ShowAccountProfile()
        {
            accountTab = 1;
            foreach (Transform child in accountBody) Destroy(child.gameObject);
            accEmailField = null;
            accPassField = null;
            var c = new Vector2(0.5f, 0.5f);

            // Estrutura de perfil (tipo Steam): CABEÇALHO (avatar + nome + e-mail + status)
            // → FAIXA DE NÚMEROS (4 células) → GRADE 2×2 (nome/e-mail/cor/foto) → AÇÕES em par.
            // Sem áreas vazias: cada faixa ocupa a largura toda do corpo (1300).
            // ---- CABEÇALHO ----
            if (pendingAvatar < -1) pendingAvatar = PresetOf(AccountClient.Photo);
            var avatar = UIFactory.Panel("Avatar", accountBody, c, c, new Vector2(-530f, 300f), new Vector2(150f, 150f), AvatarColorOf(AccountClient.AvatarColor));
            var initial = UIFactory.Label("AvatarInitial", avatar.transform, string.IsNullOrEmpty(AccountClient.Name) ? "J" : AccountClient.Name.Substring(0, 1).ToUpperInvariant(), 68, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f), Color.white);
            initial.raycastTarget = false;
            if (pendingAvatar >= 0)
            {
                DrawHelmet((RectTransform)avatar.transform, pendingAvatar, 150f);
                initial.text = "";
            }
            else if (!string.IsNullOrEmpty(AccountClient.Photo) && AccountClient.Photo.Length > 128)
            {
                try
                {
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(Convert.FromBase64String(AccountClient.Photo)))
                    {
                        var shot = UIFactory.Rect("AvatarPhoto", avatar.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 140f));
                        var raw = shot.gameObject.AddComponent<RawImage>();
                        raw.texture = tex;
                        raw.raycastTarget = false;
                        initial.text = "";
                    }
                    else Destroy(tex);
                }
                catch (Exception) { /* foto corrompida: mantém a inicial */ }
            }
            UIFactory.Label("AccName", accountBody, string.IsNullOrEmpty(AccountClient.Name) ? "Jogador" : AccountClient.Name, 52, TextAnchor.MiddleLeft, c, c, new Vector2(-30f, 330f), new Vector2(800f, 80f), MenuTheme.Text);
            UIFactory.Label("AccEmail", accountBody, AccountClient.Email, 28, TextAnchor.MiddleLeft, c, c, new Vector2(-30f, 280f), new Vector2(800f, 46f), MenuTheme.TextDim);
            UIFactory.Label("AccStatus", accountBody, "CONTA CONFIRMADA  •  ONLINE", 24, TextAnchor.MiddleLeft, c, c, new Vector2(-30f, 238f), new Vector2(800f, 40f), new Color(0.35f, 0.9f, 0.45f));

            // ---- FAIXA DE NÚMEROS (4 células, largura toda) ----
            AccountStatCell(-495f, "VITÓRIAS", AccountClient.Wins.ToString());
            AccountStatCell(-165f, "PÓDIOS", AccountClient.Podiums.ToString());
            AccountStatCell(165f, "TÍTULOS", AccountClient.Titles.ToString());
            AccountStatCell(495f, "CORRIDAS", AccountClient.Races.ToString());

            // ---- GRADE 2×2 (células de 610×116) ----
            var nameCell = AccountCell(-320f, -30f, "NOME NO JOGO");
            accNameField = AccountFieldIn(nameCell, AccountClient.Name ?? "Jogador", InputField.ContentType.Standard, "");
            var emailCell = AccountCell(320f, -30f, "E-MAIL");
            UIFactory.Label("CellEmailV", emailCell.transform, AccountClient.Email, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(570f, 60f), MenuTheme.Text);

            var colorCell = AccountCellBg(-320f, -160f);
            var colorBtn = MenuTheme.StyledButton((RectTransform)colorCell.transform, "AccColor", "COR DO AVATAR: " + AvatarColorName(AccountClient.AvatarColor), c, c, Vector2.zero, new Vector2(570f, 76f), 28, null);
            int nextColor = AccountClient.AvatarColor;
            var colorLabel = colorBtn.transform.Find("AccColor_Label").GetComponent<Text>();
            Action paintColor = () =>
            {
                var col = AvatarColorOf(nextColor);
                colorBtn.image.color = col;
                // Texto escuro em cor clara (antes branco no amarelo, ilegível).
                colorLabel.color = col.r * 0.299f + col.g * 0.587f + col.b * 0.114f > 0.6f ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
                // Prévia: o quadrado do avatar já mostra a cor nova (salva com SALVAR PERFIL).
                var av = accountBody != null ? accountBody.Find("Avatar") : null;
                if (av != null) av.GetComponent<Image>().color = col;
            };
            colorBtn.onClick.AddListener(() =>
            {
                nextColor = (nextColor + 1) & 7;
                colorLabel.text = "COR DO AVATAR: " + AvatarColorName(nextColor);
                paintColor();
            });
            paintColor();

            // AVATAR LOCAL (v79): sem URL — escolhe um dos capacetes prontos (fica salvo na conta).
            var photoCell = AccountCellBg(320f, -160f);
            accPhotoField = null;
            MenuTheme.StyledButton((RectTransform)photoCell.transform, "AccAvatar", pendingAvatar >= 0 ? "AVATAR: CAPACETE " + (pendingAvatar + 1) : "ESCOLHER AVATAR", c, c, Vector2.zero, new Vector2(570f, 76f), 28,
                () => ShowAvatarPicker(nextColor));

            // ---- AÇÕES (4 botões, largura toda) ----
            MenuTheme.StyledButton(accountBody, "AccSave", "SALVAR PERFIL", c, c, new Vector2(-480f, -300f), new Vector2(300f, 84f), 24, () => AccountDoSave(nextColor));
            MenuTheme.StyledButton(accountBody, "AccSync", "SINCRONIZAR", c, c, new Vector2(-160f, -300f), new Vector2(300f, 84f), 24, () =>
            {
                SetAccountStatus("Sincronizando com a nuvem...");
                AccountClient.SetProfile(AccountClient.Name, null, AccountClient.AvatarColor, (ok, err) => SetAccountStatus(ok ? "Tudo sincronizado com a nuvem." : "Não sincronizou: " + err));
            });
            MenuTheme.StyledButton(accountBody, "AccReset", "REDEFINIR SENHA", c, c, new Vector2(160f, -300f), new Vector2(300f, 84f), 22, () =>
            {
                SetAccountStatus("Enviando e-mail...");
                AccountClient.SendPasswordReset(AccountClient.Email, (ok, err) => SetAccountStatus(ok ? "Enviamos um link de redefinição para " + AccountClient.Email : "Não foi possível enviar: " + err));
            });
            MenuTheme.StyledButton(accountBody, "AccLogout", "SAIR DA CONTA", c, c, new Vector2(480f, -300f), new Vector2(300f, 84f), 24, AccountDoLogout);
        }

        // ---------- Avatares locais (capacetes desenhados por UI — sem imagem externa) ----------
        int pendingAvatar = -2; // -2 = ainda não lido do perfil; -1 = sem avatar (inicial do nome)
        static readonly Color[] HelmetMain =
        {
            new Color(0.9f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.9f), new Color(1f, 0.8f, 0.1f), new Color(0.1f, 0.7f, 0.35f),
            new Color(0.95f, 0.95f, 0.95f), new Color(0.12f, 0.12f, 0.14f), new Color(1f, 0.48f, 0.1f), new Color(0.55f, 0.2f, 0.85f),
            new Color(0f, 0.75f, 0.8f), new Color(0.95f, 0.35f, 0.65f), new Color(0.6f, 0.62f, 0.66f), new Color(0.45f, 0.25f, 0.1f)
        };
        static readonly Color[] HelmetStripe =
        {
            Color.white, new Color(1f, 0.8f, 0.1f), new Color(0.1f, 0.1f, 0.12f), Color.white,
            new Color(0.9f, 0.12f, 0.12f), new Color(1f, 0.48f, 0.1f), new Color(0.1f, 0.1f, 0.12f), new Color(1f, 0.8f, 0.1f),
            Color.white, Color.white, new Color(0.1f, 0.35f, 0.9f), new Color(1f, 0.8f, 0.1f)
        };

        static int PresetOf(string photo)
        {
            if (!string.IsNullOrEmpty(photo) && photo.StartsWith("preset:") && int.TryParse(photo.Substring(7), out int n) && n >= 0 && n < HelmetMain.Length) return n;
            return -1;
        }

        static void DrawHelmet(RectTransform parent, int preset, float size)
        {
            preset = Mathf.Clamp(preset, 0, HelmetMain.Length - 1);
            var c = new Vector2(0.5f, 0.5f);
            var bg = UIFactory.Panel("HelmetBg", parent, c, c, Vector2.zero, new Vector2(size, size), new Color(0.06f, 0.07f, 0.1f, 1f));
            bg.sprite = MenuTheme.RoundedSprite(64, 64, 32);
            var shell = UIFactory.Panel("HelmetShell", bg.transform, c, c, new Vector2(0f, -size * 0.02f), new Vector2(size * 0.74f, size * 0.7f), HelmetMain[preset]);
            shell.sprite = MenuTheme.RoundedSprite(64, 60, 28);
            var stripe = UIFactory.Panel("HelmetStripe", shell.transform, c, c, new Vector2(0f, size * 0.2f), new Vector2(size * 0.14f, size * 0.3f), HelmetStripe[preset]);
            stripe.sprite = MenuTheme.RoundedSprite(8, 20, 4);
            var visor = UIFactory.Panel("HelmetVisor", shell.transform, c, c, new Vector2(size * 0.05f, -size * 0.06f), new Vector2(size * 0.58f, size * 0.17f), new Color(0.05f, 0.06f, 0.09f, 0.95f));
            visor.sprite = MenuTheme.RoundedSprite(40, 12, 6);
            var shine = UIFactory.Panel("HelmetShine", visor.transform, c, c, new Vector2(-size * 0.12f, size * 0.03f), new Vector2(size * 0.16f, size * 0.03f), new Color(1f, 1f, 1f, 0.35f));
            shine.raycastTarget = false;
            foreach (var g in new Image[] { bg, shell, stripe, visor }) g.raycastTarget = false;
        }

        void ShowAvatarPicker(int colorIdx)
        {
            var c = new Vector2(0.5f, 0.5f);
            var dim = UIFactory.Panel("AvatarPicker", accountBody, c, c, Vector2.zero, new Vector2(1300f, 780f), new Color(0.04f, 0.05f, 0.07f, 0.98f));
            dim.raycastTarget = true;
            dim.transform.SetAsLastSibling();
            var drt = (RectTransform)dim.transform;
            UIFactory.Label("PickTitle", drt, "ESCOLHA SEU CAPACETE", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1200f, 50f), MenuTheme.Text);
            for (int i = 0; i < HelmetMain.Length; i++)
            {
                int idx = i;
                float x = (i % 6 - 2.5f) * 200f;
                float y = i < 6 ? 120f : -110f;
                var btn = MenuTheme.StyledButton(drt, "Pick" + i, "", c, c, new Vector2(x, y), new Vector2(180f, 200f), 18, () =>
                {
                    pendingAvatar = idx;
                    Destroy(dim.gameObject);
                    SetAccountStatus("Avatar escolhido — toque em SALVAR PERFIL para gravar na conta.");
                    ShowAccountProfile();
                });
                if (idx == pendingAvatar) btn.image.color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.45f);
                var holder = UIFactory.Rect("H" + i, btn.transform, c, c, c, new Vector2(0f, 10f), new Vector2(150f, 150f));
                DrawHelmet(holder, i, 150f);
            }
            MenuTheme.StyledButton(drt, "PickNone", "SEM AVATAR (INICIAL)", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 30f), new Vector2(460f, 70f), 22, () =>
            {
                pendingAvatar = -1;
                Destroy(dim.gameObject);
                ShowAccountProfile();
            });
            MenuTheme.StyledButton(drt, "PickClose", "FECHAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(260f, 30f), new Vector2(460f, 70f), 22, () => Destroy(dim.gameObject));
        }

        Image AccountCellBg(float x, float y)
        {
            var bg = UIFactory.Panel("AccCell" + x + "_" + y, accountBody, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(610f, 116f), new Color(1f, 1f, 1f, 0.06f));
            bg.raycastTarget = true; // campo clicável
            return bg;
        }

        Image AccountCell(float x, float y, string label)
        {
            var bg = AccountCellBg(x, y);
            UIFactory.Label("L" + label, bg.transform, label, 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -6f), new Vector2(400f, 30f), MenuTheme.TextDim);
            return bg;
        }

        void AccountStatCell(float x, string label, string value)
        {
            var bg = UIFactory.Panel("Stat" + label, accountBody, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 110f), new Vector2(300f, 110f), new Color(1f, 1f, 1f, 0.06f));
            bg.raycastTarget = false;
            UIFactory.Label("Sl" + label, bg.transform, label, 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(270f, 34f), MenuTheme.TextDim);
            UIFactory.Label("Sv" + label, bg.transform, value, 40, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 8f), new Vector2(270f, 60f), MenuTheme.Accent);
        }

        InputField AccountFieldIn(Image cell, string value, InputField.ContentType type, string placeholder)
        {
            var ph = UIFactory.Label("AccPh" + cell.name, cell.transform, placeholder, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, -12f), new Vector2(400f, 60f), new Color(1f, 1f, 1f, 0.25f));
            ph.raycastTarget = false;
            var txt = UIFactory.Label("AccTx" + cell.name, cell.transform, value ?? "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, -12f), new Vector2(430f, 60f), MenuTheme.Text);
            txt.raycastTarget = false;
            var input = cell.gameObject.AddComponent<InputField>();
            input.targetGraphic = cell;
            input.placeholder = ph;
            input.textComponent = txt;
            input.text = value ?? "";
            input.contentType = type;
            return input;
        }

        Color AvatarColorOf(int i) { return Color.HSVToRGB((i & 7) / 8f, 0.62f, 0.95f); }

        static readonly string[] AvatarColorNames = { "VERMELHO", "AMARELO", "VERDE-LIMÃO", "VERDE", "CIANO", "AZUL", "ROXO", "ROSA" };
        static string AvatarColorName(int i) => AvatarColorNames[i & 7];

        void AccountDoPhoto()
        {
            if (accPhotoField == null || string.IsNullOrWhiteSpace(accPhotoField.text)) { SetAccountStatus("Cole antes a URL de uma imagem"); return; }
            SetAccountStatus("Baixando foto...");
            StartCoroutine(AccountPhotoRoutine(accPhotoField.text.Trim()));
        }

        IEnumerator AccountPhotoRoutine(string url)
        {
            var req = UnityWebRequestTexture.GetTexture(url);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                SetAccountStatus("Não consegui baixar essa imagem");
                yield break;
            }
            var tex = DownloadHandlerTexture.GetContent(req);
            var bytes = tex.EncodeToPNG();
            Destroy(tex);
            if (bytes == null || bytes.Length > 300000)
            {
                SetAccountStatus("Imagem grande demais (máx. ~300 KB)");
                yield break;
            }
            AccountClient.SetProfile(accNameField != null ? accNameField.text : null, Convert.ToBase64String(bytes), AccountClient.AvatarColor, (ok, err) => SetAccountStatus(ok ? "Foto salva!" : err));
        }

        void AccountDoSave(int color)
        {
            SetAccountStatus("Salvando...");
            string photo = pendingAvatar >= 0 ? "preset:" + pendingAvatar : (pendingAvatar == -1 && PresetOf(AccountClient.Photo) >= 0 ? "" : null);
            AccountClient.SetProfile(accNameField != null ? accNameField.text : null, photo, color, (ok, err) =>
            {
                if (ok)
                {
                    onlineName = AccountClient.Name;
                    SetAccountStatus("Perfil salvo!");
                }
                else SetAccountStatus(err);
            });
        }

        void AccountDoLogout()
        {
            pendingAvatar = -2;
            AccountClient.Logout();
            SetAccountStatus("");
            ShowAccountAuth();
        }

        void CreateOnlineRoom()
        {
            carSentForRoom = false;
            ApplyServerUrl();
            SetOnlineStatus("");
            OnlineClient.Connect(OnlineClient.ServerUrl);
            OnlineClient.CreateRoom(onlineName);
            ShowLobby();
        }

        void JoinOnlineRoom()
        {
            if (string.IsNullOrEmpty(onlineCode) || onlineCode.Length != 6) { SetOnlineStatus("Digite o código de 6 letras da sala"); return; }
            carSentForRoom = false;
            ApplyServerUrl();
            SetOnlineStatus("");
            OnlineClient.Connect(OnlineClient.ServerUrl);
            OnlineClient.JoinRoom(onlineCode, onlineName);
            ShowLobby();
        }

        // ---------- SALA ----------
        // Valores "ao vivo" da sala: (texto, função) — atualizados a cada 0,5 s (o host muda, todos veem).
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Text, Func<string>>> lobbyLive =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Text, Func<string>>>();
        bool lobbyBuiltAsHost;
        Button lobbyRetryBtn;
        InputField lobbyLapsField;
        string lobbyMsg;
        float lobbyMsgUntil;

        void ShowLobby()
        {
            back = LeaveLobby;
            var s = NewScreen("");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.9f);
            lobbyScreen = s;
            lobbyLive.Clear();
            bool host = OnlineClient.IsHost;
            lobbyBuiltAsHost = host;
            Color role = host ? HostColor : GuestColor;
            var c = new Vector2(0.5f, 0.5f);
            var ml = new Vector2(0f, 0.5f);

            // Cabeçalho: papel + código grande + copiar + estado da conexão.
            var head = UIFactory.Panel("LobbyHead", s, c, c, new Vector2(ContentX, 420f), new Vector2(1440f, 110f), CardColor);
            head.sprite = MenuTheme.RoundedSprite(140, 100, 16);
            var hr = (RectTransform)head.transform;
            UIFactory.Panel("RoleBar", hr, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 110f), role);
            var badge = UIFactory.Panel("RoleBadge", hr, ml, ml, new Vector2(32f, 0f), new Vector2(250f, 58f), role);
            badge.sprite = MenuTheme.RoundedSprite(140, 60, 12);
            UIFactory.Label("RoleText", badge.transform, host ? "ANFITRIÃO" : "CONVIDADO", 26, TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(250f, 58f), new Color(0.05f, 0.05f, 0.07f));
            UIFactory.Label("RoomWord", hr, "SALA", 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(310f, 0f), new Vector2(80f, 40f), MenuTheme.TextDim);
            Live(UIFactory.Label("RoomCode", hr, "", 60, TextAnchor.MiddleLeft, ml, ml, new Vector2(386f, 0f), new Vector2(340f, 80f), MenuTheme.Text),
                () => OnlineClient.RoomCode ?? "······");
            MenuTheme.StyledButton(hr, "CopyCode", "COPIAR", ml, ml, new Vector2(730f, 0f), new Vector2(170f, 60f), 24, () =>
            {
                if (string.IsNullOrEmpty(OnlineClient.RoomCode)) return;
                GUIUtility.systemCopyBuffer = OnlineClient.RoomCode;
                LobbyMessage("Código " + OnlineClient.RoomCode + " copiado — envie para os amigos");
            });
            lobbyStatus = UIFactory.Label("LobbyConn", hr, "", 22, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(500f, 80f), MenuTheme.TextDim);
            lobbyStatus.horizontalOverflow = HorizontalWrapMode.Wrap;

            // CORRIDA (mesmas opções do Single Player + REGRAS). Host edita; convidado só vê. Linhas de 68 (não 76) para as
            // 8 linhas caberem acima do aviso e dos botões.
            const float lobbyStep = 68f;
            var race = SetupCard(s, host ? "CORRIDA" : "CORRIDA  ·  DEFINIDA PELO ANFITRIÃO", ContentX - 365f, 8, 330f, lobbyStep);
            int row = 0;
            LiveRow(race, "Pista", row++, () => TrackNames[RoomTrackIndex], host ? (Action<int>)(d => PushRoom(track: ServerTrackIds[Step(RoomTrackIndex, d, ServerTrackIds.Length)])) : null, lobbyStep);
            lobbyLapsField = LapsRow(race, row++, () => OnlineClient.RoomLaps, v => PushRoom(laps: v), () => ServerTrack(OnlineClient.RoomTrackId), host, lobbyStep);
            LiveRow(race, "Carros no grid", row++, () => OnlineClient.RoomTotalCars.ToString(), host ? (Action<int>)(d => PushRoom(cars: StepIn(OnlineCarCounts, OnlineClient.RoomTotalCars, d))) : null, lobbyStep);
            LiveRow(race, "Dificuldade", row++, RoomDiffName, host ? (Action<int>)(d => PushRoom(diff: ServerDiffIds[Step(Mathf.Max(0, Array.IndexOf(ServerDiffIds, OnlineClient.RoomDifficulty)), d, ServerDiffIds.Length)])) : null, lobbyStep);
            LiveRow(race, "Categoria", row++, () => OnlineClient.RoomCategory == "f1" ? "F1" : "GT", host ? (Action<int>)(d => PushRoom(cat: OnlineClient.RoomCategory == "f1" ? "gt" : "f1")) : null, lobbyStep);
            LiveRow(race, "Pit Stop", row++, () => OnlineClient.RoomPitStops ? "LIGADO" : "DESLIGADO", host ? (Action<int>)(d => PushRoom(pit: !OnlineClient.RoomPitStops)) : null, lobbyStep);
            LiveRow(race, "Clima", row++, () => WeatherLabel(OnlineClient.RoomWeather), host ? (Action<int>)(d => PushRoom(weather: WeatherIds[Step(Mathf.Max(0, Array.IndexOf(WeatherIds, OnlineClient.RoomWeather ?? "")), d, WeatherIds.Length)])) : null, lobbyStep);
            RulesRow(race, row++, () => OnlineClient.RoomTotalLoss, v => PushRoom(totalLoss: v), () => OnlineClient.RoomPenalties, v => PushRoom(penalties: v), host, lobbyStep);

            // PILOTOS na sala.
            var pilots = SetupCard(s, "PILOTOS", ContentX + 365f, 4, 330f);
            Live((Text)pilots.Find("CardTitle").GetComponent<Text>(), () => "PILOTOS  " + OnlineClient.LobbyPlayers.Count + "/8");
            lobbyList = UIFactory.Rect("LobbyList", pilots, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -80f), new Vector2(668f, 320f)).transform;

            // MEU CARRO
            var mine = SetupCard(s, "MEU CARRO", ContentX + 365f, 2, -84f);
            CardRow(mine, "Carro", 0, () => CarLabel(settings.players[0].car), d => { settings.players[0].car = StepLobbyCar(settings.players[0].car, d); SendMyCar(); });
            CardRow(mine, "Cor", 1, () => CarPalette.Names[settings.players[0].colorIndex], d => { settings.players[0].colorIndex = Step(settings.players[0].colorIndex, d, CarPalette.Colors.Length); SendMyCar(); });

            // Ações por papel. Ação principal (INICIAR / ESTOU PRONTO) À DIREITA (pedido do usuário); SAIR à esquerda.
            var b = new Vector2(0.5f, 0f);
            if (host)
                lobbyStartBtn = MenuTheme.StyledButton(s, "LobbyStart", "INICIAR CORRIDA", b, b, new Vector2(ContentX + 250f, 40f), new Vector2(460f, 100f), 34, StartAsHost);
            else
            {
                lobbyReadyBtn = MenuTheme.StyledButton(s, "LobbyReady", "ESTOU PRONTO", b, b, new Vector2(ContentX + 250f, 40f), new Vector2(460f, 100f), 34, ToggleReady);
                Tint(lobbyReadyBtn, GuestColor);
            }
            MenuTheme.StyledButton(s, "LobbyBack", "SAIR DA SALA", b, b, new Vector2(ContentX - 250f, 40f), new Vector2(460f, 100f), 34, LeaveLobby);
            // TENTAR DE NOVO ocupa o LUGAR da ação principal quando a conexão falha (antes ficava solto no meio, por cima do
            // aviso e dos botões — "truncou com os outros").
            lobbyRetryBtn = MenuTheme.StyledButton(s, "LobbyRetry", "TENTAR DE NOVO", b, b, new Vector2(ContentX + 250f, 40f), new Vector2(460f, 100f), 34, () => { if (lobbyBuiltAsHost) CreateOnlineRoom(); else JoinOnlineRoom(); });
            lobbyRetryBtn.gameObject.SetActive(false);
            Live(UIFactory.Label("LobbyHint", s, "", 26, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX, -362f), new Vector2(1400f, 40f), WarnColor), LobbyHint);
            RefreshLobby();
        }

        static void SetButtonText(Button b, string text, int font)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<Text>();
            if (t == null) return;
            t.text = MenuTheme.Spaced(text);
            MenuTheme.FitText(t, font);
        }

        /// <summary>Linha "REGRAS": dois botões que alternam PERDA TOTAL e PENALIDADES (SIM/NÃO). Uma linha só para o
        /// cartão caber na tela. editable = false (convidado): só mostra.</summary>
        void RulesRow(RectTransform card, int row, Func<bool> pt, Action<bool> setPt, Func<bool> pen, Action<bool> setPen, bool editable, float step = 76f)
        {
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            float y = -84f - row * step;
            var line = UIFactory.Panel("Regras_Row", card, tl, tl, new Vector2(16f, y), new Vector2(668f, step - 8f), new Color(1f, 1f, 1f, row % 2 == 0 ? 0.05f : 0.025f));
            var lr = (RectTransform)line.transform;
            UIFactory.Label("Regras", lr, "REGRAS", 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(140f, 60f), MenuTheme.TextDim);
            string PtText() => "PERDA TOTAL: " + (pt() ? "SIM" : "NÃO");
            string PenText() => "PENALIDADES: " + (pen() ? "SIM" : "NÃO");
            if (!editable)
            {
                var ro = UIFactory.Label("Regras_Value", lr, "", 22, TextAnchor.MiddleCenter, ml, ml, new Vector2(156f, 0f), new Vector2(500f, 60f), new Color(0.75f, 0.78f, 0.84f));
                Live(ro, () => PtText() + "   ·   " + PenText());
                return;
            }
            Button bPt = null, bPen = null;
            bPt = MenuTheme.StyledButton(lr, "Regras_PT", PtText(), ml, ml, new Vector2(150f, 0f), new Vector2(252f, step - 16f), 20, () => { setPt(!pt()); SetButtonText(bPt, PtText(), 20); });
            bPen = MenuTheme.StyledButton(lr, "Regras_Pen", PenText(), ml, ml, new Vector2(412f, 0f), new Vector2(252f, step - 16f), 20, () => { setPen(!pen()); SetButtonText(bPen, PenText(), 20); });
        }

        Text Live(Text t, Func<string> value)
        {
            lobbyLive.Add(new System.Collections.Generic.KeyValuePair<Text, Func<string>>(t, value));
            t.text = value();
            return t;
        }

        /// <summary>Linha de configuração da sala no mesmo visual do Single Player. change == null = só leitura.</summary>
        void LiveRow(RectTransform card, string label, int row, Func<string> value, Action<int> change, float step = 76f)
        {
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            float y = -84f - row * step;
            var line = UIFactory.Panel(label + "_Row", card, tl, tl, new Vector2(16f, y), new Vector2(668f, step - 8f), new Color(1f, 1f, 1f, row % 2 == 0 ? 0.05f : 0.025f));
            var lr = (RectTransform)line.transform;
            MenuTheme.FitText(UIFactory.Label(label, lr, label.ToUpperInvariant(), 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(210f, 60f), MenuTheme.TextDim), 22);
            // MAIÚSCULAS como na configuração do Single Player (antes "Circuito Verde"/"Médio" ao lado de "SECO").
            var valueT = UIFactory.Label(label + "_Value", lr, "", 26, TextAnchor.MiddleCenter, ml, ml, new Vector2(306f, 0f), new Vector2(286f, 60f), change != null ? MenuTheme.Text : new Color(0.75f, 0.78f, 0.84f));
            MenuTheme.FitText(valueT, 26);
            Live(valueT, () => (value() ?? "").ToUpperInvariant());
            if (change == null) return;
            MenuTheme.StyledButton(lr, label + "_Prev", "<", ml, ml, new Vector2(234f, 0f), new Vector2(66f, 56f), 32, () => change(-1));
            MenuTheme.StyledButton(lr, label + "_Next", ">", ml, ml, new Vector2(596f, 0f), new Vector2(66f, 56f), 32, () => change(1));
        }

        /// <summary>Total de carros da sala (humanos + IA) — até as 24 vagas do grid, como no servidor.</summary>
        static readonly int[] OnlineCarCounts = { 2, 4, 6, 8, 10, 12, 16, 20, 24 };

        void PushRoom(string track = null, int? laps = null, int? cars = null, string diff = null, string cat = null, bool? pit = null, string weather = null, bool? totalLoss = null, bool? penalties = null)
        {
            string t = track ?? ServerTrackIds[RoomTrackIndex];
            int l = laps ?? OnlineClient.RoomLaps, n = cars ?? OnlineClient.RoomTotalCars;
            string df = diff ?? OnlineClient.RoomDifficulty, ct = cat ?? OnlineClient.RoomCategory, w = weather ?? OnlineClient.RoomWeather;
            bool p = pit ?? OnlineClient.RoomPitStops;
            bool pt = totalLoss ?? OnlineClient.RoomTotalLoss, pen = penalties ?? OnlineClient.RoomPenalties;
            OnlineClient.SendRoomSettings(t, l, n, df, ct, p, w, pt, pen);
            // Mostra na hora (o servidor confirma depois): toques seguidos não desfazem um ao outro com estado velho.
            OnlineClient.RoomTrackId = t; OnlineClient.RoomLaps = l; OnlineClient.RoomTotalCars = n;
            OnlineClient.RoomDifficulty = df; OnlineClient.RoomCategory = ct; OnlineClient.RoomPitStops = p; OnlineClient.RoomWeather = w;
            OnlineClient.RoomTotalLoss = pt; OnlineClient.RoomPenalties = pen;
            SyncMyCar();
        }


        /// <summary>Carro na sala respeita a categoria: F1 = só fórmulas; GT = só carros de rua.</summary>
        CarModel StepLobbyCar(CarModel current, int d)
        {
            bool f1 = OnlineClient.RoomCategory == "f1";
            var m = current;
            for (int i = 0; i < AllModels.Length; i++)
            {
                m = (CarModel)Step((int)m, d, AllModels.Length);
                if (IsFormula(m) == f1) return m;
            }
            return current;
        }

        void SendMyCar()
        {
            carSentForRoom = true;
            OnlineClient.UpdatePlayer((int)settings.players[0].car, settings.players[0].colorIndex);
        }

        void StartAsHost()
        {
            if (!OnlineClient.IsHost) return;
            OnlineClient.SetReady(true); // compatível com servidor antigo, que exigia o host "pronto"
            OnlineClient.StartRace();
        }

        void LobbyMessage(string msg)
        {
            lobbyMsg = msg;
            lobbyMsgUntil = Time.unscaledTime + 5f;
        }

        string LobbyHint()
        {
            if (!string.IsNullOrEmpty(lobbyMsg) && Time.unscaledTime < lobbyMsgUntil) return lobbyMsg;
            if (OnlineClient.ConnectFailed) return "Não foi possível conectar — " + (OnlineClient.LastServerMessage ?? "verifique a internet");
            if (!OnlineClient.Connected || OnlineClient.RoomCode == null) return OnlineClient.ConnectionStatus.Length > 0 ? OnlineClient.ConnectionStatus : "Entrando na sala...";
            var list = OnlineClient.LobbyPlayers;
            int guests = 0, ready = 0;
            foreach (var p in list) if (!p.isHost) { guests++; if (p.ready) ready++; }
            if (OnlineClient.IsHost)
            {
                if (guests == 0) return "Aguardando convidados — compartilhe o código " + OnlineClient.RoomCode;
                if (ready < guests) return "Aguardando convidados marcarem PRONTO (" + ready + "/" + guests + ")";
                return "Todos prontos! Pode iniciar a corrida";
            }
            var me = OnlineClient.Me;
            return me != null && me.ready ? "Você está PRONTO — aguardando o anfitrião iniciar" : "Escolha seu carro e marque ESTOU PRONTO";
        }

        /// <summary>Ids de pista no SERVIDOR — MESMA ORDEM de TrackNames/TrackLibrary.All (24 pistas).</summary>
        static readonly string[] ServerTrackIds =
        {
            "forest", "desert", "city", "velocita", "principado", "ardenne", "jardim", "golfo",
            "serra", "sol", "parque", "dragao", "corniche", "porto", "cascatas", "dourado",
            "colinas", "norte", "danubio", "dunas", "fortaleza", "texas", "estadio", "neon"
        };

        static readonly string[] ServerDiffIds = { "easy", "medium", "hard", "expert" };

        int RoomTrackIndex => Mathf.Clamp(System.Array.IndexOf(ServerTrackIds, OnlineClient.RoomTrackId), 0, TrackNames.Length - 1);

        string RoomDiffName()
        {
            int i = System.Array.IndexOf(ServerDiffIds, OnlineClient.RoomDifficulty);
            return DifficultyNames[Mathf.Clamp(i, 0, DifficultyNames.Length - 1)];
        }

        void RefreshLobby()
        {
            if (lobbyScreen == null) return;
            // Papel mudou (anfitrião saiu e o servidor passou a sala para mim): remonta a tela.
            if (OnlineClient.RoomCode != null && OnlineClient.IsHost != lobbyBuiltAsHost) { ShowLobby(); return; }
            // Categoria mudou: meu carro precisa ser da categoria da sala.
            if (OnlineClient.RoomCode != null && IsFormula(settings.players[0].car) != (OnlineClient.RoomCategory == "f1"))
            {
                settings.players[0].car = OnlineClient.RoomCategory == "f1" ? CarModel.F1 : CarModel.Titan;
                SendMyCar();
            }
            foreach (var kv in lobbyLive)
            {
                if (kv.Key == null) continue;
                kv.Key.text = kv.Value();
            }
            if (lobbyLapsField != null && !lobbyLapsField.isFocused) lobbyLapsField.text = LapsText(OnlineClient.RoomLaps, ServerTrack(OnlineClient.RoomTrackId));
            if (lobbyStatus != null)
            {
                bool ok = OnlineClient.Connected && OnlineClient.RoomCode != null;
                lobbyStatus.text = ok ? "●  CONECTADO" : OnlineClient.ConnectFailed ? "●  SEM CONEXÃO" : "●  CONECTANDO...";
                lobbyStatus.color = ok ? OkColor : OnlineClient.ConnectFailed ? BadColor : WarnColor;
            }
            // Conexão falhou: TENTAR DE NOVO no lugar da ação principal (nunca os dois juntos).
            bool failed = OnlineClient.ConnectFailed;
            if (lobbyRetryBtn != null) lobbyRetryBtn.gameObject.SetActive(failed);
            if (lobbyStartBtn != null) lobbyStartBtn.gameObject.SetActive(!failed);
            if (lobbyReadyBtn != null) lobbyReadyBtn.gameObject.SetActive(!failed);

            // Lista de pilotos.
            if (lobbyList != null)
                for (int i = lobbyList.childCount - 1; i >= 0; i--) Destroy(lobbyList.GetChild(i).gameObject);
            var list = OnlineClient.LobbyPlayers;
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            for (int i = 0; i < list.Count && lobbyList != null; i++)
            {
                var p = list[i];
                bool me = p.id == OnlineClient.MyPlayerId;
                var rowImg = UIFactory.Panel("LobbyPlayer" + i, lobbyList, tl, tl, new Vector2(0f, -i * 40f), new Vector2(668f, 36f), new Color(1f, 1f, 1f, me ? 0.09f : (i % 2 == 0 ? 0.04f : 0.02f)));
                var rr = (RectTransform)rowImg.transform;
                UIFactory.Panel("Tag", rr, ml, ml, Vector2.zero, new Vector2(6f, 36f), p.isHost ? HostColor : GuestColor);
                UIFactory.Label("Name", rr, (me ? "▸ " : "") + p.name, 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(18f, 0f), new Vector2(230f, 34f), me ? MenuTheme.Text : new Color(0.82f, 0.84f, 0.88f));
                UIFactory.Label("Car", rr, CarLabel(OnlineClient.CarModelFromServer(p.carModel)), 20, TextAnchor.MiddleLeft, ml, ml, new Vector2(252f, 0f), new Vector2(260f, 34f), MenuTheme.TextDim);
                string st = p.isHost ? "ANFITRIÃO" : p.ready ? "PRONTO" : "AGUARDANDO";
                Color sc = p.isHost ? HostColor : p.ready ? OkColor : MenuTheme.TextDim;
                UIFactory.Label("State", rr, st, 20, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(150f, 34f), sc);
            }

            var meInfo = OnlineClient.Me;
            if (lobbyReadyBtn != null)
            {
                var lbl = lobbyReadyBtn.transform.Find("LobbyReady_Label");
                if (lbl != null) lbl.GetComponent<Text>().text = meInfo != null && meInfo.ready ? "CANCELAR PRONTO" : "ESTOU PRONTO";
                lobbyReadyBtn.interactable = OnlineClient.Connected && OnlineClient.RoomCode != null;
            }
            if (lobbyStartBtn != null)
            {
                bool othersReady = list.Count >= 2 && list.TrueForAll(p => p.isHost || p.ready);
                lobbyStartBtn.interactable = OnlineClient.IsHost && OnlineClient.Connected && othersReady;
            }
        }

        void ToggleReady()
        {
            var me = OnlineClient.Me;
            OnlineClient.SetReady(me == null || !me.ready);
        }

        void LeaveLobby()
        {
            UnbindOnline();
            OnlineClient.Disconnect();
            ShowOnlineSetup();
        }

        void BindOnline()
        {
            OnlineClient.OnRoomCreated += OnOnlineRoomCreated;
            OnlineClient.OnRoomJoined += OnOnlineRoomJoined;
            OnlineClient.OnLobbyUpdate += OnOnlineLobbyUpdate;
            OnlineClient.OnError += OnOnlineError;
            OnlineClient.OnWarning += OnOnlineWarning;
            OnlineClient.OnGameStart += StartOnlineRace;
        }

        void UnbindOnline()
        {
            OnlineClient.OnRoomCreated -= OnOnlineRoomCreated;
            OnlineClient.OnRoomJoined -= OnOnlineRoomJoined;
            OnlineClient.OnLobbyUpdate -= OnOnlineLobbyUpdate;
            OnlineClient.OnError -= OnOnlineError;
            OnlineClient.OnWarning -= OnOnlineWarning;
            OnlineClient.OnGameStart -= StartOnlineRace;
        }

        void OnOnlineRoomCreated(string code)
        {
            Debug.Log("[Online] código " + code);
            LobbyMessage("Sala " + code + " criada — toque em COPIAR e envie o código");
            RefreshLobby();
        }

        void OnOnlineRoomJoined(string code)
        {
            SyncMyCar();
            RefreshLobby();
        }

        void OnOnlineLobbyUpdate()
        {
            // Antes marcava "carro enviado" SEM enviar: se esta atualização chegasse antes do room:joined, o
            // servidor ficava com o carro padrão (lista mostrava Porsche Cayman S com Temerario em MEU CARRO).
            SyncMyCar();
            RefreshLobby();
        }

        /// <summary>Carro do jogador na sala: respeita a categoria (F1 = fórmulas, GT = carros de rua — antes a
        /// sala GT aceitava o FÓRMULA C2 vindo da garagem) e é enviado ao servidor uma vez por sala e a cada troca.</summary>
        void SyncMyCar()
        {
            var pc = settings.players[0];
            bool f1 = OnlineClient.RoomCategory == "f1";
            bool fixedCar = false;
            if (IsFormula(pc.car) != f1) { pc.car = StepLobbyCar(pc.car, 1); fixedCar = true; }
            if (fixedCar || !carSentForRoom) SendMyCar();
        }

        void OnOnlineError(string message)
        {
            if (lobbyScreen != null) LobbyMessage("Erro: " + message);
            else if (onlineSetupStatus != null) onlineSetupStatus.text = "Erro: " + message;
        }

        void OnOnlineWarning(string message)
        {
            if (lobbyScreen != null) LobbyMessage(message);
        }

        void SetOnlineStatus(string msg)
        {
            if (lobbyScreen != null) LobbyMessage(msg);
            if (onlineSetupStatus != null) onlineSetupStatus.text = msg;
        }

        void StartOnlineRace(GameStartInfo info)
        {
            UnbindOnline();
            LaunchOnlineRace(info, onlineName);
        }

        /// <summary>Carrega a corrida online a partir do game:start do servidor. Usado no lobby E dentro da corrida (reinício
        /// votado / jogar novamente): todos recebem o MESMO game:start e vão juntos para a mesma corrida da mesma sala.</summary>
        public static void LaunchOnlineRace(GameStartInfo info, string fallbackName)
        {
            if (info == null) return;
            string onlineName = string.IsNullOrEmpty(fallbackName) ? "Jogador" : fallbackName;
            var st = GameManager.Settings;
            st.mode = GameMode.Online;
            st.careerPhase = -1;
            st.careerGrid = -1;
            st.sessionTimeLimit = 0f;
            RaceX.Career.CareerData.raceActive = false;
            st.track = ServerTrack(info.trackId);
            st.laps = Mathf.Clamp(info.laps, 0, MaxLaps); // 0 = voltas reais (o RaceBootstrap converte)
            st.totalCars = Mathf.Max(1, info.totalCars);
            st.difficulty = ServerDifficulty(info.difficulty);
            st.formulaOnly = info.category == "f1";
            st.pitStops = info.pitStops;
            st.weather = info.weather ?? "";
            st.totalLoss = info.totalLoss;
            st.penalties = info.penalties;
            var pid = OnlineClient.MyPlayerId;
            StartSlotInfo mine = null;
            foreach (var sp in info.startPositions)
            {
                if (sp.isAI) continue;
                if (mine == null || (pid != null && sp.id == pid)) mine = sp;
            }
            if (mine == null)
            {
                mine = new StartSlotInfo { id = pid, name = onlineName, carModel = "titan", carColor = 0 };
            }
            st.players[0].name = string.IsNullOrEmpty(mine.name) ? onlineName : mine.name;
            st.players[0].car = OnlineClient.CarModelFromServer(mine.carModel);
            st.players[0].colorIndex = Mathf.Clamp(mine.carColor, 0, CarPalette.Colors.Length - 1);
            SceneLoader.LoadRace(st);
        }

        static TrackId ServerTrack(string id) => id switch
        {
            "forest" => TrackId.ForestCircuit,
            "desert" => TrackId.DesertCanyon,
            "city" => TrackId.NightCity,
            "velocita" => TrackId.Velocita,
            "principado" => TrackId.Principado,
            "ardenne" => TrackId.Ardenne,
            "jardim" => TrackId.JardimDaBaia,
            "golfo" => TrackId.GolfoReal,
            "serra" => TrackId.SerraDoCafe,
            "sol" => TrackId.CircuitoDoSol,
            "parque" => TrackId.ParqueReal,
            "dragao" => TrackId.DragaoOriental,
            "corniche" => TrackId.CornicheDaNoite,
            "porto" => TrackId.PortoSul,
            "cascatas" => TrackId.IlhaDasCascatas,
            "dourado" => TrackId.ValeDourado,
            "colinas" => TrackId.ColinasVerdes,
            "norte" => TrackId.CampoDoNorte,
            "danubio" => TrackId.CurvasDoDanubio,
            "dunas" => TrackId.DunasDoNorte,
            "fortaleza" => TrackId.Fortaleza,
            "texas" => TrackId.ColinasDoTexas,
            "estadio" => TrackId.EstadioCentral,
            "neon" => TrackId.NeonStrip,
            _ => TrackId.ForestCircuit
        };

        static Difficulty ServerDifficulty(string d) => d switch
        {
            "easy" => Difficulty.Easy,
            "hard" => Difficulty.Hard,
            "expert" => Difficulty.Expert,
            _ => Difficulty.Medium
        };

        // Linha "Rótulo   < valor >"
        const float ContentX = 215f;

        float rowTop = 330f, rowStep = 84f;

        void OptionRow(RectTransform parent, string label, int row, Func<string> value, Action<int> change, float x = ContentX)
        {
            float y = rowTop - row * rowStep;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label(label, parent, label, 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(x - 250f, y), new Vector2(380f, 72f));
            var text = UIFactory.Label(label + "_Value", parent, value(), 32, TextAnchor.MiddleCenter, c, c, new Vector2(x + 50f, y), new Vector2(380f, 72f));
            MenuTheme.StyledButton(parent, label + "_Prev", "<", c, c, new Vector2(x - 195f, y), new Vector2(76f, 68f), 38, () => { change(-1); text.text = value(); });
            MenuTheme.StyledButton(parent, label + "_Next", ">", c, c, new Vector2(x + 295f, y), new Vector2(76f, 68f), 38, () => { change(1); text.text = value(); });
        }

        static string CarLabel(CarModel m)
        {
            switch (m)
            {
            case CarModel.Falcon: return "Corvette ZR1";
            case CarModel.Titan: return "Porsche Cayman S";
            case CarModel.Storm: return "Lamborghini Temerario";
            case CarModel.F1: return "F1 (Fórmula 1)";
            case CarModel.Ferrari: return "Ferrari Concept";
            case CarModel.R8: return "Audi R8";
            case CarModel.Mustang: return "Ford Mustang";
            case CarModel.Revuelto: return "Lamborghini Revuelto";
            case CarModel.GtrNismo: return "Nissan GT-R Nismo";
            case CarModel.McLarenP1: return "McLaren P1";
            case CarModel.Bugatti: return "Bugatti";
            case CarModel.F2008: return "Ferrari F2008";
            case CarModel.F1Studio: return "Fórmula 1 (2022)";
            default: return CarSpec.CreateDefault(m).displayName;
            }
        }

        static readonly CarModel[] AllModels = (CarModel[])System.Enum.GetValues(typeof(CarModel));

        /// <summary>Próximo carro da configuração: categoria F1 = só monopostos; GT = carros de rua
        /// (o F1 entra no GT só com "F1 em GT" ligado).</summary>
        static CarModel StepSetupCar(CarModel current, int d, bool formula)
        {
            var m = current;
            for (int i = 0; i < AllModels.Length; i++)
            {
                m = (CarModel)Step((int)m, d, AllModels.Length);
                bool ok = formula ? CarSpec.IsFormula(m) : (!CarSpec.IsFormula(m) || (m == CarModel.F1 && SettingsManager.AllowF1));
                if (ok) return m;
            }
            return current;
        }

        static int Step(int value, int delta, int count) => ((value + delta) % count + count) % count;

        static int StepIn(int[] options, int value, int delta)
        {
            int i = Array.IndexOf(options, value);
            if (i < 0) i = 0;
            return options[Step(i, delta, options.Length)];
        }

        RectTransform SetupCard(RectTransform parent, string title, float x, int rows, float top = 350f, float rowStep = 76f)
        {
            var c = new Vector2(0.5f, 0.5f);
            var card = UIFactory.Panel("Card_" + title, parent, c, new Vector2(0.5f, 1f), new Vector2(x, top), new Vector2(700f, 96f + rows * rowStep), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            card.sprite = MenuTheme.RoundedSprite(140, 100, 16);
            var rt = (RectTransform)card.transform;
            var tl = new Vector2(0f, 1f);
            UIFactory.Label("CardTitle", rt, title, 30, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -14f), new Vector2(640f, 44f), MenuTheme.Accent);
            UIFactory.Panel("CardLine", rt, tl, tl, new Vector2(28f, -62f), new Vector2(644f, 2f), MenuTheme.AccentDim);
            return rt;
        }

        void CardRow(RectTransform card, string label, int row, Func<string> value, Action<int> change, float step = 76f)
        {
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            float y = -84f - row * step;
            var line = UIFactory.Panel(label + "_Row", card, tl, tl, new Vector2(16f, y), new Vector2(668f, step - 8f), new Color(1f, 1f, 1f, row % 2 == 0 ? 0.05f : 0.025f));
            var lr = (RectTransform)line.transform;
            MenuTheme.FitText(UIFactory.Label(label, lr, label.ToUpperInvariant(), 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(210f, 60f), MenuTheme.TextDim), 22);
            // Valores em MAIÚSCULAS (antes "Circuito Verde"/"Médio" ao lado de "DESLIGADO"/"GT") e que diminuem para caber.
            var raw = value;
            value = () => (raw() ?? "").ToUpperInvariant();
            var text = UIFactory.Label(label + "_Value", lr, value(), 26, TextAnchor.MiddleCenter, ml, ml, new Vector2(306f, 0f), new Vector2(286f, 60f), MenuTheme.Text);
            MenuTheme.FitText(text, 26);
            MenuTheme.StyledButton(lr, label + "_Prev", "<", ml, ml, new Vector2(234f, 0f), new Vector2(66f, 56f), 32, () => { change(-1); text.text = value(); });
            MenuTheme.StyledButton(lr, label + "_Next", ">", ml, ml, new Vector2(596f, 0f), new Vector2(66f, 56f), 32, () => { change(1); text.text = value(); });
        }

        /// <summary>Climas escolhíveis fora da carreira ("" = seco) — ids do WeatherSystem.Forced e do servidor.</summary>
        static readonly string[] WeatherIds = { "", "cloudy", "light", "heavy", "mixed" };

        static string WeatherLabel(string id) => id switch
        {
            "cloudy" => "NUBLADO",
            "light" => "CHUVA FRACA",
            "heavy" => "CHUVA FORTE",
            "mixed" => "VARIÁVEL",
            _ => "SECO"
        };

        /// <summary>Limite de voltas digitadas (mesmo do servidor online).</summary>
        const int MaxLaps = 200;

        static string LapsText(int laps, TrackId track) => laps <= 0 ? "REAIS (" + TrackLibrary.RealLaps(track) + ")" : laps.ToString();

        /// <summary>Próxima opção pronta de voltas; funciona também a partir de um número digitado.</summary>
        static int StepLaps(int value, int d)
        {
            var o = RaceSettings.LapOptions;
            int i = Array.IndexOf(o, value);
            if (i >= 0) return o[Step(i, d, o.Length)];
            if (d > 0) { foreach (int x in o) if (x > value) return x; return o[0]; }
            for (int k = o.Length - 1; k >= 0; k--) if (o[k] < value) return o[k];
            return o[o.Length - 1];
        }

        /// <summary>Linha VOLTAS: &lt; &gt; percorre as opções prontas e o campo aceita DIGITAR (1 a 200).
        /// 0 = voltas reais da pista. editable == false: só mostra (convidado na sala online).</summary>
        InputField LapsRow(RectTransform card, int row, Func<int> get, Action<int> set, Func<TrackId> track, bool editable, float step = 76f)
        {
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            float y = -84f - row * step;
            var line = UIFactory.Panel("Voltas_Row", card, tl, tl, new Vector2(16f, y), new Vector2(668f, step - 8f), new Color(1f, 1f, 1f, row % 2 == 0 ? 0.05f : 0.025f));
            var lr = (RectTransform)line.transform;
            UIFactory.Label("Voltas", lr, "VOLTAS", 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(210f, 60f), MenuTheme.TextDim);
            if (!editable)
            {
                Live(UIFactory.Label("Voltas_Value", lr, "", 26, TextAnchor.MiddleCenter, ml, ml, new Vector2(306f, 0f), new Vector2(286f, 60f), new Color(0.75f, 0.78f, 0.84f)), () => LapsText(get(), track()));
                return null;
            }
            var field = MakeField(lr, "Voltas_Field", ml, new Vector2(449f, 0f), new Vector2(286f, 56f), LapsText(get(), track()), "digite", 26);
            field.characterLimit = 12;
            field.onEndEdit.AddListener(v =>
            {
                int n = 0; bool any = false;
                foreach (char ch in v) if (ch >= '0' && ch <= '9') { n = Mathf.Min(n * 10 + (ch - '0'), 9999); any = true; }
                if (any && n >= 1) set(Mathf.Clamp(n, 1, MaxLaps));
                field.text = LapsText(get(), track());
            });
            MenuTheme.StyledButton(lr, "Voltas_Prev", "<", ml, ml, new Vector2(234f, 0f), new Vector2(66f, 56f), 32, () => { set(StepLaps(get(), -1)); field.text = LapsText(get(), track()); });
            MenuTheme.StyledButton(lr, "Voltas_Next", ">", ml, ml, new Vector2(596f, 0f), new Vector2(66f, 56f), 32, () => { set(StepLaps(get(), 1)); field.text = LapsText(get(), track()); });
            return field;
        }

        internal void ShowRaceSetup()
        {
            back = ShowPlay;
            if (settings.careerPhase >= 0 || settings.players == null || settings.players.Length < 2)
            {
                var mode = settings.mode;
                settings = new RaceSettings { mode = mode };
                settings.players[0].startCamera = (CameraView)SettingsManager.LastCamera;
                GameManager.Instance.settings = settings;
            }
            settings.careerPhase = -1;
            settings.careerGrid = -1;
            settings.sessionTimeLimit = 0f;
            if (settings.laps >= 900) settings.laps = 3;
            if (settings.totalCars > 16) settings.totalCars = RaceSettings.ClassicCarCount;
            RaceX.Career.CareerData.raceActive = false;
            bool split = settings.mode == GameMode.LocalSplitScreen;
            // Carro escolhido na GARAGEM tem que bater com a categoria (antes: categoria F1 com McLaren P1).
            for (int pi = 0; pi < settings.players.Length; pi++)
            {
                var pc = settings.players[pi];
                if (settings.formulaOnly && !IsFormula(pc.car)) pc.car = CarModel.F1;
                else if (!settings.formulaOnly && IsFormula(pc.car) && !(pc.car == CarModel.F1 && SettingsManager.AllowF1)) settings.formulaOnly = true;
            }
            // Câmera inclinada para CIMA (pitch -13): o carro desce para a faixa livre entre os cartões e os
            // botões VOLTAR/INICIAR (y ≈ 365–465 de 540). Com pitch 0 (v80) ficava em y ≈ 305, atrás dos cartões.
            SetShowroomCam(split ? new Vector3(0f, 4.0f, -12.5f) : new Vector3(0f, 3.8f, -10.5f), -13f);
            var s = NewScreen(split ? "CORRIDA — TELA DIVIDIDA" : "CONFIGURAÇÕES DA CORRIDA");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.82f);
            int players = split ? 2 : 1;
            // Cartão CORRIDA com linhas de 68 (não 76): a linha REGRAS cabe sem o cartão cobrir o carro da vitrine.
            const float raceStep = 68f;
            var raceCard = SetupCard(s, "CORRIDA", ContentX - 365f, 7, 350f, raceStep);
            var carCard = SetupCard(s, split ? "PILOTOS E CARROS" : "PILOTO E CARRO", ContentX + 365f, split ? 6 : 5);
            int row = 0;
            InputField spLaps = null;
            CardRow(raceCard, "Pista", row++, () => TrackNames[(int)settings.track], d =>
            {
                settings.track = (TrackId)Step((int)settings.track, d, TrackNames.Length);
                if (spLaps != null) spLaps.text = LapsText(settings.laps, settings.track); // "REAIS (N)" muda com a pista
            }, raceStep);
            spLaps = LapsRow(raceCard, row++, () => settings.laps, v => settings.laps = v, () => settings.track, true, raceStep);
            CardRow(raceCard, "Carros no grid", row++, () => settings.totalCars.ToString(),
                d => settings.totalCars = StepIn(RaceSettings.CarCountOptions, settings.totalCars, d), raceStep);
            CardRow(raceCard, "Dificuldade", row++, () => DifficultyNames[(int)settings.difficulty], d => settings.difficulty = (Difficulty)Step((int)settings.difficulty, d, 4), raceStep);
            CardRow(raceCard, "Pit Stop", row++, () => settings.pitStops ? "LIGADO" : "DESLIGADO", d => settings.pitStops = !settings.pitStops, raceStep);
            CardRow(raceCard, "Clima", row++, () => WeatherLabel(settings.weather), d => settings.weather = WeatherIds[Step(Mathf.Max(0, Array.IndexOf(WeatherIds, settings.weather ?? "")), d, WeatherIds.Length)], raceStep);
            RulesRow(raceCard, row++, () => settings.totalLoss, v => settings.totalLoss = v, () => settings.penalties, v => settings.penalties = v, true, raceStep);
            row = 0;
            CardRow(carCard, "Categoria", row++, () => settings.formulaOnly ? "F1" : "GT",
                d =>
                {
                    settings.formulaOnly = !settings.formulaOnly;
                    if (settings.formulaOnly) { settings.players[0].car = CarModel.F1; settings.players[1].car = CarModel.F1; }
                    else { settings.players[0].car = CarModel.Titan; settings.players[1].car = CarModel.Titan; }
                    RefreshShowroomCarAsync();
                    ShowRaceSetup();
                });

            if (!split) CardRow(carCard, "Câmera", row++, () => CameraNames[(int)settings.players[0].startCamera], d => settings.players[0].startCamera = (CameraView)Step((int)settings.players[0].startCamera, d, 4));
            CardRow(carCard, "Rádio da equipe", row++, () => SettingsManager.PitRadio == 0 ? "DESLIGADO" : "LIGADO", d => SettingsManager.PitRadio = SettingsManager.PitRadio == 0 ? 1 : 0);

            for (int p = 0; p < players; p++)
            {
                var ps = settings.players[p];
                string tag = split ? $"J{p + 1} " : "";
                CardRow(carCard, tag + "Carro", row++, () => CarLabel(ps.car), d =>
                {
                    ps.car = StepSetupCar(ps.car, d, settings.formulaOnly);
                    RefreshShowroomCarAsync();
                });
                CardRow(carCard, tag + "Cor", row++, () => CarPalette.Names[ps.colorIndex], d =>
                {
                    ps.colorIndex = Step(ps.colorIndex, d, CarPalette.Colors.Length);
                    if (split && settings.players[0].colorIndex == settings.players[1].colorIndex) ps.colorIndex = Step(ps.colorIndex, d, CarPalette.Colors.Length);
                    RefreshShowroomCarAsync();
                });
            }

            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "Back", "VOLTAR", b, new Vector2(1f, 0f), new Vector2(-30f, 40f), new Vector2(380f, 100f), 36, ShowPlay);
            MenuTheme.StyledButton(s, "Start", "INICIAR CORRIDA", b, new Vector2(0f, 0f), new Vector2(30f, 40f), new Vector2(480f, 100f), 36, () =>
            {
                settings.careerPhase = -1;
                settings.careerGrid = -1;
                settings.sessionTimeLimit = 0f;
                RaceX.Career.CareerData.raceActive = false;
                if (!split) SettingsManager.LastCamera = (int)settings.players[0].startCamera;
                SettingsManager.Save();
                SceneLoader.LoadRace(settings);
            });
        }

        // ======================= CONFIGURAÇÕES (v79: 2 cartões) =======================
        internal void ShowSettings()
        {
            back = () => { SettingsManager.Save(); ShowMain(); };
            var s = NewScreen("CONFIGURAÇÕES");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.86f);
            bool mobile = Application.isMobilePlatform;
            var audio = SetupCard(s, "ÁUDIO E JOGO", ContentX - 365f, 8);
            var video = SetupCard(s, "VÍDEO E DESEMPENHO", ContentX + 365f, mobile ? 4 : 5);
            int row = 0;
            CardRow(audio, "Volume geral", row++, () => Pct(SettingsManager.MasterVolume), d => { SettingsManager.MasterVolume = Clamp10(SettingsManager.MasterVolume, d); AudioListener.volume = SettingsManager.MasterVolume; });
            CardRow(audio, "Volume motor", row++, () => Pct(SettingsManager.EngineVolume), d => SettingsManager.EngineVolume = Clamp10(SettingsManager.EngineVolume, d));
            CardRow(audio, "Música", row++, () => SettingsManager.MusicOn ? "LIGADA" : "DESLIGADA", d => SettingsManager.MusicOn = !SettingsManager.MusicOn);
            CardRow(audio, "Efeitos", row++, () => SettingsManager.SfxOn ? "LIGADOS" : "DESLIGADOS", d => SettingsManager.SfxOn = !SettingsManager.SfxOn);
            CardRow(audio, "Rádio da equipe", row++, () => SettingsManager.PitRadio == 0 ? "DESLIGADO" : "LIGADO", d => SettingsManager.PitRadio = SettingsManager.PitRadio == 0 ? 1 : 0);
            CardRow(audio, "Nomes nos carros", row++, () => SettingsManager.NameTags ? "LIGADOS" : "DESLIGADOS", d => SettingsManager.NameTags = !SettingsManager.NameTags);
            CardRow(audio, "Mini-mapa", row++, () => SettingsManager.MinimapDynamic ? "DINÂMICO" : "PISTA INTEIRA", d => SettingsManager.MinimapDynamic = !SettingsManager.MinimapDynamic);
            if (mobile)
                CardRow(audio, "Controles", row++, () => ControlNames[(int)SettingsManager.ControlMode], d => SettingsManager.ControlMode = SettingsManager.ControlMode == ControlMode.Buttons ? ControlMode.Joystick : ControlMode.Buttons);
            else
                CardRow(audio, "F1 em GT", row++, () => SettingsManager.AllowF1 ? "SIM" : "NÃO", d => SettingsManager.AllowF1 = !SettingsManager.AllowF1);

            row = 0;
            CardRow(video, "Qualidade", row++, () => QualityNames[(int)SettingsManager.Quality].ToUpperInvariant(), d => { SettingsManager.Quality = (QualityTier)Step((int)SettingsManager.Quality, d, 4); SettingsManager.ApplyQuality(SettingsManager.Quality); });
            CardRow(video, "Limite de FPS", row++, () => SettingsManager.FpsLabel(SettingsManager.TargetFps), d =>
            {
                SettingsManager.TargetFps = StepIn(SettingsManager.FpsOptions, SettingsManager.TargetFps, d);
                SettingsManager.ApplyFrameRate(SettingsManager.Quality);
            });
            CardRow(video, "Contador FPS", row++, () => SettingsManager.ShowFps ? "LIGADO" : "DESLIGADO", d => SettingsManager.ShowFps = !SettingsManager.ShowFps);
            if (mobile)
                CardRow(video, "F1 em GT", row++, () => SettingsManager.AllowF1 ? "SIM" : "NÃO", d => SettingsManager.AllowF1 = !SettingsManager.AllowF1);
            else
            {
                CardRow(video, "Tela cheia", row++, () => Screen.fullScreen ? "SIM" : "NÃO", d => Screen.fullScreen = !Screen.fullScreen);
                CardRow(video, "Resolução", row++, () => Screen.width + " × " + Screen.height, d => { });
            }
            // Explicação do FPS/qualidade (pedido: deixar claro o seletor 30/60/120).
            var c = new Vector2(0.5f, 0.5f);
            float hintY = 350f - (96f + (mobile ? 4 : 5) * 76f) - 30f;
            var hint = UIFactory.Label("FpsHint", s,
                "AUTO: celular 60 FPS (Alta/Ultra) ou 30 FPS (Baixa/Média).\n30 economiza bateria · 60 recomendado · 120 só em tela 120 Hz.\nQualidade mais baixa = mais FPS.",
                20, TextAnchor.UpperLeft, c, new Vector2(0.5f, 1f), new Vector2(ContentX + 365f, hintY), new Vector2(680f, 100f), MenuTheme.TextDim);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuTheme.StyledButton(s, "About", "SOBRE O JOGO", c, c, new Vector2(ContentX + 365f, hintY - 150f), new Vector2(420f, 64f), 24, ShowCredits);

            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "Back", "VOLTAR", b, b, new Vector2(ContentX, 40f), new Vector2(380f, 90f), 32, () => { SettingsManager.Save(); ShowMain(); });
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static float Clamp10(float v, int d) => Mathf.Clamp01(Mathf.Round((v + d * 0.1f) * 10f) / 10f);

        // ======================= GARAGEM (v79) =======================
        string garageStatus = "";

        internal void ShowGarage()
        {
            back = ShowMain;
            garageIndex = (int)settings.players[0].car;
            var s = NewScreen("GARAGEM");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.18f);
            // Carro em DESTAQUE: câmera mais perto e deslocada → o carro fica à esquerda, livre dos cartões.
            SetShowroomCam(new Vector3(1.5f, 1.9f, -6.6f), 8f);
            var current = (CarModel)garageIndex;
            var c = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);

            // Faixa de seleção (2 linhas de chips no topo).
            for (int i = 0; i < AllModels.Length; i++)
            {
                CarModel cm = AllModels[i];
                bool sel = (int)cm == garageIndex;
                // 5 por linha, só na faixa ENTRE o painel lateral e o cartão de dados (antes o cartão cobria F2008/F1 2022).
                float cx = -120f + (i % 5 - 2) * 164f;
                float cy = -132f - (i / 5) * 52f;
                var chip = MenuTheme.StyledButton(s, "GarageCar" + i, ShortCarLabel(cm), top, top, new Vector2(cx, cy), new Vector2(158f, 46f), 18,
                    () => { garageIndex = (int)cm; settings.players[0].car = cm; garageStatus = ""; RefreshShowroomCarAsync(); ShowGarage(); });
                if (sel) chip.image.color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.55f);
            }

            // Nome do carro sob ele, com < > para trocar.
            var bc = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "GaragePrev", "<", bc, bc, new Vector2(-470f, 150f), new Vector2(70f, 64f), 34, () => ChangeGarage(-1));
            // Nome longo ("TATUUS F4-T421 (F4 BRITÂNICO)") diminui para caber entre as setas (antes passava por cima).
            MenuTheme.FitText(UIFactory.Label("GarageName", s, CarLabel(current).ToUpperInvariant(), 40, TextAnchor.MiddleCenter, bc, bc, new Vector2(-190f, 150f), new Vector2(470f, 64f), MenuTheme.Accent), 40);
            MenuTheme.StyledButton(s, "GarageNext", ">", bc, bc, new Vector2(90f, 150f), new Vector2(70f, 64f), 34, () => ChangeGarage(1));
            UIFactory.Label("GarageCat", s, IsFormula(current) ? "FÓRMULA" : "GT / SUPERCARRO", 22, TextAnchor.MiddleCenter, bc, bc, new Vector2(-190f, 110f), new Vector2(480f, 30f), MenuTheme.TextDim);

            // CARTÃO DE DADOS à direita (fora do carro).
            var card = UIFactory.Panel("GarageCard", s, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, -80f), new Vector2(640f, 800f), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            card.sprite = MenuTheme.RoundedSprite(128, 160, 16);
            var crt = (RectTransform)card.transform;
            var tl = new Vector2(0f, 1f);
            UIFactory.Label("CardCredits", crt, "CRÉDITOS: " + CareerStats.Credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " CR", 26, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -18f), new Vector2(590f, 40f), MenuTheme.Text);
            UIFactory.Label("PerfTitle", crt, "DESEMPENHO", 24, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -66f), new Vector2(400f, 34f), MenuTheme.Accent);
            UIFactory.Panel("PerfLine", crt, tl, tl, new Vector2(28f, -100f), new Vector2(584f, 2f), MenuTheme.AccentDim);

            var baseSpec = CarSpec.CreateDefault(current);
            var upSpec = CarSpec.CreateDefault(current);
            CareerStats.ApplyUpgrades(upSpec);
            float maxTop = 1f, maxTq = 1f, maxBr = 1f, maxGrip = 1f, maxDf = 0.01f, maxMass = 1f;
            foreach (var m in AllModels)
            {
                var sp = CarSpec.CreateDefault(m);
                maxTop = Mathf.Max(maxTop, sp.topSpeedKmh); maxTq = Mathf.Max(maxTq, sp.maxTorqueNm); maxBr = Mathf.Max(maxBr, sp.brakeTorque);
                maxGrip = Mathf.Max(maxGrip, sp.sidewaysStiffness); maxDf = Mathf.Max(maxDf, sp.downforceCoefficient); maxMass = Mathf.Max(maxMass, sp.massKg);
            }
            maxTop *= 1.2f; maxTq *= 1.3f; maxBr *= 1.35f; maxGrip *= 1.25f; maxDf *= 1.25f;
            GarageStat(crt, 0, "Vel. máxima", baseSpec.topSpeedKmh / maxTop, upSpec.topSpeedKmh / maxTop, Mathf.RoundToInt(upSpec.topSpeedKmh) + " km/h");
            GarageStat(crt, 1, "Torque", baseSpec.maxTorqueNm / maxTq, upSpec.maxTorqueNm / maxTq, Mathf.RoundToInt(upSpec.maxTorqueNm) + " Nm");
            GarageStat(crt, 2, "Freios", baseSpec.brakeTorque / maxBr, upSpec.brakeTorque / maxBr, Mathf.RoundToInt(upSpec.brakeTorque) + " Nm");
            GarageStat(crt, 3, "Aderência", baseSpec.sidewaysStiffness / maxGrip, upSpec.sidewaysStiffness / maxGrip, upSpec.sidewaysStiffness.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            GarageStat(crt, 4, "Downforce", baseSpec.downforceCoefficient / maxDf, upSpec.downforceCoefficient / maxDf, upSpec.downforceCoefficient.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            // Peso: barra "cheia" = mais leve.
            GarageStat(crt, 5, "Peso", 1f - baseSpec.massKg / (maxMass * 1.15f), 1f - upSpec.massKg / (maxMass * 1.15f), Mathf.RoundToInt(upSpec.massKg) + " kg");

            UIFactory.Label("UpTitle", crt, "MELHORIAS (NÍVEL ATÉ " + CareerStats.MaxUpgrade + ")", 24, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -404f), new Vector2(590f, 34f), MenuTheme.Accent);
            UIFactory.Panel("UpLine", crt, tl, tl, new Vector2(28f, -438f), new Vector2(584f, 2f), MenuTheme.AccentDim);
            for (int k = 0; k < CareerStats.KindCount; k++)
            {
                int kind = k;
                int lvl = CareerStats.UpgradeLevel(current, kind);
                float y = -470f - k * 58f;
                UIFactory.Label("UpN" + k, crt, CareerStats.KindNames[k].ToUpperInvariant(), 20, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, y), new Vector2(200f, 26f), MenuTheme.Text);
                UIFactory.Label("UpE" + k, crt, CareerStats.KindEffects[k], 15, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, y - 22f), new Vector2(200f, 20f), MenuTheme.TextDim);
                for (int p = 0; p < CareerStats.MaxUpgrade; p++)
                {
                    var pip = UIFactory.Panel("Pip" + k + "_" + p, crt, tl, tl, new Vector2(232f + p * 17f, y - 6f), new Vector2(13f, 22f), p < lvl ? (p < 5 ? MenuTheme.Accent : new Color(1f, 0.78f, 0.3f)) : new Color(1f, 1f, 1f, 0.12f));
                    pip.raycastTarget = false;
                }
                if (lvl >= CareerStats.MaxUpgrade)
                    UIFactory.Label("UpMax" + k, crt, "MÁXIMO", 20, TextAnchor.MiddleCenter, tl, tl, new Vector2(412f, y - 2f), new Vector2(200f, 44f), new Color(0.35f, 0.9f, 0.45f));
                else
                {
                    int cost = CareerStats.UpgradeCost(lvl);
                    bool can = CareerStats.Credits >= cost;
                    var btn = MenuTheme.StyledButton(crt, "UpB" + k, cost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " cr", tl, tl, new Vector2(412f, y + 4f), new Vector2(200f, 46f), 20, () =>
                    {
                        garageStatus = CareerStats.BuyUpgrade(current, kind)
                            ? CareerStats.KindNames[kind] + " melhorado!"
                            : "Créditos insuficientes — ganhe mais em PROGRESSO > como ganhar.";
                        ShowGarage();
                    });
                    if (!can) btn.image.color = new Color(1f, 1f, 1f, 0.03f);
                }
            }
            // Mensagem da compra ao lado do título MELHORIAS (antes ficava por cima da linha REDUÇÃO DE PESO).
            MenuTheme.FitText(UIFactory.Label("GarageStatus", crt, garageStatus, 20, TextAnchor.MiddleRight, tl, tl, new Vector2(330f, -404f), new Vector2(290f, 34f), MenuTheme.Accent), 20);
            UIFactory.Label("GarageNote", crt, "Barra clara = de fábrica · laranja = com suas melhorias", 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(600f, 26f), MenuTheme.TextDim);
            MenuTheme.StyledButton(s, "GarageBack", "VOLTAR", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140f, -52f), new Vector2(260f, 60f), 28, ShowMain);
        }

        /// <summary>Linha de atributo: barra de fábrica (clara) + barra com melhorias (laranja) + valor real.</summary>
        void GarageStat(RectTransform card, int row, string label, float baseT, float upT, string value)
        {
            var tl = new Vector2(0f, 1f);
            float y = -130f - row * 44f;
            UIFactory.Label("StL" + row, card, label.ToUpperInvariant(), 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, y), new Vector2(170f, 30f), MenuTheme.TextDim);
            var bg = UIFactory.Panel("StBg" + row, card, tl, tl, new Vector2(200f, y - 8f), new Vector2(260f, 14f), new Color(1f, 1f, 1f, 0.08f));
            bg.raycastTarget = false;
            var up = UIFactory.Panel("StUp" + row, bg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(260f * Mathf.Clamp01(upT), 14f), MenuTheme.Accent);
            up.raycastTarget = false;
            var bs = UIFactory.Panel("StBase" + row, bg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(260f * Mathf.Clamp01(Mathf.Min(baseT, upT)), 14f), new Color(1f, 1f, 1f, 0.55f));
            bs.raycastTarget = false;
            UIFactory.Label("StV" + row, card, value, 20, TextAnchor.MiddleRight, tl, tl, new Vector2(470f, y), new Vector2(142f, 30f), MenuTheme.Text);
        }

        static bool IsFormula(CarModel m) => CarSpec.IsFormula(m);

        static string ShortCarLabel(CarModel m)
        {
            switch (m)
            {
            case CarModel.Falcon: return "CORVETTE";
            case CarModel.Titan: return "CAYMAN S";
            case CarModel.Storm: return "TEMERARIO";
            case CarModel.F1: return "F1";
            case CarModel.Ferrari: return "FERRARI";
            case CarModel.R8: return "AUDI R8";
            case CarModel.Mustang: return "MUSTANG";
            case CarModel.Revuelto: return "REVUELTO";
            case CarModel.GtrNismo: return "GT-R";
            case CarModel.McLarenP1: return "MCLAREN P1";
            case CarModel.Bugatti: return "BUGATTI";
            case CarModel.F2008: return "F2008";
            case CarModel.F1Studio: return "F1 2022";
            case CarModel.F1Legacy: return "F1 2022 PINTURA";
            case CarModel.F1Basic: return "F1 CLÁSSICO";
            case CarModel.F1Concept26: return "CONCEPT 2026";
            case CarModel.F1Custom22: return "F1 CUSTOM";
            case CarModel.McLarenF1: return "MCLAREN F1";
            case CarModel.FerrariF60: return "FERRARI F60";
            case CarModel.F1Sponsor: return "F1 PATROCÍNIOS";
            case CarModel.Lotus77: return "LOTUS 77";
            case CarModel.FP04: return "FP04";
            case CarModel.F4British: return "F4 BRITÂNICO";
            case CarModel.McLarenMP45: return "MCLAREN MP4/5";
            case CarModel.F1Photo22: return "F1 2022 B";
            case CarModel.F1CarConcept: return "F1 CONCEPT";
            case CarModel.F1Y2026: return "F1 2026";
            case CarModel.FormulaC1: return "FÓRMULA C1";
            case CarModel.FormulaC2: return "FÓRMULA C2";
            default: return m.ToString().ToUpperInvariant();
            }
        }

        void ChangeGarage(int delta)
        {
            garageIndex = Step(garageIndex, delta, AllModels.Length);
            settings.players[0].car = (CarModel)garageIndex;
            garageStatus = "";
            RefreshShowroomCarAsync();
            ShowGarage();
        }

        // ======================= PROGRESSO (v79) =======================
        void ShowProgress()
        {
            back = ShowMain;
            var s = NewScreen("PROGRESSO");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.88f);
            var stats = SetupCard(s, "SUA CARREIRA", ContentX - 365f, 7);
            var tl = new Vector2(0f, 1f);
            var ml = new Vector2(0f, 0.5f);
            int upPts = CareerStats.TotalUpgradePoints();
            int upMax = AllModels.Length * CareerStats.KindCount * CareerStats.MaxUpgrade;
                        string[] names = { "Corridas", "Vitórias", "Pódios", "Títulos", "Créditos", "Melhorias compradas", "Próxima meta" };
            string[] vals =
            {
                CareerStats.Races.ToString(), CareerStats.Wins.ToString(), CareerStats.Podiums.ToString(), CareerStats.Titles.ToString(),
                CareerStats.Credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " cr", upPts + " / " + upMax,
                NextGoal()
            };
            float[] fills =
            {
                Mathf.Clamp01(CareerStats.Races / 50f), Mathf.Clamp01(CareerStats.Wins / 25f), Mathf.Clamp01(CareerStats.Podiums / 40f),
                Mathf.Clamp01(CareerStats.Titles / 5f), Mathf.Clamp01(CareerStats.Credits / 60000f), upMax > 0 ? upPts / (float)upMax : 0f, -1f
            };
            for (int i = 0; i < names.Length; i++)
            {
                float y = -84f - i * 76f;
                var line = UIFactory.Panel("PRow" + i, stats, tl, tl, new Vector2(16f, y), new Vector2(668f, 68f), new Color(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.025f));
                var lr = (RectTransform)line.transform;
                UIFactory.Label("PL" + i, lr, names[i].ToUpperInvariant(), 21, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(300f, 60f), MenuTheme.TextDim);
                UIFactory.Label("PV" + i, lr, vals[i], 24, TextAnchor.MiddleRight, ml, ml, new Vector2(400f, 0f), new Vector2(250f, 60f), MenuTheme.Text);
                if (fills[i] >= 0f)
                {
                    var bg = UIFactory.Panel("PB" + i, lr, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 6f), new Vector2(636f, 4f), new Color(1f, 1f, 1f, 0.08f));
                    UIFactory.Panel("PF" + i, bg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(636f * fills[i], 4f), MenuTheme.Accent);
                }
            }

            // COMO GANHAR CRÉDITOS (pedido: explicar).
            var how = SetupCard(s, "COMO GANHAR CRÉDITOS", ContentX + 365f, 8);
            var exp = UIFactory.Label("HowText", how,
                "Você ganha créditos ao TERMINAR A CORRIDA DE DOMINGO do MODO CARREIRA. Quanto melhor a posição, mais créditos " +
                "(mínimo de " + CareerStats.RewardFor(24, 24).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " CR). Treinos, classificação e sprint não pagam.\n\nGaste na GARAGEM: motor, freios, pneus, aerodinâmica e peso, " +
                "até o nível " + CareerStats.MaxUpgrade + " em cada carro. A melhoria vale nas suas corridas na hora.",
                21, TextAnchor.UpperLeft, tl, tl, new Vector2(28f, -80f), new Vector2(640f, 220f), MenuTheme.Text);
            exp.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Label("HowTab", how, "PRÊMIO POR POSIÇÃO (GRID DE 24 CARROS)", 20, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -316f), new Vector2(640f, 30f), MenuTheme.Accent);
            int[] positions = { 1, 2, 3, 5, 10, 15, 20, 24 };
            for (int i = 0; i < positions.Length; i++)
            {
                int col = i % 2, r = i / 2;
                string txt = positions[i] + "º  →  " + CareerStats.RewardFor(positions[i], 24).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " cr";
                UIFactory.Label("Rw" + i, how, txt, 22, TextAnchor.MiddleLeft, tl, tl, new Vector2(40f + col * 320f, -360f - r * 40f), new Vector2(300f, 36f), r == 0 && col == 0 ? Gold : MenuTheme.Text);
            }
            UIFactory.Label("HowCost", how, "Custo da melhoria: " + CareerStats.UpgradeCost(0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " cr no nível 1, +" + (CareerStats.UpgradeCost(1) - CareerStats.UpgradeCost(0)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + " cr a cada nível.", 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(28f, -530f), new Vector2(640f, 30f), MenuTheme.TextDim);
            MenuTheme.StyledButton(how, "GoGarage", "IR PARA A GARAGEM", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(420f, 60f), 22, ShowGarage);

            MenuTheme.StyledButton(s, "ProgressBack", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(ContentX, 30f), new Vector2(380f, 80f), 30, ShowMain);
        }

        static readonly Color Gold = new Color32(255, 193, 7, 255);

        static string NextGoal()
        {
            int[] goals = { 1, 5, 10, 25, 50, 100 };
            foreach (int g in goals) if (CareerStats.Wins < g) return g + (g == 1 ? " vitória" : " vitórias");
            return "lenda!";
        }

        // ======================= RECORDES (v79) =======================
        static int recordsTrack;
        static int recordsPage;

        void ShowRecords()
        {
            back = ShowMain;
            var s = NewScreen("RECORDES");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.9f);
            recordsTrack = Mathf.Clamp(recordsTrack, 0, TrackNames.Length - 1);
            var c = new Vector2(0.5f, 0.5f);
            var tl = new Vector2(0f, 1f);
            // Lista de pistas (2 colunas × 12) com o melhor tempo de qualquer carro.
            var list = UIFactory.Panel("TrackList", s, c, new Vector2(0.5f, 1f), new Vector2(ContentX - 380f, 380f), new Vector2(720f, 800f), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            list.sprite = MenuTheme.RoundedSprite(144, 160, 16);
            var lrt = (RectTransform)list.transform;
            UIFactory.Label("ListTitle", lrt, "PISTAS — SEU MELHOR TEMPO", 24, TextAnchor.MiddleLeft, tl, tl, new Vector2(24f, -16f), new Vector2(660f, 36f), MenuTheme.Accent);
            for (int t = 0; t < TrackNames.Length; t++)
            {
                int tid = t;
                float best = BestOnTrack(t, out _);
                int col = t / 12, r = t % 12;
                string label = TrackNames[t] + "   " + (best > 0f ? HUD.FormatTime(best) : "--:--");
                var btn = MenuTheme.StyledButton(lrt, "Trk" + t, label, tl, tl, new Vector2(16f + col * 348f, -64f - r * 60f), new Vector2(338f, 54f), 21, () => { recordsTrack = tid; ShowRecords(); });
                btn.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
                if (t == recordsTrack) btn.image.color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.5f);
            }

            // Cartão da pista escolhida: tempo por carro.
            var card = UIFactory.Panel("RecCard", s, c, new Vector2(0.5f, 1f), new Vector2(ContentX + 380f, 380f), new Vector2(700f, 800f), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            card.sprite = MenuTheme.RoundedSprite(140, 160, 16);
            var crt = (RectTransform)card.transform;
            MenuTheme.StyledButton(crt, "RecPrev", "<", tl, tl, new Vector2(16f, -14f), new Vector2(64f, 52f), 30, () => { recordsTrack = (recordsTrack + TrackNames.Length - 1) % TrackNames.Length; ShowRecords(); });
            UIFactory.Label("RecTitle", crt, TrackNames[recordsTrack].ToUpperInvariant(), 28, TextAnchor.MiddleCenter, tl, tl, new Vector2(90f, -14f), new Vector2(520f, 52f), MenuTheme.Accent);
            MenuTheme.StyledButton(crt, "RecNext", ">", tl, tl, new Vector2(620f, -14f), new Vector2(64f, 52f), 30, () => { recordsTrack = (recordsTrack + 1) % TrackNames.Length; ShowRecords(); });
            float trackBest = BestOnTrack(recordsTrack, out int bestModel);
            UIFactory.Label("RecBest", crt, trackBest > 0f ? "RECORDE: " + HUD.FormatTime(trackBest) + " — " + CarLabel((CarModel)bestModel) : "SEM VOLTAS NESTA PISTA AINDA", 20, TextAnchor.MiddleCenter, tl, tl, new Vector2(16f, -76f), new Vector2(668f, 30f), MenuTheme.TextDim);
            const int perPage = 12;
            int pages = Mathf.Max(1, Mathf.CeilToInt(AllModels.Length / (float)perPage));
            recordsPage = Mathf.Clamp(recordsPage, 0, pages - 1);
            int first = recordsPage * perPage, last = Mathf.Min(AllModels.Length, first + perPage);
            if (pages > 1)
            {
                MenuTheme.StyledButton(crt, "RecPgPrev", "<", tl, tl, new Vector2(150f, -730f), new Vector2(64f, 52f), 30, () => { recordsPage = (recordsPage + pages - 1) % pages; ShowRecords(); });
                UIFactory.Label("RecPg", crt, "CARROS " + (first + 1) + "–" + last + " DE " + AllModels.Length, 20, TextAnchor.MiddleCenter, tl, tl, new Vector2(230f, -730f), new Vector2(240f, 52f), MenuTheme.TextDim);
                MenuTheme.StyledButton(crt, "RecPgNext", ">", tl, tl, new Vector2(486f, -730f), new Vector2(64f, 52f), 30, () => { recordsPage = (recordsPage + 1) % pages; ShowRecords(); });
            }
            for (int m = first; m < last; m++)
            {
                float best = SettingsManager.GetBestLap(recordsTrack, (int)AllModels[m]);
                float y = -120f - (m - first) * 50f;
                var row = UIFactory.Panel("RecRow" + m, crt, tl, tl, new Vector2(16f, y), new Vector2(668f, 44f), new Color(1f, 1f, 1f, m % 2 == 0 ? 0.05f : 0.02f));
                var rr = (RectTransform)row.transform;
                bool isRec = best > 0f && (int)AllModels[m] == bestModel;
                var ml = new Vector2(0f, 0.5f);
                UIFactory.Label("RN", rr, CarLabel(AllModels[m]), 22, TextAnchor.MiddleLeft, ml, ml, new Vector2(16f, 0f), new Vector2(420f, 40f), isRec ? Gold : MenuTheme.Text);
                UIFactory.Label("RT", rr, best > 0f ? HUD.FormatTime(best) : "--:--.---", 22, TextAnchor.MiddleRight, ml, ml, new Vector2(440f, 0f), new Vector2(210f, 40f), isRec ? Gold : best > 0f ? MenuTheme.Text : MenuTheme.TextDim);
            }
            MenuTheme.StyledButton(s, "Back", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(ContentX, 24f), new Vector2(380f, 70f), 28, ShowMain);
        }

        static float BestOnTrack(int track, out int model)
        {
            float best = 0f; model = -1;
            foreach (var m in AllModels)
            {
                float t = SettingsManager.GetBestLap(track, (int)m);
                if (t > 0f && (best <= 0f || t < best)) { best = t; model = (int)m; }
            }
            return best;
        }

        // ======================= SOBRE (saiu do menu principal; abre por CONFIGURAÇÕES) =======================
        void ShowCredits()
        {
            back = ShowSettings;
            var s = NewScreen("SOBRE O JOGO");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.97f);
            var t = UIFactory.Label("Text", s,
                "RaceX Proto — protótipo de corrida\n\nEngine: Unity 6 + URP\nMultiplayer: Node.js + Socket.IO\nContas e nuvem: Supabase\n\nVersão " + Application.version + " · build v80",
                32, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ContentX, 40f), new Vector2(1200f, 500f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuTheme.StyledButton(s, "Back", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(ContentX, 40f), new Vector2(380f, 90f), 32, ShowSettings);
        }
    }
}
