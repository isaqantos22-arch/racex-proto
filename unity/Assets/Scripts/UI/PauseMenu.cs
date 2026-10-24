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
        UnityEngine.UI.Text volumeLabel;
        UnityEngine.UI.Text controlsLabel;
        UnityEngine.UI.Text minimapLabel;
        UnityEngine.UI.Text autoThrottleLabel;

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
            UIFactory.Label("Title", panel.transform, "PAUSA", 80, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 300f), new Vector2(800f, 120f));
            // Carreira: volta ao HUB com o relógio REAL correndo (a sessão NÃO pausa — como na F1).
            bool careerBtn = race != null && race.Settings.careerPhase >= 0;
            // 5 botões sem sobreposição: linha própria para cada um.
            UIFactory.Button("Continue", panel.transform, "CONTINUAR", c, c, new Vector2(0f, careerBtn ? 210f : 150f), new Vector2(520f, 100f), Toggle);
            if (careerBtn)
                UIFactory.Button("CareerHub", panel.transform, "MENU DA CARREIRA (TEMPO CORRENDO)", c, c, new Vector2(0f, 90f), new Vector2(520f, 100f), () =>
                {
                    panel.SetActive(false);
                    Time.timeScale = 1f; // corrida segue com a IA pilotando seu carro (treino real)
                    RaceX.UI.CareerFlow.Open();
                });
            UIFactory.Button("Restart", panel.transform, "REINICIAR", c, c, new Vector2(0f, careerBtn ? -30f : 30f), new Vector2(520f, 100f), SceneLoader.ReloadCurrent);
            UIFactory.Button("Settings", panel.transform, "CONFIGURAÇÕES", c, c, new Vector2(0f, careerBtn ? -150f : -90f), new Vector2(520f, 100f), () => settingsPanel.SetActive(!settingsPanel.activeSelf));
            UIFactory.Button("Quit", panel.transform, "SAIR PARA O MENU", c, c, new Vector2(0f, careerBtn ? -270f : -210f), new Vector2(520f, 100f), SceneLoader.LoadMainMenu);

            bool mobile = Application.isMobilePlatform;
            float panelH = mobile ? 400f : 320f;
            var sp = UIFactory.Panel("SettingsPanel", panel.transform, c, c, new Vector2(560f, -30f), new Vector2(480f, panelH), new Color(0.1f, 0.12f, 0.16f, 0.95f));
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
            int count = ControlNames.Length;
            int next = ((int)SettingsManager.ControlMode + delta % count + count) % count;
            SettingsManager.ControlMode = (ControlMode)next;
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
            if (race != null && race.State == RaceState.Finished) return;
            if (lastToggleFrame == Time.frameCount) return;
            lastToggleFrame = Time.frameCount;
            bool open = !panel.activeSelf;
            panel.SetActive(open);
            settingsPanel.SetActive(false);
            transform.SetAsLastSibling();
            if (!online)
            {
                Time.timeScale = open ? 0f : 1f;
                Time.fixedDeltaTime = 0.02f; // passo de física fixo (colisões estáveis)
            }
            AudioListener.pause = open && !online;
            if (!open) SettingsManager.Save();
        }

        void OnDestroy()
        {
            if (!online) Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            AudioListener.pause = false;
        }
    }
}
