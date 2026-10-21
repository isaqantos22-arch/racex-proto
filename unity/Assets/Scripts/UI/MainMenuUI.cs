using System;
using System.Collections;
using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Splash → Menu principal → Jogar → Configurações da corrida → Corrida.</summary>
    public class MainMenuUI : MonoBehaviour
    {
        static readonly string[] TrackNames = { "Circuito Verde", "Desfiladeiro Solar", "Metrópole Noturna" };
        static readonly string[] DifficultyNames = { "Fácil", "Médio", "Difícil", "Expert" };
        static readonly string[] CameraNames = { "Terceira pessoa", "Primeira pessoa", "Volante", "Frontal" };
        static readonly string[] QualityNames = { "Baixa", "Média", "Alta", "Ultra" };
        static readonly string[] ControlNames = { "Botões na tela", "Inclinação", "Joystick virtual" };

        Canvas canvas;
        RectTransform current;
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
        int codeCursor;
        RectTransform lobbyScreen;
        Text onlineSetupStatus;
        Text lobbyStatus;
        Transform lobbyList;
        Button lobbyReadyBtn;
        Button lobbyStartBtn;
        bool carSentForRoom;
        InputField serverUrlField;
        float lobbyPollTimer;

        void Start()
        {
            Time.timeScale = 1f;
            settings = GameManager.Settings;
            UIFactory.EnsureEventSystem();
            canvas = UIFactory.CreateCanvas("MenuCanvas");
            GameAudio.EnterMenu();
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

        internal void RefreshShowroomCar()
        {
            float yaw = showroomCar != null ? showroomCar.eulerAngles.y : 200f;
            if (showroomCar != null) Destroy(showroomCar.gameObject);
            if (showroomCar2 != null) Destroy(showroomCar2.gameObject);
            bool split = settings.mode == GameMode.LocalSplitScreen;
            var p = settings.players[0];
            var car = CarFactory.Create(CarSpec.CreateDefault(p.car), p.colorIndex, "Showroom_" + p.car, new Vector3(split ? -2.2f : 0f, 0.1f, 0f), Quaternion.Euler(0f, yaw, 0f));
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            car.enabled = false;
            showroomCar = car.transform;
            if (split)
            {
                var p2 = settings.players[1];
                var car2 = CarFactory.Create(CarSpec.CreateDefault(p2.car), p2.colorIndex, "Showroom_" + p2.car, new Vector3(2.2f, 0.1f, 0f), Quaternion.Euler(0f, yaw, 0f));
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
            var c = new Vector2(0.5f, 0.5f);
            var bsize = new Vector2(330f, 60f);

            MenuTheme.StyledButton(s, "MainPlay", "JOGAR", c, c, new Vector2(-745f, 412f), bsize, 26, ShowPlay);
            MenuTheme.StyledButton(s, "MainOnline", "ONLINE", c, c, new Vector2(-745f, 334f), bsize, 26, ShowOnlineSetup);
            MenuTheme.StyledButton(s, "MainGarage", "GARAGEM", c, c, new Vector2(-745f, 256f), bsize, 26, ShowGarage);
            MenuTheme.StyledButton(s, "MainProgress", "PROGRESSO", c, c, new Vector2(-745f, 178f), bsize, 26, ShowProgress);
            MenuTheme.StyledButton(s, "MainSettings", "CONFIGURAÇÕES", c, c, new Vector2(-745f, 100f), bsize, 26, ShowSettings);
            MenuTheme.StyledButton(s, "MainRecords", "RECORDES", c, c, new Vector2(-745f, 22f), bsize, 26, ShowRecords);
            MenuTheme.StyledButton(s, "MainCredits", "CRÉDITOS", c, c, new Vector2(-745f, -56f), bsize, 26, ShowCredits);
            if (!Application.isMobilePlatform) MenuTheme.StyledButton(s, "MainQuit", "SAIR", c, c, new Vector2(-745f, -134f), bsize, 26, Application.Quit);
        }

        internal void ShowPlay()
        {
            back = ShowMain;
            var s = NewScreen("JOGAR");
            MenuButton(s, "SINGLE PLAYER", 0, () => { settings.mode = GameMode.SinglePlayer; ShowRaceSetup(); });
            var local = MenuButton(s, "MULTIPLAYER LOCAL", 1, () => { settings.mode = GameMode.LocalSplitScreen; ShowRaceSetup(); });
            local.interactable = !Application.isMobilePlatform;
            MenuButton(s, "ONLINE", 2, ShowOnlineSetup);
            MenuButton(s, "VOLTAR", 4, ShowMain);
        }

        internal void ShowOnlineSetup()
        {
            back = () => { UnbindOnline(); ShowMain(); };
            lobbyScreen = null;
            var s = NewScreen("ONLINE");
            ((Image)s.Find("Bg").GetComponent<Image>()).color = new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.86f);
            int row = 0;
            InfoRow(s, "NOME", row++, () => onlineName);
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

        void CodeRow(RectTransform parent, int row)
        {
            float y = 330f - row * 84f;
            var c = new Vector2(0.5f, 0.5f);
            UIFactory.Label("CodeLabel", parent, "CÓDIGO DA SALA", 32, TextAnchor.MiddleRight, c, new Vector2(1f, 0.5f), new Vector2(ContentX - 250f, y), new Vector2(380f, 72f));
            for (int i = 0; i < onlineCode.Length; i++)
            {
                int idx = i;
                var cell = MenuTheme.StyledButton(parent, "CodeCell" + i, onlineCode[idx].ToString(), c, c, new Vector2(ContentX - 130f + i * 80f, y), new Vector2(72f, 72f), 34, () => { codeCursor = idx; RefreshCodeRow(); });
                cell.transform.Find("CodeCell" + i + "_Label").GetComponent<Text>().color = idx == codeCursor ? MenuTheme.Accent : MenuTheme.TextDim;
            }
            MenuTheme.StyledButton(parent, "CodePrev", "<", c, c, new Vector2(ContentX - 215f, y), new Vector2(76f, 68f), 38, () => { onlineCode = StepCodeChar(codeCursor, -1); RefreshCodeRow(); });
            MenuTheme.StyledButton(parent, "CodeNext", ">", c, c, new Vector2(ContentX + 356f, y), new Vector2(76f, 68f), 38, () => { onlineCode = StepCodeChar(codeCursor, 1); RefreshCodeRow(); });
        }

        string StepCodeChar(int pos, int delta)
        {
            var arr = onlineCode.ToCharArray();
            int idx = CodeAlphabet.IndexOf(arr[pos]);
            if (idx < 0) idx = 0;
            idx = (idx + delta % CodeAlphabet.Length + CodeAlphabet.Length) % CodeAlphabet.Length;
            arr[pos] = CodeAlphabet[idx];
            return new string(arr);
        }

        void RefreshCodeRow()
        {
            if (current == null) return;
            for (int i = 0; i < onlineCode.Length; i++)
            {
                var cell = current.Find("CodeCell" + i);
                if (cell == null) continue;
                var label = cell.Find("CodeCell" + i + "_Label").GetComponent<Text>();
                label.text = onlineCode[i].ToString();
                label.color = i == codeCursor ? MenuTheme.Accent : MenuTheme.TextDim;
            }
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
            UIFactory.Panel("LobbyBox", s, c, c, new Vector2(ContentX, 110f), new Vector2(1100f, 360f), new Color(1f, 1f, 1f, 0.05f));
            lobbyList = UIFactory.Rect("LobbyList", s, c, c, new Vector2(0.5f, 0.5f), new Vector2(ContentX, 110f), new Vector2(1040f, 340f)).transform;
            var b = new Vector2(0.5f, 0f);
            lobbyReadyBtn = MenuTheme.StyledButton(s, "LobbyReady", "PRONTO", b, b, new Vector2(-400f, 40f), new Vector2(360f, 100f), 36, ToggleReady);
            lobbyStartBtn = MenuTheme.StyledButton(s, "LobbyStart", "COMEÇAR", b, b, new Vector2(0f, 40f), new Vector2(360f, 100f), 36, () => OnlineClient.StartRace());
            lobbyStartBtn.interactable = false;
            MenuTheme.StyledButton(s, "LobbyBack", "VOLTAR", b, b, new Vector2(400f, 40f), new Vector2(360f, 100f), 36, LeaveLobby);
            RefreshLobby();
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
            "desert" => TrackId.DesertCanyon,
            "city" => TrackId.NightCity,
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
            OptionRow(s, "Voltas", row++, () => settings.laps.ToString(), d => settings.laps = StepIn(RaceSettings.LapOptions, settings.laps, d));
            OptionRow(s, "Adversários", row++, () => settings.totalCars == RaceSettings.ClassicCarCount ? "Clássico (8)" : settings.totalCars.ToString(),
                d => settings.totalCars = StepIn(RaceSettings.CarCountOptions, settings.totalCars, d));
            OptionRow(s, "Dificuldade", row++, () => DifficultyNames[(int)settings.difficulty], d => settings.difficulty = (Difficulty)Step((int)settings.difficulty, d, 4));
            OptionRow(s, "Categoria", row++, () => settings.formulaOnly ? "F1" : "GT",
                d =>
                {
                    settings.formulaOnly = !settings.formulaOnly;
                    if (settings.formulaOnly) { settings.players[0].car = CarModel.F1; settings.players[1].car = CarModel.F1; }
                    else { settings.players[0].car = CarModel.Titan; settings.players[1].car = CarModel.Titan; }
                    RefreshShowroomCar();
                });

            if (!split) OptionRow(s, "Câmera", row++, () => CameraNames[(int)settings.players[0].startCamera], d => settings.players[0].startCamera = (CameraView)Step((int)settings.players[0].startCamera, d, 4));

            int players = split ? 2 : 1;
            for (int p = 0; p < players; p++)
            {
                var ps = settings.players[p];
                string tag = split ? $"J{p + 1} " : "";
                OptionRow(s, tag + "Carro", row++, () => CarLabel(ps.car), d =>
                {
                    if (settings.formulaOnly) { ps.car = CarModel.F1; RefreshShowroomCar(); return; }
                    ps.car = (CarModel)Step((int)ps.car, d, ModelCount(settings));
                    RefreshShowroomCar();
                });
                OptionRow(s, tag + "Cor", row++, () => CarPalette.Names[ps.colorIndex], d =>
                {
                    ps.colorIndex = Step(ps.colorIndex, d, CarPalette.Colors.Length);
                    if (split && settings.players[0].colorIndex == settings.players[1].colorIndex) ps.colorIndex = Step(ps.colorIndex, d, CarPalette.Colors.Length);
                    RefreshShowroomCar();
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
                    () => { garageIndex = (int)cm; settings.players[0].car = cm; RefreshShowroomCar(); ShowGarage(); });
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
            RefreshShowroomCar();
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
