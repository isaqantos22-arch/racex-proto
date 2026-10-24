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
        UnityEngine.UI.Button pitButton;
        UnityEngine.UI.Button ffButton;
        bool fastForward;

        /// <summary>Acelera o tempo 5x (pula a minutagem restante do treino); de novo = volta a 1x.</summary>
        void ToggleFastForward()
        {
            fastForward = !fastForward;
            float ts = fastForward ? 5f : 1f;
            Time.timeScale = ts;
            if (fastForward) RaceX.Career.CareerMode.CompressSessionClock(5f); // relógio REAL da sessão também corre 5x
            if (ffButton != null)
            {
                var t = ffButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) t.text = fastForward ? "1x" : "5x";
            }
        }
        Text pitInfoText;
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
            ffButton = MenuTheme.StyledButton(root, "FFBtn", "5x", tr, tr, new Vector2(-34f * s, -140f), new Vector2(100f * s, 90f * s), (int)(28 * s), ToggleFastForward);
            tireText = UIFactory.Label("Tire", root, "", (int)(26 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -24f), new Vector2(240f * s, 40f), MenuTheme.TextDim);
            pitInfoText = UIFactory.Label("PitInfo", root, "", (int)(24 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -70f), new Vector2(240f * s, 36f), MenuTheme.Accent);

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
            if (entry == null || entry.car == null) return;
            var car = entry.car;
            var progress = race.GetProgress(entry);
            int laps = race.Settings.laps;

            lapText.text = laps >= 900
                ? $"Volta {entry.CurrentLap(laps, progress)}"
                : $"Volta {entry.CurrentLap(laps, progress)} / {laps}";
            if (timeText != null)
            {
                float current = race.State == RaceState.Racing && !entry.finished ? Time.time - entry.lapStartTime : 0f;
                string sess = "";
                float left = race.SessionTimeLeft;
                if (left >= 0f) sess = "  |  FIM DA SESSÃO EM " + FormatTime(left);
                timeText.text = FormatTime(current) + (entry.bestLap > 0f ? "  ·  Melhor " + FormatTime(entry.bestLap) : "") + sess;
            }
            positionText.text = $"{entry.position}º / {race.entries.Count}";

            speedText.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
            gearText.text = car.IsReversing ? "R" : car.Gear.ToString();
            if (tireText != null) tireText.text = RaceX.Core.PitLaneSystem.WearText(car);
            // Botão PIT + rota: visíveis apenas com o modo ativo; aviso de distância até a entrada do pit.
            if (pitButton != null)
            {
                bool pitOn = RaceX.Core.PitLaneSystem.Instance != null && RaceX.Core.PitLaneSystem.PitEnabled;
                pitButton.gameObject.SetActive(pitOn);
                if (pitInfoText != null)
                {
                    if (!pitOn) pitInfoText.text = "";
                    else if (entry != null && entry.car != null && entry.car.pitControlled) pitInfoText.text = "NO PIT — SIGA A LINHA";
                    else
                    {
                        float d = RaceX.Core.PitLaneSystem.DistanceToPitEntry(car);
                        pitInfoText.text = d < 0f ? "PIT: ENTRE NA FAIXA LARANJA" : $"PIT A {Mathf.RoundToInt(d)} m — LINHA LARANJA";
                    }
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
            bool careerRace = RaceX.Career.CareerData.raceActive;
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
                // Carreira: CONTINUAR volta ao HUB DA CARREIRA (de lá, correr a próxima sessão).
                MenuTheme.StyledButton(innerRt, "Continue", "CONTINUAR", b, new Vector2(0f, 0f), new Vector2(0f, 30f), new Vector2(380f, 86f), 30, () =>
                {
                    if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
                    resultsHidden = true;
                    RaceX.UI.CareerFlow.Open();
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
            return $"{m}:{s:00.000}";
        }
    }
}
