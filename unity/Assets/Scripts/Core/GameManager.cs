using System;
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
        public static readonly int[] LapOptions = { 0, 1, 2, 3, 5, 7, 10 }; // 0 = voltas REAIS (regra F1: 305 km / pista)
        public static readonly int[] CarCountOptions = { 4, 6, 8, 10, 12, 16 };
        public const int ClassicCarCount = 8;

        public GameMode mode = GameMode.SinglePlayer;
        public TrackId track = TrackId.ForestCircuit;
        public int laps = 3;
        public int totalCars = ClassicCarCount;
        public Difficulty difficulty = Difficulty.Medium;
        public bool formulaOnly;
        public int careerPhase = -1;   // sessão do fim de semana na carreira (-1 = não é carreira)
        public int careerGrid = -1;    // slot 0-based do humano definido pela classificação (-1 = sorteio)
        public float sessionTimeLimit; // limite REAL de tempo da sessão em segundos (0 = por distância)
        public bool pitStops;          // pit lane + desgaste de pneu + compostos
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
            Time.fixedDeltaTime = 0.01f; // 100 Hz — valor ORIGINAL. O 0.02 (v62) degradou a fricção do WheelCollider: rodas patinavam (fumaça) e TODOS os carros ficavam limitados ~100 km/h
            AudioListener.volume = SettingsManager.MasterVolume;
            settings.players[0].startCamera = (CameraView)SettingsManager.LastCamera;

            // Modo de calibração de IA ao vivo: -aicallb na linha de comando.
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg == "-aicallb")
                {
                    var go = new GameObject("AICalib");
                    DontDestroyOnLoad(go);
                    go.AddComponent<AICalibrationLive>();
                    break;
                }
        }
    }
}
