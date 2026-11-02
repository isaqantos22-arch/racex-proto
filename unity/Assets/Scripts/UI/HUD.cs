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
        // LARGADA (v81): painel de 5 luzes (2 lâmpadas por coluna, como o pórtico da F1) + volta de apresentação.
        GameObject lightsPanel, formationPanel;
        readonly List<Image> lightLamps = new List<Image>();
        int lastLights = -99;
        Text tireText;
        Text danoText;
        Text fpsText;
        float fpsAcc;
        int fpsFrames, fpsLast = -1;
        float fpsMin = float.MaxValue;
        Image[] wheelImgs;      // painel CARRO: 4 rodas (temperatura azul → desgaste verde/amarelo/laranja/vermelho)
        Text[] wheelPct;        // % de vida de cada pneu ao lado da roda
        Text carTitle;          // "CARRO · M" (composto atual)
        Text weatherText;       // clima/pista: "CHUVA FRACA · PISTA MOLHADA 45% · 24°C"
        bool punctureWarned;
        Image dmgFrontImg, dmgRearImg; // barras de dano frente/trás
        Image dmgLeftImg, dmgRightImg; // laterais (sidepod/assoalho)
        Image flagBg; Text flagText;   // bandeira atual da direção de prova
        Image flagIcon; Text flagIconLabel; string flagIconKey;   // ícone tremulando ao lado do texto (SC/VSC com letreiro)
        GameObject scPipFrame; RawImage scPipImage; Text scPipLabel;  // janela de TV do safety car (entrando/saindo)
        Text penaltyText;              // "+5 s" sob a posição
        float lastPanelT;
        // Silhueta em 2 MODOS como no vídeo de referência: °C (temperaturas de pneu, freio e motor) e (!) (estado:
        // desgaste e danos). Alterna sozinho a cada 5 s, toque no painel troca (e segura 10 s), dano novo abre o (!).
        Image[] brakeImgs;      // disco de freio no centro de cada roda
        Image engineImg;        // motor (bloco atrás do cockpit)
        Text fuelText;          // linha de baixo: combustível / motor
        Image[] modeIcons; Text[] modeIconText;   // ícones °C e (!) em cima da silhueta (o ativo acende)
        Image pitBar; Text pitBarText;   // faixa PITSTOP no topo da silhueta
        // CRONÔMETRO DA PARADA como no vídeo de referência (F1 2012): painel escuro no alto, um pouco à direita do centro, com
        // o selo redondo do patrocinador e o tempo PARADO com 3 casas ("5.500"); começa quando o carro para e CONGELA na
        // liberação (fica 3 s na tela). O selo é o do jogo (RX) — o do vídeo é a marca LG, de uma empresa real.
        GameObject pitTimerPanel; Text pitTimerText; bool pitTimerWas; float pitTimerLast, pitTimerHideAt;
        // apagado (cinza) sem pedido; vermelho piscando só com o box pedido (pedido do usuário: o vermelho fixo chamava atenção à toa)
        static readonly Color PitBarDim = new Color(0.16f, 0.16f, 0.18f, 0.9f), PitBarLit = new Color(0.95f, 0.1f, 0.06f, 1f);
        const float PanelModeSeconds = 12f;   // °C ↔ (!) alterna a cada 12 s
        static readonly Color ModeIconOff = new Color(0.12f, 0.12f, 0.13f, 0.95f);
        int panelMode;          // 0 = °C, 1 = (!)
        float panelModeUntil;
        float lastDamageSum = -1f;
        string panelTitle = "CARRO";
        Color panelTitleColor = Color.white;
        bool pitPulseOn;
        /// <summary>Cor do pneu: AZUL enquanto frio (aquecendo, ~1 volta) → verde → amarelo → laranja → vermelho com o desgaste.</summary>
        static Color TyreColor(float temp, float wear)
        {
            if (temp < 0.98f) return Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.95f), Mathf.Clamp01(temp));
            return WearColor(wear);
        }

        /// <summary>Cor de UMA roda pela temperatura REAL (°C) contra a janela do composto e pelo desgaste:
        /// azul = abaixo da janela (frio), magenta = acima (superaquecido), senão verde→vermelho pelo desgaste.</summary>
        static Color WheelColor(int compound, float tempC, float wear, bool puncture)
        {
            if (puncture) return new Color(0.35f, 0.05f, 0.05f);
            float lo = RaceX.Core.TyreModel.Low(compound), hi = RaceX.Core.TyreModel.High(compound);
            if (tempC < lo - 4f) return Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.95f), Mathf.Clamp01(1f - (lo - tempC) / 30f));
            // Superaquecido em DEGRADÊ (como o frio): rosa claro logo acima da janela → magenta intenso a +25 °C,
            // onde o desgaste já está ~2,25× (TyreModel.OverheatWear, +5%/°C). Antes era um magenta fixo.
            if (tempC > hi + 4f) return Color.Lerp(new Color(1f, 0.6f, 0.92f), new Color(0.72f, 0.02f, 0.58f), Mathf.Clamp01((tempC - hi - 4f) / 21f));
            return WearColor(wear);
        }

        static Color WearColor(float wear)
        {
            if (wear < 0.35f) return new Color(0.25f, 0.85f, 0.35f);
            if (wear < 0.55f) return new Color(1f, 0.85f, 0.15f);
            if (wear < 0.75f) return new Color(1f, 0.55f, 0.1f);
            return new Color(1f, 0.22f, 0.15f);
        }
        static Color ZoneColor(float d) => RaceX.Car.CarDamage.ColorFor(d, false);

        /// <summary>Modo °C: pneu só pela temperatura — azul frio, verde na janela do composto, rosa/magenta acima.</summary>
        static Color TyreTempColor(int compound, float tempC, bool puncture)
        {
            if (puncture) return new Color(0.35f, 0.05f, 0.05f);
            float lo = RaceX.Core.TyreModel.Low(compound), hi = RaceX.Core.TyreModel.High(compound);
            if (tempC < lo - 4f) return Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.95f), Mathf.Clamp01(1f - (lo - tempC) / 30f));
            if (tempC > hi + 4f) return Color.Lerp(new Color(1f, 0.6f, 0.92f), new Color(0.72f, 0.02f, 0.58f), Mathf.Clamp01((tempC - hi - 4f) / 21f));
            return new Color(0.25f, 0.85f, 0.35f);
        }

        /// <summary>Disco de freio: azul abaixo da janela, verde na janela, laranja→vermelho na fadiga, preto quebrado.</summary>
        static Color BrakeTempColor(RaceX.Car.CarSystems sys, float t, bool failed)
        {
            if (failed) return new Color(0.08f, 0.08f, 0.08f);
            if (t < sys.BrakeWindowLow) return Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.95f), Mathf.Clamp01(1f - (sys.BrakeWindowLow - t) / 250f));
            if (t < sys.BrakeFadeFrom) return new Color(0.25f, 0.85f, 0.35f);
            return Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.15f, 0.1f), Mathf.Clamp01((t - sys.BrakeFadeFrom) / (sys.BrakeFailAt - sys.BrakeFadeFrom)));
        }

        /// <summary>(!) do MOTOR: o desgaste é a VIDA do motor da temporada (cota de 4 por ano — um motor roda várias corridas),
        /// então 10–40% é uso normal, não problema. Antes usava a escala de dano (amarelo a partir de 10%) e o motor da
        /// carreira ficava sempre amarelo. Verde até 60% de vida, amarelo 60–80, laranja 80–90, vermelho acima;
        /// radiador amassado conta como dano direto.</summary>
        static Color EngineWearColor(float wear, float radiatorLoss, bool failed)
        {
            if (failed) return new Color(0.08f, 0.08f, 0.08f);
            if (radiatorLoss > 0.02f) return RaceX.Car.CarDamage.ColorFor(Mathf.Max(0.12f, radiatorLoss * 1.6f), false);
            if (wear < 0.6f) return new Color(0.25f, 0.85f, 0.35f);
            if (wear < 0.8f) return new Color(1f, 0.85f, 0.15f);
            if (wear < 0.9f) return new Color(1f, 0.55f, 0.1f);
            return new Color(1f, 0.15f, 0.1f);
        }

        /// <summary>Linha de combustível muda de cor conforme acaba: verde (mais da metade), amarelo, laranja (menos de 25%
        /// ou faltando para terminar a prova), vermelho (menos de 10%, falta mais de 1 volta ou sem combustível).</summary>
        static Color FuelColor(RaceX.Car.CarSystems sys, float lapsDelta)
        {
            if (sys.engineFailed || sys.fuelKg <= 0f) return new Color(1f, 0.25f, 0.2f);
            float frac = sys.fuelCapacityKg > 0f ? sys.fuelKg / sys.fuelCapacityKg : 1f;
            if ((sys.Timed && frac < 0.1f) || (!sys.Timed && lapsDelta < -1f)) return new Color(1f, 0.25f, 0.2f);   // na corrida o tanque termina quase vazio de propósito: vermelho só se faltar
            if (frac < 0.25f || (!sys.Timed && lapsDelta < 0f)) return new Color(1f, 0.55f, 0.1f);
            if (frac < 0.5f) return new Color(1f, 0.85f, 0.15f);
            return new Color(0.35f, 0.9f, 0.45f);
        }

        /// <summary>Água do motor: azul frio (abaixo de 90 °C), verde normal, amarelo perto do limite, laranja perdendo
        /// potência, vermelho perto da quebra, preto quebrado.</summary>
        static Color EngineTempColor(float t, bool failed)
        {
            if (failed) return new Color(0.08f, 0.08f, 0.08f);
            if (t < 90f) return new Color(0.3f, 0.7f, 1f);
            if (t < 120f) return new Color(0.25f, 0.85f, 0.35f);
            if (t < RaceX.Car.CarSystems.EngineHotFrom) return new Color(1f, 0.85f, 0.15f);
            if (t < 135f) return new Color(1f, 0.55f, 0.1f);
            return new Color(1f, 0.15f, 0.1f);
        }
        UnityEngine.UI.Button pitButton;
        bool lastPitHighlighted;
        UnityEngine.UI.Button ffButton;
        static bool ffOn;
        static readonly List<HUD> liveHuds = new List<HUD>();

        /// <summary>Tempo acelerado 5x ligado (botão 1x/5x do HUD ou do HUB da carreira).</summary>
        public static bool FastForwardOn => ffOn;

        /// <summary>Liga/desliga o 5x (pula a minutagem do treino). Normal = 0.01 (100 Hz, física ORIGINAL —
        /// o 0.02 degradava a fricção); 5x = 0.02 (menos passos por frame, colisões estáveis). Com o jogo
        /// congelado (hub no fim da sessão, timeScale 0) só guarda a escolha — vale ao retomar.
        /// O relógio REAL da sessão acompanha: 5 vezes mais rápido ligado, volta ao normal desligado.</summary>
        public static void SetFastForward(bool on)
        {
            if (on == ffOn) { RefreshFfLabels(); return; }
            ffOn = on;
            RaceX.Career.CareerMode.ScaleSessionClock(on ? 5f : 0.2f);
            if (Time.timeScale > 0.01f) ResumeTime();
            RefreshFfLabels();
        }

        /// <summary>Retoma o tempo do jogo respeitando o 1x/5x escolhido (pausa/hub fechando).</summary>
        public static void ResumeTime()
        {
            Time.timeScale = ffOn ? 5f : 1f;
            Time.fixedDeltaTime = ffOn ? 0.02f : 0.01f;
            Time.maximumDeltaTime = ffOn ? 0.12f : 0.05f; // cabe 5 passos de 0.01 por frame no normal
        }

        static void RefreshFfLabels()
        {
            for (int i = liveHuds.Count - 1; i >= 0; i--)
            {
                var h = liveHuds[i];
                if (h == null) { liveHuds.RemoveAt(i); continue; }
                if (h.ffButton == null) continue;
                var t = h.ffButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (t != null) { t.text = ffOn ? "5x" : "1x"; t.color = ffOn ? new Color(0.3f, 1f, 0.45f) : MenuTheme.TextDim; }
            }
        }

        void ToggleFastForward() => SetFastForward(!ffOn);
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
            // Cena nova = tempo NORMAL — exceto na CARREIRA quando o 5x foi escolhido no HUB (aí vale
            // para a sessão que começa). Fora da carreira o 5x nunca vaza para a próxima tela.
            liveHuds.Add(this);
            bool careerSession = race != null && race.Settings != null && race.Settings.careerPhase >= 0;
            if (!careerSession) ffOn = false;
            ResumeTime();
            var fade = UIFactory.Panel("Fade", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            var fadeRt = (RectTransform)fade.transform;
            fadeRt.anchorMin = Vector2.zero; fadeRt.anchorMax = Vector2.one; fadeRt.sizeDelta = Vector2.zero;
            fade.raycastTarget = false;
            fade.enabled = false;
            FadeImage = fade;

            float s = compact ? 0.7f : 1f;
            var tl = new Vector2(0f, 1f);
            lapText = UIFactory.Label("Lap", root, "", (int)(44 * s), TextAnchor.UpperLeft, tl, tl, new Vector2(30f, -24f), new Vector2(500f, 60f), MenuTheme.TextDim);
            Emphasize(lapText);   // volta e tempo também em destaque (contorno): antes cinza claro sumindo no céu
            if (!compact)
            {
                timeText = UIFactory.Label("Time", root, "", 30, TextAnchor.UpperLeft, tl, tl, new Vector2(30f, -80f), new Vector2(700f, 44f), MenuTheme.TextDim);
                Emphasize(timeText);
            }

            var tc = new Vector2(0.5f, 1f);
            positionText = UIFactory.Label("Position", root, "", (int)(64 * s), TextAnchor.UpperCenter, tc, tc, new Vector2(0f, -18f), new Vector2(400f, 80f));
            positionText.color = MenuTheme.Text;
            var outline = positionText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);

            var tr = new Vector2(1f, 1f);
            MenuTheme.StyledButton(root, "CameraBtn", "CAM", tr, tr, new Vector2(-160f * s, -24f), new Vector2(110f * s, 100f * s), (int)(28 * s), () => carCamera?.NextView());
            MenuTheme.StyledButton(root, "PauseBtn", "II", tr, tr, new Vector2(-34f * s, -24f), new Vector2(100f * s, 100f * s), (int)(40 * s), () => pauseMenu?.ToggleFor(entry != null ? entry.car : null));

            // Pit stop: botão SEMPRE criado; ativa quando o modo liga (HUD nasce antes do PitLaneSystem).
            pitButton = MenuTheme.StyledButton(root, "PitBtn", "PIT", tr, tr, new Vector2(-160f * s, -140f), new Vector2(110f * s, 90f * s), (int)(26 * s), () => { if (entry != null) RaceX.Core.PitLaneSystem.RequestPit(entry.car); });
            // Acelerar o TEMPO 5x (pula a minutagem do treino): aperta de novo e volta a 1x.
            // Posição y−250 (abaixo do PIT) para não invadir a coluna CAM/pausa do canto.
            ffButton = MenuTheme.StyledButton(root, "FFBtn", ffOn ? "5x" : "1x", tr, tr, new Vector2(-34f * s, -140f), new Vector2(100f * s, 90f * s), (int)(28 * s), ToggleFastForward);
            // Online: sem 5x (acelerar o tempo só num aparelho tiraria ele da corrida dos outros).
            if (race != null && race.Settings != null && race.Settings.mode == GameMode.Online) ffButton.gameObject.SetActive(false);
            tireText = UIFactory.Label("Tire", root, "", (int)(26 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -24f), new Vector2(240f * s, 40f), MenuTheme.TextDim);
            pitInfoText = UIFactory.Label("PitInfo", root, "", (int)(24 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -70f), new Vector2(240f * s, 36f), MenuTheme.Accent);
            // Estratégia sob o pneu: "PARADAS 0/1 · BOX V12" (quantas paradas já feitas e a próxima).
            pitPlanText = UIFactory.Label("PitPlan", root, "", (int)(22 * s), TextAnchor.UpperRight, tr, tr, new Vector2(-290f * s, -112f), new Vector2(240f * s, 30f), MenuTheme.TextDim);
            RefreshFfLabels();
            // OLHAR PARA TRÁS: segurar = câmera virada para trás; soltar = volta. Centro de baixo (livre no celular).
            var bc = new Vector2(0.5f, 0f);
            var backBtn = MenuTheme.StyledButton(root, "LookBackBtn", "TRÁS", bc, bc, new Vector2(0f, 26f * s), new Vector2(170f * s, 78f * s), (int)(24 * s), null);
            var trig = backBtn.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>() ?? backBtn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            void Hook(UnityEngine.EventSystems.EventTriggerType type, bool held)
            {
                var en = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
                en.callback.AddListener(_ => CarCamera.LookBackHeld = held);
                trig.triggers.Add(en);
            }
            Hook(UnityEngine.EventSystems.EventTriggerType.PointerDown, true);
            Hook(UnityEngine.EventSystems.EventTriggerType.PointerUp, false);
            Hook(UnityEngine.EventSystems.EventTriggerType.PointerExit, false);

            // PAINEL CARRO (regra do usuário): silhueta com 4 rodas coloridas por DESGASTE
            // (verde/amarelo/vermelho) + barras de DANO da frente e da traseira.
            // Pivot (0.5, 0.5) em todos os elementos internos para o corpo nunca vazar para fora do painel.
            // Em TODOS os modos (single, tela dividida e online) — na tela dividida em escala menor.
            {
                var carPanel = UIFactory.Panel("CarPanel", root, tr, tr, new Vector2(-160f * s, -250f * s), new Vector2(200f * s, 298f * s), new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.88f));
                carPanel.sprite = MenuTheme.RoundedSprite(100, 112, 20);
                // Toque no painel: troca °C ↔ (!) e segura o modo escolhido por 10 s.
                carPanel.raycastTarget = true;   // o UIFactory desliga o raycast das decorações: sem isto o toque não chegava
                var modeBtn = carPanel.gameObject.AddComponent<Button>();
                modeBtn.transition = Selectable.Transition.None;
                modeBtn.onClick.AddListener(() => { panelMode ^= 1; panelModeUntil = Time.unscaledTime + PanelModeSeconds; lastPanelT = -1f; });
                var topAnchor = new Vector2(0.5f, 1f);
                var centerPivot = new Vector2(0.5f, 0.5f);
                // CABEÇALHO como no F1 2012 (vídeo de referência, hud_detalhes.png): faixa PITSTOP (vermelho escuro; vivo
                // piscando com o box pedido) e, abaixo, os ícones °C e (!) — o modo ativo acende (°C azul, (!) vermelho).
                pitBar = UIFactory.Panel("PitBar", carPanel.transform, topAnchor, topAnchor, new Vector2(0f, -6f * s), new Vector2(150f * s, 22f * s), PitBarDim);
                pitBar.sprite = MenuTheme.RoundedSprite(20, 6, 3);
                var pbt = UIFactory.Label("PitBarText", pitBar.transform, "PITSTOP", (int)(16 * s), TextAnchor.MiddleCenter, centerPivot, centerPivot, Vector2.zero, new Vector2(150f * s, 22f * s), new Color(1f, 1f, 1f, 0.85f));
                pbt.fontStyle = FontStyle.Bold;
                pbt.raycastTarget = false;
                pitBarText = pbt;
                modeIcons = new Image[2];
                modeIconText = new Text[2];
                string[] icoTxt = { "°C", "(!)" };
                for (int k = 0; k < 2; k++)
                {
                    var ic = UIFactory.Panel("ModeIcon" + k, carPanel.transform, topAnchor, topAnchor, new Vector2((k == 0 ? -24f : 24f) * s, -31f * s), new Vector2(44f * s, 24f * s), ModeIconOff);
                    ic.sprite = MenuTheme.RoundedSprite(10, 6, 3);
                    var it = UIFactory.Label("ModeIconText", ic.transform, icoTxt[k], (int)(16 * s), TextAnchor.MiddleCenter, centerPivot, centerPivot, Vector2.zero, new Vector2(44f * s, 24f * s), Color.white);
                    it.fontStyle = FontStyle.Bold;
                    it.raycastTarget = false;
                    modeIcons[k] = ic; modeIconText[k] = it;
                }
                // Corpo da silhueta abaixo do cabeçalho (mesmas medidas de antes, deslocado 52 px).
                var bodyRt = new GameObject("CarBody", typeof(RectTransform)).GetComponent<RectTransform>();
                bodyRt.SetParent(carPanel.transform, false);
                bodyRt.anchorMin = bodyRt.anchorMax = topAnchor;
                bodyRt.pivot = topAnchor;
                bodyRt.anchoredPosition = new Vector2(0f, -52f * s);
                bodyRt.sizeDelta = new Vector2(200f * s, 246f * s);
                var carBody = bodyRt.transform;
                carTitle = UIFactory.Label("CarTitle", carBody, "CARRO", (int)(17 * s), TextAnchor.MiddleCenter, topAnchor, topAnchor, new Vector2(0f, -10f * s), new Vector2(190f * s, 22f * s), MenuTheme.TextDim);
                dmgFrontImg = UIFactory.Panel("DmgF", carBody, topAnchor, centerPivot, new Vector2(0f, -44f * s), new Vector2(44f * s, 9f * s), Color.green);
                dmgFrontImg.sprite = MenuTheme.RoundedSprite(22, 5, 4);
                var m = entry.model;
                bool formula = RaceX.Car.CarSpec.IsFormula(m);
                var parts = formula
                    ? new[] { new Vector4(0f, -62f, 76f, 8f), new Vector4(0f, -84f, 12f, 36f), new Vector4(0f, -122f, 26f, 54f), new Vector4(0f, -134f, 44f, 34f), new Vector4(0f, -164f, 20f, 26f), new Vector4(0f, -184f, 62f, 10f) }
                    : new[] { new Vector4(0f, -122f, 52f, 128f), new Vector4(0f, -100f, 40f, 16f), new Vector4(0f, -126f, 38f, 30f), new Vector4(0f, -156f, 34f, 12f) };
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    var pv = parts[pi];
                    float a = formula ? (pi == 2 ? 0.26f : 0.18f) : (pi == 0 ? 0.16f : pi == 2 ? 0.08f : 0.3f);
                    var part = UIFactory.Panel("Body" + pi, carBody, topAnchor, centerPivot, new Vector2(pv.x, pv.y) * s, new Vector2(pv.z, pv.w) * s, new Color(1f, 1f, 1f, a));
                    part.sprite = MenuTheme.RoundedSprite(Mathf.Max(4, (int)pv.z / 2), Mathf.Max(4, (int)pv.w / 2), Mathf.Clamp((int)Mathf.Min(pv.z, pv.w) / 4, 1, 10));
                }
                if (formula)
                {
                    var cockpit = UIFactory.Panel("Cockpit", carBody, topAnchor, centerPivot, new Vector2(0f, -112f) * s, new Vector2(12f, 18f) * s, new Color(0f, 0f, 0f, 0.55f));
                    cockpit.sprite = MenuTheme.RoundedSprite(6, 9, 3);
                }
                dmgRearImg = UIFactory.Panel("DmgR", carBody, topAnchor, centerPivot, new Vector2(0f, -200f * s), new Vector2(44f * s, 9f * s), Color.green);
                dmgRearImg.sprite = MenuTheme.RoundedSprite(22, 5, 4);
                // LATERAIS (sidepod/assoalho de cada lado): verde → amarelo → laranja → vermelho → PRETO.
                dmgLeftImg = UIFactory.Panel("DmgLeft", carBody, topAnchor, centerPivot, new Vector2(-21f * s, -122f * s), new Vector2(7f * s, 56f * s), Color.green);
                dmgLeftImg.sprite = MenuTheme.RoundedSprite(4, 28, 3);
                dmgRightImg = UIFactory.Panel("DmgRight", carBody, topAnchor, centerPivot, new Vector2(21f * s, -122f * s), new Vector2(7f * s, 56f * s), Color.green);
                dmgRightImg.sprite = MenuTheme.RoundedSprite(4, 28, 3);
                var wPos = new[] { new Vector2(-34f, -80f), new Vector2(34f, -80f), new Vector2(-34f, -164f), new Vector2(34f, -164f) };
                wheelImgs = new Image[4];
                wheelPct = new Text[4];
                for (int i = 0; i < 4; i++)
                {
                    var w = UIFactory.Panel("Wheel" + i, carBody, topAnchor, centerPivot, wPos[i] * s, new Vector2(18f * s, 28f * s), Color.green);
                    w.sprite = MenuTheme.RoundedSprite(9, 14, 5);
                    wheelImgs[i] = w;
                    // % de vida ao lado de CADA pneu (esquerda dos esquerdos, direita dos direitos).
                    bool leftSide = wPos[i].x < 0f;
                    var pct = UIFactory.Label("WheelPct" + i, carBody, "100%", (int)(15 * s), leftSide ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                        topAnchor, centerPivot, new Vector2((leftSide ? -70f : 70f) * s, wPos[i].y * s), new Vector2(46f * s, 22f * s), Color.white);
                    pct.raycastTarget = false;
                    wheelPct[i] = pct;
                }
                // Disco de FREIO no centro de cada roda (cor pela temperatura no °C, pelo desgaste/duto no (!)).
                brakeImgs = new Image[4];
                for (int i = 0; i < 4; i++)
                {
                    // Monoposto: barrinha POR DENTRO da roda (entre a roda e o carro, como no F1 2012) — em cima da roda ela
                    // tinha a mesma cor e sumia. GT: carroceria larga, fica no centro da roda (com contorno escuro).
                    Vector2 bp = formula ? new Vector2(wPos[i].x + (wPos[i].x < 0f ? 17f : -17f), wPos[i].y) : wPos[i];
                    var bo = UIFactory.Panel("BrakeOutline" + i, carBody, topAnchor, centerPivot, bp * s, new Vector2(9f * s, 21f * s), new Color(0f, 0f, 0f, 0.75f));
                    bo.sprite = MenuTheme.RoundedSprite(4, 10, 2);
                    var b = UIFactory.Panel("Brake" + i, carBody, topAnchor, centerPivot, bp * s, new Vector2(6f * s, 18f * s), new Color(0.3f, 0.3f, 0.3f));
                    b.sprite = MenuTheme.RoundedSprite(4, 7, 2);
                    b.raycastTarget = false;
                    brakeImgs[i] = b;
                }
                // MOTOR: bloco maior com contorno escuro atrás do cockpit (antes 16×14 sumia no desenho do carro).
                var eo = UIFactory.Panel("EngineOutline", carBody, topAnchor, centerPivot, new Vector2(0f, -147f) * s, new Vector2(26f, 26f) * s, new Color(0f, 0f, 0f, 0.8f));
                eo.sprite = MenuTheme.RoundedSprite(8, 8, 3);
                engineImg = UIFactory.Panel("Engine", carBody, topAnchor, centerPivot, new Vector2(0f, -147f) * s, new Vector2(21f, 21f) * s, new Color(0.3f, 0.3f, 0.3f));
                engineImg.sprite = MenuTheme.RoundedSprite(8, 7, 3);
                engineImg.raycastTarget = false;
                fuelText = UIFactory.Label("Fuel", carBody, "", (int)(14 * s), TextAnchor.MiddleCenter, topAnchor, centerPivot, new Vector2(0f, -228f * s), new Vector2(196f * s, 20f * s), MenuTheme.TextDim);
                fuelText.raycastTarget = false;
                panelModeUntil = Time.unscaledTime + PanelModeSeconds;
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
            // CLIMA no bloco da DIREITA (pneu · box · paradas · clima): centralizado com 760 px ele passava por baixo dos setores
            // S1/S2/S3 (x 270–780) e "CHUVA FORTE" sumia. Organização como no HUD de referência: esquerda = volta/tempos,
            // centro = posição/bandeiras, direita = estratégia e condições. Termina antes do botão PIT; começa depois do ícone
            // da bandeira (centro). Nada foi removido.
            var trw = new Vector2(1f, 1f);
            weatherText = UIFactory.Label("Weather", root, "", (int)(22 * s), TextAnchor.UpperRight, trw, trw, new Vector2(-290f * s, -146f), new Vector2(620f * s, 30f), MenuTheme.TextDim);
            weatherText.raycastTarget = false;
            var c = new Vector2(0.5f, 0.5f);
            countdownText = UIFactory.Label("Countdown", root, "", (int)(200 * s), TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(800f, 300f), new Color(1f, 0.9f, 0.2f));
            lightsPanel = UIFactory.Panel("StartLights", root, tc, tc, new Vector2(0f, -190f * s), new Vector2(520f * s, 150f * s), new Color(0.03f, 0.03f, 0.04f, 0.92f)).gameObject;
            lightsPanel.GetComponent<Image>().sprite = MenuTheme.RoundedSprite(120, 36, 10);
            var lampSprite = MenuTheme.RoundedSprite(64, 64, 32);
            for (int col = 0; col < 5; col++)
                for (int row = 0; row < 2; row++)
                {
                    var lamp = UIFactory.Panel("Lamp" + col + "_" + row, lightsPanel.transform, c, c, new Vector2((-200f + col * 100f) * s, (row == 0 ? 33f : -33f) * s), new Vector2(58f * s, 58f * s), new Color(0.16f, 0.16f, 0.18f, 1f));
                    lamp.sprite = lampSprite;
                    lightLamps.Add(lamp);
                }
            lightsPanel.SetActive(false);
            // Faixa larga o bastante para o texto não invadir o botão PULAR (o rótulo transborda: 520 px não cabiam).
            formationPanel = UIFactory.Panel("Formation", root, tc, tc, new Vector2(0f, -360f * s), new Vector2(900f * s, 64f * s), new Color(0f, 0f, 0f, 0.6f)).gameObject;
            UIFactory.Label("FormationText", formationPanel.transform, "VOLTA DE APRESENTAÇÃO · SEM ULTRAPASSAR", (int)(22 * s), TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f * s, 0f), new Vector2(640f * s, 60f * s), Color.white);
            var fpr = (RectTransform)formationPanel.transform;
            MenuTheme.StyledButton(fpr, "SkipFormation", "PULAR", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f * s, 0f), new Vector2(200f * s, 54f * s), (int)(22 * s), () => RaceManager.SkipFormationRequested = true);
            formationPanel.SetActive(false);
            wrongWayText = UIFactory.Label("WrongWay", root, "CONTRAMÃO", (int)(90 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 160f * s), new Vector2(900f, 120f), new Color(1f, 0.15f, 0.1f));
            wrongWayText.enabled = false;
            // Abaixo da faixa de bandeira (que desceu para −206..−248 com o ícone em cima): em +260 o rádio ficava por cima dela.
            messageText = UIFactory.Label("Message", root, "", (int)(48 * s), TextAnchor.MiddleCenter, c, c, new Vector2(0f, 190f * s), new Vector2(900f, 80f));
            // BANDEIRA da direção de prova (amarela, azul, VSC...) + contagem para devolver posição.
            flagBg = UIFactory.Panel("FlagBg", root, tc, tc, new Vector2(0f, -206f * s), new Vector2(820f * s, 42f * s), new Color(1f, 0.85f, 0.1f, 0.9f));
            flagBg.sprite = MenuTheme.RoundedSprite(200, 12, 8);
            flagText = UIFactory.Label("FlagText", flagBg.transform, "", (int)(24 * s), TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(800f * s, 40f * s), Color.black);
            // ÍCONE da bandeira EM CIMA da faixa, centralizado na frase (a faixa desceu 50 px para caber abaixo do clima).
            flagIcon = UIFactory.Panel("FlagIcon", flagBg.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 3f * s), new Vector2(66f * s, 45f * s), Color.white);
            flagIcon.raycastTarget = false;
            flagIcon.preserveAspect = true;
            flagIconLabel = UIFactory.Label("FlagIconLabel", flagIcon.transform, "", (int)(16 * s), TextAnchor.MiddleCenter, c, c, new Vector2(4f * s, 3f * s), new Vector2(62f * s, 32f * s), Color.black);
            flagIconLabel.fontStyle = FontStyle.Bold;
            flagIconLabel.raycastTarget = false;
            flagBg.gameObject.SetActive(false);
            // CRONÔMETRO DA PARADA (vídeo de referência): alto da tela, à direita da posição.
            {
                var pt = UIFactory.Panel("PitTimer", root, tc, tc, new Vector2(300f * s, -100f), new Vector2(214f * s, 46f * s), new Color(0.05f, 0.05f, 0.06f, 0.72f));
                pt.sprite = MenuTheme.RoundedSprite(40, 10, 8);
                var ml = new Vector2(0f, 0.5f);
                var badge = UIFactory.Panel("PitTimerBadge", pt.transform, ml, new Vector2(0f, 0.5f), new Vector2(8f * s, 0f), new Vector2(34f * s, 34f * s), new Color(0.78f, 0.05f, 0.18f, 1f));
                badge.sprite = MenuTheme.RoundedSprite(64, 64, 32);
                var bt = UIFactory.Label("PitTimerBadgeText", badge.transform, "RX", (int)(15 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f * s, 34f * s), Color.white);
                bt.fontStyle = FontStyle.Bold;
                bt.raycastTarget = false;
                var mr = new Vector2(1f, 0.5f);
                pitTimerText = UIFactory.Label("PitTimerText", pt.transform, "0.000", (int)(30 * s), TextAnchor.MiddleRight, mr, mr, new Vector2(-12f * s, 0f), new Vector2(150f * s, 44f * s), Color.white);
                pitTimerText.fontStyle = FontStyle.Bold;
                pitTimerText.raycastTarget = false;
                pitTimerPanel = pt.gameObject;
                pitTimerPanel.SetActive(false);
            }
            // VOLTA MAIS RÁPIDA da sessão (referência F1 2012 do usuário): faixa ROXA no alto, ao centro, com nome + tempo e
            // "VOLTA MAIS RÁPIDA" embaixo; aparece 5 s sempre que alguém (qualquer carro) bate o melhor tempo da sessão.
            {
                var fl = UIFactory.Panel("FastestLap", root, tc, tc, new Vector2(0f, -84f * s), new Vector2(440f * s, 70f * s), new Color(0f, 0f, 0f, 0f));
                fl.raycastTarget = false;
                var top = UIFactory.Panel("FastestLapTop", fl.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(440f * s, 38f * s), new Color(0.47f, 0.13f, 0.78f, 0.95f));
                top.raycastTarget = false;
                fastestText = UIFactory.Label("FastestLapText", top.transform, "", (int)(24 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f * s, 36f * s), Color.white);
                fastestText.fontStyle = FontStyle.Bold;
                fastestText.raycastTarget = false;
                var bot = UIFactory.Panel("FastestLapBottom", fl.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f * s), new Vector2(440f * s, 30f * s), new Color(0.05f, 0.05f, 0.07f, 0.85f));
                bot.raycastTarget = false;
                var bl = UIFactory.Label("FastestLapSub", bot.transform, "VOLTA MAIS RÁPIDA", (int)(18 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f * s, 28f * s), Color.white);
                bl.raycastTarget = false;
                fastestPanel = fl.gameObject;
                fastestPanel.SetActive(false);
            }
            // JANELA DE TV do safety car: aparece quando ele entra na pista e quando recolhe. Fica à ESQUERDA, entre o mini-mapa
            // e a pista, menor (pedido do usuário: no centro tapava o traçado — "literalmente não vejo nada").
            {
                var pipFrame = UIFactory.Panel("ScPipFrame", root, tc, tc, new Vector2(-530f * s, -200f * s), new Vector2(392f * s, 252f * s), new Color(0f, 0f, 0f, 0.85f));
                pipFrame.rectTransform.localScale = Vector3.one * 0.72f;
                pipFrame.raycastTarget = false;
                var rgo = new GameObject("ScPipImage", typeof(RectTransform));
                rgo.transform.SetParent(pipFrame.transform, false);
                var rrt = (RectTransform)rgo.transform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
                rrt.pivot = new Vector2(0.5f, 1f);
                rrt.anchoredPosition = new Vector2(0f, -4f * s);
                rrt.sizeDelta = new Vector2(384f * s, 216f * s);
                scPipImage = rgo.AddComponent<RawImage>();
                scPipImage.raycastTarget = false;
                var bc2 = new Vector2(0.5f, 0f);
                scPipLabel = UIFactory.Label("ScPipLabel", pipFrame.transform, "", (int)(18 * s), TextAnchor.MiddleCenter, bc2, bc2, new Vector2(0f, 16f * s), new Vector2(384f * s, 26f * s), new Color(1f, 0.8f, 0.1f));
                scPipLabel.raycastTarget = false;
                scPipFrame = pipFrame.gameObject;
                scPipFrame.SetActive(false);
            }
            penaltyText = UIFactory.Label("Penalty", root, "", (int)(26 * s), TextAnchor.UpperCenter, tc, tc, new Vector2(230f * s, -30f), new Vector2(160f, 34f), new Color(1f, 0.35f, 0.25f));
            RaceX.Core.RaceControl.Notice += OnRaceNotice;

            race.CountdownTick += OnCountdown;
            RaceX.Core.PitLaneSystem.TeamRadio += OnTeamRadio;
            race.LapCompleted += OnLap;
            race.RaceFinished += ShowResults;
            race.RaceStarted += OnRaceStartedRadio;
            MiniMap.Create(root, race, entry, compact);
            // Setores S1/S2/S3 e torre de posições em TODOS os modos (pedido do usuário: o que a carreira tem, o multiplayer
            // também tem) — na tela dividida em escala 0,7, à direita do minimapa e a torre abaixo dele.
            BuildSectors(root, s);
            BuildTower(root, s);
        }

        static readonly Color SecPurple = new Color(0.62f, 0.25f, 0.95f, 0.92f);
        // Diferenças de tempo em DESTAQUE (pedido do usuário: o roxo e os outros tempos sumiam no céu/grama): cores vivas, negrito,
        // contorno e uma "pílula" escura atrás de cada uma.
        static readonly Color DeltaPurple = new Color(0.88f, 0.62f, 1f);
        static readonly Color DeltaGreen = new Color(0.35f, 1f, 0.5f);
        static readonly Color DeltaYellow = new Color(1f, 0.86f, 0.25f);
        static readonly Color DeltaRed = new Color(1f, 0.38f, 0.32f);
        static readonly Color DeltaPill = new Color(0.03f, 0.03f, 0.06f, 0.62f);
        Image[] secDeltaBg;
        Image deltaBg;
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

        void BuildSectors(RectTransform root, float s)
        {
            SectorTimer.Ensure(race);
            var tl = new Vector2(0f, 1f);
            secBg = new Image[3];
            secText = new Text[3];
            secDelta = new Text[3];
            secDeltaBg = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var bg = UIFactory.Panel("Sector" + (i + 1), root, tl, tl, new Vector2((270f + i * 172f) * s, -128f * s), new Vector2(166f * s, 36f * s), SecNone);
                bg.sprite = MenuTheme.RoundedSprite(83, 18, 8);
                bg.raycastTarget = false;
                secBg[i] = bg;
                var t = UIFactory.Label("SectorTxt", bg.transform, "S" + (i + 1), Mathf.RoundToInt(22 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f * s, 34f * s), Color.white);
                t.raycastTarget = false;
                secText[i] = t;
                // Diferença do setor numa pílula escura LOGO ABAIXO do quadro (antes: texto solto encostado na borda, sem contraste).
                var pill = UIFactory.Panel("SectorDeltaBg", bg.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -4f * s), new Vector2(132f * s, 30f * s), DeltaPill);
                pill.sprite = MenuTheme.RoundedSprite(66, 15, 8);
                pill.enabled = false;
                secDeltaBg[i] = pill;
                var dl = UIFactory.Label("SectorDelta", pill.transform, "", Mathf.RoundToInt(23 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(132f * s, 30f * s), Color.white);
                dl.raycastTarget = false;
                Emphasize(dl);
                secDelta[i] = dl;
            }
            deltaBg = UIFactory.Panel("DeltaBg", root, tl, tl, new Vector2(270f * s, -194f * s), new Vector2(214f * s, 38f * s), DeltaPill);
            deltaBg.sprite = MenuTheme.RoundedSprite(107, 19, 9);
            deltaBg.enabled = false;
            deltaText = UIFactory.Label("Delta", deltaBg.transform, "", Mathf.RoundToInt(26 * s), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(214f * s, 38f * s), Color.white);
            deltaText.raycastTarget = false;
            Emphasize(deltaText);
        }

        /// <summary>Texto de tempo legível sem pesar: sombra escura fina (1 px), peso normal. O contorno grosso + negrito da versão
        /// anterior ficou "destacado demais, com sombra branca e bordas grosseiras" (relato do usuário).</summary>
        static void Emphasize(Text t)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(1f, -1f);
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
                if (done <= 0f && (entry.outLap || (entry.car != null && RaceX.Core.PitLaneSystem.IsInPitFlow(entry.car))))
                {
                    txt = "S" + (k + 1) + "  --.---";
                    col = SecNone;
                }
                else if (k == cur && done <= 0f)
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
                        // setor roxo (melhor da sessão) → o "a menos" também em roxo, não em verde
                        dCol = sd < 0f ? (c == SectorTimer.SectorColor.Purple ? DeltaPurple : DeltaGreen) : sd > 0.0005f ? DeltaYellow : Color.white;
                    }
                }
                if (secDelta[k].text != dTxt) secDelta[k].text = dTxt;
                secDelta[k].color = dCol;
                bool showPill = dTxt.Length > 0;
                if (secDeltaBg[k].enabled != showPill) secDeltaBg[k].enabled = showPill;
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
                // abaixo do seu melhor = volta recorde pessoal em andamento → roxo (pedido do usuário: era verde)
                deltaText.color = d > 0.0005f ? DeltaRed : d < -0.0005f ? DeltaPurple : Color.white;
            }
            if (deltaText.text != dtx) deltaText.text = dtx;
            bool showDelta = dtx.Length > 0;
            if (deltaBg.enabled != showDelta) deltaBg.enabled = showDelta;
        }

        void BuildTower(RectTransform root, float s)
        {
            var ml = new Vector2(0f, 0.5f);
            towerRoot = UIFactory.Stretch("Tower", root);
            towerRoot.anchorMin = ml; towerRoot.anchorMax = ml; towerRoot.pivot = ml;
            // Tela dividida: 30 px mais abaixo (o minimapa ocupa o alto da metade de cada jogador).
            towerRoot.anchoredPosition = new Vector2(30f * s, -60f * s - (compact ? 30f : 0f));
            towerRoot.sizeDelta = new Vector2(400f * s, 5 * 40f * s);
            towerBg = new Image[5];
            towerPos = new Text[5]; towerName = new Text[5]; towerTyre = new Text[5]; towerGap = new Text[5];
            var tl = new Vector2(0f, 1f);
            int fs = Mathf.RoundToInt(22 * s);
            for (int i = 0; i < 5; i++)
            {
                var bg = UIFactory.Panel("Row" + i, towerRoot, tl, tl, new Vector2(0f, -i * 40f * s), new Vector2(400f * s, 36f * s), SecNone);
                bg.sprite = MenuTheme.RoundedSprite(100, 18, 6);
                bg.raycastTarget = false;
                towerBg[i] = bg;
                towerPos[i] = UIFactory.Label("Pos", bg.transform, "", fs, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f * s, 0f), new Vector2(54f * s, 34f * s), Color.white);
                towerName[i] = UIFactory.Label("Name", bg.transform, "", fs, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f * s, 0f), new Vector2(180f * s, 34f * s), Color.white);
                towerTyre[i] = UIFactory.Label("Tyre", bg.transform, "", fs, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(246f * s, 0f), new Vector2(30f * s, 34f * s), Color.white);
                towerGap[i] = UIFactory.Label("Gap", bg.transform, "", fs, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f * s, 0f), new Vector2(120f * s, 34f * s), Color.white);
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
                // Nome longo: inicial + sobrenome ("G. ROCHA") — antes cortava no meio ("GABRIEL ROCH").
                if (nm.Length > 12)
                {
                    int sp = nm.IndexOf(' ');
                    nm = sp > 0 && sp < nm.Length - 1 ? nm.Substring(0, 1) + ". " + nm.Substring(sp + 1) : nm;
                    if (nm.Length > 12) nm = nm.Substring(0, 11) + ".";
                }
                SetText(towerName[r], nm);
                // Carro de outro aparelho (online): o pneu dele não é simulado aqui — sem letra (antes aparecia "M" para todos).
                int cp = e.car != null && race.Settings.pitStops && !e.car.IsRemote ? RaceX.Core.PitLaneSystem.CompoundOf(e.car) : -1;
                SetText(towerTyre[r], cp >= 0 ? RaceX.Core.TyreModel.Letter(cp) : "");
                towerTyre[r].color = cp >= 0 ? RaceX.Core.TyreModel.ColorOf(cp) : Color.white;
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
                // Fora da corrida (perda total / desclassificado): mostra o motivo, não "+2 VOLTAS".
                else if (e.retired) gap = string.IsNullOrEmpty(e.retireReason) ? "ABANDONOU" : e.retireReason;
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
            liveHuds.Remove(this);
            if (race == null) return;
            race.CountdownTick -= OnCountdown;
            RaceX.Core.PitLaneSystem.TeamRadio -= OnTeamRadio;
            race.LapCompleted -= OnLap;
            race.RaceFinished -= ShowResults;
            race.RaceStarted -= OnRaceStartedRadio;
            RaceX.Core.RaceControl.Notice -= OnRaceNotice;
            OnlineClient.OnFinished -= OnOnlineOfficial;
        }

        // ---------- ONLINE: placar OFICIAL do servidor ----------
        bool onlineWaiting, onlineFallback;

        /// <summary>Cruzou a linha antes do placar oficial: aviso e espera (o servidor fecha a corrida em até 30 s depois do
        /// 1º jogador). Antes cada aparelho mostrava o próprio placar — no convidado ele aparecia em 1º tendo chegado em 6º.</summary>
        void WaitOnlineResults()
        {
            if (onlineWaiting) return;
            onlineWaiting = true;
            OnlineRaceSync.Toast("AGUARDANDO O RESULTADO OFICIAL...", 60f);
            OnlineClient.OnFinished += OnOnlineOfficial;
            StartCoroutine(OnlineResultsFallback());
        }

        void OnOnlineOfficial()
        {
            OnlineClient.OnFinished -= OnOnlineOfficial;
            OnlineRaceSync.Toast("", 0f);
            if (resultsPanel == null) ShowResults();
        }

        System.Collections.IEnumerator OnlineResultsFallback()
        {
            yield return new WaitForSecondsRealtime(50f);
            if (resultsPanel != null) yield break;
            OnlineClient.OnFinished -= OnOnlineOfficial;
            onlineFallback = true;   // servidor não respondeu: mostra o placar deste aparelho
            OnlineRaceSync.Toast("", 0f);
            ShowResults();
        }

        /// <summary>Placar OFICIAL aplicado às entradas daqui (mesma ordem, tempos e penalidades em todos os aparelhos).</summary>
        List<RaceEntry> OnlineStandings()
        {
            var byId = new Dictionary<string, RaceEntry>();
            foreach (var e in race.entries) if (!string.IsNullOrEmpty(e.netId)) byId[e.netId] = e;
            var list = new List<RaceEntry>();
            foreach (var r in OnlineClient.Rankings)
            {
                if (r.id == null || !byId.TryGetValue(r.id, out var e) || list.Contains(e)) continue;
                if (r.finished)
                {
                    e.finished = true;
                    e.retired = false;
                    if (r.totalTime > 0f) e.totalTime = r.totalTime;
                }
                else if (r.retired && !e.retired)
                {
                    e.retired = true;
                    if (string.IsNullOrEmpty(e.retireReason)) e.retireReason = "ABANDONO";
                }
                if (r.bestLap > 0f) e.bestLap = r.bestLap;
                if (r.penalty > 0f) e.penaltySeconds = r.penalty;
                list.Add(e);
            }
            foreach (var e in race.FinalStandings()) if (!list.Contains(e)) list.Add(e);
            return list;
        }

        /// <summary>Aviso da direção de prova para ESTE carro (penalidade, bandeira, peça perdida...).</summary>
        void OnRaceNotice(RaceEntry e, string msg)
        {
            if (e != entry || messageText == null) return;
            messageText.text = msg;
            messageTimer = 4.5f;
        }

        /// <summary>Faixa da bandeira (5 Hz): devolução de posição tem prioridade; verde = escondida.</summary>
        void UpdateFlagBanner()
        {
            var car = entry != null ? entry.car : null;
            if (flagBg == null || car == null) return;
            string txt = null;
            string icon = null;
            Color col = Color.white;
            var f = RaceX.Core.RaceControl.FlagOf(car);
            // Verde por 4 s quando o perigo acaba (fim de amarela/VSC/safety car) — vale mais que a azul/branca, que
            // seguem logo depois; perigo novo ou bandeira preta vêm antes da verde.
            string g = RaceX.Core.RaceControl.GreenText(car);
            bool urgent = RaceX.Core.RaceControl.IsHazard(f) || f == RaceX.Core.RaceFlag.Black || f == RaceX.Core.RaceFlag.BlackOrange;
            if (RaceX.Core.RaceControl.GiveBack(car, out string gbName, out float gbLeft))
            {
                txt = "DEVOLVA A POSIÇÃO a " + gbName + " — " + Mathf.CeilToInt(gbLeft) + " s";
                col = new Color(1f, 0.55f, 0.1f);
            }
            else if (f != RaceX.Core.RaceFlag.Green && (urgent || g.Length == 0))
            {
                txt = RaceX.Core.RaceControl.FlagText(f);
                col = RaceX.Core.RaceControl.FlagColor(f);
                icon = RaceX.Core.RaceControl.IconKey(f);
            }
            else if (g.Length > 0)
            {
                txt = g;
                col = new Color(0.2f, 0.85f, 0.3f);
                icon = "green";
            }
            bool on = !string.IsNullOrEmpty(txt);
            if (flagBg.gameObject.activeSelf != on) flagBg.gameObject.SetActive(on);
            if (on)
            {
                flagBg.color = new Color(col.r, col.g, col.b, 0.92f);
                flagText.text = txt;
                flagText.color = col.r + col.g + col.b < 0.6f ? Color.white : Color.black;
            }
            flagIconKey = on ? icon : null;
            if (flagIcon != null)
            {
                bool iconOn = flagIconKey != null;
                if (flagIcon.gameObject.activeSelf != iconOn) flagIcon.gameObject.SetActive(iconOn);
                if (iconOn)
                {
                    flagIcon.sprite = FlagIcons.Get(flagIconKey, (int)(Time.unscaledTime * 8f));
                    string lbl = flagIconKey == "sc" ? "SC" : flagIconKey == "vsc" ? "VSC" : "";
                    if (flagIconLabel.text != lbl) flagIconLabel.text = lbl;
                }
            }
            if (penaltyText != null)
            {
                string pt = entry.retired ? entry.retireReason : entry.penaltySeconds > 0f ? "+" + entry.penaltySeconds.ToString("0") + " s" : "";
                if (penaltyText.text != pt) penaltyText.text = pt;
            }
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
            // Largada por LUZES (painel): sem números na tela.
            countdownText.text = "";
            countdownTimer = 0f;
        }

        void UpdateStartLights()
        {
            if (lightsPanel == null || race == null) return;
            bool formation = race.FormationLap;
            if (formationPanel != null && formationPanel.activeSelf != formation) formationPanel.SetActive(formation);
            int n = race.LightsOn;
            bool show = !formation && n >= 0 && race.Settings.laps < 900 && (n < 6 || Time.time - race.LightsGreenAt < 2.2f);
            if (lightsPanel.activeSelf != show) lightsPanel.SetActive(show);
            if (!show || n == lastLights) return;
            lastLights = n;
            // Som das luzes: bipe curto a cada vermelha; tom longo na largada.
            if (n >= 1 && n <= 5) RaceX.Core.GameAudio.StartBeep(false);
            else if (n == 6) RaceX.Core.GameAudio.StartBeep(true);
            var off = new Color(0.16f, 0.16f, 0.18f, 1f);
            var red = new Color(1f, 0.07f, 0.04f, 1f);
            var green = new Color(0.15f, 1f, 0.3f, 1f);
            for (int i = 0; i < lightLamps.Count; i++)
            {
                int col = i / 2;
                lightLamps[i].color = n >= 6 ? green : col < n ? red : off;
            }
        }

        List<RaceEntry> celebTop3; int celebPos; float celebAt = -1f;

        /// <summary>Comemoração depois da tabela (também chamada pelo autoteste).</summary>
        public void PlayCelebrationNow(List<RaceEntry> top3, int pos)
        {
            celebAt = -1f;
            if (resultsPanel == null || top3 == null) return;
            var panelGo = resultsPanel.gameObject;
            panelGo.SetActive(false);
            if (!VictoryCinematic.Play(entry, top3, pos, () => { if (panelGo != null) panelGo.SetActive(true); }))
                panelGo.SetActive(true);
        }

        GameObject fastestPanel; Text fastestText;
        float fastestTimer, sessionFastest;

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            // Volta mais rápida da SESSÃO (qualquer carro) → faixa roxa no alto por 5 s.
            if (lapTime > 10f && (sessionFastest <= 0f || lapTime < sessionFastest - 0.0005f))
            {
                sessionFastest = lapTime;
                if (fastestPanel != null)
                {
                    string nm = string.IsNullOrEmpty(e.name) ? "?" : e.name.ToUpperInvariant();
                    fastestText.text = nm + "   " + FormatTime(lapTime);
                    fastestPanel.SetActive(true);
                    fastestTimer = 5f;
                }
            }
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
        /// <summary>Linhas "nome|carro|tempo ou diferença|melhor volta" (resultados e aba SESSÃO do hub).</summary>
        public static List<string> SessionRows(List<RaceEntry> standings, bool timed)
        {
            var rows = new List<string>();
            float pole = standings.Count > 0 && standings[0].bestLap > 0f ? standings[0].bestLap : 0f;
            for (int i = 0; i < standings.Count; i++)
            {
                var e = standings[i];
                string mid;
                if (e.retired) mid = e.retireReason ?? "";
                else if (timed) mid = i == 0 ? (pole > 0f ? "POLE" : "--") : pole > 0f && e.bestLap > 0f ? "+" + (e.bestLap - pole).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "--";
                else mid = e.finished ? FormatTime(e.totalTime) : "VOLTA " + (e.lapTimes.Count + 1);
                string best = e.bestLap > 0f ? FormatTime(e.bestLap) : "--:--.---";
                rows.Add((e.isHuman ? "*" : "") + e.name + "|" + e.model + "|" + mid + "|" + best);
            }
            return rows;
        }

        void ReopenResults()
        {
            if (resultsPanel == null) return;
            resultsHidden = false;
            resultsPanel.gameObject.SetActive(true);
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
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
                // VOLTA DE SAÍDA / NO BOX (v79): o cronômetro não corre fora de volta cronometrada —
                // zera e começa exatamente ao cruzar a linha pela 1ª vez depois do box.
                bool inPit = RaceX.Core.PitLaneSystem.IsInPitFlow(car);
                bool outLapNow = entry.outLap && !inPit;
                int bucket = inPit ? -7 : outLapNow ? -8 : Mathf.FloorToInt(current * 10f);
                float leftNow = race.SessionTimeLeft;
                if (inPit || outLapNow) bucket = bucket * 100000 - Mathf.FloorToInt(Mathf.Max(0f, leftNow)); // relógio da sessão segue vivo
                if (bucket != lastTimeBucket)
                {
                    lastTimeBucket = bucket;
                    string sess = "";
                    if (race.ChequeredFlag) sess = "  |  BANDEIRA QUADRICULADA: TERMINE A VOLTA";
                    else if (leftNow >= 0f) sess = "  |  FIM DA SESSÃO EM " + FormatTime(leftNow);
                    string lapPart = inPit ? "NO BOX" : outLapNow ? "VOLTA DE SAÍDA" : FormatTime(current);
                    timeText.text = lapPart + (entry.bestLap > 0f ? "  ·  Melhor " + FormatTime(entry.bestLap) : "") + sess;
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
                // Pneus simulados em todos os modos (com ou sem pit stop): o painel mostra sempre.
                bool pitSys = RaceX.Core.PitLaneSystem.Instance != null;
                int cpNow = pitSys ? RaceX.Core.PitLaneSystem.CompoundOf(car) : -1;
                var sys = RaceX.Car.CarSystems.Of(car);
                // Modo do painel: alterna a cada 5 s; dano novo (peça, freio ou motor) abre o (!) por 5 s.
                float dmgSum = 0f;
                if (car.damage != null) for (int z = 0; z < RaceX.Car.CarDamage.ZoneCount; z++) dmgSum += car.damage.Level(z);
                if (sys != null) { for (int i = 0; i < 4; i++) if (sys.brakeFailed[i]) dmgSum += 1f; if (sys.engineFailed) dmgSum += 1f; }
                if (lastDamageSum >= 0f && dmgSum > lastDamageSum + 0.03f) { panelMode = 1; panelModeUntil = Time.unscaledTime + PanelModeSeconds; }
                lastDamageSum = dmgSum;
                if (Time.unscaledTime >= panelModeUntil) { panelMode ^= 1; panelModeUntil = Time.unscaledTime + PanelModeSeconds; }
                bool tempMode = panelMode == 0;
                if (modeIcons != null)
                {
                    modeIcons[0].color = tempMode ? new Color(0.3f, 0.72f, 0.95f) : ModeIconOff;
                    modeIcons[1].color = !tempMode ? new Color(0.88f, 0.16f, 0.1f) : ModeIconOff;
                    modeIconText[0].color = tempMode ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    modeIconText[1].color = !tempMode ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                }
                bool anyCold = false, anyHot = false;
                for (int i = 0; i < 4; i++)
                {
                    float w = pitSys ? RaceX.Core.PitLaneSystem.WearOf(car, i) : 0f;
                    float tc = pitSys ? RaceX.Core.PitLaneSystem.WheelTempC(car, i) : 90f;
                    bool punct = pitSys && RaceX.Core.PitLaneSystem.HasPuncture(car) && w >= 0.999f;
                    // °C = só temperatura; (!) = só desgaste/dano (antes o (!) usava a cor da temperatura e o pneu frio
                    // aparecia azul com a % de vida — misturava os dois modos).
                    Color col = pitSys ? (tempMode ? TyreTempColor(cpNow, tc, punct) : (punct ? Color.black : WearColor(w))) : WearColor(w);
                    // Pressão (furo lento/rápido): no (!) a roda fica laranja→vermelha e mostra a pressão que resta.
                    float pr = pitSys ? RaceX.Core.PitLaneSystem.PressureOf(car, i) : 1f;
                    bool leaking = pitSys && RaceX.Core.PitLaneSystem.IsLeaking(car, i);
                    if (pitSys && tc < RaceX.Core.TyreModel.Low(cpNow) - 4f) anyCold = true;
                    if (pitSys && tc > RaceX.Core.TyreModel.High(cpNow) + 4f) anyHot = true;
                    wheelImgs[i].color = col;
                    if (wheelPct != null)
                    {
                        wheelPct[i].text = !pitSys ? "" : punct ? "FURO" : tempMode ? Mathf.RoundToInt(tc) + "°"
                            : pr < 0.3f ? "VAZIO" : (leaking || pr < 0.98f) ? "P" + Mathf.RoundToInt(pr * 100f) + "%" : Mathf.RoundToInt((1f - w) * 100f) + "%";
                        if (!tempMode && (leaking || pr < 0.98f)) { col = pr < 0.3f ? new Color(1f, 0.12f, 0.1f) : new Color(1f, 0.5f, 0.1f); wheelImgs[i].color = col; }
                        wheelPct[i].color = col;
                    }
                }
                // Freios e motor: °C = temperatura; (!) = desgaste/dano (duto e radiador amassados contam).
                if (brakeImgs != null)
                    for (int i = 0; i < 4; i++)
                    {
                        if (sys == null) { brakeImgs[i].color = new Color(0.3f, 0.3f, 0.3f); continue; }
                        brakeImgs[i].color = tempMode ? BrakeTempColor(sys, sys.brakeTempC[i], sys.brakeFailed[i])
                            : RaceX.Car.CarDamage.ColorFor(Mathf.Max(sys.brakeWear[i], (1f - sys.ductEff[i]) / 0.7f * 0.8f), sys.brakeFailed[i]);
                    }
                if (engineImg != null)
                {
                    if (sys == null) engineImg.color = new Color(0.3f, 0.3f, 0.3f);
                    else engineImg.color = tempMode ? EngineTempColor(sys.engineTempC, sys.engineFailed)
                        : EngineWearColor(sys.engineWear, 1f - sys.radiatorEff, sys.engineFailed);
                }
                if (fuelText != null)
                {
                    string ft = "";
                    float dl = sys != null ? sys.FuelLapsDelta() : 0f;
                    if (sys != null)
                    {
                        if (tempMode) ft = "MOTOR " + Mathf.RoundToInt(sys.engineTempC) + "° · " + sys.fuelKg.ToString("0.0") + " kg";
                        else
                        {
                            string fuelLaps = sys.Timed ? dl.ToString("0.0") + " V" : (dl >= 0f ? "+" : "") + dl.ToString("0.0") + " V";
                            ft = "COMB " + sys.fuelKg.ToString("0.0") + " kg · " + fuelLaps + (sys.fuelSaveLevel == 2 ? " ECO+" : sys.FuelSaving ? " ECO" : "");
                        }
                    }
                    if (fuelText.text != ft) fuelText.text = ft;
                    fuelText.color = sys == null ? MenuTheme.TextDim : FuelColor(sys, dl);
                }
                if (carTitle != null)
                {
                    panelTitle = cpNow < 0 ? (tempMode ? "TEMPERATURAS" : "CARRO")
                        : (tempMode ? "TEMP · " : "CARRO · ") + RaceX.Core.PitLaneSystem.CompoundLetter(cpNow) + (anyCold ? " · FRIO" : anyHot ? " · QUENTE" : "");
                    panelTitleColor = cpNow >= 0 ? RaceX.Core.TyreModel.ColorOf(cpNow) : MenuTheme.TextDim;
                    if (!pitPulseOn) { if (carTitle.text != panelTitle) carTitle.text = panelTitle; carTitle.color = panelTitleColor; }
                }
                // FURO: aviso na tela uma vez por pneu destruído.
                bool punctNow = pitSys && RaceX.Core.PitLaneSystem.HasPuncture(car);
                if (punctNow && !punctureWarned) { messageText.text = "FURO! VÁ PARA O BOX"; messageTimer = 4f; }
                punctureWarned = punctNow;
                if (weatherText != null)
                {
                    string wt = RaceX.Core.WeatherSystem.Active ? RaceX.Core.WeatherSystem.StatusText() : "";
                    if (weatherText.text != wt) weatherText.text = wt;
                    weatherText.color = RaceX.Core.WeatherSystem.KindColor(RaceX.Core.WeatherSystem.Kind);
                }
                var dmg = car.damage;
                if (dmg != null)
                {
                    dmgFrontImg.color = RaceX.Car.CarDamage.ColorFor(dmg.FrontLevel, dmg.Detached((int)RaceX.Car.DamageZone.FrontWing));
                    dmgRearImg.color = RaceX.Car.CarDamage.ColorFor(dmg.RearLevel, dmg.Detached((int)RaceX.Car.DamageZone.RearWing));
                    if (dmgLeftImg != null) dmgLeftImg.color = RaceX.Car.CarDamage.ColorFor(dmg.LeftLevel, false);
                    if (dmgRightImg != null) dmgRightImg.color = RaceX.Car.CarDamage.ColorFor(dmg.RightLevel, false);
                    // Roda: dano da própria roda (suspensão/furo/arrancada) vale MAIS que a cor do desgaste (modo (!)).
                    for (int i = 0; i < 4 && !tempMode; i++)
                    {
                        int z = (int)RaceX.Car.DamageZone.WheelFL + i;
                        bool gone = dmg.Detached(z);
                        float wl = dmg.Level(z);
                        if (!gone && wl < 0.10f) continue;
                        wheelImgs[i].color = RaceX.Car.CarDamage.ColorFor(wl, gone);
                        if (wheelPct != null) { wheelPct[i].text = gone ? "SOLTA" : "DANO"; wheelPct[i].color = gone ? Color.white : wheelImgs[i].color; }
                    }
                }
                else
                {
                    dmgFrontImg.color = ZoneColor(car.DmgFront);
                    dmgRearImg.color = ZoneColor(car.DmgRear);
                }
                UpdateFlagBanner();
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
                // v79: "PNEU M 100%" saiu do topo — a % fica ao lado de cada roda no painel CARRO.
                string tw = "";
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

            UpdateStartLights();
            if (countdownTimer > 0f)
            {
                countdownTimer -= Time.deltaTime;
                if (countdownTimer <= 0f) countdownText.text = "";
            }
            if (celebAt > 0f && Time.unscaledTime >= celebAt) PlayCelebrationNow(celebTop3, celebPos);
            if (fastestTimer > 0f)
            {
                fastestTimer -= Time.deltaTime;
                if (fastestTimer <= 0f && fastestPanel != null) fastestPanel.SetActive(false);
            }
            if (messageTimer > 0f)
            {
                messageTimer -= Time.deltaTime;
                if (messageTimer <= 0f) messageText.text = "";
            }
            // Janela de TV do safety car.
            if (scPipFrame != null)
            {
                bool pip = RaceX.Core.SafetyCar.PipActive;
                if (scPipFrame.activeSelf != pip) scPipFrame.SetActive(pip);
                if (pip)
                {
                    if (scPipImage.texture != RaceX.Core.SafetyCar.PipTexture) scPipImage.texture = RaceX.Core.SafetyCar.PipTexture;
                    string pl = RaceX.Core.SafetyCar.PipLabel;
                    if (scPipLabel.text != pl) scPipLabel.text = pl;
                }
            }
            // Bandeira tremulando (8 quadros/s; o resto do painel atualiza a 5 Hz).
            if (flagIcon != null && flagIconKey != null && flagIcon.gameObject.activeInHierarchy)
                flagIcon.sprite = FlagIcons.Get(flagIconKey, (int)(Time.unscaledTime * 8f));
            // PITSTOP piscando no título da silhueta enquanto o box está pedido (ciclo de 1,9 s medido no vídeo).
            // Cronômetro da parada: conta enquanto o carro está parado na marca e congela na liberação (3 s na tela).
            if (pitTimerPanel != null && entry != null && entry.car != null)
            {
                bool stoppedNow = RaceX.Core.PitLaneSystem.StopInfo(entry.car, out float stT, out _, out _);
                if (stoppedNow)
                {
                    pitTimerWas = true;
                    pitTimerLast = stT;
                    if (!pitTimerPanel.activeSelf) pitTimerPanel.SetActive(true);
                    pitTimerText.text = stT.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (pitTimerWas)
                {
                    pitTimerWas = false;
                    pitTimerHideAt = Time.unscaledTime + 3f;
                    pitTimerText.text = pitTimerLast.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (pitTimerPanel.activeSelf && Time.unscaledTime > pitTimerHideAt) pitTimerPanel.SetActive(false);
            }
            // Faixa PITSTOP: pisca do pedido até o carro parar no box (no vídeo segue piscando na parada e na saída;
            // aqui a parada já tem o cronômetro). Pulso suave: sobe ~0,5 s, desce ~0,5 s, apagado ~0,9 s.
            if (pitBar != null && entry != null && entry.car != null)
            {
                bool req = RaceX.Core.PitLaneSystem.IsPitRequested(entry.car) || RaceX.Core.PitLaneSystem.HeadingToBox(entry.car)
                           || RaceX.Core.PitLaneSystem.StopInfo(entry.car, out _, out _, out _)
                           || RaceX.Core.PitLaneSystem.TeamCalling(entry.car);   // equipe chamando por dano (bico, asa, furo...)
                float ph = Mathf.Repeat(Time.unscaledTime, 1.9f);
                float lit = !req ? 0f : ph < 0.5f ? ph / 0.5f : ph < 1f ? 1f - (ph - 0.5f) / 0.5f : 0f;
                pitBar.color = Color.Lerp(PitBarDim, PitBarLit, lit);
                if (pitBarText != null) pitBarText.color = new Color(1f, 1f, 1f, req ? 0.95f : 0.35f);
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
            if (Time.timeScale <= 0f) return; // pausado: mantém o último valor (antes mostrava "5 FPS (min 1)")
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

        void OnTeamRadio(RaceX.Car.CarController c, string msg)
        {
            if (entry == null || c != entry.car || messageText == null) return;
            // Chamadas CRÍTICAS (dano/furo) passam mesmo com o rádio do pit desligado (ele só silencia os avisos de rotina).
            bool critical = msg.StartsWith("BOX, BOX") || msg.StartsWith("FURO") || msg.StartsWith("Perda de pressão");
            if (SettingsManager.PitRadio == 0 && !critical) return;
            messageText.text = "RÁDIO: " + msg;
            messageTimer = 6f;
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
            bool onlineRace = race.Settings != null && race.Settings.mode == GameMode.Online;
            if (onlineRace && OnlineClient.Rankings.Count == 0 && !onlineFallback) { WaitOnlineResults(); return; }
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

            var standings = onlineRace && OnlineClient.Rankings.Count > 0 ? OnlineStandings() : race.FinalStandings();
            int myPos = standings.FindIndex(x => x == entry) + 1;
            bool timedSession = race.Settings.laps >= 900;
            // Abandono (perda total / desclassificado) não pontua — como na F1 ("não classificado"). NA CLASSIFICAÇÃO NÃO:
            // a melhor volta feita antes da batida VALE (regra da F1; ex. Leclerc, Mônaco 2021: bateu no Q3 e manteve a
            // pole). Antes o jogador com P1 no Q1 e perda total ia para o último lugar do grid.
            if (entry.retired && !(timedSession && entry.bestLap > 0f)) myPos = standings.Count;
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
            // Treino/classificação: ordem pela MELHOR VOLTA com TODOS os nomes (quem bateu continua com o tempo que fez).
            // Antes o nome de quem deu perda total entrava VAZIO na ordem → ia para o grid das sessões seguintes como
            // "carro sem nome" (e sem equipe, vermelho). Corrida: abandono continua vazio (não pontua).
            for (int i = 0; i < standings.Count; i++) order.Add(timedSession ? standings[i].name : (standings[i].retired ? "" : standings[i].name));
            if (careerRace)
            {
                RaceX.Career.CareerMode.PlayerCarTotaled = entry.car != null && entry.car.wrecked;
                RaceX.Career.CareerMode.PlayerOutReason = entry.retired ? entry.retireReason ?? "" : "";
            }
            if (careerRace && RaceX.Career.CareerMode.data != null)
            {
                // Guarda a tabela desta sessão para a aba SESSÃO do hub (vale depois de sair da cena).
                var cd = RaceX.Career.CareerMode.data;
                cd.lastSessionTitle = RaceX.Career.CareerMode.SessionTitle(careerPhase);
                cd.lastSessionRows = SessionRows(standings, race.Settings.laps >= 900);
            }
            if (careerRace) RaceX.Career.CareerMode.OnCareerSessionFinished(myPos, standings.Count, order);
            bool realRace = race.Settings.laps < 900 && (!careerRace || careerPhase == RaceX.Career.CareerMode.PH_RACE || careerPhase == RaceX.Career.CareerMode.PH_SPRINT);
            if (realRace && entry.finished) PodiumCelebration.Play(resultsPanel, myPos);
            // Pódio do jogador numa corrida: COMEMORAÇÃO CINEMATOGRÁFICA antes da tabela (pode pular).
            // ORDEM (pedido do usuário): primeiro a COMEMORAÇÃO e o PÓDIO; a tabela com os 24 carros e as opções só aparece
            // quando ela acaba ou ao tocar em PULAR / PULAR TUDO. A interface inteira fica escondida durante a comemoração.
            if (realRace && entry.finished && myPos >= 1 && myPos <= 3 && standings.Count >= 3)
            {
                celebTop3 = standings.GetRange(0, 3);
                celebPos = myPos;
                celebAt = Time.unscaledTime;
            }
            // Tabela em COLUNAS FIXAS (antes uma linha de texto só: nomes de larguras diferentes empurravam carro/tempo).
            // Treino/classificação: a coluna do meio mostra a diferença para a melhor volta do P1 (tempo total não existe).
            bool timed = race.Settings.laps >= 900;
            float poleLap = standings.Count > 0 && standings[0].bestLap > 0f ? standings[0].bestLap : 0f;
            var tl0 = new Vector2(0f, 1f);
            var innerTr = (RectTransform)inner.transform;
            // TODOS OS CARROS (antes cortava em 16 de 24): DUAS COLUNAS de 12 linhas, faixa da cor da equipe, linhas
            // alternadas, melhor volta da sessão em ROXO e abandono em vermelho.
            float fastest = 0f;
            foreach (var e0 in standings) if (e0.bestLap > 0f && (fastest <= 0f || e0.bestLap < fastest)) fastest = e0.bestLap;
            int perCol = Mathf.Max(1, Mathf.CeilToInt(standings.Count / 2f));
            if (standings.Count <= 12) perCol = standings.Count;
            float rowH = Mathf.Min(40f, 470f / Mathf.Max(1, perCol));
            string note = careerRace ? RaceX.Career.CareerMode.QualiNote : "";
            if (!string.IsNullOrEmpty(note))
            {
                var nt = UIFactory.Label("QualiNote", innerTr, note, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(1120f, 44f), new Color(1f, 0.55f, 0.2f));
                nt.horizontalOverflow = HorizontalWrapMode.Wrap;
                RaceX.Career.CareerMode.QualiNote = "";
            }
            for (int col = 0; col < 2 && col * perCol < standings.Count; col++)
            {
                float bx = 40f + col * 566f;
                string[] hh = { "POS", "PILOTO", timed ? "DIF." : "TEMPO", "MELHOR" };
                float[] hx = { 14f, 70f, 330f, 450f };
                for (int c2 = 0; c2 < hh.Length; c2++)
                    UIFactory.Label("ColHead" + col + "_" + c2, innerTr, hh[c2], 16, TextAnchor.MiddleLeft, tl0, tl0, new Vector2(bx + hx[c2], -140f), new Vector2(140f, 22f), MenuTheme.TextDim);
            }
            for (int i = 0; i < standings.Count; i++)
            {
                var e = standings[i];
                // PadRight: motivo curto ("PT") dava Substring fora do texto e a tela de resultados não abria.
                string total;
                if (e.retired) total = e.retireReason ?? "";
                else if (timed) total = i == 0 || poleLap <= 0f || e.bestLap <= 0f ? (i == 0 ? "POLE" : "--") : "+" + (e.bestLap - poleLap).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                else total = e.finished ? FormatTime(e.totalTime) : "NA PISTA";
                if (e.finished && e.penaltySeconds > 0f) total += " (+" + e.penaltySeconds.ToString("0") + "s)";
                string best = e.bestLap > 0f ? FormatTime(e.bestLap) : "--:--.---";
                Color rowColor = i == 0 ? Gold : (i == 1 ? Silver : (i == 2 ? Bronze : MenuTheme.Text));
                int col = i / perCol, ri = i % perCol;
                float bx = 40f + col * 566f;
                float y = -164f - ri * rowH;
                Color rowBg = e.isHuman ? new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.22f)
                                        : (ri % 2 == 0 ? new Color(1f, 1f, 1f, 0.045f) : new Color(1f, 1f, 1f, 0.015f));
                var rb = UIFactory.Panel("RowBg" + i, inner.transform, tl0, tl0, new Vector2(bx, y), new Vector2(552f, rowH - 3f), rowBg);
                rb.raycastTarget = false;
                // faixa com a cor do carro (equipe)
                Color teamC = e.car != null ? CarPalette.Get(e.colorIndex) : Color.gray;
                var stripe = UIFactory.Panel("RowTeam" + i, inner.transform, tl0, tl0, new Vector2(bx + 56f, y - 4f), new Vector2(6f, rowH - 11f), teamC);
                stripe.raycastTarget = false;
                Color totalC = e.retired ? new Color(1f, 0.38f, 0.32f) : rowColor;
                bool fl = fastest > 0f && Mathf.Abs(e.bestLap - fastest) < 0.0005f;
                if (fl)
                {
                    var fb = UIFactory.Panel("RowFL" + i, inner.transform, tl0, tl0, new Vector2(bx + 444f, y - 3f), new Vector2(104f, rowH - 9f), new Color(0.47f, 0.13f, 0.78f, 0.95f));
                    fb.raycastTarget = false;
                }
                string nm = string.IsNullOrEmpty(e.name) ? "—" : e.name;
                string[] cells = { (i + 1) + "º", nm, total, best };
                float[] cx = { 14f, 70f, 330f, 450f };
                float[] cw = { 50f, 250f, 115f, 100f };
                Color[] cc = { rowColor, rowColor, totalC, fl ? Color.white : rowColor };
                int fs = Mathf.RoundToInt(Mathf.Min(24f, rowH * 0.62f));
                for (int c2 = 0; c2 < cells.Length; c2++)
                {
                    var t = UIFactory.Label("Row" + i + "_" + c2, innerTr, cells[c2], fs, TextAnchor.MiddleLeft, tl0, tl0, new Vector2(bx + cx[c2], y), new Vector2(cw[c2], rowH - 3f), cc[c2]);
                    MenuTheme.FitText(t, fs);
                }
            }

            // Botões numa fileira SÓ, abaixo da tabela e dentro do painel (antes REPLAY e CLASSIFICAÇÃO/HUB ficavam
            // metade fora do painel e por cima das últimas linhas).
            var b = new Vector2(0.5f, 0f);
            var innerRt = (RectTransform)inner.transform;
            var row = new List<KeyValuePair<string, System.Action>>();
            row.Add(new KeyValuePair<string, System.Action>("REPLAY", () => RaceX.Core.ReplaySystem.Begin(race, carCamera, resultsPanel)));
            if (celebTop3 != null)
                row.Add(new KeyValuePair<string, System.Action>("COMEMORAÇÃO", () => PlayCelebrationNow(celebTop3, celebPos)));
            if (careerRace)
            {
                // Carreira: um botão só para o HUB (CONTINUAR e CLASSIFICAÇÃO/HUB faziam a mesma coisa). O hub abre
                // como OVERLAY (timeScale 0); VOLTAR no hub reabre este painel. Sem MENU PRINCIPAL: a saída é pelo hub.
                row.Add(new KeyValuePair<string, System.Action>("CONTINUAR", () =>
                {
                    Time.timeScale = 0f;
                    if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
                    RaceX.UI.CareerFlow.Open(() => { ResumeTime(); ReopenResults(); });
                }));
            }
            else if (onlineRace)
            {
                // ONLINE: JOGAR NOVAMENTE pede a NOVA PARTIDA aos outros (SIM/NÃO, 10 s) e quem aceita corre junto NA MESMA
                // SALA. Antes recarregava a pista só neste aparelho: cada um ia correr sozinho com os nomes dos outros na tela.
                row.Add(new KeyValuePair<string, System.Action>("JOGAR NOVAMENTE", () => OnlineClient.RequestVote("rematch")));
                row.Add(new KeyValuePair<string, System.Action>("CONTINUAR", () => { if (resultsPanel != null) resultsPanel.gameObject.SetActive(false); resultsHidden = true; }));
                row.Add(new KeyValuePair<string, System.Action>("MENU PRINCIPAL", () => { OnlineClient.Disconnect(); SceneLoader.LoadMainMenu(); }));
            }
            else
            {
                row.Add(new KeyValuePair<string, System.Action>("JOGAR NOVAMENTE", () => SceneLoader.ReloadCurrent()));
                row.Add(new KeyValuePair<string, System.Action>("CONTINUAR", () => { if (resultsPanel != null) resultsPanel.gameObject.SetActive(false); resultsHidden = true; }));
                row.Add(new KeyValuePair<string, System.Action>("MENU PRINCIPAL", () => SceneLoader.LoadMainMenu()));
            }
            float bw = row.Count <= 2 ? 380f : 262f, gap = 18f;
            float x0 = -(row.Count - 1) * (bw + gap) * 0.5f;
            for (int k = 0; k < row.Count; k++)
                MenuTheme.StyledButton(innerRt, "ResBtn" + k, row[k].Key, b, b, new Vector2(x0 + k * (bw + gap), 22f), new Vector2(bw, 70f), 26, row[k].Value);
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
