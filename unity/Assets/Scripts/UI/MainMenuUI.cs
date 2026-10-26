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
        Text lobbyTrackLabel;
        Text lobbyLapsLabel, lobbyCarsLabel, lobbyDiffLabel;
        Button lobbyReadyBtn;
        Button lobbyStartBtn;
        Button lobbyTrackPrev, lobbyTrackNext;
        Button[] lobbyOptButtons;
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
            UIFactory.Label("SideTitle", side, "RACEX PROTO", 44, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 60f), new Vector2(480f, 60f), MenuTheme.Accent).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
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
            MenuTheme.StyledButton(s, "MainCredits", "CRÉDITOS", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, ShowCredits);
            if (!Application.isMobilePlatform) MenuTheme.StyledButton(s, "MainQuit", "SAIR", top, top, new Vector2(-745f, startY - step * row++), bsize, 26, Application.Quit);
        }

        internal void ShowPlay()
        {
            back = ShowMain;
            var s = NewScreen("JOGAR");
            MenuButton(s, "SINGLE PLAYER", 0, () => { settings.mode = GameMode.SinglePlayer; ShowRaceSetup(); });
            if (Application.isMobilePlatform)
            {
                MenuButton(s, "ONLINE", 1, ShowOnlineSetup);
                MenuButton(s, "VOLTAR", 2, ShowMain);
            }
            else
            {
                MenuButton(s, "MULTIPLAYER LOCAL", 1, () => { settings.mode = GameMode.LocalSplitScreen; ShowRaceSetup(); });
                MenuButton(s, "ONLINE", 2, ShowOnlineSetup);
                MenuButton(s, "VOLTAR", 4, ShowMain);
            }
        }

        internal void ShowOnlineSetup()
        {
            back = () => { UnbindOnline(); ShowMain(); };
            lobbyScreen = null;
            if (AccountClient.IsLoggedIn && !string.IsNullOrWhiteSpace(AccountClient.Name)) onlineName = AccountClient.Name;
            var s = NewScreen("ONLINE");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.86f);
            int row = 0;
            NameRow(s, row++);
            ServerUrlRow(s, row++);
            OptionRow(s, "Carros IA", row++, () => SettingsManager.OnlineAiCount + " NPCs", d => SettingsManager.OnlineAiCount = Mathf.Clamp(SettingsManager.OnlineAiCount + d * 2, 0, 8));
            ActionRow(s, "CRIAR SALA", row++, CreateOnlineRoom);
            CodeRow(s, row++);
            ActionRow(s, "ENTRAR", row++, JoinOnlineRoom);
            ActionRow(s, "VOLTAR", row++, () => { UnbindOnline(); ShowMain(); });
            onlineSetupStatus = UIFactory.Label("OnlineStatus", s, "", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ContentX + 50f, 160f - row * 84f), new Vector2(1000f, 50f), MenuTheme.Accent);
            BindOnline();
        }

        void InfoRow(RectTransform parent, string label, int row, Func<string> value)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label(label, parent, label, 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 250f, y), new Vector2(380f, 72f));
            UIFactory.Label(label + "_Value", parent, value(), 34, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX + 50f, y), new Vector2(820f, 72f), MenuTheme.Text);
        }

        void ServerUrlRow(RectTransform parent, int row)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label("SERVIDOR", parent, "SERVIDOR", 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 250f, y), new Vector2(380f, 72f));
            var bg = UIFactory.Panel("ServerFieldBg", parent, c, c, new Vector2(ContentX + 50f, y), new Vector2(820f, 72f), new Color(1f, 1f, 1f, 0.06f));
            var ph = UIFactory.Label("ServerFieldPh", bg.transform, "ws://ip:3000", 28, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.TextDim);
            var phRt = (RectTransform)ph.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var txt = UIFactory.Label("ServerFieldText", bg.transform, "", 28, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.Text);
            var txtRt = (RectTransform)txt.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.placeholder = ph;
            input.textComponent = txt;
            string savedUrl = PlayerPrefs.GetString(OnlineClient.ServerUrlKey, "");
            input.text = savedUrl.Length > 0 ? savedUrl : OnlineClient.DefaultServerUrl;
            serverUrlField = input;
            MenuTheme.StyledButton(parent, "ServerSave", "SALVAR", c, c, new Vector2(ContentX + 50f + 410f + 90f, y), new Vector2(160f, 72f), 26, ApplyServerUrl);
            UIFactory.Label("ServerHint", parent, "Outros jogadores usam o IP da máquina do servidor (ex.: 192.168.0.10:3000)", 20, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX + 50f, y - 58f), new Vector2(820f, 30f), MenuTheme.TextDim);
        }

        void ApplyServerUrl()
        {
            string value = serverUrlField != null && !string.IsNullOrEmpty(serverUrlField.text) ? serverUrlField.text : OnlineClient.DefaultServerUrl;
            OnlineClient.ServerUrl = value;
            Debug.Log("[Online] servidor definido: " + OnlineClient.ServerUrl);
        }

        void ActionRow(RectTransform parent, string label, int row, Action onClick)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            MenuTheme.StyledButton(parent, "Online_" + label, label, c, c, new Vector2(ContentX + 50f, y), new Vector2(520f, 78f), 34, onClick);
        }

        static readonly string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        InputField codeInput;

        /// <summary>Campo de texto editável do nome do jogador — abre o teclado do celular ao tocar.</summary>
        void NameRow(RectTransform parent, int row)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label("NameLabel", parent, "NOME", 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 250f, y), new Vector2(380f, 72f));
            var bg = UIFactory.Panel("NameFieldBg", parent, c, c, new Vector2(ContentX + 110f, y), new Vector2(560f, 72f), new Color(1f, 1f, 1f, 0.07f));
            var ph = UIFactory.Label("NameFieldPh", bg.transform, "Seu nome", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 60f), new Color(1f, 1f, 1f, 0.25f));
            ph.raycastTarget = false;
            var txt = UIFactory.Label("NameFieldText", bg.transform, onlineName, 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 60f), MenuTheme.Text);
            txt.raycastTarget = false;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.placeholder = ph;
            input.textComponent = txt;
            input.text = onlineName;
            input.characterLimit = 14;
            input.contentType = InputField.ContentType.Standard;

            input.onValueChanged.AddListener(v => onlineName = string.IsNullOrWhiteSpace(v) ? "Jogador" : v);
        }

        void CodeRow(RectTransform parent, int row)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label("CodeLabel", parent, "CÓDIGO DA SALA", 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 250f, y), new Vector2(380f, 72f));
            var bg = UIFactory.Panel("CodeFieldBg", parent, c, c, new Vector2(ContentX + 110f, y), new Vector2(560f, 72f), new Color(1f, 1f, 1f, 0.07f));
            var ph = UIFactory.Label("CodeFieldPh", bg.transform, "EX.: 7NKUVQ", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 60f), new Color(1f, 1f, 1f, 0.25f));
            ph.raycastTarget = false;
            var txt = UIFactory.Label("CodeFieldText", bg.transform, onlineCode, 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 60f), MenuTheme.Text);
            txt.raycastTarget = false;
            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.placeholder = ph;
            input.textComponent = txt;
            input.text = onlineCode;
            input.characterLimit = 6;
            input.contentType = InputField.ContentType.Alphanumeric;

            input.onValueChanged.AddListener(v => { onlineCode = NormalizeCode(v); if (input != null && input.text != onlineCode) { input.text = onlineCode; input.MoveTextEnd(false); } });
            codeInput = input;
        }

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
            var s = NewScreen(AccountClient.IsLoggedIn ? "MINHA CONTA" : "ENTRAR");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.86f);
            var c = new Vector2(0.5f, 0.5f);
            accountStatus = UIFactory.Label("AccountStatus", s, "", 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -430f), new Vector2(1200f, 56f), MenuTheme.Accent);
            accountBody = UIFactory.Rect("AccountBody", s, c, c, c, new Vector2(0f, 30f), new Vector2(1300f, 780f));
            if (AccountClient.IsLoggedIn) ShowAccountProfile();
            else ShowAccountAuth();
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
            accNameField = AccountField(accountBody, "NOME NO JOGO", 2, AccountClient.Name ?? "", InputField.ContentType.Standard, "como aparecerá no lobby");
            var c = new Vector2(0.5f, 0.5f);
            MenuTheme.StyledButton(accountBody, "AccLogin", "ENTRAR", c, c, new Vector2(-310f, -240f), new Vector2(540f, 88f), 32, AccountDoLogin);
            MenuTheme.StyledButton(accountBody, "AccSignup", "CRIAR CONTA", c, c, new Vector2(310f, -240f), new Vector2(540f, 88f), 32, AccountDoSignup);
        }

        void AccountDoLogin()
        {
            if (accEmailField == null || accPassField == null) return;
            SetAccountStatus("Entrando...");
            AccountClient.LogIn(accEmailField.text.Trim(), accPassField.text, (ok, err) =>
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
            SetAccountStatus("Criando conta...");
            AccountClient.SignUp(accEmailField.text.Trim(), accPassField.text, string.IsNullOrWhiteSpace(accNameField.text) ? "Jogador" : accNameField.text.Trim(), (ok, err) =>
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
            var avatar = UIFactory.Panel("Avatar", accountBody, c, c, new Vector2(-560f, 220f), new Vector2(150f, 150f), AvatarColorOf(AccountClient.AvatarColor));
            var initial = UIFactory.Label("AvatarInitial", avatar.transform, string.IsNullOrEmpty(AccountClient.Name) ? "J" : AccountClient.Name.Substring(0, 1).ToUpperInvariant(), 68, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f), Color.white);
            initial.raycastTarget = false;
            if (!string.IsNullOrEmpty(AccountClient.Photo) && AccountClient.Photo.Length > 128)
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
            UIFactory.Label("AccEmail", accountBody, AccountClient.Email, 30, TextAnchor.MiddleLeft, c, c, new Vector2(-330f, 220f), new Vector2(760f, 70f), MenuTheme.Text);
            accNameField = AccountField(accountBody, "NOME NO JOGO", 1, AccountClient.Name ?? "Jogador", InputField.ContentType.Standard);
            accPhotoField = AccountField(accountBody, "FOTO (URL)", 2, "", InputField.ContentType.Standard, "cole o endereço de uma imagem");
            MenuTheme.StyledButton(accountBody, "AccPhoto", "CARREGAR FOTO", c, c, new Vector2(560f, 170f - 2 * 104f), new Vector2(320f, 80f), 26, AccountDoPhoto);
            var colorBtn = MenuTheme.StyledButton(accountBody, "AccColor", "COR DO AVATAR: " + (AccountClient.AvatarColor + 1), c, c, new Vector2(-560f, 170f - 3 * 104f), new Vector2(520f, 80f), 28, null);
            int nextColor = AccountClient.AvatarColor;
            colorBtn.onClick.AddListener(() =>
            {
                nextColor = (nextColor + 1) & 7;
                colorBtn.transform.Find("AccColor_Label").GetComponent<Text>().text = "COR DO AVATAR: " + (nextColor + 1);
                colorBtn.transform.Find("AccColor_Label").GetComponent<Text>().color = Color.white;
                colorBtn.image.color = AvatarColorOf(nextColor);
            });
            colorBtn.image.color = AvatarColorOf(AccountClient.AvatarColor);
            UIFactory.Label("AccStats", accountBody,
                string.Format("VITÓRIAS {0}    PÓDIOS {1}    TÍTULOS {2}    CORRIDAS {3}", AccountClient.Wins, AccountClient.Podiums, AccountClient.Titles, AccountClient.Races),
                28, TextAnchor.MiddleCenter, c, c, new Vector2(120f, 170f - 3 * 104f), new Vector2(1000f, 80f), MenuTheme.Accent);
            MenuTheme.StyledButton(accountBody, "AccSave", "SALVAR PERFIL", c, c, new Vector2(-310f, -300f), new Vector2(540f, 88f), 32, () => AccountDoSave(nextColor));
            MenuTheme.StyledButton(accountBody, "AccLogout", "SAIR DA CONTA", c, c, new Vector2(310f, -300f), new Vector2(540f, 88f), 32, AccountDoLogout);
        }

        Color AvatarColorOf(int i) { return Color.HSVToRGB((i & 7) / 8f, 0.62f, 0.95f); }

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
            AccountClient.SetProfile(accNameField != null ? accNameField.text : null, null, color, (ok, err) =>
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
            carSentForRoom = false;
            ApplyServerUrl();
            SetOnlineStatus("");
            OnlineClient.Connect(OnlineClient.ServerUrl);
            OnlineClient.JoinRoom(onlineCode, onlineName);
            ShowLobby();
        }

        void ShowLobby()
        {
            back = LeaveLobby;
            var s = NewScreen("SALA");
            lobbyScreen = s;
            var c = new Vector2(0.5f, 0.5f);
            lobbyStatus = UIFactory.Label("LobbyStatus", s, "Conectando...", 30, TextAnchor.MiddleCenter, c, c, new Vector2(ContentX, 300f), new Vector2(1200f, 46f), MenuTheme.Text);
            UIFactory.Label("LobbyServer", s, "Servidor: " + OnlineClient.ServerUrl, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1200f, 30f), MenuTheme.TextDim);
            UIFactory.Panel("LobbyBox", s, c, c, new Vector2(ContentX, 170f), new Vector2(1100f, 300f), new Color(1f, 1f, 1f, 0.05f));
            lobbyList = UIFactory.Rect("LobbyList", s, c, c, new Vector2(0.5f, 0.5f), new Vector2(ContentX, 170f), new Vector2(1040f, 280f)).transform;
            var b = new Vector2(0.5f, 0f);
            lobbyReadyBtn = MenuTheme.StyledButton(s, "LobbyReady", "PRONTO", b, b, new Vector2(-400f, 40f), new Vector2(360f, 100f), 36, ToggleReady);
            lobbyStartBtn = MenuTheme.StyledButton(s, "LobbyStart", "COMEÇAR", b, b, new Vector2(0f, 40f), new Vector2(360f, 100f), 36, () => OnlineClient.StartRace());
            lobbyStartBtn.interactable = false;
            MenuTheme.StyledButton(s, "LobbyBack", "VOLTAR", b, b, new Vector2(400f, 40f), new Vector2(360f, 100f), 36, LeaveLobby);
            // CONFIGURAÇÕES DA SALA (as MESMAS opções do single player): PISTA (24) + VOLTAS/CARROS/DIFICULDADE.
            // Host navega < >; os demais só veem. O servidor sanitiza, aplica e re-transmite no lobby.
            lobbyTrackLabel = UIFactory.Label("LobbyTrack", s, "", 30, TextAnchor.MiddleCenter, b, b, new Vector2(0f, 280f), new Vector2(700f, 40f), MenuTheme.Accent);
            lobbyTrackPrev = MenuTheme.StyledButton(s, "LobbyTrackPrev", "<", b, b, new Vector2(-440f, 280f), new Vector2(110f, 90f), 34, () => CycleRoomTrack(-1));
            lobbyTrackNext = MenuTheme.StyledButton(s, "LobbyTrackNext", ">", b, b, new Vector2(440f, 280f), new Vector2(110f, 90f), 34, () => CycleRoomTrack(1));
            lobbyOptButtons = new Button[6];
            lobbyLapsLabel = UIFactory.Label("LobbyLaps", s, "", 26, TextAnchor.MiddleCenter, b, b, new Vector2(-420f, 185f), new Vector2(300f, 40f), MenuTheme.Text);
            lobbyCarsLabel = UIFactory.Label("LobbyCars", s, "", 26, TextAnchor.MiddleCenter, b, b, new Vector2(0f, 185f), new Vector2(300f, 40f), MenuTheme.Text);
            lobbyDiffLabel = UIFactory.Label("LobbyDiff", s, "", 26, TextAnchor.MiddleCenter, b, b, new Vector2(420f, 185f), new Vector2(300f, 40f), MenuTheme.Text);
            lobbyOptButtons[0] = MenuTheme.StyledButton(s, "LapsPrev", "<", b, b, new Vector2(-590f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(0, -1));
            lobbyOptButtons[1] = MenuTheme.StyledButton(s, "LapsNext", ">", b, b, new Vector2(-250f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(0, 1));
            lobbyOptButtons[2] = MenuTheme.StyledButton(s, "CarsPrev", "<", b, b, new Vector2(-170f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(1, -1));
            lobbyOptButtons[3] = MenuTheme.StyledButton(s, "CarsNext", ">", b, b, new Vector2(170f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(1, 1));
            lobbyOptButtons[4] = MenuTheme.StyledButton(s, "DiffPrev", "<", b, b, new Vector2(250f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(2, -1));
            lobbyOptButtons[5] = MenuTheme.StyledButton(s, "DiffNext", ">", b, b, new Vector2(590f, 185f), new Vector2(90f, 80f), 30, () => CycleRoomSetting(2, 1));
            RefreshLobby();
        }

        /// <summary>Ids de pista no SERVIDOR — MESMA ORDEM de TrackNames/TrackLibrary.All (24 pistas).</summary>
        static readonly string[] ServerTrackIds =
        {
            "forest", "desert", "city", "velocita", "principado", "ardenne", "jardim", "golfo",
            "serra", "sol", "parque", "dragao", "corniche", "porto", "cascatas", "dourado",
            "colinas", "norte", "danubio", "dunas", "fortaleza", "texas", "estadio", "neon"
        };

        /// <summary>Total de carros aceito pelo servidor (humanos + IA) — server/utils.js TOTAL_CAR_OPTIONS.</summary>
        static readonly int[] ServerCarCounts = { 4, 6, 8, 10, 12, 16 };
        static readonly string[] ServerDiffIds = { "easy", "medium", "hard", "expert" };

        int RoomTrackIndex => Mathf.Clamp(System.Array.IndexOf(ServerTrackIds, OnlineClient.RoomTrackId), 0, TrackNames.Length - 1);

        void CycleRoomTrack(int dir)
        {
            if (!OnlineClient.IsHost) return;
            int next = (int)Mathf.Repeat(RoomTrackIndex + dir, ServerTrackIds.Length);
            OnlineClient.SetRoomTrack(ServerTrackIds[next]); // servidor aplica e re-transmite no lobby
        }

        /// <summary>Host altera VOLTAS (0), CARROS (1) ou DIFICULDADE (2) — reenvia o conjunto completo da sala.</summary>
        void CycleRoomSetting(int which, int dir)
        {
            if (!OnlineClient.IsHost) return;
            string track = ServerTrackIds[RoomTrackIndex];
            int laps = OnlineClient.RoomLaps;
            int cars = OnlineClient.RoomTotalCars;
            string diff = OnlineClient.RoomDifficulty;
            switch (which)
            {
                case 0:
                {
                    int i = Mathf.Max(0, System.Array.IndexOf(RaceSettings.LapOptions, laps));
                    laps = RaceSettings.LapOptions[(int)Mathf.Repeat(i + dir, RaceSettings.LapOptions.Length)];
                    break;
                }
                case 1:
                {
                    int i = Mathf.Max(0, System.Array.IndexOf(ServerCarCounts, cars));
                    cars = ServerCarCounts[(int)Mathf.Repeat(i + dir, ServerCarCounts.Length)];
                    break;
                }
                case 2:
                {
                    int i = Mathf.Max(0, System.Array.IndexOf(ServerDiffIds, diff));
                    diff = ServerDiffIds[(int)Mathf.Repeat(i + dir, ServerDiffIds.Length)];
                    break;
                }
            }
            OnlineClient.SendRoomSettings(track, laps, cars, diff);
        }

        string RoomDiffName()
        {
            int i = System.Array.IndexOf(ServerDiffIds, OnlineClient.RoomDifficulty);
            return DifficultyNames[Mathf.Clamp(i, 0, DifficultyNames.Length - 1)];
        }

        void RefreshLobby()
        {
            if (lobbyScreen == null) return;
            var title = lobbyScreen.Find("Title");
            if (title != null)
                title.GetComponent<Text>().text = OnlineClient.RoomCode != null ? "SALA " + OnlineClient.RoomCode : OnlineClient.ConnectFailed ? "SALA — FALHA DE CONEXÃO" : "SALA — CONECTANDO";
            if (lobbyStatus != null)
            {
                if (!OnlineClient.Connected && !OnlineClient.ConnectFailed)
                    lobbyStatus.text = "Conectando...";
                else if (OnlineClient.ConnectFailed)
                    lobbyStatus.text = "Falha ao conectar — verifique o servidor em ONLINE" + (string.IsNullOrEmpty(OnlineClient.LastServerMessage) ? "" : " (" + OnlineClient.LastServerMessage + ")");
                else
                    lobbyStatus.text = OnlineClient.LastError ?? "Aguardando jogadores...";
            }
            for (int i = 0; i < 16; i++)
            {
                var old = lobbyList != null ? lobbyList.Find("LobbyPlayer" + i) : null;
                if (old != null) Destroy(old.gameObject);
            }
            var list = OnlineClient.LobbyPlayers;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                string marker = p.id == OnlineClient.MyPlayerId ? "> " : "";
                string ready = p.ready ? "  [PRONTO]" : "  [...]";
                string host = p.isHost ? "  (anfitrião)" : "";
                float y = 140f - i * 44f;
                UIFactory.Label("LobbyPlayer" + i, lobbyList, marker + p.name + ready + host, 30, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-400f, y), new Vector2(900f, 40f), p.id == OnlineClient.MyPlayerId ? MenuTheme.Accent : MenuTheme.Text);
            }
            var me = OnlineClient.Me;
            if (lobbyReadyBtn != null)
                lobbyReadyBtn.transform.Find("LobbyReady_Label").GetComponent<Text>().text = me != null && me.ready ? "CANCELAR PRONTO" : "PRONTO";
            if (lobbyStartBtn != null)
            {
                bool canStart = OnlineClient.IsHost && list.Count > 0 && list.TrueForAll(p => p.ready);
                lobbyStartBtn.interactable = canStart;
            }
            if (lobbyTrackLabel != null)
            {
                bool host = OnlineClient.IsHost;
                lobbyTrackLabel.text = "PISTA  " + TrackNames[RoomTrackIndex] + (host ? "" : "  (só o anfitrião muda)");
                if (lobbyTrackPrev != null) lobbyTrackPrev.interactable = host;
                if (lobbyTrackNext != null) lobbyTrackNext.interactable = host;
                if (lobbyLapsLabel != null) lobbyLapsLabel.text = "VOLTAS  " + OnlineClient.RoomLaps;
                if (lobbyCarsLabel != null) lobbyCarsLabel.text = "CARROS  " + OnlineClient.RoomTotalCars;
                if (lobbyDiffLabel != null) lobbyDiffLabel.text = "NÍVEL  " + RoomDiffName();
                if (lobbyOptButtons != null)
                    foreach (var ob in lobbyOptButtons)
                        if (ob != null) ob.interactable = host;
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
            OnlineClient.OnGameStart += StartOnlineRace;
        }

        void UnbindOnline()
        {
            OnlineClient.OnRoomCreated -= OnOnlineRoomCreated;
            OnlineClient.OnRoomJoined -= OnOnlineRoomJoined;
            OnlineClient.OnLobbyUpdate -= OnOnlineLobbyUpdate;
            OnlineClient.OnError -= OnOnlineError;
            OnlineClient.OnGameStart -= StartOnlineRace;
        }

        void OnOnlineRoomCreated(string code)
        {
            Debug.Log("[Online] código " + code);
            if (lobbyStatus != null) lobbyStatus.text = "Sala " + code + " criada — compartilhe o código com outros jogadores";
            RefreshLobby();
        }

        void OnOnlineRoomJoined(string code)
        {
            if (!carSentForRoom)
            {
                carSentForRoom = true;
                OnlineClient.UpdatePlayer((int)settings.players[0].car, settings.players[0].colorIndex);
            }
            RefreshLobby();
        }

        void OnOnlineLobbyUpdate()
        {
            carSentForRoom = true;
            RefreshLobby();
        }

        void OnOnlineError(string message)
        {
            if (lobbyStatus != null) lobbyStatus.text = "Erro: " + message;
            else if (onlineSetupStatus != null) onlineSetupStatus.text = "Erro: " + message;
        }

        void SetOnlineStatus(string msg)
        {
            if (lobbyStatus != null) lobbyStatus.text = msg;
            if (onlineSetupStatus != null) onlineSetupStatus.text = msg;
        }

        void StartOnlineRace(GameStartInfo info)
        {
            UnbindOnline();
            var st = GameManager.Settings;
            st.mode = GameMode.Online;
            st.track = ServerTrack(info.trackId);
            st.laps = Mathf.Clamp(info.laps, 1, 20);
            st.totalCars = Mathf.Max(1, info.totalCars);
            st.difficulty = ServerDifficulty(info.difficulty);
            st.formulaOnly = false;
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

        void OptionRow(RectTransform parent, string label, int row, Func<string> value, Action<int> change, float x = ContentX)
        {
            float y = 330f - row * 84f;
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
            default: return m.ToString();
            }
        }

        static readonly CarModel[] AllModels = (CarModel[])System.Enum.GetValues(typeof(CarModel));

        static int ModelCount(RaceSettings st) => st.formulaOnly ? 1 : (SettingsManager.AllowF1 ? AllModels.Length : AllModels.Length - 1);

        static int Step(int value, int delta, int count) => ((value + delta) % count + count) % count;

        static int StepIn(int[] options, int value, int delta)
        {
            int i = Array.IndexOf(options, value);
            if (i < 0) i = 0;
            return options[Step(i, delta, options.Length)];
        }

        internal void ShowRaceSetup()
        {
            back = ShowPlay;
            bool split = settings.mode == GameMode.LocalSplitScreen;
            SetShowroomCam(split ? new Vector3(0f, 3.6f, -12.5f) : new Vector3(0f, 3.4f, -10.5f), 26f);
            var s = NewScreen(split ? "CORRIDA — TELA DIVIDIDA" : "CONFIGURAÇÕES DA CORRIDA");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.82f);
            int row = 0;
            OptionRow(s, "Pista", row++, () => TrackNames[(int)settings.track], d => settings.track = (TrackId)Step((int)settings.track, d, TrackNames.Length));
            OptionRow(s, "Voltas", row++, () => settings.laps <= 0 ? "REAIS (" + TrackLibrary.RealLaps(settings.track) + ")" : settings.laps.ToString(), d => settings.laps = StepIn(RaceSettings.LapOptions, settings.laps, d));
            OptionRow(s, "Adversários", row++, () => settings.totalCars == RaceSettings.ClassicCarCount ? "Clássico (8)" : settings.totalCars.ToString(),
                d => settings.totalCars = StepIn(RaceSettings.CarCountOptions, settings.totalCars, d));
            OptionRow(s, "Dificuldade", row++, () => DifficultyNames[(int)settings.difficulty], d => settings.difficulty = (Difficulty)Step((int)settings.difficulty, d, 4));
            OptionRow(s, "Pit Stop", row++, () => settings.pitStops ? "LIGADO (com estratégia)" : "DESLIGADO", d => settings.pitStops = !settings.pitStops);
            OptionRow(s, "Categoria", row++, () => settings.formulaOnly ? "F1" : "GT",
                d =>
                {
                    settings.formulaOnly = !settings.formulaOnly;
                    if (settings.formulaOnly) { settings.players[0].car = CarModel.F1; settings.players[1].car = CarModel.F1; }
                    else { settings.players[0].car = CarModel.Titan; settings.players[1].car = CarModel.Titan; }
                    RefreshShowroomCarAsync();
                });

            if (!split) OptionRow(s, "Câmera", row++, () => CameraNames[(int)settings.players[0].startCamera], d => settings.players[0].startCamera = (CameraView)Step((int)settings.players[0].startCamera, d, 4));

            int players = split ? 2 : 1;
            for (int p = 0; p < players; p++)
            {
                var ps = settings.players[p];
                string tag = split ? $"J{p + 1} " : "";
                OptionRow(s, tag + "Carro", row++, () => CarLabel(ps.car), d =>
                {
                    if (settings.formulaOnly) { ps.car = CarModel.F1; RefreshShowroomCarAsync(); return; }
                    ps.car = (CarModel)Step((int)ps.car, d, ModelCount(settings));
                    RefreshShowroomCarAsync();
                });
                OptionRow(s, tag + "Cor", row++, () => CarPalette.Names[ps.colorIndex], d =>
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
                if (!split) SettingsManager.LastCamera = (int)settings.players[0].startCamera;
                SettingsManager.Save();
                SceneLoader.LoadRace(settings);
            });
        }

        internal void ShowSettings()
        {
            back = () => { SettingsManager.Save(); ShowMain(); };
            var s = NewScreen("CONFIGURAÇÕES");
            int row = 0;
            OptionRow(s, "Volume geral", row++, () => Pct(SettingsManager.MasterVolume), d => { SettingsManager.MasterVolume = Clamp10(SettingsManager.MasterVolume, d); AudioListener.volume = SettingsManager.MasterVolume; });
            OptionRow(s, "Volume do motor", row++, () => Pct(SettingsManager.EngineVolume), d => SettingsManager.EngineVolume = Clamp10(SettingsManager.EngineVolume, d));
            OptionRow(s, "Música", row++, () => SettingsManager.MusicOn ? "Ligada" : "Desligada", d => SettingsManager.MusicOn = !SettingsManager.MusicOn);
            OptionRow(s, "Efeitos sonoros", row++, () => SettingsManager.SfxOn ? "Ligados" : "Desligados", d => SettingsManager.SfxOn = !SettingsManager.SfxOn);
            OptionRow(s, "Nomes sobre carros", row++, () => SettingsManager.NameTags ? "Ligados" : "Desligados", d => SettingsManager.NameTags = !SettingsManager.NameTags);
            OptionRow(s, "Mini-mapa", row++, () => SettingsManager.MinimapDynamic ? "Dinâmico (trecho)" : "Pista inteira", d => SettingsManager.MinimapDynamic = !SettingsManager.MinimapDynamic);
            OptionRow(s, "Qualidade", row++, () => QualityNames[(int)SettingsManager.Quality], d => { SettingsManager.Quality = (QualityTier)Step((int)SettingsManager.Quality, d, 4); SettingsManager.ApplyQuality(SettingsManager.Quality); });
            if (Application.isMobilePlatform)
                OptionRow(s, "Controles", row++, () => ControlNames[(int)SettingsManager.ControlMode], d => SettingsManager.ControlMode = (ControlMode)Step((int)SettingsManager.ControlMode, d, 3));
            else
                OptionRow(s, "Tela cheia", row++, () => Screen.fullScreen ? "Sim" : "Não", d => Screen.fullScreen = !Screen.fullScreen);
            OptionRow(s, "F1 em corridas GT", row++, () => SettingsManager.AllowF1 ? "Sim" : "Não", d => SettingsManager.AllowF1 = !SettingsManager.AllowF1);
            MenuTheme.StyledButton(s, "Back", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 100f), 36, () => { SettingsManager.Save(); ShowMain(); });
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";
        static float Clamp10(float v, int d) => Mathf.Clamp01(Mathf.Round((v + d * 0.1f) * 10f) / 10f);

        internal void ShowGarage()
        {
            back = ShowMain;
            garageIndex = (int)settings.players[0].car;
            var s = NewScreen("GARAGEM");
            SetShowroomCam(new Vector3(0f, 2.2f, -7.4f), 10f);
            string modelName = CarLabel((CarModel)garageIndex);
            var c = new Vector2(0.5f, 0.5f);
            var current = (CarModel)garageIndex;
            MenuTheme.StyledButton(s, "GaragePrev", "<", c, c, new Vector2(-490f, 430f), new Vector2(70f, 56f), 34, () => ChangeGarage(-1));
            UIFactory.Label("GarageName", s, modelName, 46, TextAnchor.MiddleCenter, c, c, new Vector2(-90f, 430f), new Vector2(760f, 60f), MenuTheme.Accent);
            MenuTheme.StyledButton(s, "GarageNext", ">", c, c, new Vector2(310f, 430f), new Vector2(70f, 56f), 34, () => ChangeGarage(1));
            UIFactory.Label("GarageCredits", s, "Créditos: " + CareerStats.Credits, 30, TextAnchor.MiddleCenter, c, c, new Vector2(215f, 386f), new Vector2(600f, 36f), MenuTheme.Text);

            for (int i = 0; i < AllModels.Length; i++)
            {
                int idx = i;
                CarModel cm = AllModels[i];
                bool sel = (int)cm == garageIndex;
                float cx = 215f + (i % 7 - 3) * 126f;
                float cy = 316f - (i / 7) * 56f;
                MenuTheme.StyledButton(s, "GarageCar" + i, CarLabel(cm).Split(' ')[0], c, c, new Vector2(cx, cy), new Vector2(118f, 46f), 20,
                    () => { garageIndex = (int)cm; settings.players[0].car = cm; RefreshShowroomCarAsync(); ShowGarage(); });
                if (sel) UIFactory.Panel("GarageSel" + i, s, c, c, new Vector2(cx, cy - 26f), new Vector2(114f, 4f), MenuTheme.Accent);
            }

            string[] upLabels = { "Motor", "Freios", "Pneus" };
            int[] kinds = { CareerStats.KindEngine, CareerStats.KindBrakes, CareerStats.KindTires };
            for (int row = 0; row < 3; row++)
            {
                int kind = kinds[row];
                int lvl = CareerStats.UpgradeLevel(current, kind);
                var rowRt = UIFactory.Rect("UpgRow" + row, s, c, c, c, new Vector2(215f, 232f - row * 52f), new Vector2(700f, 40f));
                UIFactory.Label("UpgL" + row, rowRt, upLabels[row], 26, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(160f, 36f), MenuTheme.Text);
                UIFactory.Label("UpgV" + row, rowRt, "Nv " + lvl + "/" + CareerStats.MaxUpgrade, 26, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(180f, 0f), new Vector2(140f, 36f), MenuTheme.Accent);
                if (lvl >= CareerStats.MaxUpgrade)
                    MenuTheme.StyledButton(rowRt, "UpgB" + row, "MAX", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(560f, 0f), new Vector2(190f, 40f), 22, () => { });
                else
                {
                    int cost = CareerStats.UpgradeCost(lvl);
                    string btn = "COMPRAR " + cost + " cr";
                    MenuTheme.StyledButton(rowRt, "UpgB" + row, btn, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(560f, 0f), new Vector2(260f, 40f), 20, () =>
                    {
                        CareerStats.BuyUpgrade(current, kind);
                        ShowGarage();
                    });
                }
            }

            var spec = CarSpec.CreateDefault(current);
            CareerStats.ApplyUpgrades(spec);
            string[] labels = { "Velocidade", "Aceleração", "Aderência" };
            float[] vals =
            {
                Mathf.Clamp01(spec.topSpeedKmh / 340f) * 0.7f + 0.3f,
                Mathf.Clamp01(1f - (spec.zeroToHundredSeconds - 2.5f) / 2.5f) * 0.7f + 0.3f,
                Mathf.Clamp01(spec.downforceCoefficient / 4f) * 0.6f + 0.35f
            };
            for (int row = 0; row < 3; row++)
            {
                var rowRt = UIFactory.Rect("GarageRow" + row, s, c, c, c, new Vector2(215f, 44f - row * 48f), new Vector2(560f, 34f));
                MenuTheme.StatBar(rowRt, labels[row], vals[row]);
            }
            UIFactory.Label("GarageNote", s, "Ganhe créditos correndo para melhorar o carro", 24, TextAnchor.MiddleCenter, c, c, new Vector2(215f, -128f), new Vector2(700f, 30f), MenuTheme.TextDim);
            MenuTheme.StyledButton(s, "GarageBack", "VOLTAR", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140f, -52f), new Vector2(260f, 60f), 28, ShowMain);
        }

        void ChangeGarage(int delta)
        {
            garageIndex = Step(garageIndex, delta, AllModels.Length);
            settings.players[0].car = (CarModel)garageIndex;
            RefreshShowroomCarAsync();
            ShowGarage();
        }

        void ShowProgress()
        {
            back = ShowMain;
            var s = NewScreen("PROGRESSO");
            var c = new Vector2(0.5f, 0.5f);
            var box = UIFactory.Panel("ProgressBox", s, c, c, new Vector2(215f, 40f), new Vector2(820f, 560f), new Color(1f, 1f, 1f, 0.05f));
            string[] labels = { "Corridas", "Vitórias", "Pódios", "Créditos" };
            float[] vals =
            {
                Mathf.Clamp01(CareerStats.Races / 50f),
                Mathf.Clamp01(CareerStats.Wins / 25f),
                Mathf.Clamp01(CareerStats.Podiums / 40f),
                Mathf.Clamp01(CareerStats.Credits / 60000f)
            };
            int[] nums = { CareerStats.Races, CareerStats.Wins, CareerStats.Podiums, CareerStats.Credits };
            for (int i = 0; i < 4; i++)
            {
                var rowRt = UIFactory.Rect("ProgressRow" + i, box.transform, c, c, c, new Vector2(0f, 190f - i * 84f), new Vector2(640f, 36f));
                MenuTheme.StatBar(rowRt, labels[i] + ": " + nums[i], vals[i]);
            }
            UIFactory.Label("ProgressNote", box.transform, "Corra para ganhar créditos e evoluir seus carros na GARAGEM", 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -190f), new Vector2(760f, 50f), MenuTheme.TextDim);
            MenuTheme.StyledButton(s, "ProgressBack", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(215f, 24f), new Vector2(300f, 56f), 26, ShowMain);
        }

        void ShowRecords()
        {
            back = ShowMain;
            var s = NewScreen("RECORDES");
            var c = new Vector2(0.5f, 0.5f);
            for (int t = 0; t < TrackNames.Length; t++)
            {
                float x = 215f + (t - 1) * 470f;
                UIFactory.Label("RecHead" + t, s, TrackNames[t], 28, TextAnchor.MiddleCenter, c, c, new Vector2(x, 265f), new Vector2(440f, 40f), MenuTheme.Accent);
                for (int m = 0; m < AllModels.Length; m++)
                {
                    float best = SettingsManager.GetBestLap(t, (int)AllModels[m]);
                    string line = $"{CarLabel(AllModels[m]),-22} {(best > 0f ? HUD.FormatTime(best) : "--:--.---")}";
                    UIFactory.Label("Rec" + t + "_" + m, s, line, 22, TextAnchor.MiddleLeft, c, c, new Vector2(x, 225f - m * 44f), new Vector2(440f, 40f), MenuTheme.Text);
                }
            }
            MenuTheme.StyledButton(s, "Back", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 100f), 36, ShowMain);
        }

        void ShowCredits()
        {
            back = ShowMain;
            var s = NewScreen("CRÉDITOS");
            UIFactory.Label("Text", s,
                "RaceX Proto — protótipo\n\nCódigo: gerado a partir do Prompt Mestre\nEngine: Unity 6 + URP\nRede: Node.js + Socket.io (MIT)\n\nTodos os modelos e pistas são gerados por código (sem assets de terceiros).",
                36, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 600f));
            MenuTheme.StyledButton(s, "Back", "VOLTAR", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 100f), 36, ShowMain);
        }
    }
}
