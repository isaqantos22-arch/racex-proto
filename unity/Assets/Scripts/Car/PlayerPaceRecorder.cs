using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// Memória da pilotagem do jogador: maior velocidade LIMPA por trecho da pista (128 trechos por pista).
    /// A IA Difícil/Expert usa esses números em CarAI.CornerSpeedAhead (limitados pela física).
    /// Erros são descartados: só grava com 4 rodas no asfalto, fora do pit e com a corrida rodando.
    /// </summary>
    public static class PlayerPaceLearner
    {
        /// <summary>v100 (04/11, pedido do usuário: "14 m é muito"): 720 trechos por volta — 2,5 m no Circuito Verde (1,8 km), ~10 m
        /// na Ardenne (7 km). Vale para o perfil de ritmo, o ESTILO (traçado, freio, tirar o pé, volante) e a melhor volta própria da IA.</summary>
        public const int Segments = 720;
        const string Key = "RaceX.PlayerPace.";
        // Pistas 3+ foram redesenhadas: amostras antigas não valem mais.
        // v99: a física dos monopostos mudou (pneu µ 2,9 → 1,8, apoio menor) — as voltas gravadas antes eram de um carro que
        // fazia curva rápida de pé embaixo; recomeça em todas as pistas.
        static string KeyFor(int trackId) => "RaceX.PlayerPace5." + trackId;
        static readonly System.Collections.Generic.Dictionary<int, float[]> cache = new System.Collections.Generic.Dictionary<int, float[]>();
        static readonly System.Collections.Generic.HashSet<int> dirty = new System.Collections.Generic.HashSet<int>();

        static float[] Get(int trackId)
        {
            if (cache.TryGetValue(trackId, out var arr)) return arr;
            arr = new float[Segments];
            string raw = PlayerPrefs.GetString(KeyFor(trackId), "");
            if (!string.IsNullOrEmpty(raw))
            {
                var parts = raw.Split(';');
                for (int i = 0; i < parts.Length && i < Segments; i++)
                    float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
            }
            cache[trackId] = arr;
            return arr;
        }

        public static int Seg(float along, float length)
        {
            if (length <= 1f) return 0;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(along, length) / length * Segments), 0, Segments - 1);
        }

        public static void Record(int trackId, float along, float length, float speed)
        {
            var arr = Get(trackId);
            int i = Seg(along, length);
            if (speed > arr[i]) { arr[i] = speed; dirty.Add(trackId); }
        }

        static string BestKey(int trackId) => "RaceX.PlayerPaceBestLap3." + trackId;

        /// <summary>Melhor volta LIMPA já usada como referência nesta pista (0 = nenhuma).</summary>
        public static float BestCleanLap(int trackId) => PlayerPrefs.GetFloat(BestKey(trackId), 0f);

        /// <summary>v79: grava o perfil de velocidade da volta INTEIRA (só volta limpa e melhor pessoal).
        /// Trechos sem amostra mantêm o valor anterior.</summary>
        public static void CommitLap(int trackId, float[] lapSpeeds, float lapTime)
        {
            if (lapSpeeds == null || lapSpeeds.Length < Segments) return;
            var arr = Get(trackId);
            for (int i = 0; i < Segments; i++)
                if (lapSpeeds[i] > 5f && lapSpeeds[i] < 120f) arr[i] = lapSpeeds[i];
            dirty.Add(trackId);
            PlayerPrefs.SetFloat(BestKey(trackId), lapTime);
            Flush();
        }

        /// <summary>Velocidade limpa do jogador (m/s) no trecho; 0 se ainda não há dado.</summary>
        public static float SampleSpeed(int trackId, float along, float length)
        {
            return Get(trackId)[Seg(along, length)];
        }

        /// <summary>Perfil do jogador nesta pista (cópia, m/s por trecho) — null se tiver menos da metade dos trechos (sem
        /// volta limpa suficiente). ONLINE: é o "ritmo do próprio jogador" que o piloto automático usa.</summary>
        public static float[] Profile(int trackId)
        {
            var arr = Get(trackId);
            int filled = 0;
            foreach (float v in arr) if (v > 5f) filled++;
            if (filled < Segments / 2) return null;
            return (float[])arr.Clone();
        }

        /// <summary>Perfil desta pista em texto ("v;v;...") para mandar ao servidor ao carregar a corrida online ("" = sem perfil).</summary>
        public static string ExportTrack(int trackId)
        {
            var arr = Profile(trackId);
            if (arr == null) return "";
            var sb = new System.Text.StringBuilder(Segments * 5);
            for (int i = 0; i < Segments; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(arr[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Lê o perfil vindo de outro jogador (online). null se vazio/inválido.</summary>
        public static float[] ParseProfile(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var parts = raw.Split(';');
            if (parts.Length < Segments / 2) return null;
            var arr = new float[Segments];
            int filled = 0;
            for (int i = 0; i < parts.Length && i < Segments; i++)
                if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 5f && v < 120f)
                { arr[i] = v; filled++; }
            return filled >= Segments / 2 ? arr : null;
        }

        public static void Flush()
        {
            if (dirty.Count == 0) return;
            var sb = new System.Text.StringBuilder(Segments * 6);
            foreach (int id in dirty)
            {
                var arr = Get(id);
                sb.Length = 0;
                for (int i = 0; i < Segments; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(arr[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
                PlayerPrefs.SetString(KeyFor(id), sb.ToString());
            }
            dirty.Clear();
            PlayerPrefs.Save();
        }

        /// <summary>Exporta "id=v;v;...|id=..." de todas as pistas com dado (para a nuvem).</summary>
        public static string ExportAll()
        {
            Flush();
            var sb = new System.Text.StringBuilder("v5");
            foreach (int id in System.Enum.GetValues(typeof(TrackId)))
            {
                string raw = PlayerPrefs.GetString(KeyFor(id), "");
                if (string.IsNullOrEmpty(raw)) continue;
                sb.Append('|');
                sb.Append(id).Append('=').Append(raw);
            }
            return sb.Length > 2 ? sb.ToString() : "";
        }

        /// <summary>Mescla dados da nuvem: por trecho fica a MAIOR velocidade limpa (local ou nuvem).</summary>
        public static void ImportMerge(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            // Só dados da física atual (v3, v99): perfis de antes vinham do carro de pneu 2,9 e voltariam pela nuvem.
            if (!data.StartsWith("v5|")) return;   // v100: pneu novo (v4)
            foreach (var block in data.Split('|'))
            {
                int eq = block.IndexOf('=');
                if (eq <= 0 || !int.TryParse(block.Substring(0, eq), out int id)) continue;
                var arr = Get(id);
                var parts = block.Substring(eq + 1).Split(';');
                bool changed = false;
                for (int i = 0; i < parts.Length && i < Segments; i++)
                    if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, ic, out float v) && v > arr[i] && v < 120f)
                    { arr[i] = v; changed = true; }
                if (changed) dirty.Add(id);
            }
            Flush();
        }
    }

    /// <summary>Vai no carro do jogador. v79: amostra a 10 Hz num BUFFER DA VOLTA ATUAL; a volta é
    /// descartada se sair da pista, bater, dar ré, entrar no pit ou estiver na volta de saída. Só ao
    /// COMPLETAR uma volta limpa que seja a melhor pessoal na pista o perfil é gravado (antes gravava
    /// trechos soltos o tempo todo, inclusive de voltas com erro).</summary>
    public class PlayerPaceRecorder : MonoBehaviour
    {
        CarController car;
        WaypointSystem waypoints;
        RaceManager race;
        RaceEntry entry;
        int trackId;
        int hint;
        float sampleTimer;
        readonly float[] lapBuf = new float[PlayerPaceLearner.Segments];
        bool lapDirty = true; // primeira volta (largada/saída do box) nunca conta
        int lastLap = -1;
        CarAI aiPilot;
        // v99: ESTILO da volta atual (PlayerStyle) — traçado, freio, tirar o pé, desaceleração e volante por trecho.
        readonly float[] latSum = new float[PlayerPaceLearner.Segments];
        readonly int[] nBuf = new int[PlayerPaceLearner.Segments];
        readonly float[] brakeSum = new float[PlayerPaceLearner.Segments];
        readonly float[] coastN = new float[PlayerPaceLearner.Segments];
        readonly float[] decelBuf = new float[PlayerPaceLearner.Segments];
        readonly float[] steerSum = new float[PlayerPaceLearner.Segments];
        float prevV = -1f, prevSteer, prevT = -1f;
        int lapCompound = -1;
        bool lapWet, lapMixed;

        public void Initialize(CarController c, WaypointSystem wp, RaceManager rm, int track)
        {
            car = c; waypoints = wp; race = rm; trackId = track;
            entry = rm != null ? rm.GetEntry(c) : null;
            if (rm != null) rm.LapCompleted += OnLap;
            if (car != null) car.Collided += OnCollided;
            OpenTelemetry();
        }

        void OnDestroy()
        {
            if (race != null) race.LapCompleted -= OnLap;
            if (car != null) car.Collided -= OnCollided;
            CloseTelemetry();
        }

        // ---------- TELEMETRIA BRUTA DO JOGADOR (v100, 04/11, pedido: "observe tudo") ----------
        // Desktop, carro humano, fora do autoteste: 10 Hz em persistentDataPath/telemetry/tele_<data>_<pista>.csv — posição na
        // pista, pedais, volante (comando e ângulo real), guinada, g lateral/longitudinal, escorregamento de cada roda e dos
        // eixos, perda de atrito pela carga, temperatura e composto do pneu, água, marcha, rpm, fora/box/volta suja.
        System.IO.StreamWriter tele;
        float teleFlush, telePrevV = -1f, telePrevT = -1f;
        public static string LastTelemetryPath { get; private set; }

        void OpenTelemetry()
        {
            try
            {
                if (Application.isMobilePlatform || RaceX.Core.AutoTest.Active || entry == null || !entry.isHuman) return;
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "telemetry");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "tele_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_t" + trackId + ".csv");
                tele = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8);
                tele.WriteLine("t;volta;along;lat;kmh;acel;freio;volanteCmd;volanteGraus;guinadaGrausS;gLat;gLong;slipFL;slipFR;slipRL;slipRR;fSide;rSide;cargaMul;pneuTemp;composto;agua;marcha;rpm;fora;box;voltaSuja;carro;massa;apoio;aderLat;aderLong");
                LastTelemetryPath = path;
                Debug.Log("[Telemetria] gravando em " + path);
            }
            catch (System.Exception ex) { Debug.LogWarning("[Telemetria] não abriu: " + ex.Message); tele = null; }
        }

        void CloseTelemetry()
        {
            try { if (tele != null) { tele.Flush(); tele.Dispose(); } } catch (System.Exception) { }
            tele = null;
        }

        void WriteTelemetry(float along, float lat)
        {
            if (tele == null || car == null) return;
            try
            {
                var rb = car.Body;
                float v = car.ForwardSpeed, now = Time.time;
                float yaw = rb != null ? rb.angularVelocity.y : 0f;
                float gLat = Mathf.Abs(v * yaw) / 9.81f;
                float gLong = telePrevT > 0f && now - telePrevT > 0.04f ? (v - telePrevV) / (now - telePrevT) / 9.81f : 0f;
                telePrevV = v; telePrevT = now;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var prog = race != null && entry != null ? race.GetProgress(entry) : null;
                var sp = car.spec;
                tele.WriteLine(string.Join(";", new string[] {
                    now.ToString("0.00", ci), (prog != null ? prog.lap : 0).ToString(ci), along.ToString("0.0", ci), lat.ToString("0.00", ci),
                    (v * 3.6f).ToString("0.0", ci), car.throttleInput.ToString("0.00", ci), car.brakeInput.ToString("0.00", ci),
                    car.steerInput.ToString("0.000", ci), car.SteerAngle.ToString("0.0", ci), (yaw * Mathf.Rad2Deg).ToString("0.0", ci),
                    gLat.ToString("0.00", ci), gLong.ToString("0.00", ci),
                    car.WheelSlip[0].ToString("0.00", ci), car.WheelSlip[1].ToString("0.00", ci), car.WheelSlip[2].ToString("0.00", ci), car.WheelSlip[3].ToString("0.00", ci),
                    car.FrontSideSlip.ToString("0.00", ci), car.RearSideSlip.ToString("0.00", ci), car.AvgLoadMul.ToString("0.000", ci),
                    RaceX.Core.PitLaneSystem.TireTemp(car).ToString("0.00", ci), RaceX.Core.PitLaneSystem.CompoundOf(car).ToString(ci),
                    RaceX.Core.WeatherSystem.Wetness.ToString("0.00", ci), car.Gear.ToString(ci), car.Rpm.ToString("0", ci),
                    car.IsOffTrack ? "1" : "0", (RaceX.Core.PitLaneSystem.IsInPitFlow(car) || car.pitControlled) ? "1" : "0", lapDirty ? "1" : "0",
                    sp != null ? sp.displayName : "", sp != null ? sp.massKg.ToString("0", ci) : "", sp != null ? sp.downforceCoefficient.ToString("0.00", ci) : "",
                    sp != null ? sp.sidewaysStiffness.ToString("0.00", ci) : "", sp != null ? sp.forwardStiffness.ToString("0.00", ci) : "" }));
                teleFlush += 0.1f;
                if (teleFlush >= 2f) { teleFlush = 0f; tele.Flush(); }
            }
            catch (System.Exception) { CloseTelemetry(); }
        }

        void OnCollided(float relative, Vector3 point)
        {
            if (relative > 12f) lapDirty = true;
        }

        int prevSeg = -1;

        void ResetLap(bool dirtyStart)
        {
            prevSeg = -1;
            System.Array.Clear(lapBuf, 0, lapBuf.Length);
            System.Array.Clear(latSum, 0, latSum.Length);
            System.Array.Clear(nBuf, 0, nBuf.Length);
            System.Array.Clear(brakeSum, 0, brakeSum.Length);
            System.Array.Clear(coastN, 0, coastN.Length);
            System.Array.Clear(decelBuf, 0, decelBuf.Length);
            System.Array.Clear(steerSum, 0, steerSum.Length);
            prevV = -1f; prevT = -1f;
            lapCompound = car != null ? RaceX.Core.PitLaneSystem.CompoundOf(car) : -1;
            lapWet = RaceX.Core.WeatherSystem.Wetness >= PlayerStyle.WetFrom;
            lapMixed = false;
            lapDirty = dirtyStart;
        }

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e == null || e.car != car) return;
            try
            {
                int filled = 0;
                for (int i = 0; i < lapBuf.Length; i++) if (lapBuf[i] > 5f) filled++;
                float prevBest = PlayerPaceLearner.BestCleanLap(trackId);
                bool personalBest = prevBest <= 0f || lapTime < prevBest;
                bool covered = filled >= PlayerPaceLearner.Segments * 0.85f && lapTime > 10f;
                // Volta precisa ter cobertura quase total do traçado (>= 85% dos trechos amostrados). Volta do piloto automático
                // do autoteste (stylerecord=1) só alimenta o arquivo de estilo do teste — nunca o perfil de ritmo do aparelho.
                if (!lapDirty && personalBest && covered && !RaceX.Core.AutoTest.StyleFromAutopilot)
                    PlayerPaceLearner.CommitLap(trackId, lapBuf, lapTime);
                // Estilo: melhor volta limpa NESTA condição (pista, tipo de carro, pneu, seco/molhado) — volta que mudou de pneu
                // ou atravessou o limite de pista molhada não conta.
                if (!lapDirty && !lapMixed && covered && lapCompound >= 0) PlayerStyle.Commit(BuildStyle(lapTime));
                // Aba GLOBAL: só volta limpa e completa vira recorde (pessoal → nuvem, com conta).
                if (!lapDirty && covered) RaceX.Core.GlobalBoard.OnCleanLap(trackId, car.spec, lapTime, lapCompound, lapWet);
            }
            catch (System.Exception ex) { Debug.LogWarning("[PlayerPace] volta ignorada: " + ex.Message); }
            ResetLap(false);
            lastLap = race != null && entry != null && race.GetProgress(entry) != null ? race.GetProgress(entry).lap : lastLap;
        }

        PlayerStyle.Profile BuildStyle(float lapTime)
        {
            var s = car.spec;
            var p = new PlayerStyle.Profile
            {
                track = trackId,
                formula = s != null && CarSpec.IsFormula(s.model),
                compound = lapCompound,
                wet = lapWet,
                lapTime = lapTime,
                when = System.DateTime.Now.ToString("s"),
                massKg = car.Body != null ? car.Body.mass : (s != null ? s.massKg : 0f),
                downforce = s != null ? s.downforceCoefficient : 0f,
                forwardStiffness = s != null ? s.forwardStiffness : 0f,
            };
            for (int i = 0; i < PlayerStyle.Segments; i++)
            {
                p.v[i] = lapBuf[i];
                int k = nBuf[i];
                p.n[i] = k;
                if (k == 0) continue;
                p.lat[i] = latSum[i] / k;
                p.brake[i] = brakeSum[i] / k;
                p.coast[i] = coastN[i] / k;
                p.decel[i] = decelBuf[i];
                p.steerRate[i] = steerSum[i] / k;
            }
            // Lateral SUAVIZADA (média móvel de 5 trechos, ~12 m no Circuito Verde): com 720 trechos cada amostra de 2,5 m traz o
            // tremor do volante — a IA perseguia esse tremor e "puxava o volante" nas curvas suaves/lentas (relato de 04/11).
            var raw = (float[])p.lat.Clone();
            int S = PlayerStyle.Segments;
            for (int i = 0; i < S; i++)
            {
                if (p.n[i] == 0) continue;
                float sum = 0f; int cnt = 0;
                for (int o = -2; o <= 2; o++) { int j = ((i + o) % S + S) % S; if (p.n[j] > 0) { sum += raw[j]; cnt++; } }
                if (cnt > 0) p.lat[i] = sum / cnt;
            }
            return p;
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (car == null || waypoints == null || waypoints.Spline == null) return;
            if (race != null && entry != null)
            {
                var p = race.GetProgress(entry);
                int lap = p != null ? p.lap : 0;
                // Volta mudou sem passar pelo OnLap (volta de saída, cruzamento pós-pit): descarta.
                if (lap != lastLap) { if (lastLap >= 0) ResetLap(entry.outLap); lastLap = lap; }
                if (entry.outLap) lapDirty = true;
            }
            sampleTimer -= Time.deltaTime;
            if (sampleTimer > 0f) return;
            sampleTimer = 0.1f;
            if (race != null && race.State != RaceState.Racing) return;
            if (tele != null && waypoints != null)
            {
                float alongT = waypoints.DistanceAlong(car.transform.position, ref hint);
                var poseT = waypoints.Spline.PoseAtDistance(alongT);
                WriteTelemetry(alongT, Vector3.Dot(car.transform.position - poseT.position, poseT.rotation * Vector3.right));
            }
            // Piloto automático (autoteste) no carro do jogador: NÃO é pilotagem do jogador — não grava (exceto stylerecord=1,
            // que testa o caminho do estilo num arquivo separado).
            if (aiPilot == null) aiPilot = car.GetComponent<CarAI>();
            if (aiPilot != null && aiPilot.enabled && !RaceX.Core.AutoTest.StyleFromAutopilot) { lapDirty = true; return; }
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car) || car.pitControlled) { lapDirty = true; return; }
            if (car.IsOffTrack || car.IsReversing) { lapDirty = true; return; }
            if (car.GroundedWheels < 4) { prevT = -1f; return; }
            float v = car.ForwardSpeed;
            if (v < 5f) { prevT = -1f; return; }
            float along = waypoints.DistanceAlong(car.transform.position, ref hint);
            int seg = PlayerPaceLearner.Seg(along, waypoints.Spline.Length);
            // Estilo: onde ele passa (lateral), freio, só tirando o pé, desaceleração na freada e ritmo do volante.
            var pose = waypoints.Spline.PoseAtDistance(along);
            float latNow = Vector3.Dot(car.transform.position - pose.position, pose.rotation * Vector3.right);
            float now = Time.time, dt = prevT > 0f ? now - prevT : 0f;
            bool coast = car.throttleInput < 0.08f && car.brakeInput < 0.05f;
            float decelNow = dt > 0.04f && dt < 0.3f && car.brakeInput > 0.1f && prevV > 0f ? Mathf.Min(60f, (prevV - v) / dt) : 0f;
            float steerNow = dt > 0.04f && dt < 0.3f ? Mathf.Abs(car.steerInput - prevSteer) / dt : 0f;
            // v100: com 720 trechos (2,5 m) a amostra de 0,1 s pula trechos acima de ~90 km/h — a volta nunca fechava os 85% de
            // cobertura e NENHUMA volta virava estilo/ritmo/recorde (sessão do usuário de 04/11). Os trechos entre a amostra
            // anterior e esta recebem os mesmos valores (no máximo 40 trechos = 100 m, para não varrer a volta num salto).
            int n = PlayerPaceLearner.Segments, from = seg;
            if (prevSeg >= 0 && dt > 0f && dt < 0.3f)
            {
                int gap = (seg - prevSeg + n) % n;
                if (gap > 1 && gap <= 40) from = (prevSeg + 1) % n;
            }
            for (int sg = from; ; sg = (sg + 1) % n)
            {
                if (v > lapBuf[sg]) lapBuf[sg] = v;
                latSum[sg] += latNow; nBuf[sg]++;
                brakeSum[sg] += Mathf.Clamp01(car.brakeInput);
                if (coast) coastN[sg]++;
                if (decelNow > decelBuf[sg]) decelBuf[sg] = decelNow;
                steerSum[sg] += steerNow;
                if (sg == seg) break;
            }
            prevSeg = seg;
            prevV = v; prevSteer = car.steerInput; prevT = now;
            if ((RaceX.Core.WeatherSystem.Wetness >= PlayerStyle.WetFrom) != lapWet || RaceX.Core.PitLaneSystem.CompoundOf(car) != lapCompound) lapMixed = true;
        }

        void OnDisable() { PlayerPaceLearner.Flush(); try { if (tele != null) tele.Flush(); } catch (System.Exception) { } }
        void OnApplicationPause(bool p) { if (p) { PlayerPaceLearner.Flush(); try { if (tele != null) tele.Flush(); } catch (System.Exception) { } } }
        void OnApplicationQuit() { CloseTelemetry(); }
    }
}
