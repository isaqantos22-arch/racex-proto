using RaceX.Car;
using RaceX.Core;
using RaceX.Input;
using UnityEngine;

namespace RaceX.UI
{
    public class PauseMenu : MonoBehaviour
    {
        static readonly string[] ControlNames = { "Botões na tela", "Inclinação", "Joystick virtual" };

        public RaceManager race;
        public bool online;

        GameObject panel;
        GameObject settingsPanel;
        GameObject strategyPanel;
        UnityEngine.UI.Text volumeLabel;
        UnityEngine.UI.Text controlsLabel;
        UnityEngine.UI.Text minimapLabel;
        UnityEngine.UI.Text autoThrottleLabel;
        UnityEngine.UI.Text strategyStopsLabel, strategyStartLabel, strategyStopsCpdLabel;

        public bool IsOpen => panel != null && panel.activeSelf;

        public static PauseMenu Create(Canvas canvas, RaceManager race, bool online)
        {
            var root = UIFactory.Stretch("PauseMenu", canvas.transform);
            var menu = root.gameObject.AddComponent<PauseMenu>();
            menu.race = race;
            menu.online = online;
            menu.Build(root);
            return menu;
        }

        void Build(RectTransform root)
        {
            var dim = UIFactory.Panel("Pause", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.7f));
            var rt = (RectTransform)dim.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            panel = dim.gameObject;

            var c = new Vector2(0.5f, 0.5f);
            // Carreira: MENU DA CARREIRA volta ao hub com o relógio REAL retomado (a sessão segue viva lá).
            bool careerBtn = race != null && race.Settings.careerPhase >= 0;
            bool stratVisible = StrategyVisible;
            // LISTA DINÂMICA (v79): os botões são empilhados conforme os que existem — antes as posições
            // eram fixas para 6 botões, sobrava um buraco sem a ESTRATÉGIA e o título/MENU saíam cortados.
            var items = new System.Collections.Generic.List<(string name, string label, System.Action act)>();
            items.Add(("Continue", "CONTINUAR", new System.Action(Toggle)));
            if (careerBtn)
                items.Add(("CareerHub", "MENU DA CARREIRA", (System.Action)(() =>
                {
                    panel.SetActive(false);
                    Time.timeScale = 1f; // corrida segue com a IA pilotando seu carro (treino real)
                    RaceX.Career.CareerMode.ResumeSessionClock(); // sessão corre viva dentro do hub
                    RaceX.UI.CareerFlow.Open();
                })));
            items.Add(("Restart", "REINICIAR", new System.Action(SceneLoader.ReloadCurrent)));
            if (stratVisible)
                items.Add(("Strategy", "ESTRATÉGIA", (System.Action)(() =>
                {
                    if (strategyPanel != null) strategyPanel.SetActive(!strategyPanel.activeSelf);
                    settingsPanel.SetActive(false);
                })));
            items.Add(("Settings", "CONFIGURAÇÕES", (System.Action)(() =>
            {
                settingsPanel.SetActive(!settingsPanel.activeSelf);
                if (strategyPanel != null) strategyPanel.SetActive(false);
            })));
            items.Add(("Quit", "SAIR PARA O MENU", new System.Action(SceneLoader.LoadMainMenu)));

            const float btnH = 84f, step = 98f, titleH = 110f;
            float totalH = titleH + items.Count * step;
            float topY = totalH * 0.5f;
            UIFactory.Label("Title", panel.transform, "PAUSA", 72, TextAnchor.MiddleCenter, c, c, new Vector2(0f, topY - titleH * 0.5f), new Vector2(800f, titleH));
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float y = topY - titleH - step * i - step * 0.5f;
                UIFactory.Button(it.name, panel.transform, it.label, c, c, new Vector2(0f, y), new Vector2(520f, btnH), it.act);
            }
            if (stratVisible) BuildStrategyPanel(panel.transform, c, new Vector2(-560f, 0f));

            bool mobile = Application.isMobilePlatform;
            float panelH = mobile ? 400f : 320f;
            var sp = UIFactory.Panel("SettingsPanel", panel.transform, c, c, new Vector2(560f, 0f), new Vector2(480f, panelH), new Color(0.1f, 0.12f, 0.16f, 0.95f));
            settingsPanel = sp.gameObject;
            volumeLabel = UIFactory.Label("Volume", sp.transform, "", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(440f, 60f));
            UIFactory.Button("VolDown", sp.transform, "-", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-20f, panelH - 70f), new Vector2(120f, 90f), () => ChangeVolume(-0.1f), 50);
            UIFactory.Button("VolUp", sp.transform, "+", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(20f, panelH - 70f), new Vector2(120f, 90f), () => ChangeVolume(0.1f), 50);
            ChangeVolume(0f);

            // Mapa dinâmico — pode ser mudado a qualquer momento durante a corrida (não define a pista/regras).
            minimapLabel = UIFactory.Label("Minimap", sp.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(440f, 50f));
            UIFactory.Button("MinimapToggle", sp.transform, "ALTERNAR", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(260f, 60f), ToggleMinimap, 26);
            RefreshMinimapLabel();

            // Método de controle — apenas em mobile; a troca é aplicada na hora, sem reiniciar a corrida.
            if (mobile)
            {
                controlsLabel = UIFactory.Label("Controls", sp.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -250f), new Vector2(440f, 50f));
                UIFactory.Button("ControlsPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-190f, -290f), new Vector2(90f, 60f), () => ChangeControlMode(-1), 32);
                UIFactory.Button("ControlsNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(110f, -290f), new Vector2(90f, 60f), () => ChangeControlMode(1), 32);
                RefreshControlsLabel();

                // Aceleração automática — opção permanente (o toggle em jogo foi removido).
                autoThrottleLabel = UIFactory.Label("AutoThrottle", sp.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -330f), new Vector2(440f, 50f));
                UIFactory.Button("AutoThrottleToggle", sp.transform, "ALTERNAR", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-40f, -370f), new Vector2(260f, 60f), ToggleAutoThrottle, 26);
                RefreshAutoThrottleLabel();
                if (panelH < 460f) panelH = 460f;
            }

            settingsPanel.SetActive(false);
            panel.SetActive(false);
        }

        // ---- ESTRATÉGIA DE PIT (paradas + compostos) ----

        CarController HumanCar()
        {
            if (race == null) return null;
            foreach (var e in race.entries)
                if (e.isHuman && e.car != null) return e.car;
            return null;
        }

        /// <summary>Estratégia só faz sentido em corrida por distância com pit ativo e jogador humano.</summary>
        bool StrategyVisible
        {
            get
            {
                if (race == null || race.Settings.laps >= 900) return false;
                if (RaceX.Core.PitLaneSystem.Instance == null || !RaceX.Core.PitLaneSystem.PitEnabled) return false;
                return HumanCar() != null;
            }
        }

        void BuildStrategyPanel(Transform parent, Vector2 anchor, Vector2 pos)
        {
            var c = new Vector2(0.5f, 0.5f);
            var sp = UIFactory.Panel("StrategyPanel", parent, c, c, pos, new Vector2(440f, 300f), new Color(0.1f, 0.12f, 0.16f, 0.95f));
            strategyPanel = sp.gameObject;
            strategyStopsLabel = UIFactory.Label("StratStops", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(400f, 50f));
            strategyStartLabel = UIFactory.Label("StratStart", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(400f, 50f));
            strategyStopsCpdLabel = UIFactory.Label("StratStopCpd", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(400f, 50f));
            UIFactory.Button("StopsPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -44f), new Vector2(80f, 60f), () => ChangeStrategy(-1, 0, 0), 34);
            UIFactory.Button("StopsNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -44f), new Vector2(80f, 60f), () => ChangeStrategy(1, 0, 0), 34);
            UIFactory.Button("StartPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -124f), new Vector2(80f, 60f), () => ChangeStrategy(0, -1, 0), 34);
            UIFactory.Button("StartNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -124f), new Vector2(80f, 60f), () => ChangeStrategy(0, 1, 0), 34);
            UIFactory.Button("StopPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -204f), new Vector2(80f, 60f), () => ChangeStrategy(0, 0, -1), 34);
            UIFactory.Button("StopNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -204f), new Vector2(80f, 60f), () => ChangeStrategy(0, 0, 1), 34);
            RefreshStrategyLabels();
            strategyPanel.SetActive(false);
        }

        void ChangeStrategy(int stopsDelta, int startDelta, int stopDelta)
        {
            var car = HumanCar();
            if (car == null) return;
            int s = Mathf.Clamp(RaceX.Core.PitLaneSystem.PlannedStops(car) + stopsDelta, 0, 3);
            int sc = Mathf.Clamp(RaceX.Core.PitLaneSystem.StrategyStartCompound(car) + startDelta, 0, 2);
            int cc = Mathf.Clamp(RaceX.Core.PitLaneSystem.StrategyStopCompound(car) + stopDelta, 0, 2);
            RaceX.Core.PitLaneSystem.SetPlayerStrategy(car, s, sc, cc);
            RefreshStrategyLabels();
        }

        void RefreshStrategyLabels()
        {
            var car = HumanCar();
            if (car == null) return;
            strategyStopsLabel.text = "Paradas: < " + RaceX.Core.PitLaneSystem.PlannedStops(car) + " >";
            strategyStartLabel.text = "Pneu de largada: < " + RaceX.Core.PitLaneSystem.CompoundLetter(RaceX.Core.PitLaneSystem.StrategyStartCompound(car)) + " >";
            strategyStopsCpdLabel.text = "Pneus das paradas: < " + RaceX.Core.PitLaneSystem.CompoundLetter(RaceX.Core.PitLaneSystem.StrategyStopCompound(car)) + " >";
        }

        void ChangeVolume(float delta)
        {
            float v = Mathf.Clamp01(Mathf.Round((SettingsManager.MasterVolume + delta) * 10f) / 10f);
            SettingsManager.MasterVolume = v;
            AudioListener.volume = v;
            volumeLabel.text = $"Volume geral: {Mathf.RoundToInt(v * 100f)}%";
        }

        void ToggleMinimap()
        {
            SettingsManager.MinimapDynamic = !SettingsManager.MinimapDynamic;
            RefreshMinimapLabel();
        }

        void RefreshMinimapLabel()
        {
            minimapLabel.text = "Mini-mapa: " + (SettingsManager.MinimapDynamic ? "Dinâmico (trecho)" : "Pista inteira");
        }

        void ToggleAutoThrottle()
        {
            TouchControlsUI.AutoThrottleEnabled = !TouchControlsUI.AutoThrottleEnabled;
            RefreshAutoThrottleLabel();
        }

        void RefreshAutoThrottleLabel()
        {
            autoThrottleLabel.text = "Aceleramento automático: " + (TouchControlsUI.AutoThrottleEnabled ? "LIGADO" : "DESLIGADO");
        }

        void ChangeControlMode(int delta)
        {
            SettingsManager.ControlMode = SettingsManager.ControlMode == ControlMode.Buttons ? ControlMode.Joystick : ControlMode.Buttons;
            TouchControlsUI.ApplyModeToAll(SettingsManager.ControlMode);
            RefreshControlsLabel();
        }

        void RefreshControlsLabel()
        {
            controlsLabel.text = "Controles: " + ControlNames[(int)SettingsManager.ControlMode];
        }

        int lastToggleFrame = -1;

        public void Toggle()
        {
            // Após o fim da sessão a pausa só continua valendo quando a tabela de resultados está
            // OCULTA (botão CONTINUAR) — com resultados na tela, o Toggle fica bloqueado como antes.
            if (race != null && race.State == RaceState.Finished && !HUD.AnyResultsHidden()) return;
            if (lastToggleFrame == Time.frameCount) return;
            lastToggleFrame = Time.frameCount;
            bool open = !panel.activeSelf;
            panel.SetActive(open);
            settingsPanel.SetActive(false);
            if (strategyPanel != null) strategyPanel.SetActive(false);
            transform.SetAsLastSibling();
            if (!online)
            {
                Time.timeScale = open ? 0f : 1f;
                Time.fixedDeltaTime = 0.01f; // 100 Hz ORIGINAL (0.02 degradava a fricção das rodas e limitava todos os carros)
                if (open) RaceX.Career.CareerMode.PauseSessionClock();
                else RaceX.Career.CareerMode.ResumeSessionClock();
            }
            AudioListener.pause = open && !online;
            if (!open) SettingsManager.Save();
        }

        void OnDestroy()
        {
            if (!online) Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.01f;
            AudioListener.pause = false;
            RaceX.Career.CareerMode.ResumeSessionClock();
        }
    }
}
