using System;
using System.Collections.Generic;
using System.IO;
using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Career
{
    /// <summary>Uma linha da tabela de classificação da temporada.</summary>
    [Serializable]
    public class StandingEntry
    {
        public string driverName;
        public string team;
        public int points;
        public bool isPlayer;
        public int wins;
    }

    /// <summary>Save do modo carreira (serializado com JsonUtility — campos públicos, sem Dictionary).</summary>
    [Serializable]
    public class CareerData
    {
        public string pilotName = "";
        public string nationality = "";
        public float attack, feel, stage, heat;
        public string code = "";
        public string archetype = "";
        public string academyId = "";
        public string academyName = "";
        public string f2Team = "";
        public string category = "F2";
        public bool awaitingTeamChoice;     // aguardando o jogador escolher proposta de equipe
        public string[] teamOffers = new string[0]; // propostas na mesa (escolha de equipe)
        public long sessionDeadlineUtc;     // fim da sessão em ticks UTC (relógio REAL: corre também no menu)
        public List<string> pracNames = new List<string>(); // treino: pilotos com melhor volta gravada
        public List<float> pracBest = new List<float>();    // treino: melhor volta de cada piloto (sincronizado com pracNames)
        public int season = 1;
        public int round;
        public int pointsPlayer;
        public int seasonWins;
        public int seasonPodiums;
        public int careerWins;
        public int careerPodiums;
        public int titles;
        public bool promotedThisSeason;
        public List<StandingEntry> standings = new List<StandingEntry>();

        /// <summary>Fase atual do fim de semana (ver CareerMode.PH_*). Padrão 6 = Q1 pendente (migração de saves antigos).</summary>
        public int weekendPhase = CareerMode.PH_Q1;
        public bool fpDone;
        public int gpGrid;
        public int sqGrid;
        /// <summary>Classificação eliminatória: sobreviventes (melhor volta, melhor primeiro) + já eliminados (segmento do fundo do grid).</summary>
        public List<string> sqChain = new List<string>();
        public List<string> sqLocked = new List<string>();
        public List<string> qChain = new List<string>();
        public List<string> qLocked = new List<string>();
        /// <summary>Dificuldade das IAs na carreira (0 Fácil, 1 Médio, 2 Difícil, 3 Expert).</summary>
        public int difficulty = 1;
        /// <summary>Versão do save (0 = save antigo pré-fases-eliminatórias → migrar).</summary>
        public int saveVersion;

        /// <summary>Corrida em andamento (não persiste — JsonUtility ignora campos static).</summary>
        public static bool raceActive;
    }

    /// <summary>Lógica do modo carreira: save/load, calendário, corridas e progressão F2 → F1.</summary>
    public static class CareerMode
    {
        public static CareerData data;

        public static bool HasSave => File.Exists(SavePath);

        static string SavePath => Path.Combine(Application.persistentDataPath, "racex_career.json");

        /// <summary>Verdadeiro após o round final — consumido pela UI na tela de fim de temporada.</summary>
        public static bool JustFinishedSeason { get; private set; }

        /// <summary>Posição final do piloto na tabela da temporada encerrada.</summary>
        public static int FinalPosition { get; private set; }

        static readonly string[] F2Teams =
        {
            "Invicta", "Hitech", "Campos", "DAMS", "MP Motorsport", "Prema",
            "Rodin", "ART Grand Prix", "Virtuosi", "Carlin", "Trident", "Independente"
        };

        // F2: ordem do campeonato de equipes 2025 (Invicta 316, Hitech 278, Campos 258, DAMS 207, MP 207, Prema 163, Rodin 155, ART 127, Trident 2);
        // Virtuosi e Carlin nao constam na tabela 2025 pesquisada: posicao estimada. Independente e ficticia (fundo).
        // F1: ordem pela hierarquia 2025 (McLaren, Red Bull/Mercedes/Ferrari, Williams, Racing Bulls, Aston Martin, Haas, Sauber, Alpine):
        // Adler=McLaren, Nova=Mercedes, Vortex=Red Bull, Meridian=Ferrari, Apex=Williams, Horizonte=Racing Bulls, Falcao=Aston Martin, Vector=Haas, Northline=Sauber, Solaris=Alpine; Cobalt e Duna ficticias (fundo).
        static readonly string[] F1Teams =
        {
            "Scuderia Adler", "Racing Nova", "Vortex GP", "Team Meridian", "Apex Racing", "Equipe Horizonte",
            "Falcao Motorsport", "Vector Racing", "Northline GP", "Solaris F1", "Cobalt Racing", "Duna Racing"
        };

        static readonly string[] RivalNames =
        {
            "L. Moreira", "T. Nakamura", "R. Silva", "K. Lindqvist", "D. Ferreira",
            "M. Costa", "A. Bianchi", "J. Okafor", "P. Sorensen", "V. Rossi", "C. Mendez",
            "F. Duarte", "G. Hakala", "H. Verne", "I. Castro", "J. Marchetti",
            "K. Sato", "L. Fontana", "M. Richter", "N. Osei", "O. Kirchner",
            "P. Duval", "Q. Marques"
        };

        /// <summary>Acesso de LEITURA aos rivais (testes de Editor / arnês de lógica).</summary>
        public static string[] RivalNamesAccess => RivalNames;

        /// <summary>Escada GT: índice 0 = equipe mais FRACA (onde o jogador desconhecido começa), 11 = mais forte.</summary>
        public static readonly string[] GTTeams =
        {
            "Garagem Duarte", "Escuderia Serrana", "Team Vulcano", "Racing Pampa",
            "Tropa Diesel", "Coral Motorsport", "Aurora GT", "Bruma Racing",
            "Flecha Sport", "Team Quasar", "Real Amazonia", "Imperio Motorsport"
        };

        /// <summary>Carro por força da equipe GT — velocidades REAIS (Mustang 250 → Bugatti 407 km/h).</summary>
        public static readonly CarModel[] GTCarsByTier =
        {
            CarModel.Mustang,    // 250 km/h
            CarModel.Titan,      // 283 km/h
            CarModel.GtrNismo,   // 315 km/h
            CarModel.GtrNismo,
            CarModel.Falcon,     // 330 km/h
            CarModel.Ferrari,    // 330 km/h
            CarModel.R8,         // 330 km/h
            CarModel.Falcon,
            CarModel.Revuelto,   // 350 km/h
            CarModel.McLarenP1,  // 350 km/h
            CarModel.McLarenP1,
            CarModel.Bugatti     // 407 km/h
        };

        static string[] TeamsFor(string category) =>
            category == "GT" ? GTTeams : category == "F1" ? F1Teams : F2Teams;

        /// <summary>Carro por força da equipe F1 — fundo de grade usa F2008, meio F1Studio, ponta F1 2022.</summary>
        public static readonly CarModel[] F1CarsByTier =
        {
            CarModel.F2008, CarModel.F2008, CarModel.F2008, CarModel.F2008,
            CarModel.F1Studio, CarModel.F1Studio, CarModel.F1Studio, CarModel.F1Studio,
            CarModel.F1, CarModel.F1, CarModel.F1, CarModel.F1
        };

        /// <summary>Índice do time na escada da categoria atual (0 = mais fraca).</summary>
        public static int TeamTier(string team)
        {
            var teams = TeamsFor(data != null ? data.category : "F2");
            int i = Array.IndexOf(teams, team);
            if (i < 0) return 0;
            bool gt = data != null && data.category == "GT";
            return gt ? i : teams.Length - 1 - i;
        }

        /// <summary>Fator de desempenho do carro por equipe. F2: 1,00 (fundo) a 1,06 (Prema) — nunca abaixo do F2 atual.
        /// F1: 1,12 (fundo) a 1,20 (ponta; grid real 2025 estava a 1,36% de tempo entre o mais rapido e o mais lento) — todo F1 acima de qualquer F2. GT já difere pelo modelo do carro.</summary>
        public static float TeamPerformanceFor(string team)
        {
            if (data == null || string.IsNullOrEmpty(team)) return 1f;
            int t = TeamTier(team);
            if (data.category == "F1") return 1.12f + t / 11f * 0.08f;
            if (data.category == "F2") return 1.00f + t / 11f * 0.06f;
            return 1f;
        }

        public static string TeamOfDriver(string driverName)
        {
            if (data == null) return "";
            if (driverName == data.pilotName) return data.f2Team;
            var e = data.standings.Find(s => s.driverName == driverName);
            return e != null ? e.team : "";
        }

        /// <summary>Aplica a diferença real entre equipes: torque (aceleração), apoio aerodinâmico e aderência lateral.</summary>
        public static void ApplyTeamPerformance(CarSpec spec, string team)
        {
            if (spec == null) return;
            float f = TeamPerformanceFor(team);
            if (f <= 1.0001f) return;
            float d = f - 1f;
            spec.maxTorqueNm *= f;
            spec.downforceCoefficient *= 1f + d * 0.6f;
            spec.sidewaysStiffness *= 1f + d * 0.25f;
        }

        /// <summary>Carro do JOGADOR: o da sua equipe atual (GT/F1) ou o da categoria F2.</summary>
        public static CarModel PlayerCar()
        {
            if (data == null) return CarModel.F1;
            if (data.category == "GT") return GTCarsByTier[TeamTier(data.f2Team)];
            if (data.category == "F1") return F1CarsByTier[TeamTier(data.f2Team)];
            return CarModel.F2008;
        }

        /// <summary>Carro de qualquer piloto do grid (consulta a tabela da temporada).</summary>
        public static CarModel CarForDriver(string driverName)
        {
            if (data == null) return CarModel.F1;
            if (driverName == data.pilotName) return PlayerCar();
            if (data.category == "F2") return CarModel.F2008;
            var e = data.standings.Find(s => s.driverName == driverName);
            string team = e != null ? e.team : "";
            if (data.category == "GT") return GTCarsByTier[TeamTier(team)];
            return F1CarsByTier[TeamTier(team)];
        }

        /// <summary>Rótulo do carro da equipe (para a tela de propostas).</summary>
        public static string TeamCarLabel(string team)
        {
            if (data == null) return "";
            if (data.category == "GT") return GTTeamCarKmh(team) + " KM/H";
            if (data.category == "F2") return "F2 — CARRO DE FÓRMULA";
            var m = F1CarsByTier[TeamTier(team)];
            return m == CarModel.F1 ? "F1 2022 — PONTEIRA" : m == CarModel.F1Studio ? "F1 — MEIO DE GRADE" : "F1 2008 — FUNDO DE GRADE";
        }

        static string GTTeamCarKmh(string team)
        {
            return GTCarsByTier[TeamTier(team)] switch
            {
                CarModel.Mustang => "GT — MUSTANG 250",
                CarModel.Titan => "GT — TITAN 283",
                CarModel.GtrNismo => "GT — GT-R 315",
                CarModel.Falcon => "GT — FALCON 330",
                CarModel.Ferrari => "GT — FERRARI 330",
                CarModel.R8 => "GT — R8 330",
                CarModel.Revuelto => "GT — REVUELTO 350",
                CarModel.McLarenP1 => "GT — P1 350",
                CarModel.Bugatti => "GT — BUGATTI 407",
                _ => "GT"
            };
        }

        /// <summary>Vantagem por força da equipe: -0,02 (mais fraca) a +0,08 (mais forte) no ritmo da IA.</summary>
        public static float SkillBiasFor(string driverName)
        {
            if (data == null) return 0f;
            string team = TeamOfDriver(driverName);
            if (string.IsNullOrEmpty(team)) return 0f;
            return (TeamTier(team) / 11f) * 0.10f - 0.02f;
        }

        // Calendário da temporada: 24 corridas, cada traçado aparece UMA vez (sem reuso).
        static readonly TrackId[] CalendarTracks =
        {
            TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.NightCity, TrackId.Velocita,
            TrackId.Principado, TrackId.Ardenne, TrackId.JardimDaBaia, TrackId.GolfoReal,
            TrackId.SerraDoCafe, TrackId.CircuitoDoSol, TrackId.ParqueReal, TrackId.DragaoOriental,
            TrackId.CornicheDaNoite, TrackId.PortoSul, TrackId.IlhaDasCascatas, TrackId.ValeDourado,
            TrackId.ColinasVerdes, TrackId.CampoDoNorte, TrackId.CurvasDoDanubio, TrackId.DunasDoNorte,
            TrackId.Fortaleza, TrackId.ColinasDoTexas, TrackId.EstadioCentral, TrackId.NeonStrip
        };

        /// <summary>Voltas OFICIAIS de cada sede do calendário F1 2026 (distância real ~305 km):
        /// Melbourne 58, Xangai 56, Suzuka 53, Sakhir 57, Jeddah 50, Miami 57, Montreal 70, Monaco 78,
        /// Barcelona 66, Red Bull Ring 71, Silverstone 52, Spa 44, Hungaroring 70, Zandvoort 72,
        /// Monza 53, Madri 66, Baku 51, Singapura 62, Austin 56, Mexico 71, Interlagos 71,
        /// Vegas 50, Lusail 57, Yas Marina 58 — mapeadas na ordem do nosso calendário.</summary>
        static readonly int[] CalendarLaps =
        {
            58, 56, 53, 57, 50, 57, 70, 78, 66, 71, 52, 44, 70, 72, 53, 66, 51, 62, 56, 71, 71, 50, 57, 58
        };

        /// <summary>6 finais de semana Sprint no calendário (formato F1 2026).</summary>
        public static bool SprintRound(int round) => round >= 0 && round < 24 && (round % 4) == 2;


        static readonly int[] F1Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        /// <summary>Pontos do Sprint (100 km): top 8 — 8,7,6,5,4,3,2,1 (regras F1 2026).</summary>
        static readonly int[] SprintPoints = { 8, 7, 6, 5, 4, 3, 2, 1 };

        /// <summary>Fases do fim de semana de corrida (formato F1).</summary>
        public const int PH_FP = 0;      // sexta: treino (120 min tradicional / 60 min em fim de semana Sprint — só TL1)
        public const int PH_FP3 = 1;     // sábado: TL3 (60 min) — só formato tradicional
        public const int PH_SQ1 = 2;     // sexta (Sprint): classif. sprint Q1 (12 min, elimina 8)
        public const int PH_SQ2 = 3;     // (10 min, elimina 6)
        public const int PH_SQ3 = 4;     // (8 min, define o grid da Sprint)
        public const int PH_SPRINT = 5;  // sábado (Sprint): corrida sprint (~100 km, pontos 8..1)
        public const int PH_Q1 = 6;      // sábado: classificação Q1 (18 min, elimina 8)
        public const int PH_Q2 = 7;      // Q2 (15 min, elimina 6)
        public const int PH_Q3 = 8;      // Q3 (13 min, define o grid do GP)
        public const int PH_RACE = 9;    // domingo: corrida (pontos F1)

        /// <summary>Rodadas com formato Sprint (0-based: etapa do meio).</summary>
        // [removido — duplicado; versão nova em cima]

        /// <summary>Grid da próxima sessão em ordem de largada (índice 0 = pole). Alimentado pela classificação.</summary>
        public static List<string> RaceGrid;

        /// <summary>Fase atual do fim de semana em andamento (para a UI de resultados).</summary>
        public static int CurrentPhase => data != null ? data.weekendPhase : -1;

        public static int TotalRounds => CalendarTracks.Length;

        /// <summary>Circuito da rodada (calendário fixo de 6 etapas).</summary>
        public static TrackId RoundTrack(int round)
        {
            return round >= 0 && round < CalendarTracks.Length ? CalendarTracks[round] : CalendarTracks[0];
        }

        /// <summary>Voltas da rodada (calendário fixo de 6 etapas).</summary>
        public static int RoundLaps(int round)
        {
            return round >= 0 && round < CalendarLaps.Length ? CalendarLaps[round] : CalendarLaps[0];
        }

        /// <summary>Nomes dos 9 rivais que correm a rodada atual (2 ficam de fora, rodízio por rodada).
        /// Preenchido em LaunchNextRace e usado pelo RaceBootstrap para nomear as IAs —
        /// garante que a classificação da corrida bata com a tabela da temporada.</summary>
        public static List<string> RaceRoster;

        static bool importingCloud;

        public static void Save()
        {
            if (data == null) return;
            data.saveVersion = 3;
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
                if (!importingCloud)
                {
                    if (data.titles > CareerStats.Titles) CareerStats.Titles = data.titles;
                    CareerStats.TouchUpdated();
                    if (AccountClient.IsLoggedIn)
                        AccountClient.PushFullState();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Carreira] falha ao salvar: " + ex.Message);
            }
        }

        public static string ExportRawJson()
        {
            try
            {
                if (data != null) return JsonUtility.ToJson(data);
                if (HasSave) return File.ReadAllText(SavePath);
            }
            catch { }
            return "";
        }

        public static void ImportRawJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var parsed = JsonUtility.FromJson<CareerData>(json);
                if (parsed == null || string.IsNullOrEmpty(parsed.pilotName)) return;
                importingCloud = true;
                data = parsed;
                if (data.standings == null) data.standings = new List<StandingEntry>();
                if (data.saveVersion < 3)
                {
                    data.weekendPhase = PH_FP;
                    data.fpDone = false;
                    data.sessionDeadlineUtc = 0;
                    data.gpGrid = 0;
                    data.sqGrid = 0;
                    data.qChain?.Clear();
                    data.qLocked?.Clear();
                    data.sqChain?.Clear();
                    data.sqLocked?.Clear();
                    data.pracNames?.Clear();
                    data.pracBest?.Clear();
                }
                MigrateStandings();
                data.saveVersion = 3;
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
                if (data.titles > CareerStats.Titles) CareerStats.Titles = data.titles;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Carreira] falha ao importar nuvem: " + ex.Message);
            }
            finally
            {
                importingCloud = false;
            }
        }

        public static void Load()
        {
            if (!HasSave)
            {
                data = null;
                return;
            }
            try
            {
                data = JsonUtility.FromJson<CareerData>(File.ReadAllText(SavePath));
                if (data.standings == null) data.standings = new List<StandingEntry>();
                bool needSave = false;
                if (data.saveVersion < 3)
                {
                    data.weekendPhase = PH_FP;
                    data.fpDone = false;
                    data.sessionDeadlineUtc = 0;
                    data.gpGrid = 0;
                    data.sqGrid = 0;
                    data.qChain?.Clear();
                    data.qLocked?.Clear();
                    data.sqChain?.Clear();
                    data.sqLocked?.Clear();
                    data.pracNames?.Clear();
                    data.pracBest?.Clear();
                    needSave = true;
                }
                MigrateStandings();
                if (needSave)
                {
                    data.saveVersion = 3;
                    Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Carreira] falha ao carregar: " + ex.Message);
                data = null;
            }
        }

        public static void Delete()
        {
            data = null;
            CareerData.raceActive = false;
            JustFinishedSeason = false;
            if (File.Exists(SavePath))
            {
                try { File.Delete(SavePath); } catch (Exception ex) { Debug.LogWarning("[Carreira] falha ao apagar: " + ex.Message); }
            }
        }

        /// <summary>Cria uma carreira vazia — será preenchida pelo quiz (ApplyQuiz).</summary>
        public static void StartCareer(string name, string nationality)
        {
            data = new CareerData();
            data.pilotName = name;
            data.nationality = nationality;
            data.category = "F2";
            data.season = 1;
            data.round = 0;
        }

        /// <summary>Guarda o caminho escolhido (GT ou fórmula) para o quiz aplicar no fim.</summary>
        public static bool GtPathChosen;

        public static void ChoosePath(bool gt) => GtPathChosen = gt;

        /// <summary>Aplica o resultado do quiz: código de 4 letras, arquétipo, academia e grid inicial.
        /// GtPathChosen=true → carreira GT: começa na equipe MAIS FRACA (ninguém conhece o jogador).
        /// false → fórmulas: F2 pela academia (fluxo antigo).</summary>
        public static void ApplyQuiz(int attack, int feel, int stage, int heat, string archetype, string academyId)
        {
            bool gt = GtPathChosen;
            if (data == null) StartCareer("Piloto", "Brasil");
            data.attack = attack;
            data.feel = feel;
            data.stage = stage;
            data.heat = heat;
            data.code = string.Concat(
                attack >= 0 ? "A" : "C",
                feel >= 0 ? "I" : "M",
                stage >= 0 ? "S" : "G",
                heat >= 0 ? "H" : "L");
            data.archetype = archetype;
            data.academyId = academyId;
            if (gt)
            {
                data.category = "GT";
                data.academyName = "";
                data.f2Team = GTTeams[0];
            }
            else
            {
                data.academyName = CareerQuizData.AcademyName(academyId);
                data.f2Team = CareerQuizData.AcademyTeam(academyId);
                data.category = "F2";
            }
            data.season = 1;
            data.round = 0;
            data.pointsPlayer = 0;
            data.seasonWins = 0;
            data.seasonPodiums = 0;
            data.careerWins = 0;
            data.careerPodiums = 0;
            data.titles = 0;
            data.promotedThisSeason = false;
            int teamIdx = Math.Max(0, Array.IndexOf(TeamsFor(data.category), data.f2Team));
            BuildGrid(TeamsFor(data.category), RivalNames, teamIdx);
            OfferEntryTeams();
            Save();
        }

        /// <summary>Mesa de propostas de entrada: as 3 equipes mais fracas da categoria (estreante não traz valor).</summary>
        static void OfferEntryTeams()
        {
            var teams = TeamsFor(data.category);
            bool gt = data.category == "GT";
            data.awaitingTeamChoice = true;
            data.teamOffers = gt
                ? new[] { teams[0], teams[1], teams[2] }
                : new[] { teams[teams.Length - 3], teams[teams.Length - 2], teams[teams.Length - 1] };
            data.f2Team = data.teamOffers[0];
        }

        /// <summary>Monta a tabela: 12 pilotos, 6 equipes × 2, jogador na equipe da academia no lugar de um fictício.</summary>
        static void BuildGrid(string[] teams, string[] names, int playerTeamIndex)
        {
            var list = new List<StandingEntry>();
            int nameIdx = 0;
            for (int t = 0; t < teams.Length; t++)
            {
                if (t == playerTeamIndex)
                {
                    list.Add(new StandingEntry { driverName = names[nameIdx++], team = teams[t], points = 0 });
                    list.Add(new StandingEntry { driverName = data.pilotName, team = teams[t], points = 0, isPlayer = true });
                }
                else
                {
                    list.Add(new StandingEntry { driverName = names[nameIdx++], team = teams[t], points = 0 });
                    list.Add(new StandingEntry { driverName = names[nameIdx++], team = teams[t], points = 0 });
                }
            }
            data.standings = list;
        }

        /// <summary>Prepara as configurações e carrega a sessão atual do fim de semana (formato F1: FP/Quali/Sprint/Corrida).</summary>
        public static void LaunchNextRace()
        {
            if (CareerData.raceActive || data == null) return;
            JustFinishedSeason = false;
            int phase = Mathf.Clamp(data.weekendPhase, PH_FP, PH_RACE);
            bool sprintWk = SprintRound(data.round);
            // Save de rodada não-Sprint preso em fases de Sprint? Normaliza para o fluxo do GP.
            if (!sprintWk && phase >= PH_SQ1 && phase <= PH_SPRINT) phase = PH_Q1 + Mathf.Min(2, phase - PH_SQ1);

            // Jogador eliminado na classificação? Fases seguintes sem ele são SIMULADAS (rivais,
            // grid real salvo) até a próxima sessão dele — como na F1, eliminado não corre Q2/Q3.
            int guard = 0;
            while (guard++ < 4 && (phase == PH_SQ2 || phase == PH_SQ3 || phase == PH_Q2 || phase == PH_Q3))
            {
                bool sq = phase <= PH_SQ3;
                var chain = sq ? data.sqChain : data.qChain;
                if (chain.Contains(data.pilotName)) break;
                SimQualiPhase(sq ? data.sqChain : data.qChain, sq ? data.sqLocked : data.qLocked, phase);
                data.weekendPhase = ++phase;
                if (phase == PH_SPRINT) FinalizeQualiGrid(true);
                else if (phase == PH_RACE) FinalizeQualiGrid(false);
            }

            // Treinos/classificação: SEM limite de voltas — termina só quando o tempo acaba
            // (com volta de cortesia: quem está na volta, completa). Corrida/sprint: por voltas.
            int laps;
            switch (phase)
            {
                case PH_FP: case PH_FP3:
                case PH_SQ1: case PH_SQ2: case PH_SQ3:
                case PH_Q1: case PH_Q2: case PH_Q3: laps = 999; break;
                case PH_SPRINT: laps = Mathf.Max(5, RoundLaps(data.round) / 4); break; // ~100 km real
                default: laps = RoundLaps(data.round); break;
            }

            // Tempos REAIS de 2026: treino 120 min na sexta tradicional (TL1+TL2) / 60 min no Sprint,
            // TL3 60 min no sábado, classificação eliminatória Q1/Q2/Q3 = 18/15/13, SQ = 12/10/8 min.
            // Relógio REAL (UTC): continua correndo no menu/hub; reabrir a sessão NÃO recomeça o tempo.
            float timeLimit;
            switch (phase)
            {
                case PH_FP: timeLimit = sprintWk ? 3600f : 7200f; break;
                case PH_FP3: timeLimit = 3600f; break;
                case PH_SQ1: timeLimit = 720f; break;
                case PH_SQ2: timeLimit = 600f; break;
                case PH_SQ3: timeLimit = 480f; break;
                case PH_Q1: timeLimit = 1080f; break;
                case PH_Q2: timeLimit = 900f; break;
                case PH_Q3: timeLimit = 780f; break;
                default: timeLimit = 0f; break;
            }
            if (timeLimit > 0f)
            {
                StartSessionClock(timeLimit);
                int left = SessionSecondsLeft();
                if (left > 0) timeLimit = left;
            }
            // Treinos e classificações saem do PIT (como na F1); corrida tem pit stop a partir de 4 voltas.
            bool pit = (phase != PH_SPRINT && phase != PH_RACE) || laps >= 4;

            // Q2/Q3 (e SQ2/SQ3): só os SOBREVIVENTES correm — eliminados ficam de fora (regra real).
            // Correntes vazias (save migrado no meio da classificação)? Rebaixa para a fase 1 da classificação.
            List<string> chainS = phase == PH_SQ2 || phase == PH_SQ3 ? data.sqChain
                                : phase == PH_Q2 || phase == PH_Q3 ? data.qChain : null;
            if (chainS != null && chainS.Count < 10)
            {
                phase = phase <= PH_SQ3 ? PH_SQ1 : PH_Q1;
                data.weekendPhase = phase;
                Save();
                chainS = null;
            }

            RaceSettings settings = new RaceSettings
            {
                mode = GameMode.SinglePlayer,
                track = RoundTrack(data.round),
                laps = laps,
                totalCars = chainS != null ? Mathf.Max(2, chainS.Count) : 24,
                difficulty = (Difficulty)Mathf.Clamp(data.difficulty, 0, 3),
                formulaOnly = data.category != "GT",
                careerPhase = phase,
                sessionTimeLimit = timeLimit,
                pitStops = pit,
                players = new[]
                {
                    new PlayerSetup
                    {
                        name = data.pilotName,
                        car = PlayerCar(),
                        colorIndex = 0
                    }
                }
            };
            CareerData.raceActive = true;
            if (chainS != null)
            {
                // Roster = só quem sobreviveu (o jogador incluído, se vivo); sai do pit, sem grid.
                RaceRoster = new List<string>();
                foreach (var n in chainS) if (n != data.pilotName) RaceRoster.Add(n);
                RaceGrid = null;
            }
            else
            {
                RaceRoster = new List<string>(RivalNames);
                // Grid real (resultado da classificação): mantém a ordem real se viva na sessão;
                // caso contrário reconstrói sinteticamente pela posição salva.
                if (phase == PH_SPRINT && data.sqGrid > 0 && (RaceGrid == null || RaceGrid.Count < 10 || !RaceGrid.Contains(data.pilotName)))
                    RaceGrid = GridFromStandings(data.sqGrid);
                if (phase == PH_RACE && data.gpGrid > 0 && (RaceGrid == null || RaceGrid.Count < 10 || !RaceGrid.Contains(data.pilotName)))
                    RaceGrid = GridFromStandings(data.gpGrid);
                if (phase == PH_FP || phase == PH_FP3 || phase == PH_SQ1 || phase == PH_Q1) RaceGrid = null;
            }
            Save();
            SceneLoader.LoadRace(settings);
        }

        /// <summary>Elimina por fase (Q1: 8, Q2: 6, final: 0) simulando os rivais pela habilidade (determinístico).</summary>
        static void SimQualiPhase(List<string> chain, List<string> locked, int phase)
        {
            int eliminate = phase == PH_SQ1 || phase == PH_Q1 ? 8 : phase == PH_SQ2 || phase == PH_Q2 ? 6 : 0;
            var rnd = new System.Random(data.season * 1000 + data.round * 31 + phase);
            var scored = new List<(string name, float t)>();
            foreach (var n in chain)
                if (n != data.pilotName) scored.Add((n, SkillBiasFor(n) + (float)rnd.NextDouble() * 0.12f));
            scored.Sort((a, b) => b.t.CompareTo(a.t));
            var order = new List<string>();
            foreach (var s in scored) order.Add(s.name);
            ApplyElimination(chain, locked, order, eliminate);
        }

        /// <summary>Aplica o corte da fase: sobreviventes ficam no chain, eliminados entram no fundo do grid (P11+/P17+).</summary>
        static void ApplyElimination(List<string> chain, List<string> locked, List<string> phaseOrder, int eliminate)
        {
            if (eliminate <= 0 || phaseOrder.Count <= eliminate)
            {
                chain.Clear();
                chain.AddRange(phaseOrder);
                return;
            }
            var cut = phaseOrder.Count - eliminate;
            locked.InsertRange(0, phaseOrder.GetRange(cut, eliminate)); // eliminados desta fase ficam À FRENTE dos de fases anteriores
            chain.Clear();
            chain.AddRange(phaseOrder.GetRange(0, cut));
        }

        /// <summary>Fim da classificação (simulada ou pulada): monta o grid completo e salva a posição do jogador.</summary>
        static void FinalizeQualiGrid(bool sprint)
        {
            var chain = sprint ? data.sqChain : data.qChain;
            var locked = sprint ? data.sqLocked : data.qLocked;
            var full = new List<string>(chain);
            full.AddRange(locked);
            foreach (var n in RivalNames) if (!full.Contains(n)) full.Add(n); // segurança: grid completo
            if (!full.Contains(data.pilotName)) full.Add(data.pilotName);    // eliminado em fase pulada = último
            RaceGrid = full;
            int pos = full.IndexOf(data.pilotName) + 1;
            if (sprint) data.sqGrid = pos; else data.gpGrid = pos;
            Save();
        }

        /// <summary>Grid sintético a partir da posição do piloto na classificação (rivais mantêm a ordem do roster).</summary>
        static List<string> GridFromStandings(int playerPos)
        {
            var grid = new List<string>();
            int idx = 0;
            for (int slot = 1; slot <= RaceRoster.Count + 1; slot++)
            {
                if (slot == playerPos) grid.Add(data.pilotName);
                else grid.Add(RaceRoster[idx++ % RaceRoster.Count]);
            }
            return grid;
        }

        /// <summary>Título da sessão atual (HUD/resultados).</summary>
        public static string SessionTitle(int phase)
        {
            switch (phase)
            {
                case PH_FP: return SprintRound(data?.round ?? 0) ? "TL1 — SEXTA (60 MIN)" : "TREINO LIVRE — SEXTA (120 MIN)";
                case PH_FP3: return "TL3 — SÁBADO (60 MIN)";
                case PH_SQ1: return "CLASSIF. SPRINT — SQ1 (12 MIN)";
                case PH_SQ2: return "CLASSIF. SPRINT — SQ2 (10 MIN)";
                case PH_SQ3: return "CLASSIF. SPRINT — SQ3 (8 MIN)";
                case PH_SPRINT: return "SPRINT — SÁBADO";
                case PH_Q1: return "CLASSIFICAÇÃO — Q1 (18 MIN)";
                case PH_Q2: return "CLASSIFICAÇÃO — Q2 (15 MIN)";
                case PH_Q3: return "CLASSIFICAÇÃO — Q3 (13 MIN)";
                default: return "CORRIDA — DOMINGO";
            }
        }

        /// <summary>Pula o treino de sexta sem rodar a sessão.</summary>
        public static void SkipPractice()
        {
            if (data == null || data.weekendPhase != PH_FP) return;
            bool sprintWk = SprintRound(data.round);
            if (sprintWk) { data.sqChain.Clear(); data.sqLocked.Clear(); data.weekendPhase = PH_SQ1; }
            else data.weekendPhase = PH_FP3;
            Save();
        }

        /// <summary>Pula a sessão atual do fim de semana. Classificação pulada = largada em último (regra real;
        /// fases restantes são simuladas para montar o grid). Sprint/corrida pulada = sem pontos; avança.</summary>
        public static void SkipSession()
        {
            if (data == null || CareerData.raceActive) return;
            ClearSessionClock();
            int phase = Mathf.Clamp(data.weekendPhase, PH_FP, PH_RACE);
            // PULAR RESPEITANDO O QUE FOI FEITO (regra do usuário): se existe SESSÃO VIVA da fase
            // atual (hub aberto por cima da cena) com voltas registradas, usa a ORDEM REAL dela
            // (por melhor volta) para o corte — o jogador entra classificado pela volta que fez,
            // e não "em último como se não tivesse corrido". Sem sessão viva: comportamento antigo.
            List<string> realOrder = null;
            var rm = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
            if (rm != null && rm.Settings.laps >= 900 && !string.IsNullOrEmpty(data.pilotName))
            {
                var fs = rm.FinalStandings();
                if (fs != null && fs.Count > 0 && fs.Exists(x => x.isHuman && x.bestLap > 0f))
                {
                    realOrder = new List<string>();
                    foreach (var e2 in fs) realOrder.Add(e2.name);
                }
            }
            // A sessão (se viva) ENCERRA aqui no save: sem isso o botão de sessão do hub fica morto
            // (LaunchNextRace retorna com raceActive=true) e a fase nunca é lançada de novo.
            CareerData.raceActive = false;
            switch (phase)
            {
                case PH_FP:
                    SkipPractice();
                    break;
                case PH_FP3:
                    data.fpDone = true;
                    data.qChain.Clear(); data.qLocked.Clear();
                    data.weekendPhase = PH_Q1;
                    Save();
                    break;
                case PH_SQ1:
                    data.sqChain.Clear();
                    foreach (var n in RivalNames) data.sqChain.Add(n);
                    data.sqLocked.Clear();
                    SimQualiPhase(data.sqChain, data.sqLocked, PH_SQ2);
                    SimQualiPhase(data.sqChain, data.sqLocked, PH_SQ3);
                    FinalizeQualiGrid(true);
                    data.weekendPhase = PH_SPRINT;
                    Save();
                    break;
                case PH_SQ2:
                    data.sqChain.Remove(data.pilotName);
                    SimQualiPhase(data.sqChain, data.sqLocked, PH_SQ2);
                    SimQualiPhase(data.sqChain, data.sqLocked, PH_SQ3);
                    FinalizeQualiGrid(true);
                    data.weekendPhase = PH_SPRINT;
                    Save();
                    break;
                case PH_SQ3:
                    data.sqChain.Remove(data.pilotName);
                    SimQualiPhase(data.sqChain, data.sqLocked, PH_SQ3);
                    FinalizeQualiGrid(true);
                    data.weekendPhase = PH_SPRINT;
                    Save();
                    break;
                case PH_SPRINT:
                    data.qChain.Clear(); data.qLocked.Clear();
                    data.weekendPhase = PH_Q1;
                    Save();
                    break;
                case PH_Q1:
                    if (realOrder != null && realOrder.Contains(data.pilotName))
                        ApplyElimination(data.qChain, data.qLocked, realOrder, 8); // volta REAL do jogador vale
                    else
                    {
                        data.qChain.Clear();
                        foreach (var n in RivalNames) data.qChain.Add(n);
                        data.qLocked.Clear();
                        SimQualiPhase(data.qChain, data.qLocked, PH_Q2);
                        SimQualiPhase(data.qChain, data.qLocked, PH_Q3);
                    }
                    FinalizeQualiGrid(false);
                    data.weekendPhase = PH_Q2;
                    Save();
                    break;
                case PH_Q2:
                    if (realOrder != null && realOrder.Contains(data.pilotName))
                        ApplyElimination(data.qChain, data.qLocked, realOrder, 6); // volta REAL do jogador vale
                    else
                    {
                        data.qChain.Remove(data.pilotName);
                        SimQualiPhase(data.qChain, data.qLocked, PH_Q2);
                        SimQualiPhase(data.qChain, data.qLocked, PH_Q3);
                    }
                    FinalizeQualiGrid(false);
                    data.weekendPhase = PH_Q3;
                    Save();
                    break;
                case PH_Q3:
                    if (realOrder != null && realOrder.Contains(data.pilotName))
                        ApplyElimination(data.qChain, data.qLocked, realOrder, 0); // grid pela ordem REAL
                    else
                    {
                        data.qChain.Remove(data.pilotName);
                        SimQualiPhase(data.qChain, data.qLocked, PH_Q3);
                    }
                    FinalizeQualiGrid(false);
                    data.weekendPhase = PH_RACE;
                    Save();
                    break;
                case PH_RACE:
                    data.weekendPhase = PH_FP;
                    data.fpDone = false;
                    data.gpGrid = 0;
                    data.sqGrid = 0;
                    data.sqChain.Clear(); data.sqLocked.Clear();
                    data.qChain.Clear(); data.qLocked.Clear();
                    data.round++;
                    Save();
                    if (data.round >= TotalRounds) FinishSeason();
                    break;
            }
        }


        /// <summary>Resultado de sessão do fim de semana: roteia por fase (FP não pontua; Quali define grid; Sprint 8..1; Corrida F1).</summary>
        public static void OnCareerSessionFinished(int position, int totalCars, List<string> finishingOrder)
        {
            if (!CareerData.raceActive || data == null) return;
            ClearSessionClock();
            int phase = Mathf.Clamp(data.weekendPhase, PH_FP, PH_RACE);
            bool sprintWk = SprintRound(data.round);
            CareerData.raceActive = false;

            switch (phase)
            {
                case PH_FP:
                    if (sprintWk) { data.sqChain.Clear(); data.sqLocked.Clear(); data.weekendPhase = PH_SQ1; }
                    else data.weekendPhase = PH_FP3;
                    Save();
                    break;
                case PH_FP3:
                    data.fpDone = true;
                    data.qChain.Clear(); data.qLocked.Clear();
                    data.weekendPhase = PH_Q1;
                    Save();
                    break;
                case PH_SQ1:
                    ApplyElimination(data.sqChain, data.sqLocked, OrderOrFallback(finishingOrder, totalCars), 8);
                    data.weekendPhase = PH_SQ2;
                    Save();
                    break;
                case PH_SQ2:
                    ApplyElimination(data.sqChain, data.sqLocked, OrderOrFallback(finishingOrder, totalCars), 6);
                    data.weekendPhase = PH_SQ3;
                    Save();
                    break;
                case PH_SQ3:
                    ApplyElimination(data.sqChain, data.sqLocked, OrderOrFallback(finishingOrder, totalCars), 0);
                    FinalizeQualiGrid(true);
                    data.weekendPhase = PH_SPRINT;
                    Save();
                    break;
                case PH_SPRINT:
                    {
                        int pts = position >= 1 && position <= SprintPoints.Length ? SprintPoints[position - 1] : 0;
                        data.pointsPlayer += pts;
                        foreach (StandingEntry e in data.standings) if (e.isPlayer) e.points = data.pointsPlayer;
                        if (finishingOrder != null && finishingOrder.Count > 0)
                        {
                            for (int i = 0; i < finishingOrder.Count && i < SprintPoints.Length; i++)
                            {
                                string name = finishingOrder[i];
                                if (string.IsNullOrEmpty(name) || name == data.pilotName) continue;
                                for (int s = 0; s < data.standings.Count; s++)
                                    if (!data.standings[s].isPlayer && data.standings[s].driverName == name)
                                    {
                                        data.standings[s].points += SprintPoints[i];
                                        break;
                                    }
                            }
                        }
                        SortStandings();
                        data.qChain.Clear(); data.qLocked.Clear();
                        data.weekendPhase = PH_Q1;
                        Save();
                    }
                    break;
                case PH_Q1:
                    ApplyElimination(data.qChain, data.qLocked, OrderOrFallback(finishingOrder, totalCars), 8);
                    data.weekendPhase = PH_Q2;
                    Save();
                    break;
                case PH_Q2:
                    ApplyElimination(data.qChain, data.qLocked, OrderOrFallback(finishingOrder, totalCars), 6);
                    data.weekendPhase = PH_Q3;
                    Save();
                    break;
                case PH_Q3:
                    ApplyElimination(data.qChain, data.qLocked, OrderOrFallback(finishingOrder, totalCars), 0);
                    FinalizeQualiGrid(false);
                    data.weekendPhase = PH_RACE;
                    Save();
                    break;
                default:
                    OnRaceFinished(position, totalCars, finishingOrder);
                    break;
            }
        }

        /// <summary>Ordem da fase: a real (melhor volta) se houver; senão grid de consentimento pelo roster.</summary>
        static List<string> OrderOrFallback(List<string> finishingOrder, int totalCars)
        {
            if (finishingOrder != null && finishingOrder.Count > 0) return finishingOrder;
            var order = new List<string>();
            if (RaceRoster != null) order.AddRange(RaceRoster);
            if (data != null && !order.Contains(data.pilotName)) order.Add(data.pilotName);
            return order;
        }

        /// <summary>CONTINUAR na tela de resultados: se o fim de semana tem próxima sessão, lança direto;
        /// retorna false quando deve voltar ao hub (fim de rodada/temporada).</summary>
        public static bool ContinueAfterSession()
        {
            if (data == null || JustFinishedSeason) return false;
            if (data.round >= TotalRounds) return false;
            // Garante o avanço mesmo se raceActive ficou preso true (nunca mais "CONTINUAR não faz nada").
            CareerData.raceActive = false;
            LaunchNextRace();
            return true;
        }

        /// <summary>Resultado da corrida: pontos, rivais, ordenação e fim de temporada.
        /// finishingOrder = nomes na ordem final de classificação da corrida (1º ao último);
        /// rivais pontuam pela posição REAL que terminaram (tabela F1).</summary>
        public static void OnRaceFinished(int position, int totalCars, List<string> finishingOrder = null)
        {
            if (!CareerData.raceActive || data == null) return;
            CareerData.raceActive = false;
            int pts = position >= 1 && position <= F1Points.Length ? F1Points[position - 1] : 0;
            data.pointsPlayer += pts;
            if (position == 1)
            {
                data.careerWins++;
                data.seasonWins++;
            }
            if (position <= 3)
            {
                data.careerPodiums++;
                data.seasonPodiums++;
            }

            if (finishingOrder != null && finishingOrder.Count > 0)
            {
                for (int i = 0; i < finishingOrder.Count && i < F1Points.Length; i++)
                {
                    string name = finishingOrder[i];
                    if (string.IsNullOrEmpty(name) || name == data.pilotName) continue;
                    for (int s = 0; s < data.standings.Count; s++)
                    {
                        if (!data.standings[s].isPlayer && data.standings[s].driverName == name)
                        {
                            data.standings[s].points += F1Points[i];
                            if (i == 0) data.standings[s].wins++;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Fallback (sem ordem de chegada): sorteio determinístico por rodada/temporada.
                var rnd = new System.Random(data.round * 31 + data.season);
                var rivalIdx = new List<int>();
                for (int i = 0; i < data.standings.Count; i++)
                    if (!data.standings[i].isPlayer) rivalIdx.Add(i);
                for (int i = rivalIdx.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    int tmp = rivalIdx[i];
                    rivalIdx[i] = rivalIdx[j];
                    rivalIdx[j] = tmp;
                }
                int count = Math.Min(6, rivalIdx.Count);
                for (int i = 0; i < count; i++)
                    data.standings[rivalIdx[i]].points += rnd.Next(0, 19);
            }

            foreach (StandingEntry e in data.standings)
                if (e.isPlayer) e.points = data.pointsPlayer;

            SortStandings();

            // Fim de semana reinicia: volta ao treino livre da próxima rodada.
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0;
            data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            data.round++;
            Save();
            if (data.round >= TotalRounds) FinishSeason();
        }

        /// <summary>REINICIAR CARREIRA: zera progresso (temporada, pontos, grid) mas PRESERVA o
        /// questionário (código, arquétipo, estatísticas, nome, nacionalidade) e a categoria.
        /// Jogador volta para a mesa de propostas para escolher equipe.</summary>
        public static void ResetCareerKeepProfile()
        {
            if (data == null) return;
            data.season = 1;
            data.round = 0;
            data.pointsPlayer = 0;
            data.seasonWins = 0;
            data.seasonPodiums = 0;
            data.careerWins = 0;
            data.careerPodiums = 0;
            data.titles = 0;
            data.promotedThisSeason = false;
            if (data.category != "GT") data.category = "F2";
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0;
            data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            CareerData.raceActive = false;
            JustFinishedSeason = false;
            OfferEntryTeams();
            data.standings = new List<StandingEntry>();
            Save();
        }

        /// <summary>Jogador assinou com a equipe escolhida na mesa de propostas.</summary>
        public static void SignWithTeam(string team)
        {
            if (data == null) return;
            data.f2Team = team;
            data.awaitingTeamChoice = false;
            BuildGrid(TeamsFor(data.category), RivalNames, Mathf.Max(0, Array.IndexOf(TeamsFor(data.category), team)));
            Save();
        }

        // ---------- RELÓGIO REAL DE SESSÃO (corre também no menu/hub) ----------
        /// <summary>Inicia (uma vez) o relógio REAL da sessão atual: deadline em UTC, sobrevive a cena/menus.</summary>
        public static void StartSessionClock(float seconds)
        {
            if (data == null) return;
            long now = DateTime.UtcNow.Ticks;
            if (data.sessionDeadlineUtc > now) return; // já correndo (reabrir sessão não recomeça o tempo)
            data.sessionDeadlineUtc = now + (long)seconds * TimeSpan.TicksPerSecond;
            Save();
        }

        /// <summary>Segundos restantes do relógio REAL da sessão (0 = expirou/sem sessão cronometrada).</summary>
        public static bool SessionClockActive => data != null && data.sessionDeadlineUtc > 0;

        public static int SessionSecondsLeft()
        {
            if (data == null || data.sessionDeadlineUtc <= 0) return 0;
            long t = (long)((data.sessionDeadlineUtc - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerSecond);
            return t > 0 ? (int)t : 0;
        }

        /// <summary>Encerrou/pulou a sessão: zera o relógio e as melhores voltas do treino (a próxima sessão inicia limpa).</summary>
        public static void ClearSessionClock()
        {
            if (data == null) return;
            bool changed = false;
            if (data.sessionDeadlineUtc != 0) { data.sessionDeadlineUtc = 0; changed = true; }
            if (data.pracNames.Count > 0) { data.pracNames.Clear(); data.pracBest.Clear(); changed = true; }
            if (changed) Save();
        }

        /// <summary>Aceleração do tempo (botão 5x do HUD): comprime o prazo UTC restante pelo fator — pular a minutagem restante.</summary>
        public static void CompressSessionClock(float factor)
        {
            if (data == null || data.sessionDeadlineUtc <= 0) return;
            long now = DateTime.UtcNow.Ticks;
            if (data.sessionDeadlineUtc <= now) return;
            data.sessionDeadlineUtc = now + (long)((data.sessionDeadlineUtc - now) / Mathf.Max(1.01f, factor));
            Save();
        }

        /// <summary>PRAZO VENCIDO (relógio real) notificado pelo RaceManager/testes: encerra a sessão
        /// da fase atual com a ordem de melhor volta gravada e AVANÇA a fase — o mesmo caminho de
        /// SkipSession (que já sabe respeitar voltas reais). É o método que o teste de lógica usa.</summary>
        public static void NotifySessionExpired()
        {
            if (data == null) return;
            data.sessionDeadlineUtc = 0;
            SkipSession();
        }

        /// <summary>Grava a melhor volta de um piloto no TREINO (sobrevive a menus/recarregar a sessão). Só salva se melhorou.</summary>
        public static void SavePracticeBest(string driver, float best)
        {
            if (data == null || string.IsNullOrEmpty(driver) || best <= 0f) return;
            int i = data.pracNames.IndexOf(driver);
            if (i >= 0)
            {
                if (best < data.pracBest[i]) { data.pracBest[i] = best; Save(); }
            }
            else { data.pracNames.Add(driver); data.pracBest.Add(best); Save(); }
        }

        /// <summary>Melhor volta gravada do piloto no treino em andamento (0 = não tem).</summary>
        public static float LoadPracticeBest(string driver)
        {
            if (data == null || string.IsNullOrEmpty(driver)) return 0f;
            int i = data.pracNames.IndexOf(driver);
            return i >= 0 ? data.pracBest[i] : 0f;
        }

        /// <summary>Save antigo (12 pilotos)? Reconstrói a tabela com 24, preservando pontos/vitórias do jogador.</summary>
        public static void MigrateStandings()
        {
            if (data == null || data.standings == null || data.standings.Count >= TrackBuilder.GridSlots) return;
            int pts = data.pointsPlayer, wins = data.careerWins, podiums = data.careerPodiums;
            BuildGrid(TeamsFor(data.category), RivalNames, Mathf.Max(0, Array.IndexOf(TeamsFor(data.category), data.f2Team)));
            foreach (var e in data.standings) if (e.isPlayer) e.points = pts;
            data.pointsPlayer = pts;
            data.careerWins = wins;
            data.careerPodiums = podiums;
            SortStandings();
            Save();
        }

        /// <summary>Ordena a tabela: pontos desc, depois vitórias (countback F1), jogador vence empates.</summary>
        static void SortStandings()
        {
            if (data == null) return;
            data.standings.Sort((a, b) =>
            {
                int cmp = b.points.CompareTo(a.points);
                if (cmp != 0) return cmp;
                cmp = b.wins.CompareTo(a.wins);
                if (cmp != 0) return cmp;
                if (a.isPlayer != b.isPlayer) return a.isPlayer ? -1 : 1;
                return 0;
            });
        }

        /// <summary>Encerra a temporada: promoção (top 3) e reinício do campeonato.</summary>
        static void FinishSeason()
        {
            int pos = 1;
            for (int i = 0; i < data.standings.Count; i++)
            {
                if (data.standings[i].isPlayer)
                {
                    pos = i + 1;
                    break;
                }
            }
            FinalPosition = pos;
            if (pos == 1) data.titles++;

            bool promoted = pos <= 3;
            bool wasF2 = data.category == "F2";
            if (promoted && wasF2)
            {
                data.category = "F1";
                data.promotedThisSeason = true;
                // Subiu para a F1: RECEBE PROPOSTAS — 3 piores equipes da F1 sempre;
                // top 3 com 2º/1º lugar ganha também 1 intermediária (desempenho abre portas).
                data.awaitingTeamChoice = true;
                var offers = new List<string> { F1Teams[9], F1Teams[10], F1Teams[11] };
                if (pos <= 2) offers.Add(F1Teams[5]);
                data.teamOffers = offers.ToArray();
                data.f2Team = data.teamOffers[0];
                BuildGrid(F1Teams, RivalNames, 0);
            }
            else if (promoted && data.category == "GT")
            {
                int t = TeamTier(data.f2Team);
                if (t < GTTeams.Length - 1)
                {
                    data.f2Team = GTTeams[t + 1];
                    data.promotedThisSeason = true;
                }
                else data.promotedThisSeason = false;
            }
            else
            {
                data.promotedThisSeason = false;
            }

            data.season++;
            data.round = 0;
            data.pointsPlayer = 0;
            data.seasonWins = 0;
            data.seasonPodiums = 0;
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0;
            data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            foreach (StandingEntry e in data.standings) { e.points = 0; e.wins = 0; }
            JustFinishedSeason = true;
            Save();
        }

        /// <summary>Limpa o flag de fim de temporada (consumido pela UI ao exibir a tela).</summary>
        public static void ConsumeSeasonEnd()
        {
            JustFinishedSeason = false;
        }
    }
}
