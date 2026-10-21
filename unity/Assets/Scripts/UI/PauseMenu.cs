using RaceX.Core;
using UnityEngine;

namespace RaceX.UI
{
    public class PauseMenu : MonoBehaviour
    {
        public RaceManager race;
        public bool online;

        GameObject panel;
        GameObject settingsPanel;
        UnityEngine.UI.Text volumeLabel;

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
            UIFactory.Button("Continue", panel.transform, "CONTINUAR", c, c, new Vector2(0f, 150f), new Vector2(520f, 100f), Toggle);
            UIFactory.Button("Restart", panel.transform, "REINICIAR", c, c, new Vector2(0f, 30f), new Vector2(520f, 100f), SceneLoader.ReloadCurrent);
            UIFactory.Button("Settings", panel.transform, "CONFIGURAÇÕES", c, c, new Vector2(0f, -90f), new Vector2(520f, 100f), () => settingsPanel.SetActive(!settingsPanel.activeSelf));
            UIFactory.Button("Quit", panel.transform, "SAIR PARA O MENU", c, c, new Vector2(0f, -210f), new Vector2(520f, 100f), SceneLoader.LoadMainMenu);

            var sp = UIFactory.Panel("SettingsPanel", panel.transform, c, c, new Vector2(560f, -30f), new Vector2(480f, 220f), new Color(0.1f, 0.12f, 0.16f, 0.95f));
            settingsPanel = sp.gameObject;
            volumeLabel = UIFactory.Label("Volume", sp.transform, "", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(440f, 60f));
            UIFactory.Button("VolDown", sp.transform, "-", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 30f), new Vector2(120f, 90f), () => ChangeVolume(-0.1f), 50);
            UIFactory.Button("VolUp", sp.transform, "+", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(20f, 30f), new Vector2(120f, 90f), () => ChangeVolume(0.1f), 50);
            ChangeVolume(0f);
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
            if (!online) Time.timeScale = open ? 0f : 1f;
            AudioListener.pause = open && !online;
            if (!open) SettingsManager.Save();
        }

        void OnDestroy()
        {
            if (!online) Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
