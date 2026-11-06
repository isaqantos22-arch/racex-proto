using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>
    /// TESTE AUTOMÁTICO (v80): se existir <c>persistentDataPath/autotest.txt</c> (linhas chave=valor), o
    /// jogo pula o menu, abre uma corrida e um PILOTO AUTOMÁTICO (CarAI) dirige o carro do jogador de
    /// verdade — acelera, freia, faz as curvas. A cada 2 s grava telemetria em
    /// <c>autotest_log.txt</c> (e no logcat com a tag [AUTOTEST]): volta, velocidade, posição lateral,
    /// saídas de pista, NPCs parados, respawns, batidas, marcas de pneu, água na pista, pneus.
    /// Sem o arquivo nada disso existe (jogo normal).
    /// Chaves: track (número do TrackId), laps, cars, minutes, difficulty (0-3), weather
    /// (dry|cloudy|light|heavy|mixed), autopilot (1/0), car (nome do CarModel),
    /// pitlap (aperta PIT do jogador nessa volta; 0 = não), pitslowmo (câmera lenta só durante a parada do jogador,
    /// ex.: 0.25 — para fotografar os mecânicos pelo adb; 1 = normal).
    /// </summary>
    public static class AutoTest
    {
        static Dictionary<string, string> cfg;
        static bool launched;

        static string CfgPath => Path.Combine(Application.persistentDataPath, "autotest.txt");
        public static string LogPath => Path.Combine(Application.persistentDataPath, "autotest_log.txt");

        static Dictionary<string, string> Cfg
        {
            get
            {
                if (cfg != null) return cfg;
                cfg = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(CfgPath))
                        foreach (var raw in File.ReadAllLines(CfgPath))
                        {
                            var line = raw.Trim();
                            int eq = line.IndexOf('=');
                            if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;
                            cfg[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
                        }
                }
                catch (System.Exception ex) { Debug.LogWarning("[AUTOTEST] config ilegível: " + ex.Message); }
                return cfg;
            }
        }

        /// <summary>Teste ligado: configuração lida E o arquivo ainda existe. Apagar autotest.txt desliga o teste na hora,
        /// mesmo com o jogo aberto (antes a configuração ficava na memória e a corrida seguinte — até a da carreira —
        /// saía no piloto automático).</summary>
        public static bool Active => Cfg.Count > 0 && File.Exists(CfgPath);

        static string Get(string k, string def) => Cfg.TryGetValue(k, out var v) && v.Length > 0 ? v : def;
        static int GetInt(string k, int def) => int.TryParse(Get(k, ""), out int v) ? v : def;
        internal static string GetCfg(string k, string def) => Get(k, def);
        internal static int GetCfgInt(string k, int def) => GetInt(k, def);

        /// <summary>Previsão forçada pelo teste (null = usar a normal).</summary>
        public static string Weather => Active ? Get("weather", "") : "";
        /// <summary>stylerecord=1: as voltas do piloto automático gravam o ESTILO (arquivo do teste) — exercita o caminho
        /// gravar → IA usar sem precisar de alguém jogando.</summary>
        public static bool StyleFromAutopilot => Active && GetInt("stylerecord", 0) == 1;
        /// <summary>f2spec=1: todos os fórmulas da corrida de teste com o carro único da F2 (CarSpec.ApplyF2Spec), como na carreira.</summary>
        public static bool F2Spec => Active && GetInt("f2spec", 0) == 1;

        /// <summary>Chamado pelo menu principal: lança a corrida de teste UMA vez por execução.</summary>
        public static bool TryLaunchFromMenu(MonoBehaviour host)
        {
            if (launched || !Active || host == null) return false;
            launched = true;
            if (GetInt("selftest", 0) == 1) { host.StartCoroutine(SelfTestRun()); return true; }
            if (Get("online", "") == "host")
            {
                var oh = new GameObject("AutoOnlineHost");
                Object.DontDestroyOnLoad(oh);
                oh.AddComponent<AutoOnlineHost>();
                return true;
            }
            if (CareerMode)
            {
                var go = new GameObject("AutoCareerDriver");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<AutoCareerDriver>().weekends = Mathf.Max(1, GetInt("weekends", 5));
                return true;
            }
            host.StartCoroutine(Launch());
            return true;
        }

        /// <summary>selftest=1: testes de LÓGICA da carreira na memória (o save do jogador não é tocado) e fim.</summary>
        static IEnumerator SelfTestRun()
        {
            yield return new WaitForSecondsRealtime(1f);
            try { File.WriteAllText(LogPath, "# selftest " + System.DateTime.Now.ToString("s") + "\n"); } catch (System.Exception) { }
            Log("SELFTEST inicio");
            RaceX.Career.CareerMode.SelfTest(l => Log("SELFTEST " + l));
            Log("SELFTEST FIM");
        }

        /// <summary>career=1: o autoteste joga a CARREIRA do jogador (o usuário autorizou; vai reiniciar o perfil depois).</summary>
        public static bool CareerMode => Active && GetInt("career", 0) == 1;
        public static int CareerLaps(int laps) => Mathf.Min(laps, Mathf.Max(3, GetInt("racelaps", 5)));
        public static float CareerSeconds(float s) => Mathf.Min(s, Mathf.Max(40, GetInt("qualisec", 100)));
        /// <summary>1º fim de semana: perda total proposital no Q1 depois da 1ª volta (testa a regra da classificação).</summary>
        public static bool Q1CrashPending = true;

        /// <summary>Print da tela no aparelho (autotest_shot_NOME.png na pasta do jogo).</summary>
        public static void Shot(string name)
        {
            if (!Active) return;
            // No celular o Unity grava o arquivo RELATIVO à pasta do jogo (caminho absoluto não salva nada).
            string file = "at_" + name + ".png";
            try { ScreenCapture.CaptureScreenshot(Application.isMobilePlatform ? file : Path.Combine(Application.persistentDataPath, file)); Log("PRINT " + name); }
            catch (System.Exception ex) { Log("PRINT falhou " + name + ": " + ex.Message); }
        }

        static IEnumerator Launch()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            var s = new RaceSettings
            {
                mode = GameMode.SinglePlayer,
                track = (TrackId)Mathf.Clamp(GetInt("track", 0), 0, 23),
                laps = Mathf.Max(1, GetInt("laps", 3)),
                totalCars = Mathf.Clamp(GetInt("cars", 24), 1, TrackBuilder.GridSlots),
                difficulty = (Difficulty)Mathf.Clamp(GetInt("difficulty", 1), 0, 3),
                formulaOnly = true,
                pitStops = true,
            };
            // practice=1: sessão POR TEMPO (treino) — carros saem do box, voltas lentas/rápidas, garagem.
            if (GetInt("practice", 0) == 1) { s.laps = 999; s.sessionTimeLimit = Mathf.Max(2, GetInt("minutes", 5)) * 60f; }
            CarModel model = CarModel.F1;
            try { model = (CarModel)System.Enum.Parse(typeof(CarModel), Get("car", "F1"), true); } catch (System.Exception) { }
            s.players = new[] { new PlayerSetup { name = "AUTOTESTE", car = model, startCamera = CameraView.ThirdPerson } };
            // split=1: TELA DIVIDIDA (confere o HUD do multiplayer local por print; o 2º carro fica parado no grid).
            if (GetInt("split", 0) == 1)
            {
                s.mode = GameMode.LocalSplitScreen;
                s.players = new[]
                {
                    new PlayerSetup { name = "AUTOTESTE", car = model, startCamera = CameraView.ThirdPerson },
                    new PlayerSetup { name = "JOGADOR 2", car = model, colorIndex = 5, startCamera = CameraView.ThirdPerson }
                };
            }
            try { File.WriteAllText(LogPath, "# autotest " + System.DateTime.Now.ToString("s") + " track=" + s.track + " laps=" + s.laps + " cars=" + s.totalCars + " weather=" + Weather + "\n"); }
            catch (System.Exception) { }
            Debug.Log("[AUTOTEST] lançando corrida " + s.track + " voltas=" + s.laps + " carros=" + s.totalCars);
            SceneLoader.LoadRace(s);
        }

        /// <summary>Arquivo de COMANDO durante o teste online (o agente grava pelo adb): restart | rematch | start | leave.</summary>
        public static string CmdPath => Path.Combine(Application.persistentDataPath, "autotest_cmd.txt");

        /// <summary>Chamado pelo RaceBootstrap depois de montar a corrida.</summary>
        public static void AttachRunner(RaceManager race)
        {
            if (!Active || race == null) return;
            var go = new GameObject("AutoTestRunner");
            var r = go.AddComponent<AutoTestRunner>();
            r.race = race;
            r.minutes = Mathf.Max(1, GetInt("minutes", 8));
            r.autopilot = GetInt("autopilot", 1) == 1;
            r.crashTest = GetInt("crashtest", 0) >= 1;
            r.crashOnTrack = GetInt("crashtest", 0) == 2;
            r.incidentTest = GetInt("incidenttest", 0) == 1;
            r.pitLap = GetInt("pitlap", 0);
            r.pitSlowmo = float.TryParse(Get("pitslowmo", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sm) ? Mathf.Clamp(sm, 0.05f, 1f) : 1f;
            r.celebTest = GetInt("celebtest", 0) == 1;
            r.shots = GetInt("shots", 1) == 1;
            r.garageTest = GetInt("garagetest", 0) == 1;
        }

        public static int GetIntPublic(string k, int def) => GetInt(k, def);

        public static void Log(string line)
        {
            Debug.Log("[AUTOTEST] " + line);
            try { File.AppendAllText(LogPath, line + "\n"); } catch (System.Exception) { }
        }
    }

    /// <summary>
    /// AUTOTESTE DA CARREIRA: joga os fins de semana sozinho (treinos pulados, classificação e corrida encurtadas, piloto
    /// automático), tira prints dos resultados, da comemoração, do hub, da estratégia, dos e-mails, da imprensa e da ficha do
    /// piloto, e registra o estado da carreira (pontos, grid, superlicença, energia, lesão, notícias) a cada rodada.
    /// </summary>
    public class AutoCareerDriver : MonoBehaviour
    {
        public int weekends = 5;
        int races, absents;

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            try { File.WriteAllText(AutoTest.LogPath, "# carreira autoteste " + System.DateTime.Now.ToString("s") + "\n"); } catch (System.Exception) { }
            if (RaceX.Career.CareerMode.data == null) RaceX.Career.CareerMode.Load();
            var d0 = RaceX.Career.CareerMode.data;
            if (d0 == null) { AutoTest.Log("CARREIRA sem save — FIM"); AutoTest.Log("CARREIRA FIM"); yield break; }
            AutoTest.Log("CARREIRA inicio: " + d0.pilotName + " " + d0.category + " equipe=" + d0.f2Team + " rodada=" + (d0.round + 1) + " fase=" + d0.weekendPhase);
            int guard = 0;
            while (races < weekends && guard++ < 200)
            {
                var d = RaceX.Career.CareerMode.data;
                RaceX.Career.CareerData.raceActive = false;
                Time.timeScale = 1f;
                // Fim de temporada no MUNDO VIVO: assina a 1ª proposta como a tela do jogo faz (SignWorldOffer vira a
                // temporada). Antes usava SignWithTeam, a temporada nunca virava e as rodadas passavam de 24.
                if (d.awaitingTeamChoice && RaceX.Career.CareerWorld.Active && RaceX.Career.CareerWorld.W.offers.Exists(o => !o.midseason))
                {
                    int oi = RaceX.Career.CareerWorld.W.offers.FindIndex(o => !o.midseason);
                    AutoTest.Log("CARREIRA fim de temporada: assina proposta " + RaceX.Career.CareerWorld.W.offers[oi].teamName);
                    RaceX.Career.CareerMode.SignWorldOffer(oi);
                    RaceX.Career.CareerMode.ConsumeSeasonEnd();
                    continue;
                }
                if (RaceX.Career.CareerMode.JustFinishedSeason) RaceX.Career.CareerMode.ConsumeSeasonEnd();
                if (d.awaitingTeamChoice && d.teamOffers != null && d.teamOffers.Length > 0)
                {
                    AutoTest.Log("CARREIRA assina com " + d.teamOffers[0]);
                    RaceX.Career.CareerMode.SignWithTeam(d.teamOffers[0]);
                    continue;
                }
                var W = RaceX.Career.CareerWorld.Active ? RaceX.Career.CareerWorld.W : null;
                if (d.weekendPhase == RaceX.Career.CareerMode.PH_FP && W != null && (W.injuryRounds > 0 || W.banRounds > 0))
                {
                    absents++;
                    AutoTest.Log("CARREIRA rodada " + (d.round + 1) + " SEM o jogador: " + (W.injuryRounds > 0 ? "lesão (" + W.injuryText + ")" : "suspensão") + " — reserva corre");
                    RaceX.Career.CareerMode.SkipAbsentWeekend();
                    continue;
                }
                if (d.weekendPhase == RaceX.Career.CareerMode.PH_FP || d.weekendPhase == RaceX.Career.CareerMode.PH_FP3)
                {
                    AutoTest.Log("CARREIRA pula treino fase=" + d.weekendPhase + " rodada=" + (d.round + 1));
                    RaceX.Career.CareerMode.SkipSession();
                    continue;
                }
                int phase = d.weekendPhase, round = d.round;
                bool raceLike = phase == RaceX.Career.CareerMode.PH_RACE || phase == RaceX.Career.CareerMode.PH_SPRINT;
                if (raceLike)
                {
                    // plano da equipe antes da largada
                    RaceX.UI.CareerFlow.AutoTestShow("estrategia");
                    yield return new WaitForSecondsRealtime(1.2f);
                    AutoTest.Shot("r" + (round + 1) + "_estrategia_plano_da_equipe");
                    yield return new WaitForSecondsRealtime(0.8f);
                    AutoTest.Log("CARREIRA estrategia r" + (round + 1) + ": " + d.stratTeamNote + " | ritmo=" + d.stratPace + " paradas=" + d.stratStops + " largada=" + d.stratStart + " grid=P" + (phase == RaceX.Career.CareerMode.PH_RACE ? d.gpGrid : d.sqGrid));
                    RaceX.UI.CareerFlow.ForceCloseSilent();
                }
                AutoTest.Log("CARREIRA lança r" + (round + 1) + " fase=" + phase + " (" + RaceX.Career.CareerMode.SessionTitle(phase) + ")");
                RaceX.Career.CareerMode.LaunchNextRace();
                // espera a sessão começar e terminar
                float t = 0f;
                RaceManager rm = null;
                while (t < 300f) { rm = FindFirstObjectByType<RaceManager>(); if (rm != null && rm.State == RaceState.Racing) break; t += 1f; yield return new WaitForSecondsRealtime(1f); }
                if (rm == null) { AutoTest.Log("CARREIRA sessão não abriu — FIM"); break; }
                t = 0f;
                while (t < 2400f && rm != null && rm.State != RaceState.Finished) { t += 2f; yield return new WaitForSecondsRealtime(2f); }
                yield return new WaitForSecondsRealtime(raceLike ? 28f : 3f);   // comemoração + tabela
                AutoTest.Shot("r" + (round + 1) + "_resultado_fase" + phase);
                yield return new WaitForSecondsRealtime(1f);
                var dd = RaceX.Career.CareerMode.data;
                AutoTest.Log("CARREIRA fim da sessão r" + (round + 1) + " fase " + phase + " → agora fase=" + dd.weekendPhase + " rodada=" + (dd.round + 1) + " gpGrid=P" + dd.gpGrid);
                if (phase == RaceX.Career.CareerMode.PH_RACE)
                {
                    races++;
                    Time.timeScale = 0f;
                    RaceX.UI.CareerFlow.AutoTestShow("hub");
                    yield return new WaitForSecondsRealtime(1.2f); AutoTest.Shot("r" + (round + 1) + "_hub");
                    for (int tab = 0; tab <= 2; tab++)
                    {
                        RaceX.UI.CareerFlow.AutoTestShow("mundo:" + tab);
                        yield return new WaitForSecondsRealtime(1.2f);
                        AutoTest.Shot("r" + (round + 1) + "_mundo_aba" + tab + (tab == 0 ? "_voce" : tab == 1 ? "_emails" : "_imprensa"));
                    }
                    LogWorld(round);
                    RaceX.UI.CareerFlow.ForceCloseSilent();
                    Time.timeScale = 1f;
                }
            }
            AutoTest.Log("CARREIRA resumo: corridas=" + races + " rodadas sem o jogador=" + absents);
            AutoTest.Log("CARREIRA FIM");
        }

        void LogWorld(int round)
        {
            var d = RaceX.Career.CareerMode.data;
            int me = d.standings.FindIndex(s => s.isPlayer) + 1;
            AutoTest.Log("MUNDO r" + (round + 1) + ": campeonato P" + me + " pontos=" + d.pointsPlayer);
            if (!RaceX.Career.CareerWorld.Active) return;
            var W = RaceX.Career.CareerWorld.W;
            AutoTest.Log("MUNDO superlicenca=" + W.licencePoints.ToString("0.#") + " suspenso=" + W.banRounds + " energia=" + Mathf.RoundToInt(W.energy * 100f) + "% lesao=" + W.injuryRounds + " (" + W.injuryText + ") seguidores=" + Mathf.RoundToInt(W.following));
            int n = W.news.Count;
            for (int i = Mathf.Max(0, n - 6); i < n; i++)
                AutoTest.Log("EMAIL [" + W.news[i].cat + "] " + W.news[i].headline + " :: " + W.news[i].body);
            if (W.hasPress && W.pendingPress != null) AutoTest.Log("IMPRENSA pergunta pendente");
        }
    }

    public class AutoTestRunner : MonoBehaviour
    {
        public RaceManager race;
        public float minutes = 8f;
        public bool autopilot = true;
        /// <summary>crashtest=1: aos 40 s aplica impactos sintéticos em 3 NPCs (bico 75 km/h, roda 75 km/h,
        /// muro 300 km/h) pelo mesmo caminho da colisão — valida destroço, bandeiras, box e perda total.</summary>
        public bool crashTest;
        /// <summary>crashtest=2: além disso, deixa os dois carros que abandonam EM CIMA da pista à frente do jogador (testa o contorno).</summary>
        public bool crashOnTrack;
        /// <summary>incidenttest=1 (v107d): aos 45 s anota um incidente NPC × autoteste (autoteste atingido) e aos 75 s autoteste × NPC
        /// (autoteste culpado) — testa o "INCIDENTE ANOTADO — em análise" e a DECISÃO para os dois lados sem depender de batida real.</summary>
        public bool incidentTest;
        int incidentStep;
        public int pitLap;
        public float pitSlowmo = 1f;
        public bool celebTest, shots, garageTest;
        // --- verificações v82 ---
        int scPtUnder, scContacts, scOvertakePen, maxStack, npcBrakeStart;
        bool scWas, pipWas, scHeightDone, garageDone, finishSeen;
        float scStartT = -1f, finishT = -1f, startT = -1f, minPitGrip = 1f, maxUpV, exitWatchT = -1f, exitStillT;
        readonly Dictionary<CarController, float> brakePrevV = new Dictionary<CarController, float>(), brakePrevT = new Dictionary<CarController, float>();
        readonly List<float> brakeSamples = new List<float>();
        // v100: g lateral REAL por faixa de 25 km/h (v × guinada, 4 rodas no chão, sem contato) — compara com a tabela do relatório.
        readonly List<float>[] latBands = new List<float>[14];
        int capLatSamples, capBrakeSamples, capTotalSamples;
        int scStartLap, exitStops, escape0, passed0, sessionFastShots;
        float sessionFastest;
        readonly List<KeyValuePair<float, string>> pendingShots = new List<KeyValuePair<float, string>>();
        void ShotIn(float delay, string name) { if (shots) pendingShots.Add(new KeyValuePair<float, string>(Time.unscaledTime + delay, name)); }
        bool pitAsked, wasStopped;
        bool crashDone;
        int partsOff, totaled, penalties, repairs;
        readonly Dictionary<CarController, bool> neededPit = new Dictionary<CarController, bool>();
        // v89: carro ATIVO parado na pista (fora: abandonos/destroços) — quanto tempo, o máximo simultâneo e quantos passaram de 6 s.
        readonly Dictionary<CarController, float> stoppedFor = new Dictionary<CarController, float>();
        int maxStoppedActive, longStops;
        readonly HashSet<CarController> wreckLogged = new HashSet<CarController>();

        RaceEntry me;
        CarAI pilot;
        float t0 = -1f, nextLog;
        int hint = -1;
        int collisions, playerOffEvents;
        bool playerWasOff;
        float maxSpeed;
        readonly Dictionary<CarController, System.Action<float, Vector3>> handlers = new Dictionary<CarController, System.Action<float, Vector3>>();

        IEnumerator Start()
        {
            while (race != null && race.State != RaceState.Racing) yield return null;
            if (race == null) yield break;
            foreach (var e in race.entries)
            {
                if (e.isHuman && me == null) me = e;
                if (e.car == null) continue;
                var car = e.car;
                System.Action<float, Vector3> h = (rel, p) => { if (rel > 3f) { collisions++; if (SafetyCar.Deployed && !SafetyCar.Is(car)) scContacts++; } };
                car.Collided += h;
                handlers[car] = h;
            }
            race.LapCompleted += OnLap;
            CarDamage.PartDetached += OnPartOff;
            CarDamage.CarTotaled += OnTotal;
            RaceControl.Notice += OnNotice;
            if (me != null && autopilot)
            {
                var input = me.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                if (input != null) input.enabled = false;
                pilot = me.car.GetComponent<CarAI>();
                if (pilot == null) pilot = me.car.gameObject.AddComponent<CarAI>();
                // Carro do JOGADOR em ritmo normal (máximo da IA): a dificuldade da corrida vale só para os
                // adversários — com Médio o piloto automático andava devagar demais.
                pilot.Initialize(race.Track.Waypoints, race, Difficulty.Expert);
            }
            t0 = Time.time;
            AutoTest.Log("inicio pista=" + race.Settings.track + " carros=" + race.entries.Count + " autopiloto=" + (pilot != null) + " skid_shader=" + SkidMarks.ShaderName
                + " clima=" + (WeatherSystem.Active ? WeatherSystem.StatusText() : "sem"));
            // v99 (pedido do usuário: "verifique se todos estão com carros iguais"): física de cada carro, agrupada.
            var specs = new Dictionary<string, List<string>>();
            foreach (var e in race.entries)
            {
                var sp = e.car != null ? e.car.spec : null;
                if (sp == null) continue;
                string key = "aderencia=" + sp.sidewaysStiffness.ToString("0.00") + " freio=" + sp.forwardStiffness.ToString("0.00")
                             + " apoio=" + sp.downforceCoefficient.ToString("0.00") + " massa=" + sp.massKg.ToString("0")
                             + " torque=" + sp.maxTorqueNm.ToString("0") + " vmax=" + sp.topSpeedKmh.ToString("0");
                if (!specs.TryGetValue(key, out var names)) specs[key] = names = new List<string>();
                names.Add(e.name);
            }
            foreach (var kv in specs) AutoTest.Log("CARROS " + kv.Value.Count + "x " + kv.Key + " (" + string.Join(", ", kv.Value) + ")");
        }

        void V82Checks(CarController car)
        {
            float now = Time.time;
            if (startT < 0f) { startT = now; escape0 = PitLaneSystem.EscapeResets; passed0 = PitLaneSystem.PassedSnaps; ShotIn(25f, "hud_minimapa_silhueta"); }
            // [21] largada: nos 3 primeiros segundos, NPC freando parado/devagar
            if (now - startT < 3f)
                foreach (var e in race.entries)
                    if (e != me && e.car != null && e.car.brakeInput > 0.3f && e.car.SpeedKmh < 20f) { npcBrakeStart++; break; }
            // [20] física: carro em cima de carro e subida
            if (Time.frameCount % 10 == 0)
            {
                int stack = 0;
                for (int i = 0; i < race.entries.Count; i++)
                {
                    var a = race.entries[i].car; if (a == null) continue;
                    var rb = a.Body; if (rb != null && !rb.isKinematic && rb.linearVelocity.y > maxUpV) maxUpV = rb.linearVelocity.y;
                    for (int j = 0; j < race.entries.Count; j++)
                    {
                        var b = race.entries[j].car; if (b == null || b == a) continue;
                        Vector3 d = a.transform.position - b.transform.position;
                        if (d.y > 0.7f && new Vector2(d.x, d.z).magnitude < 2.2f) { stack++; break; }
                    }
                }
                if (stack > maxStack) { maxStack = stack; if (stack > 0) { AutoTest.Log("FISICA carro em cima de outro: " + stack); ShotIn(0.1f, "fisica_empilhado"); } }
                // v99: FREADA REAL que o carro entrega (pedal ≥ 90%, acima de 100 km/h, em qualquer carro) — calibra o plano da IA.
                for (int i = 0; i < race.entries.Count; i++)
                {
                    var c = race.entries[i].car; if (c == null) continue;
                    float v = c.ForwardSpeed;
                    if (brakePrevT.TryGetValue(c, out float pt) && brakePrevV.TryGetValue(c, out float pv) && c.brakeInput >= 0.9f && pv > 28f
                        && now - pt > 0.05f && now - pt < 0.4f && !c.pitControlled && c.GroundedWheels == 4)
                    {
                        float dec = (pv - v) / (now - pt);
                        if (dec > 0f && dec < 80f && brakeSamples.Count < 20000) brakeSamples.Add(dec);
                    }
                    brakePrevV[c] = v; brakePrevT[c] = now;
                    if (c.GroundedWheels == 4 && !c.pitControlled && c.Body != null && v > 14f)
                    {
                        float aLat = Mathf.Abs(v * c.Body.angularVelocity.y) / 9.81f;
                        int band = Mathf.Clamp((int)(v * 3.6f / 25f), 0, latBands.Length - 1);
                        if (latBands[band] == null) latBands[band] = new List<float>();
                        if (aLat < 9f && latBands[band].Count < 20000) latBands[band].Add(aLat);
                        capTotalSamples++;
                        if (c.LateralCapActive) capLatSamples++;
                        if (c.BrakeCapActive) capBrakeSamples++;
                    }
                }
            }
            // [15][16][17] safety car
            bool scNow = SafetyCar.Deployed;
            if (scNow && !scWas) { scStartT = now; scStartLap = race.GetProgress(me).lap; AutoTest.Log("SC acionado na volta " + scStartLap); }
            if (!scNow && scWas) AutoTest.Log("SC recolheu: durou " + (now - scStartT).ToString("0") + " s, " + (race.GetProgress(me).lap - scStartLap) + " volta(s); toquesNaFila=" + scContacts + " PTnaFila=" + scPtUnder);
            scWas = scNow;
            bool pip = SafetyCar.PipActive;
            if (pip && !pipWas) ShotIn(1.0f, "sc_janela_tv");
            pipWas = pip;
            if (!scHeightDone && SafetyCar.OnTrack && SafetyCar.Car != null)
            {
                scHeightDone = true;
                var sc = SafetyCar.Car.transform;
                var bb = new Bounds(sc.position, Vector3.zero); bool any = false;
                foreach (var r in sc.GetComponentsInChildren<Renderer>()) { if (r is ParticleSystemRenderer) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                var sp = race.Track.Waypoints.Spline;
                int ci = sp.ClosestIndex(sc.position);
                float gap = bb.min.y - sp.points[ci].y;
                AutoTest.Log("SC altura: ponto mais baixo do carro " + gap.ToString("0.00") + " m acima do asfalto (negativo = afundado)");
                ShotIn(4f, "sc_na_pista");
            }
            // [2] pneu não suja no box
            if (PitLaneSystem.IsInPitFlow(car)) for (int i = 0; i < 4; i++) minPitGrip = Mathf.Min(minPitGrip, car.extraGrip[i]);
            // [3][4] parada: prints e composto montado
            if (PitLaneSystem.StopInfo(car, out float st, out float dur, out _))
            {
                if (st > 0.65f * Mathf.Min(dur, 3f) && st - Time.deltaTime <= 0.65f * Mathf.Min(dur, 3f))
                {
                    AutoTest.Log("PARADA pneu novo: equipe monta " + TyreModel.Letter(PitLaneSystem.FittingCompound(car)) + " (montado agora " + TyreModel.Letter(PitLaneSystem.CompoundOf(car)) + ")");
                    ShotIn(0.05f, "parada_pneu_novo");
                }
            }
            // [5] saída do box: parada no corredor
            if (!PitLaneSystem.StopInfo(car, out _, out _, out _) && exitWatchT > 0f)
            {
                if (now - exitWatchT < 15f && PitLaneSystem.IsInPitFlow(car))
                {
                    if (car.SpeedKmh < 2f) exitStillT += Time.deltaTime; else exitStillT = 0f;
                    // v99: 1,5 → 4 s — a liberação segura (faixa rápida/quem vem no corredor) leva ~3 s e não é travamento.
                    if (exitStillT > 4f) { exitStops++; exitStillT = -999f; AutoTest.Log("SAIDA DO BOX: carro parado no corredor"); ShotIn(0.1f, "saida_parado"); }
                }
                else exitWatchT = -1f;
            }
            // [28/35/36/37] fim: comemoração forçada (se preciso) e resultados
            if (race.State == RaceState.Finished && !finishSeen)
            {
                finishSeen = true; finishT = now;
                AutoTest.Log("CORRIDA terminou: jogador P" + me.position + " carrosNaClassificacao=" + race.FinalStandings().Count);
                // Comemoração só existe em CORRIDA e SPRINT (regra do usuário) — o teste não força em treino/classificação.
                int cph = race.Settings.careerPhase;
                bool raceSession = race.Settings.laps < 900 && (!RaceX.Career.CareerData.raceActive && !AutoTest.CareerMode
                    || cph == RaceX.Career.CareerMode.PH_RACE || cph == RaceX.Career.CareerMode.PH_SPRINT);
                if (celebTest && raceSession)
                    StartCoroutine(ForceCelebration());
            }
            // CARREIRA: perda total proposital no 1º Q1 (depois de uma volta marcada) — testa a regra nova da classificação.
            if (AutoTest.CareerMode && AutoTest.Q1CrashPending && race.Settings.careerPhase == RaceX.Career.CareerMode.PH_Q1
                && me.bestLap > 0f && car.damage != null && !PitLaneSystem.IsInPitFlow(car) && car.SpeedKmh > 60f)
            {
                AutoTest.Q1CrashPending = false;
                car.damage.TestImpact(new Vector3(0f, 0.2f, car.spec.bodySize.z * 0.46f), 60f, true);
                AutoTest.Log("Q1: PERDA TOTAL proposital do jogador (bico a 60 km/h) com melhor volta " + me.bestLap.ToString("0.000") + " posicao=" + me.position);
                ShotIn(1f, "q1_perda_total");
            }
            // [1] treino: conserto ao continuar a sessão
            if (garageTest && !garageDone && now - startT > 45f && car.damage != null && !PitLaneSystem.IsInPitFlow(car))
            {
                garageDone = true;
                car.damage.TestImpact(new Vector3(0f, 0.2f, car.spec.bodySize.z * 0.46f), 45f, true);
                float before = car.damage.MaxLevel;
                PitLaneSystem.GarageRepairPlayer();
                AutoTest.Log("GARAGEM: dano antes=" + (before * 100f).ToString("0") + "% depois do CONTINUAR=" + (car.damage.MaxLevel * 100f).ToString("0") + "%");
            }
            for (int i = pendingShots.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= pendingShots[i].Key) { AutoTest.Shot(pendingShots[i].Value); pendingShots.RemoveAt(i); }
        }

        IEnumerator ForceCelebration()
        {
            yield return new WaitForSecondsRealtime(1f);
            var st = race.FinalStandings();
            bool running = Object.FindFirstObjectByType<RaceX.UI.VictoryCinematic>() != null;
            running = Object.FindFirstObjectByType<RaceX.UI.VictoryCinematic>() != null;
            if (!running && st.Count >= 3)
            {
                var hud = Object.FindFirstObjectByType<RaceX.UI.HUD>();
                if (hud != null) hud.PlayCelebrationNow(st.GetRange(0, 3), 1);
                AutoTest.Log("COMEMORACAO forcada pelo teste (jogador P" + me.position + ")");
            }
            else AutoTest.Log("COMEMORACAO do proprio jogo: " + running);
            yield return new WaitForSecondsRealtime(1.5f); AutoTest.Shot("comemoracao_1_ao_lado_do_carro");
            yield return new WaitForSecondsRealtime(5.5f); AutoTest.Shot("comemoracao_2_carro");
            yield return new WaitForSecondsRealtime(7f); AutoTest.Shot("comemoracao_3_podio");
            yield return new WaitForSecondsRealtime(9f); AutoTest.Shot("resultados");
            AutoTest.Log("timeScale nos resultados=" + Time.timeScale.ToString("0.0"));
        }

        void OnDestroy()
        {
            if (race != null) race.LapCompleted -= OnLap;
            CarDamage.PartDetached -= OnPartOff;
            CarDamage.CarTotaled -= OnTotal;
            RaceControl.Notice -= OnNotice;
            foreach (var kv in handlers) if (kv.Key != null) kv.Key.Collided -= kv.Value;
        }

        void OnPartOff(CarController c, int zone, Vector3 at) { partsOff++; AutoTest.Log("DANO " + c.name + " perdeu " + CarDamage.ZoneNames[zone]); }
        void OnTotal(CarController c)
        {
            totaled++;
            bool underSc = SafetyCar.Deployed && Time.time - scStartT > 8f;
            if (underSc) scPtUnder++;
            AutoTest.Log("DANO PERDA TOTAL " + c.name + (underSc ? " [SOB SAFETY CAR]" : ""));
        }
        void OnNotice(RaceEntry e, string msg)
        {
            if (msg.StartsWith(RaceControl.PenaltyPrefix)) penalties++;
            if (msg.Contains("ultrapassou sob SAFETY CAR")) scOvertakePen++;
            if (msg.StartsWith("VSC") && e != me) return; // um aviso de VSC por carro polui o log
            AutoTest.Log("DIRECAO " + e.name + ": " + msg);
        }

        void CrashTest()
        {
            crashDone = true;
            var picks = new List<RaceEntry>();
            foreach (var e in race.entries)
                if (e != me && e.car != null && e.car.damage != null && !PitLaneSystem.IsInPitFlow(e.car) && !e.retired) picks.Add(e);
            if (picks.Count < 3) { AutoTest.Log("crashtest: carros insuficientes"); return; }
            var a = picks[0].car; var b = picks[1].car; var c = picks[2].car;
            if (crashOnTrack && me != null && me.car != null)
            {
                // crashtest=2: os dois carros que vão abandonar ficam EM CIMA da pista, 200 m à frente do jogador — um atravessado (60°)
                // no meio-esquerdo e outro alinhado no meio-direito 8 m depois: pior caso para a fila contornar (vão só na borda).
                // Movidos ANTES das batidas: a perda total (e o SC) acontecem ali.
                var wpt = race.Track.Waypoints;
                int hh = -1;
                float aPl = wpt.DistanceAlong(me.car.transform.position, ref hh);
                float len = wpt.Spline.Length;
                var p1 = wpt.Spline.PoseAtDistance(Mathf.Repeat(aPl + 200f, len), -2f);
                c.ResetTo(p1.position + Vector3.up * 0.3f, p1.rotation * Quaternion.Euler(0f, 60f, 0f));
                var p2 = wpt.Spline.PoseAtDistance(Mathf.Repeat(aPl + 208f, len), 3f);
                b.ResetTo(p2.position + Vector3.up * 0.3f, p2.rotation);
                AutoTest.Log("crashtest=2: destrocos NA PISTA 200 m a frente do jogador (along " + Mathf.Repeat(aPl + 200f, len).ToString("0") + "; lat -2 atravessado, lat +3 alinhado)");
            }
            float la = a.spec.bodySize.z * 0.46f;
            // Limites por energia (PESQUISA_PERDA_TOTAL.md): bico 45 km/h perpendiculares = 15,9 g → bico perdido (troca na
            // parada); roda 20 km/h no muro = 3,1 g → suspensão quebrada (abandono); 300 km/h → perda total.
            a.damage.TestImpact(new Vector3(0f, 0.2f, la), 45f, true);                        // bico
            var wl = b.transform.InverseTransformPoint(b.wheels[1].transform.position);
            b.damage.TestImpact(wl, 20f, true);                                                // roda dianteira direita
            c.damage.TestImpact(new Vector3(0f, 0.3f, c.spec.bodySize.z * 0.46f), 300f, true); // muro a 300 km/h
            AutoTest.Log("crashtest: bico=" + picks[0].name + " roda=" + picks[1].name + " PT=" + picks[2].name);
            // Carro do JOGADOR também: lateral esquerda 50 km/h (~20 g, laranja), roda traseira direita 55 km/h
            // (~24 g, laranja) e bico 75 km/h (~44 g, solta) — silhueta no HUD, bandeira preta e laranja, box e conserto.
            var pc = me.car;
            if (pc != null && pc.damage != null)
            {
                // Jogador: lateral esquerda 25 km/h (4,9 g — dano laranja), roda traseira direita 8 km/h (0,5 g — suspensão
                // torta) e FURO RÁPIDO na traseira direita (pneu murcha, se desfaz e solta faísca no aro: efeito visual).
                float half = pc.spec.bodySize.x * 0.5f;
                pc.damage.TestImpact(new Vector3(-half, 0.3f, 0f), 25f, true);
                pc.damage.TestImpact(pc.transform.InverseTransformPoint(pc.wheels[3].transform.position), 8f, true);
                PitLaneSystem.DebugPuncture(pc, 3, 1f / 8f);
                AutoTest.Log("crashtest: jogador lateral esq. 25 km/h, roda tras. dir. 8 km/h + furo rapido tras. dir.");
            }
        }

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (lapTime > 10f && (sessionFastest <= 0f || lapTime < sessionFastest - 0.0005f))
            {
                sessionFastest = lapTime;
                AutoTest.Log("VOLTA MAIS RAPIDA da sessao: " + e.name + " " + lapTime.ToString("0.000"));
                if (sessionFastShots < 2) { sessionFastShots++; ShotIn(0.8f, "volta_mais_rapida_" + sessionFastShots); }
            }
            if (e == me) AutoTest.Log("VOLTA jogador n=" + e.lapTimes.Count + " tempo=" + lapTime.ToString("0.000") + " pneu=" + TyreModel.Letter(PitLaneSystem.CompoundOf(e.car)) + " desgaste=" + (PitLaneSystem.Wear(e.car) * 100f).ToString("0") + "%");
            else if (e.lapTimes != null && e.lapTimes.Count <= 2) AutoTest.Log("volta npc " + e.name + " n=" + e.lapTimes.Count + " tempo=" + lapTime.ToString("0.000"));
        }

        /// <summary>Freios (maior disco), motor (água/desgaste) e combustível do carro testado.</summary>
        static string SysTelemetry(CarController car)
        {
            var sys = CarSystems.Of(car);
            if (sys == null) return "";
            float bt = 0f; for (int i = 0; i < 4; i++) bt = Mathf.Max(bt, sys.brakeTempC[i]);
            return " freio=" + bt.ToString("0") + "C motor=" + sys.engineTempC.ToString("0") + "C motorDesg=" + (sys.engineWear * 100f).ToString("0.0")
                + "% comb=" + sys.fuelKg.ToString("0.0") + "kg eco=" + sys.fuelSaveLevel + " potencia=" + (car.powerFactor * 100f).ToString("0");
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (t0 < 0f || race == null || me == null || me.car == null) return;
            var car = me.car;
            if (car.SpeedKmh > maxSpeed) maxSpeed = car.SpeedKmh;
            // Parada do jogador: registra início/fim e, se pedido, câmera lenta só durante a parada.
            bool stoppedNow = PitLaneSystem.StopInfo(car, out float stT, out float stDur, out bool stNose);
            if (stoppedNow && !wasStopped)
            {
                AutoTest.Log("PARADA inicio volta=" + race.GetProgress(me).lap + " duracao=" + stDur.ToString("0.00") + "s bico=" + (stNose ? 1 : 0));
                if (pitSlowmo < 1f) Time.timeScale = pitSlowmo;
            }
            if (stoppedNow && !wasStopped) ShotIn(0.3f, "parada_inicio_mecanicos");
            if (!stoppedNow && wasStopped)
            {
                exitWatchT = Time.time; exitStillT = 0f;
                AutoTest.Log("PARADA fim (liberado) pneu=" + TyreModel.Letter(PitLaneSystem.CompoundOf(car)));
                ShotIn(1.5f, "saida_box");
                if (pitSlowmo < 1f) Time.timeScale = 1f;
            }
            wasStopped = stoppedNow;
            // Box pedido como faria a equipe: na APROXIMAÇÃO da entrada (300–700 m antes), na volta em que o carro vai chegar
            // nela. Antes pedia em cima da linha de chegada — no Parque Real a entrada fica ~40 m depois dela (pedido tarde).
            float dEntryAt = PitLaneSystem.DistanceToPitEntry(car);
            if (pitLap > 0 && !pitAsked && race.Settings.laps < 900 && PitLaneSystem.LapAtNextEntry(car) >= pitLap
                && dEntryAt > 300f && dEntryAt < 700f && !PitLaneSystem.IsPitActiveOrRequested(car))
            {
                pitAsked = true;
                PitLaneSystem.RequestPit(car);
                AutoTest.Log("PIT pedido na volta " + race.GetProgress(me).lap + " (entrada a " + dEntryAt.ToString("0") + " m, " + car.SpeedKmh.ToString("0") + " km/h)");
                ShotIn(dEntryAt / Mathf.Max(20f, car.SpeedKmh / 3.6f) * 0.8f, "faixa_azul_1"); ShotIn(dEntryAt / Mathf.Max(20f, car.SpeedKmh / 3.6f) + 2f, "faixa_azul_2");
            }
            V82Checks(car);
            bool off = car.IsOffTrack && !PitLaneSystem.IsInPitFlow(car); // corredor do box não é saída de pista
            if (off && !playerWasOff) playerOffEvents++;
            playerWasOff = off;
            if (Time.time < nextLog) return;
            nextLog = Time.time + 2f;

            var wp = race.Track.Waypoints;
            float along = wp.DistanceAlong(car.transform.position, ref hint);
            var pose = wp.Spline.PoseAtDistance(along);
            float lat = Vector3.Dot(car.transform.position - pose.position, pose.rotation * Vector3.right);
            int npcOff = 0, npcStopped = 0, npcPit = 0;
            foreach (var e in race.entries)
            {
                if (e == me || e.car == null) continue;
                if (PitLaneSystem.IsInPitFlow(e.car)) { npcPit++; stoppedFor[e.car] = 0f; continue; }
                if (e.car.IsOffTrack) npcOff++;
                // Parados = carros ATIVOS (abandonos e destroços antes inflavam a conta até o fim da corrida).
                bool active = !e.finished && !e.retired && !e.car.wrecked && e.car.gameObject.activeInHierarchy;
                if (active && e.car.SpeedKmh < 10f && race.State == RaceState.Racing && Time.time - t0 > 15f)
                {
                    npcStopped++;
                    stoppedFor.TryGetValue(e.car, out float st);
                    st += 2f;
                    stoppedFor[e.car] = st;
                    if (st >= 6f && st < 8f)
                    {
                        longStops++;
                        var ai = e.car.Ai;
                        AutoTest.Log("PARADO 6s: " + e.name + " bandeira=" + RaceControl.FlagOf(e.car) + " sc=" + SafetyCar.State + (ai != null ? " " + ai.Diag() : ""));
                    }
                }
                else stoppedFor[e.car] = 0f;
            }
            if (npcStopped > maxStoppedActive) maxStoppedActive = npcStopped;
            // DESTROÇO na pista: onde parou, a lateral e o quanto está atravessado (uma vez por carro) — para entender filas presas.
            foreach (var e in race.entries)
            {
                if (e.car == null || !e.retired || !e.car.gameObject.activeInHierarchy || e.car.SpeedKmh > 3f || wreckLogged.Contains(e.car)) continue;
                wreckLogged.Add(e.car);
                int wh = -1;
                float wa = wp.DistanceAlong(e.car.transform.position, ref wh);
                var wpPose = wp.Spline.PoseAtDistance(wa);
                float wl = Vector3.Dot(e.car.transform.position - wpPose.position, wpPose.rotation * Vector3.right);
                float ang = Vector3.Angle(Vector3.ProjectOnPlane(e.car.transform.forward, Vector3.up), wpPose.rotation * Vector3.forward);
                AutoTest.Log("DESTROCO " + e.name + " along=" + wa.ToString("0") + " lat=" + wl.ToString("0.0") + " angulo=" + ang.ToString("0") + "° meiaPista=" + (race.Track.Definition != null ? (race.Track.Definition.roadWidth * 0.5f).ToString("0.0") : "-"));
            }
            if (crashTest && !crashDone && Time.time - t0 > 40f && race.Settings.laps < 900) CrashTest();   // só em corrida
            if (incidentTest && incidentStep < 2 && race.Settings.laps < 900 && me != null && Time.time - t0 > (incidentStep == 0 ? 45f : 75f))
            {
                RaceEntry npc = null;
                foreach (var e in race.entries) if (e != me && e.car != null && !e.retired && !e.finished) { npc = e; if (incidentStep == 1) continue; break; }
                if (npc != null)
                {
                    if (incidentStep == 0) RaceControl.TestIncident(npc, me); else RaceControl.TestIncident(me, npc);
                    AutoTest.Log("incidenttest " + (incidentStep + 1) + ": " + (incidentStep == 0 ? npc.name + " x " + me.name : me.name + " x " + npc.name));
                }
                incidentStep++;
            }
            // Piloto automático obedece a bandeira preta e laranja como as IAs (o jogador de verdade usa o botão PIT).
            if (pilot != null && car.damage != null && car.damage.NeedsPit && !PitLaneSystem.IsPitActiveOrRequested(car)) PitLaneSystem.RequestPit(car);
            int dmgCars = 0, retired = 0;
            foreach (var e in race.entries)
            {
                if (e.retired) retired++;
                if (e.car == null || e.car.damage == null) continue;
                bool np = e.car.damage.NeedsPit;
                if (np) dmgCars++;
                if (neededPit.TryGetValue(e.car, out bool was) && was && !np && !e.retired) { repairs++; AutoTest.Log("BOX consertou " + e.name); }
                neededPit[e.car] = np;
            }
            var sb = new StringBuilder(260);
            sb.Append("t=").Append((Time.time - t0).ToString("0"))
              .Append(" volta=").Append(race.GetProgress(me).lap)
              .Append(" pos=").Append(me.position)
              .Append(" gapSC=").Append(SafetyCar.OnTrack ? SafetyCar.GapAhead(car).ToString("0") : "-")
              .Append(" v=").Append(car.SpeedKmh.ToString("0"))
              .Append(" vmax=").Append(maxSpeed.ToString("0"))
              .Append(" along=").Append(along.ToString("0"))
              .Append(" lat=").Append(lat.ToString("0.0"))
              .Append(" fora=").Append(off ? 1 : 0)
              .Append(" saidasJog=").Append(playerOffEvents)
              .Append(" npcFora=").Append(npcOff)
              .Append(" npcParados=").Append(npcStopped)
              .Append(" zig=").Append(CarAI.ZigPlayer).Append('/').Append(CarAI.ZigNpc)
              .Append(" npcBox=").Append(npcPit)
              .Append(" respawns=").Append(CarAI.RespawnCount)
              .Append(" incidentesIA=").Append(AiTrackMemory.Incidents)
              .Append(" batidas=").Append(collisions)
              .Append(" marcas=").Append(SkidMarks.Count).Append('/').Append(SkidMarks.Total)
              .Append(" agua=").Append((WeatherSystem.Wetness * 100f).ToString("0"))
              .Append(" pneu=").Append(TyreModel.Letter(PitLaneSystem.CompoundOf(car)))
              .Append(" desg=").Append((PitLaneSystem.Wear(car) * 100f).ToString("0"))
              .Append(" tempPneus=").Append(PitLaneSystem.WheelTempC(car, 0).ToString("0")).Append('/').Append(PitLaneSystem.WheelTempC(car, 1).ToString("0"))
              .Append('/').Append(PitLaneSystem.WheelTempC(car, 2).ToString("0")).Append('/').Append(PitLaneSystem.WheelTempC(car, 3).ToString("0"))
              .Append(" sc=").Append(SafetyCar.State)
              .Append(" empilhados=").Append(maxStack)
              .Append(" dano=").Append(car.DamageFactor.ToString("0.00"))
              .Append(" danoMax=").Append(car.damage != null ? (car.damage.MaxLevel * 100f).ToString("0") : "-")
              .Append(" bandeira=").Append(RaceControl.FlagOf(car))
              .Append(" pecasSoltas=").Append(partsOff)
              .Append(" PT=").Append(totaled)
              .Append(" abandonos=").Append(retired)
              .Append(" carrosDanif=").Append(dmgCars)
              .Append(" consertos=").Append(repairs)
              .Append(" penal=").Append(penalties)
              .Append(" penalJog=").Append(me.penaltySeconds.ToString("0"))
              .Append(" limitesJog=").Append(me.trackLimitStrikes)
              .Append(SysTelemetry(car))
              .Append(" fps=").Append((1f / Mathf.Max(0.001f, Time.smoothDeltaTime)).ToString("0"));
            AutoTest.Log(sb.ToString());

            // v106 (pergunta do usuário em 06/11): com keeprunning e o carro do autoteste FORA (perda total), a corrida não termina por ele
            // e os carros que já chegaram continuavam rodando até o limite de tempo — as voltas extras entravam nas contas. Agora o teste
            // acaba quando todos os outros chegaram ou abandonaram (corrida, não treino).
            bool othersDone = me != null && me.retired && race.Settings.laps < 900 && race.entries.TrueForAll(x => x == null || x == me || x.finished || x.retired);
            if (Time.time - t0 > minutes * 60f || race.State == RaceState.Finished || othersDone)
            {
                // v99: ritmo da IA (melhor volta de cada carro) e freada medida — base para comparar com o jogador.
                var bests = new List<KeyValuePair<string, float>>();
                foreach (var e in race.entries)
                    if (e != null && e != me && e.bestLap > 10f) bests.Add(new KeyValuePair<string, float>(e.name, e.bestLap));
                bests.Sort((a, b) => a.Value.CompareTo(b.Value));
                var bl = new StringBuilder("MELHORES IA (" + bests.Count + "):");
                foreach (var kv in bests) bl.Append(' ').Append(kv.Key.Replace(' ', '_')).Append('=').Append(kv.Value.ToString("0.000"));
                AutoTest.Log(bl.ToString());
                if (bests.Count > 0)
                    AutoTest.Log("RITMO IA melhor=" + bests[0].Value.ToString("0.000") + " mediana=" + bests[bests.Count / 2].Value.ToString("0.000")
                        + " pior=" + bests[bests.Count - 1].Value.ToString("0.000") + " jogador(autopiloto)=" + me.bestLap.ToString("0.000"));
                if (brakeSamples.Count > 0)
                {
                    brakeSamples.Sort();
                    AutoTest.Log("FREADA medida (pedal>=90%, >100 km/h): mediana=" + brakeSamples[brakeSamples.Count / 2].ToString("0.0")
                        + " p90=" + brakeSamples[(int)(brakeSamples.Count * 0.9f)].ToString("0.0") + " max=" + brakeSamples[brakeSamples.Count - 1].ToString("0.0")
                        + " m/s2 amostras=" + brakeSamples.Count);
                }
                var gl = new StringBuilder("G LATERAL p95/max por faixa (km/h):");
                for (int bnd = 0; bnd < latBands.Length; bnd++)
                {
                    var lb = latBands[bnd]; if (lb == null || lb.Count < 20) continue;
                    lb.Sort();
                    gl.Append(' ').Append(bnd * 25).Append('-').Append(bnd * 25 + 25).Append('=')
                      .Append(lb[(int)(lb.Count * 0.95f)].ToString("0.00")).Append('/').Append(lb[lb.Count - 1].ToString("0.00")).Append('g');
                }
                AutoTest.Log(gl.ToString());
                if (capTotalSamples > 0)
                    AutoTest.Log("TETO de g ativo: lateral " + (100f * capLatSamples / capTotalSamples).ToString("0.0") + "% freada "
                        + (100f * capBrakeSamples / capTotalSamples).ToString("0.0") + "% das amostras");
                AutoTest.Log("SAIDAS DE FRENTE corrigidas pela IA: " + CarAI.UndersteerEvents);
                // v89: zigue-zague na reta (reversões ≥ 0,5 m acima de 108 km/h, fora da fila do SC) por km de reta e por motivo.
                float kmP = CarAI.ZigDistPlayer / 1000f, kmN = CarAI.ZigDistNpc / 1000f;
                var zr = new StringBuilder();
                for (int i = 0; i < CarAI.ZigByReason.Length; i++) zr.Append(' ').Append(CarAI.LatReasonNames[i]).Append('=').Append(CarAI.ZigByReason[i]);
                AutoTest.Log("ZIGZAG jogador=" + CarAI.ZigPlayer + " em " + kmP.ToString("0.0") + " km de reta (" + (kmP > 0.01f ? CarAI.ZigPlayer / kmP : 0f).ToString("0.0")
                    + "/km) npc=" + CarAI.ZigNpc + " em " + kmN.ToString("0.0") + " km (" + (kmN > 0.01f ? CarAI.ZigNpc / kmN : 0f).ToString("0.0") + "/km) motivos:" + zr
                    + " | paradosAtivosMax=" + maxStoppedActive + " parados6s=" + longStops);
                AutoTest.Log("ULTRAPASSAGENS tentativas=" + CarAI.PassTries + " feitas=" + CarAI.PassesDone + " mantidasNaCurva=" + CarAI.PassesInCorner
                    + " | VOLANTE licoes de segurar=" + CarAI.HoldLessons + " soltouEPuxou=" + CarAI.RepullPasses + " (perto da borda " + CarAI.RepullNearOut + ")"
                    + " | RITMO curva +1%=" + CarAI.OverUps + " recuos=" + CarAI.OverDowns + " | PNEU FRIO licoes=" + CarAI.ColdLessons + " | ANGULO licoes=" + CarAI.AngleLessons + " ficou=" + CarAI.AngleKeeps + " voltou=" + CarAI.AngleBacks);
                AutoTest.Log("V82 resumo: largada_npcFreando=" + npcBrakeStart + " sc_toquesNaFila=" + scContacts + " sc_PTnaFila=" + scPtUnder
                    + " sc_penalUltrapassagem=" + scOvertakePen + " empilhadosMax=" + maxStack + " subidaMax=" + maxUpV.ToString("0.0") + "m/s"
                    + " gripMinNoBox=" + minPitGrip.ToString("0.00") + " saidaBox_recolocado=" + (PitLaneSystem.EscapeResets - escape0)
                    + " saidaBox_paradas=" + exitStops + " passouDaMarca=" + (PitLaneSystem.PassedSnaps - passed0)
                    + " respawnsLaterais=" + CarAI.LateralRespawns + " cedeuNoTreino=" + CarAI.PracticeYields);
                AutoTest.Log("FIM t=" + (Time.time - t0).ToString("0") + " voltasJog=" + me.lapTimes.Count + " melhor=" + me.bestLap.ToString("0.000") + " saidasJog=" + playerOffEvents
                    + " respawns=" + CarAI.RespawnCount + " incidentesIA=" + AiTrackMemory.Incidents + " batidas=" + collisions + " marcasTotal=" + SkidMarks.Total
                    + " pecasSoltas=" + partsOff + " PT=" + totaled + " consertos=" + repairs + " penalidades=" + penalties);
                AiTrackMemory.Flush();
                t0 = -1f;
                enabled = false;
            }
        }
    }

    /// <summary>online=host (teste ONLINE com pessoas de verdade): este aparelho CRIA a sala (código no log: "SALA ONLINE:"),
    /// espera os convidados marcarem PRONTO e inicia sozinho; corre no piloto automático; vota SIM nas votações dos outros;
    /// depois do resultado oficial pede JOGAR NOVAMENTE (testa se todos continuam NA MESMA sala).
    /// Comando durante o teste: autotest_cmd.txt com restart | rematch | start | leave.
    /// Chaves: onlinename, onlinetrack (id do servidor: parque, forest...), onlinelaps, onlinecars, onlinediff
    /// (easy/medium/hard/expert), onlinecat (gt/f1), onlinepit (0/1), onlineweather, onlinept (0/1), onlinepen (0/1),
    /// onlinemin (jogadores para iniciar, contando o anfitrião), car, onlinecolor, onlinerematch (quantas novas partidas pedir).</summary>
    public class AutoOnlineHost : MonoBehaviour
    {
        string myName = "CLAUDE";
        int minPlayers = 3;
        bool settingsSent, carSent, started, roomLogged;
        float nextPoll, finishedAt = -1f, voteSeenAt;
        string lastVoteId, lastLobby = "";
        int rematchAsked;

        void Start()
        {
            myName = AutoTest.GetCfg("onlinename", "CLAUDE");
            minPlayers = Mathf.Clamp(AutoTest.GetCfgInt("onlinemin", 3), 2, 8);
            try { File.WriteAllText(AutoTest.LogPath, "# online host " + System.DateTime.Now.ToString("s") + "\n"); } catch (System.Exception) { }
            OnlineClient.OnGameStart += OnGameStart;
            OnlineClient.OnFinished += OnOfficial;
            OnlineClient.OnVoteResult += (ok, kind, text) => AutoTest.Log("ONLINE votação " + kind + " → " + (ok ? "ACEITA" : "RECUSADA") + " (" + text + ")");
            OnlineClient.OnDropped += id => AutoTest.Log("ONLINE caiu da conexão: " + id);
            OnlineClient.OnBack += id => AutoTest.Log("ONLINE voltou: " + id);
            OnlineClient.OnHostChanged += () => AutoTest.Log("ONLINE anfitrião agora: " + (OnlineClient.IsHost ? "EU" : "outro"));
            OnlineClient.OnError += msg => AutoTest.Log("ONLINE erro: " + msg);
            OnlineClient.OnWarning += msg => AutoTest.Log("ONLINE aviso: " + msg);
            OnlineClient.OnKicked += msg => AutoTest.Log("ONLINE fora da sala: " + msg);
            // onlineserver=ws://IP:3000 → servidor de TESTE só nesta conexão (não grava por cima do servidor salvo do jogador).
            string url = AutoTest.GetCfg("onlineserver", "");
            if (url.Length == 0) url = OnlineClient.ServerUrl;
            OnlineClient.Connect(url);
            OnlineClient.CreateRoom(myName);
            AutoTest.Log("ONLINE conectando em " + url + " como " + myName + " (inicia com " + minPlayers + " jogadores prontos)");
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + 1f;
            if (!AutoTest.Active) return;
            PollCommand();
            if (string.IsNullOrEmpty(OnlineClient.RoomCode) || !OnlineClient.Connected) return;
            if (!roomLogged)
            {
                roomLogged = true;
                AutoTest.Log("SALA ONLINE: " + OnlineClient.RoomCode);
                Debug.Log("[AUTOTEST] SALA ONLINE: " + OnlineClient.RoomCode);
            }
            if (!settingsSent && OnlineClient.IsHost)
            {
                settingsSent = true;
                OnlineClient.SendRoomSettings(AutoTest.GetCfg("onlinetrack", "parque"), AutoTest.GetCfgInt("onlinelaps", 5), AutoTest.GetCfgInt("onlinecars", 12),
                    AutoTest.GetCfg("onlinediff", "medium"), AutoTest.GetCfg("onlinecat", "f1"), AutoTest.GetCfgInt("onlinepit", 1) == 1,
                    AutoTest.GetCfg("onlineweather", ""), AutoTest.GetCfgInt("onlinept", 1) == 1, AutoTest.GetCfgInt("onlinepen", 1) == 1);
                AutoTest.Log("ONLINE configurações da sala enviadas");
            }
            if (!carSent)
            {
                carSent = true;
                CarModel model = CarModel.F1;
                try { model = (CarModel)System.Enum.Parse(typeof(CarModel), AutoTest.GetCfg("car", "F1"), true); } catch (System.Exception) { }
                OnlineClient.UpdatePlayer((int)model, Mathf.Clamp(AutoTest.GetCfgInt("onlinecolor", 5), 0, CarPalette.Colors.Length - 1));
            }
            var list = OnlineClient.LobbyPlayers;
            var sb = new StringBuilder();
            foreach (var p in list) sb.Append(p.name).Append(p.isHost ? "(anfitrião)" : p.ready ? "(pronto)" : "(aguardando)").Append(p.connected ? "" : "[sem conexão]").Append("  ");
            string summary = sb.ToString();
            if (summary != lastLobby) { lastLobby = summary; AutoTest.Log("SALA " + OnlineClient.RoomCode + ": " + list.Count + " jogadores — " + summary); }
            if (!started && OnlineClient.IsHost && list.Count >= minPlayers && list.TrueForAll(p => p.isHost || p.ready))
            {
                started = true;
                AutoTest.Log("ONLINE todos prontos (" + list.Count + ") — iniciando a corrida");
                OnlineClient.SetReady(true);
                OnlineClient.StartRace();
            }
            var v = OnlineClient.Vote;
            if (v != null && !v.mine && !v.answered)
            {
                if (lastVoteId != v.id) { lastVoteId = v.id; voteSeenAt = Time.realtimeSinceStartup; AutoTest.Log("ONLINE votação " + v.kind + " pedida por " + v.byName); }
                else if (Time.realtimeSinceStartup - voteSeenAt > 2f) { OnlineClient.AnswerVote(true); AutoTest.Log("ONLINE votei SIM"); }
            }
            if (finishedAt > 0f && rematchAsked < AutoTest.GetCfgInt("onlinerematch", 1) && Time.realtimeSinceStartup - finishedAt > 20f)
            {
                rematchAsked++;
                finishedAt = -1f;
                AutoTest.Log("ONLINE pedindo JOGAR NOVAMENTE (" + rematchAsked + ")");
                OnlineClient.RequestVote("rematch");
            }
        }

        void OnGameStart(GameStartInfo info)
        {
            int humans = 0;
            foreach (var sp in info.startPositions) if (!sp.isAI) humans++;
            AutoTest.Log("ONLINE corrida " + info.round + " sala=" + OnlineClient.RoomCode + " pista=" + info.trackId + " voltas=" + info.laps + " carros=" + info.totalCars + " jogadores=" + humans + " sync=" + info.sync);
            finishedAt = -1f;
            // No menu quem carrega a pista é este teste; dentro da corrida (reinício/nova partida) é o OnlineRaceSync.
            if (OnlineRaceSync.Instance == null) RaceX.UI.MainMenuUI.LaunchOnlineRace(info, myName);
        }

        void OnOfficial()
        {
            finishedAt = Time.realtimeSinceStartup;
            var race = OnlineRaceSync.Instance != null ? OnlineRaceSync.Instance.Race : null;
            var sb = new StringBuilder("RESULTADO OFICIAL:");
            foreach (var r in OnlineClient.Rankings)
            {
                var e = OnlineDrive.EntryOf(race, r.id);
                string nm = !string.IsNullOrEmpty(r.name) ? r.name : e != null ? e.name : r.id;
                sb.Append(' ').Append(r.position).Append('.').Append(nm)
                  .Append(r.finished ? "(" + r.totalTime.ToString("0.000") + ")" : r.retired ? "(ABANDONO)" : "(volta " + r.lap + ")");
            }
            AutoTest.Log(sb.ToString());
        }

        void PollCommand()
        {
            try
            {
                if (!File.Exists(AutoTest.CmdPath)) return;
                string cmd = File.ReadAllText(AutoTest.CmdPath).Trim().ToLowerInvariant();
                File.Delete(AutoTest.CmdPath);
                AutoTest.Log("ONLINE comando: " + cmd);
                if (cmd == "restart") OnlineClient.RequestVote("restart");
                else if (cmd == "rematch") OnlineClient.RequestVote("rematch");
                else if (cmd == "start") { started = true; OnlineClient.SetReady(true); OnlineClient.StartRace(); }
                else if (cmd == "leave") OnlineClient.Disconnect();
            }
            catch (System.Exception ex) { AutoTest.Log("ONLINE comando falhou: " + ex.Message); }
        }
    }
}
