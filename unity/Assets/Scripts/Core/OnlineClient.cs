using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using RaceX.Car;
using UnityEngine;

namespace RaceX.Core
{
    public class RemoteState
    {
        public string id;
        public Vector3 position;
        public float rotation;
        public float speed;
        public int lap;
        public int checkpoint;
        public float timestamp;
        /// <summary>0 = correndo, 1 = cruzou a linha final, 2 = abandonou (perda total/quebra/desclassificado).</summary>
        public int status;
    }

    public class StartSlotInfo
    {
        public string id;
        public int slot;
        public bool isAI;
        public string name;
        public string carModel;
        public int carColor;
    }

    public class GameStartInfo
    {
        public string trackId;
        public int laps;
        public int totalCars;
        public string difficulty;
        public string category = "gt";
        public bool pitStops;
        public string weather = "";
        public bool totalLoss = true;
        public bool penalties = true;
        public long startAt;
        /// <summary>Servidor com largada sincronizada: esperar o game:go (todos carregados) antes das luzes.</summary>
        public bool sync;
        /// <summary>Número da corrida na sala (1ª, reinício, nova partida...).</summary>
        public int round;
        public readonly List<StartSlotInfo> startPositions = new List<StartSlotInfo>();
    }

    public class LobbyPlayerInfo
    {
        public string id;
        public string name;
        public bool ready;
        public string carModel;
        public int carColor;
        public bool isHost;
        public bool connected;
    }

    public class FinishedRanking
    {
        public int position;
        public string id;
        public bool isAI;
        public string name;
        public bool finished;
        public bool retired;
        public float totalTime;
        public float bestLap;
        public float penalty;
        public int lap;
    }

    /// <summary>Votação da sala: "restart" (reiniciar a corrida — todos precisam aceitar) ou "rematch" (jogar novamente).</summary>
    public class VoteInfo
    {
        public string id;
        public string kind;
        public string byId;
        public string byName;
        /// <summary>Relógio real (Time.realtimeSinceStartup) em que a votação fecha.</summary>
        public float deadline;
        /// <summary>Fui eu que pedi (não respondo — só espero).</summary>
        public bool mine;
        public bool answered;
        public int yes = 1, no, total;
    }

    public static class OnlineClient
    {
        public const string ServerUrlKey = "RaceX.ServerUrl";
        public const string DefaultServerUrl = "wss://racex-multiplayer.onrender.com/socket.io/?EIO=4&transport=websocket";

        public static bool Connected;
        public static bool ConnectFailed => connectFailed;
        public static string RoomCode;
        public static bool IsHost;
        public static string MyPlayerId;
        public static string LastError;
        public static string LastServerMessage;
        public static GameStartInfo StartInfo;
        public static readonly List<LobbyPlayerInfo> LobbyPlayers = new List<LobbyPlayerInfo>();
        public static readonly Dictionary<string, RemoteState> RemoteStates = new Dictionary<string, RemoteState>();
        public static readonly List<FinishedRanking> Rankings = new List<FinishedRanking>();
        public static float RemoteStateTimestamp;
        public static string StateCorrection;

        public static LobbyPlayerInfo Me => LobbyPlayers.Find(p => p.id == MyPlayerId);

        public static Action<string> OnRoomCreated;
        public static Action<string> OnRoomJoined;
        public static Action<string> OnError;
        public static Action<string> OnWarning;
        public static Action OnLobbyUpdate;
        public static Action<GameStartInfo> OnGameStart;
        public static Action OnStateUpdate;
        public static Action OnFinished;

        // ---------- Largada sincronizada ----------
        /// <summary>Nomes de quem ainda está carregando a pista (game:waiting). WaitingKnown = já veio a 1ª lista.</summary>
        public static readonly List<string> WaitingFor = new List<string>();
        public static bool WaitingKnown;
        /// <summary>"Vai" recebido (game:go): relógio real em que as luzes começam e a espera com as 5 acesas (a MESMA para todos).</summary>
        public static float GoAtRealtime = -1f;
        public static float GoHoldSeconds = -1f;
        public static int Round;
        public static Action OnWaiting;
        public static Action OnGo;

        // ---------- Votação (reiniciar / jogar novamente) ----------
        public static VoteInfo Vote;
        public static Action OnVoteChanged;
        /// <summary>(aceito, tipo, texto para a tela).</summary>
        public static Action<bool, string, string> OnVoteResult;
        /// <summary>Tirado da sala (recusou a nova partida) — volta ao menu.</summary>
        public static Action<string> OnKicked;

        // ---------- Queda de conexão na corrida ----------
        /// <summary>Senha de volta à sala (vem só para mim no room:joined): reconectar e retomar o MEU carro.</summary>
        public static string MyToken;
        /// <summary>Jogadores fora do ar agora (id → ritmo deles nesta pista): o anfitrião pilota esses carros.</summary>
        public static readonly Dictionary<string, string> Dropped = new Dictionary<string, string>();
        public static Action<string> OnDropped;
        public static Action<string> OnBack;
        public static Action OnHostChanged;
        public static Action OnRejoined;
        /// <summary>Janela para voltar à corrida depois de cair (s).</summary>
        const float RejoinWindow = 120f;
        static bool rejoining;

        public static string ServerUrl
        {
            get
            {
                string url = PlayerPrefs.GetString(ServerUrlKey, "");
                if (string.IsNullOrEmpty(url)) url = DefaultServerUrl;
                return NormalizeServerUrl(url);
            }
            set
            {
                PlayerPrefs.SetString(ServerUrlKey, NormalizeServerUrl(value));
                PlayerPrefs.Save();
            }
        }

        static string NormalizeServerUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return DefaultServerUrl;
            if (url.IndexOf("socket.io", StringComparison.OrdinalIgnoreCase) >= 0) return url;
            string baseUrl = url.Trim();
            if (baseUrl.Length == 0) return DefaultServerUrl;
            if (!baseUrl.Contains("://")) baseUrl = "ws://" + baseUrl;
            return baseUrl.TrimEnd('/') + "/socket.io/?EIO=4&transport=websocket";
        }

        /// <summary>Log detalhado [Online]: OFF por padrão (log limpo no dispositivo).
        /// Warnings e erros continuam sempre visíveis.</summary>
        public static bool VerboseLogs = false;
        static void Log(string msg) { if (VerboseLogs) Debug.Log("[Online] " + msg); }

        /// <summary>Nome do carro no servidor = nome do enum em minúsculo (server/utils.js CAR_MODELS).
        /// Antes só existiam falcon/titan/storm: o F1 ia como "storm" e o resto virava Porsche.</summary>
        public static CarModel CarModelFromServer(string name)
        {
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out CarModel m) && Enum.IsDefined(typeof(CarModel), m)) return m;
            return CarModel.Titan;
        }

        public static string CarModelToServer(CarModel model) => model.ToString().ToLowerInvariant();

        public static RemoteState GetRemote(string id) => id != null && RemoteStates.TryGetValue(id, out var s) ? s : null;

        // ---------- Servidor dormindo (Render Free) ----------
        // O plano gratuito desliga o servidor parado; a 1ª conexão leva até ~1 min para ACORDAR.
        // Antes o cliente desistia em 10 s ("Tempo esgotado") — era o erro antes de criar a sala.
        // Agora: cada tentativa espera 20 s e reconecta sozinho até 90 s no total.
        const float AttemptTimeout = 20f;
        const float WakeWindow = 90f;
        const float RetryDelay = 3f;
        static float connectWindowEnd;
        static float retryAt = -1f;
        static int attempt;

        /// <summary>Texto de estado da conexão para a UI (vazio quando conectado).</summary>
        public static string ConnectionStatus { get; private set; } = "";

        public static void Connect(string serverUrl)
        {
            attempt = 0;
            connectWindowEnd = Time.realtimeSinceStartup + WakeWindow;
            retryAt = -1f;
            ConnectionStatus = "Conectando ao servidor...";
            ConnectAttempt(serverUrl, true);
        }

        static void ConnectAttempt(string serverUrl, bool resetRoom)
        {
            EnsurePump();
            StopSocket();
            attempt++;
            if (resetRoom) ResetRoomState();
            OpenSocket(serverUrl);
        }

        static void ResetRoomState()
        {
            Connected = false;
            RoomCode = null;
            IsHost = false;
            MyPlayerId = null;
            LastError = null;
            StartInfo = null;
            LobbyPlayers.Clear();
            RemoteStates.Clear();
            Rankings.Clear();
            StateCorrection = null;
            pending = null;
            ClearRaceSync();
        }

        static void ClearRaceSync()
        {
            WaitingFor.Clear();
            WaitingKnown = false;
            GoAtRealtime = -1f;
            GoHoldSeconds = -1f;
            Vote = null;
            Dropped.Clear();
        }

        static void OpenSocket(string serverUrl)
        {
            lastUrl = NormalizeServerUrl(serverUrl);
            Log("Conectando em " + lastUrl);
            connectFailed = false;
            socketRunning = true;
            socketIoConnected = false;
            connectStartedAt = Time.realtimeSinceStartup;
            try
            {
                cts = new CancellationTokenSource();
                var uri = new Uri(lastUrl);
                socketThread = new Thread(() => SocketLoop(uri, cts.Token)) { IsBackground = true, Name = "RaceXSocket" };
                socketThread.Start();
            }
            catch (Exception ex)
            {
                socketRunning = false;
                OnError?.Invoke("Falha ao iniciar conexão: " + ex.Message);
            }
        }

        /// <summary>Falhou esta tentativa: tenta de novo se ainda estiver na janela de "acordar".</summary>
        static bool ScheduleRetry(string why)
        {
            if (Time.realtimeSinceStartup + RetryDelay >= connectWindowEnd) return false;
            retryAt = Time.realtimeSinceStartup + RetryDelay;
            connectStartedAt = 0f;
            ConnectionStatus = "Acordando o servidor (plano gratuito leva até 1 min)... tentativa " + (attempt + 1);
            Log("Tentativa " + attempt + " falhou (" + why + "), nova em " + RetryDelay + " s");
            return true;
        }

        public static void Disconnect()
        {
            rejoining = false;
            MyToken = null;
            retryAt = -1f;
            connectWindowEnd = 0f;
            ConnectionStatus = "";
            pending = null;
            Connected = false;
            connectFailed = false;
            connectStartedAt = 0f;
            StopSocket();
            RoomCode = null;
            IsHost = false;
            MyPlayerId = null;
            LastError = null;
            StartInfo = null;
            LobbyPlayers.Clear();
            RemoteStates.Clear();
            Rankings.Clear();
            StateCorrection = null;
            ClearRaceSync();
        }

        static void StopSocket()
        {
            socketRunning = false;
            try { cts?.Cancel(); } catch { }
            var th = socketThread;
            socketThread = null;
            if (th != null && th != Thread.CurrentThread)
            {
                try { th.Join(400); } catch { }
            }
            try { ws?.Dispose(); } catch { }
            ws = null;
            cts = null;
            sendLock = new SemaphoreSlim(1, 1);
            socketIoConnected = false;
        }

        public static void CreateRoom(string hostName)
        {
            IsHost = true; // papel pretendido: a tela de anfitrião já aparece enquanto conecta
            pending = new PendingAction { kind = 0, name = hostName };
            DrainPending();
        }

        public static void JoinRoom(string code, string name)
        {
            IsHost = false;
            pending = new PendingAction { kind = 1, code = code, name = name };
            DrainPending();
        }

        public static void UpdatePlayer(int carModel, int carColor)
        {
            Emit("player:join", "{\"carModel\":" + JsonString(CarModelToServer((CarModel)carModel)) + ",\"carColor\":" + carColor + "}");
        }

        public static void SetReady(bool ready)
        {
            Emit("player:ready", "{\"ready\":" + (ready ? "true" : "false") + "}");
        }

        public static void StartRace()
        {
            Emit("room:start", "{}");
        }

        /// <summary>Pista montada neste aparelho: o servidor só dá a largada quando TODOS mandarem isto. Vai junto o RITMO do
        /// jogador nesta pista (perfil "v;v;..." do PlayerPaceLearner): se ele cair, o anfitrião pilota o carro nesse ritmo.</summary>
        public static void SendLoaded(string pace = "") => Emit("player:loaded", "{\"pace\":" + JsonString(pace ?? "") + "}");

        /// <summary>Pede votação: "restart" (reiniciar a corrida) ou "rematch" (jogar novamente, na mesma sala).</summary>
        public static void RequestVote(string kind) => Emit("vote:request", "{\"kind\":" + JsonString(kind) + "}");

        public static void AnswerVote(bool yes)
        {
            var v = Vote;
            if (v == null || v.mine || v.answered) return;
            v.answered = true;
            Emit("vote:answer", "{\"voteId\":" + JsonString(v.id) + ",\"yes\":" + (yes ? "true" : "false") + "}");
            OnVoteChanged?.Invoke();
        }

        /// <summary>Abandono do jogador (perda total, quebra, desclassificado): fecha a corrida dele no servidor.</summary>
        public static void SendRetired() => Emit("player:finished", "{\"retired\":true}");

        /// <summary>Pista escolhida na sala (atualizada pelo broadcast do servidor).</summary>
        public static string RoomTrackId = "forest";
        public static int RoomLaps = 3;
        public static int RoomTotalCars = 8;
        public static string RoomDifficulty = "medium";
        public static string RoomCategory = "gt";
        public static bool RoomPitStops;
        public static string RoomWeather = "";
        public static bool RoomTotalLoss = true;
        public static bool RoomPenalties = true;

        /// <summary>Host altera as configurações da sala — o servidor valida, aplica e re-transmite no lobby
        /// (o que ele recusar volta como aviso "room:warning").</summary>
        public static void SendRoomSettings(string trackId, int laps, int totalCars, string difficulty, string category, bool pitStops, string weather, bool totalLoss, bool penalties)
        {
            if (!IsHost) return;
            Emit("room:settings", "{\"trackId\":" + JsonString(trackId) +
                 ",\"laps\":" + laps + ",\"totalCars\":" + totalCars +
                 ",\"difficulty\":" + JsonString(difficulty) +
                 ",\"category\":" + JsonString(category) +
                 ",\"pitStops\":" + (pitStops ? "true" : "false") +
                 ",\"weather\":" + JsonString(weather ?? "") +
                 ",\"totalLoss\":" + (totalLoss ? "true" : "false") +
                 ",\"penalties\":" + (penalties ? "true" : "false") + "}");
        }

        /// <summary>Anfitrião: posições dos NPCs (ele pilota a IA; os convidados só exibem).</summary>
        public static void SendAiPositions(List<string> items)
        {
            if (items == null || items.Count == 0) return;
            Emit("ai:positions", "[" + string.Join(",", items) + "]");
        }

        /// <summary>dist = distância de corrida percorrida (m) — desempata quem ainda não terminou no placar oficial.
        /// status 0 correndo / 2 abandonou. penalty = segundos de penalidade (já somados no finishedTime).</summary>
        public static string AiStateJson(string id, Vector3 p, float rot, float speed, int lap, int checkpoint, float finishedTime, float bestLap, float dist = 0f, int status = 0, float penalty = 0f)
        {
            var f = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(200);
            sb.Append("{\"id\":").Append(JsonString(id))
              .Append(",\"x\":").Append(p.x.ToString(f)).Append(",\"y\":").Append(p.y.ToString(f)).Append(",\"z\":").Append(p.z.ToString(f))
              .Append(",\"rotation\":").Append(rot.ToString(f)).Append(",\"speed\":").Append(speed.ToString(f))
              .Append(",\"lap\":").Append(lap).Append(",\"checkpoint\":").Append(checkpoint)
              .Append(",\"dist\":").Append(dist.ToString("0.0", f)).Append(",\"st\":").Append(status);
            if (finishedTime > 0f) sb.Append(",\"finishedTime\":").Append(finishedTime.ToString(f)).Append(",\"bestLap\":").Append(bestLap.ToString(f))
                                    .Append(",\"penalty\":").Append(penalty.ToString(f));
            return sb.Append('}').ToString();
        }

        public static void SendPosition(float x, float y, float z, float rot, float speed, int lap, int checkpoint, float dist = 0f, int status = 0)
        {
            var f = CultureInfo.InvariantCulture;
            Emit("player:position",
                "{\"x\":" + x.ToString(f) + ",\"y\":" + y.ToString(f) + ",\"z\":" + z.ToString(f) +
                ",\"rotation\":" + rot.ToString(f) + ",\"speed\":" + speed.ToString(f) +
                ",\"lap\":" + lap + ",\"checkpoint\":" + checkpoint +
                ",\"dist\":" + dist.ToString("0.0", f) + ",\"st\":" + status + "}");
        }

        /// <summary>Fim da corrida do jogador: tempo total (já com penalidades), melhor volta e penalidade (s).</summary>
        public static void SendFinished(float totalTime = 0f, float bestLap = 0f, float penalty = 0f)
        {
            var f = CultureInfo.InvariantCulture;
            Emit("player:finished", "{\"totalTime\":" + totalTime.ToString(f) + ",\"bestLap\":" + bestLap.ToString(f) + ",\"penalty\":" + penalty.ToString(f) + "}");
        }

        static readonly ConcurrentQueue<SocketEvent> Received = new ConcurrentQueue<SocketEvent>();
        static readonly ConcurrentQueue<string> Outgoing = new ConcurrentQueue<string>();
        static volatile bool socketRunning;
        static volatile bool socketIoConnected;
        static ClientWebSocket ws;
        static CancellationTokenSource cts;
        static Thread socketThread;
        static SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        /// <summary>Acorda o envio assim que algo entra na fila.</summary>
        static readonly SemaphoreSlim outgoingSignal = new SemaphoreSlim(0, 1);

        /// <summary>Envio em laço PRÓPRIO: antes a fila só era esvaziada antes de cada ReceiveAsync (que bloqueia
        /// até o servidor mandar algo). No lobby o servidor fica ~25 s calado (só ping) — as mudanças do anfitrião
        /// saíam atrasadas e em bloco, cada uma com o estado antigo, e uma desfazia a outra (pista voltava).</summary>
        static void SendLoop(CancellationToken token)
        {
            try
            {
                while (socketRunning && !token.IsCancellationRequested && ws != null && ws.State == WebSocketState.Open)
                {
                    DrainOutgoing(token);
                    outgoingSignal.Wait(100, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning("[Online] envio parou: " + ex.Message); }
        }
        static PendingAction? pending;

        struct SocketEvent
        {
            public string name;
            public string data;
        }

        struct PendingAction
        {
            public int kind;
            public string code;
            public string name;
        }

        public static void EnsurePump()
        {
            if (pump != null) return;
            var go = new GameObject("OnlineClientPump");
            UnityEngine.Object.DontDestroyOnLoad(go);
            pump = go.AddComponent<OnlinePump>();
        }

        static OnlinePump pump;
        static float connectStartedAt;
        static string lastUrl;
        static volatile bool connectFailed;

        static void SocketLoop(Uri uri, CancellationToken token)
        {
            try
            {
                ws = new ClientWebSocket();
                ws.ConnectAsync(uri, token).Wait(token);
                System.Threading.Tasks.Task.Run(() => SendLoop(token));
                var buffer = new byte[65536];
                var sb = new StringBuilder();
                while (socketRunning && !token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    DrainOutgoing(token);
                    sb.Clear();
                    var result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).Result;
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    while (!result.EndOfMessage)
                    {
                        result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).Result;
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    HandleFrame(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                if (socketRunning) Received.Enqueue(new SocketEvent { name = "__connect_error", data = ex.Message });
            }
            finally
            {
                socketIoConnected = false;
                if (!token.IsCancellationRequested && socketRunning) Received.Enqueue(new SocketEvent { name = "__disconnect", data = "" });
            }
        }

        static void DrainOutgoing(CancellationToken token)
        {
            while (socketRunning && ws != null && ws.State == WebSocketState.Open && Outgoing.TryDequeue(out string frame))
            {
                sendLock.Wait(token);
                try
                {
                    ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(frame)), WebSocketMessageType.Text, true, token).Wait(token);
                }
                finally
                {
                    sendLock.Release();
                }
            }
        }

        static void HandleFrame(string frame)
        {
            if (frame.Length == 0) return;
            switch (frame[0])
            {
            case '0':
                SendRaw("40");
                break;
            case '2':
                SendRaw("3");
                break;
            case '3':
                break;
            case '4':
                if (frame.Length < 2) return;
                switch (frame[1])
                {
                case '0':
                    socketIoConnected = true;
                    Received.Enqueue(new SocketEvent { name = "__connect", data = frame.Length > 2 ? frame.Substring(2) : "" });
                    break;
                case '1':
                    socketIoConnected = false;
                    Received.Enqueue(new SocketEvent { name = "__disconnect", data = "" });
                    break;
                case '2':
                    var ev = ParseEvent(frame.Substring(2));
                    if (ev.HasValue) Received.Enqueue(ev.Value);
                    break;
                case '4':
                    Received.Enqueue(new SocketEvent { name = "__connect_error", data = frame.Substring(2) });
                    break;
                }
                break;
            }
        }

        static void SendRaw(string payload)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            sendLock.Wait();
            try
            {
                ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            }
            finally
            {
                sendLock.Release();
            }
        }

        static SocketEvent? ParseEvent(string body)
        {
            if (body.Length < 2 || body[0] != '[') return null;
            int depth = 0;
            int comma = -1;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 1 && comma < 0) comma = i;
                if (depth == 0 && i > 0) break;
            }
            if (comma < 0)
            {
                string only = body.Substring(1, body.Length - 2).Trim().Trim('"');
                return new SocketEvent { name = only, data = "" };
            }
            string name = body.Substring(1, comma - 1).Trim().Trim('"');
            int end = body.Length - 1;
            if (end < comma + 1) return new SocketEvent { name = name, data = "" };
            string data = body.Substring(comma + 1, end - comma - 1).Trim();
            return new SocketEvent { name = name, data = data };
        }

        static void Emit(string name, string payload)
        {
            if (!socketIoConnected) return;
            Outgoing.Enqueue("42[\"" + name + "\"," + payload + "]");
            try { outgoingSignal.Release(); } catch (SemaphoreFullException) { }
        }

        static void DrainPending()
        {
            if (!socketIoConnected || !pending.HasValue) return;
            var p = pending.Value;
            pending = null;
            if (p.kind == 0)
                Emit("room:create", "{\"hostName\":" + JsonString(p.name) + "}");
            else if (p.kind == 2)
                Emit("room:rejoin", "{\"roomCode\":" + JsonString(p.code) + ",\"token\":" + JsonString(p.name) + "}");   // volta à corrida
            else
                Emit("room:join", "{\"roomCode\":" + JsonString(p.code) + ",\"playerName\":" + JsonString(p.name) + "}");
        }

        public static void DrainEvents()
        {
            if (retryAt > 0f && Time.realtimeSinceStartup >= retryAt)
            {
                retryAt = -1f;
                var keep = pending;
                ConnectAttempt(lastUrl, false);
                pending = keep; // a ação (criar/entrar) continua na fila até conectar
            }
            if (!Connected && !connectFailed && connectStartedAt > 0f && Time.realtimeSinceStartup - connectStartedAt > AttemptTimeout)
            {
                connectStartedAt = 0f;
                if (!ScheduleRetry("timeout"))
                {
                    connectFailed = true;
                    LastError = "timeout";
                    ConnectionStatus = "O servidor não respondeu em " + WakeWindow.ToString("0") + " s";
                    OnError?.Invoke("O servidor não respondeu. Confira a internet e tente de novo");
                    Log("Tempo esgotado ao conectar ao servidor (" + lastUrl + ")");
                }
            }
            DrainPending();
            while (Received.TryDequeue(out var ev))
            {
                switch (ev.name)
                {
                case "__connect":
                    Connected = true;
                    connectFailed = false;
                    connectStartedAt = 0f;
                    retryAt = -1f;
                    ConnectionStatus = "";
                    Log("Conectado ao servidor");
                    DrainPending();
                    break;
                case "__connect_error":
                    if (!Connected && ScheduleRetry(ev.data)) break;
                    connectFailed = true;
                    ConnectionStatus = "Falha de conexão";
                    connectStartedAt = 0f;
                    LastError = ev.data;
                    LastServerMessage = "Falha de conexão: " + ev.data;
                    Log("Falha de conexão: " + ev.data);
                    OnError?.Invoke("Falha de conexão: " + ev.data);
                    break;
                case "__disconnect":
                    Log("Desconectado do servidor");
                    // CAIU NO MEIO DA CORRIDA: tenta voltar sozinho (até 2 min) e retoma o PRÓPRIO carro na sala (room:rejoin
                    // com a senha). Enquanto isso o anfitrião pilota o carro no ritmo deste jogador; o jogo aqui segue normal.
                    if (Connected && StartInfo != null && !string.IsNullOrEmpty(RoomCode) && !string.IsNullOrEmpty(MyToken))
                    {
                        Connected = false;
                        rejoining = true;
                        pending = new PendingAction { kind = 2, code = RoomCode, name = MyToken };
                        connectWindowEnd = Time.realtimeSinceStartup + RejoinWindow;
                        ScheduleRetry("queda na corrida");
                        ConnectionStatus = "Conexão perdida — reconectando...";
                        OnError?.Invoke("CONEXÃO PERDIDA — reconectando...");
                        break;
                    }
                    if (!Connected)
                    {
                        if (retryAt > 0f || ScheduleRetry("desconectado")) break;
                        connectFailed = true;
                        connectStartedAt = 0f;
                        if (rejoining) { rejoining = false; OnError?.Invoke("Não foi possível voltar à sala"); break; }
                    }
                    Connected = false;
                    if (!string.IsNullOrEmpty(RoomCode)) OnError?.Invoke("Conexão perdida com o servidor");
                    break;
                case "room:created":
                    RoomCode = ExtractString(ev.data, "roomCode");
                    OnRoomCreated?.Invoke(RoomCode);
                    Log("Sala criada: " + RoomCode);
                    break;
                case "room:joined":
                {
                    bool wasHost = IsHost;
                    ParseRoomJoined(ev.data);
                    OnRoomJoined?.Invoke(RoomCode);
                    OnLobbyUpdate?.Invoke();
                    Log("Entrou na sala " + RoomCode + " como " + MyPlayerId + (IsHost ? " (anfitrião)" : ""));
                    if (rejoining && ExtractString(ev.data, "token").Length > 0)
                    {
                        rejoining = false;
                        OnRejoined?.Invoke();
                    }
                    if (StartInfo != null && wasHost != IsHost) OnHostChanged?.Invoke();   // voltei e a sala já tem outro anfitrião
                    break;
                }
                case "player:dropped":
                {
                    string id = ExtractString(ev.data, "playerId");
                    if (id.Length == 0 || id == MyPlayerId) break;
                    Dropped[id] = ExtractString(ev.data, "pace");
                    OnDropped?.Invoke(id);
                    break;
                }
                case "player:back":
                {
                    string id = ExtractString(ev.data, "playerId");
                    if (id.Length == 0 || id == MyPlayerId) break;
                    Dropped.Remove(id);
                    OnBack?.Invoke(id);
                    break;
                }
                case "room:warning":
                    LastServerMessage = ExtractString(ev.data, "message");
                    OnWarning?.Invoke(LastServerMessage);
                    break;
                case "room:host":
                {
                    bool wasHost = IsHost;
                    IsHost = ExtractString(ev.data, "hostId") == MyPlayerId;
                    OnLobbyUpdate?.Invoke();
                    if (wasHost != IsHost) OnHostChanged?.Invoke();   // na corrida: o novo anfitrião passa a pilotar a IA
                    break;
                }
                case "room:full":
                    LastError = "Sala cheia (máximo de 8 jogadores)";
                    OnError?.Invoke(LastError);
                    break;
                case "room:error":
                    LastError = ExtractString(ev.data, "message");
                    LastServerMessage = LastError;
                    Log("Erro da sala: " + LastError);
                    OnError?.Invoke(LastError);
                    break;
                case "game:start":
                    StartInfo = ParseGameStart(ev.data);
                    // Nova corrida (1ª, reinício ou nova partida): placar e espera da anterior não valem mais.
                    Rankings.Clear();
                    RemoteStates.Clear();
                    ClearRaceSync();
                    Round = StartInfo.round;
                    OnGameStart?.Invoke(StartInfo);
                    break;
                case "game:waiting":
                    WaitingFor.Clear();
                    WaitingFor.AddRange(ExtractStringArray(ev.data, "waiting"));
                    WaitingKnown = true;
                    OnWaiting?.Invoke();
                    break;
                case "game:go":
                    GoHoldSeconds = Mathf.Clamp(ExtractInt(ev.data, "holdMs"), 0, 5000) / 1000f;
                    GoAtRealtime = Time.realtimeSinceStartup + Mathf.Clamp(ExtractInt(ev.data, "inMs"), 0, 10000) / 1000f;
                    WaitingFor.Clear();
                    WaitingKnown = true;
                    OnGo?.Invoke();
                    break;
                case "vote:start":
                {
                    int secs = Mathf.Clamp(ExtractInt(ev.data, "seconds"), 3, 30);
                    Vote = new VoteInfo
                    {
                        id = ExtractString(ev.data, "voteId"),
                        kind = ExtractString(ev.data, "kind"),
                        byId = ExtractString(ev.data, "byId"),
                        byName = ExtractString(ev.data, "byName"),
                        deadline = Time.realtimeSinceStartup + secs,
                        total = Mathf.Max(1, LobbyPlayers.Count)
                    };
                    Vote.mine = Vote.byId == MyPlayerId;
                    OnVoteChanged?.Invoke();
                    break;
                }
                case "vote:update":
                    if (Vote != null && ExtractString(ev.data, "voteId") == Vote.id)
                    {
                        Vote.yes = ExtractInt(ev.data, "yes");
                        Vote.no = ExtractInt(ev.data, "no");
                        Vote.total = Mathf.Max(1, ExtractInt(ev.data, "total"));
                        OnVoteChanged?.Invoke();
                    }
                    break;
                case "vote:result":
                {
                    bool accepted = ExtractBool(ev.data, "accepted");
                    string kind = ExtractString(ev.data, "kind");
                    var no = ExtractStringArray(ev.data, "no");
                    Vote = null;
                    string text = accepted
                        ? (kind == "restart" ? "REINICIANDO A CORRIDA..." : "NOVA PARTIDA — CARREGANDO...")
                        : (kind == "restart" ? "REINÍCIO RECUSADO" : "NOVA PARTIDA RECUSADA") + (no.Count > 0 ? " — " + string.Join(", ", no) : "");
                    OnVoteChanged?.Invoke();
                    OnVoteResult?.Invoke(accepted, kind, text);
                    break;
                }
                case "room:kicked":
                    LastError = ExtractString(ev.data, "message");
                    OnKicked?.Invoke(LastError);
                    break;
                case "game:update":
                    ParseGameUpdate(ev.data);
                    OnStateUpdate?.Invoke();
                    break;
                case "game:finished":
                    ParseGameFinished(ev.data);
                    OnFinished?.Invoke();
                    break;
                case "player:disconnected":
                    if (LobbyPlayers.Count > 0) OnLobbyUpdate?.Invoke();
                    break;
                case "player:correction":
                    StateCorrection = ev.data;
                    break;
                }
            }
        }

        static void ParseRoomJoined(string json)
        {
            string code = ExtractString(json, "roomCode");
            string hostId = ExtractString(json, "hostId");
            string pid = ExtractString(json, "playerId");
            string tok = ExtractString(json, "token");
            if (code.Length > 0) RoomCode = code;
            if (pid.Length > 0) MyPlayerId = pid;
            if (tok.Length > 0) MyToken = tok;
            IsHost = hostId.Length > 0 && hostId == MyPlayerId;
            string roomTrack = ExtractString(json, "trackId"); // settings do lobby (vem antes dos players no payload)
            if (roomTrack.Length > 0) RoomTrackId = roomTrack;
            RoomLaps = Mathf.Clamp(ExtractInt(json, "laps"), 0, 200);
            RoomTotalCars = Mathf.Clamp(ExtractInt(json, "totalCars"), 2, 24);
            string roomDiff = ExtractString(json, "difficulty");
            if (roomDiff.Length > 0) RoomDifficulty = roomDiff;
            RoomCategory = ExtractString(json, "category") == "f1" ? "f1" : "gt";
            RoomPitStops = ExtractBool(json, "pitStops");
            RoomWeather = ExtractString(json, "weather");
            RoomTotalLoss = ExtractBoolOr(json, "totalLoss", true);
            RoomPenalties = ExtractBoolOr(json, "penalties", true);
            LobbyPlayers.Clear();
            string arr = ExtractArray(json, "players");
            if (arr != null)
            {
                foreach (string obj in SplitObjects(arr))
                {
                    LobbyPlayers.Add(new LobbyPlayerInfo
                    {
                        id = ExtractString(obj, "id"),
                        name = ExtractString(obj, "name"),
                        ready = ExtractBool(obj, "ready"),
                        carModel = ExtractString(obj, "carModel"),
                        carColor = ExtractInt(obj, "carColor"),
                        isHost = ExtractBool(obj, "isHost"),
                        connected = ExtractBool(obj, "connected")
                    });
                }
            }
        }

        static GameStartInfo ParseGameStart(string json)
        {
            var info = new GameStartInfo
            {
                trackId = ExtractString(json, "trackId"),
                laps = ExtractInt(json, "laps"),
                totalCars = ExtractInt(json, "totalCars"),
                difficulty = ExtractString(json, "difficulty"),
                category = ExtractString(json, "category") == "f1" ? "f1" : "gt",
                pitStops = ExtractBool(json, "pitStops"),
                weather = ExtractString(json, "weather"),
                totalLoss = ExtractBoolOr(json, "totalLoss", true),
                penalties = ExtractBoolOr(json, "penalties", true),
                startAt = ExtractLong(json, "startAt"),
                sync = ExtractBool(json, "sync"),
                round = ExtractInt(json, "round")
            };
            string arr = ExtractArray(json, "startPositions");
            if (arr != null)
            {
                foreach (string obj in SplitObjects(arr))
                {
                    info.startPositions.Add(new StartSlotInfo
                    {
                        id = ExtractString(obj, "id"),
                        slot = ExtractInt(obj, "slot"),
                        isAI = ExtractBool(obj, "isAI"),
                        name = ExtractString(obj, "name"),
                        carModel = ExtractString(obj, "carModel"),
                        carColor = ExtractInt(obj, "carColor")
                    });
                }
            }
            return info;
        }

        // "st" (estado: correndo/terminou/abandonou) vem logo depois de "lap" no servidor novo — opcional (servidor antigo não manda).
        static readonly Regex ArrayState = new Regex(
            "\"id\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"x\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"y\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"z\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"rotation\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"speed\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"lap\"\\s*:\\s*(\\d+)(?:\\s*,\\s*\"st\"\\s*:\\s*(\\d+))?",
            RegexOptions.Compiled);

        static readonly Regex DictState = new Regex(
            "\"([^\"]+)\"\\s*:\\s*\\{[^}]*?\"x\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)[^}]*?\"y\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)[^}]*?\"z\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)[^}]*?\"rotation\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)[^}]*?\"speed\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)[^}]*?\"lap\"\\s*:\\s*(\\d+)",
            RegexOptions.Compiled);

        static void ParseGameUpdate(string json)
        {
            var states = new Dictionary<string, RemoteState>();
            float now = Time.realtimeSinceStartup;
            foreach (Match m in ArrayState.Matches(json))
            {
                states[m.Groups[1].Value] = MakeState(m, 2, now);
            }
            if (states.Count == 0)
            {
                foreach (Match m in DictState.Matches(json))
                {
                    states[m.Groups[1].Value] = MakeState(m, 2, now);
                }
            }
            if (states.Count == 0) return;
            RemoteStates.Clear();
            foreach (var kv in states) RemoteStates[kv.Key] = kv.Value;
            RemoteStateTimestamp = now;
        }

        static RemoteState MakeState(Match m, int g, float now)
        {
            var inv = CultureInfo.InvariantCulture;
            var state = new RemoteState
            {
                position = new Vector3(
                    float.Parse(m.Groups[g].Value, inv),
                    float.Parse(m.Groups[g + 1].Value, inv),
                    float.Parse(m.Groups[g + 2].Value, inv)),
                rotation = float.Parse(m.Groups[g + 3].Value, inv),
                speed = float.Parse(m.Groups[g + 4].Value, inv),
                lap = int.Parse(m.Groups[g + 5].Value, inv),
                timestamp = now
            };
            if (m.Groups.Count > g + 6 && m.Groups[g + 6].Success) state.status = int.Parse(m.Groups[g + 6].Value, inv);
            return state;
        }

        static void ParseGameFinished(string json)
        {
            Rankings.Clear();
            string arr = ExtractArray(json, "rankings");
            if (arr == null) return;
            foreach (string obj in SplitObjects(arr))
            {
                Rankings.Add(new FinishedRanking
                {
                    position = ExtractInt(obj, "position"),
                    id = ExtractString(obj, "id"),
                    isAI = ExtractBool(obj, "isAI"),
                    name = ExtractString(obj, "name"),
                    finished = ExtractBool(obj, "finished"),
                    retired = ExtractBool(obj, "retired"),
                    totalTime = ExtractFloat(obj, "totalTime"),
                    bestLap = ExtractFloat(obj, "bestLap"),
                    penalty = ExtractFloat(obj, "penalty"),
                    lap = ExtractInt(obj, "lap")
                });
            }
        }

        /// <summary>["a","b"] → lista de textos (nomes da espera/votação).</summary>
        static List<string> ExtractStringArray(string json, string key)
        {
            var list = new List<string>();
            string arr = ExtractArray(json, key);
            if (arr == null) return list;
            foreach (Match m in Regex.Matches(arr, "\"((?:[^\"\\\\]|\\\\.)*)\"")) list.Add(Unescape(m.Groups[1].Value));
            return list;
        }

        static bool ExtractBoolOr(string json, string key, bool fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)");
            return m.Success ? m.Groups[1].Value == "true" : fallback;
        }

        static List<string> SplitObjects(string arr)
        {
            var list = new List<string>();
            int i = 0;
            while (i < arr.Length)
            {
                if (arr[i] != '{')
                {
                    i++;
                    continue;
                }
                int depth = 0;
                for (int j = i; j < arr.Length; j++)
                {
                    if (arr[j] == '{') depth++;
                    else if (arr[j] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            list.Add(arr.Substring(i, j - i + 1));
                            i = j + 1;
                            break;
                        }
                    }
                }
                if (depth != 0) break;
            }
            return list;
        }

        static string ExtractArray(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[");
            if (!m.Success) return null;
            int start = m.Index + m.Length - 1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        static string ExtractString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Unescape(m.Groups[1].Value) : "";
        }

        static int ExtractInt(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        static long ExtractLong(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0L;
        }

        static float ExtractFloat(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
        }

        static bool ExtractBool(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)");
            return m.Success && m.Groups[1].Value == "true";
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n == 'r' ? '\r' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string JsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    public sealed class OnlinePump : MonoBehaviour
    {
        void Update() => OnlineClient.DrainEvents();
    }

    public class OnlineGhost : MonoBehaviour
    {
        public string playerId;
        /// <summary>Corrida e entrada deste carro aqui (para o abandono feito no aparelho dono valer também aqui).</summary>
        public RaceManager race;
        public RaceEntry entry;

        CarController car;
        bool retiredHere;

        void Awake()
        {
            car = GetComponent<CarController>();
            if (car != null) car.IsRemote = true;
        }

        void LateUpdate()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay usa as posições gravadas
            if (playerId == null || car == null) return;
            var st = OnlineClient.GetRemote(playerId);
            if (st == null) { car.RemoteVelocity = Vector3.zero; return; }
            var rot = Quaternion.Euler(0f, st.rotation, 0f);
            // Velocidade REAL do carro remoto (direção × velocidade enviadas pelo dono): sem isso ele valia 0 km/h aqui.
            Vector3 vel = st.status == 0 ? rot * Vector3.forward * (Mathf.Max(0f, st.speed) / 3.6f) : Vector3.zero;
            car.RemoteVelocity = vel;
            if (st.status == 2 && !retiredHere && race != null && entry != null && !entry.finished)
            {
                retiredHere = true;
                race.Retire(entry, "ABANDONO");
            }
            float age = Time.realtimeSinceStartup - st.timestamp;
            if (age > 2f)
            {
                car.transform.SetPositionAndRotation(st.position, rot);
                return;
            }
            // O pacote chega ~0,1–0,25 s atrasado: projeta pela velocidade até 0,35 s (antes o carro remoto andava sempre
            // atrás de onde estava de verdade).
            Vector3 target = st.position + vel * Mathf.Min(age, 0.35f);
            float dt = Mathf.Max(0f, Time.deltaTime);
            float k = 1f - Mathf.Exp(-12f * dt);
            car.transform.position = Vector3.Lerp(car.transform.position, target, k);
            car.transform.rotation = Quaternion.Slerp(car.transform.rotation, rot, 1f - Mathf.Exp(-10f * dt));
        }
    }
}
