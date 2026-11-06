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
        /// <summary>Personalidade acumulada no questionário: lealdade, ambição, paixão, temperamento (deltas).</summary>
        static readonly float[] quizPers = new float[4];
        static readonly string[] Nationalities = { "Brasil", "Reino Unido", "Itália", "Alemanha", "Japão", "EUA" };
        /// <summary>Aba do hub: 0 = pilotos (temporada), 1 = equipes, 2 = SESSÃO (ao vivo / última do fim de semana).</summary>
        static int standTab;

        /// <summary>Abre o fluxo: hub (se houver save) ou criação de piloto.</summary>
        static System.Action onClosed;

        public static void Open(System.Action onClosedCallback = null)
        {
            onClosed = onClosedCallback;
            AttachHubAutopilot();
            UIFactory.EnsureEventSystem();
            // Remove qualquer canvas de carreira antigo — garante uma única tela, sem sobreposição
            foreach (var old in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (old != null && old.gameObject.name == "CareerCanvas")
                    UnityEngine.Object.Destroy(old.gameObject);
            canvas = UIFactory.CreateCanvas("CareerCanvas", 50);
            BuildOffice(canvas.transform);
            var bg = UIFactory.Panel("CareerBg", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(MenuTheme.Bg.r, MenuTheme.Bg.g, MenuTheme.Bg.b, 0.96f)); // 0.88 deixava o cenário 3D de trás aparecer como formas borradas
            bg.raycastTarget = true;
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            BuildBezel(canvas.transform);
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

        static Image FullPanel(string name, Transform parent, Color col)
        {
            var img = UIFactory.Panel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, col);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            return img;
        }

        /// <summary>Fundo de "sala da equipe": parede, faixas de luz do box e silhuetas da equipe atrás do monitor.</summary>
        static void BuildOffice(Transform parent)
        {
            var room = FullPanel("Office", parent, new Color(0.07f, 0.08f, 0.11f, 1f));
            var rt = (RectTransform)room.transform;
            var top = new Vector2(0.5f, 1f);
            var bottom = new Vector2(0.5f, 0f);
            UIFactory.Panel("WallUpper", rt, top, top, Vector2.zero, new Vector2(2400f, 420f), new Color(0.11f, 0.12f, 0.16f, 1f));
            for (int i = 0; i < 4; i++)
                UIFactory.Panel("LightStrip" + i, rt, top, top, new Vector2(-720f + i * 480f, -40f), new Vector2(320f, 10f), new Color(0.9f, 0.93f, 1f, 0.55f));
            UIFactory.Panel("Floor", rt, bottom, bottom, Vector2.zero, new Vector2(2400f, 230f), new Color(0.045f, 0.05f, 0.065f, 1f));
            UIFactory.Panel("FloorLine", rt, bottom, bottom, new Vector2(0f, 228f), new Vector2(2400f, 3f), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.35f));
            float[] xs = { -760f, -470f, -150f, 180f, 500f, 790f };
            float[] hs = { 520f, 600f, 560f, 620f, 540f, 580f };
            var headSprite = MenuTheme.RoundedSprite(32, 32, 16);
            var bodySprite = MenuTheme.RoundedSprite(60, 90, 26);
            for (int i = 0; i < xs.Length; i++)
            {
                float h = hs[i];
                var shirt = i % 2 == 0 ? new Color(0.16f, 0.17f, 0.22f, 1f) : new Color(MenuTheme.AccentDim.r * 0.55f, MenuTheme.AccentDim.g * 0.55f, MenuTheme.AccentDim.b * 0.55f, 1f);
                var body = UIFactory.Panel("Crew" + i, rt, bottom, bottom, new Vector2(xs[i], 120f), new Vector2(190f, h - 140f), shirt);
                body.sprite = bodySprite;
                var head = UIFactory.Panel("CrewHead" + i, rt, bottom, bottom, new Vector2(xs[i], 120f + h - 150f), new Vector2(96f, 110f), new Color(0.13f, 0.12f, 0.12f, 1f));
                head.sprite = headSprite;
            }
        }

        /// <summary>Moldura do monitor por cima das bordas da tela (não move nenhum elemento do hub).</summary>
        static void BuildBezel(Transform parent)
        {
            var frame = UIFactory.Stretch("Bezel", parent);
            var col = new Color(0.02f, 0.02f, 0.025f, 1f);
            const float t = 14f;
            var specs = new[]
            {
                (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t)),
                (new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t + 8f)),
                (new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f)),
                (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f))
            };
            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                var r = UIFactory.Rect("BezelSide" + i, frame, s.Item1, s.Item2, s.Item3, Vector2.zero, s.Item4);
                var img = r.gameObject.AddComponent<Image>();
                img.color = col;
                img.raycastTarget = false;
            }
            var led = UIFactory.Panel("BezelLed", frame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(8f, 6f), new Color(0.3f, 1f, 0.45f, 0.9f));
            led.sprite = MenuTheme.RoundedSprite(8, 6, 3);
        }

        // ---------- Piloto automático enquanto o hub está aberto NA CORRIDA ----------
        static RaceX.Car.CarAI hubAi;
        static RaceX.Input.PlayerInputHandler hubInput;

        /// <summary>Corrida viva (por voltas) + hub aberto: a IA pilota o carro do jogador — antes ele ficava
        /// parado na pista e levava batida. Treino/classificação: o carro está na garagem (PitLaneSystem).</summary>
        static void AttachHubAutopilot()
        {
            try
            {
                DetachHubAutopilot();
                var rm = LiveCareerRace();
                if (rm == null || rm.State != RaceX.Core.RaceState.Racing || rm.Settings.laps >= 900) return;
                foreach (var e in rm.entries)
                {
                    if (!e.isHuman || e.car == null || e.finished) continue;
                    if (e.car.GetComponent<RaceX.Car.CarAI>() != null) return;
                    hubInput = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                    if (hubInput != null) hubInput.enabled = false;
                    hubAi = e.car.gameObject.AddComponent<RaceX.Car.CarAI>();
                    hubAi.Initialize(rm.Track.Waypoints, rm, rm.Settings.difficulty);
                    return;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[CareerFlow] autopiloto do hub ignorado: " + ex.Message); }
        }

        static void DetachHubAutopilot()
        {
            if (hubAi != null)
            {
                var car = hubAi.car;
                UnityEngine.Object.Destroy(hubAi);
                if (car != null) { car.throttleInput = 0f; car.brakeInput = 0f; car.steerInput = 0f; }
            }
            if (hubInput != null) hubInput.enabled = true;
            hubAi = null;
            hubInput = null;
        }

        static void Close()
        {
            DetachHubAutopilot();
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

        /// <summary>Hub → menu principal (sem o callback do overlay, que reabriria os resultados).</summary>
        static void GoMainMenu()
        {
            DetachHubAutopilot();
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            content = null;
            current = null;
            onClosed = null;
            if (CareerMode.data != null) CareerMode.SaveQuiet();
            RaceX.Core.SceneLoader.LoadMainMenu();
        }

        /// <summary>O hub está aberto (overlay da sessão)?</summary>
        public static bool IsOpen => canvas != null;

        /// <summary>Fecha o hub SEM callback e SEM mostrar o menu principal (usado quando o painel
        /// de resultados precisa aparecer por cima — ex.: fim da Q2 pelo relógio real com o hub aberto).</summary>
        public static void ForceCloseSilent()
        {
            DetachHubAutopilot();
            if (canvas == null) return;
            UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            content = null;
            current = null;
            onClosed = null;
            HUD.ResumeTime(); // sessão acabou: devolve o fluxo do jogo (1x/5x escolhido)
        }

        /// <summary>Destroi a tela atual e cria uma nova (padrão Destroy + rebuild).</summary>
        static RectTransform Screen()
        {
            if (current != null) UnityEngine.Object.Destroy(current.gameObject);
            current = UIFactory.Stretch("CareerScreen", content.transform);
            return current;
        }

        /// <summary>AUTOTESTE: abre uma tela da carreira para o print ("hub", "estrategia", "mundo:N").</summary>
        public static void AutoTestShow(string what)
        {
            var d = CareerMode.data;
            if (d == null) return;
            if (canvas == null) Open();
            if (what == "hub") { ShowHub(); return; }
            if (what == "estrategia")
            {
                ShowHub();
                bool raceLike = d.weekendPhase == CareerMode.PH_RACE || d.weekendPhase == CareerMode.PH_SPRINT;
                if (raceLike && current != null) ShowStrategyModal(current, d, CareerMode.SessionLapsFor(d.round, d.weekendPhase));
                return;
            }
            if (what.StartsWith("mundo:") && int.TryParse(what.Substring(6), out int tab)) CareerWorldScreens.Open(tab);
        }

        /// <summary>Telas do MUNDO (CareerWorldScreens) usam a mesma área do hub.</summary>
        internal static RectTransform NewScreen() => Screen();
        internal static void BackToHub() => ShowHub();

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
            bg.raycastTarget = true; // campo clicável (mesmo conserto do login)
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
            Array.Clear(quizPers, 0, quizPers.Length);
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
            UIFactory.Label("PathSub", s, "ONDE VOCÊ VAI CORRER? COMECE EM BAIXO — NINGUÉM COMEÇA NA PONTA.", 30, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 250f), new Vector2(1500f, 44f), MenuTheme.TextDim);

            MenuTheme.StyledButton(s, "PathGT", "GT SÉRIE WORLD", c, c, new Vector2(0f, 130f), new Vector2(760f, 110f), 30, () =>
            {
                pathChoiceOpen = false;
                CareerMode.ChoosePath(true);
                ShowQuiz();
            });
            MenuTheme.StyledButton(s, "PathF1", "FÓRMULA F1 MUNDIAL", c, c, new Vector2(0f, -40f), new Vector2(760f, 110f), 30, () =>
            {
                pathChoiceOpen = false;
                CareerMode.ChoosePath(false);
                ShowQuiz();
            });
            var back = MenuTheme.StyledButton(s, "PathBack", "VOLTAR", c, c, new Vector2(0f, -230f), new Vector2(420f, 70f), 26, () =>
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
            var promptT = UIFactory.Label("Prompt", s, q.prompt, 34, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 200f), new Vector2(1500f, 150f), MenuTheme.Text);
            promptT.horizontalOverflow = HorizontalWrapMode.Wrap;

            for (int i = 0; i < q.options.Length; i++)
            {
                int opt = i;
                var btn = MenuTheme.StyledButton(s, "Opt" + opt, q.options[opt].text, c, c, new Vector2(0f, 60f - opt * 104f), new Vector2(1400f, 90f), 22, () => Answer(opt));
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
            // PERSONALIDADE pela resposta (lealdade, ambição, paixão, temperamento): o eixo e o arquétipo da
            // resposta puxam cada traço — o resultado muda o piloto dentro do jogo (erros, evolução, mercado).
            quizPers[3] += o.heat * 0.025f;                                   // esquentado ↔ frio
            quizPers[1] += Mathf.Max(0, o.attack) * 0.015f + Mathf.Max(0, o.stage) * 0.01f;
            quizPers[0] -= Mathf.Max(0, o.stage) * 0.012f;
            switch (o.archetype)
            {
                case "prodigy": quizPers[1] += 0.03f; break;
                case "aggressor": quizPers[2] += 0.02f; quizPers[3] += 0.02f; break;
                case "technician": quizPers[0] += 0.03f; quizPers[3] -= 0.025f; break;
                case "media_darling": quizPers[1] += 0.02f; quizPers[0] -= 0.02f; break;
                case "late_bloomer": quizPers[2] += 0.035f; quizPers[0] += 0.015f; break;
            }
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
                MaxKey(CareerQuizData.AcademyKeys, quizAcademy),
                Mathf.Clamp(quizPers[0], -0.2f, 0.2f), Mathf.Clamp(quizPers[1], -0.2f, 0.2f), Mathf.Clamp(quizPers[2], -0.2f, 0.2f), Mathf.Clamp(quizPers[3], -0.2f, 0.2f));
            Array.Clear(quizPers, 0, quizPers.Length);
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
            if (CareerWorld.Active && CareerWorld.W.offers.Exists(o => !o.midseason)) { ShowWorldOffers(); return; }
            var d = CareerMode.data;
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "PROPOSTAS DE CONTRATO — " + (d.category == "F1" ? "FÓRMULA 1" : d.category == "GT" ? "GT" : "FÓRMULA 2"));
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

        /// <summary>Mesa de propostas do MUNDO VIVO: quem quer você, por quê, por quanto tempo e o carro de cada uma.</summary>
        static void ShowWorldOffers()
        {
            var d = CareerMode.data;
            var W = CareerWorld.W;
            var s = Screen();
            var c = new Vector2(0.5f, 0.5f);
            MenuTheme.Header(s, "PROPOSTAS DE CONTRATO · " + (CareerWorld.Year + 1));
            var mine = CareerWorld.TeamOf(CareerWorld.Player);
            string intro = W.notRenewed ? "SUA EQUIPE NÃO VAI RENOVAR. ESCOLHA ONDE CORRER NO PRÓXIMO ANO:" : "SEU CONTRATO ACABOU. ESTAS EQUIPES QUEREM VOCÊ:";
            UIFactory.Label("OffersIntro", s, intro, 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 300f), new Vector2(1700f, 44f), W.notRenewed ? new Color(1f, 0.45f, 0.35f) : MenuTheme.TextDim);
            int n = 0;
            for (int i = 0; i < W.offers.Count; i++)
            {
                var o = W.offers[i];
                if (o.midseason) continue;
                int idx = i;
                float y = 210f - n * 122f;
                string tag = (o.series == "F1" && d.category == "F2" ? "SOBE PARA A F1 · " : "") + o.teamName.ToUpperInvariant() + " (" + o.series + ") · " + o.years + " ANO(S) · CARRO " + o.car.ToString("0.0") + (mine != null && o.teamName == mine.name ? " · RENOVAÇÃO" : "");
                MenuTheme.StyledButton(s, "WOffer" + i, tag, c, c, new Vector2(0f, y), new Vector2(1500f, 70f), 24, () =>
                {
                    CareerMode.SignWorldOffer(idx);
                    ShowHub();
                });
                var why = UIFactory.Label("Why" + i, s, o.why, 18, TextAnchor.MiddleCenter, c, c, new Vector2(0f, y - 50f), new Vector2(1500f, 30f), MenuTheme.TextDim);
                why.horizontalOverflow = HorizontalWrapMode.Wrap;
                n++;
            }
            var b = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(s, "OffersWorld", "VER MUNDO (EQUIPES, PILOTOS, NOTÍCIAS)", b, b, new Vector2(0f, 36f), new Vector2(620f, 64f), 22, () => CareerWorldScreens.Open(6));
        }

        // ---------- Hub (tela principal do modo carreira) ----------
        /// <summary>Corrida da carreira viva na cena (hub aberto por cima dela), senão null.</summary>
        internal static RaceX.Core.RaceManager LiveCareerRace()
        {
            var rm = UnityEngine.Object.FindFirstObjectByType<RaceX.Core.RaceManager>();
            if (rm == null || rm.Settings == null || rm.Settings.careerPhase < 0 || rm.State == RaceX.Core.RaceState.Finished) return null;
            return rm;
        }

        internal static int LiveSessionSeconds()
        {
            return LiveSessionSeconds(LiveCareerRace());
        }

        /// <summary>Segundos exibidos no hub: relógio REAL ou o timer AO VIVO da cena (0 = nenhum).</summary>
        internal static int LiveSessionSeconds(RaceX.Core.RaceManager live)
        {
            int s = CareerMode.SessionGameSecondsLeft();
            if (s > 0) return s;
            if (live != null && live.SessionTimeLeft > 0f) return Mathf.CeilToInt(live.SessionTimeLeft);
            return 0;
        }

        static void ShowHub()
        {
            CareerData d = CareerMode.data;
            if (d == null) { ShowCreate(); return; }
            // Aguardando escolha de equipe (novo save reiniciado ou promoção à F1)? Mesa de propostas primeiro.
            if (d.awaitingTeamChoice) { ShowTeamChoice(); return; }
            var liveRm = LiveCareerRace();
            if (liveRm == null) CareerData.raceActive = false; // sem corrida viva: limpa flag de sessão abandonada
            var c = new Vector2(0.5f, 0.5f);
            var s = Screen();
            MenuTheme.Header(s, "MODO CARREIRA");

            // Gestão no topo direito: 2 botões IGUAIS lado a lado (simétricos).
            var tr = new Vector2(1f, 1f);
            MenuTheme.StyledButton(s, "ResetCareer", "REINICIAR PERFIL", tr, tr, new Vector2(-334f, -40f), new Vector2(280f, 56f), 17, () =>
                ShowConfirm(s, "REINICIAR PERFIL?", "A temporada recomeça (o questionário é mantido).", () => { CareerMode.ResetCareerKeepProfile(); ShowTeamChoice(); }));
            MenuTheme.StyledButton(s, "DeleteCareer", "APAGAR CARREIRA", tr, tr, new Vector2(-40f, -40f), new Vector2(280f, 56f), 17, () =>
                ShowConfirm(s, "APAGAR CARREIRA?", "Todo o progresso será perdido.", DeleteCareer));
            // MENU PRINCIPAL (pedido do usuário): com o hub por cima de uma sessão, sai da cena (confirmação); aberto pelo
            // menu, só fecha o hub. O relógio real da sessão continua — reabrir a sessão pelo hub retoma de onde está.
            bool inRaceScene = UnityEngine.Object.FindFirstObjectByType<RaceX.Core.RaceManager>() != null;
            MenuTheme.StyledButton(s, "MainMenuBtn", "MENU PRINCIPAL", tr, tr, new Vector2(CareerWorld.Active ? -922f : -628f, -40f), new Vector2(280f, 56f), 17, () =>
            {
                if (!inRaceScene) { Close(); return; }
                ShowConfirm(s, "MENU PRINCIPAL?", "Sair da sessão e voltar ao menu.\nO progresso salvo é mantido.", GoMainMenu);
            });
            // MUNDO VIVO: notícias, imprensa, semana, propostas, pilotos, equipes, rivalidades e arquivo.
            if (CareerWorld.Active)
            {
                string badge = CareerWorldScreens.Badge();
                var wb = MenuTheme.StyledButton(s, "WorldBtn", "MUNDO", tr, tr, new Vector2(-628f, -40f), new Vector2(280f, 56f), 20, () => CareerWorldScreens.Open());
                // selo numérico de novidades (e-mails não lidos, entrevista, propostas)
                int count = CareerWorld.NotificationCount;
                if (count > 0)
                {
                    var dot = UIFactory.Panel("WorldCount", wb.transform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-8f, -6f), new Vector2(46f, 46f), new Color(0.9f, 0.12f, 0.1f, 1f));
                    dot.sprite = MenuTheme.RoundedSprite(64, 64, 32);
                    dot.raycastTarget = false;
                    var num = UIFactory.Label("N", dot.transform, count > 99 ? "99+" : count.ToString(), count > 99 ? 16 : 22, TextAnchor.MiddleCenter,
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f), Color.white);
                    num.raycastTarget = false;
                }
                if (badge.Length > 0)
                {
                    wb.GetComponent<Image>().color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.45f);
                    UIFactory.Label("WorldBadge", s, badge, 16, TextAnchor.MiddleCenter, tr, tr, new Vector2(-628f, -100f), new Vector2(280f, 24f), MenuTheme.Accent);
                }
            }

            // CARTÕES de resumo (mesmo tamanho, faixa única) — sem frases explicativas.
            int champPos = d.standings != null ? d.standings.FindIndex(x => x.isPlayer) : -1;
            string[] cardK = { "PILOTO", "CATEGORIA", "PONTOS", "CAMPEONATO", "VITÓRIAS", "PÓDIOS", "TÍTULOS" };
            string[] cardV =
            {
                d.pilotName.ToUpperInvariant(), d.category + " · TEMP. " + d.season, d.pointsPlayer.ToString(),
                champPos >= 0 ? (champPos + 1) + "º" : "-", d.careerWins.ToString(), d.careerPodiums.ToString(), d.titles.ToString()
            };
            const float cardW = 250f, cardGap = 14f;
            float cardsW = cardK.Length * cardW + (cardK.Length - 1) * cardGap;
            var top = new Vector2(0.5f, 1f);
            for (int i = 0; i < cardK.Length; i++)
            {
                float x = -cardsW * 0.5f + cardW * 0.5f + i * (cardW + cardGap);
                bool hi = i == 3;
                var card = UIFactory.Panel("Card" + i, s, top, top, new Vector2(x, -136f), new Vector2(cardW, 84f),
                    hi ? new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.22f) : new Color(1f, 1f, 1f, 0.06f));
                card.sprite = MenuTheme.RoundedSprite(64, 22, 8);
                card.raycastTarget = false;
                var ct = new Vector2(0.5f, 1f);
                UIFactory.Label("K", card.transform, cardK[i], 15, TextAnchor.MiddleCenter, ct, ct, new Vector2(0f, -8f), new Vector2(cardW - 10f, 22f), MenuTheme.TextDim).raycastTarget = false;
                string v = cardV[i].Length > 14 ? cardV[i].Substring(0, 14) : cardV[i];
                UIFactory.Label("V", card.transform, v, i == 0 ? 24 : 32, TextAnchor.MiddleCenter, ct, ct, new Vector2(0f, -34f), new Vector2(cardW - 10f, 44f), hi ? MenuTheme.Accent : MenuTheme.Text).raycastTarget = false;
            }

            // Relógio REAL/ao vivo da sessão: conta mesmo dentro do menu (pausável pelo menu de pausa).
            string pauseSuffix = CareerMode.SessionClockPaused ? "  (PAUSADO)" : "";
            int sessLeft = LiveSessionSeconds(liveRm);
            bool timedLive = liveRm != null && liveRm.Settings.laps >= 900;
            if (sessLeft > 0 || timedLive)
            {
                string clockText = sessLeft > 0
                    ? "SESSÃO TERMINA EM  " + sessLeft / 60 + ":" + (sessLeft % 60).ToString("00") + pauseSuffix
                    : "SESSÃO ENCERRADA" + pauseSuffix;
                var clockLbl = UIFactory.Label("SessionClock", s, clockText, 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -236f), new Vector2(1400f, 32f), new Color(1f, 0.55f, 0.1f));
                clockLbl.gameObject.AddComponent<SessionClockTicker>().label = clockLbl;
            }

            // 24 pilotos EM 2 COLUNAS (12 + 12) em cartões — tudo numa tela, sem rolar.
            bool showTeams = standTab == 1;
            bool showSession = standTab == 2;
            List<string> sessRows = null;
            string sessTitle = "";
            if (showSession)
            {
                if (liveRm != null)
                {
                    sessRows = HUD.SessionRows(liveRm.FinalStandings(), liveRm.Settings.laps >= 900);
                    sessTitle = "AO VIVO · " + CareerMode.SessionTitle(liveRm.Settings.careerPhase);
                }
                else { sessRows = d.lastSessionRows ?? new List<string>(); sessTitle = string.IsNullOrEmpty(d.lastSessionTitle) ? "NENHUMA SESSÃO AINDA" : "ÚLTIMA SESSÃO · " + d.lastSessionTitle; }
            }
            int colW = 740, rowH = 29;
            for (int col = 0; col < 2; col++)
            {
                var anchor = col == 0 ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
                var box = UIFactory.Rect("Standings" + col, s, anchor, anchor, anchor, new Vector2(col == 0 ? 30f : -30f, 6f), new Vector2(colW, 420f));
                var boxBg = UIFactory.Panel("StandBg", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(colW, 420f), new Color(1f, 1f, 1f, 0.035f));
                boxBg.sprite = MenuTheme.RoundedSprite(64, 36, 10);
                boxBg.raycastTarget = false;
                boxBg.transform.SetAsFirstSibling();
                string title = showSession ? (col == 0 ? sessTitle : "POSIÇÕES 13–24") : showTeams ? (col == 0 ? "EQUIPES 1–6" : "EQUIPES 7–12") : (col == 0 ? "PILOTOS 1–12" : "PILOTOS 13–24");
                UIFactory.Label("ColTitle", box, title, 20, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -12f), new Vector2(colW - 40f, 30f), MenuTheme.Accent);
                UIFactory.Panel("ColLine", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -46f), new Vector2(colW - 48f, 2f), MenuTheme.AccentDim).raycastTarget = false;
                if (showSession)
                {
                    for (int i = col * 12; i < Mathf.Min(col * 12 + 12, sessRows.Count); i++)
                        RenderSessionRow(box, sessRows[i], i, i - col * 12, colW, rowH);
                }
                else if (showTeams)
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
                    {
                        bool mine = totals[i].Item1 == d.f2Team || d.standings.Exists(x => x.isPlayer && x.team == totals[i].Item1);
                        UIFactory.Label("TeamRow" + i, box, (i + 1) + "º   " + totals[i].Item1.ToUpperInvariant() + "   " + totals[i].Item2 + " PTS", 21, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -60f - (i - col * 6) * 40f), new Vector2(colW - 40f, 36f), mine ? MenuTheme.Accent : MenuTheme.Text);
                    }
                }
                else
                {
                    int playerIdx = d.standings.FindIndex(x => x.isPlayer);
                    for (int i = col * 12; i < Mathf.Min(col * 12 + 12, d.standings.Count); i++)
                        RenderStandRow(box, d.standings[i], i, i == playerIdx, i - col * 12, colW, rowH);
                }
            }

            // Pista AO VIVO (hub por cima da sessão): mapa entre as colunas. Opcional — nunca quebra o hub.
            if (liveRm != null)
            {
                try
                {
                    RaceX.Core.RaceEntry selfE = null;
                    for (int i = 0; i < liveRm.entries.Count; i++)
                        if (liveRm.entries[i].isHuman) { selfE = liveRm.entries[i]; break; }
                    var mapLbl = UIFactory.Label("LiveMapLabel", s, "AO VIVO — 0 NA PISTA", 18, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 190f), new Vector2(360f, 30f), MenuTheme.Accent);
                    mapLbl.gameObject.AddComponent<LiveMapCounter>().label = mapLbl;
                    MiniMap.CreateAt(s, liveRm, selfE, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), 330f, true);
                }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }

            if (CareerMode.JustFinishedSeason) { Footer(s, "VOLTAR", Close); ShowSeasonEnd(s); }
            else if (d.round < CareerMode.TotalRounds) ShowWeekend(s, d, true);
            else Footer(s, "VOLTAR", Close);
        }

        /// <summary>Uma linha da tabela de classificação (destaque quando é a sua).</summary>
        /// <summary>Linha da aba SESSÃO: posição, piloto, carro, tempo/diferença, melhor volta ("*" = jogador).</summary>
        static void RenderSessionRow(RectTransform box, string raw, int i, int row, int colW, int rowH)
        {
            var p = raw.Split('|');
            bool me = p[0].StartsWith("*");
            string name = me ? p[0].Substring(1) : p[0];
            float y = -60f - row * rowH;
            var tl = new Vector2(0f, 1f);
            if (me) UIFactory.Panel("SessBg" + i, box, tl, tl, new Vector2(18f, y), new Vector2(colW - 36f, rowH), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.2f)).raycastTarget = false;
            Color main = me ? MenuTheme.Accent : MenuTheme.Text;
            UIFactory.Label("SP" + i, box, (i + 1) + "º", 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(30f, y), new Vector2(56f, rowH), i < 3 ? new Color(1f, 0.82f, 0.3f) : MenuTheme.TextDim).raycastTarget = false;
            MenuTheme.FitText(UIFactory.Label("SN" + i, box, name.ToUpperInvariant(), 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(90f, y), new Vector2(250f, rowH), main), 19);
            if (p.Length > 1) MenuTheme.FitText(UIFactory.Label("SC" + i, box, p[1], 16, TextAnchor.MiddleLeft, tl, tl, new Vector2(345f, y), new Vector2(150f, rowH), MenuTheme.TextDim), 16);
            if (p.Length > 2) UIFactory.Label("SG" + i, box, p[2], 18, TextAnchor.MiddleRight, tl, tl, new Vector2(495f, y), new Vector2(100f, rowH), main).raycastTarget = false;
            if (p.Length > 3) UIFactory.Label("SB" + i, box, p[3], 18, TextAnchor.MiddleRight, tl, tl, new Vector2(600f, y), new Vector2(colW - 640f, rowH), MenuTheme.TextDim).raycastTarget = false;
        }

        static void RenderStandRow(RectTransform box, StandingEntry e, int i, bool isPlayer, int rowOverride = -1, int colW = 740, int rowH = 29)
        {
            int row = rowOverride >= 0 ? rowOverride : i;
            float y = -60f - row * rowH;
            if (isPlayer)
            {
                var hl = UIFactory.Panel("PlayerBg" + row + "_" + i, box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, y), new Vector2(colW - 36f, rowH), new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.2f));
                hl.raycastTarget = false;
            }
            var tl = new Vector2(0f, 1f);
            Color main = isPlayer ? MenuTheme.Accent : MenuTheme.Text;
            UIFactory.Label("P" + i, box, (i + 1) + "º", 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(30f, y), new Vector2(56f, rowH), i < 3 ? new Color(1f, 0.82f, 0.3f) : MenuTheme.TextDim).raycastTarget = false;
            UIFactory.Label("N" + i, box, e.driverName.ToUpperInvariant(), 19, TextAnchor.MiddleLeft, tl, tl, new Vector2(90f, y), new Vector2(270f, rowH), main).raycastTarget = false;
            UIFactory.Label("T" + i, box, e.team, 17, TextAnchor.MiddleLeft, tl, tl, new Vector2(370f, y), new Vector2(220f, rowH), MenuTheme.TextDim).raycastTarget = false;
            UIFactory.Label("Pt" + i, box, e.points + (e.wins > 0 ? "  (" + e.wins + "V)" : ""), 19, TextAnchor.MiddleRight, tl, tl, new Vector2(590f, y), new Vector2(colW - 630f, rowH), main).raycastTarget = false;
        }

        /// <summary>Sessões do fim de semana na ordem (uma ficha por sessão; Q1–Q3 viram uma só).</summary>
        static int[] WeekendChips(bool sprint) => sprint
            ? new[] { CareerMode.PH_FP, CareerMode.PH_SQ1, CareerMode.PH_SPRINT, CareerMode.PH_Q1, CareerMode.PH_RACE }
            : new[] { CareerMode.PH_FP, CareerMode.PH_FP3, CareerMode.PH_Q1, CareerMode.PH_RACE };

        static string ChipName(int ph, bool sprint)
        {
            switch (ph)
            {
                case CareerMode.PH_FP: return sprint ? "SEX · TREINO 1" : "SEX · TREINO LIVRE";
                case CareerMode.PH_FP3: return "SÁB · TREINO 2";
                case CareerMode.PH_SQ1: return "SEX · CLASSIF. SPRINT";
                case CareerMode.PH_SPRINT: return "SÁB · SPRINT";
                case CareerMode.PH_Q1: return "SÁB · CLASSIFICAÇÃO";
                default: return "DOM · CORRIDA";
            }
        }

        /// <summary>Fase da ficha que contém a fase atual (Q2/Q3 pertencem à ficha da Q1 etc.).</summary>
        static int ChipOf(int phase)
        {
            if (phase == CareerMode.PH_SQ2 || phase == CareerMode.PH_SQ3) return CareerMode.PH_SQ1;
            if (phase == CareerMode.PH_Q2 || phase == CareerMode.PH_Q3) return CareerMode.PH_Q1;
            return phase;
        }

        static int ChipOrder(int ph, bool sprint) => Array.IndexOf(WeekendChips(sprint), ChipOf(ph));

        /// <summary>Previsão do tempo da sessão (mesma usada ao carregar a pista).</summary>
        internal static RaceX.Core.WeatherForecast ForecastOf(CareerData d, int phase) =>
            RaceX.Core.WeatherSystem.ForecastFor(d.season, d.round, ChipOf(phase) == ChipOf(d.weekendPhase) ? d.weekendPhase : ChipOf(phase), (int)CareerMode.RoundTrack(d.round));

        /// <summary>Painel do fim de semana: linha do tempo SEX/SÁB/DOM com previsão do tempo e grade
        /// 2×4 de botões IGUAIS (nada de 3 em cima e 2 embaixo).</summary>
        static void ShowWeekend(RectTransform s, CareerData d, bool closeBtn)
        {
            bool chainEmpty = (d.weekendPhase == CareerMode.PH_SQ2 || d.weekendPhase == CareerMode.PH_SQ3) && d.sqChain.Count < 10
                           || (d.weekendPhase == CareerMode.PH_Q2 || d.weekendPhase == CareerMode.PH_Q3) && d.qChain.Count < 10;
            if (chainEmpty) d.weekendPhase = d.weekendPhase <= CareerMode.PH_SQ3 ? CareerMode.PH_SQ1 : CareerMode.PH_Q1;
            var b = new Vector2(0.5f, 0f);
            bool sprint = CareerMode.SprintRound(d.round);
            // Nome de EXIBIÇÃO da pista (antes o nome interno do enum: "FORESTCIRCUIT").
            string trackName = RaceX.Track.TrackLibrary.Get(CareerMode.RoundTrack(d.round)).displayName.ToUpperInvariant();

            string status = "";
            if (d.weekendPhase == CareerMode.PH_RACE && d.gpGrid > 0) status = "GRID " + d.gpGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_SPRINT && d.sqGrid > 0) status = "GRID SPRINT " + d.sqGrid + "º";
            else if ((d.weekendPhase == CareerMode.PH_SQ2 || d.weekendPhase == CareerMode.PH_SQ3) && d.sqChain.Count > 0 && !d.sqChain.Contains(d.pilotName)) status = "ELIMINADO · GRID SPRINT " + d.sqGrid + "º";
            else if ((d.weekendPhase == CareerMode.PH_Q2 || d.weekendPhase == CareerMode.PH_Q3) && d.qChain.Count > 0 && !d.qChain.Contains(d.pilotName)) status = "ELIMINADO · GRID " + d.gpGrid + "º";
            else if (d.weekendPhase == CareerMode.PH_Q2) status = "Q2";
            else if (d.weekendPhase == CareerMode.PH_Q3) status = "Q3";
            else if (d.weekendPhase == CareerMode.PH_SQ2) status = "SQ2";
            else if (d.weekendPhase == CareerMode.PH_SQ3) status = "SQ3";
            string head = "RODADA " + (d.round + 1) + "/" + CareerMode.TotalRounds + "  ·  " + trackName + (sprint ? "  ·  SPRINT" : "") + (status.Length > 0 ? "  ·  " + status : "");
            UIFactory.Label("WeekendHead", s, head, 22, TextAnchor.MiddleCenter, b, b, new Vector2(0f, 300f), new Vector2(1800f, 32f), MenuTheme.Text);

            // LINHA DO TEMPO: uma ficha por sessão (atual em destaque, feitas apagadas) com a previsão.
            var chips = WeekendChips(sprint);
            int curOrder = ChipOrder(d.weekendPhase, sprint);
            const float chipW = 330f, chipGap = 16f;
            float chipsW = chips.Length * chipW + (chips.Length - 1) * chipGap;
            for (int i = 0; i < chips.Length; i++)
            {
                float x = -chipsW * 0.5f + chipW * 0.5f + i * (chipW + chipGap);
                bool cur = i == curOrder, done = i < curOrder;
                var chip = UIFactory.Panel("Chip" + i, s, b, b, new Vector2(x, 226f), new Vector2(chipW, 62f),
                    cur ? new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.26f) : new Color(1f, 1f, 1f, done ? 0.025f : 0.06f));
                chip.sprite = MenuTheme.RoundedSprite(66, 16, 7);
                chip.raycastTarget = false;
                var ct = new Vector2(0.5f, 1f);
                UIFactory.Label("Name", chip.transform, ChipName(chips[i], sprint), 18, TextAnchor.MiddleCenter, ct, ct, new Vector2(0f, -4f), new Vector2(chipW - 10f, 28f), done ? MenuTheme.TextDim : (cur ? MenuTheme.Accent : MenuTheme.Text)).raycastTarget = false;
                var fc = ForecastOf(d, chips[i]);
                UIFactory.Label("Wx", chip.transform, fc.Label + "  " + Mathf.RoundToInt(fc.airTemp) + "°C", 16, TextAnchor.MiddleCenter, ct, ct, new Vector2(0f, -32f), new Vector2(chipW - 10f, 24f),
                    done ? MenuTheme.TextDim : RaceX.Core.WeatherSystem.KindColor(fc.kind)).raycastTarget = false;
            }

            string btnLabel;
            switch (d.weekendPhase)
            {
                case CareerMode.PH_FP: btnLabel = sprint ? "TREINO 1" : "TREINO LIVRE"; break;
                case CareerMode.PH_FP3: btnLabel = "TREINO 2"; break;
                case CareerMode.PH_SQ1: btnLabel = "SPRINT · SQ1"; break;
                case CareerMode.PH_SQ2: btnLabel = "SPRINT · SQ2"; break;
                case CareerMode.PH_SQ3: btnLabel = "SPRINT · SQ3"; break;
                case CareerMode.PH_SPRINT: btnLabel = "CORRIDA SPRINT"; break;
                case CareerMode.PH_Q1: btnLabel = "CLASSIF. · Q1"; break;
                case CareerMode.PH_Q2: btnLabel = "CLASSIF. · Q2"; break;
                case CareerMode.PH_Q3: btnLabel = "CLASSIF. · Q3"; break;
                default: btnLabel = "CORRIDA"; break;
            }
            // GRADE 2×4 — todos os botões com o MESMO tamanho.
            float[] gx = { -630f, -210f, 210f, 630f };
            float[] gy = { 136f, 58f };
            var size = new Vector2(400f, 64f);
            const int fs = 19;
            var liveWk = LiveCareerRace();
            Action sessionAct;
            if (liveWk != null && liveWk.Settings.careerPhase == d.weekendPhase) { btnLabel = "CONTINUAR SESSÃO"; sessionAct = () => { RaceX.Core.PitLaneSystem.GarageRepairPlayer(); Close(); }; }
            else if (liveWk != null) sessionAct = () => { CareerData.raceActive = false; CareerMode.LaunchNextRace(); };
            else sessionAct = CareerMode.LaunchNextRace;
            // Lesionado ou suspenso (mundo vivo): o fim de semana corre SEM você — um reserva assume o carro.
            var Wabs = CareerWorld.Active ? CareerWorld.W : null;
            bool absent = Wabs != null && liveWk == null && d.weekendPhase == CareerMode.PH_FP && (Wabs.injuryRounds > 0 || Wabs.banRounds > 0);
            if (absent)
            {
                string why = Wabs.injuryRounds > 0 ? "LESÃO (" + Wabs.injuryRounds + ")" : "SUSPENSO (" + Wabs.banRounds + ")";
                btnLabel = "AUSENTE: " + why + " · SIMULAR RODADA";
                sessionAct = () => ShowConfirm(s, "RODADA SEM VOCÊ", (Wabs.injuryRounds > 0 ? "Liberação médica pendente: " + Wabs.injuryText + "." : "Suspensão por pontos na superlicença.") + "\nUm reserva corre no seu lugar; você não pontua.", () => { CareerMode.SkipAbsentWeekend(); ShowHub(); });
            }
            var sessBtn = MenuTheme.StyledButton(s, "SessionBtn", (absent ? "" : "▶ ") + btnLabel, b, b, new Vector2(gx[0], gy[0]), size, fs + 1, sessionAct);
            sessBtn.GetComponent<Image>().color = new Color(MenuTheme.Accent.r, MenuTheme.Accent.g, MenuTheme.Accent.b, 0.35f);

            string skipLabel = d.weekendPhase == CareerMode.PH_FP || d.weekendPhase == CareerMode.PH_FP3 ? "PULAR TREINO"
                : d.weekendPhase == CareerMode.PH_RACE || d.weekendPhase == CareerMode.PH_SPRINT ? "PULAR CORRIDA"
                : "PULAR SESSÃO";
            string skipBody = d.weekendPhase == CareerMode.PH_RACE || d.weekendPhase == CareerMode.PH_SPRINT ? "Sem pontos nesta corrida."
                : d.weekendPhase == CareerMode.PH_FP || d.weekendPhase == CareerMode.PH_FP3 ? "O treino é encerrado."
                : null;   // classificação: depende da volta que o jogador já tem (calculado ao tocar)
            if (!absent)
                MenuTheme.StyledButton(s, "SkipSess", skipLabel, b, b, new Vector2(gx[1], gy[0]), size, fs, () =>
                    ShowConfirm(s, skipLabel + "?", (skipBody ?? CareerMode.SkipQualiPreview()) + "\nEssa ação não pode ser desfeita.", () => { CareerData.raceActive = false; CareerMode.SkipSession(); ShowHub(); }));

            // ESTRATÉGIA desde a SEXTA: treino/classi = pneu da próxima saída; corrida = paradas e compostos.
            bool raceLike = d.weekendPhase == CareerMode.PH_RACE || d.weekendPhase == CareerMode.PH_SPRINT;
            int sLaps = CareerMode.SessionLapsFor(d.round, d.weekendPhase);
            string stratLbl = raceLike ? "ESTRATÉGIA · " + StrategySummary(d, sLaps)
                : "PNEU · " + (d.practiceCompound < 0 ? "AUTO" : RaceX.Core.TyreModel.Letter(d.practiceCompound));
            MenuTheme.StyledButton(s, "StrategyBtn", stratLbl, b, b, new Vector2(gx[2], gy[0]), size, fs, () =>
            {
                if (raceLike) ShowStrategyModal(s, d, sLaps); else ShowPracticeTyreModal(s, d);
            });
            MenuTheme.StyledButton(s, "MyLapsBtn", "MINHAS VOLTAS (" + CareerMode.CurrentSessionLaps().Count + ")", b, b, new Vector2(gx[3], gy[0]), size, fs, () => { lapsPage = -1; ShowLapsModal(s); });

            // Linha 2: TEMPO 1x/5x | DIFICULDADE | EQUIPES/PILOTOS | VOLTAR
            bool ff = HUD.FastForwardOn;
            var ffBtn = MenuTheme.StyledButton(s, "FfBtn", ff ? "TEMPO 5x" : "TEMPO 1x", b, b, new Vector2(gx[0], gy[1]), size, fs, () => { HUD.SetFastForward(!HUD.FastForwardOn); ShowHub(); });
            if (ff) ffBtn.GetComponentInChildren<Text>().color = new Color(0.3f, 1f, 0.45f);
            int diff = Mathf.Clamp(d.difficulty, 0, 3);
            // AJUSTES: dificuldade + regras (perda total, penalidades) + rádio da equipe — pedido do usuário: essas opções
            // em TODOS os modos (aqui na carreira, na configuração do Single/tela dividida e na sala online).
            MenuTheme.StyledButton(s, "DiffBtn", "AJUSTES · " + DiffNames[diff], b, b, new Vector2(gx[1], gy[1]), size, fs, () => ShowSettingsModal(s, d));
            // Alterna PILOTOS → EQUIPES → SESSÃO (ao vivo ou a última do fim de semana) → PILOTOS.
            string tabLabel = standTab == 0 ? "VER EQUIPES" : standTab == 1 ? "VER SESSÃO" : "VER TEMPORADA";
            MenuTheme.StyledButton(s, "TabBtn", tabLabel, b, b, new Vector2(gx[2], gy[1]), size, fs, () => { standTab = (standTab + 1) % 3; ShowHub(); });
            MenuTheme.StyledButton(s, "WeekendBack", "VOLTAR", b, b, new Vector2(gx[3], gy[1]), size, fs, Close);
        }

        // ---------- Modais (overlay por cima do hub) ----------
        static Image Modal(RectTransform s, string title, Vector2 size, out GameObject root)
        {
            var c = new Vector2(0.5f, 0.5f);
            var dim = UIFactory.Panel("ModalDim", s, c, c, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.74f));
            var drt = (RectTransform)dim.transform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.sizeDelta = Vector2.zero;
            dim.raycastTarget = true; // bloqueia cliques no hub por trás
            dim.transform.SetAsLastSibling();
            root = dim.gameObject;
            var box = UIFactory.Panel("ModalBox", dim.transform, c, c, Vector2.zero, size, new Color(0.07f, 0.08f, 0.11f, 0.98f));
            box.raycastTarget = true;
            var top = new Vector2(0.5f, 1f);
            UIFactory.Panel("ModalLine", box.transform, top, top, Vector2.zero, new Vector2(size.x, 4f), MenuTheme.Accent);
            UIFactory.Label("ModalTitle", box.transform, title, 34, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -20f), new Vector2(size.x - 40f, 50f), MenuTheme.Text);
            return box;
        }

        static void ShowConfirm(RectTransform s, string title, string body, Action onYes)
        {
            var box = Modal(s, title, new Vector2(820f, 360f), out var root);
            var c = new Vector2(0.5f, 0.5f);
            var bodyT = UIFactory.Label("ConfirmBody", box.transform, body, 24, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 20f), new Vector2(760f, 120f), MenuTheme.TextDim);
            bodyT.horizontalOverflow = HorizontalWrapMode.Wrap;
            var bt = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton((RectTransform)box.transform, "ConfirmYes", "SIM", bt, bt, new Vector2(-190f, 34f), new Vector2(320f, 70f), 26, () =>
            {
                UnityEngine.Object.Destroy(root);
                onYes?.Invoke();
            });
            MenuTheme.StyledButton((RectTransform)box.transform, "ConfirmNo", "NÃO", bt, bt, new Vector2(190f, 34f), new Vector2(320f, 70f), 26, () => UnityEngine.Object.Destroy(root));
        }

        // ---------- ESTRATÉGIA (hub) ----------
        static readonly string[] CompoundFull = { "MACIO (S)", "MÉDIO (M)", "DURO (H)", "INTERMEDIÁRIO (I)", "CHUVA (W)" };
        static int MaxC => RaceX.Core.TyreModel.Count - 1;

        static int StratStopsShown(CareerData d, int laps) =>
            d.stratStops >= 0 ? d.stratStops : RaceX.Core.PitLaneSystem.SuggestedStops(laps, d.stratStart, d.stratStop);

        static string StrategySummary(CareerData d, int laps)
        {
            int stops = StratStopsShown(d, laps);
            string seq = RaceX.Core.PitLaneSystem.CompoundLetter(d.stratStart);
            for (int i = 0; i < stops; i++) seq += "→" + RaceX.Core.PitLaneSystem.CompoundLetter(StintCompound(d, i));
            return (d.stratStops < 0 ? "AUTO " : "") + stops + "P " + seq;
        }

        /// <summary>Treino/classificação: escolha do pneu da PRÓXIMA saída (testar compostos desde sexta).</summary>
        static readonly string[] DiffNames = { "FÁCIL", "MÉDIO", "DIFÍCIL", "EXPERT" };

        /// <summary>AJUSTES da carreira. Dificuldade vale para as IAs; perda total e penalidades são REGRAS (valem a partir da
        /// próxima sessão — não mudam a sessão em andamento); rádio da equipe vale na hora.</summary>
        static void ShowSettingsModal(RectTransform s, CareerData d)
        {
            var box = Modal(s, "AJUSTES", new Vector2(1000f, 560f), out var root);
            var brt = (RectTransform)box.transform;
            var c = new Vector2(0.5f, 0.5f);
            Action reopen = () => { UnityEngine.Object.Destroy(root); ShowSettingsModal(s, d); };
            StratRow(brt, "DIFICULDADE", DiffNames[Mathf.Clamp(d.difficulty, 0, 3)], 150f,
                () => { d.difficulty = (d.difficulty + 3) % 4; CareerMode.Save(); reopen(); },
                () => { d.difficulty = (d.difficulty + 1) % 4; CareerMode.Save(); reopen(); });
            Action togglePt = () => { d.noTotalLoss = !d.noTotalLoss; CareerMode.Save(); reopen(); };
            StratRow(brt, "PERDA TOTAL", d.noTotalLoss ? "DESLIGADA" : "LIGADA", 70f, togglePt, togglePt);
            Action togglePen = () => { d.noPenalties = !d.noPenalties; CareerMode.Save(); reopen(); };
            StratRow(brt, "PENALIDADES", d.noPenalties ? "DESLIGADAS" : "LIGADAS", -10f, togglePen, togglePen);
            Action toggleRadio = () => { RaceX.Core.SettingsManager.PitRadio = RaceX.Core.SettingsManager.PitRadio == 0 ? 1 : 0; RaceX.Core.SettingsManager.Save(); reopen(); };
            StratRow(brt, "RÁDIO DA EQUIPE", RaceX.Core.SettingsManager.PitRadio == 0 ? "DESLIGADO" : "LIGADO", -90f, toggleRadio, toggleRadio);
            UIFactory.Label("RulesHint", brt, "Perda total e penalidades valem a partir da próxima sessão.", 22, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -150f), new Vector2(940f, 34f), MenuTheme.TextDim);
            var bt = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(brt, "SetClose", "FECHAR", bt, bt, new Vector2(0f, 30f), new Vector2(380f, 70f), 24, () => { UnityEngine.Object.Destroy(root); ShowHub(); });
        }

        static void ShowPracticeTyreModal(RectTransform s, CareerData d)
        {
            var box = Modal(s, "PNEUS DO TREINO", new Vector2(1000f, 640f), out var root);
            var brt = (RectTransform)box.transform;
            var c = new Vector2(0.5f, 0.5f);
            Action reopen = () => { UnityEngine.Object.Destroy(root); ShowPracticeTyreModal(s, d); };
            Action<int> change = delta =>
            {
                int v = d.practiceCompound + delta;
                if (v > MaxC) v = -1;
                if (v < -1) v = MaxC;
                d.practiceCompound = v;
                if (v >= 0) RaceX.Core.PitLaneSystem.FitPlayerPracticeCompoundNow(v);
                CareerMode.SaveQuiet();
                reopen();
            };
            string val = d.practiceCompound < 0 ? "AUTO · EQUIPE" : CompoundFull[Mathf.Clamp(d.practiceCompound, 0, MaxC)];
            StratRow(brt, "PRÓXIMA SAÍDA", val, 170f, () => change(-1), () => change(1));
            // Ficha técnica dos compostos (vida e janela de temperatura do modelo do jogo).
            for (int i = 0; i <= MaxC; i++)
            {
                string line = RaceX.Core.TyreModel.Name(i) + "  ·  ~" + Mathf.RoundToInt(RaceX.Core.TyreModel.LifeLaps(i)) + " VOLTAS  ·  JANELA " + Mathf.RoundToInt(RaceX.Core.TyreModel.Low(i)) + "–" + Mathf.RoundToInt(RaceX.Core.TyreModel.High(i)) + "°C"
                    + (i == RaceX.Core.TyreModel.Inter ? "  ·  PISTA ÚMIDA" : i == RaceX.Core.TyreModel.Wet ? "  ·  PISTA ALAGADA" : "");
                UIFactory.Label("Info" + i, brt, line, 21, TextAnchor.MiddleCenter, c, c, new Vector2(0f, 80f - i * 40f), new Vector2(940f, 34f), RaceX.Core.TyreModel.ColorOf(i));
            }
            var bt = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(brt, "PtAuto", "AUTOMÁTICO", bt, bt, new Vector2(-220f, 34f), new Vector2(380f, 70f), 24, () => { d.practiceCompound = -1; CareerMode.SaveQuiet(); reopen(); });
            MenuTheme.StyledButton(brt, "PtClose", "FECHAR", bt, bt, new Vector2(220f, 34f), new Vector2(380f, 70f), 24, () => { UnityEngine.Object.Destroy(root); ShowHub(); });
        }

        static void ShowStrategyModal(RectTransform s, CareerData d, int laps)
        {
            bool raceLike = d.weekendPhase == CareerMode.PH_RACE || d.weekendPhase == CareerMode.PH_SPRINT;
            if (!raceLike) { ShowPracticeTyreModal(s, d); return; }
            // A EQUIPE PROPÕE o plano (dados do fim de semana + clima + ritmo); o jogador edita e CONFIRMA.
            var fcP = RaceX.Core.WeatherSystem.ForecastFor(d.season, d.round, d.weekendPhase, (int)CareerMode.RoundTrack(d.round));
            if (d.stratStops < 0)
            {
                d.stratPace = RaceX.Core.PitLaneSystem.SuggestPace(laps, fcP, out string whyP);   // ritmo sugerido pelo desgaste medido
                stratMsg = RaceX.Core.PitLaneSystem.TeamProposal(laps, fcP, d.stratPace, out int sP, out var csP) + " Ritmo: " + whyP + ".";
                d.stratStart = sP; d.stratStints.Clear(); d.stratStints.AddRange(csP); d.stratStops = csP.Count;
                CareerMode.SaveQuiet();
            }
            int stops = StratStopsShown(d, laps);
            float boxH = 805f + 85f * Mathf.Max(0, stops - 1);
            var box = Modal(s, "ESTRATÉGIA DE PIT — " + laps + " VOLTAS", new Vector2(1000f, boxH), out var root);
            var brt = (RectTransform)box.transform;
            var c = new Vector2(0.5f, 0.5f);
            float y0 = boxH * 0.5f - 150f;
            // PLANO DE RITMO (início/meio/fim): a equipe escolhe pneus por ele e avisa as fases pelo rádio.
            {
                Action<int> pace = dp =>
                {
                    int n = RaceX.Core.PitLaneSystem.PaceCount;
                    d.stratPace = ((d.stratPace + dp) % n + n) % n;
                    stratMsg = RaceX.Core.PitLaneSystem.TeamProposal(laps, fcP, d.stratPace, out int sQ, out var csQ);
                    d.stratStart = sQ; d.stratStints.Clear(); d.stratStints.AddRange(csQ); d.stratStops = csQ.Count;
                    ApplyStrategyToLiveRace(d);
                    CareerMode.SaveQuiet();
                    UnityEngine.Object.Destroy(root); ShowStrategyModal(s, d, laps);
                };
                StratRow(brt, "RITMO", RaceX.Core.PitLaneSystem.PaceName(d.stratPace), y0, () => pace(-1), () => pace(1));
                y0 -= 85f;
            }
            if (!string.IsNullOrEmpty(d.stratTeamNote))
                UIFactory.Label("TeamNote", brt, d.stratTeamNote, 20, TextAnchor.MiddleCenter, c, c, new Vector2(0f, boxH * 0.5f - 88f), new Vector2(940f, 30f), MenuTheme.Accent);
            Action reopen = () => { UnityEngine.Object.Destroy(root); ShowStrategyModal(s, d, laps); };
            Action<int, int, int> change = (dStops, dStart, dStop) =>
            {
                int st = Mathf.Clamp(StratStopsShown(d, laps) + dStops, 0, 3);
                d.stratStart = Mathf.Clamp(d.stratStart + dStart, 0, MaxC);
                d.stratStop = Mathf.Clamp(d.stratStop + dStop, 0, MaxC);
                d.stratStops = st;
                // Trocou o PNEU (não o número de paradas): a equipe ajusta as paradas e avisa.
                stratMsg = dStops == 0 ? AdjustStopsForCompounds(d, laps) : "";
                ApplyStrategyToLiveRace(d);
                CareerMode.SaveQuiet();
                reopen();
            };
            StratRow(brt, "PARADAS", stops.ToString(), y0, () => change(-1, 0, 0), () => change(1, 0, 0));
            StratRow(brt, "PNEU DE LARGADA", CompoundFull[Mathf.Clamp(d.stratStart, 0, MaxC)], y0 - 85f, () => change(0, -1, 0), () => change(0, 1, 0));
            // Uma linha POR PARADA: cada stint com o seu composto (antes todas as paradas usavam o mesmo pneu).
            for (int k = 0; k < stops; k++)
            {
                int kk = k;
                Action<int> stint = dc =>
                {
                    while (d.stratStints.Count <= kk) d.stratStints.Add(Mathf.Clamp(d.stratStop, 0, MaxC));
                    d.stratStints[kk] = Mathf.Clamp(d.stratStints[kk] + dc, 0, MaxC);
                    if (d.stratStops < 0) d.stratStops = StratStopsShown(d, laps);
                    stratMsg = AdjustStopsForCompounds(d, laps);
                    ApplyStrategyToLiveRace(d);
                    CareerMode.SaveQuiet();
                    reopen();
                };
                StratRow(brt, "PNEU NA " + (k + 1) + "ª PARADA", CompoundFull[StintCompound(d, k)], y0 - 170f - k * 85f, () => stint(-1), () => stint(1));
            }
            float yInfo = y0 - 170f - Mathf.Max(1, stops) * 85f + 20f;
            var stintList = StintList(d, stops);
            var boxLaps = RaceX.Core.PitLaneSystem.StopLapsFor(laps, d.stratStart, stintList, fcP);   // parada de chuva na volta prevista
            string boxTxt = boxLaps.Count == 0 ? "SEM PARADAS" : "BOX NA VOLTA " + string.Join(", ", boxLaps);
            int covered = RaceX.Core.PitLaneSystem.CompoundLifeLaps(d.stratStart);
            foreach (int sc in stintList) covered += RaceX.Core.PitLaneSystem.CompoundLifeLaps(sc);
            bool twoDry = d.stratStart > 2 || stintList.Exists(x => x != d.stratStart || x > 2);
            UIFactory.Label("StratBox", brt, boxTxt, 26, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo), new Vector2(940f, 40f), MenuTheme.Accent);
            UIFactory.Label("StratLife", brt, "VIDA DOS PNEUS: MACIO ~" + RaceX.Core.PitLaneSystem.CompoundLifeLaps(0) + " · MÉDIO ~" + RaceX.Core.PitLaneSystem.CompoundLifeLaps(1) + " · DURO ~" + RaceX.Core.PitLaneSystem.CompoundLifeLaps(2) + " VOLTAS", 20, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo - 44f), new Vector2(940f, 30f), MenuTheme.TextDim);
            if (covered < laps)
                UIFactory.Label("StratWarn", brt, "ATENÇÃO: esses pneus cobrem só ~" + covered + " das " + laps + " voltas — o desgaste vai custar tempo.", 20, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo - 80f), new Vector2(940f, 30f), new Color(1f, 0.45f, 0.3f));
            else if (!twoDry && d.weekendPhase == CareerMode.PH_RACE)
                UIFactory.Label("StratRule", brt, "REGRA: na corrida seca é obrigatório usar 2 compostos secos diferentes.", 20, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo - 80f), new Vector2(940f, 30f), new Color(1f, 0.8f, 0.3f));
            var fcR = RaceX.Core.WeatherSystem.ForecastFor(d.season, d.round, d.weekendPhase, (int)CareerMode.RoundTrack(d.round));
            // Avisos da equipe: ajuste automático de paradas (última troca) e pneu errado para o tempo.
            string wxWarn = WeatherTyreWarning(d, stintList, fcR);
            string note = string.IsNullOrEmpty(stratMsg) ? wxWarn : string.IsNullOrEmpty(wxWarn) ? stratMsg : stratMsg + "\n" + wxWarn;
            if (!string.IsNullOrEmpty(note))
            {
                var nl = UIFactory.Label("StratNote", brt, note, 19, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo - 186f), new Vector2(940f, 92f),
                    string.IsNullOrEmpty(wxWarn) ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.45f, 0.3f));
                nl.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            UIFactory.Label("StratFc", brt, "PREVISÃO: " + fcR.Label + (fcR.kind >= RaceX.Core.WeatherKind.LightRain || fcR.rainStart >= 0f ? " — leve INTERMEDIÁRIO/CHUVA em mente" : ""), 20, TextAnchor.MiddleCenter, c, c, new Vector2(0f, yInfo - 116f), new Vector2(940f, 30f), RaceX.Core.WeatherSystem.KindColor(fcR.kind));
            var bt = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(brt, "StratAuto", "PLANO DA EQUIPE", bt, bt, new Vector2(-220f, 34f), new Vector2(380f, 70f), 24, () =>
            {
                d.stratPace = RaceX.Core.PitLaneSystem.SuggestPace(laps, fcP, out string whyR);
                stratMsg = RaceX.Core.PitLaneSystem.TeamProposal(laps, fcP, d.stratPace, out int sR, out var csR) + " Ritmo: " + whyR + ".";
                d.stratStart = sR; d.stratStints.Clear(); d.stratStints.AddRange(csR); d.stratStops = csR.Count; d.stratTeamNote = "";
                ApplyStrategyToLiveRace(d);
                CareerMode.SaveQuiet();
                reopen();
            });
            MenuTheme.StyledButton(brt, "StratClose", "CONFIRMAR", bt, bt, new Vector2(220f, 34f), new Vector2(380f, 70f), 24, () => { stratMsg = ""; ApplyStrategyToLiveRace(d); CareerMode.SaveQuiet(); UnityEngine.Object.Destroy(root); ShowHub(); });
        }

        static string stratMsg = "";

        /// <summary>Depois de trocar um pneu na estratégia: se os pneus não cobrem as voltas, ADICIONA uma parada; se sobra
        /// um stint inteiro, TIRA uma (desde que a corrida seca continue com 2 compostos secos diferentes — regra da F1).
        /// Retorna a mensagem da equipe ("" = nada mudou).</summary>
        static string AdjustStopsForCompounds(CareerData d, int laps)
        {
            int stops = StratStopsShown(d, laps);
            var list = StintList(d, stops);
            int covered = RaceX.Core.PitLaneSystem.CompoundLifeLaps(d.stratStart);
            foreach (int c in list) covered += RaceX.Core.PitLaneSystem.CompoundLifeLaps(c);
            if (covered < laps && stops < 3)
            {
                int add = list.Count > 0 ? list[list.Count - 1] : Mathf.Clamp(d.stratStop, 0, MaxC);
                while (d.stratStints.Count < stops) d.stratStints.Add(StintCompound(d, d.stratStints.Count));
                if (d.stratStints.Count == stops) d.stratStints.Add(add); else d.stratStints[stops] = add;
                d.stratStops = stops + 1;
                return "EQUIPE: com esse pneu ele não chega ao fim — adicionamos mais uma parada (agora " + (stops + 1) + ").";
            }
            if (stops > 0)
            {
                int lastLife = RaceX.Core.PitLaneSystem.CompoundLifeLaps(list[stops - 1]);
                if (covered - lastLife >= laps)
                {
                    var fewer = list.GetRange(0, stops - 1);
                    bool dryRace = d.weekendPhase == CareerMode.PH_RACE && !TyreIsWet(d.stratStart) && !fewer.Exists(TyreIsWet);
                    bool twoDry = fewer.Exists(x => x != d.stratStart);
                    if (dryRace && !twoDry)
                        return "EQUIPE: esse pneu aguenta mais, mas a regra exige 2 compostos secos — mantivemos " + stops + " parada(s).";
                    d.stratStops = stops - 1;
                    return "EQUIPE: esse pneu aguenta mais — tiramos uma parada (agora " + (stops - 1) + ").";
                }
            }
            return "";
        }

        static bool TyreIsWet(int c) => RaceX.Core.TyreModel.IsWetTyre(c);

        /// <summary>Pneu errado para o tempo previsto (ou o da pista agora, com a corrida viva).</summary>
        static string WeatherTyreWarning(CareerData d, List<int> stints, RaceX.Core.WeatherForecast fc)
        {
            bool rainExpected = fc.kind >= RaceX.Core.WeatherKind.LightRain || fc.rainStart >= 0f;
            var live = LiveCareerRace();
            if (live != null && RaceX.Core.WeatherSystem.Active) rainExpected = RaceX.Core.WeatherSystem.Wetness > 0.25f || rainExpected;
            bool anyWet = TyreIsWet(d.stratStart) || stints.Exists(TyreIsWet);
            bool allSlick = !anyWet;
            if (!rainExpected && anyWet)
                return "ATENÇÃO: pneu de CHUVA/INTERMEDIÁRIO com pista seca — a borracha mole superaquece, se desfaz rápido e o carro DERRAPA nas curvas (perde ~5–6 s por volta).";
            if (rainExpected && allSlick)
                return "ATENÇÃO: pneu de pista SECA com chuva prevista — sem sulcos ele não tira a água: o carro aquaplana e DERRAPA. Leve intermediário/chuva no plano.";
            return "";
        }

        static int StintCompound(CareerData d, int i) => Mathf.Clamp(i < d.stratStints.Count ? d.stratStints[i] : d.stratStop, 0, MaxC);
        static List<int> StintList(CareerData d, int stops) { var l = new List<int>(); for (int i = 0; i < stops; i++) l.Add(StintCompound(d, i)); return l; }

        static void StratRow(RectTransform box, string label, string value, float y, Action prev, Action next)
        {
            var c = new Vector2(0.5f, 0.5f);
            var row = UIFactory.Panel("StratRow_" + label, box, c, c, new Vector2(0f, y), new Vector2(900f, 70f), new Color(1f, 1f, 1f, 0.05f));
            var ml = new Vector2(0f, 0.5f);
            UIFactory.Label("L", row.transform, label, 24, TextAnchor.MiddleLeft, ml, ml, new Vector2(24f, 0f), new Vector2(330f, 60f), MenuTheme.TextDim);
            MenuTheme.StyledButton((RectTransform)row.transform, "Prev", "<", ml, ml, new Vector2(380f, 0f), new Vector2(70f, 56f), 30, prev);
            UIFactory.Label("V", row.transform, value, 28, TextAnchor.MiddleCenter, ml, ml, new Vector2(460f, 0f), new Vector2(330f, 60f), MenuTheme.Text);
            MenuTheme.StyledButton((RectTransform)row.transform, "Next", ">", ml, ml, new Vector2(800f, 0f), new Vector2(70f, 56f), 30, next);
        }

        /// <summary>Hub aberto por cima da corrida/sprint em andamento: a estratégia vale na hora.</summary>
        static void ApplyStrategyToLiveRace(CareerData d)
        {
            var live = LiveCareerRace();
            if (live == null || live.Settings.laps >= 900 || live.Settings.careerPhase != d.weekendPhase) return;
            foreach (var e in live.entries)
            {
                if (!e.isHuman || e.car == null) continue;
                // (já parou: o SetPlayerStrategy mantém o pneu montado e replaneja só as próximas paradas)
                int laps = Mathf.Max(2, live.Settings.laps);
                int stops = d.stratStops >= 0 ? d.stratStops : RaceX.Core.PitLaneSystem.SuggestedStops(laps, d.stratStart, d.stratStop);
                RaceX.Core.PitLaneSystem.SetPlayerStrategy(e.car, d.stratStart, StintList(d, stops));
            }
        }

        // ---------- MINHAS VOLTAS ----------
        static int lapsPage = -1;
        const int LapsPerPage = 12;

        static void ShowLapsModal(RectTransform s)
        {
            var laps = CareerMode.CurrentSessionLaps();
            var box = Modal(s, "MINHAS VOLTAS — " + CareerMode.SessionLapsTitle(), new Vector2(1300f, 800f), out var root);
            var brt = (RectTransform)box.transform;
            var top = new Vector2(0.5f, 1f);
            int pages = Mathf.Max(1, Mathf.CeilToInt(laps.Count / (float)LapsPerPage));
            if (lapsPage < 0 || lapsPage >= pages) lapsPage = pages - 1; // abre na página mais recente
            // Melhores da sessão (destaque): volta e setores.
            float bestLap = float.MaxValue; float[] bestS = { float.MaxValue, float.MaxValue, float.MaxValue };
            foreach (var l in laps)
            {
                if (l.clean && l.total > 0f && l.total < bestLap) bestLap = l.total;
                if (l.s1 > 0f && l.s1 < bestS[0]) bestS[0] = l.s1;
                if (l.s2 > 0f && l.s2 < bestS[1]) bestS[1] = l.s2;
                if (l.s3 > 0f && l.s3 < bestS[2]) bestS[2] = l.s3;
            }
            float[] colX = { -540f, -350f, -160f, 30f, 240f, 410f, 560f };
            string[] heads = { "VOLTA", "S1", "S2", "S3", "TEMPO", "PNEU", "LIMPA" };
            for (int i = 0; i < heads.Length; i++)
                UIFactory.Label("H" + i, brt, heads[i], 22, TextAnchor.MiddleCenter, top, top, new Vector2(colX[i], -96f), new Vector2(200f, 34f), MenuTheme.TextDim);
            UIFactory.Panel("HLine", brt, top, top, new Vector2(0f, -116f), new Vector2(1220f, 2f), MenuTheme.AccentDim);
            if (laps.Count == 0)
            {
                UIFactory.Label("Empty", brt, "Nenhuma volta completa nesta sessão ainda.\nA volta de saída do box não conta.", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 100f), MenuTheme.TextDim);
            }
            int first = lapsPage * LapsPerPage;
            for (int r = 0; r < LapsPerPage && first + r < laps.Count; r++)
            {
                var l = laps[first + r];
                // 12 linhas de 44 px a partir de −142: a última acaba a 156 px do rodapé, acima da legenda (90–118 px).
                // Com 46 px a partir de −150 a 12ª linha descia até 104 px e as letras da legenda ficavam em cima da tabela.
                float y = -142f - r * 44f;
                var rowBg = UIFactory.Panel("Row" + r, brt, top, top, new Vector2(0f, y), new Vector2(1220f, 40f), new Color(1f, 1f, 1f, r % 2 == 0 ? 0.05f : 0.02f));
                rowBg.raycastTarget = false;
                UIFactory.Label("N" + r, brt, l.lapNo.ToString(), 24, TextAnchor.MiddleCenter, top, top, new Vector2(colX[0], y), new Vector2(200f, 40f), MenuTheme.Text);
                float[] secs = { l.s1, l.s2, l.s3 };
                int[] cols = { l.c1, l.c2, l.c3 };
                for (int k = 0; k < 3; k++)
                {
                    // ROXO só no MELHOR setor da sessão (antes ficava roxo todo setor que FOI o melhor na
                    // hora — dois roxos na mesma coluna); verde = foi seu melhor quando você fez; amarelo = mais lento.
                    bool bestNow = secs[k] > 0f && Mathf.Abs(secs[k] - bestS[k]) < 0.0005f;
                    Color chip = bestNow ? new Color(0.62f, 0.25f, 0.95f, 0.9f)
                        : (cols[k] == 1 || cols[k] == 2) ? new Color(0.2f, 0.8f, 0.3f, 0.9f)
                        : cols[k] == 3 ? new Color(1f, 0.78f, 0.1f, 0.9f) : new Color(1f, 1f, 1f, 0.06f);
                    if (!bestNow && (cols[k] == 1 || cols[k] == 2)) cols[k] = 2;
                    var cp = UIFactory.Panel("Chip" + r + "_" + k, brt, top, top, new Vector2(colX[k + 1], y), new Vector2(170f, 34f), chip);
                    cp.raycastTarget = false;
                    UIFactory.Label("S" + r + "_" + k, cp.transform, secs[k] > 0f ? secs[k].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "--.---", 22, TextAnchor.MiddleCenter,
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 34f), cols[k] == 3 ? new Color(0.08f, 0.08f, 0.1f) : Color.white);
                }
                bool isBest = l.clean && Mathf.Abs(l.total - bestLap) < 0.0005f;
                UIFactory.Label("T" + r, brt, HUD.FormatTime(l.total), 24, TextAnchor.MiddleCenter, top, top, new Vector2(colX[4], y), new Vector2(220f, 40f), isBest ? new Color(0.75f, 0.45f, 1f) : MenuTheme.Text);
                UIFactory.Label("P" + r, brt, l.compound >= 0 ? RaceX.Core.TyreModel.Letter(l.compound) + " " + ShortTyre(l.compound) : "-", 20, TextAnchor.MiddleCenter, top, top, new Vector2(colX[5], y), new Vector2(120f, 40f), l.compound >= 0 ? RaceX.Core.TyreModel.ColorOf(l.compound) : MenuTheme.TextDim);
                UIFactory.Label("C" + r, brt, l.clean ? "SIM" : "NÃO", 22, TextAnchor.MiddleCenter, top, top, new Vector2(colX[6], y), new Vector2(140f, 40f), l.clean ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 0.45f, 0.3f));
            }
            var bt = new Vector2(0.5f, 0f);
            if (pages > 1)
            {
                MenuTheme.StyledButton(brt, "PgPrev", "<", bt, bt, new Vector2(-460f, 34f), new Vector2(90f, 64f), 30, () => { lapsPage = Mathf.Max(0, lapsPage - 1); UnityEngine.Object.Destroy(root); ShowLapsModal(s); });
                UIFactory.Label("PgLbl", brt, "PÁGINA " + (lapsPage + 1) + "/" + pages, 22, TextAnchor.MiddleCenter, bt, bt, new Vector2(-330f, 46f), new Vector2(170f, 40f), MenuTheme.TextDim);
                MenuTheme.StyledButton(brt, "PgNext", ">", bt, bt, new Vector2(-200f, 34f), new Vector2(90f, 64f), 30, () => { lapsPage = Mathf.Min(pages - 1, lapsPage + 1); UnityEngine.Object.Destroy(root); ShowLapsModal(s); });
            }
            UIFactory.Label("Legend", brt, "ROXO = melhor setor da sessão · VERDE = era seu melhor na hora · AMARELO = mais lento", 18, TextAnchor.MiddleCenter, bt, bt, new Vector2(170f, 104f), new Vector2(800f, 28f), MenuTheme.TextDim);
            MenuTheme.StyledButton(brt, "LapsClose", "FECHAR", bt, bt, new Vector2(400f, 34f), new Vector2(320f, 64f), 24, () => UnityEngine.Object.Destroy(root));
        }

        static string ShortTyre(int c) => c == 0 ? "MACIO" : c == 1 ? "MÉDIO" : c == 2 ? "DURO" : c == 3 ? "INTER" : "CHUVA";

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
                string next = CareerWorld.Active ? "Próxima temporada: " + d.category + " · " + d.f2Team : "Continue na " + d.category + " na próxima temporada.";
                UIFactory.Label("SeasonPromo", panel.transform, next, 28, TextAnchor.MiddleCenter, c, c, new Vector2(0f, -48f), new Vector2(1200f, 40f), MenuTheme.Text);
            }
            MenuTheme.StyledButton((RectTransform)panel.transform, "SeasonContinue", "CONTINUAR", c, c, new Vector2(0f, -112f), new Vector2(340f, 70f), 28, () => { CareerMode.ConsumeSeasonEnd(); ShowHub(); });
        }

        static void DeleteCareer()
        {
            CareerMode.Delete();
            ShowCreate();
        }
    }

    internal class SessionClockTicker : MonoBehaviour
    {
        public Text label;
        RaceX.Core.RaceManager live;
        double lastLookup;
        int lastSec = -1;
        string lastSuffix = "";

        void Update()
        {
            if (label == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastLookup >= 1.0) // lookup da cena no máximo 1x/s
            {
                live = CareerFlow.LiveCareerRace();
                lastLookup = now;
            }
            string suffix = CareerMode.SessionClockPaused ? "  (PAUSADO)" : "";
            int sec = CareerFlow.LiveSessionSeconds(live);
            if (sec == lastSec && suffix == lastSuffix) return;
            lastSec = sec;
            lastSuffix = suffix;
            label.text = sec > 0
                ? "SESSÃO TERMINA EM  " + (sec / 60) + ":" + (sec % 60).ToString("00") + suffix
                : "SESSÃO ENCERRADA" + suffix;
        }
    }

    /// <summary>Contador de carros NA PISTA no mapa ao vivo do hub (0,5 s, tempo real).</summary>
    internal class LiveMapCounter : MonoBehaviour
    {
        public Text label;
        double lastRefresh;

        void Update()
        {
            if (label == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastRefresh < 0.5) return;
            lastRefresh = now;
            var rm = CareerFlow.LiveCareerRace();
            if (rm == null) { label.text = "PISTA AO VIVO — 0 NA PISTA"; return; }
            int n = 0;
            for (int i = 0; i < rm.entries.Count; i++)
            {
                var e = rm.entries[i];
                // v108: destruído / abandonou / retirado da pista não conta como "na pista" (relato do usuário, 06/11)
                if (e.car != null && !e.retired && !e.car.wrecked && e.car.gameObject.activeInHierarchy && !RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) n++;
            }
            label.text = "PISTA AO VIVO — " + n + " NA PISTA";
        }
    }
}
