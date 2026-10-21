using RaceX.Car;
using RaceX.Core;
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
        Image[] rpmBands;
        Color[] rpmBandColors;
        float messageTimer, countdownTimer;
        RectTransform resultsPanel;

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

            float y = raiseForTouch ? 280f : 30f;
            var brc = new Vector2(1f, 0f);
            var gauge = UIFactory.Panel("Gauge", root, brc, brc, new Vector2(-30f, y), new Vector2(360f * s, 170f * s), new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.88f));
            gauge.sprite = MenuTheme.RoundedSprite(180, 85, 26);
            gauge.type = Image.Type.Simple;
            speedText = UIFactory.Label("Speed", gauge.transform, "0", (int)(84 * s), TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-110f * s, -10f * s), new Vector2(220f * s, 100f * s), MenuTheme.Accent);
            UIFactory.Label("Unit", gauge.transform, "km/h", (int)(26 * s), TextAnchor.LowerLeft, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f * s, -78f * s), new Vector2(80f * s, 30f * s), MenuTheme.TextDim);
            gearText = UIFactory.Label("Gear", gauge.transform, "1", (int)(60 * s), TextAnchor.MiddleCenter, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f * s, -10f * s), new Vector2(80f * s, 100f * s), new Color(1f, 0.85f, 0.2f));
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

            var c = new Vector2(0.5f, 0.5f);
            countdownText = UIFactory.Label("Countdown", root, "", (int)(200 * s), TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(800f, 300f), new Color(1f, 0.9f, 0.2f));
            wrongWayText = UIFactory.Label("WrongWay", root, "CONTRAMÃO", (int)(90 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 160f * s), new Vector2(900f, 120f), new Color(1f, 0.15f, 0.1f));
            wrongWayText.enabled = false;
            messageText = UIFactory.Label("Message", root, "", (int)(48 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 260f * s), new Vector2(900f, 80f));

            race.CountdownTick += OnCountdown;
            race.LapCompleted += OnLap;
            race.RaceFinished += ShowResults;
            MiniMap.Create(root, race, entry, compact);
        }

        void OnDestroy()
        {
            if (race == null) return;
            race.CountdownTick -= OnCountdown;
            race.LapCompleted -= OnLap;
            race.RaceFinished -= ShowResults;
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

        void Update()
        {
            if (resultsPanel != null && resultsHidden && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                resultsHidden = false;
                resultsPanel.gameObject.SetActive(true);
                return;
            }
            if (entry == null || entry.car == null) return;
            var car = entry.car;
            var progress = race.GetProgress(entry);
            int laps = race.Settings.laps;

            lapText.text = $"Volta {entry.CurrentLap(laps, progress)} / {laps}";
            if (timeText != null)
            {
                float current = race.State == RaceState.Racing && !entry.finished ? Time.time - entry.lapStartTime : 0f;
                timeText.text = FormatTime(current) + (entry.bestLap > 0f ? "  ·  Melhor " + FormatTime(entry.bestLap) : "");
            }
            positionText.text = $"{entry.position}º / {race.entries.Count}";

            speedText.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
            gearText.text = car.IsReversing ? "R" : car.Gear.ToString();
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
            if (myPos > 0) CareerStats.OnRaceFinished(myPos, standings.Count);
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
            MenuTheme.StyledButton(innerRt, "Again", "JOGAR NOVAMENTE", b, new Vector2(1f, 0f), new Vector2(-390f, 30f), new Vector2(380f, 86f), 30, () => SceneLoader.ReloadCurrent());
            MenuTheme.StyledButton(innerRt, "Continue", "CONTINUAR", b, new Vector2(0f, 0f), new Vector2(390f, 30f), new Vector2(380f, 86f), 30, () => { if (resultsPanel != null) resultsPanel.gameObject.SetActive(false); resultsHidden = true; });
            MenuTheme.StyledButton(innerRt, "Replay", "REPLAY", b, new Vector2(1f, 0f), new Vector2(-390f, 128f), new Vector2(380f, 86f), 30, () => RaceX.Core.ReplaySystem.Begin(race, carCamera, resultsPanel));
            MenuTheme.StyledButton(innerRt, "Menu", "MENU PRINCIPAL", b, new Vector2(0f, 0f), new Vector2(390f, 128f), new Vector2(380f, 86f), 30, () => SceneLoader.LoadMainMenu());
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return $"{m}:{s:00.000}";
        }
    }
}
