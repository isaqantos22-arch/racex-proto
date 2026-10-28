using UnityEngine;

namespace RaceX.Core
{
    public enum ControlMode { Buttons = 0, Tilt = 1, Joystick = 2 }
    public enum QualityTier { Low = 0, Medium = 1, High = 2, Ultra = 3 }

    public static class SettingsManager
    {
        const string Prefix = "RaceX.";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f);
            set { PlayerPrefs.SetFloat(Prefix + "MasterVolume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); }
        }

        public static float EngineVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "EngineVolume", 1f);
            set => PlayerPrefs.SetFloat(Prefix + "EngineVolume", Mathf.Clamp01(value));
        }

        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt(Prefix + "MusicOn", 1) == 1;
            set => PlayerPrefs.SetInt(Prefix + "MusicOn", value ? 1 : 0);
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "MusicVolume", 0.7f);
            set => PlayerPrefs.SetFloat(Prefix + "MusicVolume", Mathf.Clamp01(value));
        }

        public static bool AllowF1
        {
            get => PlayerPrefs.GetInt(Prefix + "AllowF1", 0) == 1;
            set => PlayerPrefs.SetInt(Prefix + "AllowF1", value ? 1 : 0);
        }

        public static int OnlineAiCount
        {
            get => PlayerPrefs.GetInt(Prefix + "OnlineAiCount", 6);
            set => PlayerPrefs.SetInt(Prefix + "OnlineAiCount", Mathf.Clamp(value, 0, 8));
        }

        public static bool MinimapDynamic
        {
            get => PlayerPrefs.GetInt(Prefix + "MinimapDynamic", 1) == 1;
            set => PlayerPrefs.SetInt(Prefix + "MinimapDynamic", value ? 1 : 0);
        }

        public static bool NameTags
        {
            get => PlayerPrefs.GetInt(Prefix + "NameTags", 1) == 1;
            set => PlayerPrefs.SetInt(Prefix + "NameTags", value ? 1 : 0);
        }

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(Prefix + "ShowFps", 1) == 1;
            set => PlayerPrefs.SetInt(Prefix + "ShowFps", value ? 1 : 0);
        }

        /// <summary>Rádio do pit: 0 = Desligado, 1 = Ligado. A chave do PlayerPrefs é a MESMA de antes
        /// (valores antigos 1..5 de limiar viram "Ligado" automaticamente — cliente compatível).</summary>
        public static int PitRadio
        {
            get => PlayerPrefs.GetInt(Prefix + "PitRadio", 3) == 0 ? 0 : 1;
            set => PlayerPrefs.SetInt(Prefix + "PitRadio", value <= 0 ? 0 : 1);
        }

        public static bool SfxOn
        {
            get => PlayerPrefs.GetInt(Prefix + "SfxOn", 1) == 1;
            set => PlayerPrefs.SetInt(Prefix + "SfxOn", value ? 1 : 0);
        }

        public static ControlMode ControlMode
        {
            get { var m = (ControlMode)PlayerPrefs.GetInt(Prefix + "ControlMode", (int)ControlMode.Buttons); return m == ControlMode.Tilt ? ControlMode.Joystick : m; }
            set => PlayerPrefs.SetInt(Prefix + "ControlMode", (int)value);
        }

        public static float TiltSensitivity
        {
            get => PlayerPrefs.GetFloat(Prefix + "TiltSensitivity", 1f);
            set => PlayerPrefs.SetFloat(Prefix + "TiltSensitivity", Mathf.Clamp(value, 0.5f, 2f));
        }

        public static int LastCamera
        {
            get => PlayerPrefs.GetInt(Prefix + "LastCamera", 0);
            set => PlayerPrefs.SetInt(Prefix + "LastCamera", value);
        }

        public static QualityTier Quality
        {
            get
            {
                if (!PlayerPrefs.HasKey(Prefix + "Quality")) Quality = DetectQuality();
                return (QualityTier)PlayerPrefs.GetInt(Prefix + "Quality");
            }
            set => PlayerPrefs.SetInt(Prefix + "Quality", (int)value);
        }

        public static QualityTier DetectQuality()
        {
            if (!Application.isMobilePlatform) return SystemInfo.graphicsMemorySize >= 6000 ? QualityTier.Ultra : QualityTier.High;
            int mem = SystemInfo.systemMemorySize;
            if (mem >= 8000) return QualityTier.High;
            if (mem >= 4000) return QualityTier.Medium;
            return QualityTier.Low;
        }

        public static void ApplyQuality(QualityTier tier, bool splitScreen = false)
        {
            if (splitScreen && tier > QualityTier.Low) tier--;
            int levels = QualitySettings.names.Length;
            if (levels > 0)
            {
                int lv = tier == QualityTier.Low ? 1 : Mathf.RoundToInt((float)tier / 3f * (levels - 1));
                QualitySettings.SetQualityLevel(Mathf.Clamp(lv, 0, levels - 1), true);
            }
#if !UNITY_EDITOR
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = tier == QualityTier.Low ? 0.85f : 1f;
                urp.msaaSampleCount = tier >= QualityTier.High ? 2 : 1;
            }
#endif
            switch (tier)
            {
                case QualityTier.Low: QualitySettings.shadowDistance = 0f; break;
                case QualityTier.Medium: QualitySettings.shadowDistance = 30f; break;
                case QualityTier.High: QualitySettings.shadowDistance = 50f; break;
                default: QualitySettings.shadowDistance = 80f; break;
            }
            ApplyFrameRate(tier);
        }

        /// <summary>Limite de FPS escolhido em CONFIGURAÇÕES: 0 = AUTO (celular: 60 em Alta/Ultra, 30 em
        /// Baixa/Média; PC: sem limite), ou 30 / 60 / 120. 120 só vale em telas que suportam.</summary>
        public static readonly int[] FpsOptions = { 0, 30, 60, 120 };

        public static int TargetFps
        {
            get { int v = PlayerPrefs.GetInt(Prefix + "TargetFps", 0); return System.Array.IndexOf(FpsOptions, v) >= 0 ? v : 0; }
            set => PlayerPrefs.SetInt(Prefix + "TargetFps", System.Array.IndexOf(FpsOptions, value) >= 0 ? value : 0);
        }

        public static string FpsLabel(int v) => v <= 0 ? "AUTO" : v + " FPS";

        public static void ApplyFrameRate(QualityTier tier)
        {
            int fps = TargetFps;
            if (fps <= 0)
            {
                Application.targetFrameRate = Application.isMobilePlatform ? (tier >= QualityTier.High ? 60 : 30) : -1;
                return;
            }
            // Limite manual: sem V-Sync no PC (o V-Sync ignora targetFrameRate).
            if (!Application.isMobilePlatform) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }

        public static float RenderDistance(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low: return 250f;
                case QualityTier.Medium: return 400f;
                case QualityTier.High: return 600f;
                default: return 1000f;
            }
        }

        public static string BestLapKey(int trackId, int carModel) => trackId >= 3 ? $"{Prefix}BestLap2.{trackId}.{carModel}" : $"{Prefix}BestLap.{trackId}.{carModel}";

        public static float GetBestLap(int trackId, int carModel) => PlayerPrefs.GetFloat(BestLapKey(trackId, carModel), 0f);

        public static bool TrySaveBestLap(int trackId, int carModel, float lapTime)
        {
            float current = GetBestLap(trackId, carModel);
            if (current > 0f && current <= lapTime) return false;
            PlayerPrefs.SetFloat(BestLapKey(trackId, carModel), lapTime);
            PlayerPrefs.Save();
            return true;
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
