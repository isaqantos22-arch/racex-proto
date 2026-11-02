using System.Collections.Generic;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Música e ambiência sintetizadas em código (sem assets), com fonte persistente entre cenas.</summary>
    public static class GameAudio
    {
        static GameObject owner;
        static AudioSource music;
        static AudioSource ambience;
        static readonly Dictionary<TrackId, AudioClip> ambienceClips = new Dictionary<TrackId, AudioClip>();

        static void Ensure()
        {
            if (owner != null) return;
            owner = new GameObject("RaceXGameAudio");
            Object.DontDestroyOnLoad(owner);
            owner.AddComponent<GameAudioUpdater>();
        }

        static AudioClip[] playlist;
        static int trackIdx;

        class GameAudioUpdater : MonoBehaviour
        {
            void Update()
            {
                if (music != null) music.mute = !SettingsManager.MusicOn;
                if (music != null && music.clip != null && !music.loop && playlist != null && playlist.Length > 1 && !music.isPlaying)
                {
                    int next;
                    do { next = UnityEngine.Random.Range(0, playlist.Length); } while (next == trackIdx && playlist.Length > 1);
                    trackIdx = next;
                    music.clip = playlist[trackIdx];
                    music.Play();
                }
            }
        }

        public static void EnterMenu()
        {
            Ensure();
            if (music == null)
            {
                music = owner.AddComponent<AudioSource>();
                playlist = Resources.LoadAll<AudioClip>("Audio/Music");
                Shuffle(playlist);
                trackIdx = 0;
                if (playlist.Length > 0)
                {
                    music.clip = playlist[0];
                    music.loop = playlist.Length == 1;
                }
                else
                {
                    music.clip = MusicClip();
                    music.loop = true;
                }
            }
            music.volume = 0.5f;
            if (!music.isPlaying) music.Play();
            if (ambience != null && ambience.isPlaying) ambience.Stop();
        }

        public static void EnterRace(TrackId track)
        {
            Ensure();
            if (music != null && music.isPlaying) music.volume = 0.18f;
            if (ambience == null)
            {
                ambience = owner.AddComponent<AudioSource>();
                ambience.loop = true;
                ambience.spatialBlend = 0f;
            }
            if (!ambienceClips.TryGetValue(track, out var clip))
            {
                clip = AmbienceClip(track);
                ambienceClips[track] = clip;
            }
            ambience.clip = clip;
            ambience.volume = 0.4f;
            ambience.Play();
        }

        static void Shuffle(AudioClip[] list)
        {
            for (int i = list.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static AudioClip MusicClip()
        {
            const int rate = 44100;
            const float bpm = 104f;
            const int bars = 8;
            float beat = 60f / bpm;
            float barLen = beat * 4f;
            int n = (int)(barLen * bars * rate);
            var data = new float[n];
            // Progressão Am F C G (raiz, Hz) tocada por compasso.
            float[] roots = { 110f, 87.31f, 130.81f, 98f };
            for (int bar = 0; bar < bars; bar++)
            {
                float root = roots[bar % 4];
                int barStart = (int)(bar * barLen * rate);
                int barSamples = (int)(barLen * rate);
                for (int i = 0; i < barSamples; i++)
                {
                    int s = barStart + i;
                    if (s >= n) break;
                    float t = (float)i / rate;
                    float inBar = t / barLen;
                    float env = Mathf.Min(1f, inBar * 24f) * Mathf.Min(1f, (1f - inBar) * 12f);
                    // Baixo: oitava abaixo, colcheias.
                    float beatPos = t / beat;
                    float gate = Mathf.Round(beatPos * 2f) - beatPos * 2f < 0.25f ? 1f : 0f;
                    float bass = Mathf.Sin(Mathf.PI * 2f * root * 0.5f * t) * 0.5f * gate;
                    // Pad: acorde maior/menor da raiz (terça maior em C e F).
                    float third = (bar % 4 == 2 || bar % 4 == 1) ? 1.26f : 1.19f;
                    float pad = Mathf.Sin(Mathf.PI * 2f * root * t)
                              + Mathf.Sin(Mathf.PI * 2f * root * third * t) * 0.8f
                              + Mathf.Sin(Mathf.PI * 2f * root * 1.498f * t) * 0.7f;
                    pad *= 0.16f * env;
                    // Arpejo em colcheias, duas oitavas acima.
                    float arpStep = Mathf.Floor(beatPos * 2f) % 4f;
                    float arpFreq = root * 4f * (arpStep == 0f ? 1f : arpStep == 1f ? third : arpStep == 2f ? 1.498f : 2f);
                    float arpGate = (beatPos * 2f) % 1f < 0.5f ? 1f : 0f;
                    float arp = Mathf.Sign(Mathf.Sin(Mathf.PI * 2f * arpFreq * t)) * 0.05f * arpGate * env;
                    data[s] = Mathf.Clamp(bass * 0.6f + pad + arp, -1f, 1f) * 0.9f;
                }
            }
            var clip = AudioClip.Create("RaceXMusic", n, 1, rate, false);
            clip.SetData(data, 0);
            Debug.Log("[GAMESOUND] music ready len=" + (n / (float)rate).ToString("F1") + "s");
            return clip;
        }

        static AudioClip AmbienceClip(TrackId track)
        {
            const int rate = 44100;
            const float seconds = 8f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            System.Random rng = new System.Random(7);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.02f * (white - lp);
                lp2 += 0.008f * (white - lp2);
                float v;
                switch (track)
                {
                    case TrackId.DesertCanyon:
                    {
                        float gust = 0.5f + 0.5f * Mathf.Sin(Mathf.PI * 2f * t / seconds * 2f + Mathf.Sin(t * 0.9f));
                        v = lp * 2.4f * (0.35f + 0.65f * gust);
                        break;
                    }
                    case TrackId.NightCity:
                    {
                        float hum = Mathf.Sin(Mathf.PI * 2f * 118f * t) * 0.05f + Mathf.Sin(Mathf.PI * 2f * 236f * t) * 0.02f;
                        v = lp2 * 1.6f * 0.5f + hum;
                        break;
                    }
                    default:
                    {
                        v = lp2 * 1.2f * 0.5f;
                        break;
                    }
                }
                data[i] = Mathf.Clamp(v, -1f, 1f) * 0.8f;
            }
            var clip = AudioClip.Create("RaceXAmbience." + track, n, 1, rate, false);
            clip.SetData(data, 0);
            Debug.Log("[GAMESOUND] ambience ready " + track);
            return clip;
        }

        // ---------------- Luzes da largada ----------------
        static AudioSource sfx;
        static AudioClip beepShort, beepLong;

        /// <summary>Bipe das luzes da largada: curto (0,15 s, 880 Hz) a cada luz vermelha; longo (0,7 s, 1320 Hz) na largada.
        /// Sintetizado (seno com ataque/saída suaves para não estalar); o volume geral vem do AudioListener.</summary>
        public static void StartBeep(bool go)
        {
            try
            {
                Ensure();
                if (sfx == null) { sfx = owner.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.ignoreListenerPause = true; }
                if (beepShort == null) beepShort = Tone("StartBeepRed", 880f, 0.15f);
                if (beepLong == null) beepLong = Tone("StartBeepGo", 1320f, 0.7f);
                sfx.PlayOneShot(go ? beepLong : beepShort, 0.7f);
            }
            catch (System.Exception ex) { Debug.LogWarning("[GAMESOUND] bipe ignorado: " + ex.Message); }
        }

        static AudioClip Tone(string name, float hz, float seconds)
        {
            const int rate = 44100;
            int n = Mathf.Max(1, (int)(rate * seconds));
            var data = new float[n];
            float attack = 0.008f * rate, release = Mathf.Min(0.06f, seconds * 0.4f) * rate;
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1f, i / attack) * Mathf.Min(1f, (n - i) / release);
                float t = (float)i / rate;
                // fundamental + um pouco de 2º harmônico (soa como o bipe eletrônico do painel)
                data[i] = env * (0.8f * Mathf.Sin(2f * Mathf.PI * hz * t) + 0.2f * Mathf.Sin(4f * Mathf.PI * hz * t));
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
