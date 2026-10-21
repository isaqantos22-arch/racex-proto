using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Som de motor sintetizado (sem assets): timbre por modelo, tom contínuo modulado pelo RPM.</summary>
    [RequireComponent(typeof(CarController))]
    public class EngineAudio : MonoBehaviour
    {
        static readonly Dictionary<CarModel, AudioClip> clips = new Dictionary<CarModel, AudioClip>();

        CarController car;
        AudioSource src;

        public static void Attach(CarController car, bool isHuman)
        {
            var ea = car.gameObject.AddComponent<EngineAudio>();
            ea.car = car;
            ea.src = car.gameObject.AddComponent<AudioSource>();
            ea.src.clip = Clip(car.spec != null ? car.spec.model : CarModel.Titan);
            ea.src.loop = true;
            ea.src.spatialBlend = isHuman ? 0f : 1f;
            ea.src.maxDistance = 130f;
            ea.src.minDistance = isHuman ? 0f : 6f;
            ea.src.dopplerLevel = 0.4f;
            ea.src.volume = 0.3f;
            ea.src.Play();
        }

        struct Timbre
        {
            public float f0, noise, sub, harm2, harm3, gain;
        }

        static Timbre TimbreFor(CarModel m)
        {
            switch (m)
            {
                case CarModel.Bugatti: return new Timbre { f0 = 34f, noise = 0.05f, sub = 0.42f, harm2 = 0.18f, harm3 = 0.06f, gain = 1.0f };
                case CarModel.Revuelto: return new Timbre { f0 = 40f, noise = 0.07f, sub = 0.34f, harm2 = 0.22f, harm3 = 0.10f, gain = 0.95f };
                case CarModel.Ferrari: return new Timbre { f0 = 46f, noise = 0.10f, sub = 0.28f, harm2 = 0.26f, harm3 = 0.14f, gain = 0.9f };
                case CarModel.R8: return new Timbre { f0 = 52f, noise = 0.10f, sub = 0.24f, harm2 = 0.28f, harm3 = 0.16f, gain = 0.9f };
                case CarModel.Mustang: return new Timbre { f0 = 43f, noise = 0.20f, sub = 0.36f, harm2 = 0.20f, harm3 = 0.08f, gain = 0.95f };
                case CarModel.GtrNismo: return new Timbre { f0 = 48f, noise = 0.16f, sub = 0.30f, harm2 = 0.22f, harm3 = 0.10f, gain = 0.9f };
                case CarModel.McLarenP1: return new Timbre { f0 = 58f, noise = 0.09f, sub = 0.22f, harm2 = 0.30f, harm3 = 0.18f, gain = 0.9f };
                case CarModel.Falcon: return new Timbre { f0 = 50f, noise = 0.12f, sub = 0.30f, harm2 = 0.24f, harm3 = 0.12f, gain = 0.85f };
                case CarModel.Titan: return new Timbre { f0 = 47f, noise = 0.12f, sub = 0.32f, harm2 = 0.24f, harm3 = 0.12f, gain = 0.85f };
                case CarModel.F1:
                case CarModel.Storm: return new Timbre { f0 = 84f, noise = 0.08f, sub = 0.10f, harm2 = 0.42f, harm3 = 0.34f, gain = 0.85f };
                default: return new Timbre { f0 = 50f, noise = 0.12f, sub = 0.30f, harm2 = 0.24f, harm3 = 0.12f, gain = 0.85f };
            }
        }

        static readonly HashSet<CarModel> recorded = new HashSet<CarModel>();

        static string RecordedName(CarModel m) => m switch
        {
            CarModel.Falcon => "corvette_v8",
            CarModel.Titan => "porsche_flat6",
            CarModel.Storm => "gallardo_v10",
            CarModel.F1 => "f1_brawn",
            CarModel.Ferrari => "ferrari_f355",
            CarModel.R8 => "r8_v10",
            CarModel.Mustang => "mustang_v8",
            CarModel.Revuelto => "revuelto_v12",
            CarModel.GtrNismo => "gtr_v6",
            CarModel.McLarenP1 => "p1_v8",
            CarModel.Bugatti => "bugatti_w16",
            CarModel.F2008 => "f1_brawn",
            CarModel.F1Studio => "f1_brawn",
            _ => null
        };

        static AudioClip Clip(CarModel model)
        {
            if (clips.TryGetValue(model, out var cached)) return cached;
            var loaded = Resources.Load<AudioClip>("Audio/Engines/" + RecordedName(model));
            if (loaded != null)
            {
                clips[model] = loaded;
                recorded.Add(model);
                Debug.Log("[ENGINEAUDIO] " + model + ": clipe real '" + RecordedName(model) + "' (" + loaded.length + "s)");
                return loaded;
            }
            const int rate = 44100;
            const float duration = 0.5f;
            int n = (int)(rate * duration);
            var data = new float[n];
            Timbre tb = TimbreFor(model);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float saw = (t * tb.f0) % 1f * 2f - 1f;
                float sub = (t * tb.f0 * 0.5f) % 1f * 2f - 1f;
                float h2 = Mathf.Sin(Mathf.PI * 2f * tb.f0 * 2f * t);
                float h3 = Mathf.Sin(Mathf.PI * 2f * tb.f0 * 3f * t);
                float noise = Random.Range(-1f, 1f);
                float window = Mathf.Min(1f, Mathf.Min(i, n - i) / (rate * 0.01f));
                float v = saw * 0.5f + sub * tb.sub + h2 * tb.harm2 + h3 * tb.harm3 + noise * tb.noise;
                data[i] = v * tb.gain * 0.55f * window;
            }
            var clip = AudioClip.Create("RaceXEngine." + model, n, 1, rate, false);
            clip.SetData(data, 0);
            clips[model] = clip;
            return clip;
        }

        void Update()
        {
            if (car == null || car.spec == null || src == null) return;
            float rpm01 = Mathf.Clamp01(car.Rpm / Mathf.Max(1f, car.spec.rpmLimit));
            src.pitch = recorded.Contains(car.spec.model)
                ? Mathf.Clamp(0.9f + rpm01 * 0.4f, 0.85f, 1.35f)
                : Mathf.Clamp(0.55f + rpm01 * 2.1f, 0.3f, 3f);
            float target = car.inputLocked ? 0f : 0.55f + 0.35f * car.throttleInput;
            src.volume = Mathf.MoveTowards(src.volume, target, Time.deltaTime * 2.5f);
        }
    }
}
