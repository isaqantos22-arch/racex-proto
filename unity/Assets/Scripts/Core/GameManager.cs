using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    public enum GameMode { SinglePlayer, LocalSplitScreen, Online }
    public enum Difficulty { Easy = 0, Medium = 1, Hard = 2, Expert = 3 }

    [System.Serializable]
    public class PlayerSetup
    {
        public string name = "Jogador";
        public CarModel car = CarModel.Titan;
        public int colorIndex;
        public CameraView startCamera = CameraView.ThirdPerson;
    }

    [System.Serializable]
    public class RaceSettings
    {
        public static readonly int[] LapOptions = { 1, 2, 3, 5, 7, 10 };
        public static readonly int[] CarCountOptions = { 4, 6, 8, 10, 12, 16 };
        public const int ClassicCarCount = 8;

        public GameMode mode = GameMode.SinglePlayer;
        public TrackId track = TrackId.ForestCircuit;
        public int laps = 3;
        public int totalCars = ClassicCarCount;
        public Difficulty difficulty = Difficulty.Medium;
        public bool formulaOnly;
        public PlayerSetup[] players = { new PlayerSetup(), new PlayerSetup { name = "Jogador 2", colorIndex = 1 } };

        public int HumanCount => mode == GameMode.LocalSplitScreen ? 2 : 1;
    }

    public class GameManager : MonoBehaviour
    {
        static GameManager instance;

        public RaceSettings settings = new RaceSettings();

        public static GameManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<GameManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("GameManager");
                        instance = go.AddComponent<GameManager>();
                    }
                }
                return instance;
            }
        }

        public static RaceSettings Settings => Instance.settings;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Time.fixedDeltaTime = 0.01f;
            AudioListener.volume = SettingsManager.MasterVolume;
            settings.players[0].startCamera = (CameraView)SettingsManager.LastCamera;
        }
    }
}
