using System;
using System.Collections.Generic;
using System.Linq;
using RaceX.Career;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>MUNDO da carreira (mundo vivo): VOCÊ, BANDEJA, IMPRENSA, SEMANA, PROPOSTAS, PILOTOS, EQUIPES,
    /// RELAÇÕES e ARQUIVO. Tudo lido de CareerWorld/WorldState — a tela não guarda estado próprio além da aba.</summary>
    internal static class CareerWorldScreens
    {
        static int tab, newsFilter, newsPage, listPage, archivePage;
        static string seriesView = "";
        static WNews reading;   // e-mail aberto na BANDEJA (null = lista)
        static string weekMsg = "";
        static readonly string[] Tabs = { "VOCÊ", "BANDEJA", "IMPRENSA", "SEMANA", "PROPOSTAS", "PILOTOS", "EQUIPES", "RELAÇÕES", "ARQUIVO" };
        static readonly string[] NewsCats = { "", "voce", "corrida", "titulo", "mercado", "diretoria", "rivalidade", "mundo", "imprensa" };
        static readonly string[] NewsCatNames = { "TODAS", "VOCÊ", "CORRIDA", "TÍTULO", "MERCADO", "DIRETORIA", "RIVALIDADE", "MUNDO", "IMPRENSA" };
        static readonly Vector2 TL = new Vector2(0f, 1f);
        static readonly Color Row = new Color(1f, 1f, 1f, 0.045f);
        static readonly Color RowHi = new Color(1f, 0.48f, 0.1f, 0.2f);
        static readonly Color Good = new Color(0.35f, 0.9f, 0.45f);
        static readonly Color Bad = new Color(1f, 0.38f, 0.32f);

        static WorldState W => CareerWorld.W;

        /// <summary>Badge do botão MUNDO no hub: novidades que pedem atenção.</summary>
        public static string Badge()
        {
            if (!CareerWorld.Active) return "";
            int n = CareerWorld.UnreadCount;
            bool press = W.hasPress && W.pendingPress != null;
            bool offer = W.offers.Any(o => o.midseason);
            var parts = new List<string>();
            if (offer) parts.Add("PROPOSTA!");
            if (press) parts.Add("IMPRENSA");
            if (n > 0) parts.Add(n + " NOVA" + (n > 1 ? "S" : ""));
            return string.Join(" · ", parts);
        }

        public static void Open(int startTab = -1)
        {
            if (!CareerWorld.Active) return;
            reading = null;   // entra sempre pela lista, nunca num e-mail aberto da visita anterior
            if (startTab >= 0) tab = startTab;
            else if (W.offers.Any(o => o.midseason)) tab = 4;
            else if (W.hasPress && W.pendingPress != null) tab = 2;
            if (string.IsNullOrEmpty(seriesView)) seriesView = CareerMode.data.category == "F1" ? "F1" : "F2";
            Show();
        }

        static void Show()
        {
            var s = CareerFlow.NewScreen();
            MenuTheme.Header(s, "MUNDO · " + CareerWorld.Year + " · RODADA " + Mathf.Min(CareerMode.data.round + 1, CareerMode.TotalRounds) + "/" + CareerMode.TotalRounds);
            TabsRow(s);
            var body = UIFactory.Rect("WorldBody", s, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            body.offsetMin = new Vector2(40f, 112f);
            body.offsetMax = new Vector2(-40f, -214f);
            try
            {
                switch (tab)
                {
                    case 0: You(body); break;
                    case 1: if (reading != null) ReadMail(body); else Inbox(body); break;
                    case 2: Press(body); break;
                    case 3: Week(body); break;
                    case 4: Offers(body); break;
                    case 5: Drivers(body); break;
                    case 6: Teams(body); break;
                    case 7: Relations(body); break;
                    default: Archive(body); break;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Txt(body, "Não foi possível montar esta aba.", 24, 0, 0, 1200, 40, Bad); }
            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "WorldBack", "VOLTAR AO HUB", b, b, new Vector2(0f, 36f), new Vector2(340f, 64f), 26, CareerFlow.BackToHub);
        }

        static void TabsRow(RectTransform s)
        {
            int unread = CareerWorld.UnreadCount;
            bool press = W.hasPress && W.pendingPress != null;
            int offers = W.offers.Count(o => o.midseason);
            bool week = W.prepRound != CareerMode.data.round;
            const float w = 196f, gap = 8f;
            float total = Tabs.Length * w + (Tabs.Length - 1) * gap;
            var top = new Vector2(0.5f, 1f);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                string label = Tabs[i];
                if (i == 1 && unread > 0) label += " (" + unread + ")";
                if (i == 2 && press) label += " !";
                if (i == 3 && week) label += " •";
                if (i == 4 && offers > 0) label += " (" + offers + ")";
                float x = -total * 0.5f + w * 0.5f + i * (w + gap);
                var btn = MenuTheme.StyledButton(s, "Tab" + i, label, top, top, new Vector2(x, -150f), new Vector2(w, 56f), 17, () => { tab = idx; listPage = 0; newsPage = 0; reading = null; Show(); });
                if (i == tab) btn.GetComponent<Image>().color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.4f);
            }
        }

        // ---------------- Helpers ----------------
        static Text Txt(Transform p, string s, int size, float x, float y, float w, float h, Color c, TextAnchor a = TextAnchor.UpperLeft)
        {
            var t = UIFactory.Label("T", p, s ?? "", size, a, TL, TL, new Vector2(x, -y), new Vector2(w, h), c);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            return t;
        }

        static Image Box(Transform p, float x, float y, float w, float h, Color c)
        {
            var i = UIFactory.Panel("Box", p, TL, TL, new Vector2(x, -y), new Vector2(w, h), c);
            i.raycastTarget = false;
            return i;
        }

        static void Bar(Transform p, float x, float y, float w, float v, Color c)
        {
            Box(p, x, y, w, 12f, new Color(1f, 1f, 1f, 0.1f));
            Box(p, x, y, w * Mathf.Clamp01(v), 12f, c);
        }

        static Button Btn(Transform p, string label, float x, float y, float w, float h, int font, Action a) =>
            MenuTheme.StyledButton((RectTransform)p, "B", label, TL, TL, new Vector2(x, -y), new Vector2(w, h), font, a);

        static string Year(int season) => (W.baseYear + Mathf.Max(0, season - 1)).ToString();
        static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
        static string Fans(float f) => f >= 1000000f ? (f / 1000000f).ToString("0.0") + " mi" : f >= 1000f ? (f / 1000f).ToString("0.#") + " mil" : ((int)f).ToString();
        static string TeamName(WDriver d) { var t = CareerWorld.TeamOf(d); return t != null ? t.name : d.series == "RES" ? "Reserva" : d.series == "RET" ? "Aposentado" : "-"; }
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        static string Philosophy(string p)
        {
            switch (p)
            {
                case "works": return "Montadora";
                case "grandee": return "Tradicional";
                case "ladder": return "Formadora";
                case "backmarker": return "Fundo do grid";
                default: return "Independente";
            }
        }

        static string Academy(string a)
        {
            switch (a)
            {
                case "backed": return "Apoiado";
                case "warned": return "<color=#ffb040>Advertido</color>";
                case "dropped": return "<color=#ff6050>Dispensado</color>";
                case "graduated": return "<color=#60e070>Formado</color>";
                default: return "Sem academia";
            }
        }

        static readonly string[] TraitGood = { "Rei da classificação", "Ritmo de corrida forte", "Metronômico", "Poupa pneus", "Mestre da chuva", "Bom de briga", "Agressivo", "Adapta rápido", "Rodado", "Ímã de patrocínio" };
        static readonly string[] TraitBad = { "Sofre na classificação", "Ritmo de corrida fraco", "Inconsistente", "Gasta pneus", "Sofre na chuva", "Fraco na disputa", "Tímido no ataque", "Demora a se adaptar", "Inexperiente", "Pouco apelo comercial" };

        /// <summary>Traços pelos atributos: forças (≥ 82) e o maior ponto fraco (≤ 62).</summary>
        internal static List<string> Traits(WDriver d)
        {
            var list = new List<string>();
            var idx = Enumerable.Range(0, 10).OrderByDescending(i => d.at[i]).ToList();
            foreach (int i in idx) if (d.at[i] >= 82 && list.Count < 3) list.Add(TraitGood[i]);
            int worst = idx[idx.Count - 1];
            if (d.at[worst] <= 62) list.Add("<color=#ff7060>" + TraitBad[worst] + "</color>");
            return list;
        }

        /// <summary>Rótulo de "imprensa" do piloto (nível + fase da carreira).</summary>
        internal static string Label(WDriver d)
        {
            int age = CareerWorld.Age(d);
            float ov = CareerWorld.Overall(d);
            if (d.titlesF1 >= 3) return "Lenda viva";
            if (d.titlesF1 >= 1) return age >= 34 ? "Campeão veterano" : "Campeão";
            string lvl = ov >= 89 ? "Fora de série" : ov >= 85 ? "Elite" : ov >= 81 ? "Sólido" : ov >= 77 ? "Mediano" : "Luta pela vaga";
            if (age <= 22 && d.potential >= 88f) return "Joia · " + lvl;
            if (age >= 34) return "Veterano · " + lvl;
            return lvl;
        }

        static string Personality(WDriver d)
        {
            var p = new List<string>();
            if (d.loyalty >= 0.65f) p.Add("leal"); else if (d.loyalty <= 0.35f) p.Add("mercenário");
            if (d.ambition >= 0.7f) p.Add("ambicioso");
            if (d.passion >= 0.75f) p.Add("apaixonado pelo esporte");
            if (d.temperament >= 0.7f) p.Add("esquentado"); else if (d.temperament <= 0.35f) p.Add("frio sob pressão");
            return p.Count > 0 ? string.Join(", ", p) : "equilibrado";
        }

        static string Arrow(float form) => form >= 0.35f ? "<color=#60e070>▲</color>" : form <= -0.35f ? "<color=#ff6050>▼</color>" : "<color=#9aa0aa>■</color>";

        // ---------------- VOCÊ ----------------
        static void You(RectTransform b)
        {
            var me = CareerWorld.Player;
            if (me == null) return;
            var t = CareerWorld.TeamOf(me);
            var d = CareerMode.data;
            Txt(b, d.pilotName.ToUpperInvariant(), 40, 0, 0, 900, 50, MenuTheme.Text);
            Txt(b, CareerWorld.Age(me) + " anos · " + me.nat + " · " + (t != null ? t.name + " (" + me.series + ")" : "sem equipe") + " · " + Label(me), 22, 0, 52, 900, 30, MenuTheme.TextDim);
            Txt(b, "Traços: " + string.Join("  ·  ", Traits(me)), 20, 0, 86, 900, 28, MenuTheme.Text);
            Txt(b, "Personalidade: " + Personality(me), 20, 0, 116, 900, 28, MenuTheme.TextDim);

            float y = 160f;
            void Stat(string k, string v, float yy) { Txt(b, k, 20, 0, yy, 330, 28, MenuTheme.TextDim); Txt(b, v, 20, 330, yy, 560, 28, MenuTheme.Text); }
            string contract = W.notRenewed ? "<color=#ff6050>NÃO RENOVADO — acaba no fim do ano</color>" : "até " + Year(me.contractEnd) + (W.number1 ? " · 1º piloto" : "");
            Stat("Contrato", contract, y); y += 32f;
            Stat("Meta da diretoria", "P" + W.boardTarget + (W.ultimatum ? "  <color=#ff6050>· ULTIMATO</color>" : ""), y); y += 32f;
            Txt(b, "Confiança da diretoria", 20, 0, y, 330, 28, MenuTheme.TextDim);
            Bar(b, 330, y + 8, 300, W.boardConfidence, W.boardConfidence < 0.25f ? Bad : W.boardConfidence > 0.7f ? Good : MenuTheme.Accent);
            Txt(b, Pct(W.boardConfidence), 20, 645, y, 120, 28, MenuTheme.Text); y += 32f;
            Stat("Academia", (string.IsNullOrEmpty(d.academyName) ? "-" : d.academyName) + " · " + Academy(W.academyStanding), y); y += 32f;
            Txt(b, "Forma", 20, 0, y, 330, 28, MenuTheme.TextDim);
            Bar(b, 330, y + 8, 300, (me.form + 1f) * 0.5f, me.form >= 0 ? Good : Bad); Txt(b, Arrow(me.form), 20, 645, y, 60, 28, MenuTheme.Text); y += 32f;
            Txt(b, "Moral", 20, 0, y, 330, 28, MenuTheme.TextDim);
            Bar(b, 330, y + 8, 300, me.morale, MenuTheme.Accent); Txt(b, Pct(me.morale), 20, 645, y, 120, 28, MenuTheme.Text); y += 32f;
            Txt(b, "Energia", 20, 0, y, 330, 28, MenuTheme.TextDim);
            Bar(b, 330, y + 8, 300, W.energy, new Color(0.35f, 0.65f, 1f)); Txt(b, Pct(W.energy), 20, 645, y, 120, 28, MenuTheme.Text); y += 32f;
            Stat("Superlicença", W.licencePoints.ToString("0.#") + " / 12 pontos" + (W.banRounds > 0 ? "  <color=#ff6050>· SUSPENSO (" + W.banRounds + ")</color>" : ""), y); y += 32f;
            if (W.injuryRounds > 0) { Stat("Lesão", "<color=#ff6050>" + W.injuryText + " · fora por " + W.injuryRounds + " corrida(s)</color>", y); y += 32f; }
            Stat("Legado", W.legacyTier + " · " + me.wins + " vitórias · " + me.podiums + " pódios · " + (me.titlesF1 + me.titlesF2) + " títulos", y); y += 32f;
            var nem = CareerWorld.Driver(W.nemesisId);
            Stat("Nêmesis", nem != null ? nem.name + " (" + TeamName(nem) + ")" : "ninguém (ainda)", y); y += 32f;
            Stat("Fãs", Fans(W.following) + " seguidores · aprovação " + Pct(W.sentiment) + " · perfil " + CareerWorld.PressPersona(), y); y += 32f;
            string pot = me.potential >= 92f ? "altíssimo" : me.potential >= 88f ? "alto" : me.potential >= 84f ? "bom" : "limitado";
            Stat("Potencial (estimativa)", pot + " · pico por volta dos " + me.peakAge + " anos", y);

            // Coluna direita: atributos, promessas, conquistas e o que os fãs dizem.
            float x = 960f;
            Txt(b, "ATRIBUTOS", 22, x, 0, 500, 30, MenuTheme.Accent);
            for (int i = 0; i < 10; i++)
            {
                float yy = 36f + i * 30f;
                Txt(b, CareerWorld.AttrNames[i], 19, x, yy, 230, 28, MenuTheme.TextDim);
                Bar(b, x + 240, yy + 8, 300, me.at[i] / 100f, me.at[i] >= 82 ? Good : me.at[i] <= 62 ? Bad : MenuTheme.Accent);
                Txt(b, me.at[i].ToString(), 19, x + 555, yy, 60, 28, MenuTheme.Text);
            }
            float py = 350f;
            Txt(b, "PROMESSAS EM ABERTO", 20, x, py, 500, 28, MenuTheme.Accent); py += 30f;
            if (W.promises.Count == 0) { Txt(b, "Nenhuma.", 18, x, py, 800, 26, MenuTheme.TextDim); py += 28f; }
            foreach (var pr in W.promises.Take(3)) { Txt(b, "• " + pr.text, 18, x, py, 840, 26, MenuTheme.Text); py += 26f; }
            py += 8f;
            Txt(b, "CONQUISTAS", 20, x, py, 500, 28, MenuTheme.Accent); py += 30f;
            if (W.achievements.Count == 0) { Txt(b, "Nenhuma ainda.", 18, x, py, 800, 26, MenuTheme.TextDim); py += 28f; }
            foreach (var a in W.achievements.AsEnumerable().Reverse().Take(4)) { Txt(b, "★ " + a, 18, x, py, 840, 26, MenuTheme.Text); py += 26f; }
            py += 8f;
            Txt(b, "O QUE OS FÃS ESTÃO DIZENDO", 20, x, py, 600, 28, MenuTheme.Accent); py += 30f;
            string promise = W.promises.Count > 0 ? W.promises[0].text : "";
            // Contexto para os fãs (v108): equipe, categoria, posição no campeonato, vitórias e pódios da temporada.
            string fanTeam = CareerMode.TeamOfDriver(d.pilotName) ?? "";
            int champPos = 0;
            if (d.standings != null && d.standings.Count > 0)
            {
                var ordered = d.standings.OrderByDescending(s => s.points).ToList();
                int idx = ordered.FindIndex(s => s.isPlayer || s.driverName == d.pilotName);
                if (idx >= 0 && ordered[idx].points > 0) champPos = idx + 1;
            }
            for (int i = 0; i < 2; i++)
                Txt(b, "\"" + CareerWorldText.FanPost(W.sentiment, me.form, (int)W.following, d.pilotName, promise, d.round * 7 + i * 13 + d.season,
                    fanTeam, d.category, champPos, d.seasonWins, d.seasonPodiums) + "\"", 17, x, py + i * 46f, 840, 44, MenuTheme.TextDim);
        }

        // ---------------- BANDEJA ----------------
        static void Inbox(RectTransform b)
        {
            for (int i = 0; i < NewsCatNames.Length; i++)
            {
                int idx = i;
                var btn = Btn(b, NewsCatNames[i], i * 202f, 0, 192, 48, 15, () => { newsFilter = idx; newsPage = 0; Show(); });
                if (i == newsFilter) btn.GetComponent<Image>().color = RowHi;
            }
            var list = InboxList();
            const int per = 7;
            int pages = Mathf.Max(1, (list.Count + per - 1) / per);
            newsPage = Mathf.Clamp(newsPage, 0, pages - 1);
            float y = 64f;
            if (list.Count == 0) Txt(b, "Nada por aqui ainda.", 22, 0, y, 900, 30, MenuTheme.TextDim);
            foreach (var n in list.Skip(newsPage * per).Take(per))
            {
                Box(b, 0, y, 1840, 82, n.read ? Row : new Color(1f, 0.48f, 0.1f, 0.12f));
                string meta = Year(n.season) + " · R" + (n.round + 1) + " · " + n.cat.ToUpperInvariant() + (string.IsNullOrEmpty(n.from) ? "" : " · " + n.from);
                Txt(b, (n.read ? "" : "<color=#ff8a2a>● </color>") + n.headline, 22, 16, y + 6, 1500, 30, MenuTheme.Text);
                Txt(b, meta, 15, 1440, y + 10, 390, 24, MenuTheme.TextDim, TextAnchor.UpperRight);
                Txt(b, Preview(n.body), 17, 16, y + 40, 1800, 40, MenuTheme.TextDim);
                // a linha inteira abre o e-mail; só conta como lido depois de aberto
                var hit = UIFactory.Panel("Open", b, TL, TL, new Vector2(0f, -y), new Vector2(1840f, 82f), new Color(1f, 1f, 1f, 0f));
                hit.raycastTarget = true;
                var item = n;
                hit.gameObject.AddComponent<Button>().onClick.AddListener(() => { reading = item; Show(); });
                y += 90f;
            }
            Txt(b, "PÁGINA " + (newsPage + 1) + "/" + pages + " · " + list.Count + " notícia(s)", 18, 0, 700, 600, 28, MenuTheme.TextDim);
            Btn(b, "◄", 1620, 690, 100, 48, 22, () => { newsPage = Mathf.Max(0, newsPage - 1); Show(); });
            Btn(b, "►", 1740, 690, 100, 48, 22, () => { newsPage++; Show(); });
        }

        static List<WNews> InboxList() =>
            W.news.Where(n => newsFilter == 0 || n.cat == NewsCats[newsFilter]).AsEnumerable().Reverse().ToList();

        static string Preview(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            return body.Length <= 150 ? body : body.Substring(0, 147).TrimEnd() + "…";
        }

        /// <summary>E-mail aberto: remetente, data, assunto e o texto inteiro. Abrir marca como lido.</summary>
        static void ReadMail(RectTransform b)
        {
            var n = reading;
            if (!n.read) { n.read = true; CareerMode.SaveQuiet(); }
            var list = InboxList();
            int idx = list.IndexOf(n);
            int cat = Array.IndexOf(NewsCats, n.cat);
            Box(b, 0, 0, 1840, 610, Row);
            Txt(b, (cat > 0 ? NewsCatNames[cat] : (n.cat ?? "").ToUpperInvariant()) + " · " + Year(n.season) + " · RODADA " + (n.round + 1), 18, 30, 20, 1700, 26, MenuTheme.TextDim);
            Txt(b, "De: " + (string.IsNullOrEmpty(n.from) ? "Paddock" : n.from), 22, 30, 52, 1700, 30, MenuTheme.Accent);
            Txt(b, n.headline, 34, 30, 96, 1780, 92, MenuTheme.Text);
            Txt(b, n.body, 26, 30, 200, 1780, 390, MenuTheme.Text);
            Btn(b, "◄ VOLTAR À BANDEJA", 0, 640, 440, 60, 22, () => { reading = null; Show(); });
            if (idx > 0) Btn(b, "◄ ANTERIOR", 1180, 640, 320, 60, 22, () => { reading = list[idx - 1]; Show(); });
            if (idx >= 0 && idx < list.Count - 1) Btn(b, "PRÓXIMO ►", 1520, 640, 320, 60, 22, () => { reading = list[idx + 1]; Show(); });
        }

        // ---------------- IMPRENSA ----------------
        static void Press(RectTransform b)
        {
            Txt(b, "Perfil na imprensa: " + CareerWorld.PressPersona() + "   ·   respostas: ousado " + W.tones[0] + " · humilde " + W.tones[1] + " · provocador " + W.tones[2] + " · leal " + W.tones[3] + " · frio " + W.tones[4], 20, 0, 0, 1840, 30, MenuTheme.TextDim);
            if (!W.hasPress || W.pendingPress == null || W.pendingPress.options == null || W.pendingPress.options.Length == 0)
            {
                Txt(b, "Nenhuma entrevista pendente. Depois de cada corrida um dos 12 jornalistas do paddock te procura — o jeito de responder muda o seu perfil, os fãs e a relação com a equipe. Respostas com promessa ficam anotadas.", 24, 0, 60, 1500, 120, MenuTheme.Text);
                return;
            }
            var p = W.pendingPress;
            Box(b, 0, 50, 1840, 150, Row);
            Txt(b, p.journalist + " · " + (p.outlet ?? "").Split('|')[0], 26, 20, 62, 1200, 34, MenuTheme.Accent);
            Txt(b, p.slant, 18, 20, 98, 1200, 26, MenuTheme.TextDim);
            Txt(b, "\"" + p.question + "\"", 30, 20, 130, 1800, 60, MenuTheme.Text);
            string[] toneNames = { "ousado", "humilde", "provocador", "leal", "frio" };
            for (int i = 0; i < p.options.Length; i++)
            {
                int idx = i;
                string tag = (p.tones != null && i < p.tones.Length ? toneNames[Mathf.Clamp(p.tones[i], 0, 4)] : "") + (p.promise != null && i < p.promise.Length && !string.IsNullOrEmpty(p.promise[i]) ? " · PROMESSA" : "");
                Btn(b, p.options[i] + "   (" + tag + ")", 0, 230 + i * 100f, 1840, 84, 24, () => { CareerWorld.AnswerPress(idx); Show(); });
            }
        }

        // ---------------- SEMANA ----------------
        static void Week(RectTransform b)
        {
            bool done = W.prepRound == CareerMode.data.round;
            Txt(b, done ? "Atividade desta semana: " + WeekName(W.prepDone) + ". Na próxima rodada você escolhe de novo." : "Escolha UMA atividade para esta semana (antes do fim de semana de corrida).", 24, 0, 0, 1840, 34, MenuTheme.Text);
            Txt(b, "Energia", 20, 0, 50, 200, 28, MenuTheme.TextDim);
            Bar(b, 120, 58, 420, W.energy, new Color(0.35f, 0.65f, 1f));
            Txt(b, Pct(W.energy), 20, 560, 50, 100, 28, MenuTheme.Text);
            var acts = new[]
            {
                ("treino", "TREINO PUXADO", "Forma ↑ (rende mais com energia alta) · moral ↓ · energia ↓"),
                ("descanso", "DESCANSO", "Moral ↑ · energia recuperada"),
                ("midia", "DIA DE MÍDIA", "+1.500 seguidores · marketing ↑ · energia ↓ um pouco"),
                ("patrocinio", "EVENTO DE PATROCINADOR", "Orçamento da equipe ↑ (desenvolvimento do carro)")
            };
            for (int i = 0; i < acts.Length; i++)
            {
                var a = acts[i];
                float x = (i % 2) * 930f, y = 110f + (i / 2) * 190f;
                Box(b, x, y, 900, 170, W.prepDone == a.Item1 && done ? RowHi : Row);
                Txt(b, a.Item2, 26, x + 20, y + 14, 860, 34, MenuTheme.Text);
                Txt(b, a.Item3, 19, x + 20, y + 54, 860, 50, MenuTheme.TextDim);
                if (!done) Btn(b, "ESCOLHER", x + 620, y + 104, 260, 54, 20, () => { weekMsg = CareerWorld.ChoosePrep(a.Item1); Show(); });
            }
            if (!string.IsNullOrEmpty(weekMsg)) Txt(b, weekMsg, 22, 0, 520, 1840, 30, MenuTheme.Accent);
        }

        static string WeekName(string k) => k == "treino" ? "treino puxado" : k == "descanso" ? "descanso" : k == "midia" ? "dia de mídia" : "evento de patrocinador";

        // ---------------- PROPOSTAS ----------------
        static void Offers(RectTransform b)
        {
            var me = CareerWorld.Player;
            var mine = CareerWorld.TeamOf(me);
            Txt(b, "Sua equipe: " + (mine != null ? mine.name + " · carro " + mine.car.ToString("0.0") : "-") + " · contrato até " + Year(me.contractEnd) + (W.notRenewed ? " · <color=#ff6050>não renovado</color>" : ""), 22, 0, 0, 1840, 30, MenuTheme.Text);
            var list = new List<int>();
            for (int i = 0; i < W.offers.Count; i++) if (W.offers[i].midseason) list.Add(i);
            if (list.Count == 0)
            {
                Txt(b, "Sem propostas no momento. Equipes melhores só aparecem no meio do ano para quem anda ACIMA do carro (forma alta). As propostas de contrato chegam no fim da temporada.", 22, 0, 50, 1500, 90, MenuTheme.TextDim);
                return;
            }
            float y = 60f;
            foreach (int i in list)
            {
                var o = W.offers[i];
                int idx = i;
                Box(b, 0, y, 1840, 200, Row);
                Txt(b, o.teamName.ToUpperInvariant() + " (" + o.series + ") · carro " + o.car.ToString("0.0") + " · " + o.years + " ano(s)", 28, 20, y + 14, 1500, 36, MenuTheme.Accent);
                Txt(b, o.why, 20, 20, y + 56, 1500, 56, MenuTheme.Text);
                Txt(b, "Troca IMEDIATA: você corre por eles a partir da próxima rodada (pontos ficam com você). Vale até a rodada " + (o.expiresRound + 1) + ".", 18, 20, y + 116, 1500, 50, MenuTheme.TextDim);
                Btn(b, "ACEITAR", 1560, y + 30, 260, 64, 22, () => { CareerWorld.RespondMidseason(idx, true); CareerFlow.BackToHub(); });
                Btn(b, "RECUSAR", 1560, y + 110, 260, 64, 22, () => { CareerWorld.RespondMidseason(idx, false); Show(); });
                y += 220f;
            }
        }

        // ---------------- PILOTOS ----------------
        static void SeriesToggle(RectTransform b, bool reserves)
        {
            var opts = reserves ? new[] { "F1", "F2", "RES", "RET" } : new[] { "F1", "F2" };
            string[] names = { "FÓRMULA 1", "FÓRMULA 2", "SEM VAGA", "APOSENTADOS" };
            for (int i = 0; i < opts.Length; i++)
            {
                string k = opts[i];
                var btn = Btn(b, names[i], i * 230f, 0, 220, 48, 17, () => { seriesView = k; listPage = 0; Show(); });
                if (seriesView == k) btn.GetComponent<Image>().color = RowHi;
            }
        }

        static void Drivers(RectTransform b)
        {
            SeriesToggle(b, true);
            List<WDriver> list;
            if (seriesView == "F1" || seriesView == "F2")
                list = CareerWorld.InSeries(seriesView).OrderBy(d => CareerWorld.CarRank(CareerWorld.TeamOf(d))).ThenByDescending(d => CareerWorld.Rating(d)).ToList();
            else
                list = W.drivers.Where(d => d.series == seriesView).OrderByDescending(d => seriesView == "RET" ? d.retiredSeason : 0).ThenByDescending(d => CareerWorld.Rating(d)).ToList();
            const int per = 24;
            int pages = Mathf.Max(1, (list.Count + per - 1) / per);
            listPage = Mathf.Clamp(listPage, 0, pages - 1);
            Txt(b, "Toque num piloto para ver o perfil.", 18, 980, 12, 860, 26, MenuTheme.TextDim, TextAnchor.UpperRight);
            var page = list.Skip(listPage * per).Take(per).ToList();
            for (int i = 0; i < page.Count; i++)
            {
                var d = page[i];
                float x = (i / 12) * 930f, y = 62f + (i % 12) * 50f;
                bool me = d.id == CareerWorld.PlayerId;
                var btn = UIFactory.Button("Drv", b, "", TL, TL, new Vector2(x, -y), new Vector2(910f, 46f), () => Profile(d), 18);
                btn.GetComponent<Image>().color = me ? RowHi : Row;
                var lbl = btn.GetComponentInChildren<Text>(true);
                lbl.supportRichText = true;
                lbl.alignment = TextAnchor.MiddleLeft;
                ((RectTransform)lbl.transform).sizeDelta = new Vector2(880f, 46f);
                string info = seriesView == "RET" ? "aposentou em " + Year(d.retiredSeason) : TeamName(d);
                lbl.text = Arrow(d.form) + "  " + d.name + " <color=#8a909a>(" + d.nat + ", " + CareerWorld.Age(d) + ")</color>   " + info + "   <color=#ff8a2a>" + CareerWorld.Overall(d).ToString("0") + "</color>   <color=#8a909a>" + Label(d) + "</color>";
            }
            if (pages > 1)
            {
                Txt(b, "PÁGINA " + (listPage + 1) + "/" + pages, 18, 0, 680, 400, 28, MenuTheme.TextDim);
                Btn(b, "◄", 1620, 670, 100, 48, 22, () => { listPage = Mathf.Max(0, listPage - 1); Show(); });
                Btn(b, "►", 1740, 670, 100, 48, 22, () => { listPage++; Show(); });
            }
        }

        static void Profile(WDriver d)
        {
            var s = CareerFlow.NewScreen();
            MenuTheme.Header(s, d.name.ToUpperInvariant());
            var b = UIFactory.Rect("ProfileBody", s, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            b.offsetMin = new Vector2(40f, 112f);
            b.offsetMax = new Vector2(-40f, -150f);
            var t = CareerWorld.TeamOf(d);
            Txt(b, CareerWorld.Age(d) + " anos · " + d.nat + " · " + TeamName(d) + (t != null ? " (" + d.series + ")" : "") + " · " + Label(d), 24, 0, 0, 1840, 34, MenuTheme.Text);
            Txt(b, "Traços: " + string.Join("  ·  ", Traits(d)), 20, 0, 40, 1840, 28, MenuTheme.Text);
            Txt(b, "Personalidade: " + Personality(d) + (d.comebacks > 0 ? " · já voltou da aposentadoria" : "") + (d.newgen ? " · revelado pela base" : ""), 20, 0, 72, 1840, 28, MenuTheme.TextDim);
            string contract = d.series == "F1" || d.series == "F2" ? "contrato até " + Year(d.contractEnd) : d.series == "RET" ? "aposentado em " + Year(d.retiredSeason) : "sem vaga";
            Txt(b, contract + " · forma " + Arrow(d.form) + " · moral " + Pct(d.morale), 20, 0, 104, 1840, 28, MenuTheme.TextDim);
            Txt(b, "CARREIRA: " + d.starts + " largadas · " + d.wins + " vitórias · " + d.podiums + " pódios · " + d.poles + " poles · " + d.titlesF1 + " título(s) F1 · " + d.titlesF2 + " título(s) F2 · " + d.f1Seasons + " temporada(s) na F1", 20, 0, 140, 1840, 28, MenuTheme.Text);
            for (int i = 0; i < 10; i++)
            {
                float y = 190f + i * 34f;
                Txt(b, CareerWorld.AttrNames[i], 20, 0, y, 240, 30, MenuTheme.TextDim);
                Bar(b, 250, y + 9, 360, d.at[i] / 100f, d.at[i] >= 82 ? Good : d.at[i] <= 62 ? Bad : MenuTheme.Accent);
                Txt(b, d.at[i].ToString(), 20, 625, y, 60, 30, MenuTheme.Text);
            }
            Txt(b, "TEMPORADAS", 22, 960, 190, 600, 30, MenuTheme.Accent);
            var hist = d.history.AsEnumerable().Reverse().Take(10).ToList();
            if (hist.Count == 0) Txt(b, "Primeira temporada no mundo.", 19, 960, 226, 800, 28, MenuTheme.TextDim);
            for (int i = 0; i < hist.Count; i++)
            {
                var p = hist[i].Split('|');
                if (p.Length < 5) continue;
                int.TryParse(p[0], out int season);
                Txt(b, Year(season) + "   " + p[1] + "   " + p[2] + "   P" + p[3] + "   " + p[4] + " pts", 19, 960, 226 + i * 30f, 880, 28, MenuTheme.Text);
            }
            var r = W.rivalries.FirstOrDefault(x => (x.a == d.id && x.b == CareerWorld.PlayerId) || (x.b == d.id && x.a == CareerWorld.PlayerId));
            if (r != null && d.id != CareerWorld.PlayerId)
            {
                bool meA = r.a == CareerWorld.PlayerId;
                Txt(b, "RIVALIDADE COM VOCÊ: " + Pct(r.heat) + (r.nemesis ? " · NÊMESIS" : "") + " · você à frente " + (meA ? r.aheadA : r.aheadB) + "×" + (meA ? r.aheadB : r.aheadA) + " · " + r.contacts + " toque(s)" + (string.IsNullOrEmpty(r.origin) ? "" : " · começou em: " + r.origin), 20, 0, 550, 1840, 30, Bad);
            }
            var bb = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "ProfileBack", "VOLTAR", bb, bb, new Vector2(0f, 36f), new Vector2(300f, 64f), 26, Show);
        }

        // ---------------- EQUIPES ----------------
        static void Teams(RectTransform b)
        {
            if (seriesView != "F1" && seriesView != "F2") seriesView = CareerMode.data.category == "F1" ? "F1" : "F2";
            SeriesToggle(b, false);
            Txt(b, "POS   EQUIPE                               CARRO   TIPO              ORÇAMENTO   CHEFE                 PILOTOS", 17, 0, 62, 1840, 26, MenuTheme.TextDim);
            var teams = CareerWorld.TeamsOf(seriesView).OrderBy(t => CareerWorld.CarRank(t)).ToList();
            var me = CareerWorld.Player;
            for (int i = 0; i < teams.Count; i++)
            {
                var t = teams[i];
                float y = 92f + i * 50f;
                bool mine = me != null && me.series == t.series && me.team == t.slot;
                Box(b, 0, y, 1840, 46, mine ? RowHi : Row);
                string name = t.name + (t.name != t.originalName && !string.IsNullOrEmpty(t.originalName) ? " <color=#8a909a>(ex-" + t.originalName + ")</color>" : "");
                var drivers = W.drivers.Where(x => x.series == t.series && x.team == t.slot).Select(x => x.name).ToList();
                Txt(b, (i + 1) + "º", 20, 10, y + 10, 60, 28, i < 3 ? new Color(1f, 0.82f, 0.3f) : MenuTheme.TextDim);
                Txt(b, name, 20, 70, y + 10, 480, 28, MenuTheme.Text);
                Txt(b, t.car.ToString("0.0"), 20, 560, y + 10, 90, 28, MenuTheme.Accent);
                Txt(b, Philosophy(t.philosophy), 18, 660, y + 11, 200, 28, MenuTheme.TextDim);
                Bar(b, 870, y + 18, 140, t.budget / 100f, new Color(0.35f, 0.65f, 1f));
                Txt(b, t.principal, 18, 1030, y + 11, 280, 28, MenuTheme.TextDim);
                Txt(b, string.Join(" · ", drivers), 18, 1320, y + 11, 520, 28, MenuTheme.Text);
            }
        }

        // ---------------- RELAÇÕES ----------------
        static void Relations(RectTransform b)
        {
            Txt(b, "SUAS RIVALIDADES", 22, 0, 0, 900, 30, MenuTheme.Accent);
            var mineR = W.rivalries.Where(r => (r.a == CareerWorld.PlayerId || r.b == CareerWorld.PlayerId) && r.heat > 0.05f).OrderByDescending(r => r.heat).Take(8).ToList();
            if (mineR.Count == 0) Txt(b, "Ninguém ainda. Rivalidades nascem de disputas de posição, toques e companheiros de equipe.", 19, 0, 40, 880, 60, MenuTheme.TextDim);
            for (int i = 0; i < mineR.Count; i++)
            {
                var r = mineR[i];
                bool meA = r.a == CareerWorld.PlayerId;
                var o = CareerWorld.Driver(meA ? r.b : r.a);
                if (o == null) continue;
                float y = 40f + i * 76f;
                Box(b, 0, y, 900, 70, r.nemesis ? new Color(1f, 0.25f, 0.2f, 0.18f) : Row);
                Txt(b, o.name + " <color=#8a909a>(" + TeamName(o) + ")</color>" + (r.nemesis ? "  <color=#ff5040>NÊMESIS</color>" : r.flared ? "  <color=#ffb040>ACESA</color>" : ""), 20, 14, y + 6, 640, 28, MenuTheme.Text);
                Bar(b, 660, y + 14, 200, r.heat, r.heat >= 0.7f ? Bad : MenuTheme.Accent);
                Txt(b, "você à frente " + (meA ? r.aheadA : r.aheadB) + "×" + (meA ? r.aheadB : r.aheadA) + " · " + r.contacts + " toque(s)" + (string.IsNullOrEmpty(r.origin) ? "" : " · " + r.origin), 16, 14, y + 38, 870, 26, MenuTheme.TextDim);
            }
            Txt(b, "NO PADDOCK", 22, 940, 0, 900, 30, MenuTheme.Accent);
            var world = W.rivalries.Where(r => r.a != CareerWorld.PlayerId && r.b != CareerWorld.PlayerId && r.heat > 0.2f).OrderByDescending(r => r.heat).Take(5).ToList();
            float wy = 40f;
            if (world.Count == 0) { Txt(b, "Clima calmo entre os outros pilotos.", 19, 940, wy, 880, 28, MenuTheme.TextDim); wy += 34f; }
            foreach (var r in world)
            {
                var a = CareerWorld.Driver(r.a); var c = CareerWorld.Driver(r.b);
                if (a == null || c == null) continue;
                Txt(b, a.name + " × " + c.name + (r.nemesis ? "  <color=#ff5040>(nêmesis)</color>" : ""), 19, 940, wy, 640, 28, MenuTheme.Text);
                Bar(b, 1600, wy + 8, 220, r.heat, MenuTheme.Accent);
                wy += 36f;
            }
            wy += 20f;
            Txt(b, "GUERRA DE EQUIPES", 22, 940, wy, 900, 30, MenuTheme.Accent); wy += 40f;
            var wars = W.teamRivalries.Where(x => x.heat > 0.1f).OrderByDescending(x => x.heat).Take(5).ToList();
            if (wars.Count == 0) Txt(b, "Nenhuma briga aberta na tabela de equipes.", 19, 940, wy, 880, 28, MenuTheme.TextDim);
            foreach (var tw in wars)
            {
                Txt(b, tw.series + " · " + tw.a + " × " + tw.b + (tw.flared ? "  <color=#ffb040>(aberta)</color>" : ""), 19, 940, wy, 640, 28, MenuTheme.Text);
                Bar(b, 1600, wy + 8, 220, tw.heat, MenuTheme.Accent);
                wy += 36f;
            }
        }

        // ---------------- ARQUIVO ----------------
        static void Archive(RectTransform b)
        {
            Txt(b, "CAMPEÕES", 22, 0, 0, 900, 30, MenuTheme.Accent);
            var seasons = W.champions.Select(c => c.season).Distinct().OrderByDescending(x => x).ToList();
            const int per = 12;
            int pages = Mathf.Max(1, (seasons.Count + per - 1) / per);
            archivePage = Mathf.Clamp(archivePage, 0, pages - 1);
            if (seasons.Count == 0) Txt(b, "A primeira temporada ainda não acabou.", 19, 0, 40, 880, 28, MenuTheme.TextDim);
            var pageSeasons = seasons.Skip(archivePage * per).Take(per).ToList();
            for (int i = 0; i < pageSeasons.Count; i++)
            {
                int season = pageSeasons[i];
                var f1 = W.champions.FirstOrDefault(c => c.season == season && c.series == "F1");
                var f2 = W.champions.FirstOrDefault(c => c.season == season && c.series == "F2");
                float y = 40f + i * 46f;
                bool mine = (f1 != null && f1.driver == CareerMode.data.pilotName) || (f2 != null && f2.driver == CareerMode.data.pilotName);
                Box(b, 0, y, 900, 42, mine ? RowHi : Row);
                Txt(b, Year(season), 19, 12, y + 9, 80, 26, MenuTheme.TextDim);
                Txt(b, "F1: " + (f1 != null ? f1.driver + " <color=#8a909a>(" + f1.team + ", " + f1.points + ")</color>" : "-"), 18, 90, y + 9, 420, 26, MenuTheme.Text);
                Txt(b, "F2: " + (f2 != null ? f2.driver + " <color=#8a909a>(" + f2.team + ", " + f2.points + ")</color>" : "-"), 18, 500, y + 9, 400, 26, MenuTheme.Text);
            }
            if (pages > 1)
            {
                Btn(b, "◄", 0, 610, 100, 44, 22, () => { archivePage = Mathf.Max(0, archivePage - 1); Show(); });
                Btn(b, "►", 120, 610, 100, 44, 22, () => { archivePage++; Show(); });
            }
            // Recordes de todos os tempos (todos os pilotos do mundo, inclusive aposentados).
            float x = 960f;
            Txt(b, "RECORDES", 22, x, 0, 880, 30, MenuTheme.Accent);
            void Top(string title, Func<WDriver, int> f, float y)
            {
                var top = W.drivers.Where(d => f(d) > 0).OrderByDescending(f).Take(3).ToList();
                Txt(b, title, 19, x, y, 300, 28, MenuTheme.TextDim);
                Txt(b, top.Count == 0 ? "-" : string.Join("  ·  ", top.Select(d => (d.id == CareerWorld.PlayerId ? "<color=#ff8a2a>" + d.name + "</color>" : d.name) + " " + f(d))), 19, x + 210, y, 680, 28, MenuTheme.Text);
            }
            Top("Títulos F1", d => d.titlesF1, 40);
            Top("Vitórias", d => d.wins, 74);
            Top("Pódios", d => d.podiums, 108);
            Top("Poles", d => d.poles, 142);
            Top("Largadas", d => d.starts, 176);
            Top("Títulos F2", d => d.titlesF2, 210);
            var me = CareerWorld.Player;
            Txt(b, "SUA CARREIRA", 22, x, 262, 880, 30, MenuTheme.Accent);
            var hist = me != null ? me.history.AsEnumerable().Reverse().Take(9).ToList() : new List<string>();
            if (hist.Count == 0) Txt(b, "Primeira temporada em andamento.", 19, x, 300, 880, 28, MenuTheme.TextDim);
            for (int i = 0; i < hist.Count; i++)
            {
                var p = hist[i].Split('|');
                if (p.Length < 5) continue;
                int.TryParse(p[0], out int season);
                Txt(b, Year(season) + "   " + p[1] + "   " + p[2] + "   P" + p[3] + "   " + p[4] + " pts", 19, x, 300 + i * 32f, 880, 28, MenuTheme.Text);
            }
        }
    }
}
