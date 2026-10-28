using RaceX.Car;
using RaceX.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    public class HUD : MonoBehaviour
    {
        static readonly Color Gold = new Color32(255, 193, 7, 255);
        static readonly Color Silver = new Color32(192, 197, 206, 255);
        static readonly Color Bronze = new Color32(176, 110, 44, 255);

        public RaceManager race;
        public RaceEntry entry;
        public CarCamera carCamera;
        public PauseMenu pauseMenu;
        public bool compact;

        Text lapText, timeText, positionText, speedText, gearText, countdownText, wrongWayText, messageText;
        Text tireText;
        Text danoText;
        Text fpsText;
        float fpsAcc;
        int fpsFrames, fpsLast = -1;
        float fpsMin = float.MaxValue;
        Image[] wheelImgs;      // painel CARRO: 4 rodas (desgaste verde/amarelo/vermelho)
        Image dmgFrontImg, dmgRearImg; // barras de dano frente/trás
        float lastPanelT;
        static Color ZoneColor(float d) => d < 0.05f ? new Color(0.25f, 0.85f, 0.35f) : d < 0.35f ? new Color(1f, 0.75f, 0.1f) : new Color(1f, 0.22f, 0.15f);
        UnityEngine.UI.Button pitButton;
        bool lastPitHighlighted;
        UnityEngine.UI.Button ffButton;
        bool fastForward;

        /// <summary>Acelera o tempo 5x (pula a minutagem restante do treino); de novo = volta a 1x.
        /// Normal = 0.01 (100 Hz, física ORIGINAL — o 0.02 degradava a fricção e limitava TODOS os carros);
        /// 5x = 0.02 (menos passos por frame, colisões estáveis na aceleração do tempo).</summary>
        void ToggleFastForward()
        {
            fastForward = !fastForward;
            float ts = fastForward ? 5f : 1f;
            Time.timeScale = ts;
            Time.fixedDeltaTime = fastForward ? 0.02f : 0.01f;
            Time.maximumDeltaTime = fastForward ? 0.12f : 0.05f; // cabe 5 passos de 0.01 por frame no normal
            if (fastForward) RaceX.Career.CareerMode.CompressSessionClock(5f); // relógio REAL da sessão também corre 5x
            // ESTADO VISÍVEL: o rótulo mostra o estado ATUAL do tempo — "1x" no normal, "5x" acelerado.
            if (ffButton != null)
            {
                var t = ffButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) { t.text = fastForward ? "5x" : "1x"; t.color = fastForward ? new Color(0.3f, 1f, 0.45f) : MenuTheme.TextDim; }
            }
        }
        Text pitInfoText;
        Text pitPlanText;
        Image[] rpmBands;
        Color[] rpmBandColors;
        float messageTimer, countdownTimer;
        float telemTimer; // cadência da linha de telemetria (5 Hz — string por frame engessava o jogo)
        RectTransform resultsPanel;

        // Cache de UI (otimização mobile): Text só é tocado quando o valor muda —
        // sem realloc de string nem rebuild de Canvas a cada frame.
        int lastLap = int.MinValue, lastPos = int.MinValue, lastSpeed = int.MinValue, lastGear = int.MinValue;
        int lastTimeBucket = int.MinValue, lastPitToken = int.MinValue;
        string lastTire;
        string lastPitPlan;
        bool radioStarted;
        int radioWindowStop = -1;  // parada cujo aviso "janela em 2 voltas" já foi dado
        int radioBoxLap = -1;      // volta em que o "BOX, BOX" já foi anunciado
        int radioWearStint = -1;   // stint em que "pneus no limite" já foi anunciado
        int lastStopsDone = -1;    // paradas do jogador no frame anterior (detecta pós-pit)
        float radioGapTimer;       // atrasa a diferença de posição para não cobrir a mensagem de volta

        public Image FadeImage { get; private set; }

        public static HUD Create(Canvas canvas, Rect viewport, RaceManager race, RaceEntry entry, CarCamera cam, PauseMenu pause, bool compact, bool raiseForTouch)
        {
            var root = UIFactory.Stretch("HUD_P" + (entry.playerIndex + 1), canvas.transform);
            UIFactory.ApplySafeArea(root, viewport);
            var hud = root.gameObject.AddComponent<HUD>();
            hud.race = race;
            hud.entry = entry;
            hud.carCamera = cam;
            hud.pauseMenu = pause;
            hud.compact = compact;
            hud.Build(root, raiseForTouch);
            return hud;
        }

        void Build(RectTransform root, bool raiseForTouch)
        {
            // Cena nova = tempo NORMAL (o 5x nunca vaza para a próxima sessão/tela).
            Time.timeScale = 1f;
            fastForward = false;
            var fade = UIFactory.Panel("Fade", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var fadeRt = (RectTransform)fade.transform;
            fadeRt.anchorMin = Vector2.zero; fadeRt.anchorMax = Vector2.one; fadeRt.sizeDelta = Vector2.zero;
            fade.raycastTarget = false;
            fade.enabled = false;
            FadeImage = fade;

            float s = compact ? 0.7f : 1f;
            var tl = new Vector2(0f, 1f);
            lapText = UIFactory.Label("Lap", root, "", (int)(44 * s), TextAnchor.UpperLeft, tl, tl, new Vector2(30f, -24f), new Vector2(500f, 60f), MenuTheme.TextDim);
            if (!compact)
                timeText = UIFactory.Label("Time", root, "", 30, TextAnchor.UpperLeft, tl, tl, new Vector2(30f, -80f), new Vector2(700f, 44f), MenuTheme.TextDim);

            var tc = new Vector2(0.5f, 1f);
            positionText = UIFactory.Label("Position", root, "", (int)(64 * s), TextAnchor.UpperCenter, tc, tc, new Vector2(0f, -18f), new Vector2(400f, 80f));
            positionText.color = MenuTheme.Text;
            var outline = positionText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);

            var tr = new Vector2(1f, 1f);
            MenuTheme.StyledButton(root, "CameraBtn", "CAM", tr, tr, new Vector2(-160f * s, -24f), new Vector2(110f * s, 100f * s), (int)(28 * s), () => carCamera?.NextView());
            MenuTheme.StyledButton(root, "PauseBtn", "II", tr, tr, new Vector2(-34f * s, -24f), new Vector2(100f * s, 100f * s), (int)(40 * s), () => pauseMenu?.Toggle());

            // Pit stop: botão SEMPRE criado; ativa quando o modo liga (HUD nasce antes do PitLaneSystem).
            pitButton = MenuTheme.StyledButton(root, "PitBtn", "PIT", tr, tr, new Vector2(-160f * s, -140f), new Vector2(110f * s, 90f * s), (int)(26 * s), () => { if (entry != null) RaceX.Core.PitLaneSystem.RequestPit(entry.car); });
            // Acelerar o TEMPO 5x (pula a minutagem do treino): aperta de novo e volta a 1x.
            // Posição y−250 (abaixo do PIT) para não invadir a coluna CAM/pausa do canto.
            ffButton = MenuTheme.StyledButton(root, "FFBtn", "1x", tr, tr, new Vector2(-34f * s, -140f), new Vector2(100f * s, 90f * s), (int)(28 * s), ToggleFastForward);
            tireText = UIFactory.Label("Tire", root, "", (int)(26 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -24f), new Vector2(240f * s, 40f), MenuTheme.TextDim);
            pitInfoText = UIFactory.Label("PitInfo", root, "", (int)(24 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -70f), new Vector2(240f * s, 36f), MenuTheme.Accent);
            // Estratégia sob o pneu: "PARADAS 0/1 · BOX V12" (quantas paradas já feitas e a próxima).
            pitPlanText = UIFactory.Label("PitPlan", root, "", (int)(22 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -112f), new Vector2(240f * s, 30f), MenuTheme.TextDim);

            // PAINEL CARRO (regra do usuário): silhueta com 4 rodas coloridas por DESGASTE
            // (verde/amarelo/vermelho) + barras de DANO da frente e da traseira.
            // Pivot (0.5, 0.5) em todos os elementos internos para o corpo nunca vazar para fora do painel.
            if (!compact)
            {
                var carPanel = UIFactory.Panel("CarPanel", root, tr, tr, new Vector2(-160f * s, -250f * s), new Vector2(150f * s, 225f * s), new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.88f));
                carPanel.sprite = MenuTheme.RoundedSprite(75, 112, 20);
                var topAnchor = new Vector2(0.5f, 1f);
                var centerPivot = new Vector2(0.5f, 0.5f);
                UIFactory.Label("CarTitle", carPanel.transform, "CARRO", (int)(17 * s), TextAnchor.MiddleCenter, topAnchor, topAnchor, new Vector2(0f, -10f * s), new Vector2(140f * s, 22f * s), MenuTheme.TextDim);
                dmgFrontImg = UIFactory.Panel("DmgF", carPanel.transform, topAnchor, centerPivot, new Vector2(0f, -44f * s), new Vector2(44f * s, 9f * s), Color.green);
                dmgFrontImg.sprite = MenuTheme.RoundedSprite(22, 5, 4);
                var m = entry.model;
                bool formula = m == RaceX.Car.CarModel.F1 || m == RaceX.Car.CarModel.F2008 || m == RaceX.Car.CarModel.F1Studio;
                var parts = formula
                    ? new[] { new Vector4(0f, -62f, 76f, 8f), new Vector4(0f, -84f, 12f, 36f), new Vector4(0f, -122f, 26f, 54f), new Vector4(0f, -134f, 44f, 34f), new Vector4(0f, -164f, 20f, 26f), new Vector4(0f, -184f, 62f, 10f) }
                    : new[] { new Vector4(0f, -122f, 52f, 128f), new Vector4(0f, -100f, 40f, 16f), new Vector4(0f, -126f, 38f, 30f), new Vector4(0f, -156f, 34f, 12f) };
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    var pv = parts[pi];
                    float a = formula ? (pi == 2 ? 0.26f : 0.18f) : (pi == 0 ? 0.16f : pi == 2 ? 0.08f : 0.3f);
                    var part = UIFactory.Panel("Body" + pi, carPanel.transform, topAnchor, centerPivot, new Vector2(pv.x, pv.y) * s, new Vector2(pv.z, pv.w) * s, new Color(1f, 1f, 1f, a));
                    part.sprite = MenuTheme.RoundedSprite(Mathf.Max(4, (int)pv.z / 2), Mathf.Max(4, (int)pv.w / 2), Mathf.Clamp((int)Mathf.Min(pv.z, pv.w) / 4, 1, 10));
                }
                if (formula)
                {
                    var cockpit = UIFactory.Panel("Cockpit", carPanel.transform, topAnchor, centerPivot, new Vector2(0f, -112f) * s, new Vector2(12f, 18f) * s, new Color(0f, 0f, 0f, 0.55f));
                    cockpit.sprite = MenuTheme.RoundedSprite(6, 9, 3);
                }
                dmgRearImg = UIFactory.Panel("DmgR", carPanel.transform, topAnchor, centerPivot, new Vector2(0f, -200f * s), new Vector2(44f * s, 9f * s), Color.green);
                dmgRearImg.sprite = MenuTheme.RoundedSprite(22, 5, 4);
                var wPos = new[] { new Vector2(-34f, -80f), new Vector2(34f, -80f), new Vector2(-34f, -164f), new Vector2(34f, -164f) };
                wheelImgs = new Image[4];
                for (int i = 0; i < 4; i++)
                {
                    var w = UIFactory.Panel("Wheel" + i, carPanel.transform, topAnchor, centerPivot, wPos[i] * s, new Vector2(18f * s, 28f * s), Color.green);
                    w.sprite = MenuTheme.RoundedSprite(9, 14, 5);
                    wheelImgs[i] = w;
                }
            }

            float y = raiseForTouch ? 280f : 30f;
            var brc = new Vector2(1f, 0f);
            var gauge = UIFactory.Panel("Gauge", root, brc, brc, new Vector2(-30f, y), new Vector2(360f * s, 170f * s), new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.88f));
            gauge.sprite = MenuTheme.RoundedSprite(180, 85, 26);
            gauge.type = Image.Type.Simple;
            speedText = UIFactory.Label("Speed", gauge.transform, "0", (int)(84 * s), TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-110f * s, -10f * s), new Vector2(220f * s, 100f * s), MenuTheme.Accent);
            UIFactory.Label("Unit", gauge.transform, "km/h", (int)(26 * s), TextAnchor.LowerLeft, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f * s, -78f * s), new Vector2(80f * s, 30f * s), MenuTheme.TextDim);
            gearText = UIFactory.Label("Gear", gauge.transform, "1", (int)(60 * s), TextAnchor.MiddleCenter, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f * s, -10f * s), new Vector2(80f * s, 100f * s), new Color(1f, 0.85f, 0.2f));
            // DANO (regra do usuário: dano existe → aparece na tela). Vermelho quando o carro está batido.
#if UNITY_EDITOR
            danoText = UIFactory.Label("Dano", gauge.transform, "", (int)(24 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-160f * s, -8f * s), new Vector2(140f * s, 30f * s), new Color(1f, 0.3f, 0.2f));
#endif
            var rpmBg = UIFactory.Panel("RpmBg", gauge.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f * s), new Vector2(320f * s, 22f * s), new Color(1f, 1f, 1f, 0.12f));
            rpmBands = new Image[3];
            rpmBandColors = new[] { new Color(0.2f, 0.85f, 0.4f), new Color(1f, 0.75f, 0.1f), new Color(1f, 0.2f, 0.15f) };
            float segW = 320f * s / 3f;
            for (int i = 0; i < 3; i++)
            {
                var seg = UIFactory.Panel("RpmSeg" + i, rpmBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(segW * i, 0f), new Vector2(segW, 22f * s), rpmBandColors[i]);
                seg.raycastTarget = false;
                rpmBands[i] = seg;
            }

            fpsText = UIFactory.Label("Fps", root, "", (int)(22 * s), TextAnchor.UpperCenter, tc, tc, new Vector2(0f, -96f * s), new Vector2(360f, 30f), MenuTheme.TextDim);
            var c = new Vector2(0.5f, 0.5f);
            countdownText = UIFactory.Label("Countdown", root, "", (int)(200 * s), TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(800f, 300f), new Color(1f, 0.9f, 0.2f));
            wrongWayText = UIFactory.Label("WrongWay", root, "CONTRAMÃO", (int)(90 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 160f * s), new Vector2(900f, 120f), new Color(1f, 0.15f, 0.1f));
            wrongWayText.enabled = false;
            messageText = UIFactory.Label("Message", root, "", (int)(48 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 260f * s), new Vector2(900f, 80f));

            race.CountdownTick += OnCountdown;
            race.LapCompleted += OnLap;
            race.RaceFinished += ShowResults;
            race.RaceStarted += OnRaceStartedRadio;
            MiniMap.Create(root, race, entry, compact);
            if (!compact)
            {
                BuildSectors(root);
                BuildTower(root);
            }
        }

        static readonly Color SecPurple = new Color(0.62f, 0.25f, 0.95f, 0.92f);
        static readonly Color SecGreen = new Color(0.2f, 0.8f, 0.3f, 0.92f);
        static readonly Color SecYellow = new Color(1f, 0.78f, 0.1f, 0.92f);
        static readonly Color SecNone = new Color(0.1f, 0.11f, 0.14f, 0.8f);
        Image[] secBg;
        Text[] secText;
        Text deltaText;
        Text[] secDelta;
        float secTimer;
        readonly Dictionary<RaceEntry, Vector3Int> towerGapKey = new Dictionary<RaceEntry, Vector3Int>();
        readonly Dictionary<RaceEntry, string> towerGapText = new Dictionary<RaceEntry, string>();
        RectTransform towerRoot;
        Image[] towerBg;
        Text[] towerPos, towerName, towerTyre, towerGap;
        float towerTimer;

        void BuildSectors(RectTransform root)
        {
            SectorTimer.Ensure(race);
            var tl = new Vector2(0f, 1f);
            secBg = new Image[3];
            secText = new Text[3];
            secDelta = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var bg = UIFactory.Panel("Sector" + (i + 1), root, tl, tl, new Vector2(270f + i * 172f, -128f), new Vector2(166f, 36f), SecNone);
                bg.sprite = MenuTheme.RoundedSprite(83, 18, 8);
                bg.raycastTarget = false;
                secBg[i] = bg;
                var t = UIFactory.Label("SectorTxt", bg.transform, "S" + (i + 1), 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 34f), Color.white);
                t.raycastTarget = false;
                secText[i] = t;
                var dl = UIFactory.Label("SectorDelta", bg.transform, "", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -15f), new Vector2(166f, 26f), Color.white);
                dl.raycastTarget = false;
                secDelta[i] = dl;
            }
            deltaText = UIFactory.Label("Delta", root, "", 26, TextAnchor.UpperLeft, tl, tl, new Vector2(270f, -200f), new Vector2(500f, 34f), Color.white);
            deltaText.raycastTarget = false;
        }

        void UpdateSectors()
        {
            if (secBg == null || SectorTimer.Instance == null) return;
            secTimer -= Time.unscaledDeltaTime;
            if (secTimer > 0f) return;
            secTimer = 0.1f;
            var st = SectorTimer.Instance;
            int cur = race.State == RaceState.Racing && !entry.finished ? st.CurrentSector(entry) : -1;
            float acc = 0f, pbAcc = 0f;
            bool anyDelta = false;
            for (int k = 0; k < 3; k++)
            {
                string txt;
                Color col;
                float done = st.LapSectorTime(entry, k);
                string dTxt = "";
                Color dCol = Color.white;
                if (k == cur && done <= 0f)
                {
                    txt = "S" + (k + 1) + "  " + st.CurrentSectorElapsed(entry).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    col = SecNone;
                }
                else
                {
                    float shown = done > 0f ? done : st.LastSectorTime(entry, k);
                    txt = shown > 0f ? "S" + (k + 1) + "  " + shown.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "S" + (k + 1) + "  --.---";
                    var c = st.ColorOf(entry, k);
                    col = c == SectorTimer.SectorColor.Purple ? SecPurple : c == SectorTimer.SectorColor.Green ? SecGreen : c == SectorTimer.SectorColor.Yellow ? SecYellow : SecNone;
                    if (shown > 0f && st.SectorDelta(entry, k, out float sd))
                    {
                        dTxt = (sd >= 0f ? "+" : "-") + Mathf.Abs(sd).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                        dCol = sd < 0f ? new Color(0.3f, 1f, 0.45f) : sd > 0.0005f ? new Color(1f, 0.82f, 0.2f) : Color.white;
                    }
                }
                if (secDelta[k].text != dTxt) secDelta[k].text = dTxt;
                secDelta[k].color = dCol;
                if (secText[k].text != txt) secText[k].text = txt;
                if (secBg[k].color != col) secBg[k].color = col;
                secText[k].color = col == SecYellow ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
                float pb = st.PersonalBest(entry, k);
                if (done > 0f && pb > 0f) { acc += done; pbAcc += pb; anyDelta = true; }
            }
            string dtx = "";
            if (anyDelta)
            {
                float d = acc - pbAcc;
                dtx = "DELTA " + (d >= 0f ? "+" : "-") + Mathf.Abs(d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                deltaText.color = d > 0.0005f ? new Color(1f, 0.3f, 0.25f) : new Color(0.3f, 1f, 0.45f);
            }
            if (deltaText.text != dtx) deltaText.text = dtx;
        }

        void BuildTower(RectTransform root)
        {
            var ml = new Vector2(0f, 0.5f);
            towerRoot = UIFactory.Stretch("Tower", root);
            towerRoot.anchorMin = ml; towerRoot.anchorMax = ml; towerRoot.pivot = ml;
            towerRoot.anchoredPosition = new Vector2(30f, -60f);
            towerRoot.sizeDelta = new Vector2(400f, 5 * 40f);
            towerBg = new Image[5];
            towerPos = new Text[5]; towerName = new Text[5]; towerTyre = new Text[5]; towerGap = new Text[5];
            var tl = new Vector2(0f, 1f);
            for (int i = 0; i < 5; i++)
            {
                var bg = UIFactory.Panel("Row" + i, towerRoot, tl, tl, new Vector2(0f, -i * 40f), new Vector2(400f, 36f), SecNone);
                bg.sprite = MenuTheme.RoundedSprite(100, 18, 6);
                bg.raycastTarget = false;
                towerBg[i] = bg;
                towerPos[i] = UIFactory.Label("Pos", bg.transform, "", 22, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(54f, 34f), Color.white);
                towerName[i] = UIFactory.Label("Name", bg.transform, "", 22, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(180f, 34f), Color.white);
                towerTyre[i] = UIFactory.Label("Tyre", bg.transform, "", 22, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(246f, 0f), new Vector2(30f, 34f), Color.white);
                towerGap[i] = UIFactory.Label("Gap", bg.transform, "", 22, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(120f, 34f), Color.white);
                foreach (var t in new[] { towerPos[i], towerName[i], towerTyre[i], towerGap[i] }) t.raycastTarget = false;
            }
        }

        static void SetText(Text t, string s) { if (t.text != s) t.text = s; }

        void UpdateTower()
        {
            if (towerRoot == null) return;
            towerTimer -= Time.unscaledDeltaTime;
            if (towerTimer > 0f) return;
            towerTimer = 0.25f;
            var list = race.entries;
            bool show = list.Count >= 2;
            if (towerRoot.gameObject.activeSelf != show) towerRoot.gameObject.SetActive(show);
            if (!show) return;
            int me = list.IndexOf(entry);
            if (me < 0) { towerRoot.gameObject.SetActive(false); return; }
            int first = Mathf.Clamp(me - 2, 0, Mathf.Max(0, list.Count - 5));
            bool timed = race.Settings.laps >= 900;
            float lapLen = race.Track != null && race.Track.Waypoints != null ? race.Track.Waypoints.Length : 0f;
            var st = SectorTimer.Instance;
            for (int r = 0; r < 5; r++)
            {
                int idx = first + r;
                bool used = idx < list.Count;
                if (towerBg[r].gameObject.activeSelf != used) towerBg[r].gameObject.SetActive(used);
                if (!used) continue;
                var e = list[idx];
                bool isMe = e == entry;
                towerBg[r].color = isMe ? new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.55f) : SecNone;
                SetText(towerPos[r], "P" + (idx + 1));
                string nm = string.IsNullOrEmpty(e.name) ? "-" : e.name.ToUpperInvariant();
                SetText(towerName[r], nm.Length > 12 ? nm.Substring(0, 12) : nm);
                int cp = e.car != null && race.Settings.pitStops ? RaceX.Core.PitLaneSystem.CompoundOf(e.car) : -1;
                SetText(towerTyre[r], cp == 0 ? "S" : cp == 1 ? "M" : cp == 2 ? "H" : "");
                towerTyre[r].color = cp == 0 ? new Color(1f, 0.25f, 0.2f) : cp == 1 ? new Color(1f, 0.85f, 0.15f) : Color.white;
                string gap;
                if (timed)
                {
                    if (isMe) gap = e.bestLap > 0f ? FormatTime(e.bestLap) : "VOCÊ";
                    else if (e.bestLap > 0f && entry.bestLap > 0f)
                    {
                        float d = e.bestLap - entry.bestLap;
                        gap = (d >= 0f ? "+" : "-") + Mathf.Abs(d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else gap = e.bestLap > 0f ? FormatTime(e.bestLap) : "--:--.---";
                }
                else if (isMe) gap = "VOCÊ";
                else
                {
                    bool ahead = idx < me;
                    float dd = ahead ? e.raceDistance - entry.raceDistance : entry.raceDistance - e.raceDistance;
                    if (lapLen > 1f && dd >= lapLen)
                    {
                        int lapsDown = Mathf.FloorToInt(dd / lapLen);
                        gap = (ahead ? "-" : "+") + lapsDown + (lapsDown == 1 ? " VOLTA" : " VOLTAS");
                    }
                    else
                    {
                        // Gap só muda quando ele ou eu cruzamos um ponto de setor.
                        var key = new Vector3Int(st != null ? st.CurrentSector(e) : -1, st != null ? st.CurrentSector(entry) : -1, ahead ? 1 : 0);
                        if (!towerGapKey.TryGetValue(e, out var oldKey) || oldKey != key || !towerGapText.TryGetValue(e, out gap))
                        {
                            float g = st != null ? (ahead ? st.GapSeconds(e, entry) : st.GapSeconds(entry, e)) : 0f;
                            gap = (ahead ? "-" : "+") + g.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                            towerGapKey[e] = key;
                            towerGapText[e] = gap;
                        }
                    }
                }
                SetText(towerGap[r], gap);
            }
        }

        void OnDestroy()
        {
            if (race == null) return;
            race.CountdownTick -= OnCountdown;
            race.LapCompleted -= OnLap;
            race.RaceFinished -= ShowResults;
            race.RaceStarted -= OnRaceStartedRadio;
        }

        void OnRaceStartedRadio()
        {
            radioStarted = false;
            radioWindowStop = -1;
            radioBoxLap = -1;
            radioWearStint = -1;
            lastStopsDone = -1;
        }

        void OnCountdown(int value)
        {
            countdownText.text = value > 0 ? value + "..." : "GO!";
            countdownTimer = value > 0 ? 10f : 1f;
        }

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e != entry) return;
            messageText.text = (best ? "MELHOR VOLTA  " : "VOLTA  ") + FormatTime(lapTime);
            messageTimer = 3f;
            if (e.finished) { messageText.text = "CHEGADA!  " + e.position + "º"; messageTimer = 5f; }
        }

        bool resultsHidden;

        /// <summary>Painel de resultados está OCULTO (pausa pode abrir mesmo com a corrida encerrada).</summary>
        public bool ResultsHidden => resultsHidden;

        /// <summary>Algum HUD da cena está com o painel de resultados oculto (pós-CONTINUAR)?
        /// Com resultados ocultos a PAUSA volta a funcionar mesmo com RaceState.Finished.</summary>
        public static bool AnyResultsHidden()
        {
            foreach (var h in UnityEngine.Object.FindObjectsOfType<HUD>())
                if (h != null && h.resultsHidden) return true;
            return false;
        }

        /// <summary>Reabre o painel de resultados (hub da carreira fechou como overlay).</summary>
        void ReopenResults()
        {
            if (resultsPanel == null) return;
            resultsHidden = false;
            resultsPanel.gameObject.SetActive(true);
        }

        void Update()
        {
            if (resultsPanel != null && resultsHidden && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                resultsHidden = false;
                resultsPanel.gameObject.SetActive(true);
                return;
            }
            UpdateFps();
            if (entry == null || entry.car == null) return;
            var car = entry.car;
            var progress = race.GetProgress(entry);
            int laps = race.Settings.laps;

            int lapNo = entry.CurrentLap(laps, progress);
            if (lapNo != lastLap)
            {
                lastLap = lapNo;
                lapText.text = laps >= 900 ? $"Volta {lapNo}" : $"Volta {lapNo} / {laps}";
                // Rádio: diferença para o carro da frente no fim da volta (atrasa p/ não cobrir a volta).
                if (lapNo > 1 && SettingsManager.PitRadio > 0) radioGapTimer = 1.5f;
            }

            // Rádio: mensagem de estratégia uma vez na largada (somente corridas com pit ativo).
            if (!radioStarted && race.State == RaceState.Racing)
            {
                radioStarted = true;
                if (SettingsManager.PitRadio > 0 && race.Settings.laps < 900 && RaceX.Core.PitLaneSystem.PitEnabled)
                    ShowRadio("Estratégia: " + RaceX.Core.PitLaneSystem.StrategyText(car));
            }
            if (timeText != null)
            {
                float current = race.State == RaceState.Racing && !entry.finished ? Time.time - entry.lapStartTime : 0f;
                // Relógio resolve a string no máx. a cada 0,1 s (não toda frame).
                int bucket = Mathf.FloorToInt(current * 10f);
                if (bucket != lastTimeBucket)
                {
                    lastTimeBucket = bucket;
                    string sess = "";
                    float left = race.SessionTimeLeft;
                    if (left >= 0f) sess = "  |  FIM DA SESSÃO EM " + FormatTime(left);
                    timeText.text = FormatTime(current) + (entry.bestLap > 0f ? "  ·  Melhor " + FormatTime(entry.bestLap) : "") + sess;
                }
            }
            if (entry.position != lastPos)
            {
                lastPos = entry.position;
                positionText.text = $"{entry.position}º / {race.entries.Count}";
            }

            UpdateSectors();
            UpdateTower();

            int kmh = Mathf.RoundToInt(car.SpeedKmh);
            if (kmh != lastSpeed)
            {
                lastSpeed = kmh;
                speedText.text = kmh.ToString();
            }
            // PAINEL CARRO (5 Hz): rodas por DESGASTE (verde/amarelo/vermelho) + barras de DANO frente/trás.
            if (wheelImgs != null && Time.time - lastPanelT > 0.2f)
            {
                lastPanelT = Time.time;
                for (int i = 0; i < 4; i++)
                {
                    float w = RaceX.Core.PitLaneSystem.WearOf(car, i);
                    wheelImgs[i].color = w < 0.4f ? new Color(0.25f, 0.85f, 0.35f) : w < 0.7f ? new Color(1f, 0.75f, 0.1f) : new Color(1f, 0.22f, 0.15f);
                }
                dmgFrontImg.color = ZoneColor(car.DmgFront);
                dmgRearImg.color = ZoneColor(car.DmgRear);
            }
            // TELEMETRIA ao vivo (dados reais para diagnóstico — substitui o antigo "DANO 87%").
            // A 5 Hz (não por frame): montar string toda frame era lixo de GC e engessava o jogo.
            if (danoText != null)
            {
                telemTimer -= Time.unscaledDeltaTime;
                if (telemTimer <= 0f)
                {
                    telemTimer = 0.2f;
                    float df = car.DamageFactor;
                    string dtx = $"M{car.Gear}{(car.IsShifting ? "*" : "")} {Mathf.RoundToInt(car.Rpm)}/{Mathf.RoundToInt(car.spec.rpmLimit)}" +
                                 $" | ACEL {(car.pitControlled ? car.extThrottle : car.throttleInput):0.00}" +
                                 $" | FRE {(car.inputLocked ? 1f : (car.pitControlled ? Mathf.Max(car.extBrake, car.brakeInput) : car.brakeInput)):0.00}" +
                                 (car.handbrake && !car.inputLocked ? " | F.MÃO" : "") +
                                 (car.inputLocked ? " | LOCK" : "") +
                                 (car.pitControlled ? " [AUTO]" : "") +
                                 (df < 0.999f ? $" | DANO {Mathf.RoundToInt(df * 100f)}%" : "") +
                                 (car.MaxWheelSlip > 0.4f ? $" | PAT {car.MaxWheelSlip:0.00}" : "") +
                                 (car.IsOffTrack ? " | GRAMA" : "") +
                                 (Time.timeScale > 1.5f ? $" | {Time.timeScale:0.#}x" : "");
                    if (danoText.text != dtx) danoText.text = dtx;
                }
            }
            if (car.Gear != lastGear)
            {
                lastGear = car.Gear;
                gearText.text = car.IsReversing ? "R" : car.Gear.ToString();
            }
            if (tireText != null)
            {
                string tw = RaceX.Core.PitLaneSystem.WearText(car);
                if (tw != lastTire)
                {
                    lastTire = tw;
                    tireText.text = tw;
                }
            }
            // Botão PIT + rota: visíveis apenas com o modo ativo; aviso de distância até a entrada do pit.
            if (pitButton != null)
            {
                bool pitOn = RaceX.Core.PitLaneSystem.Instance != null && RaceX.Core.PitLaneSystem.PitEnabled;
                pitButton.gameObject.SetActive(pitOn);
                bool pitHighlighted = pitOn && entry != null && RaceX.Core.PitLaneSystem.IsPitActiveOrRequested(entry.car);
                if (pitHighlighted || pitHighlighted != lastPitHighlighted)
                {
                    lastPitHighlighted = pitHighlighted;
                    if (pitButton.image != null)
                        pitButton.image.color = pitHighlighted ? MenuTheme.Accent : MenuTheme.Panel;
                    var pitLbl = pitButton.GetComponentInChildren<Text>();
                    if (pitLbl != null)
                    {
                        pitLbl.text = pitHighlighted ? "PIT \u2713" : "PIT";
                        pitLbl.color = pitHighlighted ? new Color(0.06f, 0.06f, 0.08f, 1f) : MenuTheme.Text;
                    }
                }
                if (pitInfoText != null)
                {
                    // token: -3 sem pit, -2 no pit (autopilot), -1 sem janela, >=0 distância em m.
                    float d = pitOn ? RaceX.Core.PitLaneSystem.DistanceToPitEntry(car) : 0f;
                    if (pitOn) UpdatePitRadio(car, d, lapNo);
                    int token = !pitOn ? -3
                        : entry != null && entry.car != null && entry.car.pitControlled ? -2
                        : d < 0f ? -1 : Mathf.RoundToInt(d);
                    if (token != lastPitToken)
                    {
                        lastPitToken = token;
                        pitInfoText.text = token == -3 ? ""
                            : token == -2 ? "NO PIT — SIGA A LINHA"
                            : token == -1 ? "PIT: ENTRE NA FAIXA LARANJA"
                            : $"PIT A {token} m — LINHA LARANJA";
                    }
                }
                if (pitPlanText != null)
                {
                    string plan = pitOn ? RaceX.Core.PitLaneSystem.PitPlanText(car) : "";
                    if (plan != lastPitPlan) { lastPitPlan = plan; pitPlanText.text = plan; }
                }
            }
            float rpm01 = car.spec != null ? Mathf.InverseLerp(0f, car.spec.rpmLimit, car.Rpm) : 0f;
            SetBand(0, rpm01 > 0.12f);
            SetBand(1, rpm01 > 0.66f);
            SetBand(2, rpm01 > 0.83f);

            wrongWayText.enabled = progress != null && progress.WrongWay && race.State == RaceState.Racing;

            if (countdownTimer > 0f)
            {
                countdownTimer -= Time.deltaTime;
                if (countdownTimer <= 0f) countdownText.text = "";
            }
            if (messageTimer > 0f)
            {
                messageTimer -= Time.deltaTime;
                if (messageTimer <= 0f) messageText.text = "";
            }
            // Rádio: diferença para o carro da frente (uma vez por volta, depois da mensagem de volta).
            if (radioGapTimer > 0f)
            {
                radioGapTimer -= Time.deltaTime;
                if (radioGapTimer <= 0f && SettingsManager.PitRadio > 0 && race.Settings.laps < 900
                    && RaceX.Core.PitLaneSystem.PitEnabled && entry.position > 1 && SectorTimer.Instance != null)
                {
                    var ahead = race.entries[Mathf.Max(0, entry.position - 2)];
                    float gap = SectorTimer.Instance.GapSeconds(ahead, entry);
                    if (gap > 0f)
                        ShowRadio("P" + entry.position + ", +" + gap.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s para o P" + (entry.position - 1));
                }
            }
        }

        void UpdateFps()
        {
            if (fpsText == null) return;
            if (!SettingsManager.ShowFps)
            {
                if (fpsLast != -2) { fpsLast = -2; fpsText.text = ""; }
                return;
            }
            float dt = Time.unscaledDeltaTime;
            fpsAcc += dt;
            fpsFrames++;
            if (dt > 0f) fpsMin = Mathf.Min(fpsMin, 1f / dt);
            if (fpsAcc < 0.5f) return;
            int fps = Mathf.RoundToInt(fpsFrames / fpsAcc);
            int mn = Mathf.RoundToInt(fpsMin);
            int token = fps * 1000 + mn;
            if (token != fpsLast)
            {
                fpsLast = token;
                fpsText.text = fps + " FPS (min " + mn + ")";
            }
            fpsAcc = 0f; fpsFrames = 0; fpsMin = float.MaxValue;
        }

        void ShowRadio(string msg)
        {
            messageText.text = "RÁDIO: " + msg;
            messageTimer = 4f;
        }

        void UpdatePitRadio(RaceX.Car.CarController car, float distToEntry, int lapNo)
        {
            if (car == null || SettingsManager.PitRadio == 0 || race == null || race.Settings.laps >= 900)
            {
                lastStopsDone = -1;
                return;
            }
            // Pós-pit: anuncia o composto novo e o que vem pela frente (ou o fim das paradas).
            int stopsDone = RaceX.Core.PitLaneSystem.StopsDone(car);
            if (lastStopsDone >= 0 && stopsDone > lastStopsDone)
            {
                int next = RaceX.Core.PitLaneSystem.NextStopLap(car);
                int c = RaceX.Core.PitLaneSystem.Compound(car);
                if (next > 0) ShowRadio("Pneus " + RaceX.Core.PitLaneSystem.CompoundLetter(c) + ", próxima parada na volta " + next);
                else ShowRadio("Pneus " + RaceX.Core.PitLaneSystem.CompoundLetter(c) + ", sem mais paradas");
                radioWearStint = stopsDone;
            }
            lastStopsDone = stopsDone;
            int plannedLap = RaceX.Core.PitLaneSystem.NextStopLap(car);
            if (plannedLap <= 0) return;
            // Janela de box: avisa 2 voltas antes da parada planejada.
            if (lapNo == plannedLap - 2 && radioWindowStop != plannedLap)
            {
                radioWindowStop = plannedLap;
                ShowRadio("Janela de box em 2 voltas");
                return;
            }
            // Na volta da parada, a ~600 m da entrada do pit: chama o piloto.
            if (lapNo == plannedLap && distToEntry >= 0f && distToEntry < 600f && radioBoxLap != lapNo)
            {
                radioBoxLap = lapNo;
                ShowRadio("Box, box! Entre nesta volta");
                return;
            }
            // Pneus abaixo de 25% de borracha restante: aviso de limite do composto.
            if (RaceX.Core.PitLaneSystem.Wear(car) > 0.75f && radioWearStint != stopsDone)
            {
                radioWearStint = stopsDone;
                ShowRadio("Pneus no limite");
            }
        }

        void SetBand(int index, bool on)
        {
            if (rpmBands == null || index >= rpmBands.Length || rpmBands[index] == null) return;
            rpmBands[index].color = on ? rpmBandColors[index] : rpmBandColors[index] * 0.12f;
        }

        void ShowResults()
        {
            if (carCamera != null) carCamera.ForceThirdPerson();
            if (resultsPanel != null || entry.playerIndex > 0) return;
            // Hub/menu da carreira aberto quando a sessão terminou? Fecha em silêncio para o
            // painel de resultados APARECER (a fase já avança no OnCareerSessionFinished abaixo).
            if (RaceX.UI.CareerFlow.IsOpen) RaceX.UI.CareerFlow.ForceCloseSilent();
            var canvas = GetComponentInParent<Canvas>().transform;
            var c = new Vector2(0.5f, 0.5f);
            var panel = UIFactory.Panel("Results", canvas, c, c, Vector2.zero, new Vector2(1200f, 820f), MenuTheme.Accent);
            panel.sprite = MenuTheme.RoundedSprite(300, 205, 24);
            panel.type = Image.Type.Simple;
            var inner = UIFactory.Panel("ResultsInner", panel.transform, c, c, Vector2.zero, new Vector2(1192f, 812f), new Color(0.03f, 0.05f, 0.09f, 0.96f));
            inner.sprite = MenuTheme.RoundedSprite(298, 203, 22);
            inner.type = Image.Type.Simple;
            resultsPanel = (RectTransform)panel.transform;
            MenuTheme.Header((RectTransform)inner.transform, "RESULTADOS");

            var standings = race.FinalStandings();
            int myPos = standings.FindIndex(x => x == entry) + 1;
            bool careerRace = RaceX.Career.CareerData.raceActive && race.Settings != null && race.Settings.careerPhase >= 0;
            int careerPhase = careerRace ? RaceX.Career.CareerMode.CurrentPhase : -1;
            if (careerPhase >= 0)
            {
                string title = RaceX.Career.CareerMode.SessionTitle(careerPhase);
                var htr = (RectTransform)inner.transform;
                var hl = UIFactory.Label("SessionTitle", htr, title + (careerPhase == RaceX.Career.CareerMode.PH_Q3 || careerPhase == RaceX.Career.CareerMode.PH_SQ3 ? " — DEFINE O GRID" : ""), 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(1000f, 34f), MenuTheme.TextDim);
                ((RectTransform)hl.transform).anchoredPosition = new Vector2(0f, -108f);
            }
            if (myPos > 0 && careerPhase == RaceX.Career.CareerMode.PH_RACE) CareerStats.OnRaceFinished(myPos, standings.Count);
            var order = new List<string>();
            for (int i = 0; i < standings.Count; i++) order.Add(standings[i].name);
            if (careerRace) RaceX.Career.CareerMode.OnCareerSessionFinished(myPos, standings.Count, order);
            bool realRace = race.Settings.laps < 900 && (!careerRace || careerPhase == RaceX.Career.CareerMode.PH_RACE || careerPhase == RaceX.Career.CareerMode.PH_SPRINT);
            if (realRace && entry.finished) PodiumCelebration.Play(resultsPanel, myPos);
            for (int i = 0; i < standings.Count && i < 16; i++)
            {
                var e = standings[i];
                string total = e.finished ? FormatTime(e.totalTime) : "--:--.---";
                string best = e.bestLap > 0f ? FormatTime(e.bestLap) : "--:--.---";
                string line = $"{i + 1,2}º   {e.name,-16}  {e.model,-7}  {total}   {best}";
                Color rowColor = i == 0 ? Gold : (i == 1 ? Silver : (i == 2 ? Bronze : MenuTheme.Text));
                if (e.isHuman)
                {
                    var hl = UIFactory.Panel("HumanBg" + i, inner.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -150f - i * 34f), new Vector2(1048f, 34f), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.14f));
                    hl.raycastTarget = false;
                }
                UIFactory.Label("Row" + i, inner.transform, line, 30, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(80f, -150f - i * 34f), new Vector2(1040f, 34f), rowColor);
            }

            var b = new Vector2(0.5f, 0f);
            var innerRt = (RectTransform)inner.transform;
            if (!careerRace)
                MenuTheme.StyledButton(innerRt, "Again", "JOGAR NOVAMENTE", b, new Vector2(1f, 0f), new Vector2(-390f, 30f), new Vector2(380f, 86f), 30, () => SceneLoader.ReloadCurrent());
            if (careerRace)
            {
                // Carreira: CONTINUAR esconde a tabela e abre o HUB DA CARREIRA como OVERLAY
                // (timeScale 0 enquanto aberto — sessão acabou, congelar é seguro). Ao fechar o hub a
                // tabela CONTINUA oculta: ESC reabre os resultados e o II (pausa) volta a funcionar
                // (bug v77: o hub não aparecia e a pausa morria depois que a sessão terminava).
                MenuTheme.StyledButton(innerRt, "Continue", "CONTINUAR", b, new Vector2(0f, 0f), new Vector2(0f, 30f), new Vector2(380f, 86f), 30, () =>
                {
                    if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
                    resultsHidden = true;
                    Time.timeScale = 0f;
                    RaceX.UI.CareerFlow.Open(() => Time.timeScale = 1f);
                });
                MenuTheme.StyledButton(innerRt, "ToHub", "CLASSIFICAÇÃO/HUB", b, new Vector2(1f, 0f), new Vector2(-390f, 30f), new Vector2(380f, 86f), 30, () =>
                {
                    // Pausa a corrida e abre o hub como OVERLAY: VOLTAR no hub reabre este painel.
                    Time.timeScale = 0f;
                    RaceX.UI.CareerFlow.Open(() =>
                    {
                        Time.timeScale = 1f;
                        ReopenResults();
                    });
                });
                // Sem MENU PRINCIPAL na carreira: a saída é pelo hub da carreira.
            }
            else
            {
                MenuTheme.StyledButton(innerRt, "Continue", "CONTINUAR", b, new Vector2(0f, 0f), new Vector2(390f, 30f), new Vector2(380f, 86f), 30, () => { if (resultsPanel != null) resultsPanel.gameObject.SetActive(false); resultsHidden = true; });
                MenuTheme.StyledButton(innerRt, "Menu", "MENU PRINCIPAL", b, new Vector2(0f, 0f), new Vector2(390f, 128f), new Vector2(380f, 86f), 30, () => SceneLoader.LoadMainMenu());
            }
            MenuTheme.StyledButton(innerRt, "Replay", "REPLAY", b, new Vector2(1f, 0f), new Vector2(-390f, 128f), new Vector2(380f, 86f), 30, () => RaceX.Core.ReplaySystem.Begin(race, carCamera, resultsPanel));
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m + ":" + s.ToString("00.000", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
