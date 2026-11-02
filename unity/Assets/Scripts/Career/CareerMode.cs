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

    /// <summary>Uma volta do JOGADOR na sessão atual (MINHAS VOLTAS no hub): setores e se foi limpa.</summary>
    [Serializable]
    public class LapRecord
    {
        public int lapNo;
        public float s1, s2, s3, total;
        public int c1, c2, c3; // cor do setor no momento (0 nenhuma, 1 roxo, 2 verde, 3 amarelo)
        public bool clean;
        public int compound = -1; // composto usado na volta (TyreModel: 0 S, 1 M, 2 H, 3 I, 4 W)
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
        public long sessionDeadlineUtc;     // fim da sessão em ticks UTC (relógio real enquanto a sessão está aberta)
        public long sessionPausedPlus1;     // PAUSADO: ticks restantes + 1 (0 = rodando — o padrão de qualquer save antigo).
                                             // Gravado no save: fora da sessão (menu, outro modo, jogo minimizado/fechado)
                                             // o relógio não anda.
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
        /// <summary>MOTOR (regra 2025: 4 motores por temporada; o 5º dá 10 posições no grid, os seguintes 5 cada).</summary>
        public float engineWear;            // desgaste do motor em uso (0..1)
        public int enginesUsed = 1;         // motores usados nesta temporada
        public int engineExtraCount;        // quantas vezes já passou do limite nesta temporada
        public int enginePenaltyPlaces;     // punição pendente para o grid da próxima corrida
        /// <summary>Classificação eliminatória: sobreviventes (melhor volta, melhor primeiro) + já eliminados (segmento do fundo do grid).</summary>
        public List<string> sqChain = new List<string>();
        public List<string> sqLocked = new List<string>();
        public List<string> qChain = new List<string>();
        public List<string> qLocked = new List<string>();
        /// <summary>Dificuldade das IAs na carreira (0 Fácil, 1 Médio, 2 Difícil, 3 Expert).</summary>
        public int difficulty = 1;
        /// <summary>Regras escolhidas no hub (AJUSTES): perda total / penalidades DESLIGADAS. Guardadas "invertidas" para o save
        /// antigo (sem o campo) continuar com as regras LIGADAS. Valem a partir da próxima sessão.</summary>
        public bool noTotalLoss;
        public bool noPenalties;
        /// <summary>Estratégia de pit escolhida no HUB para a próxima corrida/sprint (-1 = automática).</summary>
        public int stratStops = -1;
        public int stratStart = 1;
        public int stratStop = 2;
        /// <summary>Composto de CADA parada (índice 0 = 1ª parada). Vazio/curto = completa com stratStop.</summary>
        public List<int> stratStints = new List<int>();
        /// <summary>PLANO DE RITMO da corrida (0 equilibrado, 1 ataque no início, 2 ataque no meio, 3 ataque no fim,
        /// 4 cauteloso, 5 ataque total). A equipe usa para escolher pneus/paradas e lembra pelo rádio.</summary>
        public int stratPace;
        /// <summary>DADOS DO FIM DE SEMANA por composto (S, M, H, I, W): voltas limpas (soma/qtde/melhor) e desgaste por
        /// volta medido — base do plano da equipe (antes o pneu era pré-fixado). Chave = temporada-rodada.</summary>
        public string wkKey = "";
        public List<float> wkLapSum = new List<float>();
        public List<int> wkLapN = new List<int>();
        public List<float> wkBest = new List<float>();
        public List<float> wkWearSum = new List<float>();
        public List<int> wkWearN = new List<int>();
        /// <summary>Aviso no menu quando a EQUIPE mudou a estratégia durante a corrida (chuva, pista secando, pneu no limite).</summary>
        public string stratTeamNote = "";
        /// <summary>ORDEM DAS GARAGENS da temporada (vaga da equipe no mundo, 1ª = garagem 1): campeonato de
        /// construtores do ANO ANTERIOR, fixa a temporada toda (regra da F1). Vazia = 1ª temporada (criada).</summary>
        public List<int> garageSlotsF1 = new List<int>();
        public List<int> garageSlotsF2 = new List<int>();
        /// <summary>Treino/classificação: composto escolhido para a PRÓXIMA saída do box (-1 = programa da equipe).</summary>
        public int practiceCompound = -1;
        /// <summary>Voltas do jogador na sessão atual (chave = temporada/rodada/fase).</summary>
        public List<LapRecord> sessionLaps = new List<LapRecord>();
        public string sessionLapsKey = "";
        public string sessionLapsTitle = "";
        /// <summary>Resultado da ÚLTIMA sessão do fim de semana (aba SESSÃO do hub): "nome|carro|tempo/dif|melhor".</summary>
        public List<string> lastSessionRows = new List<string>();
        public string lastSessionTitle = "";
        /// <summary>MUNDO VIVO (F1 + F2, pilotos, equipes, mercado, notícias, imprensa, fãs...).</summary>
        public WorldState world = new WorldState();
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

        /// <summary>Rivais da categoria atual: do MUNDO VIVO (F1/F2) ou a lista fixa (GT / save sem mundo).</summary>
        static List<string> Rivals() => CareerWorld.Active ? CareerWorld.RivalsFor(data.category) : new List<string>(RivalNames);

        /// <summary>Tabela da categoria: do mundo (pilotos reais da categoria) ou a montagem antiga.</summary>
        static void BuildGridFor(string category, int playerTeamIndex)
        {
            if (CareerWorld.Active) data.standings = CareerWorld.BuildStandings(category);
            else BuildGrid(TeamsFor(category), RivalNames, playerTeamIndex);
        }

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
            category == "GT" ? GTTeams : CareerWorld.Active ? CareerWorld.TeamNames(category) : category == "F1" ? F1Teams : F2Teams;

        /// <summary>Índice da equipe na escada da categoria atual (0 = mais fraca) — usado pela cor
        /// da equipe (2 pilotos da mesma equipe compartilham a cor; cada equipe tem uma única cor).</summary>
        public static int TeamIndexOf(string team)
        {
            if (string.IsNullOrEmpty(team)) return 0;
            var teams = TeamsFor(data != null ? data.category : "F2");
            int i = Array.IndexOf(teams, team);
            return Mathf.Max(0, i);
        }

        /// <summary>Carro de cada equipe F1 (índice = força, 0 = mais fraca): 12 modelos, um por equipe,
        /// ordenados pela força REAL do carro (época, potência e peso — ver CarSpec.DataFor): o Lotus 77 de
        /// 1976 fica com a equipe mais fraca e o RB18 campeão de 2022 com a mais forte.
        /// Ordem por detalhe do modelo 3D (escolha de projeto) — a performance vem de TeamPerformanceFor.</summary>
        public static readonly CarModel[] F1CarsByTier = CarSpec.F1Lineup;

        /// <summary>Carro de cada equipe F2 (0 = mais fraca) — lista em CarSpec.F2Lineup.</summary>
        public static readonly CarModel[] F2CarsByTier = CarSpec.F2Lineup;

        /// <summary>Índice do time na escada da categoria atual (0 = mais fraca).</summary>
        public static int TeamTier(string team)
        {
            if (CareerWorld.Active) return CareerWorld.TierOf(data.category, team); // força dinâmica (carro evolui/regulamento)
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

        /// <summary>Box da garagem no fim de semana: FIXO por equipe (os 2 pilotos da mesma equipe
        /// ocupam boxes ADJACENTES) com ordem estável POR RODADA (equipe rotaciona entre rounds) —
        /// nunca pela posição de grid/qualificação. Treino de sexta, quali e corrida usam o MESMO box.</summary>
        public static int BoxForDriver(string driverName)
        {
            if (data == null || string.IsNullOrEmpty(driverName)) return 0;
            string team = TeamOfDriver(driverName);
            if (string.IsNullOrEmpty(team)) return 0;
            var teams = TeamsFor(data.category);
            // Garagem pela ordem de construtores do ano anterior (fixa na temporada) — antes girava a cada etapa.
            int rotated = GarageRank(team, teams);
            int mate = 0;
            if (data.standings != null)
            {
                bool seen = false;
                foreach (var e in data.standings)
                {
                    if (e == null || e.team != team) continue;
                    if (e.driverName == driverName) { mate = seen ? 1 : 0; break; }
                    seen = true;
                }
            }
            return rotated * 2 + mate;
        }

        /// <summary>Posição da equipe na fila de garagens (0 = 1ª garagem). Mundo vivo: construtores do ano anterior;
        /// na 1ª temporada (não existe "ano anterior" no jogo) a ordem é criada pela força dos carros.</summary>
        static int GarageRank(string team, string[] teams)
        {
            if (CareerWorld.Active)
            {
                string series = data.category == "F1" ? "F1" : "F2";
                var list = series == "F1" ? data.garageSlotsF1 : data.garageSlotsF2;
                if (list == null || list.Count == 0)
                {
                    list = CareerWorld.FirstSeasonGarageOrder(series);
                    if (series == "F1") data.garageSlotsF1 = list; else data.garageSlotsF2 = list;
                }
                var t = CareerWorld.TeamByName(team);
                if (t != null) { int i = list.IndexOf(t.slot); if (i >= 0) return i; }
            }
            return Mathf.Max(0, Array.IndexOf(teams, team));
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
            return F2CarsByTier[Mathf.Clamp(TeamTier(data.f2Team), 0, F2CarsByTier.Length - 1)];
        }

        /// <summary>Carro de qualquer piloto do grid (consulta a tabela da temporada).</summary>
        public static CarModel CarForDriver(string driverName)
        {
            if (data == null) return CarModel.F1;
            if (driverName == data.pilotName) return PlayerCar();
            var e = data.standings.Find(s => s.driverName == driverName);
            string team = e != null ? e.team : "";
            if (data.category == "F2") return F2CarsByTier[Mathf.Clamp(TeamTier(team), 0, F2CarsByTier.Length - 1)];
            if (data.category == "GT") return GTCarsByTier[TeamTier(team)];
            return F1CarsByTier[TeamTier(team)];
        }

        /// <summary>Rótulo do carro da equipe (para a tela de propostas).</summary>
        public static string TeamCarLabel(string team)
        {
            if (data == null) return "";
            if (data.category == "GT") return GTTeamCarKmh(team) + " KM/H";
            int tier = TeamTier(team);
            // F2: carro ÚNICO (Dallara F2 2024) — o que muda entre equipes é o nível da equipe (ajuste de 0 a 6%).
            if (data.category == "F2") return CarSpec.F2Name.ToUpperInvariant() + " — " + (tier >= 8 ? "PONTEIRA" : tier >= 4 ? "MEIO DE GRADE" : "FUNDO DE GRADE");
            var m = F1CarsByTier[Mathf.Clamp(tier, 0, F1CarsByTier.Length - 1)];
            string level = tier >= 8 ? "PONTEIRA" : tier >= 4 ? "MEIO DE GRADE" : "FUNDO DE GRADE";
            return CarSpec.CreateDefault(m).displayName.ToUpperInvariant() + " — " + level;
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
        /// <summary>Voltas da corrida/sprint da etapa (mesma regra do LaunchNextRace).</summary>
        public static int SessionLapsFor(int round, int phase) => phase == PH_SPRINT ? Mathf.Max(5, RoundLaps(round) / 4) : RoundLaps(round);

        public static int RoundLaps(int round)
        {
            return round >= 0 && round < CalendarLaps.Length ? CalendarLaps[round] : CalendarLaps[0];
        }

        /// <summary>Nomes dos 9 rivais que correm a rodada atual (2 ficam de fora, rodízio por rodada).
        /// Preenchido em LaunchNextRace e usado pelo RaceBootstrap para nomear as IAs —
        /// garante que a classificação da corrida bata com a tabela da temporada.</summary>
        public static List<string> RaceRoster;

        static bool importingCloud;

        /// <summary>Salvamento de MARCO (mudança de fase, resultado, escolha no hub): grava no disco,
        /// mostra o aviso e sincroniza na nuvem.</summary>
        public static void Save() => SaveInternal(false);

        /// <summary>Salvamento SILENCIOSO (durante a sessão: melhor volta do treino, relógio da sessão):
        /// grava só no disco, sem aviso na tela e sem subir para a nuvem a cada volta.</summary>
        public static void SaveQuiet() => SaveInternal(true);

        static float lastCloudPush = -999f;

        /// <summary>AUTOTESTE de lógica: com isto ligado NADA é gravado (disco nem nuvem) — o save do jogador nunca é tocado.</summary>
        public static bool SuppressSave;

        static void SaveInternal(bool quiet)
        {
            if (data == null || SuppressSave) return;
            RaceX.UI.SaveToast.EnsureInstance();
            data.saveVersion = 4;
            // 1) DISCO (o que importa): arquivo temporário + troca — nunca deixa o save pela metade.
            bool localOk;
            try
            {
                string json = JsonUtility.ToJson(data, true);
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
                localOk = true;
            }
            catch (Exception ex)
            {
                localOk = false;
                Debug.LogWarning("[Carreira] falha ao salvar no disco: " + ex.Message);
                // Último recurso: escrita direta (caso o Move falhe no sistema de arquivos do aparelho).
                try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); localOk = true; }
                catch (Exception ex2) { Debug.LogWarning("[Carreira] escrita direta também falhou: " + ex2.Message); }
            }
            if (importingCloud) return;
            // O aviso reflete SÓ o disco: falha de rede/nuvem não é "falha ao salvar progresso".
            if (!localOk) { RaceX.UI.SaveToast.Show(false); return; }
            if (!quiet) RaceX.UI.SaveToast.Show(true);
            // 2) NUVEM (melhor esforço, separada): erro aqui nunca vira "FALHA" na tela.
            try
            {
                if (data.titles > CareerStats.Titles) CareerStats.Titles = data.titles;
                CareerStats.TouchUpdated();
                bool due = !quiet || Time.realtimeSinceStartup - lastCloudPush > 120f;
                if (due && AccountClient.IsLoggedIn)
                {
                    lastCloudPush = Time.realtimeSinceStartup;
                    AccountClient.PushFullState();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Carreira] sincronização com a nuvem adiada: " + ex.Message);
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

        /// <summary>Nuvem está ATRÁS do progresso local? Compara (season, round, weekendPhase).</summary>
        public static bool LocalCareerAheadOf(string cloudJson)
        {
            if (string.IsNullOrEmpty(cloudJson)) return true;
            try
            {
                var cloud = JsonUtility.FromJson<CareerData>(cloudJson);
                if (cloud == null || string.IsNullOrEmpty(cloud.pilotName)) return true;
                CareerData local = data;
                if (local == null && HasSave)
                    local = JsonUtility.FromJson<CareerData>(File.ReadAllText(SavePath));
                if (local == null) return false;
                if (local.season != cloud.season) return local.season > cloud.season;
                if (local.round != cloud.round) return local.round > cloud.round;
                return local.weekendPhase > cloud.weekendPhase;
            }
            catch
            {
                return true;
            }
        }

        public static void ImportRawJson(string json)
        {
            RaceX.UI.SaveToast.EnsureInstance();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var parsed = JsonUtility.FromJson<CareerData>(json);
                if (parsed == null || string.IsNullOrEmpty(parsed.pilotName)) return;
                importingCloud = true;
                data = parsed;
                if (data.standings == null) data.standings = new List<StandingEntry>();
                if (data.saveVersion < 4)
                {
                    data.round = 0;
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
                data.saveVersion = 4;
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
            RaceX.UI.SaveToast.EnsureInstance();
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
                if (data.saveVersion < 4)
                {
                    data.round = 0;
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
                // Save antigo de fórmula sem MUNDO VIVO: cria o mundo mantendo a equipe e a categoria atuais.
                if (data.category != "GT" && !CareerWorld.Active && !data.awaitingTeamChoice)
                {
                    try
                    {
                        CareerWorld.EnsureWorld(F1Teams, F2Teams, RivalNames);
                        var keep = new Dictionary<string, int>();
                        foreach (var e in data.standings) keep[e.driverName] = e.points;
                        int pp = data.pointsPlayer;
                        data.standings = CareerWorld.BuildStandings(data.category);
                        foreach (var e in data.standings) e.points = e.isPlayer ? pp : (keep.TryGetValue(e.driverName, out var v) ? v : 0);
                        SortStandings();
                        needSave = true;
                    }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
                if (needSave)
                {
                    data.saveVersion = 4;
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
        public static void ApplyQuiz(int attack, int feel, int stage, int heat, string archetype, string academyId,
            float dLoyalty = 0f, float dAmbition = 0f, float dPassion = 0f, float dTemperament = 0f)
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
            // MUNDO VIVO (fórmula): F1 + F2 simuladas juntas, com o jogador criado pelo questionário.
            if (!gt) CareerWorld.Create(archetype, dLoyalty, dAmbition, dPassion, dTemperament, F1Teams, F2Teams, RivalNames);
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
        /// <summary>Começo do fim de semana: motor com 85%+ de desgaste é trocado pela equipe. Até 4 por temporada sem
        /// punição; do 5º em diante, punição no grid da corrida (10 posições na 1ª vez, 5 nas seguintes — regra 2025).</summary>
        static void EngineCheckAtWeekendStart()
        {
            if (data.engineWear < 0.85f) return;
            data.engineWear = 0f;
            data.enginesUsed++;
            string body = "A equipe instalou o motor nº " + data.enginesUsed + " da temporada (limite: 4 sem punição).";
            if (data.enginesUsed > 4)
            {
                data.enginePenaltyPlaces = data.engineExtraCount == 0 ? 10 : 5;
                data.engineExtraCount++;
                body += " Passou do limite: punição de " + data.enginePenaltyPlaces + " posições no grid desta corrida.";
            }
            if (CareerWorld.Active) CareerWorld.AddNews("voce", "Motor novo para " + data.pilotName, body, 74, "Engenharia");
        }

        /// <summary>Aplica a punição de motor no grid da corrida (o piloto cai N posições, limitado ao fim do grid).</summary>
        static void ApplyEnginePenalty()
        {
            if (data.enginePenaltyPlaces <= 0) return;
            int i = RaceGrid != null ? RaceGrid.IndexOf(data.pilotName) : -1;
            if (i >= 0)
            {
                int to = Mathf.Min(RaceGrid.Count - 1, i + data.enginePenaltyPlaces);
                RaceGrid.RemoveAt(i);
                RaceGrid.Insert(to, data.pilotName);
            }
            data.enginePenaltyPlaces = 0;
        }

        public static void LaunchNextRace()
        {
            if (CareerData.raceActive || data == null) return;
            JustFinishedSeason = false;
            int phase = Mathf.Clamp(data.weekendPhase, PH_FP, PH_RACE);
            bool sprintWk = SprintRound(data.round);
            if (phase == PH_FP) EngineCheckAtWeekendStart();
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
            // AUTOTESTE DA CARREIRA (autotest.txt career=1): sessões encurtadas (classificação em segundos, corrida em voltas).
            if (RaceX.Core.AutoTest.CareerMode)
            {
                if (laps < 900) laps = RaceX.Core.AutoTest.CareerLaps(laps);   // sessão por tempo continua por tempo (antes virava corrida de 5 voltas)
                if (timeLimit > 0f) { ClearSessionClock(); timeLimit = RaceX.Core.AutoTest.CareerSeconds(timeLimit); }
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
                totalLoss = !data.noTotalLoss,
                penalties = !data.noPenalties,
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
                RaceRoster = Rivals();
                // Grid real (resultado da classificação): mantém a ordem real se viva na sessão;
                // caso contrário reconstrói sinteticamente pela posição salva.
                if (phase == PH_SPRINT && data.sqGrid > 0 && (RaceGrid == null || RaceGrid.Count < 10 || !RaceGrid.Contains(data.pilotName)))
                    RaceGrid = GridFromStandings(data.sqGrid);
                if (phase == PH_RACE && data.gpGrid > 0 && (RaceGrid == null || RaceGrid.Count < 10 || !RaceGrid.Contains(data.pilotName)))
                    RaceGrid = GridFromStandings(data.gpGrid);
                if (phase == PH_RACE) ApplyEnginePenalty();
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
            // nunca nome vazio/duplicado no grid (vinha do abandono na classificação → "carros sem nome")
            var clean = new List<string>();
            foreach (var n in phaseOrder) if (!string.IsNullOrEmpty(n) && !clean.Contains(n)) clean.Add(n);
            phaseOrder = clean;
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
            foreach (var n in Rivals()) if (!full.Contains(n)) full.Add(n); // segurança: grid completo
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
            List<string> realOrder = LiveQualiOrder(phase);
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
                case PH_SQ1: case PH_SQ2: case PH_SQ3:
                case PH_Q1: case PH_Q2: case PH_Q3:
                    SkipQualiPhase(phase, realOrder);
                    break;
                case PH_SPRINT:
                    data.qChain.Clear(); data.qLocked.Clear();
                    data.weekendPhase = PH_Q1;
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
            data.stratStops = -1; data.stratTeamNote = ""; // estratégia do hub volta ao automático na próxima etapa
                    Save();
                    if (data.round >= TotalRounds) FinishSeason();
                    break;
            }
        }


        static bool IsQualiPhase(int phase) => phase >= PH_SQ1 && phase <= PH_SQ3 || phase >= PH_Q1 && phase <= PH_Q3;
        static bool SprintQuali(int phase) => phase >= PH_SQ1 && phase <= PH_SQ3;
        static int QualiCut(int phase) => phase == PH_SQ1 || phase == PH_Q1 ? 8 : phase == PH_SQ2 || phase == PH_Q2 ? 6 : 0;
        static float QualiSeconds(int phase) => phase == PH_SQ1 ? 720f : phase == PH_SQ2 ? 600f : phase == PH_SQ3 ? 480f
            : phase == PH_Q1 ? 1080f : phase == PH_Q2 ? 900f : 780f;

        /// <summary>Ordem FINAL da fase de classificação viva quando o jogador pula o resto dela (regra do usuário):
        /// a melhor volta do jogador fica FIXA (ele não volta mais à pista) e os rivais completam o tempo que falta.
        /// Rival com volta pode melhorar até ~0,6% proporcional ao tempo restante; rival ainda sem volta recebe um
        /// tempo estimado pela mediana do grid e pela força da equipe. Essas margens são valores de projeto (não medidos).
        /// Null = não há sessão viva ou o jogador não tem volta (pular = largar em último).</summary>
        static List<string> LiveQualiOrder(int phase)
        {
            if (!IsQualiPhase(phase) || string.IsNullOrEmpty(data.pilotName)) return null;
            var rm = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
            if (rm == null || rm.Settings.laps < 900) return null;
            var fs = rm.FinalStandings();
            if (fs == null || fs.Count == 0) return null;
            var me = fs.Find(x => x.isHuman);
            if (me == null || me.bestLap <= 0f) return null;
            var laps = new List<float>();
            foreach (var e in fs) if (e.bestLap > 0f) laps.Add(e.bestLap);
            laps.Sort();
            float median = laps[laps.Count / 2];
            float left = Mathf.Clamp01(SessionSecondsLeft() / QualiSeconds(phase));
            var timed = new List<(string name, float t)>();
            foreach (var e in fs)
            {
                if (e.isHuman) { timed.Add((data.pilotName, e.bestLap)); continue; }
                var rnd = new System.Random(data.season * 7919 + data.round * 131 + phase * 17 + StableHash(e.name));
                float t;
                if (e.bestLap > 0f) t = e.bestLap * (1f - (0.001f + 0.005f * (float)rnd.NextDouble()) * left);
                else t = median * (1f + 0.01f - SkillBiasFor(e.name) * 0.12f + 0.004f * (float)rnd.NextDouble());
                if (e.retired) t = e.bestLap > 0f ? e.bestLap : float.MaxValue;   // bateu: a volta feita vale, mas não melhora
                timed.Add((e.name, t));
            }
            timed.Sort((a, b) => a.t.CompareTo(b.t));
            var order = new List<string>();
            foreach (var x in timed) order.Add(x.name);
            return order;
        }

        static int StableHash(string s)
        {
            unchecked { int h = 23; if (s != null) foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; }
        }

        /// <summary>Pula a fase de classificação atual. Com volta: corta pela ordem simulada — passou no corte, vai
        /// para a PRÓXIMA fase (e a corre); eliminado ou fase final, as fases restantes são simuladas e o grid sai com
        /// a posição real. Sem volta: larga em último (as fases restantes são simuladas sem ele).</summary>
        static void SkipQualiPhase(int phase, List<string> liveOrder)
        {
            bool sprint = SprintQuali(phase);
            var chain = sprint ? data.sqChain : data.qChain;
            var locked = sprint ? data.sqLocked : data.qLocked;
            int first = sprint ? PH_SQ1 : PH_Q1, last = sprint ? PH_SQ3 : PH_Q3;
            int after = sprint ? PH_SPRINT : PH_RACE;
            if (liveOrder != null && liveOrder.Contains(data.pilotName))
            {
                ApplyElimination(chain, locked, liveOrder, QualiCut(phase));
                if (phase != last && chain.Contains(data.pilotName))
                {
                    data.weekendPhase = phase + 1;   // passou: corre a próxima fase
                    Save();
                    return;
                }
            }
            else
            {
                if (phase == first)
                {
                    chain.Clear();
                    foreach (var n in Rivals()) chain.Add(n);
                    locked.Clear();
                }
                else chain.Remove(data.pilotName);
                SimQualiPhase(chain, locked, phase);
            }
            for (int p = phase + 1; p <= last; p++) SimQualiPhase(chain, locked, p);
            FinalizeQualiGrid(sprint);
            data.weekendPhase = after;
            Save();
        }

        /// <summary>Texto do aviso do PULAR na classificação: o que vai acontecer com a volta que o jogador já tem.</summary>
        public static string SkipQualiPreview()
        {
            if (data == null) return "";
            int phase = data.weekendPhase;
            if (!IsQualiPhase(phase)) return "";
            var order = LiveQualiOrder(phase);
            if (order == null) return "Sem volta registrada nesta sessão: você larga em último.";
            int pos = order.IndexOf(data.pilotName) + 1;
            int cut = QualiCut(phase);
            string head = "Sua melhor volta vale e o resto da sessão é simulado: P" + pos + ".";
            bool lastPhase = phase == PH_SQ3 || phase == PH_Q3;
            if (lastPhase) return head + "\nVocê larga em " + pos + "º.";
            string next = phase == PH_SQ1 ? "SQ2" : phase == PH_SQ2 ? "SQ3" : phase == PH_Q1 ? "Q2" : "Q3";
            if (pos <= order.Count - cut) return head + "\nVocê passa para o " + next + ".";
            return head + "\nEliminado: você larga em " + pos + "º.";
        }

        /// <summary>Resultado de sessão do fim de semana: roteia por fase (FP não pontua; Quali define grid; Sprint 8..1; Corrida F1).</summary>
        public static void OnCareerSessionFinished(int position, int totalCars, List<string> finishingOrder)
        {
            if (!CareerData.raceActive || data == null) return;
            ClearSessionClock();
            int phase = Mathf.Clamp(data.weekendPhase, PH_FP, PH_RACE);
            bool sprintWk = SprintRound(data.round);
            if (phase == PH_FP) EngineCheckAtWeekendStart();
            CareerData.raceActive = false;
            try { CareerWorld.AfterSessionInjuryCheck(); } catch (Exception ex) { Debug.LogException(ex); }

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
                    CarOutAfterQualiPhase(PH_SQ1);
                    Save();
                    break;
                case PH_SQ2:
                    ApplyElimination(data.sqChain, data.sqLocked, OrderOrFallback(finishingOrder, totalCars), 6);
                    data.weekendPhase = PH_SQ3;
                    CarOutAfterQualiPhase(PH_SQ2);
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
                    CarOutAfterQualiPhase(PH_Q1);
                    Save();
                    break;
                case PH_Q2:
                    ApplyElimination(data.qChain, data.qLocked, OrderOrFallback(finishingOrder, totalCars), 6);
                    data.weekendPhase = PH_Q3;
                    CarOutAfterQualiPhase(PH_Q2);
                    Save();
                    break;
                case PH_Q3:
                    ApplyElimination(data.qChain, data.qLocked, OrderOrFallback(finishingOrder, totalCars), 0);
                    FinalizeQualiGrid(false);
                    data.weekendPhase = PH_RACE;
                    Save();
                    break;
                default:
                    // raceActive já foi baixado acima: o resultado entra pelo caminho SEM a trava (antes a corrida
                    // principal saía sem pontos e sem avançar a rodada — CONTINUAR relançava a MESMA corrida).
                    ApplyRaceResult(position, totalCars, finishingOrder);
                    break;
            }
        }

        // ======================================================================================
        //  AUTOTESTE DE LÓGICA (autotest.txt com selftest=1): carreira TEMPORÁRIA na memória, gravação desligada; no fim o
        //  save original volta intacto. Cada linha: "OK"/"FALHA" + o que foi conferido.
        // ======================================================================================
        public static void SelfTest(Action<string> log)
        {
            var saved = data; bool savedActive = CareerData.raceActive; var savedRoster = RaceRoster; var savedGrid = RaceGrid;
            SuppressSave = true;
            void R(string what, bool ok, string extra = "") => log((ok ? "OK    " : "FALHA ") + what + (extra.Length > 0 ? " — " + extra : ""));
            try
            {
                data = new CareerData { pilotName = "TESTE", nationality = "Brasil", category = "F2", archetype = "late_bloomer" };
                ResetCareerKeepProfile();
                SignWithTeam(data.teamOffers[0]);
                R("mundo da carreira criado", CareerWorld.Active);
                // [41] reiniciar perfil zera o mundo
                var w0 = data.world;
                CareerWorld.AddNews("voce", "MARCADOR_TESTE", "x", 1, "");
                CareerWorld.W.energy = 0.3f;
                ResetCareerKeepProfile();
                bool fresh = data.world != w0 && !data.world.news.Exists(n => n.headline == "MARCADOR_TESTE") && Mathf.Approximately(data.world.energy, 1f);
                R("[41] reiniciar perfil recria o mundo (notícias/energia zeradas)", fresh);
                SignWithTeam(data.teamOffers[0]);

                // [32]/[34] Q1: jogador P1 com PERDA TOTAL; um rival com nome vazio (bug antigo)
                var rivals = Rivals();
                data.round = 0; data.weekendPhase = PH_Q1; data.qChain.Clear(); data.qLocked.Clear();
                var order = new List<string> { data.pilotName };
                order.AddRange(rivals);
                CareerData.raceActive = true;
                PlayerCarTotaled = true;
                OnCareerSessionFinished(1, order.Count, order);
                bool noBlank = RaceGrid != null && !RaceGrid.Exists(string.IsNullOrEmpty);
                R("[32] Q1 P1 + perda total: pula para a corrida e larga atrás de quem correu o Q2", data.weekendPhase == PH_RACE && data.gpGrid == 16,
                    "fase=" + data.weekendPhase + " grid=P" + data.gpGrid + " (esperado P16)");
                R("[34] grid sem nome vazio e com todos os carros", noBlank && RaceGrid.Count == rivals.Count + 1, "carros no grid=" + (RaceGrid != null ? RaceGrid.Count : 0));
                R("[32] aviso da regra nos resultados", QualiNote.Length > 0, QualiNote);
                // Q1 sem perda total: segue para o Q2 normalmente
                data.weekendPhase = PH_Q1; data.qChain.Clear(); data.qLocked.Clear(); RaceGrid = null;
                CareerData.raceActive = true; PlayerCarTotaled = false;
                OnCareerSessionFinished(1, order.Count, order);
                R("[32] Q1 P1 sem batida: vai para o Q2", data.weekendPhase == PH_Q2, "fase=" + data.weekendPhase);
                // Q2: P3 com perda total → P10 no grid (atrás dos 9 que correram o Q3)
                var o2 = new List<string>(data.qChain);
                o2.Remove(data.pilotName); o2.Insert(2, data.pilotName);
                o2.Insert(5, "");   // nome vazio (bug antigo de quem deu PT na sessão) não pode chegar ao grid
                CareerData.raceActive = true; PlayerCarTotaled = true;
                OnCareerSessionFinished(3, o2.Count, o2);
                R("[32] Q2 P3 + perda total: larga em 10º", data.weekendPhase == PH_RACE && data.gpGrid == 10, "grid=P" + data.gpGrid);
                R("[34] nome vazio na sessão não vira carro sem nome no grid", RaceGrid != null && !RaceGrid.Exists(string.IsNullOrEmpty) && RaceGrid.Count == rivals.Count + 1, "carros=" + (RaceGrid != null ? RaceGrid.Count : 0));

                // Mundo: superlicença, lesões, energia, textos
                CareerWorld.SelfTest(log);

                // [10] Plano da equipe
                data.round = 0;
                var fcDry = new WeatherForecast { kind = WeatherKind.Dry, rainStart = -1f, rainEnd = -1f };
                string n1 = PitLaneSystem.TeamProposal(53, fcDry, 0, out int s1, out var c1);
                bool two = c1.Count > 0 && c1.Exists(x => x != s1);
                R("[10] plano da equipe sem dados: 2 compostos secos e paradas", two, n1);
                // dados do fim de semana: macio rápido E durando muito → o plano usa o macio
                for (int i = 0; i < 4; i++) { RecordCompoundWear(0, 0.02f); RecordCompoundWear(1, 0.035f); RecordCompoundWear(2, 0.025f); }
                for (int i = 0; i < 4; i++) { RecordSessionLap(25f, 25f, 25f, 75.0f, 0, 0, 0, true, 0); RecordSessionLap(25f, 25f, 25f, 76.2f, 0, 0, 0, true, 1); RecordSessionLap(25f, 25f, 25f, 76.8f, 0, 0, 0, true, 2); }
                string n2 = PitLaneSystem.TeamProposal(53, fcDry, 0, out int s2, out var c2);
                R("[10] com SEUS dados (macio rápido e durável) o plano usa o macio", s2 == 0 || c2.Contains(0), n2);
                string n3 = PitLaneSystem.TeamProposal(53, new WeatherForecast { kind = WeatherKind.HeavyRain, rainStart = -1f, rainEnd = -1f }, 0, out int s3, out _);
                R("[10] chuva forte prevista: largada de pneu de chuva", s3 == TyreModel.Wet, n3);
                string n4 = PitLaneSystem.TeamProposal(53, fcDry, 1, out int s4, out var c4);
                R("[10] plano de ritmo muda a conta (ataque no início)", n4.Contains("ATAQUE NO INÍCIO"), n4);

                // [33] relógio: hub e pista no mesmo tempo, com e sem 5x
                ClearSessionClock();
                RaceX.UI.HUD.SetFastForward(true);
                StartSessionClock(600f);
                ResumeSessionClock();
                RaceX.UI.HUD.SetFastForward(false);
                int g = SessionGameSecondsLeft();
                R("[33] sessão começa com 5x ligado e desliga: tempo continua ~600 s (antes virava ~3000)", Mathf.Abs(g - 600) <= 3, "restante=" + g + " s");
                RaceX.UI.HUD.SetFastForward(true);
                int g5 = SessionGameSecondsLeft();
                RaceX.UI.HUD.SetFastForward(false);
                R("[33] com 5x ligado o hub mostra o mesmo tempo de jogo", Mathf.Abs(g5 - 600) <= 5, "restante=" + g5 + " s");
                ClearSessionClock();
            }
            catch (Exception ex) { log("FALHA exceção no autoteste: " + ex); }
            finally
            {
                data = saved; CareerData.raceActive = savedActive; RaceRoster = savedRoster; RaceGrid = savedGrid;
                PlayerCarTotaled = false; PlayerOutReason = ""; QualiNote = "";
                SuppressSave = false;
                Time.timeScale = 1f;
            }
        }

        /// <summary>Carro do jogador com PERDA TOTAL na sessão que acabou (o HUD avisa antes de fechar a sessão).</summary>
        public static bool PlayerCarTotaled;
        /// <summary>Motivo da saída do jogador na sessão que acabou (RaceEntry.retireReason: "PT", "DSQ", "MOTOR"...).</summary>
        public static string PlayerOutReason = "";
        /// <summary>Aviso da classificação para o painel de resultados ("" = nenhum).</summary>
        public static string QualiNote = "";

        /// <summary>PERDA TOTAL NA CLASSIFICAÇÃO (regra da F1, Regulamento Esportivo art. 39): a volta feita vale; passou no
        /// corte mas o carro não tem conserto para a fase seguinte (chassi novo leva horas) → não marca tempo nela e é
        /// classificado ATRÁS de todos que marcaram tempo nessa fase (ex.: passou no Q1 e não correu o Q2 = P16).
        /// As fases restantes são simuladas e o grid sai pronto.</summary>
        static void CarOutAfterQualiPhase(int phase)
        {
            bool wrecked = PlayerCarTotaled;
            PlayerCarTotaled = false;
            QualiNote = "";
            if (!wrecked || data == null) return;
            bool sprint = SprintQuali(phase);
            int last = sprint ? PH_SQ3 : PH_Q3;
            if (phase >= last) return;
            var chain = sprint ? data.sqChain : data.qChain;
            var locked = sprint ? data.sqLocked : data.qLocked;
            if (!chain.Contains(data.pilotName)) return;   // eliminado nesta fase: já tem a posição dele
            int next = phase + 1;
            // fase seguinte sem o jogador marcar tempo: rivais simulados, ele no fim dos que correram
            {
                int eliminate = QualiCut(next);
                var rnd = new System.Random(data.season * 1000 + data.round * 31 + next);
                var scored = new List<(string name, float t)>();
                foreach (var n in chain)
                    if (n != data.pilotName) scored.Add((n, SkillBiasFor(n) + (float)rnd.NextDouble() * 0.12f));
                scored.Sort((a, b) => b.t.CompareTo(a.t));
                var order = new List<string>();
                foreach (var x in scored) order.Add(x.name);
                order.Add(data.pilotName);
                ApplyElimination(chain, locked, order, eliminate);
            }
            for (int p = next + 1; p <= last; p++) SimQualiPhase(chain, locked, p);
            FinalizeQualiGrid(sprint);
            data.weekendPhase = sprint ? PH_SPRINT : PH_RACE;
            int pos = sprint ? data.sqGrid : data.gpGrid;
            string nextName = next == PH_SQ2 ? "SQ2" : next == PH_SQ3 ? "SQ3" : next == PH_Q2 ? "Q2" : "Q3";
            // Motivo REAL (RaceEntry.retireReason): "PT" = perda total; MOTOR/FREIO/COMBUSTÍVEL = quebra. Antes toda saída
            // da sessão aparecia como "PERDA TOTAL", até quebra de motor.
            string why = string.IsNullOrEmpty(PlayerOutReason) || PlayerOutReason == "PT" ? "PERDA TOTAL"
                       : PlayerOutReason == "DSQ" ? "DESCLASSIFICADO" : "QUEBRA (" + PlayerOutReason.ToLowerInvariant() + ")";
            QualiNote = why + ": sua volta vale e você passou no corte, mas o carro não fica pronto para o " + nextName +
                        ". Sem tempo no " + nextName + " → classificado atrás de quem correu: largada em " + pos + "º.";
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
            ApplyRaceResult(position, totalCars, finishingOrder);
        }

        static void ApplyRaceResult(int position, int totalCars, List<string> finishingOrder)
        {
            if (data == null) return;
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
            // Mundo vivo: a outra categoria corre a sua rodada; forma, moral, evolução, rivalidades, imprensa, notícias.
            try { CareerWorld.OnGrandPrix(finishingOrder, position); } catch (Exception ex) { Debug.LogException(ex); }

            // Fim de semana reinicia: volta ao treino livre da próxima rodada.
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0;
            data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            data.round++;
            data.stratStops = -1; data.stratTeamNote = ""; // estratégia do hub volta ao automático na próxima etapa
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
            // Zera o resto do progresso da temporada (motor, punições, estratégia, sessões, garagens, treinos).
            data.engineWear = 0f; data.enginesUsed = 1; data.engineExtraCount = 0; data.enginePenaltyPlaces = 0;
            data.stratStops = -1; data.stratStints.Clear(); data.stratTeamNote = ""; data.practiceCompound = -1;
            data.sessionLaps.Clear(); data.sessionLapsKey = ""; data.sessionLapsTitle = "";
            data.lastSessionRows.Clear(); data.lastSessionTitle = "";
            data.garageSlotsF1.Clear(); data.garageSlotsF2.Clear();
            data.pracNames.Clear(); data.pracBest.Clear();
            data.sessionDeadlineUtc = 0; data.sessionPausedPlus1 = 0;
            // MUNDO NOVO (pedido do usuário: "após reiniciar o perfil ele mantém as mesmas informações de mundo"): pilotos,
            // equipes, mercado, notícias, e-mails, lesões, suspensões, rivalidades e seguidores voltam ao início. Do jogador
            // só fica o que veio do QUESTIONÁRIO (personalidade).
            if (data.category != "GT")
            {
                var oldP = CareerWorld.Active ? CareerWorld.Player : null;
                float loy = oldP != null ? oldP.loyalty : -1f, amb = oldP != null ? oldP.ambition : 0f, pas = oldP != null ? oldP.passion : 0f, tem = oldP != null ? oldP.temperament : 0f;
                CareerWorld.Create(string.IsNullOrEmpty(data.archetype) ? "late_bloomer" : data.archetype, 0f, 0f, 0f, 0f, F1Teams, F2Teams, RivalNames);
                var np = CareerWorld.Player;
                if (np != null && loy >= 0f) { np.loyalty = loy; np.ambition = amb; np.passion = pas; np.temperament = tem; }
            }
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
            if (CareerWorld.Active)
            {
                var t = CareerWorld.TeamByName(team);
                if (t != null) { var p = CareerWorld.Player; p.series = t.series; data.category = t.series; CareerWorld.TakeSeat(p, t, 2); }
            }
            BuildGridFor(data.category, Mathf.Max(0, Array.IndexOf(TeamsFor(data.category), team)));
            Save();
        }

        // ---------- RELÓGIO DA SESSÃO ----------
        // REGRA DO USUÁRIO: o relógio só anda com a CENA DA SESSÃO aberta e o jogo em primeiro plano (o hub por cima da
        // sessão conta — a sessão segue viva). Saiu da sessão (menu principal, outro modo, minimizou, fechou o jogo) →
        // pausa NA HORA; voltou para a sessão → continua exatamente de onde parou. O tempo de carregamento não conta.
        static long pausedRemainTicks
        {
            get => data != null ? data.sessionPausedPlus1 - 1 : -1;
            set { if (data != null) data.sessionPausedPlus1 = value < 0 ? 0 : value + 1; }
        }

        public static bool SessionClockPaused => pausedRemainTicks >= 0;

        public static void PauseSessionClock()
        {
            if (data == null || data.sessionDeadlineUtc <= 0 || SessionClockPaused) return;
            pausedRemainTicks = Math.Max(0, data.sessionDeadlineUtc - DateTime.UtcNow.Ticks);
            SaveQuiet();   // gravado: fechar o jogo com a sessão pausada não desconta tempo
        }

        public static void ResumeSessionClock()
        {
            if (!SessionClockPaused) return;
            if (data != null && data.sessionDeadlineUtc > 0)
                data.sessionDeadlineUtc = DateTime.UtcNow.Ticks + pausedRemainTicks;
            pausedRemainTicks = -1;   // antes de gravar: o save não pode ficar dizendo "pausado"
            SaveQuiet();
        }

        /// <summary>Inicia (uma vez) o relógio REAL da sessão atual: deadline em UTC, sobrevive a cena/menus.</summary>
        public static void StartSessionClock(float seconds)
        {
            if (data == null) return;
            long now = DateTime.UtcNow.Ticks;
            if (data.sessionDeadlineUtc > 0 && SessionClockPaused) return; // pausada: retoma quando a sessão começar na pista
            if (data.sessionDeadlineUtc > now) return;                       // já correndo (reabrir não recomeça o tempo)
            // Sessão nova: o relógio nasce PAUSADO com o tempo cheio e começa a andar quando a sessão começa (RaceManager)
            // — os minutos do carregamento da pista não são descontados.
            // 5x já ligado ao começar a sessão: o tempo REAL já nasce comprimido (sem isso, desligar o 5x depois
            // multiplicava o tempo por 5 — hub mostrava ~30 min com 3 min na pista e a sessão "acabava do nada").
            float ff = RaceX.UI.HUD.FastForwardOn ? 5f : 1f;
            long full = (long)(seconds / ff * TimeSpan.TicksPerSecond);
            data.sessionDeadlineUtc = now + full;
            pausedRemainTicks = full;
            SaveQuiet();
        }

        /// <summary>Segundos restantes do relógio REAL da sessão (0 = expirou/sem sessão cronometrada).</summary>
        public static bool SessionClockActive => data != null && data.sessionDeadlineUtc > 0;

        /// <summary>Segundos restantes em TEMPO DE JOGO (o que a pista mostra): real × 5 com o 5x ligado. Hub e pista usam
        /// este — antes o hub mostrava o real e a pista o de jogo (números diferentes).</summary>
        public static int SessionGameSecondsLeft() => SessionSecondsLeft() * (RaceX.UI.HUD.FastForwardOn ? 5 : 1);

        public static int SessionSecondsLeft()
        {
            if (SessionClockPaused) return (int)(pausedRemainTicks / TimeSpan.TicksPerSecond);
            if (data == null || data.sessionDeadlineUtc <= 0) return 0;
            long t = (long)((data.sessionDeadlineUtc - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerSecond);
            return t > 0 ? (int)t : 0;
        }

        /// <summary>Encerrou/pulou a sessão: zera o relógio e as melhores voltas do treino (a próxima sessão inicia limpa).</summary>
        public static void ClearSessionClock()
        {
            if (data == null) return;
            pausedRemainTicks = -1;
            bool changed = false;
            if (data.sessionDeadlineUtc != 0) { data.sessionDeadlineUtc = 0; changed = true; }
            if (data.pracNames.Count > 0) { data.pracNames.Clear(); data.pracBest.Clear(); changed = true; }
            if (changed) SaveQuiet();
        }

        /// <summary>Aceleração do tempo (botão 5x do HUD): comprime o prazo UTC restante pelo fator — pular a minutagem restante.</summary>
        public static void CompressSessionClock(float factor)
        {
            if (data == null || data.sessionDeadlineUtc <= 0) return;
            if (SessionClockPaused)
            {
                pausedRemainTicks = (long)(pausedRemainTicks / Mathf.Max(1.01f, factor));
                SaveQuiet();
                return;
            }
            long now = DateTime.UtcNow.Ticks;
            if (data.sessionDeadlineUtc <= now) return;
            data.sessionDeadlineUtc = now + (long)((data.sessionDeadlineUtc - now) / Mathf.Max(1.01f, factor));
            SaveQuiet();
        }

        /// <summary>Escala o tempo REAL restante da sessão pelo fator do relógio do jogo: 5 = tempo 5x
        /// (sobra 1/5 do real), 0,2 = voltou ao 1x (o real volta a valer o mesmo que o tempo de jogo
        /// restante — antes desligar o 5x não devolvia o relógio e a sessão encolhia).</summary>
        public static void ScaleSessionClock(float factor)
        {
            if (data == null || data.sessionDeadlineUtc <= 0 || factor <= 0.01f) return;
            if (SessionClockPaused)
            {
                pausedRemainTicks = (long)(pausedRemainTicks / factor);
                SaveQuiet();
                return;
            }
            long now = DateTime.UtcNow.Ticks;
            if (data.sessionDeadlineUtc <= now) return;
            data.sessionDeadlineUtc = now + (long)((data.sessionDeadlineUtc - now) / factor);
            SaveQuiet();
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
                if (best < data.pracBest[i]) { data.pracBest[i] = best; SaveQuiet(); }
            }
            else { data.pracNames.Add(driver); data.pracBest.Add(best); SaveQuiet(); }
        }

        static string CurrentSessionKey => data == null ? "" : data.season + "-" + data.round + "-" + data.weekendPhase;

        static void EnsureWeekendData()
        {
            string key = data.season + "-" + data.round;
            if (data.wkLapSum == null || data.wkKey != key || data.wkLapSum.Count != 5)
            {
                data.wkKey = key;
                data.wkLapSum = new List<float> { 0, 0, 0, 0, 0 }; data.wkLapN = new List<int> { 0, 0, 0, 0, 0 };
                data.wkBest = new List<float> { 0, 0, 0, 0, 0 };
                data.wkWearSum = new List<float> { 0, 0, 0, 0, 0 }; data.wkWearN = new List<int> { 0, 0, 0, 0, 0 };
            }
        }

        /// <summary>Desgaste por volta medido no treino/classificação (PitLaneSystem chama no fim de cada volta).</summary>
        public static void RecordCompoundWear(int compound, float wearPerLap)
        {
            if (data == null || compound < 0 || compound > 4 || wearPerLap <= 0f || wearPerLap > 0.25f) return;
            EnsureWeekendData();
            data.wkWearSum[compound] += wearPerLap; data.wkWearN[compound]++;
        }

        /// <summary>Ritmo médio (volta limpa) e vida estimada pelo desgaste medido; false = sem dados suficientes.</summary>
        public static bool WeekendCompound(int c, out float avgLap, out float best, out float lifeLaps, out int laps)
        {
            avgLap = 0f; best = 0f; lifeLaps = 0f; laps = 0;
            if (data == null || data.wkLapSum == null || data.wkLapSum.Count != 5 || data.wkKey != data.season + "-" + data.round) return false;
            laps = data.wkLapN[c];
            if (laps > 0) { avgLap = data.wkLapSum[c] / laps; best = data.wkBest[c]; }
            if (data.wkWearN[c] >= 2) lifeLaps = Mathf.Clamp(0.92f / (data.wkWearSum[c] / data.wkWearN[c]), 5f, 70f);
            return laps >= 2 || data.wkWearN[c] >= 2;
        }

        /// <summary>Registra uma volta completa do JOGADOR na sessão (MINHAS VOLTAS). Salva em silêncio.</summary>
        public static void RecordSessionLap(float s1, float s2, float s3, float total, int c1, int c2, int c3, bool clean, int compound = -1)
        {
            if (data == null || total <= 0f) return;
            if (data.sessionLaps == null) data.sessionLaps = new List<LapRecord>();
            string key = CurrentSessionKey;
            if (data.sessionLapsKey != key)
            {
                data.sessionLaps.Clear();
                data.sessionLapsKey = key;
                data.sessionLapsTitle = SessionTitle(data.weekendPhase) + " — " + RaceX.Track.TrackLibrary.Get(RoundTrack(data.round)).displayName.ToUpperInvariant();
            }
            if (clean && compound >= 0 && compound <= 4 && total > 10f)
            {
                EnsureWeekendData();
                data.wkLapSum[compound] += total; data.wkLapN[compound]++;
                if (data.wkBest[compound] <= 0f || total < data.wkBest[compound]) data.wkBest[compound] = total;
            }
            data.sessionLaps.Add(new LapRecord
            {
                lapNo = data.sessionLaps.Count + 1,
                s1 = s1, s2 = s2, s3 = s3, total = total,
                c1 = c1, c2 = c2, c3 = c3, clean = clean, compound = compound
            });
            if (data.sessionLaps.Count > 200) data.sessionLaps.RemoveAt(0);
            SaveQuiet();
        }

        /// <summary>Voltas do jogador na ÚLTIMA sessão com voltas (a atual, ou a que acabou de terminar
        /// até a próxima registrar a 1ª volta).</summary>
        public static List<LapRecord> CurrentSessionLaps()
        {
            if (data == null || data.sessionLaps == null) return new List<LapRecord>();
            return data.sessionLaps;
        }

        /// <summary>Nome da sessão das voltas mostradas em MINHAS VOLTAS.</summary>
        public static string SessionLapsTitle()
        {
            return data != null && !string.IsNullOrEmpty(data.sessionLapsTitle) ? data.sessionLapsTitle : "SESSÃO ATUAL";
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
            BuildGridFor(data.category, Mathf.Max(0, Array.IndexOf(TeamsFor(data.category), data.f2Team)));
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

            // MUNDO VIVO: mercado de verdade (propostas por desempenho, aposentadorias, novatos, equipes...).
            // Se o contrato do jogador acabou, a temporada só vira DEPOIS da escolha (SignWorldOffer) — datas coerentes.
            if (CareerWorld.Active)
            {
                bool choose = false;
                try { choose = CareerWorld.SeasonEnd(pos); } catch (Exception ex) { Debug.LogException(ex); }
                JustFinishedSeason = true;
                if (choose)
                {
                    data.awaitingTeamChoice = true;
                    var names = new List<string>();
                    foreach (var o in CareerWorld.W.offers) names.Add(o.teamName);
                    data.teamOffers = names.ToArray();
                    Save();
                    return;
                }
                AdvanceWorldSeason();
                return;
            }

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
            data.engineWear = 0f; data.enginesUsed = 1; data.engineExtraCount = 0; data.enginePenaltyPlaces = 0;
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

        /// <summary>Vira a temporada no mundo vivo: categoria/equipe do jogador vêm do mundo; tabela nova.</summary>
        static void AdvanceWorldSeason()
        {
            var p = CareerWorld.Player;
            var t = CareerWorld.TeamOf(p);
            string before = data.category;
            if (p != null && (p.series == "F1" || p.series == "F2")) data.category = p.series;
            if (t != null) data.f2Team = t.name;
            data.promotedThisSeason = before == "F2" && data.category == "F1";
            data.awaitingTeamChoice = false;
            data.season++;
            data.round = 0;
            data.engineWear = 0f; data.enginesUsed = 1; data.engineExtraCount = 0; data.enginePenaltyPlaces = 0;
            data.pointsPlayer = 0;
            data.seasonWins = 0;
            data.seasonPodiums = 0;
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0;
            data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            data.standings = CareerWorld.BuildStandings(data.category);
            Save();
        }

        /// <summary>Proposta escolhida na mesa do fim de ano (mundo vivo).</summary>
        public static void SignWorldOffer(int index)
        {
            if (data == null || !CareerWorld.Active) return;
            CareerWorld.SignOffer(index);
            AdvanceWorldSeason();
        }

        /// <summary>Troca de equipe no MEIO da temporada (proposta aceita): pontos ficam com o piloto.</summary>
        public static void OnPlayerChangedTeamMidseason(string newTeam, string swappedDriver, string oldTeam)
        {
            data.f2Team = newTeam;
            foreach (var e in data.standings)
            {
                if (e.isPlayer) e.team = newTeam;
                else if (e.driverName == swappedDriver) e.team = oldTeam;
            }
        }

        /// <summary>Jogador LESIONADO ou SUSPENSO: o fim de semana corre sem ele (reserva no carro, zero pontos).</summary>
        public static void SkipAbsentWeekend()
        {
            if (data == null || !CareerWorld.Active) return;
            var order = CareerWorld.SimulateMySeriesOrder();
            for (int i = 0; i < order.Count && i < F1Points.Length; i++)
                foreach (var s in data.standings)
                    if (!s.isPlayer && s.driverName == order[i]) { s.points += F1Points[i]; if (i == 0) s.wins++; break; }
            SortStandings();
            try { CareerWorld.OnGrandPrix(order, 0); } catch (Exception ex) { Debug.LogException(ex); }
            data.weekendPhase = PH_FP;
            data.fpDone = false;
            data.gpGrid = 0; data.sqGrid = 0;
            data.sqChain.Clear(); data.sqLocked.Clear();
            data.qChain.Clear(); data.qLocked.Clear();
            data.round++;
            data.stratStops = -1; data.stratTeamNote = "";
            Save();
            if (data.round >= TotalRounds) FinishSeason();
        }

        /// <summary>Limpa o flag de fim de temporada (consumido pela UI ao exibir a tela).</summary>
        public static void ConsumeSeasonEnd()
        {
            JustFinishedSeason = false;
        }
    }
}
