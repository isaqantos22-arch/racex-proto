using System;
using System.Collections.Generic;

namespace RaceX.Career
{
    // Dados do MUNDO VIVO da carreira (F1 + F2 simuladas juntas, temporada após temporada).
    // Tudo serializável pelo JsonUtility (campos públicos, listas e arrays — sem Dictionary).
    // Índices de atributo: CareerWorld.A_* (0 classificação … 9 marketing).

    /// <summary>Piloto do mundo (NPC ou o próprio jogador, id "player").</summary>
    [Serializable]
    public class WDriver
    {
        public string id = "";
        public string name = "";
        public string nat = "";
        public int birthYear;
        /// <summary>"F1", "F2", "RES" (reserva, sem vaga) ou "RET" (aposentado).</summary>
        public string series = "RES";
        /// <summary>Vaga (índice da equipe na categoria) — -1 sem equipe.</summary>
        public int team = -1;
        /// <summary>10 atributos 1–100 (ver CareerWorld.AttrNames).</summary>
        public int[] at = new int[10];
        public int peakAge = 29;
        public float devRate = 0.7f, declineRate = 0.2f, potential = 80f;
        public float loyalty = 0.5f, ambition = 0.5f, passion = 0.5f, temperament = 0.5f;
        /// <summary>Forma −1..1 (resultado acima/abaixo do carro) e moral 0..1.</summary>
        public float form, morale = 0.5f;
        /// <summary>Resultado da última temporada vs o carro (−1..1): pesa no mercado.</summary>
        public float lastPerf;
        /// <summary>Última temporada (número da carreira) do contrato atual.</summary>
        public int contractEnd;
        public bool newgen;
        public int comebacks;
        public int retiredSeason = -1;
        public int starts, wins, podiums, poles, titlesF1, titlesF2, f1Seasons;
        public string archetype = "";
        /// <summary>Histórico por temporada: "temporada|categoria|equipe|posição|pontos".</summary>
        public List<string> history = new List<string>();
        /// <summary>Nota (classificação + ritmo)/2 no fim de cada temporada — curva de carreira.</summary>
        public List<float> ratingHistory = new List<float>();
    }

    /// <summary>Equipe (vaga fixa por categoria; o NOME pode mudar quando a equipe é comprada).</summary>
    [Serializable]
    public class WTeam
    {
        public string series = "F2";
        public int slot;
        public string name = "";
        public string originalName = "";
        /// <summary>Nota do carro 0–100 (define a ordem de força e o carro 3D da temporada).</summary>
        public float car = 80f;
        /// <summary>works / grandee / privateer / ladder / backmarker.</summary>
        public string philosophy = "privateer";
        public float loyalty = 0.5f, patience = 0.5f, youthFocus = 0.5f, favouritism = 0.5f, nationalBias = 0.3f;
        public string nation = "";
        /// <summary>Orçamento 30–100: desenvolvimento do carro e salários.</summary>
        public float budget = 60f;
        /// <summary>Temporadas seguidas entre as 2 piores (compra/renome só depois de várias).</summary>
        public int badSeasons;
        public int lastRebrandSeason = -99;
        public string principal = "";
        public string engineer = "";
        public int points, wins;
    }

    /// <summary>Rivalidade entre dois pilotos (calor 0–1).</summary>
    [Serializable]
    public class WRivalry
    {
        public string a = "", b = "";
        public float heat;
        public string origin = "";
        public bool flared, nemesis;
        public int aheadA, aheadB, contacts;
    }

    [Serializable]
    public class WTeamRivalry
    {
        public string series = "", a = "", b = "";
        public float heat;
        public bool flared;
    }

    /// <summary>Notícia da BANDEJA (categoria: voce, corrida, titulo, rivalidade, mundo, mercado, diretoria, imprensa).</summary>
    [Serializable]
    public class WNews
    {
        public int season, round;
        public string cat = "", headline = "", body = "", from = "";
        public int importance;
        public bool read;
    }

    /// <summary>Proposta de contrato para o jogador.</summary>
    [Serializable]
    public class WOffer
    {
        public string series = "";
        public int team;
        public string teamName = "";
        public int years = 1;
        public float car;
        public bool midseason, number1;
        public int expiresRound;
        public string why = "";
    }

    /// <summary>Pergunta pendente da sala de imprensa (depois da corrida).</summary>
    [Serializable]
    public class WPress
    {
        public string situation = "", journalist = "", outlet = "", slant = "", question = "";
        public string[] options = new string[0];
        /// <summary>Tom de cada resposta: ousado, humilde, provocador, leal, frio.</summary>
        public int[] tones = new int[0];
        /// <summary>Promessa embutida na resposta ("" = nenhuma): vencer, podio, frente, titulo.</summary>
        public string[] promise = new string[0];
        public int season, round;
    }

    [Serializable]
    public class WPromise
    {
        public string kind = "", text = "", target = "";
        public int season, madeRound;
    }

    [Serializable]
    public class WChampion
    {
        public int season;
        public string series = "", driver = "", team = "";
        public int points;
    }

    /// <summary>Estado completo do mundo vivo + o "lado de fora da pista" do jogador.</summary>
    [Serializable]
    public class WorldState
    {
        public int version = 1;
        public int baseYear = 2026;
        public List<WDriver> drivers = new List<WDriver>();
        public List<WTeam> teams = new List<WTeam>();
        public List<WRivalry> rivalries = new List<WRivalry>();
        public List<WTeamRivalry> teamRivalries = new List<WTeamRivalry>();
        public List<WNews> news = new List<WNews>();
        public List<WChampion> champions = new List<WChampion>();
        /// <summary>Tabela da OUTRA categoria (a do jogador fica em CareerData.standings): "id|pontos|vitórias".</summary>
        public List<string> otherStandings = new List<string>();
        public int newgenSeq;

        // ---- Jogador fora da pista ----
        public float following = 800f, sentiment = 0.5f, rapport;
        /// <summary>Contagem de tons nas entrevistas: ousado, humilde, provocador, leal, frio.</summary>
        public int[] tones = new int[5];
        public List<WPromise> promises = new List<WPromise>();
        public WPress pendingPress;
        public bool hasPress;
        public List<WOffer> offers = new List<WOffer>();
        /// <summary>Academia: backed (apoiado), warned (advertido), dropped (dispensado), graduated (formado), none.</summary>
        public string academyStanding = "none";
        public int academyWarnings;
        public float boardConfidence = 0.55f;
        public int boardTarget = 6;
        public int boardMissStreak;
        public bool ultimatum;
        public int injuryRounds, banRounds;
        public string injuryText = "";
        public float licencePoints;
        public float energy = 1f;
        public int prepRound = -1;
        public string prepDone = "";
        public List<string> achievements = new List<string>();
        public string legacyTier = "Estreante";
        public bool number1;
        public bool releaseClause;
        public bool notRenewed;
        public int lastRegulationSeason = 1;
        public string nemesisId = "";
        /// <summary>Contatos do jogador na última corrida (nomes) — vindos do dano (CarDamage.CarHitCar).</summary>
        public List<string> raceContacts = new List<string>();
        /// <summary>Textos já usados ("chave|índice") — perguntas, manchetes e e-mails não se repetem até esgotar as variações.</summary>
        public List<string> usedText = new List<string>();
        public bool raceTotaled;
        public float raceTotaledG;
        public int racePenalties;
        /// <summary>Pontos na superlicença por infração (rodada absoluta em que foi dada / pontos) — vencem em 12 meses
        /// (24 rodadas), como na FIA.</summary>
        public List<int> licRound = new List<int>();
        public List<float> licPts = new List<float>();
        /// <summary>Pontos da sessão atual (somados no fim da corrida).</summary>
        public float raceLicencePts;
        /// <summary>Pior impacto do jogador na sessão: pico estimado de desaceleração (g reais), zona e velocidade ⊥ (km/h).</summary>
        public float sessionPeakG;
        public string sessionPeakWhat = "";
        public float sessionImpactLoad;
    }
}
