using System.Collections;
using System.Collections.Generic;
using RaceX.Car;
using RaceX.Input;
using RaceX.Track;
using RaceX.UI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaceX.Core
{
    /// <summary>Monta a corrida inteira em tempo de execução a partir de GameManager.Settings.</summary>
    public class RaceBootstrap : MonoBehaviour
    {
        public static readonly string[] DriverNames =
        {
            "Rafael Duarte", "Marco Bellini", "Lukas Brandt", "Hiro Tanaka", "Pierre Lavoie", "Diego Morales",
            "Sven Larsen", "Tomás Ribeiro", "Alex Turner", "Nico Ferrara", "Igor Volkov", "Kenji Mori",
            "Luca Romano", "Mateo Cruz", "Oscar Lindqvist", "Felipe Arantes", "Jonas Weber", "Ethan Cole",
            "Bruno Salvatti", "Yuki Sato", "Andrés Pineda", "Max Keller", "Théo Martin", "Caio Mendes",
            "Ryan Walsh", "Emil Novak", "Gabriel Rocha", "Dario Conti", "Leo Hartmann", "Samuel Prado"
        };

        void Start()
        {
            var settings = GameManager.Settings;
            bool split = settings.mode == GameMode.LocalSplitScreen;
            bool online = settings.mode == GameMode.Online;
            if (settings.laps <= 0) settings.laps = TrackLibrary.RealLaps(settings.track); // 0 = voltas reais (regra F1)
            SettingsManager.ApplyQuality(SettingsManager.Quality, split);
            PostFxSetup.Ensure();

            var def = TrackLibrary.Get(settings.track);
            SetupEnvironment(def);
            var track = TrackBuilder.Build(def);

            var race = new GameObject("RaceManager").AddComponent<RaceManager>();
            race.Initialize(track, settings);
            RaceX.Core.PitLaneSystem pit = null;
            if (settings.pitStops)
            {
                var pitGo = new GameObject("PitLaneSystem");
                pit = pitGo.AddComponent<RaceX.Core.PitLaneSystem>();
                pit.Setup(race);
                pit.boxCount = Mathf.Clamp(settings.totalCars, 4, 24); // nº de boxes = nº de carros da sessão
            }
            // Treino livre da carreira (sex/sáb): todos NASEM no pit, saída em fila.
            bool pitStart = pit != null && settings.laps >= 900;
            Pose? PitPose(int slot) => pitStart ? pit.PitSpawnPose(slot) : (Pose?)null;
            RaceX.Core.ReplaySystem.StartRecording(race);
            GameAudio.EnterRace(settings.track);

            int total = Mathf.Clamp(settings.totalCars, settings.HumanCount, TrackBuilder.GridSlots);
            var usedColors = new HashSet<int>();
            var humans = new List<RaceEntry>();

            if (online)
            {
                SpawnOnline(race, settings, track.Waypoints, humans, usedColors);
            }
            else
            {
                var slots = AssignHumanSlots(total, settings.HumanCount);
                // Carreira: grid REAL — ordem vindas da classificação (índice 0 = pole).
                var careerGridNames = (RaceX.Career.CareerData.raceActive
                    && RaceX.Career.CareerMode.RaceGrid != null
                    && RaceX.Career.CareerMode.RaceGrid.Count >= total)
                    ? RaceX.Career.CareerMode.RaceGrid : null;
                if (careerGridNames != null && settings.HumanCount == 1)
                {
                    int hSlot = careerGridNames.IndexOf(settings.players[0].name);
                    slots[0] = Mathf.Clamp(hSlot, 0, total - 1);
                }
                var models = PickNpcModels(total - settings.HumanCount, settings.formulaOnly, SettingsManager.AllowF1);
                // Carreira: IAs recebem os nomes dos rivais da temporada (ordem fixa do roster),
                // para que a classificação da corrida pontue a tabela real.
                var names = (RaceX.Career.CareerData.raceActive && RaceX.Career.CareerMode.RaceRoster != null
                    && RaceX.Career.CareerMode.RaceRoster.Count >= total - settings.HumanCount)
                    ? new List<string>(RaceX.Career.CareerMode.RaceRoster)
                    : new List<string>(DriverNames);
                // Grid real: rivais ocupam os slots na ordem exata da classificação (sem shuffle).
                if (careerGridNames != null && settings.HumanCount == 1)
                {
                    names = new List<string>();
                    foreach (var n in careerGridNames)
                        if (n != settings.players[0].name) names.Add(n);
                    if (names.Count < total - settings.HumanCount) Shuffle(names);
                }
                else Shuffle(names);
                for (int p = 0; p < settings.HumanCount; p++)
                {
                    var ps = settings.players[p];
                    usedColors.Add(ps.colorIndex);
                    var humanModel = RaceX.Career.CareerData.raceActive
                        ? RaceX.Career.CareerMode.PlayerCar()
                        : (settings.formulaOnly ? CarModel.F1 : (!SettingsManager.AllowF1 && ps.car == CarModel.F1 ? CarModel.Titan : ps.car));
                    var hspec = CarSpec.CreateDefault(humanModel);
                    CareerStats.ApplyUpgrades(hspec);
                    humans.Add(race.SpawnCar(hspec, ps.colorIndex, ps.name, slots[p], true, p, PitPose(slots[p])));
                }

                int npc = 0, color = 0;
                for (int slot = 0; slot < total; slot++)
                {
                    if (slots.Contains(slot)) continue;
                    while (usedColors.Contains(color % CarPalette.Colors.Length) && usedColors.Count < CarPalette.Colors.Length) color++;
                    int c = color % CarPalette.Colors.Length;
                    usedColors.Add(c);
                    color++;
                    var carModel = RaceX.Career.CareerData.raceActive
                        ? RaceX.Career.CareerMode.CarForDriver(names[npc % names.Count])
                        : models[npc];
                    var entry = race.SpawnCar(CarSpec.CreateDefault(carModel), c, names[npc % names.Count], slot, false, -1, PitPose(slot));
                    var ai = entry.car.gameObject.AddComponent<CarAI>();
                    ai.Initialize(track.Waypoints, race, settings.difficulty,
                        RaceX.Career.CareerData.raceActive ? RaceX.Career.CareerMode.SkillBiasFor(names[npc % names.Count]) : 0f);
                    ai.SetLane(((npc * 37) % 9 - 4) * 0.42f);
                    RaceX.UI.Nametag.Attach(entry.car, names[npc % names.Count]);
                    npc++;
                }
            }

            foreach (var e in race.entries) EngineAudio.Attach(e.car, e.isHuman);
            foreach (var e in race.entries) VisualEffects.Attach(e.car);
            // SkidMarks desativado: Shader do material chega null no player (1673x ArgumentNullException quebra som/pause no deserto). Reativar apos fix do material.
            // foreach (var e in race.entries) RaceX.Track.SkidMarks.Attach(e.car);

            UIFactory.EnsureEventSystem();
            var canvas = UIFactory.CreateCanvas("RaceCanvas");
            var pause = PauseMenu.Create(canvas, race, online);
            bool mobile = Application.isMobilePlatform;

            for (int p = 0; p < humans.Count; p++)
            {
                Rect vp = !split ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, p == 0 ? 0.5f : 0f, 1f, 0.5f);
                SetupPlayer(humans[p], vp, canvas, race, pause, split, mobile && !split);
            }
            pause.transform.SetAsLastSibling();

            var listener = FindFirstObjectByType<AudioListener>();
            if (listener == null && humans.Count > 0) humans[0].car.gameObject.AddComponent<AudioListener>();

            race.BeginCountdown();

            if (online && humans.Count > 0)
            {
                var local = humans[0];
                race.EntryFinished += e => { if (e == local) OnlineClient.SendFinished(e.totalTime, e.bestLap); };
                StartCoroutine(OnlineTelemetry(race, local));
            }
        }

        void SpawnOnline(RaceManager race, RaceSettings settings, WaypointSystem waypoints, List<RaceEntry> humans, HashSet<int> usedColors)
        {
            var real = new List<StartSlotInfo>();
            var gi = OnlineClient.StartInfo;
            if (gi != null)
            {
                foreach (var sp in gi.startPositions) if (!sp.isAI) real.Add(sp);
            }
            StartSlotInfo mine = null;
            var pid = OnlineClient.MyPlayerId;
            foreach (var sp in real)
            {
                if (mine == null || (pid != null && sp.id == pid)) mine = sp;
            }
            if (mine == null)
            {
                var ps = settings.players[0];
                usedColors.Add(ps.colorIndex);
                var fallbackSpec = CarSpec.CreateDefault(ps.car);
                CareerStats.ApplyUpgrades(fallbackSpec);
                humans.Add(race.SpawnCar(fallbackSpec, ps.colorIndex, ps.name, 0, true, 0));
                SpawnOnlineAi(race, settings, waypoints, usedColors, new HashSet<int> { 0 });
                return;
            }
            var mySpec = CarSpec.CreateDefault(OnlineClient.CarModelFromServer(mine.carModel));
            CareerStats.ApplyUpgrades(mySpec);
            int mySlot = Mathf.Clamp(mine.slot, 0, TrackBuilder.GridSlots - 1);
            usedColors.Add(mine.carColor);
            humans.Add(race.SpawnCar(mySpec, mine.carColor, settings.players[0].name, mySlot, true, 0));
            foreach (var sp in real)
            {
                if (sp.id == mine.id) continue;
                usedColors.Add(sp.carColor);
                var entry = race.SpawnCar(CarSpec.CreateDefault(OnlineClient.CarModelFromServer(sp.carModel)), sp.carColor, sp.name, Mathf.Clamp(sp.slot, 0, TrackBuilder.GridSlots - 1), false, -1);
                var ghost = entry.car.gameObject.AddComponent<OnlineGhost>();
                ghost.playerId = sp.id;
                entry.car.Body.isKinematic = true;
                RaceX.UI.Nametag.Attach(entry.car, sp.name);
            }
            SpawnOnlineAi(race, settings, waypoints, usedColors, OccupiedSlots(real));
        }

        static HashSet<int> OccupiedSlots(List<StartSlotInfo> real)
        {
            var occupied = new HashSet<int>();
            foreach (var sp in real) occupied.Add(Mathf.Clamp(sp.slot, 0, TrackBuilder.GridSlots - 1));
            return occupied;
        }

        void SpawnOnlineAi(RaceManager race, RaceSettings settings, WaypointSystem waypoints, HashSet<int> usedColors, HashSet<int> occupied)
        {
            int aiCount = Mathf.Min(Mathf.Max(0, SettingsManager.OnlineAiCount), TrackBuilder.GridSlots - occupied.Count);
            if (aiCount <= 0) return;
            var models = PickNpcModels(aiCount, settings.formulaOnly, SettingsManager.AllowF1);
            var names = new List<string>(DriverNames);
            Shuffle(names);
            int npc = 0, color = 0;
            for (int slot = 0; slot < TrackBuilder.GridSlots && npc < aiCount; slot++)
            {
                if (occupied.Contains(slot)) continue;
                while (usedColors.Contains(color % CarPalette.Colors.Length) && usedColors.Count < CarPalette.Colors.Length) color++;
                int c = color % CarPalette.Colors.Length;
                usedColors.Add(c);
                color++;
                var entry = race.SpawnCar(CarSpec.CreateDefault(models[npc]), c, names[npc % names.Count], slot, false, -1);
                var ai = entry.car.gameObject.AddComponent<CarAI>();
                ai.Initialize(waypoints, race, settings.difficulty);
                ai.SetLane(((npc * 37) % 9 - 4) * 0.42f);
                RaceX.UI.Nametag.Attach(entry.car, names[npc % names.Count]);
                npc++;
            }
        }

        IEnumerator OnlineTelemetry(RaceManager race, RaceEntry local)
        {
            var car = local != null ? local.car : null;
            while (race != null && car != null && !local.finished && race.State != RaceState.Finished)
            {
                yield return new WaitForSeconds(0.1f);
                if (race.State != RaceState.Racing) continue;
                var p = race.GetProgress(local);
                int lap = p != null ? Mathf.Clamp(p.lap, 1, race.Settings.laps) : 1;
                int ck = p != null ? p.lastCheckpoint : 0;
                OnlineClient.SendPosition(car.transform.position.x, car.transform.position.y, car.transform.position.z, car.transform.eulerAngles.y, car.SpeedKmh, lap, ck);
            }
        }

        void SetupPlayer(RaceEntry entry, Rect viewport, Canvas canvas, RaceManager race, PauseMenu pause, bool split, bool touchControls)
        {
            var camGo = new GameObject("Camera_P" + (entry.playerIndex + 1));
            var cam = camGo.AddComponent<Camera>();
            cam.rect = viewport;
            if (entry.playerIndex == 0) camGo.tag = "MainCamera";
            var carCam = camGo.AddComponent<CarCamera>();
            carCam.persistSelection = entry.playerIndex == 0;

            TouchControlsUI touch = null;
            if (touchControls) touch = TouchControlsUI.Create(canvas.transform, viewport, SettingsManager.ControlMode, Accelerometer() );

            var hud = HUD.Create(canvas, viewport, race, entry, carCam, pause, split, touch != null);
            carCam.fadeImage = hud.FadeImage;
            carCam.Initialize(entry.car, cam, GameManager.Settings.players[entry.playerIndex].startCamera);

            var input = entry.car.gameObject.AddComponent<PlayerInputHandler>();
            input.car = entry.car;
            input.carCamera = carCam;
            input.touch = touch;
#if ENABLE_INPUT_SYSTEM
            if (split)
            {
                // J1: WASD (+ 1º gamepad); J2: setas (+ 2º gamepad)
                var pads = Gamepad.all;
                if (pads.Count > entry.playerIndex) input.SetActionMap("Gamepad", new InputDevice[] { pads[entry.playerIndex] });
                else input.SetActionMap(entry.playerIndex == 0 ? "Split1" : "Split2");
            }
#endif
            input.PausePressed += pause.Toggle;
            entry.car.assistLevel = GameManager.Settings.difficulty switch
            {
                Difficulty.Expert => 0f,
                Difficulty.Hard => 0.35f,
                Difficulty.Medium => 0.6f,
                _ => 1f,
            };
        }

        static bool Accelerometer()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Accelerometer.current != null;
#else
            return SystemInfo.supportsAccelerometer;
#endif
        }

        static void SetupEnvironment(TrackDefinition def)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = def.sunColor;
            sun.intensity = def.sunIntensity;
            sun.shadows = SettingsManager.Quality == QualityTier.Low ? LightShadows.None : LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(def.sunEuler);
            RenderSettings.sun = sun;

            var skyTex = string.IsNullOrEmpty(def.skyboxExr) ? null : Resources.Load<Texture2D>(def.skyboxExr);
            if (skyTex != null)
            {
                var skyMat = new Material(Shader.Find("Skybox/Panoramic"));
                skyMat.SetTexture("_MainTex", skyTex);
                skyMat.SetFloat("_Exposure", 1.1f);
                RenderSettings.skybox = skyMat;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            }
            RenderSettings.ambientLight = def.ambientColor;
            RenderSettings.fog = def.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = def.fogColor;
            RenderSettings.fogDensity = def.fogDensity;
        }

        /// <summary>Single player: posição sorteada entre o meio e o fim do grid.</summary>
        static List<int> AssignHumanSlots(int total, int humans)
        {
            var list = new List<int>();
            int from = total / 2;
            for (int i = 0; i < humans; i++)
            {
                int s;
                do s = Random.Range(from, total); while (list.Contains(s) && list.Count < total - from);
                if (list.Contains(s)) s = list.Count;
                list.Add(s);
            }
            return list;
        }

        /// <summary>Sorteia modelos sem que um modelo passe de 50% dos NPCs.</summary>
        static List<CarModel> PickNpcModels(int count, bool formulaOnly, bool allowF1)
        {
            var list = new List<CarModel>();
            if (formulaOnly)
            {
                for (int i = 0; i < count; i++) list.Add(CarModel.F1);
                return list;
            }
            var pool = new List<CarModel>((CarModel[])System.Enum.GetValues(typeof(CarModel)));
            if (!allowF1) pool.Remove(CarModel.F1);
            int cap = Mathf.Max(1, Mathf.CeilToInt(count * 0.5f));
            var counts = new int[pool.Count];
            for (int i = 0; i < count; i++)
            {
                int m;
                int guard = 0;
                do m = Random.Range(0, pool.Count); while (counts[m] >= cap && ++guard < 20);
                counts[m]++;
                list.Add(pool[m]);
            }
            return list;
        }

        static void Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (l[i], l[j]) = (l[j], l[i]);
            }
        }
    }
}
