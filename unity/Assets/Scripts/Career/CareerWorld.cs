using System;
using System.Collections.Generic;
using System.Linq;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Career
{
    /// <summary>
    /// MUNDO VIVO da carreira: F1 e F2 simuladas juntas, ano após ano. Pilotos com 10 atributos, personalidade e
    /// potencial oculto; forma e moral por corrida; evolução e envelhecimento; aposentadoria, quedas, voltas e
    /// mercado com critério por tipo de equipe; diretoria, academia, imprensa, fãs, promessas, rivalidades,
    /// nêmesis, lesões, suspensões, eventos do mês, regulamento e compra de equipes; bandeja de notícias.
    /// Inspirado na análise de 10 temporadas do Pit Wall (Claude outputs/PITWALL_analise.md), SEM as estranhezas:
    /// F2 com limite de idade, ex-titular de F1 não desce para a F2, "não renovado" encerra mesmo o contrato,
    /// 1 volta da aposentadoria por piloto, compra de equipe só após anos ruins seguidos, recordes 1×/temporada,
    /// diretoria com consequência real.
    /// </summary>
    public static class CareerWorld
    {
        public const int A_QUALI = 0, A_PACE = 1, A_CONS = 2, A_TYRE = 3, A_WET = 4, A_CRAFT = 5, A_AGGR = 6, A_ADAPT = 7, A_EXP = 8, A_MKT = 9;
        public static readonly string[] AttrNames = { "Classificação", "Ritmo de corrida", "Consistência", "Gestão de pneus", "Chuva", "Disputa", "Agressividade", "Adaptação", "Experiência", "Marketing" };
        public const string PlayerId = "player";
        static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        static CareerData D => CareerMode.data;
        public static WorldState W => D != null ? D.world : null;
        /// <summary>Mundo ativo: carreira de fórmula com mundo criado (GT segue o modo antigo).</summary>
        public static bool Active => D != null && D.category != "GT" && W != null && W.drivers != null && W.drivers.Count > 20 && W.teams != null && W.teams.Count >= 24;
        public static int Year => (W != null ? W.baseYear : 2026) + Mathf.Max(0, (D != null ? D.season : 1) - 1);
        public static string OtherSeries => D != null && D.category == "F1" ? "F2" : "F1";

        // ---------------- Arquétipos (números do RaceX) ----------------
        struct Arch { public int[] at; public int peak; public float dev, dec, pot, loy, amb, pas, tem; }
        static Arch ArchOf(string key)
        {
            switch (key)
            {
                case "prodigy": return new Arch { at = new[] { 84, 81, 69, 71, 77, 72, 73, 84, 6, 73 }, peak = 27, dev = 0.95f, dec = 0.26f, pot = 95, loy = 0.35f, amb = 0.85f, pas = 0.74f, tem = 0.6f };
                case "aggressor": return new Arch { at = new[] { 79, 82, 66, 69, 74, 81, 88, 76, 12, 70 }, peak = 28, dev = 0.85f, dec = 0.22f, pot = 90, loy = 0.45f, amb = 0.7f, pas = 0.8f, tem = 0.8f };
                case "technician": return new Arch { at = new[] { 75, 78, 83, 82, 80, 74, 57, 78, 14, 62 }, peak = 31, dev = 0.8f, dec = 0.15f, pot = 88, loy = 0.72f, amb = 0.5f, pas = 0.7f, tem = 0.3f };
                case "media_darling": return new Arch { at = new[] { 77, 78, 72, 72, 74, 72, 68, 78, 10, 92 }, peak = 29, dev = 0.8f, dec = 0.2f, pot = 87, loy = 0.4f, amb = 0.66f, pas = 0.55f, tem = 0.55f };
                default: return new Arch { at = new[] { 74, 76, 76, 77, 76, 70, 64, 84, 8, 60 }, peak = 32, dev = 0.6f, dec = 0.12f, pot = 93, loy = 0.6f, amb = 0.55f, pas = 0.85f, tem = 0.4f };
            }
        }

        // ---------------- Utilitários ----------------
        static int Hash(string s) { unchecked { int h = (int)2166136261; foreach (char c in s ?? "") h = (h ^ c) * 16777619; return h & 0x7fffffff; } }
        static System.Random Rng(string key) => new System.Random(Hash(key + "|" + (D != null ? D.season : 0) + "|" + (D != null ? D.round : 0)));
        static float Normal(System.Random r, float mean, float sd)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }
        static float F(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static int Clamp100(float v) => Mathf.Clamp(Mathf.RoundToInt(v), 1, 100);

        public static WDriver Driver(string id) { if (W == null) return null; foreach (var d in W.drivers) if (d.id == id) return d; return null; }
        public static WDriver ByName(string name) { if (W == null) return null; foreach (var d in W.drivers) if (d.name == name) return d; return null; }
        public static WDriver Player => Driver(PlayerId);
        public static WTeam Team(string series, int slot) { if (W == null) return null; foreach (var t in W.teams) if (t.series == series && t.slot == slot) return t; return null; }
        public static WTeam TeamOf(WDriver d) => d == null || d.team < 0 ? null : Team(d.series, d.team);
        public static WTeam TeamByName(string name) { if (W == null) return null; foreach (var t in W.teams) if (t.name == name) return t; return null; }
        public static int Age(WDriver d) => Year - d.birthYear;
        public static float Rating(WDriver d) => (d.at[A_QUALI] + d.at[A_PACE]) / 2f;
        /// <summary>Nota geral (média de classificação, ritmo, consistência, pneus e disputa).</summary>
        public static float Overall(WDriver d) => (d.at[A_QUALI] + d.at[A_PACE] + d.at[A_CONS] + d.at[A_TYRE] + d.at[A_CRAFT]) / 5f;
        public static List<WDriver> InSeries(string series) => W.drivers.Where(d => d.series == series && d.team >= 0).OrderBy(d => d.team).ThenBy(d => d.id).ToList();
        public static List<WTeam> TeamsOf(string series) => W.teams.Where(t => t.series == series).OrderBy(t => t.slot).ToList();

        /// <summary>Posição de força do carro na categoria: 0 = melhor.</summary>
        public static int CarRank(WTeam t)
        {
            if (t == null) return 6;
            int r = 0;
            foreach (var o in W.teams) if (o.series == t.series && (o.car > t.car || (o.car == t.car && o.slot < t.slot))) r++;
            return r;
        }

        /// <summary>Tier 0 (mais fraca) … 11 (mais forte) — usado pelo carro 3D e pelo desempenho.</summary>
        public static int TierOf(string series, string teamName)
        {
            var t = W.teams.FirstOrDefault(x => x.series == series && x.name == teamName);
            if (t == null) return 0;
            int n = W.teams.Count(x => x.series == series);
            return Mathf.Clamp(n - 1 - CarRank(t), 0, n - 1);
        }

        public static string[] TeamNames(string series) => TeamsOf(series).Select(t => t.name).ToArray();

        /// <summary>Nomes dos rivais da categoria do jogador (grid da corrida), em pares por equipe.</summary>
        public static List<string> RivalsFor(string series) => InSeries(series).Where(d => d.id != PlayerId).Select(d => d.name).ToList();

        // ======================================================================================
        //  CRIAÇÃO
        // ======================================================================================

        /// <summary>Cria o mundo depois do questionário. Jogador entra na F2 sem equipe (escolhe nas propostas).</summary>
        public static void Create(string archetype, float dLoy, float dAmb, float dPas, float dTem, string[] f1Teams, string[] f2Teams, string[] rivalNames)
        {
            var w = new WorldState();
            D.world = w;
            var r = new System.Random(Hash(D.pilotName + archetype));
            // Equipes: slot 0 = mais forte na largada (mesma ordem das listas do jogo).
            string[] f1Phil = { "works", "works", "works", "grandee", "grandee", "privateer", "privateer", "privateer", "privateer", "backmarker", "backmarker", "backmarker" };
            string[] f2Phil = { "ladder", "ladder", "ladder", "ladder", "ladder", "ladder", "ladder", "ladder", "ladder", "backmarker", "backmarker", "backmarker" };
            string[] nations = { "GBR", "DEU", "AUT", "ITA", "GBR", "ITA", "GBR", "USA", "CHE", "FRA", "BRA", "ESP" };
            for (int s = 0; s < 2; s++)
            {
                string series = s == 0 ? "F1" : "F2";
                var names = s == 0 ? f1Teams : f2Teams;
                for (int i = 0; i < names.Length; i++)
                {
                    var t = new WTeam
                    {
                        series = series, slot = i, name = names[i], originalName = names[i],
                        car = s == 0 ? 93f - i * 1.15f + F(r, -0.4f, 0.4f) : 83f - i * 0.55f + F(r, -0.3f, 0.3f),
                        philosophy = s == 0 ? f1Phil[i] : f2Phil[i],
                        loyalty = F(r, 0.3f, 0.85f), patience = F(r, 0.3f, 0.85f), youthFocus = F(r, 0.2f, 0.9f),
                        favouritism = F(r, 0.2f, 0.85f), nationalBias = F(r, 0.1f, 0.8f), nation = nations[i % nations.Length],
                        budget = s == 0 ? 92f - i * 4.5f : 70f - i * 2f,
                        principal = Person(r), engineer = Person(r)
                    };
                    w.teams.Add(t);
                }
            }
            var used = new HashSet<string>();
            foreach (var n in rivalNames) used.Add(n);
            used.Add(D.pilotName);
            // F2: os 23 rivais históricos + 1 (o jogador assume uma vaga ao assinar).
            for (int i = 0; i < 24; i++)
            {
                string name = i < rivalNames.Length ? rivalNames[i] : NewName(r, used, out _);
                string nat = i < CareerWorldText.RivalNats.Length ? CareerWorldText.RivalNats[i] : "BRA";
                if (i >= rivalNames.Length) NewName(r, used, out nat);
                var d = Seed(r, "f2_" + i, name, nat, false, i / 2);
                d.series = "F2"; d.team = i / 2;
                d.contractEnd = 1 + r.Next(0, 2);
                w.drivers.Add(d);
            }
            // F1: 24 pilotos com experiência.
            for (int i = 0; i < 24; i++)
            {
                string name = NewName(r, used, out string nat);
                var d = Seed(r, "f1_" + i, name, nat, true, i / 2);
                d.series = "F1"; d.team = i / 2;
                d.contractEnd = 1 + r.Next(0, 3);
                w.drivers.Add(d);
            }
            // Reservas: veteranos e jovens sem vaga (candidatos do mercado).
            for (int i = 0; i < 10; i++)
            {
                string name = NewName(r, used, out string nat);
                var d = Seed(r, "res_" + i, name, nat, i < 5, 8 + (i % 4));
                d.series = "RES"; d.team = -1;
                w.drivers.Add(d);
            }
            // Jogador.
            var a = ArchOf(archetype);
            var p = new WDriver
            {
                id = PlayerId, name = D.pilotName, nat = NatCode(D.nationality), birthYear = Year - 18, series = "F2", team = -1,
                at = (int[])a.at.Clone(), peakAge = a.peak, devRate = a.dev, declineRate = a.dec, potential = a.pot,
                loyalty = Mathf.Clamp01(a.loy + dLoy), ambition = Mathf.Clamp01(a.amb + dAmb), passion = Mathf.Clamp01(a.pas + dPas), temperament = Mathf.Clamp01(a.tem + dTem),
                archetype = archetype, contractEnd = 1
            };
            w.drivers.Add(p);
            w.academyStanding = string.IsNullOrEmpty(D.academyId) || D.academyId == "independent" ? "none" : "backed";
            w.following = 600f + p.at[A_MKT] * 8f;
            ResetOtherStandings();
            AddNews("voce", "Bem-vindo ao paddock", "Questionário concluído: " + CareerQuizData.ArchetypeName(archetype) + ". O mundo está de olho em você.", 70, "Equipe RaceX");
        }

        static WDriver Seed(System.Random r, string id, string name, string nat, bool f1, int tierFromTop)
        {
            // tierFromTop 0 = melhor equipe. F1: nota 78–94; F2: 70–84.
            float baseQ = f1 ? 92f - tierFromTop * 1.1f : 82f - tierFromTop * 0.8f;
            int age = f1 ? r.Next(21, 36) : r.Next(18, 24);
            var d = new WDriver
            {
                id = id, name = name, nat = nat, birthYear = Year - age,
                peakAge = Mathf.Clamp(Mathf.RoundToInt(Normal(r, 29f, 1.8f)), 26, 33),
                devRate = F(r, 0.5f, 0.9f), declineRate = F(r, 0.15f, 0.35f),
                loyalty = F(r, 0.25f, 0.85f), ambition = F(r, 0.3f, 0.9f), passion = F(r, 0.35f, 0.92f), temperament = F(r, 0.15f, 0.85f)
            };
            float q = Normal(r, baseQ, 2.2f), pc = Normal(r, baseQ + 0.5f, 2.2f);
            d.at = new[]
            {
                Clamp100(q), Clamp100(pc), Clamp100(Normal(r, f1 ? 80f : 72f, 5f)), Clamp100(Normal(r, f1 ? 79f : 72f, 5f)),
                Clamp100(Normal(r, f1 ? 79f : 72f, 7f)), Clamp100(Normal(r, f1 ? 80f : 68f, 5f)), Clamp100(Normal(r, 66f, 10f)),
                Clamp100(Normal(r, f1 ? 80f : 74f, 6f)), f1 ? Clamp100((age - 19) * 7 + r.Next(0, 10)) : r.Next(5, 30), Clamp100(Normal(r, 64f, 12f))
            };
            d.potential = Mathf.Max(Rating(d) + F(r, 0f, f1 ? 4f : 12f), Rating(d));
            if (f1) d.f1Seasons = Mathf.Max(0, age - 21);
            return d;
        }

        static string NewName(System.Random r, HashSet<string> used, out string nat)
        {
            for (int k = 0; k < 400; k++)
            {
                var sn = CareerWorldText.Surnames[r.Next(CareerWorldText.Surnames.Length)].Split('|');
                string name = CareerWorldText.Initials[r.Next(CareerWorldText.Initials.Length)] + ". " + sn[0];
                if (used.Add(name)) { nat = sn[1]; return name; }
            }
            nat = "BRA";
            string fb = "P. Novo" + used.Count;
            used.Add(fb);
            return fb;
        }

        static string Person(System.Random r) => CareerWorldText.PeopleFirst[r.Next(CareerWorldText.PeopleFirst.Length)] + " " + CareerWorldText.PeopleLast[r.Next(CareerWorldText.PeopleLast.Length)];
        static string NatCode(string nat) => string.IsNullOrEmpty(nat) ? "BRA" : nat.Length <= 3 ? nat.ToUpperInvariant() : nat.Substring(0, 3).ToUpperInvariant();

        /// <summary>Save antigo (sem mundo): cria um a partir da carreira atual mantendo a equipe do jogador.</summary>
        public static void EnsureWorld(string[] f1Teams, string[] f2Teams, string[] rivalNames)
        {
            if (D == null || D.category == "GT" || Active) return;
            string cat = D.category, team = D.f2Team;
            Create(string.IsNullOrEmpty(D.archetype) ? "late_bloomer" : D.archetype, 0, 0, 0, 0, f1Teams, f2Teams, rivalNames);
            var p = Player;
            p.series = cat;
            var t = W.teams.FirstOrDefault(x => x.series == cat && x.name == team);
            if (t != null) TakeSeat(p, t, 1 + 1);
        }

        // ======================================================================================
        //  ASSINATURA / VAGAS
        // ======================================================================================

        /// <summary>Coloca o piloto na vaga mais fraca da equipe (o titular mais fraco vira reserva).</summary>
        public static void TakeSeat(WDriver d, WTeam t, int years)
        {
            if (d == null || t == null) return;
            var mates = W.drivers.Where(x => x.series == t.series && x.team == t.slot && x.id != d.id).OrderBy(x => Rating(x)).ToList();
            if (mates.Count >= 2)
            {
                var out1 = mates[0];
                out1.series = "RES"; out1.team = -1;
            }
            d.series = t.series;
            d.team = t.slot;
            d.contractEnd = D.season + Mathf.Max(1, years) - 1;
        }

        /// <summary>Monta a tabela da categoria do jogador a partir do mundo.</summary>
        public static List<StandingEntry> BuildStandings(string series)
        {
            var list = new List<StandingEntry>();
            foreach (var d in InSeries(series))
            {
                var t = TeamOf(d);
                list.Add(new StandingEntry { driverName = d.name, team = t != null ? t.name : "", points = 0, isPlayer = d.id == PlayerId });
            }
            return list;
        }

        static void ResetOtherStandings()
        {
            W.otherStandings.Clear();
            foreach (var d in InSeries(OtherSeries)) W.otherStandings.Add(d.id + "|0|0");
            foreach (var t in W.teams) { t.points = 0; t.wins = 0; }
        }

        // ======================================================================================
        //  CORRIDA (hooks em cena)
        // ======================================================================================

        static bool hooked;
        /// <summary>Chamado pelo RaceBootstrap numa corrida da carreira: zera e escuta contatos/perda total/punições.</summary>
        public static void BeginRace()
        {
            if (!Active) return;
            W.raceContacts.Clear();
            W.raceTotaled = false; W.raceTotaledG = 0f; W.racePenalties = 0;
            W.raceLicencePts = 0f; W.sessionPeakG = 0f; W.sessionPeakWhat = ""; W.sessionImpactLoad = 0f;
            RaceX.Car.CarAI.ProfileProvider = AiProfile;
            if (hooked) return;
            hooked = true;
            RaceX.Car.CarDamage.CarHitCar += (a, b, g) =>
            {
                if (!Active || a == null || b == null) return;
                string pa = a.name ?? "", pb = b.name ?? "";
                bool aPlayer = pa.StartsWith("Player"), bPlayer = pb.StartsWith("Player");
                if (!aPlayer && !bPlayer) return;
                string other = (aPlayer ? pb : pa).Replace("NPC_", "");
                if (!W.raceContacts.Contains(other)) W.raceContacts.Add(other);
            };
            RaceX.Car.CarDamage.CarTotaled += c =>
            {
                if (!Active || c == null || !(c.name ?? "").StartsWith("Player")) return;
                W.raceTotaled = true;
                W.raceTotaledG = c.damage != null ? c.damage.LastImpactG : 60f;
            };
            RaceControl.Notice += (e, msg) =>
            {
                if (!Active || e == null || !e.isHuman || msg == null) return;
                if (msg.StartsWith(RaceControl.PenaltyPrefix)) { W.racePenalties++; W.raceLicencePts += LicencePointsFor(msg); }
            };
            RaceX.Car.CarDamage.Impact += (c, g, zone, wall) =>
            {
                if (!Active || c == null || !(c.name ?? "").StartsWith("Player")) return;
                float peak = PeakG(g, zone);
                W.sessionImpactLoad += peak;
                if (peak > W.sessionPeakG)
                {
                    W.sessionPeakG = peak;
                    float vKmh = Mathf.Sqrt(Mathf.Max(0f, g) * 9.81f) * 3.6f;
                    string where = zone == 0 ? "de frente" : zone == 1 ? "de traseira" : zone <= 3 ? "lateral" : "de roda";
                    W.sessionPeakWhat = "batida " + where + (wall ? " no muro" : " com outro carro") + " a ~" + Mathf.RoundToInt(vKmh) + " km/h (perpendicular)";
                }
            };
        }

        /// <summary>Perfil da IA vindo dos atributos (ritmo, agressividade, consistência, pneus, chuva, forma).</summary>
        static bool AiProfile(string name, out float skill, out float aggression, out float consistency, out float tyre, out float wet)
        {
            skill = 1f; aggression = 0.5f; consistency = 0.75f; tyre = 0.5f; wet = 1f;
            if (!Active) return false;
            var d = ByName(name);
            if (d == null) return false;
            float r = Rating(d);
            skill = Mathf.Clamp(0.93f + (r - 72f) / 24f * 0.11f + d.form * 0.012f + (d.morale - 0.5f) * 0.01f, 0.88f, 1.07f);
            aggression = Mathf.Clamp01((d.at[A_AGGR] * 0.65f + d.at[A_CRAFT] * 0.35f) / 100f);
            consistency = Mathf.Clamp(d.at[A_CONS] / 100f - (d.temperament - 0.5f) * 0.08f, 0.4f, 1f);
            tyre = Mathf.Clamp01((d.at[A_TYRE] - 50f) / 45f);
            wet = Mathf.Clamp(0.93f + d.at[A_WET] / 100f * 0.09f, 0.9f, 1.03f);
            return true;
        }

        // ======================================================================================
        //  FIM DE GP
        // ======================================================================================

        /// <summary>Fim do GP do jogador (ordem real da corrida; playerPos 0 = ausente).</summary>
        public static void OnGrandPrix(List<string> order, int playerPos)
        {
            if (!Active) return;
            int round = D.round; // já é a rodada concluída (chamado antes do round++)
            string mySeries = D.category;
            var me = Player;
            // 1) Ordem da categoria do jogador (nomes → pilotos).
            var mine = new List<WDriver>();
            if (order != null) foreach (var n in order) { var d = n == D.pilotName ? me : ByName(n); if (d != null && !mine.Contains(d)) mine.Add(d); }
            foreach (var d in InSeries(mySeries)) if (!mine.Contains(d) && (d != me || playerPos > 0)) mine.Add(d);
            if (playerPos > 0 && !mine.Contains(me)) mine.Insert(Mathf.Clamp(playerPos - 1, 0, mine.Count), me);
            // 2) A outra categoria é simulada pelos atributos.
            var other = SimOrder(OtherSeries, "gp" + round, out var dnfOther);
            AddPoints(other, dnfOther);
            // 3) Estatísticas, forma/moral, evolução.
            ApplyResults(mySeries, mine, new HashSet<WDriver>());
            ApplyResults(OtherSeries, other, dnfOther);
            // 4) Rivalidades e guerras de equipes.
            Rivalries(mine, true);
            Rivalries(other, false);
            TeamWars(mySeries, round);
            TeamWars(OtherSeries, round);
            DecayRivalries();
            // 5) Notícias de corrida.
            RaceNews(mySeries, mine, round);
            RaceNews(OtherSeries, other, round);
            // 6) Jogador: imprensa, promessas, fãs, diretoria, licença, lesão.
            if (playerPos > 0) PlayerAfterRace(mine, playerPos, round);
            else PlayerAbsentRound();
            // Proposta do meio do ano expira (vale 3 rodadas).
            foreach (var o in W.offers.Where(x => x.midseason && round >= x.expiresRound).ToList())
            {
                W.offers.Remove(o);
                AddNews("voce", "A proposta da " + o.teamName + " expirou", "Sem resposta, a equipe seguiu com quem já tinha.", 52, "");
            }
            MonthlyEvent(round);
            Records(round);
            TitleFight(round);
            Legacy();
            if (W.news.Count > 320) W.news.RemoveRange(0, W.news.Count - 320);
        }

        /// <summary>GP da categoria do jogador simulado SEM ele (lesão/suspensão): nomes na ordem de chegada.</summary>
        public static List<string> SimulateMySeriesOrder()
        {
            var o = SimOrder(D.category, "absent" + D.round, out _);
            return o.Where(d => d.id != PlayerId).Select(d => d.name).ToList();
        }

        /// <summary>Simula a ordem de uma corrida: carro 55% + piloto 45% + forma + ruído (consistência); erros = abandono.</summary>
        static List<WDriver> SimOrder(string series, string key, out HashSet<WDriver> dnf)
        {
            var r = Rng("sim" + series + key);
            var list = InSeries(series).Where(d => d.id != PlayerId || series != D.category).ToList();
            dnf = new HashSet<WDriver>();
            var score = new Dictionary<WDriver, float>();
            float wet = (float)r.NextDouble() < 0.18f ? F(r, 0.3f, 1f) : 0f;
            foreach (var d in list)
            {
                var t = TeamOf(d);
                float drv = Rating(d) * (1f - wet * 0.3f) + d.at[A_WET] * wet * 0.3f;
                float s = (t != null ? t.car : 80f) * 0.55f + drv * 0.45f + d.form * 1.2f + (d.morale - 0.5f) * 1.2f;
                s += Normal(r, 0f, 0.9f + (100 - d.at[A_CONS]) * 0.045f + d.temperament * 0.3f);
                float err = 0.025f + (100 - (d.at[A_CONS] * 0.7f + d.at[A_EXP] * 0.3f)) * 0.0012f;
                err *= 1f + (d.at[A_AGGR] - 60) / 220f;
                if ((float)r.NextDouble() < err + 0.012f) dnf.Add(d);
                score[d] = s;
            }
            var dnfSet = dnf;
            var ordered = list.OrderByDescending(d => dnfSet.Contains(d) ? -999f + score[d] : score[d]).ToList();
            return ordered;
        }

        static void AddPoints(List<WDriver> order, HashSet<WDriver> dnf)
        {
            for (int i = 0; i < order.Count && i < Points.Length; i++)
            {
                if (dnf.Contains(order[i])) continue;
                AddOtherPoints(order[i].id, Points[i], i == 0 ? 1 : 0);
                var t = TeamOf(order[i]); if (t != null) { t.points += Points[i]; if (i == 0) t.wins++; }
            }
        }

        static void AddOtherPoints(string id, int pts, int wins)
        {
            for (int i = 0; i < W.otherStandings.Count; i++)
            {
                var p = W.otherStandings[i].Split('|');
                if (p[0] != id) continue;
                W.otherStandings[i] = id + "|" + (int.Parse(p[1]) + pts) + "|" + (int.Parse(p[2]) + wins);
                return;
            }
            W.otherStandings.Add(id + "|" + pts + "|" + wins);
        }

        /// <summary>Tabela da outra categoria: (piloto, pontos, vitórias) ordenada.</summary>
        public static List<(WDriver d, int pts, int wins)> OtherTable()
        {
            var list = new List<(WDriver, int, int)>();
            foreach (var s in W.otherStandings)
            {
                var p = s.Split('|');
                var d = Driver(p[0]);
                if (d != null && d.series == OtherSeries) list.Add((d, int.Parse(p[1]), int.Parse(p[2])));
            }
            return list.OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3).ToList();
        }

        /// <summary>Forma, moral e evolução na temporada (resultado comparado ao esperado para o carro).</summary>
        static void ApplyResults(string series, List<WDriver> order, HashSet<WDriver> dnf)
        {
            int n = Mathf.Max(2, order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var d = order[i];
                var t = TeamOf(d);
                d.starts++;
                bool out1 = dnf.Contains(d);
                if (!out1 && i == 0) d.wins++;
                if (!out1 && i < 3) d.podiums++;
                float expected = CarRank(t) * 2f + 1.5f;
                float delta = Mathf.Clamp((expected - (i + 1)) / (n - 1f), -1f, 1f);
                if (out1) delta = -0.45f;
                d.form = Mathf.Clamp(d.form * 0.6f + delta * 0.35f * 2.2f, -1f, 1f);
                d.morale = Mathf.Clamp01(d.morale + (0.5f - d.morale) * 0.15f + delta * 0.08f * (0.7f + 0.6f * d.temperament) - (out1 ? 0.05f : 0f));
                // Evolução na temporada: só quem anda ACIMA/ABAIXO do carro; jovens e inexperientes evoluem mais.
                float youth = 1f + 1.2f * Mathf.Clamp01(1f - d.at[A_EXP] / 60f) * Mathf.Clamp01((26f - Age(d)) / 10f + 0.4f);
                float growth = 1f + ((d.ambition - 0.65f) * 8f + (d.passion - 0.6f) * 6f) / 100f;
                float k = Mathf.Sign(delta) * delta * delta * 1.6f * youth * (delta > 0f ? growth : 1f);
                if (Mathf.Abs(k) > 0.01f) Grow(d, k);
            }
        }

        static readonly int[] growKeys = { A_PACE, A_QUALI, A_CRAFT, A_CONS, A_TYRE, A_ADAPT };
        static readonly float[] growW = { 0.30f, 0.26f, 0.16f, 0.13f, 0.10f, 0.05f };
        static void Grow(WDriver d, float k)
        {
            for (int i = 0; i < growKeys.Length; i++)
            {
                int key = growKeys[i];
                float cap = key == A_PACE || key == A_QUALI ? d.potential : 93f;
                float head = k >= 0f ? Mathf.Clamp01((cap - d.at[key]) / 20f) : 1f;
                d.at[key] = Mathf.Clamp(Mathf.RoundToInt(d.at[key] * 1f + k * growW[i] * head * 2.2f + (float)(Hash(d.id + i + D.round) % 100) / 100f * 0.4f - 0.2f), 1, 100);
            }
        }

        // ---------------- Rivalidades ----------------
        static WRivalry Rival(string a, string b, bool create)
        {
            if (string.CompareOrdinal(a, b) > 0) { var t = a; a = b; b = t; }
            foreach (var r in W.rivalries) if (r.a == a && r.b == b) return r;
            if (!create) return null;
            var n = new WRivalry { a = a, b = b };
            W.rivalries.Add(n);
            return n;
        }

        static void Heat(WDriver x, WDriver y, float amount, string origin)
        {
            if (x == null || y == null || x == y) return;
            var r = Rival(x.id, y.id, true);
            float add = amount * (0.7f + 0.6f * (x.temperament + y.temperament) * 0.5f);
            float before = r.heat;
            r.heat = Mathf.Min(1f, r.heat + add);
            if (r.heat > before) r.origin = origin;
            if (!r.flared && r.heat >= 0.55f)
            {
                r.flared = true;
                bool me = x.id == PlayerId || y.id == PlayerId;
                AddNews(me ? "rivalidade" : "rivalidade", x.name + " e " + y.name + " em rota de colisão",
                    CareerWorldText.Pick(new[] { "A mesma curva, a mesma briga, semana após semana.", origin == "contato" ? "O toque na pista não foi esquecido por nenhum dos dois." : "Nenhum dos dois cede, e o resultado sempre junta os dois.", "Isso está ficando pessoal." }, x.id + y.id), me ? 78 : 55, "");
            }
            // Nêmesis: UMA por carreira do jogador, a partir de 0,70.
            if ((x.id == PlayerId || y.id == PlayerId) && string.IsNullOrEmpty(W.nemesisId) && r.heat >= 0.7f)
            {
                r.nemesis = true;
                W.nemesisId = x.id == PlayerId ? y.id : x.id;
                Achieve("Uma rivalidade virou seu nêmesis: " + Driver(W.nemesisId).name);
            }
        }

        static void Rivalries(List<WDriver> order, bool playerRace)
        {
            for (int i = 1; i < order.Count && i < 16; i++)
                if ((float)Rng("bat" + order[i].id + i).NextDouble() < 0.45f) Heat(order[i - 1], order[i], 0.05f, "disputa");
            if (!playerRace) return;
            var me = Player;
            int pi = order.IndexOf(me);
            if (pi > 0) Heat(order[pi - 1], me, 0.12f, "disputa");
            if (pi >= 0 && pi + 1 < order.Count) Heat(order[pi + 1], me, 0.12f, "disputa");
            foreach (var n in W.raceContacts) { var d = ByName(n); if (d != null) { Heat(me, d, 0.28f, "contato"); var r = Rival(me.id, d.id, false); if (r != null) r.contacts++; } }
            // Placar contra o nêmesis.
            if (!string.IsNullOrEmpty(W.nemesisId))
            {
                var ne = Driver(W.nemesisId);
                var r = ne != null ? Rival(PlayerId, ne.id, false) : null;
                int ni = ne != null ? order.IndexOf(ne) : -1;
                if (r != null && pi >= 0 && ni >= 0)
                {
                    bool ahead = pi < ni;
                    if (string.CompareOrdinal(PlayerId, ne.id) < 0) { if (ahead) r.aheadA++; else r.aheadB++; } else { if (ahead) r.aheadB++; else r.aheadA++; }
                    int mine = string.CompareOrdinal(PlayerId, ne.id) < 0 ? r.aheadA : r.aheadB;
                    if (ahead && mine == 1) Achieve("Primeira vez à frente do seu nêmesis");
                    if (ahead && (mine == 10 || mine == 25)) Achieve(mine + " corridas à frente do seu nêmesis");
                }
            }
        }

        static void DecayRivalries()
        {
            for (int i = W.rivalries.Count - 1; i >= 0; i--)
            {
                var r = W.rivalries[i];
                r.heat *= r.nemesis ? 0.97f : 0.92f;
                if (r.nemesis) r.heat = Mathf.Max(0.35f, r.heat);
                if (r.flared && r.heat < 0.35f) r.flared = false;
                if (r.heat < 0.15f && !r.nemesis) W.rivalries.RemoveAt(i);
            }
            for (int i = W.teamRivalries.Count - 1; i >= 0; i--)
            {
                W.teamRivalries[i].heat *= 0.9f;
                if (W.teamRivalries[i].heat < 0.12f) W.teamRivalries.RemoveAt(i);
            }
        }

        static void TeamWars(string series, int round)
        {
            if (round < 4) return;
            var teams = TeamsOf(series).OrderByDescending(t => series == D.category ? TeamPointsMine(t) : t.points).ToList();
            for (int i = 2; i < teams.Count - 1; i++)
            {
                int pa = series == D.category ? TeamPointsMine(teams[i]) : teams[i].points;
                int pb = series == D.category ? TeamPointsMine(teams[i + 1]) : teams[i + 1].points;
                if (pa - pb > 10) continue;
                var tr = W.teamRivalries.FirstOrDefault(x => x.series == series && ((x.a == teams[i].name && x.b == teams[i + 1].name) || (x.b == teams[i].name && x.a == teams[i + 1].name)));
                if (tr == null) { tr = new WTeamRivalry { series = series, a = teams[i].name, b = teams[i + 1].name }; W.teamRivalries.Add(tr); }
                tr.heat = Mathf.Min(1f, tr.heat + 0.08f * (0.8f + 0.4f * (1f - (teams[i].patience + teams[i + 1].patience) / 2f)));
                if (!tr.flared && tr.heat >= 0.5f)
                {
                    tr.flared = true;
                    AddNews("rivalidade", tr.a + " × " + tr.b + ": a guerra do meio do grid começou", CareerWorldText.Pick(new[] { "Pontos de diferença, não ritmo. A briga virou disputa aberta.", "Perto demais na tabela para continuar civilizado.", "Cada toque na pista joga mais lenha nessa fogueira." }, tr.a), 50, "");
                }
            }
        }

        static int TeamPointsMine(WTeam t)
        {
            int p = 0;
            if (D.standings != null) foreach (var s in D.standings) if (s.team == t.name) p += s.points;
            return p;
        }

        // ---------------- Notícias ----------------
        public static void AddNews(string cat, string headline, string body, int importance, string from)
        {
            if (W == null) return;
            W.news.Add(new WNews { season = D.season, round = D.round, cat = cat, headline = headline, body = body, importance = importance, from = from ?? "" });
        }

        public static int UnreadCount => W == null ? 0 : W.news.Count(n => !n.read);

        /// <summary>Número do selo do MUNDO no hub: e-mails não lidos + entrevista pendente + propostas novas.</summary>
        public static int NotificationCount => W == null ? 0
            : UnreadCount + (W.hasPress && W.pendingPress != null ? 1 : 0) + W.offers.Count(o => o.midseason);

        static void RaceNews(string series, List<WDriver> order, int round)
        {
            if (order.Count < 3) return;
            string track;
            try { track = RaceX.Track.TrackLibrary.Get(CareerMode.RoundTrack(round)).displayName; } catch { track = "a rodada " + (round + 1); }
            string pod = "Pódio: " + order[0].name + ", " + order[1].name + ", " + order[2].name + ".";
            // Piloto do dia = quem mais superou o carro.
            WDriver dotd = null; float best = 0f;
            for (int i = 0; i < order.Count; i++) { float g = CarRank(TeamOf(order[i])) * 2f + 1.5f - (i + 1); if (g > best) { best = g; dotd = order[i]; } }
            string extra = dotd != null && best >= 4f ? " Piloto do dia: " + dotd.name + " (+" + Mathf.RoundToInt(best) + " sobre o carro)." : "";
            string[] leads = {
                order[0].name + " controlou a corrida do começo ao fim.",
                order[0].name + " segurou a pressão de " + order[1].name + " nas últimas voltas.",
                "A estratégia de pneus decidiu: " + order[0].name + " fez a parada no momento certo.",
                order[0].name + " construiu a vitória na largada e só administrou depois.",
                "Corrida movimentada, com disputas em todo o pelotão; no fim, deu " + order[0].name + ".",
                order[0].name + " passou " + order[1].name + " na reta principal e não olhou mais para trás." };
            AddNews("corrida", CareerWorldText.PickFresh(new[] { order[0].name + " vence na " + series + " em " + track, track + ": vitória de " + order[0].name, "Relatório " + series + ": " + order[0].name + " leva em " + track,
                    order[0].name + " domina em " + track, "Bandeirada em " + track + ": " + order[0].name + " no topo", track + " tem novo vencedor: " + order[0].name }, "racehead"),
                CareerWorldText.PickFresh(leads, "racelead") + " " + pod + " Rodada " + (round + 1) + "/" + CareerMode.TotalRounds + "." + extra, series == D.category ? 60 : 45, "");
            // Novato no pódio / pilotos em fase boa ou ruim (1 vez por estado).
            // UM destaque de novato por fim de semana (no máximo), uma vez por piloto na temporada, com manchete variada.
            for (int i = 0; i < 3; i++)
                if (order[i].starts <= 8 && order[i].id != PlayerId && !W.usedText.Contains("rookieSeen|" + D.season + "|" + order[i].id))
                {
                    var tm = TeamOf(order[i]);
                    CareerWorldText.RookieNews(order[i].name, tm != null ? tm.name : "equipe", i + 1, out string rh, out string rb);
                    AddNews("mundo", rh, rb, 48, "");
                    W.usedText.Add("rookieSeen|" + D.season + "|" + order[i].id);
                    break;
                }
            foreach (var d in order)
            {
                if (d.id == PlayerId) continue;
                if (d.form <= -0.6f && d.morale < 0.35f && Hash(d.id + round) % 5 == 0 && !SeenThisSeason("Pressão sobre " + d.name))
                    AddNews("mundo", "Pressão sobre " + d.name, CareerWorldText.Pick(new[] { "A equipe fala em paciência. Os resultados dizem outra coisa.", "O assento já não parece garantido.", "Três fins de semana ruins e o paddock começa a comentar." }, d.id), 46, "");
            }
        }

        /// <summary>Evita a mesma manchete repetida: uma vez por temporada / uma vez na carreira.</summary>
        static bool SeenThisSeason(string headline) { foreach (var n in W.news) if (n.season == D.season && n.headline == headline) return true; return false; }
        static bool SeenEver(string headline) { foreach (var n in W.news) if (n.headline == headline) return true; return false; }

        static int recordSeasonWins = -1, recordSeasonPoles = -1;
        static void Records(int round)
        {
            // Recorde de vitórias na carreira entre todos os pilotos do mundo — no máximo 1 notícia por temporada.
            var best = W.drivers.OrderByDescending(d => d.wins).FirstOrDefault();
            if (best == null || best.wins < 20) return;
            int prevMax = W.drivers.Where(d => d != best).Select(d => d.wins).DefaultIfEmpty(0).Max();
            if (best.wins == prevMax + 1 && recordSeasonWins != D.season)
            {
                recordSeasonWins = D.season;
                AddNews("titulo", best.name + " vira o maior vencedor da história", best.wins + " vitórias na carreira. Ninguém no mundo tem mais.", 72, "");
            }
            recordSeasonPoles = recordSeasonPoles == D.season ? recordSeasonPoles : -1;
        }

        static void TitleFight(int round)
        {
            int left = CareerMode.TotalRounds - 1 - round;
            if (left != 3 && left != 1) return;
            var mine = D.standings;
            if (mine != null && mine.Count >= 2 && mine[0].points - mine[1].points <= (left + 1) * 25)
                AddNews("titulo", "O título da " + D.category + " ainda está aberto", mine[1].driverName + " está a " + (mine[0].points - mine[1].points) + " pontos de " + mine[0].driverName + " com " + left + " corrida(s) pela frente.", 66, "");
        }

        // ---------------- Jogador ----------------
        static void PlayerAfterRace(List<WDriver> order, int pos, int round)
        {
            var me = Player;
            var t = TeamOf(me);
            int expected = CarRank(t) * 2 + 2;
            // Fãs: resultado + marketing + sentimento.
            float gain = 0.004f + (pos <= 10 ? 0.006f : 0f) + (pos <= 3 ? 0.02f : 0f) + (pos == 1 ? 0.03f : 0f);
            // Crescimento com teto (~25 mi, perto dos maiores do grid real): rápido no começo, devagar no topo.
            W.following = W.following * (1f + gain * Mathf.Clamp01(1f - W.following / 25000000f)) + me.at[A_MKT] * 2f + (pos <= 3 ? 400f : 0f);
            W.sentiment = Mathf.Clamp01(W.sentiment + Mathf.Clamp((expected - pos) * 0.004f, -0.035f, 0.025f));
            W.sentiment += (0.5f - W.sentiment) * 0.06f; // opinião volta ao meio: precisa ser mantida (teto prático ~0,9)
            if (pos == 1 && me.wins == 1) Achieve("Primeira vitória na carreira");
            if (pos <= 3 && me.podiums == 1) Achieve("Primeiro pódio na carreira");
            // Diretoria: confiança corrida a corrida.
            if (pos <= W.boardTarget) W.boardConfidence = Mathf.Min(1f, W.boardConfidence + 0.02f);
            else if (pos > W.boardTarget + 3) W.boardConfidence = Mathf.Max(0f, W.boardConfidence - 0.025f);
            if (round == CareerMode.TotalRounds / 2)
            {
                int cur = D.standings.FindIndex(s => s.isPlayer) + 1;
                AddNews("diretoria", "Checagem de meio de temporada: " + D.pilotName + " em P" + cur,
                    cur <= W.boardTarget ? "P" + cur + " na metade supera o pedido (P" + W.boardTarget + "). A pressão saiu, por enquanto." : "A meta era P" + W.boardTarget + ". A segunda metade precisa entregar.", 74, t != null ? t.principal : "");
            }
            if (W.boardConfidence < 0.25f && !W.ultimatum)
            {
                W.ultimatum = true;
                AddNews("diretoria", "Ultimato: o assento já não é garantido", "A diretoria quer resultados imediatos. Sem reação, não haverá renovação.", 82, t != null ? t.principal : "");
            }
            // SUPERLICENÇA (regra da FIA): cada infração tem a SUA tarifa de pontos (saída de pista com vantagem 1, causar
            // colisão 2, ultrapassar sob SC/amarela 2…; limites de pista punidos com tempo NÃO dão ponto). Os pontos vencem
            // 12 meses depois (24 rodadas). 12 pontos válidos = suspensão de 1 corrida e a contagem zera. Antes TODA
            // penalidade valia 2 pontos — 27 saídas de pista deram suspensão, o que não acontece na F1.
            AddLicencePoints(W.raceLicencePts);
            // Energia: o fim de semana de corrida cansa (mais com batida forte); a semana recupera um pouco.
            W.energy = Mathf.Clamp01(W.energy - 0.08f - Mathf.Min(0.2f, W.sessionImpactLoad / 400f) + 0.04f);
            // Promessas da corrida.
            CheckRacePromises(pos);
            // Imprensa: pergunta pela situação.
            NewPress(order, pos, expected, round);
        }

        static void PlayerAbsentRound()
        {
            if (W.injuryRounds > 0) { W.injuryRounds--; if (W.injuryRounds == 0) Achieve("Primeira corrida depois de uma lesão"); }
            else if (W.banRounds > 0) W.banRounds--;
        }

        static void NewPress(List<WDriver> order, int pos, int expected, int round)
        {
            var me = Player;
            var mate = order.FirstOrDefault(d => d != me && d.series == me.series && d.team == me.team);
            int mi = mate != null ? order.IndexOf(mate) : 99;
            WDriver rival = null;
            foreach (var r in W.rivalries.OrderByDescending(x => x.heat)) { if (r.a == PlayerId || r.b == PlayerId) { rival = Driver(r.a == PlayerId ? r.b : r.a); break; } }
            string sit = pos == 1 ? "vitoria" : pos <= 3 ? "podio" : pos <= expected - 4 ? "acima" : (mi < pos - 1 && mi < 12) ? "companheiro"
                : W.raceTotaled || pos >= order.Count - 1 ? "abandono" : rival != null && Hash(round + rival.id) % 3 == 0 ? "rival" : "fora";
            var j = CareerWorldText.Journalists[Hash(D.season + "|" + round) % CareerWorldText.Journalists.Length].Split('|');
            string q = CareerWorldText.Question(sit, mate != null ? mate.name : "seu companheiro", rival != null ? rival.name : "seu rival", out var opts, out var tones, out var prom);
            W.pendingPress = new WPress { situation = sit, journalist = j[0], outlet = j[1], slant = j[2], question = q, options = opts, tones = tones, promise = prom, season = D.season, round = round };
            W.pendingPress.slant = j[2];
            W.hasPress = true;
            // promessa "frente" usa o companheiro como alvo
            if (mate != null) W.pendingPress.outlet = j[1] + "|" + mate.id;
        }

        /// <summary>Resposta da imprensa: tom (perfil), sentimento, seguidores, rapport com a equipe e promessas.</summary>
        public static void AnswerPress(int option)
        {
            if (!Active || !W.hasPress || W.pendingPress == null) return;
            var p = W.pendingPress;
            option = Mathf.Clamp(option, 0, p.options.Length - 1);
            int tone = p.tones[option];
            W.tones[tone]++;
            float[] sent = { 0.03f, 0.02f, -0.01f, 0.01f, 0f };
            float[] fol = { 0.03f, 0.005f, 0.05f, 0.005f, 0f };
            float[] rap = { -0.02f, 0.03f, -0.06f, 0.06f, 0.01f };
            W.sentiment = Mathf.Clamp01(W.sentiment + sent[tone]);
            W.following = W.following * (1f + fol[tone]) + fol[tone] * 400f;
            W.rapport = Mathf.Clamp(W.rapport + rap[tone], -1f, 1f);
            if (tone == 2) { var me = Player; me.at[A_MKT] = Mathf.Min(100, me.at[A_MKT] + 1); }
            string kind = p.promise.Length > option ? p.promise[option] : "";
            if (!string.IsNullOrEmpty(kind))
            {
                string target = "";
                if (kind == "frente") { var parts = p.outlet.Split('|'); target = parts.Length > 1 ? parts[1] : ""; }
                string text = kind == "vencer" ? "vencer a próxima corrida" : kind == "podio" ? "subir ao pódio na próxima" : "terminar o ano à frente de " + (Driver(target)?.name ?? "o companheiro");
                W.promises.Add(new WPromise { kind = kind, text = text, target = target, season = D.season, madeRound = p.round });
            }
            AddNews("imprensa", p.journalist + " (" + p.outlet.Split('|')[0] + ")", "\"" + p.question + "\" — Você: " + p.options[option], 40, p.journalist);
            W.hasPress = false;
            W.pendingPress = null;
            CareerMode.SaveQuiet();
        }

        /// <summary>Perfil de imprensa pelo tom acumulado das respostas.</summary>
        public static string PressPersona()
        {
            if (W == null || W.tones.Sum() < 3) return "Desconhecido";
            int top = 0; for (int i = 1; i < 5; i++) if (W.tones[i] > W.tones[top]) top = i;
            switch (top)
            {
                case 0: return W.tones[2] >= W.tones[3] ? "Showman" : "Arrojado";
                case 1: return "Diplomata";
                case 2: return "Polêmico";
                case 3: return "Homem da casa";
                default: return "Frio";
            }
        }

        static void CheckRacePromises(int pos)
        {
            for (int i = W.promises.Count - 1; i >= 0; i--)
            {
                var pr = W.promises[i];
                if (pr.kind != "vencer" && pr.kind != "podio") continue;
                if (pr.madeRound >= D.round && pr.season == D.season) continue; // feita nesta mesma corrida
                bool kept = pr.kind == "vencer" ? pos == 1 : pos <= 3;
                Promise(pr, kept);
                W.promises.RemoveAt(i);
            }
        }

        static void Promise(WPromise pr, bool kept)
        {
            var me = Player;
            if (kept)
            {
                W.sentiment = Mathf.Clamp01(W.sentiment + 0.06f);
                W.following = W.following * 1.06f + 300f;
                me.morale = Mathf.Clamp01(me.morale + 0.05f);
                AddNews("voce", "Promessa cumprida", "Você disse que ia " + pr.text + ", e fez. A torcida percebeu.", 70, "");
                Achieve("Cumpriu uma promessa feita à imprensa");
            }
            else
            {
                W.sentiment = Mathf.Clamp01(W.sentiment - 0.07f);
                me.morale = Mathf.Clamp01(me.morale - 0.05f);
                AddNews("voce", "Só conversa?", "Você prometeu " + pr.text + ". Não aconteceu, e o paddock não esqueceu.", 72, "");
            }
        }

        /// <summary>Pontos na superlicença pela infração — FIA "2026 F1 Penalty Guidelines" (versão 01), coluna da corrida:
        /// limites de pista (4ª em diante = 5 s) 0; sair da pista e ganhar vantagem duradoura (10 s) 0; bandeira azul 0;
        /// causar colisão 0 a 3 conforme a gravidade (aqui 2, o meio); ultrapassar 1 carro sob SC ou VSC (10 s) 2;
        /// ultrapassar sob amarela até 3 (aqui 2); ultrapassar o próprio SC 3. Devolveu a posição: nada.</summary>
        static float LicencePointsFor(string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("limites de pista")) return 0f;
            if (m.Contains("bandeira azul")) return 0f;
            if (m.Contains("fora da pista")) return 0f;
            if (m.Contains("safety car")) return 2f;
            if (m.Contains("amarela") || m.Contains("vsc")) return 2f;
            if (m.Contains("colisão")) return 2f;
            return 0f;
        }

        static int AbsRound => (D.season - 1) * CareerMode.TotalRounds + D.round;

        static void AddLicencePoints(float pts)
        {
            if (W.licRound == null) W.licRound = new List<int>();
            if (W.licPts == null) W.licPts = new List<float>();
            int now = AbsRound;
            if (pts > 0f) { W.licRound.Add(now); W.licPts.Add(pts); }
            // vencidos (12 meses = 1 temporada de rodadas)
            for (int i = W.licRound.Count - 1; i >= 0; i--)
                if (now - W.licRound[i] >= CareerMode.TotalRounds) { W.licRound.RemoveAt(i); W.licPts.RemoveAt(i); }
            float sum = 0f;
            foreach (var p in W.licPts) sum += p;
            W.licencePoints = sum;
            if (sum >= 12f)
            {
                W.banRounds = 1;
                W.licRound.Clear(); W.licPts.Clear(); W.licencePoints = 0f;
                AddNews("voce", D.pilotName + " suspenso por uma corrida",
                    "Doze pontos válidos na superlicença em 12 meses. Pela regra da FIA, isso dá uma corrida de suspensão, e a contagem recomeça do zero.", 86, "Direção de prova");
            }
        }

        /// <summary>Pico ESTIMADO de desaceleração (g) do impacto: v⊥²/(2·d·9,81) com d = esmagamento da estrutura atingida
        /// (bico ~0,6 m, traseira ~0,4 m, lateral ~0,3 m, roda ~0,35 m). O "g" do dano (v⊥²/9,81) é energia, não o g do
        /// corpo. Referências reais: Alonso, Austrália 2016 (46 g, costelas, perdeu 1 corrida); Kubica, Canadá 2007 (75 g,
        /// tornozelo, perdeu 1); Grosjean, Bahrein 2020 (67 g, queimaduras, perdeu 2); Verstappen, Silverstone 2021 (51 g,
        /// sem corrida perdida).</summary>
        static float PeakG(float energyG, int zone)
        {
            float v2 = Mathf.Max(0f, energyG) * 9.81f;
            float crush = zone == 0 ? 0.6f : zone == 1 ? 0.4f : zone <= 3 ? 0.3f : 0.35f;
            return v2 / (2f * crush * 9.81f);
        }

        /// <summary>LESÃO PELO CONTEXTO DA BATIDA (pedido do usuário: nada de sorteio solto ou data marcada). Usa o pior
        /// impacto da sessão (pico de g estimado pela velocidade perpendicular e pela estrutura atingida) e a ENERGIA do
        /// piloto (cansado se machuca mais). Abaixo de 25 g: nada. 25–40 g: pancada, liberado. 40–60 g: lesão leve
        /// possível (1 corrida). 60–80 g: provável (1–2). Acima de 80 g: grave (2–4). A chance é determinística por
        /// rodada (mesmo resultado se recarregar). Chamado no fim de QUALQUER sessão da carreira.</summary>
        public static void AfterSessionInjuryCheck()
        {
            if (!Active || W.sessionPeakG <= 0f) return;
            float pk = W.sessionPeakG;
            string what = W.sessionPeakWhat;
            W.sessionPeakG = 0f;
            if (pk < 25f) return;
            float tired = 1f - Mathf.Clamp01(W.energy);
            if (pk < 40f)
            {
                W.energy = Mathf.Clamp01(W.energy - 0.08f);
                AddNews("voce", "Exame após a batida: " + D.pilotName + " liberado",
                    "Pico estimado de " + Mathf.RoundToInt(pk) + " g na " + what + ". O centro médico não encontrou lesão; dores musculares por alguns dias.", 60, "Departamento médico");
                return;
            }
            float p = pk < 60f ? 0.15f + 0.35f * tired : pk < 80f ? 0.40f + 0.40f * tired : 0.75f + 0.2f * tired;
            var r = Rng("injury" + Mathf.RoundToInt(pk));
            if ((float)r.NextDouble() >= p)
            {
                W.energy = Mathf.Clamp01(W.energy - 0.12f);
                AddNews("voce", D.pilotName + " passa pelos exames e está liberado",
                    "Batida forte: pico estimado de " + Mathf.RoundToInt(pk) + " g na " + what + ". Sem fraturas; o corpo sente, mas pode correr.", 70, "Departamento médico");
                return;
            }
            int rounds = pk < 60f ? 1 : pk < 80f ? 1 + (tired > 0.5f ? 1 : 0) : 2 + Mathf.Min(2, Mathf.FloorToInt((pk - 80f) / 20f));
            string[] light = { "costelas contundidas", "entorse no tornozelo", "distensão no pescoço" };
            string[] mid = { "fissura numa costela", "lesão no punho", "concussão leve (protocolo da FIA)" };
            string[] heavy = { "fratura no pé", "fratura de vértebra estável", "fratura no punho" };
            var pool = pk < 60f ? light : pk < 80f ? mid : heavy;
            W.injuryRounds = Mathf.Max(W.injuryRounds, rounds);
            W.injuryText = pool[r.Next(pool.Length)];
            W.energy = Mathf.Clamp01(W.energy - 0.25f);
            AddNews("voce", D.pilotName + " se machuca na batida — fora por " + rounds + " corrida(s)",
                "Pico estimado de " + Mathf.RoundToInt(pk) + " g na " + what + (tired > 0.5f ? ", com o piloto cansado" : "") +
                ". Diagnóstico: " + W.injuryText + ". Um reserva assume o carro até a liberação médica.", 88, "Departamento médico");
        }

        /// <summary>Autoteste (chamado por CareerMode.SelfTest com uma carreira temporária).</summary>
        public static void SelfTest(System.Action<string> log)
        {
            void R(string what, bool ok, string extra = "") => log((ok ? "OK    " : "FALHA ") + what + (extra.Length > 0 ? " — " + extra : ""));
            // [38] superlicença
            float tl = LicencePointsFor("PENALIDADE +5 s — limites de pista (5ª)");
            float col = LicencePointsFor("PENALIDADE +5 s — causou colisão com X");
            float sc = LicencePointsFor("PENALIDADE +5 s — ultrapassou sob SAFETY CAR (não devolveu)");
            R("[38] tarifas: limite de pista 0, colisão 2, ultrapassar sob SC 2", tl == 0f && col == 2f && sc == 2f, "lim=" + tl + " col=" + col + " sc=" + sc);
            W.licRound.Clear(); W.licPts.Clear(); W.licencePoints = 0f; W.banRounds = 0;
            float pts = 0f; for (int i = 0; i < 27; i++) pts += LicencePointsFor("PENALIDADE +5 s — limites de pista (" + (i + 4) + "ª)");
            AddLicencePoints(pts);
            R("[38] 27 penalidades de limite de pista NÃO suspendem", W.banRounds == 0, "pontos=" + W.licencePoints);
            for (int i = 0; i < 6; i++) AddLicencePoints(LicencePointsFor("PENALIDADE +5 s — causou colisão com X"));
            R("[38] 12 pontos válidos (6 colisões) = 1 corrida de suspensão", W.banRounds == 1);
            W.banRounds = 0;
            AddLicencePoints(3f);
            int r0 = D.round; int s0 = D.season;
            D.season += 1;   // 24 rodadas depois
            AddLicencePoints(0f);
            R("[38] pontos vencem em 12 meses (24 rodadas)", W.licencePoints == 0f, "pontos depois de 1 temporada=" + W.licencePoints);
            D.season = s0; D.round = r0;
            // [40] lesão pelo contexto
            float vRear = 40f / 3.6f, vFront = 200f / 3.6f;
            float gRear = PeakG(vRear * vRear / 9.81f, 1), gFront = PeakG(vFront * vFront / 9.81f, 0);
            R("[40] traseira a 40 km/h fica abaixo do limite de lesão (25 g)", gRear < 25f, "pico≈" + gRear.ToString("0") + " g");
            R("[40] bico a 200 km/h perpendiculares é grave (> 80 g)", gFront > 80f, "pico≈" + gFront.ToString("0") + " g");
            W.injuryRounds = 0; W.energy = 1f; W.sessionPeakG = gRear; W.sessionPeakWhat = "teste";
            AfterSessionInjuryCheck();
            R("[40] batida leve não lesiona", W.injuryRounds == 0);
            W.energy = 0.2f; W.sessionPeakG = 140f;
            AfterSessionInjuryCheck();
            R("[40] batida a ~140 g com piloto cansado lesiona (chance 91%)", W.injuryRounds >= 2, "rodadas fora=" + W.injuryRounds + " · " + W.injuryText);
            W.injuryRounds = 0;
            // [40] energia sai de 100%
            W.energy = 1f; W.prepRound = -1;
            ChoosePrep("treino");
            R("[40] treino puxado tira energia (antes voltava a 100%)", W.energy < 0.9f, "energia=" + Mathf.RoundToInt(W.energy * 100f) + "%");
            // [42] textos sem repetir
            var qs = new HashSet<string>();
            for (int i = 0; i < 10; i++) qs.Add(CareerWorldText.Question("abandono", "A", "B", out _, out _, out _));
            R("[42] 10 entrevistas de abandono: perguntas diferentes", qs.Count >= 9, qs.Count + " de 10 diferentes");
            var hs = new HashSet<string>();
            for (int i = 0; i < 10; i++) { CareerWorldText.RookieNews("Fulano", "Equipe", 3, out string h, out _); hs.Add(h); }
            R("[42] 10 manchetes de novato diferentes", hs.Count >= 9, hs.Count + " de 10 diferentes");
        }

        /// <summary>Atividade da semana (1 por rodada).</summary>
        public static string ChoosePrep(string kind)
        {
            if (!Active) return "";
            if (W.prepRound == D.round) return "Você já escolheu a atividade desta semana.";
            var me = Player;
            string msg;
            switch (kind)
            {
                case "treino":
                    me.form = Mathf.Clamp(me.form + 0.08f * (0.4f + 0.6f * W.energy), -1f, 1f);
                    me.morale = Mathf.Clamp01(me.morale - 0.02f);
                    W.energy = Mathf.Clamp01(W.energy - 0.15f);
                    msg = "Treino puxado: forma ↑, moral ↓, energia ↓.";
                    break;
                case "descanso":
                    me.morale = Mathf.Clamp01(me.morale + 0.06f);
                    W.energy = Mathf.Clamp01(W.energy + 0.35f);
                    msg = "Descanso: moral ↑ e energia recuperada.";
                    break;
                case "midia":
                    me.at[A_MKT] = Mathf.Min(100, me.at[A_MKT] + 2);
                    W.following += 1500f;
                    W.energy = Mathf.Clamp01(W.energy - 0.08f);
                    msg = "Dia de mídia: +1.500 seguidores e marketing ↑.";
                    break;
                default:
                    var t = TeamOf(me);
                    if (t != null) t.budget = Mathf.Min(100f, t.budget + 6f);
                    W.energy = Mathf.Clamp01(W.energy - 0.06f);
                    msg = "Evento de patrocinador: orçamento da equipe ↑, energia ↓.";
                    break;
            }
            W.prepRound = D.round;
            W.prepDone = kind;
            // (antes somava +10% de energia em QUALQUER atividade — o treino tirava 10% e devolvia 10%: a energia nunca
            // saía de 100%, relato do usuário)
            CareerMode.SaveQuiet();
            return msg;
        }

        // ---------------- Eventos do mês (a cada 2 rodadas, 25%) ----------------
        static void MonthlyEvent(int round)
        {
            if (round % 2 != 1) return;
            var r = Rng("month");
            if ((float)r.NextDouble() > 0.25f) return;
            var me = Player;
            var t = TeamOf(me);
            int pick = r.Next(100);
            if (pick < 22) { int k = new[] { A_CONS, A_CRAFT, A_TYRE }[r.Next(3)]; me.at[k] = Mathf.Min(100, me.at[k] + 2); AddNews("voce", "Avanço no treino", "O bloco de treino focou em " + AttrNames[k].ToLowerInvariant() + ", e funcionou.", 60, t?.engineer); }
            else if (pick < 36)
            {
                // Antes: "Incômodo físico" SORTEADO, sem relação com nada. Agora só com o piloto CANSADO (energia baixa) —
                // descansar na semana evita (pedido do usuário: lesões e incômodos têm de ter contexto).
                if (W.energy < 0.45f)
                {
                    int k = r.Next(2); me.at[k] = Mathf.Max(55, me.at[k] - 1);
                    AddNews("voce", "Fadiga acumulada", "Sua energia está em " + Mathf.RoundToInt(W.energy * 100f) + "%. O preparador físico vê sinais de cansaço e " +
                        AttrNames[k].ToLowerInvariant() + " caiu um pouco. Uma semana de descanso recupera.", 58, "Departamento médico");
                }
            }
            else if (pick < 54) { me.at[A_MKT] = Mathf.Min(100, me.at[A_MKT] + 3); W.following += 2500f; AddNews("voce", "Momento de fama", "Um vídeo viralizou. Novos seguidores e patrocinadores rondando.", 58, ""); }
            else if (pick < 72) { if (t != null) t.budget = Mathf.Min(100f, t.budget + 8f); AddNews("mundo", (t != null ? t.name : "A equipe") + " fecha com um novo patrocinador", "Dinheiro novo: mais desenvolvimento no carro.", 55, ""); }
            else if (pick < 86 && D.category == "F1") { if (t != null) t.car -= 1f; AddNews("voce", "Problema na fábrica da " + (t != null ? t.name : "equipe"), "Os números do túnel de vento não batem com a pista. O carro perde um passo.", 60, t?.engineer); }
            else if (round >= 6 && round <= 16) MidseasonOffer(r);
        }

        /// <summary>Oportunidade de vaga no meio do ano: equipe melhor da mesma categoria quer o jogador AGORA.</summary>
        static void MidseasonOffer(System.Random r)
        {
            var me = Player;
            var mine = TeamOf(me);
            if (mine == null || W.offers.Any(o => o.midseason)) return;
            if (me.form < 0.25f) return; // só quem está andando acima do carro chama atenção no meio do ano
            int myRank = CarRank(mine), reach = 1 + Mathf.RoundToInt(me.form * 4f);
            var better = TeamsOf(me.series).Where(t => t.car > mine.car + 1.5f && CarRank(t) >= myRank - reach).OrderBy(t => t.car).ToList();
            foreach (var t in better)
            {
                var weakest = W.drivers.Where(x => x.series == t.series && x.team == t.slot).OrderBy(x => SeatScore(t, x, true)).FirstOrDefault();
                if (weakest == null || SeatScore(t, me, false) < SeatScore(t, weakest, true) - 0.02f) continue;
                W.offers.Add(new WOffer { series = t.series, team = t.slot, teamName = t.name, years = 2, car = t.car, midseason = true, expiresRound = D.round + 3, why = Why(t, me) });
                AddNews("voce", "Bastidores: " + t.name + " de olho em " + D.pilotName, "Uma vaga pode abrir AGORA. A proposta vale por 3 rodadas — veja em PROPOSTAS.", 84, t.principal);
                return;
            }
        }

        /// <summary>Aceita/recusa proposta do meio do ano (troca de equipe imediata).</summary>
        public static void RespondMidseason(int index, bool accept)
        {
            if (!Active || index < 0 || index >= W.offers.Count) return;
            var o = W.offers[index];
            W.offers.RemoveAt(index);
            if (!accept) { AddNews("voce", "Você recusou a " + o.teamName, "Fica onde está — por enquanto.", 50, ""); CareerMode.Save(); return; }
            var me = Player;
            var oldTeam = TeamOf(me);
            var t = Team(o.series, o.team);
            // Proteção: sem equipe atual ou equipe nova sem titular para trocar, a proposta não vale (antes: exceção ao aceitar).
            var weakest = me == null || t == null ? null : W.drivers.Where(x => x.series == t.series && x.team == t.slot && x.id != me.id).OrderBy(x => Rating(x)).FirstOrDefault();
            if (me == null || oldTeam == null || t == null || weakest == null)
            {
                AddNews("voce", "Proposta da " + o.teamName + " expirou", "A vaga não está mais disponível.", 50, "");
                CareerMode.Save();
                return;
            }
            weakest.team = oldTeam.slot; weakest.series = oldTeam.series;
            me.team = t.slot; me.contractEnd = D.season + o.years - 1;
            W.notRenewed = false; W.ultimatum = false; W.boardConfidence = 0.55f;
            CareerMode.OnPlayerChangedTeamMidseason(t.name, weakest.name, oldTeam.name);
            AddNews("mercado", D.pilotName + " troca a " + oldTeam.name + " pela " + t.name, "Troca imediata: você corre pela " + t.name + " a partir da próxima rodada. " + weakest.name + " vai para a " + oldTeam.name + ".", 90, "");
            CareerMode.Save();
        }

        static void Achieve(string text)
        {
            if (W.achievements.Contains(text)) return;
            W.achievements.Add(text);
            AddNews("voce", "Conquista: " + text, "Registrado na sua carreira.", 76, "");
        }

        static readonly string[] Tiers = { "Estreante", "Promessa", "Pontuador", "Vencedor", "Candidato ao título", "Campeão", "Lenda" };
        static void Legacy()
        {
            var me = Player;
            int titles = me.titlesF1 * 3 + me.titlesF2;
            int t = titles >= 9 ? 6 : me.titlesF1 > 0 ? 5 : me.wins >= 8 ? 4 : me.wins >= 1 ? 3 : me.podiums >= 1 || me.starts >= 30 ? 2 : me.starts >= 10 ? 1 : 0;
            if (Array.IndexOf(Tiers, W.legacyTier) < t)
            {
                W.legacyTier = Tiers[t];
                AddNews("voce", "Seu legado subiu: " + W.legacyTier, "Os números da carreira te colocam em um novo patamar.", 74, "");
            }
        }

        // ======================================================================================
        //  FIM DE TEMPORADA
        // ======================================================================================

        /// <summary>Encerramento: campeões, diretoria, academia, envelhecimento, carros/regulamento, equipes compradas,
        /// aposentadorias, propostas do jogador. Retorna true se o jogador PRECISA escolher equipe (mercado espera).</summary>
        public static bool SeasonEnd(int playerFinalPos)
        {
            if (!Active) return false;
            int season = D.season;
            var me = Player;
            // Campeões e histórico.
            var mineTable = D.standings.ToList();
            var otherTable = OtherTable();
            Champion(D.category, mineTable.Count > 0 ? ByNameOrPlayer(mineTable[0].driverName) : null, mineTable.Count > 0 ? mineTable[0].points : 0);
            if (otherTable.Count > 0) Champion(OtherSeries, otherTable[0].d, otherTable[0].pts);
            for (int i = 0; i < mineTable.Count; i++) { var d = ByNameOrPlayer(mineTable[i].driverName); if (d != null) History(d, i + 1, mineTable[i].points); }
            for (int i = 0; i < otherTable.Count; i++) History(otherTable[i].d, i + 1, otherTable[i].pts);
            // Desempenho da temporada vs carro (para evolução/potencial).
            var perf = new Dictionary<string, float>();
            void Perf(List<WDriver> list) { for (int i = 0; i < list.Count; i++) { var t = TeamOf(list[i]); float exp = CarRank(t) * 2f + 1.5f; perf[list[i].id] = Mathf.Clamp((exp - (i + 1)) / 12f, -1f, 1f); list[i].lastPerf = perf[list[i].id]; } }
            Perf(mineTable.Select(s => ByNameOrPlayer(s.driverName)).Where(d => d != null).ToList());
            Perf(otherTable.Select(x => x.d).ToList());
            RecordConstructorsOrder(mineTable, otherTable); // garagens do ano que vem (antes de compras/renomes)
            BoardVerdict(playerFinalPos);
            Academy(playerFinalPos);
            SeasonPromises();
            Aging(perf);
            CarsAndRegulation(season);
            Buyouts(season);
            RetireAndRelease(season, perf);
            // Jogador: contrato?
            bool needChoice = PlayerOffers(playerFinalPos);
            if (!needChoice) RunMarket();
            return needChoice;
        }

        static WDriver ByNameOrPlayer(string name) => name == D.pilotName ? Player : ByName(name);

        /// <summary>1ª temporada: não há "ano anterior" no jogo — a ordem é criada pela força dos carros (melhor = garagem 1).</summary>
        public static List<int> FirstSeasonGarageOrder(string series) =>
            TeamsOf(series).OrderByDescending(t => t.car).ThenBy(t => t.slot).Select(t => t.slot).ToList();

        /// <summary>Campeonato de construtores desta temporada → ordem das garagens da próxima (regra da F1).</summary>
        static void RecordConstructorsOrder(List<StandingEntry> mineTable, List<(WDriver d, int pts, int wins)> otherTable)
        {
            var mine = new Dictionary<int, int>();
            foreach (var s in mineTable)
            {
                var t = TeamByName(s.team);
                if (t == null || t.series != D.category) continue;
                mine[t.slot] = (mine.TryGetValue(t.slot, out int p) ? p : 0) + s.points;
            }
            var other = new Dictionary<int, int>();
            foreach (var x in otherTable)
                if (x.d != null && x.d.team >= 0) other[x.d.team] = (other.TryGetValue(x.d.team, out int p) ? p : 0) + x.pts;
            List<int> Order(string series, Dictionary<int, int> pts) =>
                TeamsOf(series).OrderByDescending(t => pts.TryGetValue(t.slot, out int p) ? p : 0).ThenByDescending(t => t.car).Select(t => t.slot).ToList();
            var a = Order(D.category, mine);
            var b = Order(OtherSeries, other);
            if (D.category == "F1") { D.garageSlotsF1 = a; D.garageSlotsF2 = b; }
            else { D.garageSlotsF2 = a; D.garageSlotsF1 = b; }
        }

        static void Champion(string series, WDriver d, int pts)
        {
            if (d == null) return;
            var t = TeamOf(d);
            W.champions.Add(new WChampion { season = D.season, series = series, driver = d.name, team = t != null ? t.name : "", points = pts });
            if (series == "F1") d.titlesF1++; else d.titlesF2++;
            AddNews("titulo", d.name + " é campeão da " + series + " " + Year, (t != null ? t.name : "") + " · " + pts + " pontos.", 90, "");
            if (d.id == PlayerId) Achieve("Campeão da " + series);
        }

        static void History(WDriver d, int pos, int pts)
        {
            var t = TeamOf(d);
            d.history.Add(D.season + "|" + d.series + "|" + (t != null ? t.name : "-") + "|" + pos + "|" + pts);
            d.ratingHistory.Add(Rating(d));
            if (d.series == "F1") d.f1Seasons++;
        }

        static void BoardVerdict(int pos)
        {
            var t = TeamOf(Player);
            string who = t != null ? t.principal : "";
            int target = W.boardTarget;
            int diff = target - pos;
            string verdict = diff >= 3 ? "A diretoria está encantada: P" + pos + " num carro de P" + target + "."
                : diff >= 0 ? "Temporada sólida: o carro era de P" + target + " e P" + pos + " é exatamente isso."
                : diff >= -2 ? "Um pouco abaixo do pedido (P" + target + "). Ninguém feliz, ninguém furioso."
                : "P" + pos + " num carro de P" + target + ". A diretoria quer respostas.";
            W.boardConfidence = Mathf.Clamp01(W.boardConfidence + diff * 0.05f);
            W.boardMissStreak = diff < -2 ? W.boardMissStreak + 1 : 0;
            AddNews("diretoria", "Balanço " + Year + " da diretoria", verdict, 80, who);
        }

        static void Academy(int pos)
        {
            if (W.academyStanding != "backed" && W.academyStanding != "warned") return;
            if (D.category == "F1") { W.academyStanding = "graduated"; Achieve("Formou-se na academia e chegou à F1"); return; }
            if (pos <= 5) { if (W.academyStanding == "warned") AddNews("voce", "A academia volta a confiar em você", "Temporada boa. O apoio continua.", 70, D.academyName); W.academyStanding = "backed"; W.academyWarnings = 0; return; }
            if (pos > 12)
            {
                W.academyWarnings++;
                if (W.academyWarnings >= 2) { W.academyStanding = "dropped"; AddNews("voce", D.academyName + " te dispensa da academia", "A porta fechou. O que vier daqui em diante, vem sem esse dinheiro.", 86, D.academyName); }
                else { W.academyStanding = "warned"; AddNews("voce", "Advertência da " + D.academyName, "P" + pos + " não é o que a academia espera. Mais um ano assim e acabou.", 80, D.academyName); }
            }
        }

        static void SeasonPromises()
        {
            for (int i = W.promises.Count - 1; i >= 0; i--)
            {
                var pr = W.promises[i];
                if (pr.kind != "frente") continue;
                var tgt = Driver(pr.target);
                int mine = D.standings.FindIndex(s => s.isPlayer), theirs = tgt != null ? D.standings.FindIndex(s => s.driverName == tgt.name) : -1;
                Promise(pr, theirs < 0 || mine < theirs);
                W.promises.RemoveAt(i);
            }
            W.promises.RemoveAll(p => p.season < D.season);
        }

        /// <summary>Envelhecimento anual: sobe até o pico (pelo potencial), cai depois; experiência +7.</summary>
        static void Aging(Dictionary<string, float> perf)
        {
            foreach (var d in W.drivers)
            {
                if (d.series == "RET") continue;
                int age = Age(d) + 1;
                float p = perf.TryGetValue(d.id, out var v) ? v : 0f;
                for (int k = 0; k < 2; k++)
                {
                    float delta;
                    if (age < d.peakAge) delta = Mathf.Min(3f, d.devRate * (d.potential - d.at[k]) * 0.32f) * (1f + 0.8f * p);
                    else if (age > d.peakAge + 1) delta = -d.declineRate * Mathf.Min(8, age - d.peakAge) * 0.5f * (1f - 0.5f * p);
                    else delta = 0.8f * p;
                    d.at[k] = Mathf.Clamp(Mathf.RoundToInt(d.at[k] + delta), 30, 99);
                }
                if (age <= 34)
                {
                    if (d.at[A_CRAFT] < 92) d.at[A_CRAFT] = Mathf.Min(92, d.at[A_CRAFT] + 1);
                    if (d.at[A_CONS] < 90 && (Hash(d.id + D.season) & 1) == 0) d.at[A_CONS]++;
                    if (d.at[A_TYRE] < 90 && (Hash(d.id + D.season) & 2) == 0) d.at[A_TYRE]++;
                }
                d.at[A_EXP] = Mathf.Min(100, d.at[A_EXP] + 7);
                if (p > 0.5f) d.potential = Mathf.Min(99f, d.potential + 2.2f * (p - 0.5f));
                else if (p < -0.4f) d.potential = Mathf.Max(Mathf.Max(d.at[0], d.at[1]), d.potential - 1.6f * (-0.4f - p));
                d.form *= 0.4f;
                d.morale = Mathf.Lerp(d.morale, 0.5f, 0.5f);
            }
        }

        /// <summary>Desenvolvimento dos carros pelo orçamento; regulamento novo a cada 5 temporadas embaralha a ordem.</summary>
        static void CarsAndRegulation(int season)
        {
            var r = Rng("cars");
            bool reg = season - W.lastRegulationSeason >= 5 && (float)r.NextDouble() < 0.6f;
            foreach (var t in W.teams)
            {
                float mean = t.series == "F1" ? 86.5f : 79.5f;
                if (reg && t.series == "F1") t.car = mean + Normal(r, 0f, 3.2f) + (t.budget - 60f) / 40f * 2.2f;
                else t.car += (t.budget - 60f) / 40f * 1.1f + F(r, -1.1f, 1.1f) + (mean - t.car) * 0.08f;
                t.car = Mathf.Clamp(t.car, t.series == "F1" ? 76f : 72f, t.series == "F1" ? 96f : 86f);
                t.budget = Mathf.Clamp(t.budget + (t.series == "F1" ? (6 - CarRank(t)) * 0.8f : 0f) + F(r, -3f, 3f), 30f, 100f);
            }
            if (reg)
            {
                W.lastRegulationSeason = season;
                var f1 = TeamsOf("F1").OrderByDescending(t => t.car).ToList();
                AddNews("regulamento", "Regulamento novo embaralha a F1", f1[0].name + " acertou em cheio; " + f1[f1.Count - 1].name + " errou a conta e despencou. As diferenças já aparecem.", 84, "");
            }
            if (D.category == "F1" || D.category == "F2")
            {
                var mine = TeamOf(Player);
                if (mine != null) AddNews("voce", "Balanço de inverno do engenheiro", CareerWorldText.Pick(new[] { "Inverno calmo: mantivemos a posição.", "Os rivais desenvolveram mais e perdemos um passo.", "Bom inverno de fábrica: ganhamos terreno." }, mine.name + season) + " Nota do carro agora: " + mine.car.ToString("0.0") + ".", 62, mine.engineer);
            }
        }

        /// <summary>Equipe falida (anos seguidos no fundo + orçamento baixo) é comprada e muda de nome — no máximo 1 por ano.</summary>
        static void Buyouts(int season)
        {
            foreach (var s in new[] { "F1", "F2" })
            {
                var table = TeamsOf(s).OrderBy(t => s == D.category ? TeamPointsMine(t) : t.points).ToList();
                foreach (var t in TeamsOf(s)) t.badSeasons = table.IndexOf(t) < 2 ? t.badSeasons + 1 : 0;
            }
            var cand = W.teams.Where(t => t.badSeasons >= 3 && t.budget < 50f && season - t.lastRebrandSeason >= 6).OrderBy(t => t.budget).FirstOrDefault();
            if (cand == null) return;
            var r = Rng("buy");
            if ((float)r.NextDouble() > 0.5f) return;
            string newName = CareerWorldText.BuyoutNames.FirstOrDefault(n => !W.teams.Any(t => t.name == n) && !W.champions.Any(c => c.team == n));
            if (newName == null) return;
            string old = cand.name;
            if (D.f2Team == old) D.f2Team = newName;
            cand.name = newName; cand.lastRebrandSeason = season; cand.badSeasons = 0; cand.budget = 72f; cand.principal = Person(r);
            if (D.standings != null) foreach (var st in D.standings) if (st.team == old) st.team = newName;
            AddNews("mundo", old + " é comprada e vira " + newName, "Dinheiro novo, nome novo e um novo chefe (" + cand.principal + "). A história fica nos livros.", 82, "");
        }

        /// <summary>Aposentadorias, F2 com limite de idade, demissões e fim de contrato.</summary>
        static void RetireAndRelease(int season, Dictionary<string, float> perf)
        {
            var r = Rng("retire");
            foreach (var d in W.drivers.ToList())
            {
                if (d.id == PlayerId || d.series == "RET") continue;
                int age = Age(d) + 1;
                float rt = Rating(d);
                if (d.series == "F1" || (d.series == "RES" && d.f1Seasons >= 2))
                {
                    float eff = age - d.passion * 3f, ch = eff >= 38 ? 0.85f : eff >= 36 ? 0.5f : eff >= 34 ? 0.22f : eff >= 32 ? 0.07f : 0f;
                    if (age > 31 && rt < 80f) ch += 0.3f * (1f - d.passion);
                    var t = TeamOf(d);
                    if (t != null && CarRank(t) <= 2) ch *= 0.7f;
                    if (d.passion >= 0.86f && rt >= 82f) ch *= 0.4f;
                    if (d.series == "RES") ch += 0.25f;
                    if (age < 30) ch = 0f;
                    if ((float)r.NextDouble() < Mathf.Min(0.95f, ch)) { Retire(d, age, d.series == "RES" ? "deixa o automobilismo" : "se aposenta da F1"); continue; }
                }
                // F2 tem LIMITE DE IDADE: 27+ não corre F2 (sobe, vai para reserva de F1 ou segue outro caminho).
                if (d.series == "F2" && age >= 27) { d.series = "RES"; d.team = -1; d.contractEnd = season; continue; }
                if (d.series == "RES" && d.f1Seasons < 2 && age >= 27 && rt < 80f) { Retire(d, age, "segue outro caminho"); continue; }
                if (d.series == "RES" && age >= 38) { Retire(d, age, "pendura o capacete"); continue; }
                // Demissão: forma e moral ruins numa equipe pouco leal.
                var tm = TeamOf(d);
                if (tm != null && d.form < -0.3f && d.morale < 0.42f && tm.loyalty < 0.45f && (float)r.NextDouble() < 0.35f * (0.45f - tm.loyalty) * 3f)
                {
                    AddNews("mercado", tm.name + " dispensa " + d.name, "Forma em queda, moral baixa e uma equipe sem paciência.", 64, "");
                    d.series = "RES"; d.team = -1; d.contractEnd = season;
                }
            }
        }

        static void Retire(WDriver d, int age, string how)
        {
            AddNews("mercado", d.name + " " + how + " aos " + age, d.titlesF1 > 0 ? "Sai com " + d.titlesF1 + " título(s), " + d.wins + " vitórias e " + d.podiums + " pódios." : d.wins + " vitórias e " + d.podiums + " pódios na carreira.", d.titlesF1 > 0 ? 84 : 62, "");
            d.series = "RET"; d.team = -1; d.retiredSeason = D.season;
        }

        // ---------------- Mercado ----------------
        static readonly Dictionary<string, float[]> weights = new Dictionary<string, float[]>
        {   // ritmo, potencial, juventude, experiência, marketing, permanência
            { "works",      new[] { 0.34f, 0.24f, 0.10f, 0.12f, 0.08f, 0.12f } },
            { "grandee",    new[] { 0.34f, 0.14f, 0.06f, 0.20f, 0.12f, 0.14f } },
            { "privateer",  new[] { 0.36f, 0.16f, 0.08f, 0.14f, 0.12f, 0.14f } },
            { "ladder",     new[] { 0.24f, 0.34f, 0.20f, 0.04f, 0.08f, 0.10f } },
            { "backmarker", new[] { 0.26f, 0.14f, 0.08f, 0.10f, 0.32f, 0.10f } }
        };

        /// <summary>Pontuação de um piloto para a vaga de uma equipe (critério muda pelo tipo de equipe).</summary>
        public static float SeatScore(WTeam t, WDriver d, bool incumbent)
        {
            var w = weights.TryGetValue(t.philosophy, out var x) ? x : weights["privateer"];
            int age = Age(d);
            float scout = ((Hash(t.name + d.id) % 100) / 100f - 0.5f) * 4f;
            float s = (Rating(d) - 65f) / 30f * w[0]
                    + (d.potential + scout - 70f) / 28f * w[1] * (0.6f + 0.8f * t.youthFocus)
                    + Mathf.Max(0f, (30f - age) / 12f) * w[2] * (0.5f + t.youthFocus)
                    + d.at[A_EXP] / 100f * w[3] * (1.4f - 0.8f * t.youthFocus)
                    + d.at[A_MKT] / 100f * w[4]
                    + (incumbent ? w[5] * (0.5f + t.loyalty) : 0f)
                    + (d.nat == t.nation ? t.nationalBias * 0.15f : 0f)
                    + Mathf.Clamp(d.form, -1f, 1f) * 0.12f
                    + Mathf.Clamp(d.lastPerf, -1f, 1f) * 0.45f; // resultado da temporada vs o carro
            if (t.series == "F1" && d.series == "F2") s += d.titlesF2 > 0 ? 0.25f : 0f;
            return s;
        }

        static string Why(WTeam t, WDriver d)
        {
            var w = weights.TryGetValue(t.philosophy, out var x) ? x : weights["privateer"];
            var parts = new List<(string, float)>
            {
                ("seu ritmo", (Rating(d) - 65f) / 30f * w[0]), ("seu potencial", (d.potential - 70f) / 28f * w[1]),
                ("sua juventude", Mathf.Max(0f, (30f - Age(d)) / 12f) * w[2]), ("sua experiência", d.at[A_EXP] / 100f * w[3]),
                ("seu apelo com patrocinadores", d.at[A_MKT] / 100f * w[4])
            }.OrderByDescending(p => p.Item2).ToList();
            string style = t.favouritism >= 0.72f ? "Eles montam a equipe em torno de um primeiro piloto." : t.youthFocus >= 0.75f ? "É uma equipe que aposta em jovens." : t.loyalty <= 0.38f ? "São implacáveis: entregue agora ou está fora." : t.patience >= 0.74f ? "Topam um projeto de longo prazo." : t.loyalty >= 0.72f ? "Costumam ficar com seus pilotos." : "";
            return "O que te vende para eles: " + parts[0].Item1 + ", com " + parts[1].Item1 + ". " + style;
        }

        static int ContractYears(WTeam t, WDriver d)
        {
            if (t.series == "F2") return d.potential > 86f && Age(d) < 21 ? 2 : 1;
            int age = Age(d);
            return age < 25 && d.potential > 86f ? 3 : age < 29 ? 2 : 1;
        }

        /// <summary>Propostas de fim de ano para o jogador. Retorna true se ele precisa escolher (contrato acabou).</summary>
        static bool PlayerOffers(int pos)
        {
            var me = Player;
            var mine = TeamOf(me);
            bool champF2 = me.series == "F2" && me.titlesF2 > 0;
            bool expiring = me.contractEnd <= D.season || W.notRenewed || champF2; // campeão da F2 não defende o título
            // Diretoria COM consequência: depois do ultimato, confiança no chão rescinde mesmo com contrato em vigor.
            if (!expiring && mine != null && W.ultimatum && W.boardConfidence < 0.12f && W.boardMissStreak >= 1)
            {
                W.notRenewed = true; expiring = true;
                AddNews("diretoria", mine.name + " rescinde o contrato de " + D.pilotName, "O ultimato não foi respondido. A equipe paga a multa e libera a vaga — escolha entre as propostas.", 92, mine.principal);
            }
            W.offers.RemoveAll(o => !o.midseason);
            W.offers.RemoveAll(o => o.midseason);
            // Diretoria decide renovar: confiança baixa ou 2 metas perdidas seguidas = NÃO renova (e aí acabou de verdade).
            bool renew = W.boardConfidence >= 0.33f && W.boardMissStreak < 2;
            if (expiring && !renew && mine != null)
            {
                W.notRenewed = true;
                AddNews("diretoria", mine.name + " não vai renovar com você", "A diretoria decidiu seguir outro caminho. Seu contrato termina agora — escolha entre as propostas.", 90, mine.principal);
            }
            if (!expiring) { if (W.boardConfidence >= 0.8f && !W.number1) { W.number1 = true; AddNews("voce", "Status de primeiro piloto", "A temporada passou das expectativas: a equipe agora é montada em torno de você.", 80, mine?.principal); } return false; }
            int age = Age(me) + 1;
            int myRank = mine != null ? CarRank(mine) : 11;
            foreach (var t in W.teams)
            {
                if (t.series == "F2" && (age >= 27 || me.f1Seasons >= 2 || champF2)) continue; // F2 com limite de idade, sem ex-titular de F1 e sem campeão
                if (t == mine && (!renew || champF2)) continue;
                if (t != mine && !InReach(t, me, pos, myRank)) continue;
                var weakest = W.drivers.Where(x => x.series == t.series && x.team == t.slot && x.id != PlayerId).OrderBy(x => SeatScore(t, x, true)).FirstOrDefault();
                float mineScore = SeatScore(t, me, t == mine);
                float theirs = weakest != null ? SeatScore(t, weakest, true) : -1f;
                bool f1Path = t.series == "F1" && me.series == "F2" && pos <= 3 && CarRank(t) >= 8; // top 3 da F2 sempre tem porta no fundo da F1
                if (t == mine || f1Path || mineScore > theirs + 0.02f)
                    W.offers.Add(new WOffer { series = t.series, team = t.slot, teamName = t.name, years = ContractYears(t, me), car = t.car, why = t == mine ? "Renovação: a diretoria quer continuar com você." : Why(t, me) });
            }
            // Mínimo de 2 propostas (vagas do fundo da categoria atual, ou F1 se a F2 não é mais possível).
            string fb = (age >= 27 || me.f1Seasons >= 2 || champF2) ? "F1" : me.series;
            foreach (var t in TeamsOf(fb).OrderBy(t => t.car))
            {
                if (W.offers.Count(o => !o.midseason) >= 2) break;
                if (W.offers.Any(o => o.series == t.series && o.team == t.slot) || (t == mine && !renew)) continue;
                bool paid = t.series == "F1" && me.series == "F2" && !champF2; // sem idade para a F2 e sem convite: só pagando a vaga
                W.offers.Add(new WOffer { series = t.series, team = t.slot, teamName = t.name, years = 1, car = t.car,
                    why = paid ? "Vaga de piloto pagante: seus patrocinadores cobrem parte do orçamento da equipe. Sem garantia além de 1 ano." : "Uma vaga no fundo do grid para provar seu valor." });
            }
            W.offers = W.offers.OrderByDescending(o => o.series == "F1").ThenByDescending(o => o.car).Take(5).ToList();
            AddNews("mercado", "Mercado aberto: " + W.offers.Count + " proposta(s) para " + D.pilotName, string.Join(", ", W.offers.Select(o => o.teamName + " (" + o.series + ", " + o.years + " ano(s))")), 88, "");
            return true;
        }

        /// <summary>Até que nível de equipe o resultado do ano leva (0 = melhor carro). P24 em carro de P24 não
        /// recebe proposta de equipe grande; da F2 para a F1 só o top 3 (ou quem andou muito acima do carro).</summary>
        static bool InReach(WTeam t, WDriver me, int pos, int myRank)
        {
            int rank = CarRank(t);
            float perf = Mathf.Max(0f, me.lastPerf);
            if (me.series == "F2" && t.series == "F1")
                return pos == 1 ? rank >= 6 : pos <= 3 ? rank >= 8 : (pos <= 6 && me.lastPerf >= 0.3f) && rank >= 10;
            if (me.series == "F1" && t.series == "F2") return true; // F2 sempre possível (respeitando idade/ex-F1)
            int reach = 1 + Mathf.RoundToInt(perf * 6f) + (pos <= 3 ? 3 : pos <= 6 ? 1 : 0) + (me.titlesF1 > 0 ? 4 : 0);
            return pos == 1 || rank >= myRank - reach;
        }

        /// <summary>Jogador escolheu uma proposta (fim de ano): assina, depois o resto do mercado acontece.</summary>
        public static void SignOffer(int index)
        {
            if (!Active || index < 0 || index >= W.offers.Count) return;
            var o = W.offers[index];
            var t = Team(o.series, o.team);
            var me = Player;
            string old = TeamOf(me)?.name ?? "";
            bool promo = me.series == "F2" && o.series == "F1";
            me.series = o.series;
            TakeSeat(me, t, o.years);
            me.contractEnd = D.season + o.years; // a temporada que começa conta
            W.notRenewed = false; W.ultimatum = false; W.boardMissStreak = 0;
            if (t.name != old) { W.boardConfidence = 0.55f; W.number1 = false; }
            W.offers.Clear();
            AddNews("mercado", promo ? D.pilotName + " sobe para a F1 com a " + t.name : D.pilotName + " assina com a " + t.name, o.years + " ano(s) de contrato. " + (promo ? "A porta da F1 se abriu." : ""), 90, "");
            RunMarket();
        }

        /// <summary>Preenche todas as vagas: F1 primeiro (equipes mais fortes escolhem antes), depois F2; novatos completam.</summary>
        static void RunMarket()
        {
            int season = D.season; // a nova temporada é season+1 (CareerMode incrementa depois)
            var r = Rng("market");
            var signedNow = new HashSet<string>();
            // Contratos que acabam liberam a vaga (o titular pode renovar pela pontuação, com bônus de permanência).
            foreach (var d in W.drivers)
                if (d.id != PlayerId && (d.series == "F1" || d.series == "F2") && (d.contractEnd <= season || (d.series == "F2" && d.titlesF2 > 0))) d.contractEnd = -1; // -1 = negociando; campeão da F2 não defende o título
            // Novatos fictícios do ano (17–19 anos, potencial oculto).
            for (int i = 0; i < 6; i++) W.drivers.Add(Newgen(r));
            foreach (var series in new[] { "F1", "F2" })
            {
                foreach (var t in TeamsOf(series).OrderByDescending(t => t.car))
                {
                    for (int seat = 0; seat < 2; seat++)
                    {
                        var seated = W.drivers.Where(x => x.series == series && x.team == t.slot && x.contractEnd >= 0).ToList();
                        if (seated.Count >= 2) break;
                        var incumbents = W.drivers.Where(x => x.series == series && x.team == t.slot && x.contractEnd < 0).ToList();
                        var pool = W.drivers.Where(x => x.id != PlayerId && !signedNow.Contains(x.id) && Eligible(x, series, t)).ToList();
                        if (pool.Count == 0) break;
                        var pick = pool.OrderByDescending(x => SeatScore(t, x, incumbents.Contains(x)) + (x.series == "RET" ? -0.05f : 0f)).First();
                        string from = pick.series == "RET" ? "RET" : pick.series;
                        string fromTeam = TeamOf(pick)?.name ?? "";
                        // (Quem sai da vaga — titular não escolhido — vira reserva no fim deste laço da equipe.)
                        if (pick.series == "RET") { pick.comebacks++; pick.retiredSeason = -1; }
                        pick.series = series; pick.team = t.slot; pick.contractEnd = season + ContractYears(t, pick);
                        signedNow.Add(pick.id);
                        NewsSigning(pick, t, from, fromTeam, incumbents.Contains(pick));
                    }
                    // Titulares não renovados dessa equipe saem para o mercado.
                    foreach (var inc in W.drivers.Where(x => x.series == series && x.team == t.slot && x.contractEnd < 0).ToList()) { inc.series = "RES"; inc.team = -1; inc.contractEnd = season; }
                }
            }
            // Quem ficou sem vaga.
            foreach (var d in W.drivers.Where(x => x.series == "RES" && x.contractEnd == season && x.f1Seasons >= 1 && !signedNow.Contains(x.id)))
                AddNews("mercado", "Sem vaga na F1 para " + d.name + " (" + (Age(d) + 1) + " anos)", "As conversas não deram em nada no inverno.", 58, "");
            // Limpa o excesso de reservas jovens não contratados (mantém o mundo enxuto).
            var extra = W.drivers.Where(x => x.series == "RES" && x.newgen && x.f1Seasons == 0 && x.starts == 0).Skip(14).ToList();
            foreach (var d in extra) W.drivers.Remove(d);
            ResetOtherStandings();
            // Meta da diretoria e palpite dos comentaristas para a nova temporada.
            var mine = TeamOf(Player);
            W.boardTarget = Mathf.Clamp(CarRank(mine) * 2 + 2 - (Player.ambition > 0.7f ? 1 : 0), 1, 20);
            AddNews("diretoria", "Meta da diretoria para " + (Year + 1) + ": P" + W.boardTarget, "Pela força do carro, P" + W.boardTarget + " está ao alcance. É o que a diretoria espera de você.", 78, mine?.principal);
            AddNews("mundo", "Palpite dos comentaristas: " + D.pilotName + " por volta de P" + Mathf.Clamp(W.boardTarget + (Hash(D.season + "p") % 3) - 1, 1, 22), "Pura especulação de pré-temporada, até as luzes apagarem.", 50, "");
        }

        static bool Eligible(WDriver x, string series, WTeam t)
        {
            int age = Age(x) + 1;
            bool free = x.contractEnd < 0 || x.series == "RES" || (x.series == "RET" && x.comebacks == 0 && D.season - x.retiredSeason <= 3 && age <= 40 && x.passion >= 0.8f && Rating(x) >= 84f);
            if (x.series == "F2" && x.contractEnd >= 0 && series == "F1" && (Rating(x) >= 80f || x.titlesF2 > 0)) free = true; // F2 → F1: sobe mesmo com contrato
            if (!free) return false;
            if (x.series == "RET" && !(x.comebacks == 0 && D.season - x.retiredSeason <= 3 && age <= 40 && x.passion >= 0.8f && Rating(x) >= 84f)) return false;
            if (series == "F2") return age <= 26 && x.f1Seasons < 2 && x.titlesF1 == 0 && x.titlesF2 == 0 && x.series != "F1";
            // F1: precisa de nota mínima (superlicença simplificada).
            return Rating(x) >= 76f && x.series != "F1" || (x.series == "F1" && x.contractEnd < 0);
        }

        static WDriver Newgen(System.Random r)
        {
            var used = new HashSet<string>(W.drivers.Select(d => d.name));
            string name = NewName(r, used, out string nat);
            int age = r.Next(17, 20);
            float pot = Mathf.Clamp(Normal(r, 84f, 5f), 72f, 97f);
            var d = new WDriver
            {
                id = "ng" + (++W.newgenSeq), name = name, nat = nat, birthYear = Year + 1 - age, series = "RES", team = -1, newgen = true,
                peakAge = Mathf.Clamp(Mathf.RoundToInt(Normal(r, 29f, 1.8f)), 26, 33), devRate = F(r, 0.5f, 0.95f), declineRate = F(r, 0.15f, 0.35f), potential = pot,
                loyalty = F(r, 0.2f, 0.9f), ambition = F(r, 0.3f, 0.95f), passion = F(r, 0.3f, 0.95f), temperament = F(r, 0.1f, 0.9f)
            };
            float b = Mathf.Clamp(pot - F(r, 8f, 16f), 62f, 84f);
            d.at = new[] { Clamp100(Normal(r, b, 2.5f)), Clamp100(Normal(r, b, 2.5f)), Clamp100(Normal(r, 70f, 5f)), Clamp100(Normal(r, 70f, 5f)), Clamp100(Normal(r, 70f, 7f)), Clamp100(Normal(r, 64f, 6f)), Clamp100(Normal(r, 65f, 10f)), Clamp100(Normal(r, 74f, 6f)), r.Next(3, 15), Clamp100(Normal(r, 58f, 12f)) };
            return d;
        }

        static void NewsSigning(WDriver d, WTeam t, string from, string fromTeam, bool renewed)
        {
            if (renewed) return; // renovação silenciosa (a bandeja fica com o que muda)
            string h;
            if (from == "RET") h = d.name + " volta da aposentadoria pela " + t.name;
            else if (d.newgen && d.starts == 0) h = "Estreante " + d.name + " assina com a " + t.name;
            else if (from == "F2" && t.series == "F1") h = d.name + " é promovido à F1 pela " + t.name;
            else h = d.name + " troca " + (string.IsNullOrEmpty(fromTeam) ? "a reserva" : "a " + fromTeam) + " pela " + t.name;
            AddNews("mercado", h, "Contrato até " + ((W.baseYear) + d.contractEnd - 1) + ". " + (t.series == "F1" ? "Fórmula 1." : "Fórmula 2."), from == "RET" ? 80 : from == "F2" && t.series == "F1" ? 72 : 54, "");
        }
    }
}
