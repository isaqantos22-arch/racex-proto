using RaceX.Car;
using RaceX.Core;
using RaceX.Input;
using UnityEngine;

namespace RaceX.UI
{
    public class PauseMenu : MonoBehaviour
    {
        static readonly string[] ControlNames = { "Botões na tela", "Inclinação", "Joystick virtual" };

        public RaceManager race;
        public bool online;

        GameObject panel;
        GameObject settingsPanel;
        GameObject strategyPanel;
        UnityEngine.UI.Text volumeLabel;
        UnityEngine.UI.Text controlsLabel;
        UnityEngine.UI.Text minimapLabel;
        UnityEngine.UI.Text radioLabel;
        UnityEngine.UI.Text autoThrottleLabel;
        UnityEngine.UI.Text strategyStopsLabel, strategyStartLabel, strategyStopsCpdLabel;

        public bool IsOpen => panel != null && panel.activeSelf;

        public static PauseMenu Create(Canvas canvas, RaceManager race, bool online)
        {
            var root = UIFactory.Stretch("PauseMenu", canvas.transform);
            var menu = root.gameObject.AddComponent<PauseMenu>();
            menu.race = race;
            menu.online = online;
            menu.Build(root);
            return menu;
        }

        void Build(RectTransform root)
        {
            var dim = UIFactory.Panel("Pause", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.7f));
            var rt = (RectTransform)dim.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            panel = dim.gameObject;

            var c = new Vector2(0.5f, 0.5f);
            // Carreira: MENU DA CARREIRA volta ao hub com o relógio REAL retomado (a sessão segue viva lá).
            bool careerBtn = race != null && race.Settings.careerPhase >= 0;
            bool stratVisible = StrategyVisible;
            // LISTA DINÂMICA (v79): os botões são empilhados conforme os que existem — antes as posições
            // eram fixas para 6 botões, sobrava um buraco sem a ESTRATÉGIA e o título/MENU saíam cortados.
            var items = new System.Collections.Generic.List<(string name, string label, System.Action act)>();
            items.Add(("Continue", "CONTINUAR", new System.Action(Toggle)));
            // EMERGÊNCIA: carro preso (de ré na mureta, na contramão, encalhado) — recoloca e segue a corrida.
            // Nunca mais "a única saída é reiniciar" com 80 de 98 voltas feitas.
            if (race != null)
                items.Add(("Rescue", "VOLTAR À PISTA", (System.Action)(() =>
                {
                    // Só o carro de QUEM ABRIU o menu (tela dividida: antes recolocava todos os humanos e atrapalhava o outro
                    // jogador — relato do usuário). Sem saber quem abriu, só age com um único humano.
                    if (requester != null) RaceX.Core.PitLaneSystem.RescueCar(requester);
                    else
                    {
                        CarController only = null; int humans = 0;
                        foreach (var en in race.entries) if (en.isHuman && en.car != null) { humans++; only = en.car; }
                        if (humans == 1) RaceX.Core.PitLaneSystem.RescueCar(only);
                    }
                    Toggle();
                })));
            if (careerBtn)
                items.Add(("CareerHub", "MENU DA CARREIRA", (System.Action)(() =>
                {
                    panel.SetActive(false);
                    // Treino/classificação: o carro vai para a GARAGEM e fica lá até você sair do hub
                    // (antes ficava parado NA PISTA e levava batida por trás). Corrida: a IA pilota o
                    // seu carro enquanto o hub está aberto (CareerFlow cuida disso).
                    RaceX.Core.PitLaneSystem.SendPlayerToGarage();
                    HUD.ResumeTime(); // sessão segue viva (respeita o 1x/5x)
                    AudioListener.pause = false;
                    RaceX.Career.CareerMode.ResumeSessionClock(); // sessão corre viva dentro do hub
                    RaceX.UI.CareerFlow.Open();
                })));
            // REPLAY instantâneo: últimos 30 s (linha do tempo com a sessão inteira). Ao sair, volta à pausa.
            if (race != null && !online)
                items.Add(("Replay", "REPLAY", (System.Action)(() =>
                {
                    if (RaceX.Core.ReplaySystem.Available(race)) RaceX.Core.ReplaySystem.Play(race, null, true, null);
                })));
            // ONLINE: reiniciar é uma VOTAÇÃO (todos precisam aceitar em 10 s) e a nova largada vale para todos na mesma sala.
            // Antes recarregava a pista só neste aparelho e ele saía da corrida dos outros.
            // Corrida já encerrada (tabela escondida com CONTINUAR): o pedido vira NOVA PARTIDA na mesma sala.
            if (online)
                items.Add(("Restart", "REINICIAR", (System.Action)(() =>
                {
                    OnlineClient.RequestVote(race != null && race.State == RaceState.Finished ? "rematch" : "restart");
                    Toggle();
                })));
            else
                items.Add(("Restart", "REINICIAR", new System.Action(SceneLoader.ReloadCurrent)));
            if (stratVisible)
                items.Add(("Strategy", "ESTRATÉGIA", (System.Action)(() =>
                {
                    if (strategyPanel != null) strategyPanel.SetActive(!strategyPanel.activeSelf);
                    settingsPanel.SetActive(false);
                })));
            items.Add(("Settings", "CONFIGURAÇÕES", (System.Action)(() =>
            {
                settingsPanel.SetActive(!settingsPanel.activeSelf);
                if (strategyPanel != null) strategyPanel.SetActive(false);
            })));
            if (online)
                items.Add(("Quit", "SAIR PARA O MENU", (System.Action)(() => { OnlineClient.Disconnect(); SceneLoader.LoadMainMenu(); })));
            else
                items.Add(("Quit", "SAIR PARA O MENU", new System.Action(SceneLoader.LoadMainMenu)));

            const float btnH = 84f, step = 98f, titleH = 110f;
            float totalH = titleH + items.Count * step;
            float topY = totalH * 0.5f;
            UIFactory.Label("Title", panel.transform, "PAUSA", 72, TextAnchor.MiddleCenter, c, c, new Vector2(0f, topY - titleH * 0.5f), new Vector2(800f, titleH));
            var prt = (RectTransform)panel.transform;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float y = topY - titleH - step * i - step * 0.5f;
                // Mesmo visual dos menus (linha laranja) — antes eram botões simples, sem harmonia.
                MenuTheme.StyledButton(prt, it.name, it.label, c, c, new Vector2(0f, y), new Vector2(520f, btnH), 30, it.act);
            }
            if (stratVisible) BuildStrategyPanel(panel.transform, c, new Vector2(-560f, 0f));

            // CONFIGURAÇÕES DA PAUSA: uma opção por LINHA (título em cima, controles centralizados embaixo),
            // painel do tamanho do conteúdo e botão FECHAR. Antes os textos vazavam para fora do painel,
            // "-"/"+" ficavam acima dele e "Controles" saía por cima dos botões da pausa.
            bool mobile = Application.isMobilePlatform;
            int rows = mobile ? 5 : 3;
            const float rowH = 118f, headH = 76f, footH = 96f;
            float panelH = headH + rows * rowH + footH;
            var sp = UIFactory.Panel("SettingsPanel", panel.transform, c, c, new Vector2(590f, 0f), new Vector2(520f, panelH), new Color(0.06f, 0.07f, 0.1f, 0.97f));
            sp.sprite = MenuTheme.RoundedSprite(130, 100, 16);
            var srt = (RectTransform)sp.transform;
            settingsPanel = sp.gameObject;
            var top = new Vector2(0.5f, 1f);
            UIFactory.Label("SetTitle", srt, "CONFIGURAÇÕES", 30, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -38f), new Vector2(480f, 50f), MenuTheme.Accent);
            int r = 0;
            System.Func<float> rowY = () => -headH - rowH * r;

            volumeLabel = UIFactory.Label("Volume", srt, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, rowY() - 18f), new Vector2(480f, 40f));
            MenuTheme.StyledButton(srt, "VolDown", "-", top, top, new Vector2(-90f, rowY() - 74f), new Vector2(150f, 56f), 34, () => ChangeVolume(-0.1f));
            MenuTheme.StyledButton(srt, "VolUp", "+", top, top, new Vector2(90f, rowY() - 74f), new Vector2(150f, 56f), 34, () => ChangeVolume(0.1f));
            ChangeVolume(0f);
            r++;

            // Mapa dinâmico — pode ser mudado a qualquer momento durante a corrida (não define a pista/regras).
            minimapLabel = UIFactory.Label("Minimap", srt, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, rowY() - 18f), new Vector2(480f, 40f));
            MenuTheme.StyledButton(srt, "MinimapToggle", "ALTERNAR", top, top, new Vector2(0f, rowY() - 74f), new Vector2(260f, 56f), 24, ToggleMinimap);
            RefreshMinimapLabel();
            r++;

            // Rádio da equipe (mensagens "RÁDIO:") — preferência que pode mudar a qualquer momento, em TODOS os modos.
            radioLabel = UIFactory.Label("Radio", srt, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, rowY() - 18f), new Vector2(480f, 40f));
            MenuTheme.StyledButton(srt, "RadioToggle", "ALTERNAR", top, top, new Vector2(0f, rowY() - 74f), new Vector2(260f, 56f), 24, ToggleRadio);
            RefreshRadioLabel();
            r++;

            // Método de controle — apenas em mobile; a troca é aplicada na hora, sem reiniciar a corrida.
            if (mobile)
            {
                controlsLabel = UIFactory.Label("Controls", srt, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, rowY() - 18f), new Vector2(480f, 40f));
                MenuTheme.StyledButton(srt, "ControlsPrev", "<", top, top, new Vector2(-90f, rowY() - 74f), new Vector2(150f, 56f), 32, () => ChangeControlMode(-1));
                MenuTheme.StyledButton(srt, "ControlsNext", ">", top, top, new Vector2(90f, rowY() - 74f), new Vector2(150f, 56f), 32, () => ChangeControlMode(1));
                RefreshControlsLabel();
                r++;

                // Aceleração automática — opção permanente (o toggle em jogo foi removido).
                autoThrottleLabel = UIFactory.Label("AutoThrottle", srt, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, rowY() - 18f), new Vector2(480f, 40f));
                MenuTheme.StyledButton(srt, "AutoThrottleToggle", "ALTERNAR", top, top, new Vector2(0f, rowY() - 74f), new Vector2(260f, 56f), 24, ToggleAutoThrottle);
                RefreshAutoThrottleLabel();
                r++;
            }
            foreach (var t in new[] { volumeLabel, minimapLabel, radioLabel, controlsLabel, autoThrottleLabel }) if (t != null) MenuTheme.FitText(t, 28);
            var bot = new Vector2(0.5f, 0f);
            MenuTheme.StyledButton(srt, "SetClose", "FECHAR", bot, bot, new Vector2(0f, 22f), new Vector2(260f, 58f), 24, () => settingsPanel.SetActive(false));

            settingsPanel.SetActive(false);
            panel.SetActive(false);
        }

        // ---- ESTRATÉGIA DE PIT (paradas + compostos) ----

        CarController HumanCar()
        {
            if (race == null) return null;
            foreach (var e in race.entries)
                if (e.isHuman && e.car != null) return e.car;
            return null;
        }

        /// <summary>Estratégia em TODAS as sessões com pit ativo (desde o treino de sexta): no treino/classi
        /// escolhe o pneu da próxima saída (teste de compostos); na corrida, paradas e compostos.</summary>
        bool StrategyVisible
        {
            get
            {
                if (race == null) return false;
                if (RaceX.Core.PitLaneSystem.Instance == null || !RaceX.Core.PitLaneSystem.PitEnabled) return false;
                return HumanCar() != null;
            }
        }

        bool PracticeLike => race != null && race.Settings.laps >= 900;

        void BuildStrategyPanel(Transform parent, Vector2 anchor, Vector2 pos)
        {
            var c = new Vector2(0.5f, 0.5f);
            if (PracticeLike)
            {
                // TREINO/CLASSIFICAÇÃO: pneu da PRÓXIMA saída do box (AUTO = programa da equipe).
                var pp = UIFactory.Panel("StrategyPanel", parent, c, c, pos, new Vector2(440f, 220f), new Color(0.1f, 0.12f, 0.16f, 0.95f));
                strategyPanel = pp.gameObject;
                UIFactory.Label("StratTitle", pp.transform, "PNEU DA PRÓXIMA SAÍDA", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(420f, 40f), MenuTheme.TextDim);
                strategyStartLabel = UIFactory.Label("StratStart", pp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(300f, 50f));
                UIFactory.Button("PcPrev", pp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-170f, -104f), new Vector2(80f, 60f), () => ChangePracticeCompound(-1), 34);
                UIFactory.Button("PcNext", pp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(170f, -104f), new Vector2(80f, 60f), () => ChangePracticeCompound(1), 34);
                UIFactory.Label("StratHint", pp.transform, "vale ao sair da garagem", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -168f), new Vector2(420f, 30f), MenuTheme.TextDim);
                RefreshPracticeCompoundLabel();
                strategyPanel.SetActive(false);
                return;
            }
            var sp = UIFactory.Panel("StrategyPanel", parent, c, c, pos, new Vector2(440f, 300f), new Color(0.1f, 0.12f, 0.16f, 0.95f));
            strategyPanel = sp.gameObject;
            strategyStopsLabel = UIFactory.Label("StratStops", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(400f, 50f));
            strategyStartLabel = UIFactory.Label("StratStart", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(400f, 50f));
            strategyStopsCpdLabel = UIFactory.Label("StratStopCpd", sp.transform, "", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(400f, 50f));
            UIFactory.Button("StopsPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -44f), new Vector2(80f, 60f), () => ChangeStrategy(-1, 0, 0), 34);
            UIFactory.Button("StopsNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -44f), new Vector2(80f, 60f), () => ChangeStrategy(1, 0, 0), 34);
            UIFactory.Button("StartPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -124f), new Vector2(80f, 60f), () => ChangeStrategy(0, -1, 0), 34);
            UIFactory.Button("StartNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -124f), new Vector2(80f, 60f), () => ChangeStrategy(0, 1, 0), 34);
            UIFactory.Button("StopPrev", sp.transform, "<", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-140f, -204f), new Vector2(80f, 60f), () => ChangeStrategy(0, 0, -1), 34);
            UIFactory.Button("StopNext", sp.transform, ">", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(140f, -204f), new Vector2(80f, 60f), () => ChangeStrategy(0, 0, 1), 34);
            RefreshStrategyLabels();
            strategyPanel.SetActive(false);
        }

        void ChangeStrategy(int stopsDelta, int startDelta, int stopDelta)
        {
            var car = HumanCar();
            if (car == null) return;
            int s = Mathf.Clamp(RaceX.Core.PitLaneSystem.PlannedStops(car) + stopsDelta, 0, 3);
            int sc = Mathf.Clamp(RaceX.Core.PitLaneSystem.StrategyStartCompound(car) + startDelta, 0, RaceX.Core.TyreModel.Count - 1);
            int cc = Mathf.Clamp(RaceX.Core.PitLaneSystem.StrategyStopCompound(car) + stopDelta, 0, RaceX.Core.TyreModel.Count - 1);
            RaceX.Core.PitLaneSystem.SetPlayerStrategy(car, s, sc, cc);
            RefreshStrategyLabels();
        }

        void ChangePracticeCompound(int delta)
        {
            var d = RaceX.Career.CareerMode.data;
            if (d == null) return;
            // -1 (AUTO) → 0 S → 1 M → 2 H → 3 I → 4 W → -1 ...
            int v = d.practiceCompound + delta;
            if (v > RaceX.Core.TyreModel.Count - 1) v = -1;
            if (v < -1) v = RaceX.Core.TyreModel.Count - 1;
            d.practiceCompound = v;
            if (v >= 0) RaceX.Core.PitLaneSystem.FitPlayerPracticeCompoundNow(v);
            RaceX.Career.CareerMode.SaveQuiet();
            RefreshPracticeCompoundLabel();
        }

        void RefreshPracticeCompoundLabel()
        {
            if (strategyStartLabel == null) return;
            var d = RaceX.Career.CareerMode.data;
            int v = d != null ? d.practiceCompound : -1;
            strategyStartLabel.text = v < 0 ? "AUTO (EQUIPE)" : RaceX.Core.TyreModel.Name(v);
            strategyStartLabel.color = v < 0 ? MenuTheme.Text : RaceX.Core.TyreModel.ColorOf(v);
        }

        void RefreshStrategyLabels()
        {
            var car = HumanCar();
            if (car == null) return;
            strategyStopsLabel.text = "Paradas: < " + RaceX.Core.PitLaneSystem.PlannedStops(car) + " >";
            strategyStartLabel.text = "Pneu de largada: < " + RaceX.Core.PitLaneSystem.CompoundLetter(RaceX.Core.PitLaneSystem.StrategyStartCompound(car)) + " >";
            strategyStopsCpdLabel.text = "Pneus das paradas: < " + RaceX.Core.PitLaneSystem.CompoundLetter(RaceX.Core.PitLaneSystem.StrategyStopCompound(car)) + " >";
        }

        void ChangeVolume(float delta)
        {
            float v = Mathf.Clamp01(Mathf.Round((SettingsManager.MasterVolume + delta) * 10f) / 10f);
            SettingsManager.MasterVolume = v;
            AudioListener.volume = v;
            volumeLabel.text = $"Volume geral: {Mathf.RoundToInt(v * 100f)}%";
        }

        void ToggleMinimap()
        {
            SettingsManager.MinimapDynamic = !SettingsManager.MinimapDynamic;
            RefreshMinimapLabel();
        }

        void RefreshMinimapLabel()
        {
            minimapLabel.text = "Mini-mapa: " + (SettingsManager.MinimapDynamic ? "Dinâmico (trecho)" : "Pista inteira");
        }

        void ToggleRadio()
        {
            SettingsManager.PitRadio = SettingsManager.PitRadio == 0 ? 1 : 0;
            RefreshRadioLabel();
        }

        void RefreshRadioLabel()
        {
            radioLabel.text = "Rádio da equipe: " + (SettingsManager.PitRadio == 0 ? "DESLIGADO" : "LIGADO");
        }

        void ToggleAutoThrottle()
        {
            TouchControlsUI.AutoThrottleEnabled = !TouchControlsUI.AutoThrottleEnabled;
            RefreshAutoThrottleLabel();
        }

        void RefreshAutoThrottleLabel()
        {
            autoThrottleLabel.text = "Aceleração automática: " + (TouchControlsUI.AutoThrottleEnabled ? "LIGADO" : "DESLIGADO");
        }

        void ChangeControlMode(int delta)
        {
            SettingsManager.ControlMode = SettingsManager.ControlMode == ControlMode.Buttons ? ControlMode.Joystick : ControlMode.Buttons;
            TouchControlsUI.ApplyModeToAll(SettingsManager.ControlMode);
            RefreshControlsLabel();
        }

        void RefreshControlsLabel()
        {
            controlsLabel.text = "Controles: " + ControlNames[(int)SettingsManager.ControlMode];
        }

        int lastToggleFrame = -1;

        /// <summary>Quem abriu a pausa (tela dividida: cada jogador tem o próprio controle e o próprio botão II) — o VOLTAR À PISTA
        /// vale só para o carro dele.</summary>
        CarController requester;

        public void ToggleFor(CarController car)
        {
            if (!IsOpen) requester = car;
            Toggle();
        }

        public void Toggle()
        {
            // Após o fim da sessão a pausa só continua valendo quando a tabela de resultados está
            // OCULTA (botão CONTINUAR) — com resultados na tela, o Toggle fica bloqueado como antes.
            if (race != null && race.State == RaceState.Finished && !HUD.AnyResultsHidden()) return;
            if (lastToggleFrame == Time.frameCount) return;
            lastToggleFrame = Time.frameCount;
            bool open = !panel.activeSelf;
            panel.SetActive(open);
            settingsPanel.SetActive(false);
            if (strategyPanel != null) strategyPanel.SetActive(false);
            transform.SetAsLastSibling();
            if (!online)
            {
                if (open) Time.timeScale = 0f;
                else HUD.ResumeTime(); // volta no 1x/5x escolhido (100 Hz no 1x — física original)
                if (open) RaceX.Career.CareerMode.PauseSessionClock();
                else RaceX.Career.CareerMode.ResumeSessionClock();
            }
            AudioListener.pause = open && !online;
            // ONLINE não pausa (o jogo dos outros continua): o piloto automático dirige o seu carro no SEU ritmo enquanto o
            // menu está aberto — como no F1 (EA) e no Forza. Fechou: o controle volta.
            if (online) OnlineRaceSync.SetPauseAutopilot(open);
            if (!open) SettingsManager.Save();
        }

        void OnDestroy()
        {
            if (online) OnlineRaceSync.SetPauseAutopilot(false);
            if (!online) Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.01f;
            AudioListener.pause = false;
            // NÃO retoma o relógio aqui: o menu é destruído quando a cena da sessão fecha (saída da carreira) — retomar
            // fazia o relógio andar fora da sessão. Quem pausa/retoma ao sair/voltar é o RaceManager.
        }
    }
}
