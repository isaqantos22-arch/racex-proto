using System.Collections;
using System.Linq;
using NUnit.Framework;
using RaceX.Car;
using RaceX.Core;
using RaceX.Input;
using RaceX.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RaceX.Tests
{
    /// <summary>Testes ponta a ponta: montam uma corrida real e deixam a física/IA rodarem.</summary>
    public class RaceSmokeTests
    {
        const float Speed = 2f;


        public static void DestroyMenuLeftovers()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && go.transform.parent == null && (go.name == "MainMenuUI" || go.name == "MenuDemo" || go.name == "MenuDemoRoot" || go.name == "DemoSun" || go.name == "MenuCamera" || go.name == "KeyLight" || go.name == "Turntable" || go.name == "MenuCanvas" || go.name.StartsWith("Showroom_") || go.name == "RaceManager" || go.name.StartsWith("Track_") || go.name.StartsWith("Player_") || go.name.StartsWith("NPC_") || go.name.StartsWith("Camera_P") || go.name == "RaceCanvas" || go.name == "RaceBootstrap" || go.name == "Diag" || go.name == "Sun"))
                    Object.DestroyImmediate(go);
        }

        [UnitySetUp]
        public IEnumerator NewScene()
        {
            DestroyMenuLeftovers();
            var scene = SceneManager.CreateScene("Test_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(scene);
            yield return null;
        }

        [TearDown]
        public void Restore() => Time.timeScale = 1f;

        static IEnumerator WaitGame(float seconds)
        {
            yield return new WaitForSeconds(seconds); // escalonado por Time.timeScale
        }

        [UnityTest]
        public IEnumerator MainMenu_ShowsButtonsAfterSplash()
        {
            new GameObject("MainMenuUI").AddComponent<MainMenuUI>();
            yield return WaitGame(2.5f);
            var labels = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Select(t => t.text.Replace(" ", "").Replace("\u2009", "")).ToList();
            Debug.Log("RACEX_TEST menu labels: " + string.Join(" | ", labels));
            Assert.Contains("JOGAR", labels);
            Assert.Contains("CONFIGURAÇÕES", labels);
            Assert.Contains("RECORDES", labels);
        }

        [UnityTest]
        public IEnumerator Race_FullFlow_SpawnsCountsDownDrivesAndCompletesLap()
        {
            var s = GameManager.Settings;
            s.mode = GameMode.SinglePlayer;
            s.laps = 1;
            s.totalCars = 8;
            s.difficulty = Difficulty.Expert;
            s.players[0].car = CarModel.Titan;

            new GameObject("RaceBootstrap").AddComponent<RaceBootstrap>();
            yield return null;
            var race = Object.FindFirstObjectByType<RaceManager>();
            Assert.IsNotNull(race, "RaceManager não foi criado");
            Assert.AreEqual(8, race.entries.Count, "número de carros");
            var human = race.entries.Single(e => e.isHuman);
            Assert.GreaterOrEqual(human.gridSlot, 4, "jogador deve largar entre o meio e o fim do grid");
            var models = race.entries.Where(e => !e.isHuman).GroupBy(e => e.model).Select(g => g.Count()).Max();
            Assert.LessOrEqual(models, 4, "máximo 50% de um mesmo modelo entre os NPCs");
            Assert.AreEqual(8, race.entries.Select(e => e.colorIndex).Distinct().Count(), "cores únicas");

            Time.timeScale = Speed;
            float t = 0f;
            float next = 0f;
            while (race.State != RaceState.Racing && t < 10f)
            {
                if (t >= next) { next += 1f; Debug.Log($"RACEX_CD t={t:F1} state={race.State} pos={human.car.transform.position} yaw={human.car.transform.eulerAngles.y:F0} v={human.car.SpeedKmh:F1} off={human.car.IsOffTrack} locked={human.car.inputLocked}"); }
                t += Time.deltaTime; yield return null;
            }
            Assert.AreEqual(RaceState.Racing, race.State, "contagem 3-2-1-GO não terminou");

            // jogador: o mesmo CarController, dirigido pela IA por 8 s (o grid começa numa curva)
            var handler = human.car.GetComponent<PlayerInputHandler>();
            handler.enabled = false;
            var pilot = human.car.gameObject.AddComponent<CarAI>();
            pilot.Initialize(race.Track.Waypoints, race, Difficulty.Expert);
            float humanMax = 0f;
            for (int k = 0; k < 8; k++)
            {
                yield return WaitGame(1f);
                humanMax = Mathf.Max(humanMax, human.car.SpeedKmh);
                Debug.Log($"RACEX_AI human {pilot.DebugState}");
                foreach (var e in race.entries) if (!e.isHuman) Debug.Log($"RACEX_AI {e.name} {e.car.GetComponent<CarAI>().DebugState}");
            }
            float humanKmh = human.car.SpeedKmh;
            Debug.Log($"RACEX_TEST human {human.model} after 8s: {humanKmh:F1} km/h max {humanMax:F1} gear {human.car.Gear} rpm {human.car.Rpm:F0} offTrack={human.car.IsOffTrack}");
            Assert.Greater(humanMax, 60f, "carro do jogador não acelerou");

            yield return WaitGame(22f);
            var npcs = race.entries.Where(e => !e.isHuman).ToList();
            foreach (var e in npcs)
                Debug.Log($"RACEX_TEST 30s npc {e.name} {e.model} dist={e.raceDistance:F0}m speed={e.car.SpeedKmh:F0}km/h y={e.car.transform.position.y:F1}");
            float avg = npcs.Average(e => e.raceDistance);
            Assert.Greater(avg, 400f, "NPCs não estão progredindo na pista");
            Assert.IsTrue(npcs.All(e => e.car.transform.position.y > -5f), "algum NPC caiu do mapa");

            t = 0f;
            while (race.finishOrder.Count == 0 && t < 120f) { t += Time.deltaTime; yield return null; }
            Assert.Greater(race.finishOrder.Count, 0, "nenhum carro completou a volta (checkpoints/voltas)");
            var winner = race.finishOrder[0];
            Debug.Log($"RACEX_TEST lap complete: {winner.name} {winner.model} lap={HUD.FormatTime(winner.bestLap)} total={HUD.FormatTime(winner.totalTime)}");
            Assert.Greater(winner.bestLap, 25f, "tempo de volta implausível (atalho?)");
            Assert.Less(winner.bestLap, 150f, "tempo de volta lento demais");
        }
    }
}
