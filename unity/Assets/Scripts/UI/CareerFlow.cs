using System;
using System.Collections.Generic;
using RaceX.Career;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Fluxo do modo carreira: criação de piloto, quiz, resultado e hub (F2 → F1).</summary>
    public static class CareerFlow
    {
        static Canvas canvas;
        static RectTransform content;
        static RectTransform current;
        static InputField nameInput;
        static Text createStatus;
        static int nationalityIndex;
        static int quizIndex;
        static readonly int[] quizStyle = new int[4];
        static readonly int[] quizArchetype = new int[CareerQuizData.ArchetypeKeys.Length];
        static readonly int[] quizAcademy = new int[CareerQuizData.AcademyKeys.Length];
        static readonly string[] Nationalities = { "Brasil", "Reino Unido", "Itália", "Alemanha", "Japão", "EUA" };
        static bool showTeamsTab;

        /// <summary>Abre o fluxo: hub (se houver save) ou criação de piloto.</summary>
        static System.Action onClosed;

        public static void Open(System.Action onClosedCallback = null)
        {
            onClosed = onClosedCallback;
            UIFactory.EnsureEventSystem();
            // Remove qualquer canvas de carreira antigo — garante uma única tela, sem sobreposição
            foreach (var old in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (old != null && old.gameObject.name == "CareerCanvas")
                    UnityEngine.Object.Destroy(old.gameObject);
            canvas = UIFactory.CreateCanvas("CareerCanvas", 50);
            var bg = UIFactory.Panel("CareerBg", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.Bg);
            bg.raycastTarget = true;
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            content = UIFactory.Stretch("CareerContent", canvas.transform);
            UIFactory.ApplySafeArea(content, new Rect(0f, 0f, 1f, 1f));
            // Esconde a UI do menu principal enquanto a carreira estiver aberta
            MainMenuUI.SetMenuVisible(false);

            if (CareerMode.HasSave)
            {
                CareerMode.Load();
                ShowHub();
            }
            else
            {
                CareerMode.data = null; // descarta carreira parcial de um quiz interrompido
                ShowCreate();
            }
        }

        static void Close()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            content = null;
            current = null;
            var cb = onClosed;
            onClosed = null;
            // Fechou o menu com corrida viva → devolve o carro ao jogador NO ATO (sem fila do pit
            // nem limite de 80 km/h pendente — bug do "voltei do menu e o carro veio reduzido").
            RaceX.Core.PitLaneSystem.ReleasePlayerNow();
            // Aberto como overlay (ex.: resultados da corrida)? Devolve o controle a quem abriu.
            if (cb != null) { cb(); return; }
            // Restaura a UI do menu principal
            MainMenuUI.SetMenuVisible(true);
        }

        /// <summary>O hub está aberto (overlay da sessão)?</summary>
        public static bool IsOpen => canvas != null;

        /// <summary>Fecha o hub SEM callback e SEM mostrar o menu principal (usado quando o painel
        /// de resultados precisa aparecer por cima — ex.: fim da Q2 pelo relógio real com o hub aberto).</summary>
        public static void ForceCloseSilent()
        {
            if (canvas == null) return;
            UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            content = null;
            current = null;
            onClosed = null;
            Time.timeScale = 1f; // sessão acabou: devolve o fluxo do jogo
        }

        /// <summary>Destroi a tela atual e cria uma nova (padrão Destroy + rebuild).</summary>
        static RectTransform Screen()
        {
            if (current != null) UnityEngine.Object.Destroy(current.gameObject);
            current = UIFactory.Stretch("CareerScreen", content.transform);
            return current;
        }

        /// <summary>Botão VOLTAR no rodapé.</summary>
        static void Footer(RectTransform s, string label, Action onClick)
        {
            var c = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "FooterBack", label, c, c, new Vector2(0f, 36f), new Vector2(280f, 64f), 26, onClick);
        }

        // ---------- Tela de criação ----------
        static void ShowCreate()
        {
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "MODO CARREIRA");

            UIFactory.Label("NameHint", s, "NOME DO PILOTO", 30, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 246f), new Vector2(900f, 40f), MenuTheme.TextDim);
            var bg = UIFactory.Panel("NameBg", s, c, c, new Vector2(0f, 170f), new Vector2(900f, 76f), new Color(1f, 1f, 1f, 0.06f));
            var ph = UIFactory.Label("NamePh", bg.transform, "Digite o nome do piloto", 30, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.TextDim);
            var phRt = (RectTransform)ph.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var txt = UIFactory.Label("NameText", bg.transform, "", 30, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.Text);
            var txtRt = (RectTransform)txt.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            nameInput = bg.gameObject.AddComponent<InputField>();
            nameInput.targetGraphic = bg;
            nameInput.placeholder = ph;
            nameInput.textComponent = txt;

            UIFactory.Label("NatHint", s, "NACIONALIDADE", 30, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 66f), new Vector2(900f, 40f), MenuTheme.TextDim);
            MenuTheme.StyledButton(s, "NatButton", "NACIONALIDADE: " + Nationalities[nationalityIndex].ToUpperInvariant(), c, c, new Vector2(0f, -26f), new Vector2(560f, 72f), 30, CycleNationality);
            MenuTheme.StyledButton(s, "StartTest", "INICIAR TESTE", c, c, new Vector2(0f, -176f), new Vector2(560f, 84f), 32, StartQuiz);
            createStatus = UIFactory.Label("CreateStatus", s, "", 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -296f), new Vector2(1000f, 36f), MenuTheme.Accent);
            Footer(s, "VOLTAR", Close);
        }

        static void CycleNationality()
        {
            nationalityIndex = (nationalityIndex + 1) % Nationalities.Length;
            if (current != null)
            {
                var lbl = current.Find("NatButton/NatButton_Label");
                if (lbl != null) lbl.GetComponent<Text>().text = "NACIONALIDADE: " + Nationalities[nationalityIndex].ToUpperInvariant();
            }
        }

        static void StartQuiz()
        {
            string name = nameInput != null ? nameInput.text.Trim() : "";
            if (name.Length == 0)
            {
                if (createStatus != null) createStatus.text = "Digite um nome para o piloto antes de iniciar.";
                return;
            }
            quizIndex = 0;
            for (int i = 0; i < quizStyle.Length; i++) quizStyle[i] = 0;
            for (int i = 0; i < quizArchetype.Length; i++) quizArchetype[i] = 0;
            for (int i = 0; i < quizAcademy.Length; i++) quizAcademy[i] = 0;
            CareerMode.StartCareer(name, Nationalities[nationalityIndex]);
            ShowPathChoice();
        }

        // ---------- Escolha do caminho: GT ou FÓRMULA ----------
        static bool pathChoiceOpen;
        static void ShowPathChoice()
        {
            pathChoiceOpen = true;
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "SUA CARREIRA");
            MenuTheme.SubHeader(s, "Onde você vai correr? Comece em baixo — ninguém começa na ponta.");

            MenuTheme.StyledButton(s, "PathGT", "GT — CARROS DE RUA", c, c, new Vector2(0f, 40f), new Vector2(640f, 110f), 30, () =>
            {
                pathChoiceOpen = false;
                CareerMode.ChoosePath(true);
                ShowQuiz();
            });
            MenuTheme.StyledButton(s, "PathF1", "FÓRMULA — MONOPOSTO", c, c, new Vector2(0f, -90f), new Vector2(640f, 110f), 30, () =>
            {
                pathChoiceOpen = false;
                CareerMode.ChoosePath(false);
                ShowQuiz();
            });
            var back = MenuTheme.StyledButton(s, "PathBack", "VOLTAR", c, c, new Vector2(0f, -210f), new Vector2(420f, 70f), 26, () =>
            {
                pathChoiceOpen = false;
                ShowCreate();
            });
            back.transform.SetAsLastSibling();
        }

        // ---------- Quiz (uma pergunta por vez) ----------
        static void ShowQuiz()
        {
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            Question q = CareerQuizData.All[quizIndex];
            Footer(s, "VOLTAR", ShowCreate);

            // Barra de progresso fina no topo (14 segmentos).
            const float segW = 84f, gap = 6f;
            float totalW = CareerQuizData.All.Length * (segW + gap) - gap;
            var top = new Vector2(0.5f, 1f);
            for (int i = 0; i < CareerQuizData.All.Length; i++)
            {
                bool filled = i <= quizIndex;
                Color col = filled ? MenuTheme.Accent : new Color(1f, 1f, 1f, 0.1f);
                var seg = UIFactory.Panel("Seg" + i, s, top, top, new Vector2(-totalW / 2f + i * (segW + gap), -170f), new Vector2(segW, 10f), col);
                seg.raycastTarget = false;
            }
            UIFactory.Label("QuestionCount", s, "PERGUNTA " + (quizIndex + 1) + "/" + CareerQuizData.All.Length, 26, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -224f), new Vector2(600f, 36f), MenuTheme.TextDim);
            var promptT = UIFactory.Label("Prompt", s, q.prompt, 34, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -310f), new Vector2(1720f, 170f), MenuTheme.Text);
            promptT.horizontalOverflow = HorizontalWrapMode.Wrap;

            for (int i = 0; i < q.options.Length; i++)
            {
                int opt = i;
                var btn = MenuTheme.StyledButton(s, "Opt" + opt, q.options[opt].text, c, c, new Vector2(0f, -70f - opt * 104f), new Vector2(1400f, 90f), 22, () => Answer(opt));
                var optT = btn.GetComponentInChildren<Text>();
                if (optT != null) optT.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        static void Answer(int opt)
        {
            OptionWeights o = CareerQuizData.All[quizIndex].options[opt];
            quizStyle[0] += o.attack;
            quizStyle[1] += o.feel;
            quizStyle[2] += o.stage;
            quizStyle[3] += o.heat;
            int ai = Array.IndexOf(CareerQuizData.ArchetypeKeys, o.archetype);
            if (ai >= 0) quizArchetype[ai]++;
            int ci = Array.IndexOf(CareerQuizData.AcademyKeys, o.academy);
            if (ci >= 0) quizAcademy[ci]++;
            quizIndex++;
            if (quizIndex >= CareerQuizData.All.Length) FinishQuiz();
            else ShowQuiz();
        }

        static void FinishQuiz()
        {
            CareerMode.ApplyQuiz(
                quizStyle[0], quizStyle[1], quizStyle[2], quizStyle[3],
                MaxKey(CareerQuizData.ArchetypeKeys, quizArchetype),
                MaxKey(CareerQuizData.AcademyKeys, quizAcademy));
            ShowResult();
        }

        /// <summary>Chave com maior contagem; empate resolve para a primeira da ordem.</summary>
        static string MaxKey(string[] keys, int[] counts)
        {
            int best = 0;
            for (int i = 1; i < keys.Length; i++)
                if (counts[i] > counts[best]) best = i;
            return keys[best];
        }

        // ---------- Tela de resultado ----------
        static void ShowResult()
        {
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            CareerData d = CareerMode.data;
            MenuTheme.Header(s, "PERFIL DO PILOTO");

            UIFactory.Label("Code", s, string.Join(" ", System.Array.ConvertAll(d.code.ToCharArray(), ch => ch.ToString())), 120, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 310f), new Vector2(900f, 140f), MenuTheme.Accent);
            UIFactory.Label("Archetype", s, CareerQuizData.ArchetypeName(d.archetype), 44, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 150f), new Vector2(1500f, 60f), MenuTheme.Text);
            UIFactory.Label("ArchetypeDesc", s, CareerQuizData.ArchetypeDescription(d.archetype), 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 78f), new Vector2(1600f, 44f), MenuTheme.TextDim);
            UIFactory.Label("Academy", s, "ACADEMIA: " + d.academyName.ToUpperInvariant() + "   —   EQUIPE F2: " + d.f2Team.ToUpperInvariant(), 30, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 8f), new Vector2(1600f, 42f), MenuTheme.Text);

            string[] statNames = { "Ataque", "Faro", "Palco", "Sangue" };
            float[] statVals = { d.attack, d.feel, d.stage, d.heat };
            for (int i = 0; i < 4; i++)
            {
                var row = UIFactory.Rect("StatRow" + i, s, c, c, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f - i * 60f), new Vector2(1200f, 44f));
                MenuTheme.StatBar(row, statNames[i], Mathf.Clamp01(statVals[i] / 8f));
            }

            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "StartCareer", "INICIAR CARREIRA", b, b, new Vector2(0f, 170f), new Vector2(520f, 84f), 32, ShowHub);
            Footer(s, "VOLTAR", ShowCreate);
        }

        // ---------- Tela de proposta de equipe (escolha entre as 3 piores da categoria) ----------
        static void ShowTeamChoice()
        {
            var d = CareerMode.data;
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "PROPOSTAS DE CONTRATO — " + (d.category == "F1" ? "FÓRMULA 1" : "FÓRMULA 2"));
            UIFactory.Label("OffersIntro", s, d.category == "F1"
                ? "VOCÊ SUBIU PARA A F1! AS EQUIPES QUE ACREDITAM EM VOCÊ OFERECEM:"
                : "NINGUÉM TE CONHECE AINDA — SÓ AS EQUIPES DE FUNDO DE GRADE OFERECEM CONTRATO:", 28, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 240f), new Vector2(1600f, 50f), MenuTheme.TextDim);

            var teams = d.teamOffers != null && d.teamOffers.Length > 0
                ? d.teamOffers
                : new[] { d.f2Team };
            for (int i = 0; i < teams.Length; i++)
            {
                int idx = i;
                string team = teams[idx];
                string carName = CareerMode.TeamCarLabel(team);
                MenuTheme.StyledButton(s, "Offer" + i, "ASSINAR COM " + team.ToUpperInvariant() + "   —   " + carName, c, c, new Vector2(0f, 140f - i * 110f), new Vector2(1400f, 88f), 26, () =>
                {
                    CareerMode.SignWithTeam(team);
                    ShowHub();
                });
            }
            Footer(s, "VOLTAR", ShowHub);
        }

        // ---------- Hub (tela principal do modo carreira) ----------
        static void ShowHub()
        {
            CareerData d = CareerMode.data;
            if (d == null) { ShowCreate(); return; }
            // Aguardando escolha de equipe (novo save reiniciado ou promoção à F1)? Mesa de propostas primeiro.
            if (d.awaitingTeamChoice) { ShowTeamChoice(); return; }
            CareerData.raceActive = false; // limpa flag de corrida abandonada no meio
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "MODO CARREIRA");

            var top = new Vector2(0.5f, 1f);
            UIFactory.Label("TopBar", s, "PILOTO: " + d.pilotName.ToUpperInvariant() + "   |   TEMPORADA " + d.season + "   |   " + d.category + "   |   " + d.pointsPlayer + " PTS", 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -150f), new Vector2(1800f, 34f), MenuTheme.Text);
            UIFactory.Label("StatsLine", s, "VITÓRIAS: " + d.careerWins + "   |   PÓDIOS: " + d.careerPodiums + "   |   TÍTULOS: " + d.titles, 18, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -196f), new Vector2(1400f, 28f), MenuTheme.TextDim);
            // Deixa CLARO que a tabela abaixo é a TEMPORADA (pontos acumulados) — não a sessão em curso.
            int champPos = d.standings != null ? d.standings.FindIndex(x => x.isPlayer) : -1;
            if (champPos >= 0)
                UIFactory.Label("ChampPos", s, "VOCÊ É " + (champPos + 1) + "º NO CAMPEONATO (tabela da temporada) — a posição da SESSÃO atual aparece no resultado dela", 17, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -218f), new Vector2(1700f, 26f), new Color(0.8f, 0.8f, 0.8f));

            // Relógio REAL da sessão correndo (treino/quali): conta mesmo dentro do menu.
            int sessLeft = CareerMode.SessionSecondsLeft();
            if (sessLeft > 0)
                UIFactory.Label("SessionClock", s, "SESSÃO TERMINA EM  " + sessLeft / 60 + ":" + (sessLeft % 60).ToString("00"), 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -244f), new Vector2(1400f, 34f), new Color(1f, 0.55f, 0.1f));

            // 24 pilotos EM 2 COLUNAS (12 à esquerda + 12 à direita) — tudo numa tela, sem rolar.
            bool showTeams = showTeamsTab;
            int colW = 760, rowH = 30;
            for (int col = 0; col < 2; col++)
            {
                var anchor = col == 0 ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
                var box = UIFactory.Rect("Standings" + col, s, anchor, anchor, anchor, new Vector2(col == 0 ? 20f : -20f, -20f), new Vector2(colW, 470f));
                if (col == 0)
                    MenuTheme.SubHeader(box, showTeams ? "EQUIPES (1-6)" : "CAMPEONATO — PILOTOS 1-12");
                else if (!showTeams)
                    MenuTheme.SubHeader(box, "CAMPEONATO — PILOTOS 13-24");
                if (showTeams)
                {
                    var totals = new List<Tuple<string, int>>();
                    foreach (var e in d.standings)
                    {
                        int idx = totals.FindIndex(t => t.Item1 == e.team);
                        if (idx >= 0) totals[idx] = Tuple.Create(e.team, totals[idx].Item2 + e.points);
                        else totals.Add(Tuple.Create(e.team, e.points));
                    }
                    totals.Sort((a, b2) => b2.Item2.CompareTo(a.Item2));
                    for (int i = col * 6; i < Mathf.Min(col * 6 + 6, totals.Count); i++)
                        UIFactory.Label("TeamRow" + i, box, (i + 1) + "º  " + totals[i].Item1.ToUpperInvariant() + "  " + totals[i].Item2 + " PTS", 19, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -80f - (i - col * 6) * rowH), new Vector2(colW - 40f, rowH), MenuTheme.Text);
                }
                else
                {
                    int playerIdx = d.standings.FindIndex(x => x.isPlayer);
                    for (int i = col * 12; i < Mathf.Min(col * 12 + 12, d.standings.Count); i++)
                        RenderStandRow(box, d.standings[i], i, i == playerIdx, i - col * 12, colW, rowH);
                }
            }

            // Alterna PILOTOS / EQUIPES — 3 botões de gestão EM LINHA no topo direito (não empilhados).
            MenuTheme.StyledButton(s, "TabBtn", showTeams ? "VER PILOTOS" : "VER EQUIPES", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-788f, -90f), new Vector2(280f, 52f), 17, () => { showTeamsTab = !showTeamsTab; ShowHub(); });
            MenuTheme.StyledButton(s, "ResetCareer", "REINICIAR PERFIL", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-486f, -90f), new Vector2(280f, 52f), 17, () => { CareerMode.ResetCareerKeepProfile(); ShowTeamChoice(); });
            MenuTheme.StyledButton(s, "DeleteCareer", "APAGAR CARREIRA", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-184f, -90f), new Vector2(280f, 52f), 17, DeleteCareer);

            if (CareerMode.JustFinishedSeason) { Footer(s, "VOLTAR", Close); ShowSeasonEnd(s); }
            else if (d.round < CareerMode.TotalRounds) ShowWeekend(s, d, true); // VOLTAR entra no grid 2x2 do rodapé
            else Footer(s, "VOLTAR", Close);
        }

        /// <summary>Uma linha da tabela de classificação (destaque quando é a sua).</summary>
        static int standSpacing = 38;
        static void RenderStandRow(RectTransform box, StandingEntry e, int i, bool isPlayer, int rowOverride = -1, int colW = 760, int rowH = 30)
        {
            int row = rowOverride >= 0 ? rowOverride : i;
            string line = (i + 1) + "º  " + e.driverName + (isPlayer ? " (VOCÊ)" : "") + "  " + e.team + "  " + e.points + " PTS" + (e.wins > 0 ? " (" + e.wins + "V)" : "");
            if (isPlayer)
            {
                var hl = UIFactory.Panel("PlayerBg" + row + "_" + i, box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -80f - row * rowH), new Vector2(colW - 40f, rowH), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.16f));
                hl.transform.SetAsFirstSibling();
            }
            UIFactory.Label("StandRow" + row + "_" + i, box, line, isPlayer ? 19 : 18, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -80f - row * rowH), new Vector2(colW - 40f, rowH), isPlayer ? MenuTheme.Accent : MenuTheme.Text);
        }

        static bool showAllStand;

        /// <summary>Painel do fim de semana (formato F1): agenda SEX/SÁB/DOM e botão da sessão atual.</summary>
        static void ShowWeekend(RectTransform s, CareerData d, bool closeBtn)
        {
            // Save migrado no meio da classificação (correntes vazias)? Exibe (e roda) a fase 1 da classificação.
            bool chainEmpty = (d.weekendPhase == CareerMode.PH_SQ2 || d.weekendPhase == CareerMode.PH_SQ3) && d.sqChain.Count < 10
                           || (d.weekendPhase == CareerMode.PH_Q2 || d.weekendPhase == CareerMode.PH_Q3) && d.qChain.Count < 10;
            if (chainEmpty) d.weekendPhase = d.weekendPhase <= CareerMode.PH_SQ3 ? CareerMode.PH_SQ1 : CareerMode.PH_Q1;
            var b = new Vector2(0.5f, 0f);
            bool sprint = CareerMode.SprintRound(d.round);
            string trackName = CareerMode.RoundTrack(d.round).ToString().ToUpperInvariant();
            string head = "RODADA " + (d.round + 1) + "/" + CareerMode.TotalRounds + " — " + trackName + (sprint ? "   •   SPRINT" : "");
            UIFactory.Label("WeekendHead", s, head, 20, TextAnchor.MiddleCenter, b, b, new Vector2(0f, 250f), new Vector2(1700f, 34f), MenuTheme.TextDim);

            string sched = sprint
                ? "SEX: TREINO 1 (60') + SQ1-SQ3      SÁB: SPRINT + Q1-Q3      DOM: CORRIDA"
                : "SEX: TREINO (120')      SÁB: TREINO 2 (60') + Q1-Q3      DOM: CORRIDA";
            UIFactory.Label("WeekendSched", s, sched, 17, TextAnchor.MiddleCenter, b, b, new Vector2(0f, 218f), new Vector2(1900f, 30f), MenuTheme.TextDim);

            bool sprintWkBtn = RaceX.Career.CareerMode.SprintRound(d.round);
            string status = "";
            if (d.weekendPhase == CareerMode.PH_RACE && d.gpGrid > 0) status = "   |   GRID: " + d.gpGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_SPRINT && d.sqGrid > 0) status = "   |   GRID SPRINT: " + d.sqGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_SQ1 && d.sqChain.Count > 0 && !d.sqChain.Contains(d.pilotName)) status = "   |   ELIMINADO NA SQ1 — GRID SPRINT: " + d.sqGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_SQ2 && d.sqChain.Count > 0 && !d.sqChain.Contains(d.pilotName)) status = "   |   ELIMINADO NA SQ1 — GRID SPRINT: " + d.sqGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_Q1 && d.qChain.Count > 0 && !d.qChain.Contains(d.pilotName)) status = "   |   ELIMINADO NA Q1 — GRID: " + d.gpGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_Q2 && d.qChain.Count > 0 && !d.qChain.Contains(d.pilotName)) status = "   |   ELIMINADO NA Q1 — GRID: " + d.gpGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_Q3 && d.qChain.Count > 0 && !d.qChain.Contains(d.pilotName)) status = "   |   ELIMINADO NA Q2 — GRID: " + d.gpGrid + "º";

            string btnLabel;
            // Botões CURTOS: o detalhe (minutos, eliminações) vive no "sched" acima, não no botão.
            switch (d.weekendPhase)
            {
                case CareerMode.PH_FP: btnLabel = sprintWkBtn ? "TREINO 1 — SEXTA" : "TREINO LIVRE — SEXTA"; break;
                case CareerMode.PH_FP3: btnLabel = "TREINO 2 — SÁBADO"; break;
                case CareerMode.PH_SQ1: btnLabel = "CLASSIF. SPRINT — SQ1"; break;
                case CareerMode.PH_SQ2: btnLabel = "CLASSIF. SPRINT — SQ2"; break;
                case CareerMode.PH_SQ3: btnLabel = "CLASSIF. SPRINT — FINAL"; break;
                case CareerMode.PH_SPRINT: btnLabel = "CORRIDA SPRINT — SÁBADO"; break;
                case CareerMode.PH_Q1: btnLabel = "CLASSIFICAÇÃO — Q1"; break;
                case CareerMode.PH_Q2: btnLabel = "CLASSIFICAÇÃO — Q2"; break;
                case CareerMode.PH_Q3: btnLabel = "CLASSIFICAÇÃO — FINAL"; break;
                default: btnLabel = "CORRIDA — DOMINGO"; break;
            }
            MenuTheme.StyledButton(s, "SessionBtn", btnLabel, b, b, new Vector2(-200f, 140f), new Vector2(430f, 64f), 22, CareerMode.LaunchNextRace);
            string skipLabel = d.weekendPhase == CareerMode.PH_FP || d.weekendPhase == CareerMode.PH_FP3 ? "PULAR TREINO"
                : d.weekendPhase == CareerMode.PH_RACE ? "PULAR CORRIDA (SEM PONTOS)"
                : d.weekendPhase == CareerMode.PH_SPRINT ? "PULAR SPRINT (SEM PONTOS)"
                : d.weekendPhase == CareerMode.PH_Q3 || d.weekendPhase == CareerMode.PH_SQ3 ? "PULAR (LARGA EM ÚLTIMO)"
                : "PULAR SESSÃO (LARGA EM ÚLTIMO)";
            MenuTheme.StyledButton(s, "SkipSess", skipLabel, b, b, new Vector2(260f, 140f), new Vector2(430f, 64f), 20, () => { CareerMode.SkipSession(); ShowHub(); });

            string[] diffNames = { "FÁCIL", "MÉDIO", "DIFÍCIL", "EXPERT" };
            int diff = Mathf.Clamp(d.difficulty, 0, 3);
            MenuTheme.StyledButton(s, "DiffBtn", "DIFICULDADE: " + diffNames[diff], b, b, new Vector2(-200f, 60f), new Vector2(430f, 64f), 20, () =>
            {
                d.difficulty = (d.difficulty + 1) % 4;
                CareerMode.Save();
                ShowHub();
            });
            if (closeBtn)
                MenuTheme.StyledButton(s, "WeekendBack", "VOLTAR", b, b, new Vector2(260f, 60f), new Vector2(430f, 64f), 20, Close); // fecha o grid 2x2 (2 em cima, 2 embaixo)
        }

        /// <summary>Painel de fim de temporada — aparece no lugar do botão de corrida.</summary>
        static void ShowSeasonEnd(RectTransform s)
        {
            CareerData d = CareerMode.data;
            var c = new Vector2(0.5f, 0.5f);
            var b = new Vector2(0.5f, 0f);
            var panel = UIFactory.Panel("SeasonPanel", s, b, b, new Vector2(0f, 175f), new Vector2(1300f, 290f), new Color(1f, 1f, 1f, 0.06f));

            UIFactory.Label("SeasonTitle", panel.transform, "FIM DA TEMPORADA " + (d.season - 1), 38, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 85f), new Vector2(1200f, 48f), MenuTheme.Accent);
            UIFactory.Label("SeasonPos", panel.transform, "POSIÇÃO FINAL: " + CareerMode.FinalPosition + "º", 28, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 20f), new Vector2(1200f, 40f), MenuTheme.Text);
            if (d.promotedThisSeason)
            {
                var promo = UIFactory.Panel("PromoPanel", panel.transform, c, c, new Vector2(0f, -48f), new Vector2(1150f, 56f), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.25f));
                UIFactory.Label("SeasonPromo", promo.transform, "PROMOÇÃO PARA A FÓRMULA 1!", 30, TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(1100f, 44f), MenuTheme.Text);
            }
            else
            {
                UIFactory.Label("SeasonPromo", panel.transform, "Continue na " + d.category + " na próxima temporada.", 28, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -48f), new Vector2(1200f, 40f), MenuTheme.Text);
            }
            MenuTheme.StyledButton((RectTransform)panel.transform, "SeasonContinue", "CONTINUAR", c, c, new Vector2(0f, -112f), new Vector2(340f, 70f), 28, () => { CareerMode.ConsumeSeasonEnd(); ShowHub(); });
        }

        static void DeleteCareer()
        {
            CareerMode.Delete();
            ShowCreate();
        }
    }
}
