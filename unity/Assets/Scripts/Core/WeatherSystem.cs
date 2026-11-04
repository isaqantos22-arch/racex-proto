using UnityEngine;

namespace RaceX.Core
{
    public enum WeatherKind { Dry = 0, Cloudy = 1, LightRain = 2, HeavyRain = 3 }

    /// <summary>Previsão de UMA sessão (determinística por temporada/etapa/fase — o hub mostra antes).</summary>
    public struct WeatherForecast
    {
        public WeatherKind kind;      // condição dominante
        public float airTemp;         // °C
        public float trackTemp;       // °C (pista seca ao sol fica bem acima do ar)
        /// <summary>Quanto o SOL esquenta a pista acima do ar neste dia (°C, céu limpo) — vale também numa sessão
        /// mista quando a chuva para e o sol volta.</summary>
        public float sunGain;
        public float rainStart;       // fração da sessão em que a chuva começa (-1 = não muda)
        public float rainEnd;         // fração em que para (-1 = não para)
        public WeatherKind changeTo;  // condição durante a janela de mudança

        public string Label => WeatherSystem.KindLabel(kind) + (rainStart >= 0f ? " → " + WeatherSystem.KindLabel(changeTo) : "");
    }

    /// <summary>
    /// Clima da sessão (v80). Água na pista (0..1) sobe com a chuva e seca sem ela; a temperatura da
    /// pista cai com nuvem/chuva. Pneus, IA e estratégia leem Wetness/TrackTemp. Visual: chuva em
    /// partículas presa à câmera, luz e névoa mais escuras. Nada aqui pode quebrar o carregamento.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        public static float Wetness => Instance != null ? Instance.wetness : 0f;
        /// <summary>Previsão da sessão em andamento (a equipe usa para marcar a parada de chuva na volta certa).</summary>
        public static WeatherForecast CurrentForecast => Instance != null ? Instance.fc : new WeatherForecast { kind = WeatherKind.Dry, rainStart = -1f, rainEnd = -1f };

        // ÁGUA NA PISTA (v81, embasada — Claude outputs/PESQUISA_v81.md §12). Duas partes:
        // (1) água DENTRO da textura do asfalto (0..TextureMm) e (2) LÂMINA acima dela (Gallaway, TTI 1971).
        /// <summary>Profundidade média de textura: especificação de pista de corrida de Le Mans, 0,70–0,90 mm.</summary>
        public const float TextureMm = 0.8f;
        /// <summary>Caimento lateral nas retas: 1,5–3% (texto derivado da FIA; guia Wirtgen) — meio da faixa.</summary>
        const float CrossSlope = 0.0225f;
        /// <summary>Rugosidade de Manning para escoamento laminar em superfície lisa (asfalto): 0,011 (TR-55).</summary>
        const float ManningN = 0.011f;
        /// <summary>Evaporação de água exposta em dia quente: 2,0–2,5 mm/h (medição em Davis, CA). Única taxa medida achada.</summary>
        const float EvapMmH = 2.25f;
        /// <summary>Lâmina de referência = chuva de 50 mm/h (limite da chuva "violenta"): define o 100% interno.</summary>
        public static float FilmRefMm => Instance != null ? Instance.filmRef : GallawayFilmMm(50f, 12f);
        public static float FilmMm => Instance != null ? Instance.film : 0f;
        public static float TextureWaterMm => Instance != null ? Instance.texWater : 0f;
        public static float RainMmH => Instance != null ? Instance.mmh : 0f;
        /// <summary>Água para onde a chuva ATUAL leva a pista (o "radar" da equipe).</summary>
        public static float EquilibriumWetness => Instance == null ? 0f : Instance.mmh <= 0f ? Instance.wetness
            : (TextureMm + GallawayFilmMm(Instance.mmh, Instance.drainLen)) / (TextureMm + Instance.filmRef);

        /// <summary>Lâmina acima da textura (mm) em regime — Gallaway (TTI 1971): d[in] = 3,38e-3·(1/T)^0,11·L^0,43·
        /// I^0,59·(1/S)^0,42 − T (T in, L ft, I in/h, S ft/ft), convertida exatamente para mm, m e mm/h:
        /// d = 0,0303·T^-0,11·L^0,43·I^0,59·S^-0,42 − T.</summary>
        public static float GallawayFilmMm(float iMmH, float drainLenM)
        {
            if (iMmH <= 0f) return 0f;
            return Mathf.Max(0f, 0.0303f * Mathf.Pow(TextureMm, -0.11f) * Mathf.Pow(drainLenM, 0.43f) * Mathf.Pow(iMmH, 0.59f) * Mathf.Pow(CrossSlope, -0.42f) - TextureMm);
        }

        /// <summary>Tempo (s) para a lâmina chegar ao regime — onda cinemática do FHWA/HEC-22 (métrico):
        /// t[min] = 6,943·(n·L)^0,6 / (I^0,4·S^0,3).</summary>
        static float SheetFlowSeconds(float iMmH, float drainLenM) =>
            60f * 6.943f * Mathf.Pow(ManningN * drainLenM, 0.6f) / (Mathf.Pow(Mathf.Max(1f, iMmH), 0.4f) * Mathf.Pow(CrossSlope, 0.3f));

        float texWater, film, mmh, lastMmH = 1f, drainLen = 12f, filmRef = 3.67f;
        public static float RainIntensity => Instance != null ? Instance.rain : 0f;
        public static float TrackTemp => Instance != null ? Instance.trackTemp : 30f;
        public static float AirTemp => Instance != null ? Instance.airTemp : 22f;
        public static WeatherKind Kind => Instance != null ? Instance.kindNow : WeatherKind.Dry;
        public static bool Active => Instance != null;

        WeatherForecast fc;
        RaceManager race;
        float wetness, rain, trackTemp, airTemp, baseTrackTemp, baseAirTemp, sunGain;
        WeatherKind kindNow;
        float sessionLength = 600f, elapsed;
        ParticleSystem rainFx, rainFar;
        Material skyMat;
        Color skyTint0 = new Color(0.5f, 0.5f, 0.5f, 1f), fogColor0;
        float skyExposure0 = 1.1f, lastGiGloom = -1f, giTimer;
        AudioSource rainAudio;
        Vector3 lastCamPos; bool camKnown;
        // Asfalto molhado: materiais da pista ficam mais escuros e brilhantes com a água (restaura no fim).
        readonly System.Collections.Generic.List<Material> roadMats = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<Color> roadColor = new System.Collections.Generic.List<Color>();
        readonly System.Collections.Generic.List<float> roadSmooth = new System.Collections.Generic.List<float>();
        float wetVisualTimer;
        Light sun;
        float sunBase = 1.2f;
        Color ambientBase;
        float fogBase;
        bool fogBaseOn;

        public static string KindLabel(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Cloudy: return "NUBLADO";
                case WeatherKind.LightRain: return "CHUVA FRACA";
                case WeatherKind.HeavyRain: return "CHUVA FORTE";
                default: return "SECO";
            }
        }

        public static Color KindColor(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Cloudy: return new Color(0.7f, 0.74f, 0.8f);
                case WeatherKind.LightRain: return new Color(0.35f, 0.7f, 1f);
                case WeatherKind.HeavyRain: return new Color(0.25f, 0.45f, 1f);
                default: return new Color(1f, 0.82f, 0.25f);
            }
        }

        /// <summary>Texto curto do estado atual para HUD: "CHUVA FRACA · PISTA 45% MOLHADA · 24°C".</summary>
        public static string StatusText()
        {
            if (Instance == null) return "";
            float sat = Instance.texWater / TextureMm;
            float f = Instance.film;
            // Níveis de JOGO pela água real: poças até o limite do intermediário (1,4 mm), alagada até o do pneu de
            // chuva (3,4 mm), acima disso alagamento extremo.
            string track = f >= 3.4f ? "ALAGAMENTO EXTREMO"
                         : f >= 1.4f ? "PISTA ALAGADA"
                         : f >= 0.05f ? "POÇAS NA PISTA"
                         : sat >= 0.97f ? "PISTA MOLHADA"
                         : sat > 0.04f ? "PISTA ÚMIDA"
                         : "PISTA SECA";
            if (sat > 0.04f && Instance.mmh <= 0.05f) track += " (SECANDO)";
            if (sat > 0.04f) track += " · IDEAL: " + TyreModel.Name(TyreModel.BestForWetness(Instance.wetness, 1));
            return KindLabel(Instance.kindNow) + " · " + track + " · " + Mathf.RoundToInt(Instance.trackTemp) + "°C";
        }

        static float RainOf(WeatherKind k) => k == WeatherKind.HeavyRain ? 1f : k == WeatherKind.LightRain ? 0.35f : 0f;

        /// <summary>Intensidade (mm/h) dentro da categoria oficial: fraca 1,0–2,5 mm/h; forte 7,6–50 mm/h (AMS).</summary>
        static float MmHOf(WeatherKind k, float n) => k == WeatherKind.HeavyRain ? Mathf.Lerp(7.6f, 50f, n) : k == WeatherKind.LightRain ? Mathf.Lerp(1.0f, 2.5f, n) : 0f;

        /// <summary>Oscilação lenta e determinística da intensidade (períodos de ~7 e ~17 min): fraca 0,55–1,3×,
        /// forte 0,75–1,0×. Faz a água subir, estabilizar e às vezes recuar sem trocar a condição.</summary>
        float SwingN(float t)
        {
            float ph = (fc.airTemp * 13.7f + fc.sunGain * 3.1f) % 6.283f;
            float n = 0.5f + 0.32f * Mathf.Sin(t * 0.015f + ph) + 0.18f * Mathf.Sin(t * 0.0062f + ph * 1.7f); // 0..1
            return Mathf.Clamp01(n);
        }

        float RainSwing(float t)
        {
            float n = SwingN(t);
            return kindNow == WeatherKind.HeavyRain ? Mathf.Lerp(0.75f, 1f, n) : Mathf.Lerp(0.55f, 1.1f, n);
        }

        /// <summary>Previsão determinística (mesmos dados no hub e na sessão).</summary>
        public static WeatherForecast ForecastFor(int season, int round, int phase, int trackId)
        {
            int seed = season * 7919 + round * 104729 + phase * 1299709 + trackId * 15485863;
            var rnd = new System.Random(seed);
            // Probabilidades de jogo (a F1 real corre ~95% no seco — aqui há mais variedade para testar pneus).
            double rainBias = trackId == 1 || trackId == 19 ? -0.12 : trackId == 0 || trackId == 5 || trackId == 14 ? 0.08 : 0.0;
            double p = rnd.NextDouble();
            var f = new WeatherForecast { rainStart = -1f, rainEnd = -1f };
            if (p < 0.58 - rainBias) f.kind = WeatherKind.Dry;
            else if (p < 0.80 - rainBias * 0.5) f.kind = WeatherKind.Cloudy;
            else if (p < 0.92) f.kind = WeatherKind.LightRain;
            else f.kind = WeatherKind.HeavyRain;
            // Ar mais frio em dia de chuva (antes 16–32 °C para QUALQUER clima → "chuva fraca, pista 38 °C").
            double r = rnd.NextDouble();
            f.airTemp = f.kind == WeatherKind.Dry ? (float)(16 + r * 18)          // 16–34 °C
                      : f.kind == WeatherKind.Cloudy ? (float)(14 + r * 14)       // 14–28 °C
                      : f.kind == WeatherKind.LightRain ? (float)(12 + r * 12)    // 12–24 °C
                      : (float)(10 + r * 12);                                     // 10–22 °C
            f.sunGain = (float)(14 + rnd.NextDouble() * 12);                      // céu limpo: +14..+26 °C
            f.trackTemp = TrackTempFor(f.kind, f.airTemp, f.sunGain, InitialWetness(f.kind));
            // Sessão MISTA (~25%): a chuva chega (ou para) no meio da sessão.
            if (rnd.NextDouble() < 0.25)
            {
                f.rainStart = (float)(0.15 + rnd.NextDouble() * 0.5);
                f.rainEnd = rnd.NextDouble() < 0.5 ? Mathf.Min(0.95f, f.rainStart + 0.2f + (float)rnd.NextDouble() * 0.3f) : -1f;
                f.changeTo = f.kind >= WeatherKind.LightRain ? WeatherKind.Cloudy : (rnd.NextDouble() < 0.6 ? WeatherKind.LightRain : WeatherKind.HeavyRain);
            }
            return f;
        }

        /// <summary>Previsão forçada (teste automático): dry|cloudy|light|heavy|mixed.</summary>
        public static WeatherForecast Forced(string kind)
        {
            var f = new WeatherForecast { airTemp = 24f, sunGain = 20f, rainStart = -1f, rainEnd = -1f };
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "cloudy": f.kind = WeatherKind.Cloudy; f.airTemp = 20f; break;
                case "light": f.kind = WeatherKind.LightRain; f.airTemp = 18f; break;
                case "heavy": f.kind = WeatherKind.HeavyRain; f.airTemp = 16f; break;
                case "mixed": f.kind = WeatherKind.Dry; f.rainStart = 0.15f; f.rainEnd = 0.6f; f.changeTo = WeatherKind.HeavyRain; break;
                default: f.kind = WeatherKind.Dry; break;
            }
            f.trackTemp = TrackTempFor(f.kind, f.airTemp, f.sunGain, InitialWetness(f.kind));
            return f;
        }

        /// <summary>Água na pista no INÍCIO da sessão (chuva forte já alagada, fraca já úmida).</summary>
        public static float InitialWetness(WeatherKind k)
        {
            if (k < WeatherKind.LightRain) return 0f;
            float fr = GallawayFilmMm(50f, 12f);
            return Mathf.Clamp01((TextureMm + GallawayFilmMm(MmHOf(k, 0.5f), 12f)) / (TextureMm + fr));
        }

        /// <summary>Temperatura de equilíbrio da pista (dados reais, ver PESQUISA_racex_v80.md §11):
        /// sol: pista 14–26 °C acima do ar (Sepang ar 33/pista 58; Interlagos sprint 2024 ar 28/pista 49);
        /// nublado: 3–9 °C acima (nuvem tira até 20 °C da pista); chuva: ≈ temperatura do ar (Suzuka 2024 ar 20/pista 17;
        /// Interlagos quali molhada 2024 ar 20/pista 23); água evaporando tira mais ~3 °C. Carros NÃO esquentam a pista
        /// de forma mensurável (o sol é o que manda) — sem termo de tráfego.</summary>
        public static float TrackTempFor(WeatherKind k, float air, float sunGain, float wet)
        {
            float offset = k == WeatherKind.Dry ? sunGain
                         : k == WeatherKind.Cloudy ? Mathf.Clamp(sunGain * 0.3f, 3f, 9f)
                         : k == WeatherKind.LightRain ? 2f
                         : 0f;
            // Água sobre o asfalto: a evaporação segura a pista perto do ar mesmo com sol voltando.
            offset *= 1f - 0.8f * Mathf.Clamp01(wet);
            return air + offset - 3f * Mathf.Clamp01(wet);
        }

        public static WeatherSystem Begin(RaceManager raceManager, WeatherForecast forecast, float sessionSeconds)
        {
            var go = new GameObject("WeatherSystem");
            var w = go.AddComponent<WeatherSystem>();
            w.race = raceManager;
            w.fc = forecast;
            w.sessionLength = Mathf.Max(120f, sessionSeconds);
            w.airTemp = forecast.airTemp;
            w.baseAirTemp = forecast.airTemp;
            w.sunGain = forecast.sunGain > 0f ? forecast.sunGain : Mathf.Max(6f, forecast.trackTemp - forecast.airTemp);
            w.baseTrackTemp = forecast.trackTemp;
            w.trackTemp = forecast.trackTemp;
            w.kindNow = forecast.kind;
            w.rain = RainOf(forecast.kind);
            // Largura da pista desta etapa = caminho da água (caimento de borda a borda, aceito pela FIA).
            try { if (raceManager != null && raceManager.Track != null && raceManager.Track.Definition != null) w.drainLen = Mathf.Max(8f, raceManager.Track.Definition.roadWidth); } catch (System.Exception) { }
            w.filmRef = GallawayFilmMm(50f, w.drainLen);
            // Começa coerente com a condição: com chuva a textura já está cheia e a lâmina no regime da intensidade.
            w.mmh = MmHOf(forecast.kind, 0.5f);
            w.texWater = forecast.kind >= WeatherKind.LightRain ? TextureMm : 0f;
            w.film = w.texWater >= TextureMm ? GallawayFilmMm(w.mmh, w.drainLen) : 0f;
            w.wetness = Mathf.Clamp01((w.texWater + w.film) / (TextureMm + w.filmRef));
            w.trackTemp = TrackTempFor(forecast.kind, w.airTemp, w.sunGain, w.wetness); // começa coerente com a água
            try { w.SetupVisuals(); } catch (System.Exception ex) { Debug.LogWarning("[Weather] visual ignorado: " + ex.Message); }
            Debug.Log("[WEATHER] inicio " + forecast.Label + " ar=" + forecast.airTemp.ToString("0") + " pista=" + forecast.trackTemp.ToString("0") + " sessao=" + w.sessionLength.ToString("0") + "s");
            return w;
        }

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Os materiais do asfalto são compartilhados (MaterialLibrary): devolve o visual seco.
            for (int i = 0; i < roadMats.Count; i++)
            {
                var m = roadMats[i];
                if (m == null) continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", roadColor[i]);
                if (m.HasProperty("_Color")) m.SetColor("_Color", roadColor[i]);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", roadSmooth[i]);
            }
        }

        /// <summary>Materiais do asfalto na cena (nome "Asphalt..."), guardando a cor/brilho secos.</summary>
        void CollectRoadMaterials()
        {
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var m = r.sharedMaterial;
                    if (m == null || !m.name.StartsWith("Asphalt") || roadMats.Contains(m)) continue;
                    roadMats.Add(m);
                    roadColor.Add(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.gray);
                    roadSmooth.Add(m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.2f);
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>Ruído de chuva gerado no jogo (2 s em loop, ruído marrom suave) — sem arquivo de áudio.</summary>
        static AudioClip RainClip()
        {
            const int rate = 22050, len = rate * 2;
            var data = new float[len];
            var rnd = new System.Random(77);
            float b = 0f;
            for (int i = 0; i < len; i++)
            {
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                b = b * 0.97f + w * 0.12f;           // grave (chiado de chuva contínua)
                data[i] = (b * 0.7f + w * 0.18f) * 0.6f; // + agudo leve (gotas)
            }
            for (int i = 0; i < 400; i++) { float k = i / 400f; data[i] *= k; data[len - 1 - i] *= k; } // emenda sem estalo
            var clip = AudioClip.Create("RainNoise", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (race != null && race.State == RaceState.Racing) elapsed += dt;
            float frac = elapsed / sessionLength;
            WeatherKind k = fc.kind;
            if (fc.rainStart >= 0f && frac >= fc.rainStart && (fc.rainEnd < 0f || frac < fc.rainEnd)) k = fc.changeTo;
            if (k != kindNow)
            {
                Debug.Log("[WEATHER] mudou para " + KindLabel(k) + " em " + elapsed.ToString("0") + "s");
                kindNow = k;
            }
            // Chuva VARIA devagar dentro da condição (nuvens passando): fraca entre ~0,2 e ~0,45; forte 0,75–1,0.
            float targetRain = RainOf(kindNow) * RainSwing(elapsed);
            rain = Mathf.MoveTowards(rain, targetRain, dt * 0.05f);
            // ÁGUA POR BALANÇO (v81): entra com a chuva, sai por escoamento, evaporação e pelos carros (mais rápido
            // sem chuva e com a pista quente). A pista PARA num equilíbrio: garoa ~35%, chuva fraca ~50–60%, forte
            // ~93%; sem chuva seca em ~10–15 min. Antes qualquer chuva só somava e TODA sessão chegava a 100%.
            // Coeficientes são estimativa de projeto (não há medição pública de lâmina d'água por intensidade).
            // Água pela hidrologia de pavimento: a chuva enche primeiro a TEXTURA (volume = profundidade de textura);
            // a LÂMINA vai ao regime de Gallaway no tempo da onda cinemática (HEC-22); sem chuva a lâmina escoa no
            // mesmo tempo e a textura seca por evaporação (2,25 mm/h medido). Os carros secando o traçado não entram
            // (não há número publicado).
            float targetMmH = MmHOf(kindNow, SwingN(elapsed));
            mmh = Mathf.MoveTowards(mmh, targetMmH, dt * 0.2f);
            if (mmh > 0.05f)
            {
                lastMmH = mmh;
                texWater = Mathf.Min(TextureMm, texWater + mmh / 3600f * dt);
            }
            float filmTarget = mmh > 0.05f && texWater >= TextureMm - 1e-3f ? GallawayFilmMm(mmh, drainLen) : 0f;
            film += (filmTarget - film) * (1f - Mathf.Exp(-dt / SheetFlowSeconds(lastMmH, drainLen)));
            if (mmh <= 0.05f && film < 0.01f) texWater = Mathf.Max(0f, texWater - EvapMmH / 3600f * dt);
            wetness = Mathf.Clamp01((texWater + film) / (TextureMm + filmRef));
            // Ar cai até 3 °C com a chuva caindo; a pista vai para o equilíbrio do clima ATUAL e da água.
            airTemp = Mathf.MoveTowards(airTemp, baseAirTemp - 3f * rain, dt * 0.02f);
            float targetTrack = TrackTempFor(kindNow, airTemp, sunGain, wetness);
            // Chuva esfria a pista em poucos minutos (~6 °C/min); o sol reaquece devagar (~1,5 °C/min).
            float rate = targetTrack < trackTemp ? (rain > 0.02f ? 0.10f : 0.05f) : 0.025f;
            trackTemp = Mathf.MoveTowards(trackTemp, targetTrack, dt * rate);
            UpdateVisuals();
        }

        void SetupVisuals()
        {
            sun = RenderSettings.sun;
            if (sun != null) sunBase = sun.intensity;
            ambientBase = RenderSettings.ambientLight;
            fogBase = RenderSettings.fogDensity;
            fogBaseOn = RenderSettings.fog;
            var go = new GameObject("RainFx");
            rainFx = go.AddComponent<ParticleSystem>();
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var src = Resources.Load<Material>("RaceX_Particle");
            Material mat = src != null ? new Material(src) : null;
            if (mat == null)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Unlit/Color");
                if (sh != null) mat = new Material(sh);
            }
            if (mat != null) pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.velocityScale = 0.04f;
            pr.lengthScale = 2.5f;
            var main = rainFx.main;
            main.loop = true;
            main.startLifetime = 0.9f;
            main.startSpeed = 22f;
            main.startSize = 0.035f;
            main.startColor = new Color(0.75f, 0.82f, 0.95f, 0.45f);
            main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = rainFx.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // emite para BAIXO
            var em = rainFx.emission;
            em.rateOverTime = 0f;
            main.maxParticles = 1800;
            shape.scale = new Vector3(55f, 1f, 55f);
            try
            {
                rainAudio = gameObject.AddComponent<AudioSource>();
                rainAudio.clip = RainClip();
                rainAudio.loop = true;
                rainAudio.spatialBlend = 0f;
                rainAudio.volume = 0f;
                rainAudio.Play();
            }
            catch (System.Exception) { rainAudio = null; }
            // Camada LONGE: cobre ~220 m em volta (laterais e fundo) com gotas maiores e menos densas.
            try
            {
                var far = new GameObject("RainFxFar");
                rainFar = far.AddComponent<ParticleSystem>();
                var prf = far.GetComponent<ParticleSystemRenderer>();
                if (mat != null) prf.sharedMaterial = mat;
                prf.renderMode = ParticleSystemRenderMode.Stretch;
                prf.velocityScale = 0.05f;
                prf.lengthScale = 3.5f;
                var mf = rainFar.main;
                mf.loop = true; mf.startLifetime = 1.5f; mf.startSpeed = 26f; mf.startSize = 0.09f;
                mf.startColor = new Color(0.72f, 0.78f, 0.9f, 0.35f);
                mf.maxParticles = 2600;
                mf.simulationSpace = ParticleSystemSimulationSpace.World;
                var shf = rainFar.shape;
                shf.shapeType = ParticleSystemShapeType.Box;
                shf.scale = new Vector3(220f, 1f, 220f);
                far.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var emf = rainFar.emission; emf.rateOverTime = 0f;
            }
            catch (System.Exception) { rainFar = null; }
            skyMat = RenderSettings.skybox;
            if (skyMat != null)
            {
                if (skyMat.HasProperty("_Tint")) skyTint0 = skyMat.GetColor("_Tint");
                if (skyMat.HasProperty("_Exposure")) skyExposure0 = skyMat.GetFloat("_Exposure");
            }
            fogColor0 = RenderSettings.fogColor;
            CollectRoadMaterials();
        }

        void UpdateVisuals()
        {
            try
            {
                var cam = Camera.main;
                if (rainFx != null)
                {
                    if (cam != null)
                    {
                        // Antecipa pela velocidade da câmera: a 300 km/h o carro não "sai" da chuva.
                        Vector3 cp = cam.transform.position;
                        Vector3 camVel = camKnown && Time.deltaTime > 0f ? (cp - lastCamPos) / Time.deltaTime : Vector3.zero;
                        if (camVel.sqrMagnitude > 140f * 140f) camVel = Vector3.zero; // troca de câmera/teleporte
                        lastCamPos = cp; camKnown = true;
                        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                        rainFx.transform.position = cp + camVel * 0.75f + fwd.normalized * 8f + Vector3.up * 14f;
                        if (rainFar != null) rainFar.transform.position = cp + camVel * 0.75f + Vector3.up * 34f;
                    }
                    // Densidade pela qualidade gráfica (celular fraco não afoga em partícula).
                    int q = (int)SettingsManager.Quality;
                    float density = q <= 0 ? 600f : q == 1 ? 950f : 1400f;
                    var em = rainFx.emission;
                    em.rateOverTime = rain * density;
                    var main = rainFx.main;
                    main.startColor = new Color(0.75f, 0.82f, 0.95f, Mathf.Lerp(0.35f, 0.55f, rain));
                    if (rainFar != null)
                    {
                        var emf = rainFar.emission;
                        emf.rateOverTime = rain * (q <= 0 ? 700f : q == 1 ? 1100f : 1600f);
                    }
                }
                if (rainAudio != null)
                    rainAudio.volume = SettingsManager.SfxOn ? Mathf.Clamp01(rain) * 0.32f * SettingsManager.MasterVolume : 0f;
                // Pista molhada: asfalto escurece (~30%) e brilha (reflexo da água) conforme a água acumula.
                wetVisualTimer -= Time.deltaTime;
                if (wetVisualTimer <= 0f && roadMats.Count > 0)
                {
                    wetVisualTimer = 0.5f;
                    float w = Mathf.Clamp01(texWater / TextureMm);
                    float shine = Mathf.Clamp01(0.6f * w + film / 1.5f);
                    for (int i = 0; i < roadMats.Count; i++)
                    {
                        var m = roadMats[i];
                        if (m == null) continue;
                        var c0 = roadColor[i];
                        var c = new Color(c0.r * (1f - 0.32f * w), c0.g * (1f - 0.30f * w), c0.b * (1f - 0.26f * w), c0.a);
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Lerp(roadSmooth[i], 0.9f, shine));
                    }
                }
                float gloom = Mathf.Clamp01(rain * 0.8f + (kindNow == WeatherKind.Cloudy ? 0.25f : 0f));
                if (sun != null) sun.intensity = sunBase * (1f - 0.55f * gloom);
                RenderSettings.ambientLight = Color.Lerp(ambientBase, ambientBase * 0.6f, gloom);
                // CÉU acompanha o tempo: nublado acinzenta, chuva escurece (antes o céu ficava azul na tempestade).
                if (skyMat != null)
                {
                    if (skyMat.HasProperty("_Tint")) skyMat.SetColor("_Tint", Color.Lerp(skyTint0, new Color(0.34f, 0.36f, 0.4f, 1f), gloom));
                    if (skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExposure0 * (1f - 0.5f * gloom));
                    giTimer -= Time.deltaTime;
                    if (giTimer <= 0f && Mathf.Abs(gloom - lastGiGloom) > 0.05f)
                    {
                        giTimer = 5f; lastGiGloom = gloom;
                        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Skybox) DynamicGI.UpdateEnvironment();
                    }
                }
                if (gloom > 0.05f)
                {
                    RenderSettings.fog = true;
                    // Visibilidade cai na chuva forte (spray + cortina d'água): neblina cinza-azulada mais densa.
                    RenderSettings.fogDensity = Mathf.Max(fogBaseOn ? fogBase : 0f, 0.0065f * gloom);
                    RenderSettings.fogColor = Color.Lerp(fogColor0, new Color(0.5f, 0.54f, 0.6f, 1f), gloom);
                }
                else
                {
                    RenderSettings.fog = fogBaseOn;
                    RenderSettings.fogDensity = fogBase;
                    RenderSettings.fogColor = fogColor0;
                }
            }
            catch (System.Exception) { }
        }
    }
}
