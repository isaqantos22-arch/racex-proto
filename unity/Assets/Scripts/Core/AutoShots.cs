using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using RaceX.Car;
using RaceX.Input;
using RaceX.Track;
using RaceX.UI;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Diagnóstico: com o argumento -racexshots &lt;pasta&gt; percorre menus e corridas tirando capturas de tela.</summary>
    public class AutoShots : MonoBehaviour
    {
        string dir;
        readonly StringBuilder log = new StringBuilder();
        static bool skyOnly;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-racexshots");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("AutoShots");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoShots>().dir = args[i + 1];
            skyOnly = System.Array.IndexOf(args, "-racexsky") >= 0;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            bool allowF1 = SettingsManager.AllowF1;
            SettingsManager.AllowF1 = true;
            var settings = GameManager.Settings;
            yield return new WaitForSecondsRealtime(3.5f);

            if (skyOnly)
            {
                foreach (var tid in new[] { TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.NightCity })
                {
                    var st = GameManager.Settings;
                    st.mode = GameMode.SinglePlayer;
                    st.formulaOnly = false;
                    st.laps = 1;
                    st.totalCars = 4;
                    st.track = tid;
                    st.players[0].car = CarModel.Titan;
                    st.players[0].startCamera = CameraView.ThirdPerson;
                    SceneLoader.LoadRace(st);
                    yield return new WaitForSecondsRealtime(7f);
                    log.AppendLine("sky_" + tid + ": ok");
                    yield return Shot("sky_" + tid);
                }
                File.WriteAllText(Path.Combine(dir, "autoshots.txt"), log.ToString());
                Application.Quit();
                yield break;
            }

            var menu = FindFirstObjectByType<MainMenuUI>();
            if (menu != null)
            {
                yield return Shot("menu_main");
                menu.ShowPlay(); yield return null; yield return Shot("menu_play");
                settings.mode = GameMode.SinglePlayer;
                menu.ShowRaceSetup(); yield return null; yield return Shot("menu_setup_single");
                settings.mode = GameMode.LocalSplitScreen;
                menu.RefreshShowroomCar(); menu.ShowRaceSetup(); yield return null; yield return Shot("menu_setup_split");
                settings.mode = GameMode.SinglePlayer;
                menu.RefreshShowroomCar();
                menu.ShowSettings(); yield return null; yield return Shot("menu_settings");
                menu.ShowGarage(); yield return null; yield return Shot("menu_garage");
                menu.ShowOnlineSetup(); yield return null; yield return Shot("menu_online");
            }
            else log.AppendLine("MainMenuUI não encontrado");

            foreach (CarModel model in System.Enum.GetValues(typeof(CarModel)))
                yield return RaceShots(model, false);
            yield return RaceShots(CarModel.Titan, true);

            // Ambiência sonora nas 3 pistas (log + captura)
            foreach (var tid in new[] { TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.NightCity })
            {
                var st = GameManager.Settings;
                st.mode = GameMode.SinglePlayer;
                st.formulaOnly = false;
                st.laps = 1;
                st.totalCars = 4;
                st.track = tid;
                st.players[0].car = CarModel.Titan;
                st.players[0].startCamera = CameraView.ThirdPerson;
                SceneLoader.LoadRace(st);
                yield return new WaitForSecondsRealtime(8f);
                log.AppendLine("ambience_" + tid + ": som ativo (ver GAMESOUND no log do player)");
                yield return Shot("ambience_" + tid);
            }
            GameManager.Settings.track = TrackId.ForestCircuit;

            SettingsManager.AllowF1 = allowF1;
            File.WriteAllText(Path.Combine(dir, "autoshots.txt"), log.ToString());
            Application.Quit();
        }

        IEnumerator RaceShots(CarModel model, bool split)
        {
            var settings = GameManager.Settings;
            settings.mode = split ? GameMode.LocalSplitScreen : GameMode.SinglePlayer;
            settings.formulaOnly = false;
            settings.laps = 1;
            settings.totalCars = 6;
            settings.players[0].car = model;
            settings.players[0].startCamera = CameraView.ThirdPerson;
            settings.players[1].car = CarModel.Falcon;
            SceneLoader.LoadRace(settings);
            yield return new WaitForSecondsRealtime(1f);

            RaceManager race = null;
            for (float t = 0f; t < 15f && (race == null || race.State != RaceState.Racing); t += Time.unscaledDeltaTime)
            {
                race = FindFirstObjectByType<RaceManager>();
                yield return null;
            }
            string tag = (split ? "split_" : "race_") + model;
            if (race == null || race.State != RaceState.Racing) { log.AppendLine(tag + ": corrida não começou"); yield break; }

            var human = race.entries.First(e => e.isHuman && e.playerIndex == 0);
            var car = human.car;
            {
                var rends = car.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (rends.Length > 0)
                {
                    var wb = rends[0].bounds;
                    foreach (var r in rends) wb.Encapsulate(r.bounds);
                    Debug.Log($"[MODELSIZE] {tag} worldSize={wb.size.x:F2}x{wb.size.y:F2}x{wb.size.z:F2} bodySize={car.spec.bodySize.z:F2}");
                    foreach (var r in rends.OrderByDescending(r => r.bounds.size.magnitude).Take(5))
                        Debug.Log($"[MODELPART] {tag} {r.name} {r.bounds.size.x:F2}x{r.bounds.size.y:F2}x{r.bounds.size.z:F2} scale={r.transform.lossyScale.x:F3}");
                }
            }
            car.GetComponent<PlayerInputHandler>().enabled = false;
            Vector3 start = car.transform.position;
            car.throttleInput = 1f;
            yield return new WaitForSeconds(2.5f);
            Vector3 moved = car.transform.position - start;
            log.AppendLine($"{tag}: v={car.SpeedKmh:F1}km/h fwdSpeed={car.ForwardSpeed:F1}m/s dot(desloc,frente)={Vector3.Dot(moved, car.transform.forward):F1}m gear={car.Gear}");

            var cams = FindObjectsByType<CarCamera>(FindObjectsSortMode.None);
            var cam = cams.FirstOrDefault(c => c.car == car);
            if (split || cam == null)
            {
                yield return Shot(tag);
                yield break;
            }
            foreach (CameraView v in System.Enum.GetValues(typeof(CameraView)))
            {
                cam.SetView(v);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(tag + "_" + v);
            }
            var smoke = car.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(p => p.name == "Fx_TireSmoke");
            if (smoke != null)
            {
                var mat = smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                log.AppendLine($"{tag}_smoke: material={(mat != null ? mat.name : "null")} shader={(mat != null ? mat.shader.name : "-")} supported={(mat != null && mat.shader.isSupported)}");
                cam.SetView(CameraView.ThirdPerson);
                for (int f = 0; f < 20; f++)
                {
                    var ep = new ParticleSystem.EmitParams { position = smoke.transform.position + Vector3.up * 0.5f, velocity = Random.insideUnitSphere * 0.8f + Vector3.up * 0.5f, startLifetime = 1.5f, startColor = new Color(0.78f, 0.78f, 0.78f, 0.55f), startSize = 0.8f };
                    smoke.Emit(ep, 4);
                    yield return null;
                }
                yield return Shot(tag + "_smoke");
            }
            car.throttleInput = 0f;
        }
    }
}
