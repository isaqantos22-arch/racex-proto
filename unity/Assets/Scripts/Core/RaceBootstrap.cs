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
        /// <summary>Sessão da carreira na F2: todos com o carro único da categoria (CarSpec.ApplyF2Spec).</summary>
        static bool CareerF2 => RaceX.Career.CareerData.raceActive && RaceX.Career.CareerMode.data != null && RaceX.Career.CareerMode.data.category == "F2";

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
            var swBoot = System.Diagnostics.Stopwatch.StartNew();
            var settings = GameManager.Settings;
            bool split = settings.mode == GameMode.LocalSplitScreen;
            bool online = settings.mode == GameMode.Online;
            if (settings.laps <= 0) settings.laps = TrackLibrary.RealLaps(settings.track); // 0 = voltas reais (regra F1)
            SettingsManager.ApplyQuality(SettingsManager.Quality, split);
            PostFxSetup.Ensure();

            var def = TrackLibrary.Get(settings.track);
            // Sol/céu/névoa nunca podem prender o carregamento (build 17: exceção aqui deixou a cena em "ATIVANDO" para sempre).
            try { SetupEnvironment(def); }
            catch (System.Exception ex) { Debug.LogWarning("[Env] ambiente ignorado: " + ex.Message); }
            var track = TrackBuilder.Build(def);
            long msTrack = swBoot.ElapsedMilliseconds;

            var race = new GameObject("RaceManager").AddComponent<RaceManager>();
            race.Initialize(track, settings);
            // Regras escolhidas para ESTA corrida (configuração de cada modo, sala online ou hub da carreira).
            RaceX.Car.CarDamage.AllowTotalLoss = settings.totalLoss;
            RaceX.Core.RaceControl.PenaltiesEnabled = settings.penalties;
            // Direção de prova: bandeiras, VSC, limites de pista e penalidades (regras da F1).
            try { RaceX.Core.RaceControl.Create(race); }
            catch (System.Exception ex) { Debug.LogWarning("[RaceControl] ignorado: " + ex.Message); }
            // CLIMA (v80): antes do pit (o pneu de largada depende da água na pista). Carreira usa a
            // MESMA previsão mostrada no hub; o teste automático pode forçar; fora disso, seco.
            try
            {
                bool career = RaceX.Career.CareerData.raceActive && RaceX.Career.CareerMode.data != null && settings.careerPhase >= 0;
                // Mundo vivo: IA com os atributos dos pilotos + escuta contatos/perda total/punições do jogador.
                if (career) RaceX.Career.CareerWorld.BeginRace(); else RaceX.Car.CarAI.ProfileProvider = null;
                string forced = RaceX.Core.AutoTest.Weather;
                // Single Player / tela dividida / online: clima escolhido na configuração da corrida.
                if (string.IsNullOrEmpty(forced) && !career && !string.IsNullOrEmpty(settings.weather) && settings.weather != "dry") forced = settings.weather;
                if (career || !string.IsNullOrEmpty(forced))
                {
                    var cd = RaceX.Career.CareerMode.data;
                    var fc = !string.IsNullOrEmpty(forced) ? RaceX.Core.WeatherSystem.Forced(forced)
                        : RaceX.Core.WeatherSystem.ForecastFor(cd.season, cd.round, settings.careerPhase, (int)settings.track);
                    float secs = settings.sessionTimeLimit > 0f ? settings.sessionTimeLimit : Mathf.Max(1, settings.laps >= 900 ? 20 : settings.laps) * 90f;
                    RaceX.Core.WeatherSystem.Begin(race, fc, secs);
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[Weather] ignorado: " + ex.Message); }
            RaceX.Core.PitLaneSystem pit = null;
            {
                var pitGo = new GameObject("PitLaneSystem");
                pit = pitGo.AddComponent<RaceX.Core.PitLaneSystem>();
                // Um box POR CARRO (sem teto): com 24 carros em 16 boxes, os slots 16–23 nasciam
                // SOBRE os boxes 0–7 (pares de carros colados). Tem que vir ANTES do Setup:
                // o lane se dimensiona pelo nº de boxes.
                // Carreira: SEMPRE 24 boxes (uma garagem por piloto) — o box de CADA piloto é fixo
                // por equipe no fim de semana inteiro; numa quali reduzida não pode encolher o lane.
                pit.boxCount = (RaceX.Career.CareerData.raceActive && RaceX.Career.CareerMode.data != null)
                    ? Mathf.Max(TrackBuilder.GridSlots, settings.totalCars)
                    : Mathf.Max(settings.totalCars, 4);
                pit.Setup(race);
            }
            // Safety car: garagem no fim do pit lane em TODAS as pistas (só corridas; treino/classificação não).
            try { RaceX.Core.SafetyCar.Create(race); } catch (System.Exception ex) { Debug.LogWarning("[SC] ignorado: " + ex.Message); }
            // Treino livre da carreira (sex/sáb): todos NASEM no pit, saída em fila.
            bool pitStart = pit != null && settings.pitStops && settings.laps >= 900;
            // Box da garagem: na carreira é FIXO por equipe no fim de semana (BoxForDriver), NUNCA
            // pela posição de grid/qualificação; fora da carreira a dupla de grid divide um box (PitLaneSystem.BoxForSlot).
            int BoxOf(string driverName)
            {
                if (!RaceX.Career.CareerData.raceActive || RaceX.Career.CareerMode.data == null) return -1;
                int maxBox = pit != null ? Mathf.Max(0, pit.boxCount - 1) : TrackBuilder.GridSlots - 1;
                return Mathf.Clamp(RaceX.Career.CareerMode.BoxForDriver(driverName), 0, maxBox);
            }
            // Treino: o carro nasce no MESMO box e no MESMO lado para onde o PitLaneSystem o leva de volta (GarageLane:
            // ordem de criação dentro do box; sozinho no box = centro). Antes nascia na garagem "slot" (fora da carreira) e
            // pelo lado da ordem de criação, mas voltava pela regra do gridSlot — um carro parava em cima do outro.
            var boxTotal = new Dictionary<int, int>();
            var boxUsed = new Dictionary<int, int>();
            int GarageBox(string driverName, int slot)
            {
                int b = BoxOf(driverName);
                return b >= 0 ? b : (pit != null ? pit.BoxForSlot(slot) : slot / 2);
            }
            Pose? SpawnPose(string driverName, int slot)
            {
                if (!pitStart) return null;
                int g = GarageBox(driverName, slot);
                boxUsed.TryGetValue(g, out int used);
                boxUsed[g] = used + 1;
                boxTotal.TryGetValue(g, out int tot);
                return pit.PitSpawnPose(g, tot <= 1 ? -1 : used % 2);
            }
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
                var models = (PlannedNpcModels != null && PlannedNpcModels.Count == total - settings.HumanCount)
                    ? PlannedNpcModels
                    : PickNpcModels(total - settings.HumanCount, settings.formulaOnly, SettingsManager.AllowF1);
                PlannedNpcModels = null;
                // Carreira: IAs recebem os nomes dos rivais da temporada (ordem fixa do roster),
                // para que a classificação da corrida pontue a tabela real.
                int needNpc = total - settings.HumanCount;
                var roster = RaceX.Career.CareerData.raceActive ? RaceX.Career.CareerMode.RaceRoster : null;
                bool IsHumanName(string n)
                {
                    for (int p = 0; p < settings.HumanCount; p++) if (settings.players[p].name == n) return true;
                    return false;
                }
                // Lista CURTA (vaga vazia no mundo, nome vazio no grid salvo): completa com nomes reservas que ainda não estão
                // no grid. Antes o grid inteiro virava nomes do Single Player (sem equipe = todos vermelhos) ou, com o grid
                // salvo, o laço names[npc % Count] REPETIA pilotos.
                void PadNames(List<string> list)
                {
                    if (list.Count >= needNpc) return;
                    var pad = new List<string>(DriverNames);
                    Shuffle(pad);
                    foreach (var n in pad)
                    {
                        if (list.Count >= needNpc) break;
                        if (!string.IsNullOrEmpty(n) && !list.Contains(n) && !IsHumanName(n)) list.Add(n);
                    }
                }
                List<string> names;
                if (careerGridNames != null && settings.HumanCount == 1)
                {
                    // Grid real: rivais ocupam os slots na ordem exata da classificação (sem shuffle). Nome vazio/duplicado
                    // no grid salvo → completa com os rivais que faltam, sem nunca criar "carro sem nome".
                    names = new List<string>();
                    foreach (var n in careerGridNames)
                        if (!string.IsNullOrEmpty(n) && !IsHumanName(n) && !names.Contains(n)) names.Add(n);
                    if (roster != null)
                        foreach (var n in roster)
                            if (names.Count < needNpc && !string.IsNullOrEmpty(n) && !IsHumanName(n) && !names.Contains(n)) names.Add(n);
                    PadNames(names);
                }
                else
                {
                    names = roster != null ? new List<string>(roster) : new List<string>(DriverNames);
                    if (roster != null) PadNames(names);
                    Shuffle(names);
                }
                // Quantos carros dividem cada garagem, na ORDEM em que serão criados (humanos e depois os slots da IA).
                {
                    int k = 0;
                    void CountBox(int b) { boxTotal.TryGetValue(b, out int n); boxTotal[b] = n + 1; }
                    for (int p = 0; p < settings.HumanCount; p++) CountBox(GarageBox(settings.players[p].name, slots[p]));
                    for (int slot = 0; slot < total; slot++)
                        if (!slots.Contains(slot)) CountBox(GarageBox(names[k++ % names.Count], slot));
                }
                // MÁXIMO 2 POR COR (fora da carreira — lá as 12 equipes já têm cor única): plano antes de criar os carros.
                int[] spColor = RaceX.Career.CareerData.raceActive ? null : PlanSinglePlayerColors(total, settings, slots);
                for (int p = 0; p < settings.HumanCount; p++)
                {
                    var ps = settings.players[p];
                    // Carreira: o carro usa a cor da EQUIPE; Single Player mantém a cor escolhida.
                    int hColor = RaceX.Career.CareerData.raceActive
                        ? CarPalette.TeamColorOf(RaceX.Career.CareerMode.TeamIndexOf(RaceX.Career.CareerMode.TeamOfDriver(ps.name)))
                        : (spColor != null ? spColor[Mathf.Clamp(slots[p], 0, total - 1)] : ps.colorIndex);
                    var humanModel = RaceX.Career.CareerData.raceActive
                        ? RaceX.Career.CareerMode.PlayerCar()
                        : (settings.formulaOnly ? CarModel.F1 : (!SettingsManager.AllowF1 && ps.car == CarModel.F1 ? CarModel.Titan : ps.car));
                    var hspec = CarSpec.CreateDefault(humanModel);
                    if (CareerF2 || AutoTest.F2Spec) hspec.ApplyF2Spec();   // F2 = carro único (o ajuste de equipe vem depois)
                    // Upgrades da GARAGEM (créditos do Single Player) não valem na carreira: lá o carro é o da equipe, igual ao do
                    // companheiro e na mesma base dos rivais (pedido do usuário: "não quero vantagem").
                    if (!RaceX.Career.CareerData.raceActive) CareerStats.ApplyUpgrades(hspec);
                    if (RaceX.Career.CareerData.raceActive && RaceX.Career.CareerMode.data != null)
                        RaceX.Career.CareerMode.ApplyTeamPerformance(hspec, RaceX.Career.CareerMode.data.f2Team);
                    int hBox = BoxOf(ps.name);
                    var hEntry = race.SpawnCar(hspec, hColor, ps.name, slots[p], true, p, SpawnPose(ps.name, slots[p]));
                    if (hBox >= 0) hEntry.pitBox = hBox;
                    humans.Add(hEntry);
                }

                int npc = 0;
                for (int slot = 0; slot < total; slot++)
                {
                    if (slots.Contains(slot)) continue;
                    string drvName = names[npc % names.Count];
                    // CORES POR EQUIPE: na carreira, os 2 carros da MESMA equipe compartilham a cor da
                    // equipe (12 cores únicas); no Single Player, pares de grid viram equipes virtuais
                    // (cada par tem cor própria — nunca 4 carros com a mesma cor).
                    string drvTeam = RaceX.Career.CareerData.raceActive ? RaceX.Career.CareerMode.TeamOfDriver(drvName) : "";
                    int c = RaceX.Career.CareerData.raceActive && !string.IsNullOrEmpty(drvTeam)
                        ? CarPalette.TeamColorOf(RaceX.Career.CareerMode.TeamIndexOf(drvTeam))
                        : (spColor != null ? spColor[slot] : CarPalette.TeamColorOf(slot / 2));   // sem equipe (vaga completada): cor por par
                    var carModel = RaceX.Career.CareerData.raceActive
                        ? RaceX.Career.CareerMode.CarForDriver(drvName)
                        : models[npc];
                    var nspec = CarSpec.CreateDefault(carModel);
                    if (CareerF2 || AutoTest.F2Spec) nspec.ApplyF2Spec();
                    if (RaceX.Career.CareerData.raceActive)
                        RaceX.Career.CareerMode.ApplyTeamPerformance(nspec, RaceX.Career.CareerMode.TeamOfDriver(drvName));
                    int nBox = BoxOf(drvName);
                    var entry = race.SpawnCar(nspec, c, drvName, slot, false, -1, SpawnPose(drvName, slot));
                    if (nBox >= 0) entry.pitBox = nBox;
                    var ai = entry.car.gameObject.AddComponent<CarAI>();
                    ai.Initialize(track.Waypoints, race, settings.difficulty,
                        RaceX.Career.CareerData.raceActive ? RaceX.Career.CareerMode.SkillBiasFor(drvName) : 0f);
                    ai.SetLane(((npc * 37) % 9 - 4) * 0.42f);
                    RaceX.UI.Nametag.Attach(entry.car, drvName);
                    npc++;
                }
            }

            foreach (var e in race.entries) EngineAudio.Attach(e.car, e.isHuman);
            foreach (var e in race.entries) VisualEffects.Attach(e.car);
            // Freios, motor e combustível (offline: no online cada cliente simula só o próprio carro).
            foreach (var e in race.entries) if (!online || e.isHuman) CarSystems.Attach(e.car, race, e);
            // Mecânicos animados no box (só boxes ativos perto da câmera; sem o asset, nada muda).
            if (pit != null) { try { RaceX.Core.PitCrewSystem.Create(race); } catch (System.Exception ex) { Debug.LogWarning("[PitCrew] ignorado: " + ex.Message); } }
            Debug.Log("[BOOT] pista=" + msTrack + "ms total_ate_efeitos=" + swBoot.ElapsedMilliseconds + "ms carros=" + race.entries.Count);
            // Marcas de pneu por TODA a cena (freio/travada + grama/terra). Material com FALLBACK de
            // shader e try/catch: sem shader válido o Attach falha em silêncio e nunca trava o loading.
            foreach (var e in race.entries) RaceX.Track.SkidMarks.Attach(e.car);

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

            // ONLINE: a largada espera TODOS carregarem (OnlineRaceSync → game:go do servidor) — antes cada aparelho largava
            // quando acabava de carregar. Também cuida das votações (reiniciar / jogar novamente) e do resultado oficial.
            if (online) RaceX.Core.OnlineRaceSync.Create(canvas, race, humans.Count > 0 ? humans[0].name : settings.players[0].name);
            else race.BeginCountdown();
            RaceX.Core.AutoTest.AttachRunner(race);

            if (online && humans.Count > 0)
            {
                var local = humans[0];
                race.EntryFinished += e => { if (e == local) OnlineClient.SendFinished(e.totalTime, e.bestLap > 0f && e.bestLap < float.MaxValue ? e.bestLap : 0f, e.penaltySeconds); };
                race.EntryRetired += e => { if (e == local) OnlineClient.SendRetired(); };
                StartCoroutine(OnlineTelemetry(race, local));
                StartCoroutine(OnlineAiTelemetry(race));   // em todos: quem virar anfitrião no meio da corrida passa a transmitir
            }
        }

        /// <summary>Carros por família de cor já distribuídos no online (jogadores + IA) — máximo 2 por cor.</summary>
        int[] onlineFam = new int[CarPalette.FamilyCount];
        readonly Dictionary<string, int> onlineColor = new Dictionary<string, int>();

        /// <summary>Single Player / tela dividida: jogadores mantêm a cor escolhida (até 2 por família); o carro da IA que divide
        /// o box com o jogador (dupla do grid) fica da MESMA cor — companheiro de equipe; as outras duplas ficam com a cor de
        /// equipe delas ou a próxima família livre. Antes: jogador vermelho + dupla vermelha = 3 vermelhos.</summary>
        static int[] PlanSinglePlayerColors(int total, RaceSettings settings, IList<int> slots)
        {
            var col = new int[total];
            var fam = new int[CarPalette.FamilyCount];
            var done = new bool[total];
            for (int p = 0; p < settings.HumanCount && p < slots.Count; p++)
            {
                int s = Mathf.Clamp(slots[p], 0, total - 1);
                int c = CarPalette.FreeColor(fam, 1, settings.players[p].colorIndex);
                col[s] = c; fam[CarPalette.FamilyOf(c)]++; done[s] = true;
            }
            var humanSlots = new List<int>();
            for (int s = 0; s < total; s++) if (done[s]) humanSlots.Add(s);
            foreach (int s in humanSlots)
            {
                int partner = s % 2 == 0 ? s + 1 : s - 1;
                if (partner < 0 || partner >= total || done[partner]) continue;
                int c = CarPalette.FreeColor(fam, 1, col[s]);
                col[partner] = c; fam[CarPalette.FamilyOf(c)]++; done[partner] = true;
            }
            for (int k = 0; k * 2 < total; k++)
            {
                int a = k * 2, b = k * 2 + 1;
                bool na = !done[a], nb = b < total && !done[b];
                int need = (na ? 1 : 0) + (nb ? 1 : 0);
                if (need == 0) continue;
                int c = CarPalette.FreeColor(fam, need, CarPalette.TeamColorOf(k));
                if (na) col[a] = c;
                if (nb) col[b] = c;
                fam[CarPalette.FamilyOf(c)] += need;
            }
            return col;
        }

        void SpawnOnline(RaceManager race, RaceSettings settings, WaypointSystem waypoints, List<RaceEntry> humans, HashSet<int> usedColors)
        {
            var real = new List<StartSlotInfo>();
            var gi = OnlineClient.StartInfo;
            if (gi != null)
            {
                foreach (var sp in gi.startPositions) if (!sp.isAI) real.Add(sp);
            }
            // Cores dos jogadores na ORDEM DO GRID (igual em todos os aparelhos): a escolhida enquanto a família tiver menos
            // de 2 carros; o 3º em diante recebe a próxima livre.
            onlineFam = new int[CarPalette.FamilyCount];
            onlineColor.Clear();
            var bySlot = new List<StartSlotInfo>(real);
            bySlot.Sort((x, y) => x.slot.CompareTo(y.slot));
            foreach (var sp in bySlot)
            {
                int c = CarPalette.FreeColor(onlineFam, 1, sp.carColor);
                onlineFam[CarPalette.FamilyOf(c)]++;
                if (sp.id != null) onlineColor[sp.id] = c;
            }
            int ColorFor(StartSlotInfo sp) => sp.id != null && onlineColor.TryGetValue(sp.id, out int c) ? c : sp.carColor;
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
                onlineFam[CarPalette.FamilyOf(ps.colorIndex)]++;
                var fallbackSpec = CarSpec.CreateDefault(ps.car);
                CareerStats.ApplyUpgrades(fallbackSpec);
                var fb = race.SpawnCar(fallbackSpec, ps.colorIndex, ps.name, 0, true, 0);
                fb.netId = OnlineClient.MyPlayerId;
                humans.Add(fb);
                SpawnOnlineAi(race, settings, waypoints, usedColors, new HashSet<int> { 0 }, null);
                return;
            }
            var mySpec = CarSpec.CreateDefault(OnlineClient.CarModelFromServer(mine.carModel));
            CareerStats.ApplyUpgrades(mySpec);
            int mySlot = Mathf.Clamp(mine.slot, 0, TrackBuilder.GridSlots - 1);
            usedColors.Add(mine.carColor);
            var myEntry = race.SpawnCar(mySpec, ColorFor(mine), settings.players[0].name, mySlot, true, 0);
            myEntry.netId = mine.id;
            humans.Add(myEntry);
            foreach (var sp in real)
            {
                if (sp.id == mine.id) continue;
                usedColors.Add(sp.carColor);
                var entry = race.SpawnCar(CarSpec.CreateDefault(OnlineClient.CarModelFromServer(sp.carModel)), ColorFor(sp), sp.name, Mathf.Clamp(sp.slot, 0, TrackBuilder.GridSlots - 1), false, -1);
                entry.netId = sp.id;
                var ghost = entry.car.gameObject.AddComponent<OnlineGhost>();
                ghost.playerId = sp.id;
                ghost.race = race;
                ghost.entry = entry;
                entry.car.Body.isKinematic = true;
                RaceX.UI.Nametag.Attach(entry.car, sp.name);
            }
            SpawnOnlineAi(race, settings, waypoints, usedColors, OccupiedSlots(real), gi);
        }

        static HashSet<int> OccupiedSlots(List<StartSlotInfo> real)
        {
            var occupied = new HashSet<int>();
            foreach (var sp in real) occupied.Add(Mathf.Clamp(sp.slot, 0, TrackBuilder.GridSlots - 1));
            return occupied;
        }

        /// <summary>NPCs do online: as VAGAS vêm do servidor (ai_0..ai_N, do total de carros da sala) e
        /// nomes/modelos/cores saem de uma semente do código da sala — todos veem os MESMOS adversários.
        /// O ANFITRIÃO pilota a IA e transmite as posições (OnlineAiTelemetry); nos convidados os NPCs
        /// são fantasmas que seguem essas posições. Antes cada aparelho criava NPCs próprios (quantidade
        /// da configuração local, nomes sorteados) e ninguém sincronizava: cada um corria contra outros.</summary>
        void SpawnOnlineAi(RaceManager race, RaceSettings settings, WaypointSystem waypoints, HashSet<int> usedColors, HashSet<int> occupied, GameStartInfo gi)
        {
            var aiSlots = new List<StartSlotInfo>();
            if (gi != null) foreach (var sp in gi.startPositions) if (sp.isAI) aiSlots.Add(sp);
            if (gi == null)
            {
                // Sem dados da sala (teste offline): mantém o comportamento antigo com a quantidade local.
                int n = Mathf.Min(Mathf.Max(0, SettingsManager.OnlineAiCount), TrackBuilder.GridSlots - occupied.Count);
                for (int slot = 0, k = 0; slot < TrackBuilder.GridSlots && k < n; slot++)
                    if (!occupied.Contains(slot)) aiSlots.Add(new StartSlotInfo { id = "ai_" + k++, slot = slot, isAI = true });
            }
            if (aiSlots.Count == 0) return;
            bool driveAi = gi == null || OnlineClient.IsHost;
            var oldState = Random.state;
            Random.InitState(StableSeed(OnlineClient.RoomCode));
            var models = PickNpcModels(aiSlots.Count, settings.formulaOnly, false);
            var names = new List<string>(DriverNames);
            Shuffle(names);
            Random.state = oldState;
            int color = 0;
            OnlineDrive.Reset();
            for (int npc = 0; npc < aiSlots.Count; npc++)
            {
                int slot = Mathf.Clamp(aiSlots[npc].slot, 0, TrackBuilder.GridSlots - 1);
                if (occupied.Contains(slot)) continue;
                occupied.Add(slot);
                // Máximo 2 por cor: a dupla do grid (slot/2) prefere a cor de equipe dela; família cheia → próxima livre.
                // Antes eram 8 cores em rodízio — com muitos carros a mesma cor se repetia 3 vezes ou mais.
                int c = CarPalette.FreeColor(onlineFam, 1, CarPalette.TeamColorOf(slot / 2));
                onlineFam[CarPalette.FamilyOf(c)]++;
                usedColors.Add(c);
                color++;
                string nm = names[npc % names.Count];
                var entry = race.SpawnCar(CarSpec.CreateDefault(models[npc]), c, nm, slot, false, -1);
                entry.netId = aiSlots[npc].id;
                if (driveAi)
                {
                    var ai = entry.car.gameObject.AddComponent<CarAI>();
                    ai.Initialize(waypoints, race, settings.difficulty);
                    ai.SetLane(((npc * 37) % 9 - 4) * 0.42f);
                    OnlineDrive.Driven.Add(new KeyValuePair<string, RaceEntry>(aiSlots[npc].id, entry));
                }
                else
                {
                    var ghost = entry.car.gameObject.AddComponent<OnlineGhost>();
                    ghost.playerId = aiSlots[npc].id;
                    ghost.race = race;
                    ghost.entry = entry;
                    entry.car.Body.isKinematic = true;
                }
                RaceX.UI.Nametag.Attach(entry.car, nm);
            }
        }

        // Carros que ESTE aparelho pilota e transmite (IA do anfitrião + carro de quem caiu): RaceX.Core.OnlineDrive.Driven.

        static int StableSeed(string s)
        {
            if (string.IsNullOrEmpty(s)) return 12345;
            unchecked
            {
                int h = 17;
                foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }

        /// <summary>Anfitrião: envia as posições dos NPCs a 10 Hz (o servidor repassa no game:update). Continua depois que o
        /// anfitrião cruza a linha, até o resultado OFICIAL — antes parava junto com a corrida dele e a IA congelava na tela
        /// de quem ainda corria. Distância medida na hora (a do RaceManager para de atualizar quando a corrida dele acaba).</summary>
        IEnumerator OnlineAiTelemetry(RaceManager race)
        {
            var items = new List<string>(24);
            // Enquanto a sala existir (queda de conexão: espera reconectar em vez de parar de vez).
            while (race != null && !string.IsNullOrEmpty(OnlineClient.RoomCode) && OnlineClient.Rankings.Count == 0)
            {
                yield return new WaitForSeconds(0.1f);
                if (!OnlineClient.Connected || !OnlineClient.IsHost || OnlineDrive.Driven.Count == 0) continue;
                if (race.State == RaceState.Setup) continue;
                items.Clear();
                foreach (var kv in OnlineDrive.Driven)
                {
                    var e = kv.Value;
                    if (e == null || e.car == null) continue;
                    var p = race.GetProgress(e);
                    int lap = p != null ? Mathf.Clamp(p.lap, 1, race.Settings.laps) : 1;
                    int ck = p != null ? p.lastCheckpoint : 0;
                    var t = e.car.transform;
                    float dist = race.Track != null && race.Track.Checkpoints != null ? race.Track.Checkpoints.RaceDistance(e.car) : e.raceDistance;
                    items.Add(OnlineClient.AiStateJson(kv.Key, t.position, t.eulerAngles.y, e.car.SpeedKmh, lap, ck,
                        e.finished ? e.totalTime : 0f, e.bestLap < float.MaxValue ? e.bestLap : 0f,
                        dist, e.retired ? 2 : 0, e.penaltySeconds));
                }
                OnlineClient.SendAiPositions(items);
            }
        }

        /// <summary>Posição do jogador a 10 Hz, inclusive depois de cruzar a linha (volta de desaceleração) até o resultado
        /// oficial — antes parava na linha e o carro dele congelava na tela dos outros.</summary>
        IEnumerator OnlineTelemetry(RaceManager race, RaceEntry local)
        {
            var car = local != null ? local.car : null;
            while (race != null && car != null && !string.IsNullOrEmpty(OnlineClient.RoomCode) && OnlineClient.Rankings.Count == 0)
            {
                yield return new WaitForSeconds(0.1f);
                if (!OnlineClient.Connected || race.State == RaceState.Setup || race.State == RaceState.Countdown) continue;
                var p = race.GetProgress(local);
                int lap = p != null ? Mathf.Clamp(p.lap, 1, race.Settings.laps) : 1;
                int ck = p != null ? p.lastCheckpoint : 0;
                float dist = race.Track != null && race.Track.Checkpoints != null ? race.Track.Checkpoints.RaceDistance(car) : local.raceDistance;
                OnlineClient.SendPosition(car.transform.position.x, car.transform.position.y, car.transform.position.z, car.transform.eulerAngles.y, car.SpeedKmh, lap, ck,
                    dist, local.retired ? 2 : local.finished ? 1 : 0);
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
            if (entry.playerIndex == 0 && race.Track != null && race.Track.Waypoints != null && race.Settings != null)
                entry.car.gameObject.AddComponent<RaceX.Car.PlayerPaceRecorder>().Initialize(entry.car, race.Track.Waypoints, race, (int)race.Settings.track);
            // Linha tracejada para CADA jogador (tela dividida): cada câmera desenha só a do seu dono.
            // Antes só o J1 tinha guia e o J2 via a do J1 quando chegava perto dele.
            if (race.Track != null && race.Track.Waypoints != null && race.Settings != null)
            {
                try { entry.car.gameObject.AddComponent<RaceX.Track.RacingLineGuide>().Initialize(entry.car, race, split ? cam : null); }
                catch (System.Exception ex) { Debug.LogWarning("[RacingLine] " + ex.Message); }
            }
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
            var pauseCar = entry.car;   // a pausa sabe QUEM abriu (VOLTAR À PISTA só recoloca esse carro)
            input.PausePressed += () => pause.ToggleFor(pauseCar);
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
            // Resources.Load não aceita extensão: tenta também o caminho sem ".hdr" (céus novos do Poly Haven, v99).
            if (skyTex == null && !string.IsNullOrEmpty(def.skyboxExr) && def.skyboxExr.Contains("."))
                skyTex = Resources.Load<Texture2D>(def.skyboxExr.Substring(0, def.skyboxExr.LastIndexOf('.')));
            Material skyMat = null;
            if (skyTex != null)
            {
                // Shader.Find("Skybox/Panoramic") volta NULL no APK (o IL2CPP remove shader que nenhuma cena usa) e a
                // exceção prendia o carregamento em 100% (build 17). O build cria Resources/Env/SkyPanoramic.mat
                // (RaceXSetup.SetupBaseMaterials) só para o shader entrar no pacote; sem ele, fica o ambiente liso.
                try
                {
                    var tpl = Resources.Load<Material>("Env/SkyPanoramic");
                    var sh = tpl != null ? tpl.shader : Shader.Find("Skybox/Panoramic");
                    if (sh != null)
                    {
                        skyMat = tpl != null ? new Material(tpl) : new Material(sh);
                        skyMat.name = "Sky_" + skyTex.name;
                        skyMat.SetTexture("_MainTex", skyTex);
                        skyMat.SetFloat("_Exposure", SkyExposure(skyTex.name));
                    }
                    Debug.Log("[Env] céu " + skyTex.name + " shader=" + (sh != null ? sh.name : "AUSENTE") + " exposicao=" + SkyExposure(skyTex.name).ToString("0.00"));
                }
                catch (System.Exception ex) { Debug.LogWarning("[Env] céu ignorado: " + ex.Message); skyMat = null; }
            }
            // O céu é só PANO DE FUNDO: a luz ambiente continua a da pista (def.ambientColor), como sempre foi. O ambiente
            // pelo HDRI (build 18) clareava a noite inteira e mudaria a iluminação já aprovada das 24 pistas.
            if (skyMat != null) RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = def.ambientColor;
            RenderSettings.fog = def.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = def.fogColor;
            RenderSettings.fogDensity = def.fogDensity;
        }

        /// <summary>Exposição por céu (medida em scratchpad hdr_stats.py). O projeto está em espaço de cor GAMMA: o valor do HDRI vai
        /// direto para a tela (Skybox/Panoramic: tinta 0,5 × ColorSpaceDouble 2 = 1). Alvo = mediana do céu ~0,4–0,5 de dia e
        /// ~0,12–0,15 à noite. O HDRI da cidade (build 18) tinha mediana 1,19 — por isso saiu branco. Sem entrada: 1,1.</summary>
        static float SkyExposure(string tex)
        {
            switch (tex)
            {
                case "kloofendal_48d_partly_cloudy_puresky_2k": return 1.4f;   // dia, nuvens (mediana 0,33)
                case "kloofendal_43d_clear_puresky_2k": return 2.2f;           // dia limpo (0,18)
                case "sunflowers_puresky_2k": return 2.2f;                     // meio-dia, nuvens esparsas (0,19)
                case "kloppenheim_06_puresky_2k": return 0.8f;                 // entardecer (0,43; a 1,0 saiu céu pálido no build 19)
                case "kloppenheim_07_puresky_2k": return 0.5f;                 // noite nublada com brilho de cidade (0,30)
                case "kloppenheim_02_puresky_2k": return 1.0f;                 // noite limpa, estrelas e lua (0,10)
                // Céus antigos (4k, com horizonte: colinas, rochas, estacionamento ao entardecer) — pedido do usuário de vê-los
                case "sky_forest": return 2.0f;                                // dia, nuvens e colinas verdes (0,19; a 2,4 o horizonte saiu branco)
                case "sky_desert": return 3.0f;                                // deserto, sol forte e rochas (0,06; horizonte 0,12)
                case "belfast_sunset_puresky_2k": return 0.5f;                 // pôr do sol roxo/rosa, só céu (0,93) — substitui o sky_city (tinha prédio e árvores)
                case "industrial_sunset_puresky_2k": return 0.6f;              // fim de tarde com nuvens dramáticas, só céu (0,76)
                default: return 1.1f;
            }
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
        public static List<CarModel> PlannedNpcModels;

        public static List<CarModel> PlanNpcModels(int count, bool formulaOnly)
        {
            PlannedNpcModels = PickNpcModels(count, formulaOnly, SettingsManager.AllowF1);
            return PlannedNpcModels;
        }

        static List<CarModel> PickNpcModels(int count, bool formulaOnly, bool allowF1)
        {
            var list = new List<CarModel>();
            if (formulaOnly)
            {
                // Categoria F1: grid com os monopostos variados (antes eram todos o mesmo F1).
                var fm = new List<CarModel>(CarSpec.FormulaModels);
                Shuffle(fm);
                for (int i = 0; i < count; i++) list.Add(fm[i % fm.Count]);
                return list;
            }
            // GT: só carros de rua (antes F2008/F1Studio podiam cair no grid GT); F1 só com "F1 em GT".
            var pool = new List<CarModel>();
            foreach (CarModel cm in System.Enum.GetValues(typeof(CarModel)))
                if (!CarSpec.IsFormula(cm) || (cm == CarModel.F1 && allowF1)) pool.Add(cm);
            while (pool.Count > 4) pool.RemoveAt(Random.Range(0, pool.Count));
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
