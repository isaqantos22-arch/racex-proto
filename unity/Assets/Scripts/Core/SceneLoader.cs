using RaceX.Track;
using RaceX.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceX.Core
{
    public static class SceneLoader
    {
        public const string MainMenuScene = "MainMenu";
        public const string ForestScene = "Track_ForestCircuit";

        public static void LoadMainMenu()
        {
            Time.timeScale = 1f;
            Load(MainMenuScene, typeof(MainMenuUI));
        }

        public static void LoadRace(RaceSettings settings)
        {
            Time.timeScale = 1f;
            GameManager.Instance.settings = settings;
            Load(TrackLibrary.Get(settings.track).sceneName, typeof(RaceBootstrap));
        }

        public static void ReloadCurrent()
        {
            Time.timeScale = 1f;
            var active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0) SceneManager.LoadScene(active.buildIndex);
            else LoadRace(GameManager.Settings);
        }

        static void Load(string sceneName, System.Type bootstrap)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                return;
            }
            Debug.LogWarning($"RaceX: cena '{sceneName}' não está no Build Settings. Rode RaceX > Setup Project. Criando cena em tempo de execução.");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.CreateScene(sceneName + "_Runtime_" + Time.frameCount);
            SceneManager.SetActiveScene(scene);
            SceneManager.UnloadSceneAsync(previous);
            new GameObject(bootstrap.Name).AddComponent(bootstrap);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBootstrap()
        {
            if (Object.FindFirstObjectByType<MainMenuUI>() != null || Object.FindFirstObjectByType<RaceBootstrap>() != null) return;
            if (SceneManager.GetActiveScene().rootCount > 3) return;
            new GameObject("MainMenuUI").AddComponent<MainMenuUI>();
        }
    }
}
