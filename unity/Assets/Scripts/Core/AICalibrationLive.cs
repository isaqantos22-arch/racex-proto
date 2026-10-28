using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Calibração de IA AO VIVO: roda o jogo de verdade (build) e mede, por carro e pista,
    /// a fração máxima do limite físico em que a IA completa voltas limpas (busca binária —
    /// "acelera, solta, curva; se perder o controle, reduz e tenta de novo").
    /// Ativar com argumento -aicallb na linha de comando do executável.</summary>
    public class AICalibrationLive : MonoBehaviour
    {
        public static float FractionOverride = 1f;

        static readonly TrackId[] Tracks =
        {
            TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.Principado
        };
        const int LapsPerAttempt = 1;
        const int VerifyLaps = 2;
        const float MaxOffSeconds = 1.0f;

        class Attempt
        {
            public float fraction;
            public bool clean;
            public float bestLap = float.MaxValue;
            public int offs;
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 9999;
            Time.timeScale = 8f;

            var report = new List<string>();
            report.Add("{\"cars\":[");
            bool firstCar = true;

            foreach (CarModel model in Enum.GetValues(typeof(CarModel)))
            {
                for (int t = 0; t < Tracks.Length; t++)
                {
                    var attempts = new List<Attempt>();
                    float lo = 0.55f, hi = 1.05f, bestF = 0f, bestLap = float.MaxValue;

                    // Busca binária: até onde o carro completa a volta limpa (Expert = limite).
                    for (int iter = 0; iter < 5; iter++)
                    {
                        float f = 0.5f * (lo + hi);
                        var a = new Attempt { fraction = f };
                        yield return RunAttempt(model, Tracks[t], a, LapsPerAttempt);
                        attempts.Add(a);
                        if (a.clean) { lo = f; bestF = f; bestLap = Mathf.Min(bestLap, a.bestLap); }
                        else hi = f;
                        if (hi - lo < 0.04f) break;
                    }

                    // Verificação: voltas de prova nas três dificuldades (frações do limite medido).
                    var verify = new Dictionary<string, float>();
                    foreach (var (name, frac) in new[] { ("expert", 1f), ("hard", 0.96f), ("medium", 0.90f) })
                    {
                        var v = new Attempt { fraction = Mathf.Min(bestF > 0f ? 1f : 0.9f, 1f) * frac * (bestF > 0f ? 1.05f : 1f) };
                        yield return RunAttempt(model, Tracks[t], v, VerifyLaps);
                        verify[name] = v.clean && v.bestLap < float.MaxValue ? v.bestLap : 0f;
                    }

                    var spec = CarSpec.CreateDefault(model);
                    string line = (firstCar ? "" : ",\n") + string.Format(
                        "{{\"model\":\"{0}\",\"track\":\"{1}\",\"fmax\":{2},\"bestLapExpert\":{3},\"lapHard\":{4},\"lapMedium\":{5},\"attempts\":{6}}}",
                        spec.displayName, Tracks[t],
                        bestF.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        bestLap < float.MaxValue ? bestLap.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "0",
                        verify["hard"].ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        verify["medium"].ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        attempts.Count);
                    report.Add(line);
                    firstCar = false;
                    Debug.Log($"[AICALLB] {spec.displayName} {Tracks[t]} fmax={bestF:F2} expert={bestLap:F2} hard={verify["hard"]:F2} medium={verify["medium"]:F2}");
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }
            report.Add("\n]}");
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "ai_calibration.json"), string.Concat(report));
            Debug.Log("[AICALLB] concluido -> " + Path.Combine(Application.persistentDataPath, "ai_calibration.json"));
            Application.Quit();
        }

        /// <summary>Roda uma tentativa: carrega a pista com 1 carro dirigido pela IA e observa as voltas.</summary>
        IEnumerator RunAttempt(CarModel model, TrackId track, Attempt a, int laps)
        {
            FractionOverride = a.fraction;
            var settings = new RaceSettings
            {
                mode = GameMode.SinglePlayer,
                track = track,
                laps = laps,
                totalCars = 1,
                difficulty = Difficulty.Expert,
                players = new[] { new PlayerSetup { name = "CAL", car = model, startCamera = CameraView.ThirdPerson } }
            };
            SceneLoader.LoadRace(settings);
            yield return new WaitForSecondsRealtime(1.5f);

            var race = FindObjectOfType<RaceManager>();
            if (race == null) yield break;
            while (race.State != RaceState.Racing) yield return null;

            var entry = race.entries[0];
            var car = entry.car;
            var input = car.GetComponent<RaceX.Input.PlayerInputHandler>();
            if (input != null) input.enabled = false;
            var ai = car.GetComponent<CarAI>();
            if (ai == null) ai = car.gameObject.AddComponent<CarAI>();
            ai.Initialize(race.Track.Waypoints, race, Difficulty.Expert);

            float offTimer = 0f, stuckTimer = 0f;
            while (race.State == RaceState.Racing && entry.lapTimes.Count < laps)
            {
                if (car.IsOffTrack) offTimer += Time.deltaTime; else offTimer = 0f;
                if (offTimer > MaxOffSeconds) { a.offs++; yield break; }
                if (car.transform.up.y < 0.5f) yield break;
                if (car.Velocity.magnitude < 5f) stuckTimer += Time.deltaTime; else stuckTimer = 0f;
                if (stuckTimer > 6f) yield break;
                yield return null;
            }
            if (entry.lapTimes.Count >= laps)
            {
                a.clean = true;
                a.bestLap = entry.bestLap;
            }
        }
    }
}
