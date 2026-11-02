using System.Collections.Generic;
using RaceX.Car;
using RaceX.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.Core
{
    /// <summary>ONLINE: carros que ESTE aparelho pilota e transmite além do próprio — o anfitrião pilota a IA e, se alguém
    /// cair da conexão, o carro dessa pessoa (como no F1 da Codemasters/EA: "a IA continua dirigindo o seu carro e, quando
    /// você volta, retoma o controle"). A IA anda no RITMO DO DONO do carro (perfil PlayerPaceLearner que ele mandou ao
    /// carregar a pista); sem perfil, na dificuldade mais fácil — cair não dá vantagem (regra comum nas ligas de F1).</summary>
    public static class OnlineDrive
    {
        public static readonly List<KeyValuePair<string, RaceEntry>> Driven = new List<KeyValuePair<string, RaceEntry>>();

        public static void Reset() => Driven.Clear();

        public static bool IsDriven(RaceEntry e)
        {
            foreach (var kv in Driven) if (kv.Value == e) return true;
            return false;
        }

        /// <summary>Carro de outro aparelho (fantasma) passa a ser pilotado AQUI pela IA, no ritmo dado.</summary>
        public static bool TakeOver(RaceManager race, RaceEntry e, float[] pace, Difficulty? difficulty = null)
        {
            if (race == null || e == null || e.car == null || string.IsNullOrEmpty(e.netId) || IsDriven(e) || e.retired) return false;
            var car = e.car;
            Vector3 v = car.RemoteVelocity;
            var ghost = car.GetComponent<OnlineGhost>();
            if (ghost != null) ghost.enabled = false;
            car.IsRemote = false;
            car.RemoteVelocity = Vector3.zero;
            car.Body.isKinematic = false;
            car.Body.linearVelocity = v;   // sai no embalo em que estava (não freia do nada)
            car.inputLocked = false;
            var ai = car.GetComponent<CarAI>();
            if (ai == null) ai = car.gameObject.AddComponent<CarAI>();
            ai.Initialize(race.Track.Waypoints, race, difficulty ?? (pace != null ? race.Settings.difficulty : Difficulty.Easy));
            ai.paceCap = pace;
            ai.enabled = true;
            Driven.Add(new KeyValuePair<string, RaceEntry>(e.netId, e));
            PitLaneSystem.CarsChanged();
            Debug.Log("[Online] assumiu o carro de " + e.name + (pace != null ? " (no ritmo dele)" : " (IA fácil — sem perfil)"));
            return true;
        }

        /// <summary>Devolve o carro ao dono (voltou a conexão) ou ao novo anfitrião: vira fantasma de novo aqui.</summary>
        public static void Release(RaceEntry e)
        {
            if (e == null) return;
            Driven.RemoveAll(kv => kv.Value == e);
            if (e.car == null) return;
            var car = e.car;
            var ai = car.GetComponent<CarAI>();
            if (ai != null) UnityEngine.Object.Destroy(ai);
            car.throttleInput = 0f; car.brakeInput = 0f; car.steerInput = 0f;
            car.Body.isKinematic = true;
            car.IsRemote = true;
            var ghost = car.GetComponent<OnlineGhost>();
            if (ghost == null) { ghost = car.gameObject.AddComponent<OnlineGhost>(); ghost.playerId = e.netId; }
            ghost.race = OnlineRaceSync.Instance != null ? OnlineRaceSync.Instance.Race : null;
            ghost.entry = e;
            ghost.enabled = true;
            PitLaneSystem.CarsChanged();
            Debug.Log("[Online] carro de " + e.name + " devolvido (segue a posição da rede)");
        }

        public static RaceEntry EntryOf(RaceManager race, string netId)
        {
            if (race == null || string.IsNullOrEmpty(netId)) return null;
            foreach (var e in race.entries) if (e.netId == netId) return e;
            return null;
        }
    }

    /// <summary>ONLINE dentro da corrida.
    /// (1) LARGADA SINCRONIZADA: avisa o servidor que a pista carregou e mostra "AGUARDANDO JOGADOR(ES)" com os nomes até
    /// TODOS carregarem; as luzes começam juntas, com a MESMA espera com as 5 acesas. Antes cada aparelho largava quando
    /// terminava de carregar (o convidado largava antes e a IA, pilotada pelo anfitrião, só saía quando ele chegava).
    /// (2) VOTAÇÃO: "REINICIAR A CORRIDA?" / "NOVA PARTIDA?" com SIM | NÃO e contagem de 10 s (sem resposta = não).
    /// (3) Nova corrida da sala (reinício ou nova partida aceitos) recarrega a pista para TODOS, na mesma sala.
    /// (4) Resultado oficial do servidor fecha a corrida também para quem ainda estava na pista.</summary>
    public class OnlineRaceSync : MonoBehaviour
    {
        public static OnlineRaceSync Instance { get; private set; }

        /// <summary>Sem "vai" do servidor depois disso, larga assim mesmo (servidor antigo/queda).</summary>
        const float GoFallbackSeconds = 300f;

        RaceManager race;
        public RaceManager Race => race;
        string myName;
        int myRound;
        bool waitGo;
        bool started;
        float createdAt;
        float nextLoadedResend;

        GameObject waitPanel;
        Text waitTitle, waitNames;
        GameObject votePanel;
        Text voteTitle, voteSub, voteCount;
        GameObject voteButtons;
        GameObject toastPanel;
        Text toastText;
        float toastUntil;
        bool leaving;

        public static OnlineRaceSync Create(Canvas canvas, RaceManager race, string myName)
        {
            var root = UIFactory.Stretch("OnlineRaceSync", canvas.transform);
            var sync = root.gameObject.AddComponent<OnlineRaceSync>();
            sync.race = race;
            sync.myName = myName;
            sync.myRound = OnlineClient.Round;
            sync.createdAt = Time.realtimeSinceStartup;
            sync.waitGo = OnlineClient.StartInfo != null && OnlineClient.StartInfo.sync;
            sync.Build(root);
            return sync;
        }

        void Build(RectTransform root)
        {
            var c = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);

            // Espera da largada: tela escurecida com o aviso no centro (ninguém anda antes de todos carregarem).
            var dim = UIFactory.Panel("WaitDim", root, c, c, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
            var drt = (RectTransform)dim.transform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.sizeDelta = Vector2.zero;
            dim.raycastTarget = false;
            waitPanel = dim.gameObject;
            var box = UIFactory.Panel("WaitBox", dim.transform, c, c, new Vector2(0f, 60f), new Vector2(980f, 230f), new Color(0.05f, 0.06f, 0.09f, 0.95f));
            box.sprite = MenuTheme.RoundedSprite(140, 100, 16);
            UIFactory.Panel("WaitLine", box.transform, top, top, Vector2.zero, new Vector2(980f, 4f), MenuTheme.Accent);
            waitTitle = UIFactory.Label("WaitTitle", box.transform, "", 44, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -62f), new Vector2(940f, 70f), MenuTheme.Text);
            waitNames = UIFactory.Label("WaitNames", box.transform, "", 30, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -150f), new Vector2(940f, 90f), MenuTheme.Accent);
            waitNames.horizontalOverflow = HorizontalWrapMode.Wrap;
            waitPanel.SetActive(waitGo);

            // Votação: faixa compacta NO ALTO (a corrida continua — não cobre os controles nem o meio da pista).
            var vp = UIFactory.Panel("VotePanel", root, top, top, new Vector2(0f, -150f), new Vector2(860f, 200f), new Color(0.05f, 0.06f, 0.09f, 0.95f));
            vp.sprite = MenuTheme.RoundedSprite(140, 100, 16);
            vp.raycastTarget = true;
            votePanel = vp.gameObject;
            var vrt = (RectTransform)vp.transform;
            UIFactory.Panel("VoteLine", vrt, top, top, Vector2.zero, new Vector2(860f, 4f), MenuTheme.Accent);
            voteTitle = UIFactory.Label("VoteTitle", vrt, "", 36, TextAnchor.MiddleLeft, top, top, new Vector2(-70f, -40f), new Vector2(680f, 52f), MenuTheme.Text);
            voteSub = UIFactory.Label("VoteSub", vrt, "", 24, TextAnchor.MiddleLeft, top, top, new Vector2(-70f, -84f), new Vector2(680f, 36f), MenuTheme.TextDim);
            voteCount = UIFactory.Label("VoteCount", vrt, "", 64, TextAnchor.MiddleCenter, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(110f, 90f), MenuTheme.Accent);
            var btns = UIFactory.Rect("VoteBtns", vrt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(820f, 70f));
            voteButtons = btns.gameObject;
            var yes = MenuTheme.StyledButton(btns, "VoteYes", "SIM", new Vector2(0.5f, 0.5f), c, new Vector2(-150f, 0f), new Vector2(260f, 66f), 30, () => OnlineClient.AnswerVote(true));
            yes.GetComponent<Image>().color = new Color(0.12f, 0.42f, 0.2f, 1f);
            var no = MenuTheme.StyledButton(btns, "VoteNo", "NÃO", new Vector2(0.5f, 0.5f), c, new Vector2(150f, 0f), new Vector2(260f, 66f), 30, () => OnlineClient.AnswerVote(false));
            no.GetComponent<Image>().color = new Color(0.45f, 0.12f, 0.1f, 1f);
            votePanel.SetActive(false);

            // Aviso curto (resultado da votação, conexão, "aguardando o resultado oficial").
            var tp = UIFactory.Panel("Toast", root, top, top, new Vector2(0f, -150f), new Vector2(900f, 70f), new Color(0.05f, 0.06f, 0.09f, 0.92f));
            tp.sprite = MenuTheme.RoundedSprite(140, 60, 14);
            tp.raycastTarget = false;
            toastPanel = tp.gameObject;
            toastText = UIFactory.Label("ToastText", tp.transform, "", 28, TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(880f, 66f), MenuTheme.Text);
            toastText.raycastTarget = false;
            toastPanel.SetActive(false);
        }

        void OnEnable()
        {
            Instance = this;
            OnlineClient.OnGo += OnGo;
            OnlineClient.OnWaiting += RefreshWaiting;
            OnlineClient.OnVoteChanged += RefreshVote;
            OnlineClient.OnVoteResult += OnVoteResult;
            OnlineClient.OnKicked += OnKicked;
            OnlineClient.OnGameStart += OnGameStart;
            OnlineClient.OnFinished += OnOfficialResult;
            OnlineClient.OnError += OnError;
            OnlineClient.OnDropped += OnDropped;
            OnlineClient.OnBack += OnBack;
            OnlineClient.OnHostChanged += OnHostChanged;
            OnlineClient.OnRejoined += OnRejoined;
        }

        void OnDisable()
        {
            OnlineClient.OnGo -= OnGo;
            OnlineClient.OnWaiting -= RefreshWaiting;
            OnlineClient.OnVoteChanged -= RefreshVote;
            OnlineClient.OnVoteResult -= OnVoteResult;
            OnlineClient.OnKicked -= OnKicked;
            OnlineClient.OnGameStart -= OnGameStart;
            OnlineClient.OnFinished -= OnOfficialResult;
            OnlineClient.OnError -= OnError;
            OnlineClient.OnDropped -= OnDropped;
            OnlineClient.OnBack -= OnBack;
            OnlineClient.OnHostChanged -= OnHostChanged;
            OnlineClient.OnRejoined -= OnRejoined;
            if (Instance == this) Instance = null;
        }

        // ---------- Queda de conexão / troca de anfitrião ----------
        /// <summary>Alguém caiu: o ANFITRIÃO assume o carro dele no ritmo dele (o perfil veio no player:loaded).</summary>
        void OnDropped(string id)
        {
            var e = OnlineDrive.EntryOf(race, id);
            if (e == null || e.isHuman) return;   // eu mesmo: aqui continuo dirigindo (o jogo segue local)
            Toast((e.name ?? "JOGADOR").ToUpperInvariant() + " PERDEU A CONEXÃO — PILOTO AUTOMÁTICO", 4f);
            if (OnlineClient.IsHost) OnlineDrive.TakeOver(race, e, PlayerPaceLearner.ParseProfile(OnlineClient.Dropped.TryGetValue(id, out var p) ? p : null));
        }

        /// <summary>Voltou: o carro volta a seguir a posição dele (ele dirige de novo).</summary>
        void OnBack(string id)
        {
            var e = OnlineDrive.EntryOf(race, id);
            if (e == null || e.isHuman) return;
            Toast((e.name ?? "JOGADOR").ToUpperInvariant() + " VOLTOU", 3f);
            if (OnlineDrive.IsDriven(e) && !id.StartsWith("ai_")) OnlineDrive.Release(e);
        }

        /// <summary>Anfitrião caiu e a sala passou para mim: passo a pilotar a IA e quem estiver fora (antes a IA congelava).
        /// Deixei de ser anfitrião (voltei depois de cair): solto tudo — os carros seguem o novo anfitrião.</summary>
        void OnHostChanged()
        {
            if (race == null) return;
            if (OnlineClient.IsHost)
            {
                int n = 0;
                foreach (var e in race.entries)
                {
                    if (e == null || e.isHuman || string.IsNullOrEmpty(e.netId) || OnlineDrive.IsDriven(e)) continue;
                    if (e.netId.StartsWith("ai_"))
                    {
                        if (OnlineDrive.TakeOver(race, e, null, race.Settings.difficulty)) n++;   // IA da sala: dificuldade normal
                    }
                    else if (OnlineClient.Dropped.TryGetValue(e.netId, out var pace) && OnlineDrive.TakeOver(race, e, PlayerPaceLearner.ParseProfile(pace))) n++;
                }
                if (n > 0) Toast("VOCÊ VIROU O ANFITRIÃO — PILOTANDO " + n + " CARROS", 4f);
            }
            else
            {
                foreach (var kv in new List<KeyValuePair<string, RaceEntry>>(OnlineDrive.Driven)) OnlineDrive.Release(kv.Value);
            }
        }

        void OnRejoined()
        {
            Toast("CONEXÃO RECUPERADA", 3f);
        }

        // ---------- Menu aberto no online ----------
        static RaceX.Input.PlayerInputHandler pauseInput;
        static CarAI pauseAi;

        /// <summary>Menu aberto no online: a corrida NÃO para (não dá para pausar o jogo dos outros). Como no F1 (EA) e no Forza,
        /// o piloto automático dirige o seu carro enquanto o menu está aberto — aqui no SEU ritmo (perfil desta pista; sem
        /// perfil, IA fácil), então abrir o menu não dá vantagem. Fechou o menu: o controle volta para você.</summary>
        public static void SetPauseAutopilot(bool on)
        {
            var s = Instance;
            if (s == null || s.race == null) return;
            if (!on)
            {
                if (pauseAi != null)
                {
                    var car = pauseAi.car;
                    bool keep = s.race.State == RaceState.Finished;   // depois da linha o automático continua (volta de desaceleração)
                    if (!keep)
                    {
                        UnityEngine.Object.Destroy(pauseAi);
                        if (car != null) { car.throttleInput = 0f; car.brakeInput = 0f; car.steerInput = 0f; }
                    }
                }
                if (pauseInput != null) pauseInput.enabled = true;
                pauseAi = null;
                pauseInput = null;
                return;
            }
            if (s.race.State != RaceState.Racing || pauseAi != null) return;
            foreach (var e in s.race.entries)
            {
                if (!e.isHuman || e.car == null || e.finished || e.retired) continue;
                if (e.car.GetComponent<CarAI>() != null) return;   // já tem piloto automático (autoteste)
                pauseInput = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                if (pauseInput != null) pauseInput.enabled = false;
                var pace = PlayerPaceLearner.Profile((int)s.race.Settings.track);
                pauseAi = e.car.gameObject.AddComponent<CarAI>();
                pauseAi.Initialize(s.race.Track.Waypoints, s.race, pace != null ? s.race.Settings.difficulty : Difficulty.Easy);
                pauseAi.paceCap = pace;
                return;
            }
        }

        void Start()
        {
            if (!waitGo)
            {
                // Servidor antigo (sem largada sincronizada): comportamento de antes.
                started = true;
                race.BeginCountdown();
                return;
            }
            // "Carreguei" + o MEU ritmo nesta pista: se eu cair, o anfitrião pilota meu carro nesse ritmo.
            OnlineClient.SendLoaded(PlayerPaceLearner.ExportTrack((int)race.Settings.track));
            nextLoadedResend = Time.realtimeSinceStartup + 3f;
            RefreshWaiting();
            if (OnlineClient.GoAtRealtime > 0f) OnGo();
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (!started && waitGo)
            {
                bool go = OnlineClient.GoAtRealtime > 0f && now >= OnlineClient.GoAtRealtime;
                bool fallback = now - createdAt > GoFallbackSeconds;
                if (go || fallback) BeginRace(fallback);
                else if (OnlineClient.GoAtRealtime < 0f && now >= nextLoadedResend)
                {
                    // "Carreguei" de novo a cada 3 s até vir o "vai" (o servidor ignora repetição): se a mensagem se perdeu numa
                    // reconexão, a sala não fica esperando por este aparelho.
                    nextLoadedResend = now + 3f;
                    OnlineClient.SendLoaded(PlayerPaceLearner.ExportTrack((int)race.Settings.track));
                }
            }
            var v = OnlineClient.Vote;
            if (v != null && votePanel.activeSelf)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(v.deadline - now));
                voteCount.text = left.ToString();
                if (now > v.deadline + 3f) { OnlineClient.Vote = null; RefreshVote(); }   // servidor não respondeu
            }
            if (toastPanel.activeSelf && now > toastUntil) toastPanel.SetActive(false);
        }

        void BeginRace(bool fallback)
        {
            if (started) return;
            started = true;
            waitPanel.SetActive(false);
            if (fallback) Debug.LogWarning("[Online] sem o \"vai\" do servidor em " + GoFallbackSeconds + " s — largando assim mesmo");
            race.BeginCountdown(!fallback && OnlineClient.GoHoldSeconds >= 0f ? OnlineClient.GoHoldSeconds : -1f);
        }

        void OnGo()
        {
            if (started) return;
            waitTitle.text = "TODOS PRONTOS";
            waitNames.text = "LARGADA EM INSTANTES";
        }

        void RefreshWaiting()
        {
            if (started || waitTitle == null) return;
            if (OnlineClient.GoAtRealtime > 0f) { OnGo(); return; }
            var list = OnlineClient.WaitingFor;
            if (!OnlineClient.WaitingKnown || list.Count == 0)
            {
                waitTitle.text = "SINCRONIZANDO A LARGADA";
                waitNames.text = "";
                return;
            }
            // Pedido do usuário: "Aguardando jogador" — no plural quando é mais de um.
            waitTitle.text = list.Count == 1 ? "AGUARDANDO JOGADOR" : "AGUARDANDO JOGADORES";
            waitNames.text = string.Join(", ", list).ToUpperInvariant();
        }

        void RefreshVote()
        {
            var v = OnlineClient.Vote;
            if (v == null) { votePanel.SetActive(false); return; }
            votePanel.SetActive(true);
            transform.SetAsLastSibling();   // por cima da tabela de resultados
            toastPanel.SetActive(false);
            voteTitle.text = v.kind == "restart" ? "REINICIAR A CORRIDA?" : "NOVA PARTIDA?";
            string who = string.IsNullOrEmpty(v.byName) ? "Um jogador" : v.byName;
            if (v.mine) voteSub.text = "Aguardando os outros pilotos (" + v.yes + "/" + Mathf.Max(v.total, 1) + ")";
            else if (v.answered) voteSub.text = "Resposta enviada — aguardando os outros";
            else voteSub.text = who + (v.kind == "restart" ? " pediu para reiniciar" : " está pedindo uma nova partida");
            voteButtons.SetActive(!v.mine && !v.answered);
        }

        void OnVoteResult(bool accepted, string kind, string text)
        {
            votePanel.SetActive(false);
            Toast(text, accepted ? 6f : 4f);
        }

        void OnKicked(string msg)
        {
            if (leaving) return;
            leaving = true;
            Toast(string.IsNullOrEmpty(msg) ? "VOCÊ SAIU DA SALA" : msg.ToUpperInvariant(), 3f);
            Invoke(nameof(LeaveToMenu), 2.5f);
        }

        void LeaveToMenu()
        {
            OnlineClient.Disconnect();
            Time.timeScale = 1f;
            SceneLoader.LoadMainMenu();
        }

        /// <summary>Nova corrida da sala (reinício/nova partida votados): TODOS recarregam a pista juntos.</summary>
        void OnGameStart(GameStartInfo info)
        {
            if (leaving || info == null || info.round == myRound) return;
            leaving = true;
            Time.timeScale = 1f;
            RaceX.UI.MainMenuUI.LaunchOnlineRace(info, myName);
        }

        /// <summary>Resultado oficial chegou: quem ainda está na pista termina aqui também (o HUD mostra a tabela oficial).</summary>
        void OnOfficialResult()
        {
            if (race != null && race.State != RaceState.Finished) race.EndNow();
        }

        void OnError(string msg)
        {
            if (!string.IsNullOrEmpty(msg)) Toast(msg.ToUpperInvariant(), 4f);
        }

        /// <summary>Aviso curto no alto da tela (vazio = esconde).</summary>
        public static void Toast(string text, float seconds = 3f)
        {
            var s = Instance;
            if (s == null || s.toastPanel == null) return;
            if (string.IsNullOrEmpty(text) || seconds <= 0f) { s.toastPanel.SetActive(false); return; }
            if (s.votePanel != null && s.votePanel.activeSelf) return;   // a votação tem prioridade
            s.toastText.text = text;
            MenuTheme.FitText(s.toastText, 28);
            s.toastUntil = Time.realtimeSinceStartup + seconds;
            s.toastPanel.SetActive(true);
            s.transform.SetAsLastSibling();
        }
    }
}
