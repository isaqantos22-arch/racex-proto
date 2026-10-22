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
        public long startAt;
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
        public float totalTime;
        public float bestLap;
        public int lap;
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
        public static Action OnLobbyUpdate;
        public static Action<GameStartInfo> OnGameStart;
        public static Action OnStateUpdate;
        public static Action OnFinished;

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

        static void Log(string msg) => Debug.Log("[Online] " + msg);

        public static CarModel CarModelFromServer(string name) => name switch
        {
            "falcon" => CarModel.Falcon,
            "storm" => CarModel.Storm,
            "f1" => CarModel.F1,
            _ => CarModel.Titan
        };

        public static string CarModelToServer(CarModel model) => model switch
        {
            CarModel.Falcon => "falcon",
            CarModel.Storm => "storm",
            CarModel.F1 => "storm",
            _ => "titan"
        };

        public static RemoteState GetRemote(string id) => id != null && RemoteStates.TryGetValue(id, out var s) ? s : null;

        public static void Connect(string serverUrl)
        {
            EnsurePump();
            StopSocket();
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

        public static void Disconnect()
        {
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
            pending = new PendingAction { kind = 0, name = hostName };
            DrainPending();
        }

        public static void JoinRoom(string code, string name)
        {
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

        public static void SendPosition(float x, float y, float z, float rot, float speed, int lap, int checkpoint)
        {
            var f = CultureInfo.InvariantCulture;
            Emit("player:position",
                "{\"x\":" + x.ToString(f) + ",\"y\":" + y.ToString(f) + ",\"z\":" + z.ToString(f) +
                ",\"rotation\":" + rot.ToString(f) + ",\"speed\":" + speed.ToString(f) +
                ",\"lap\":" + lap + ",\"checkpoint\":" + checkpoint + "}");
        }

        public static void SendFinished(float totalTime = 0f, float bestLap = 0f)
        {
            var f = CultureInfo.InvariantCulture;
            Emit("player:finished", "{\"totalTime\":" + totalTime.ToString(f) + ",\"bestLap\":" + bestLap.ToString(f) + "}");
        }

        static readonly ConcurrentQueue<SocketEvent> Received = new ConcurrentQueue<SocketEvent>();
        static readonly ConcurrentQueue<string> Outgoing = new ConcurrentQueue<string>();
        static volatile bool socketRunning;
        static volatile bool socketIoConnected;
        static ClientWebSocket ws;
        static CancellationTokenSource cts;
        static Thread socketThread;
        static SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
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
        }

        static void DrainPending()
        {
            if (!socketIoConnected || !pending.HasValue) return;
            var p = pending.Value;
            pending = null;
            if (p.kind == 0)
                Emit("room:create", "{\"hostName\":" + JsonString(p.name) + "}");
            else
                Emit("room:join", "{\"roomCode\":" + JsonString(p.code) + ",\"playerName\":" + JsonString(p.name) + "}");
        }

        public static void DrainEvents()
        {
            if (!Connected && !connectFailed && connectStartedAt > 0f && Time.realtimeSinceStartup - connectStartedAt > 10f)
            {
                connectStartedAt = 0f;
                connectFailed = true;
                LastError = "timeout";
                OnError?.Invoke("Tempo esgotado ao conectar ao servidor (" + lastUrl + ")");
                Log("Tempo esgotado ao conectar ao servidor (" + lastUrl + ")");
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
                    Log("Conectado ao servidor");
                    DrainPending();
                    break;
                case "__connect_error":
                    connectFailed = true;
                    connectStartedAt = 0f;
                    LastError = ev.data;
                    LastServerMessage = "Falha de conexão: " + ev.data;
                    Log("Falha de conexão: " + ev.data);
                    OnError?.Invoke("Falha de conexão: " + ev.data);
                    break;
                case "__disconnect":
                    Log("Desconectado do servidor");
                    if (!Connected)
                    {
                        connectFailed = true;
                        connectStartedAt = 0f;
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
                    ParseRoomJoined(ev.data);
                    OnRoomJoined?.Invoke(RoomCode);
                    OnLobbyUpdate?.Invoke();
                    Log("Entrou na sala " + RoomCode + " como " + MyPlayerId + (IsHost ? " (anfitrião)" : ""));
                    break;
                case "room:error":
                    LastError = ExtractString(ev.data, "message");
                    LastServerMessage = LastError;
                    Log("Erro da sala: " + LastError);
                    OnError?.Invoke(LastError);
                    break;
                case "game:start":
                    StartInfo = ParseGameStart(ev.data);
                    OnGameStart?.Invoke(StartInfo);
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
            if (code.Length > 0) RoomCode = code;
            if (pid.Length == 0) Debug.LogWarning("[Online] room:joined sem playerId");
            if (pid.Length > 0) MyPlayerId = pid;
            IsHost = hostId.Length > 0 && hostId == MyPlayerId;
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
                startAt = ExtractLong(json, "startAt")
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

        static readonly Regex ArrayState = new Regex(
            "\"id\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"x\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"y\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"z\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"rotation\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"speed\"\\s*:\\s*(-?[\\d.]+(?:[eE][+-]?\\d+)?)\\s*,\\s*\"lap\"\\s*:\\s*(\\d+)",
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
                    totalTime = ExtractFloat(obj, "totalTime"),
                    bestLap = ExtractFloat(obj, "bestLap"),
                    lap = ExtractInt(obj, "lap")
                });
            }
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

        CarController car;

        void Awake()
        {
            car = GetComponent<CarController>();
        }

        void LateUpdate()
        {
            if (playerId == null || car == null) return;
            var st = OnlineClient.GetRemote(playerId);
            if (st == null) return;
            float age = Time.realtimeSinceStartup - st.timestamp;
            if (age > 2f)
            {
                car.transform.SetPositionAndRotation(st.position, Quaternion.Euler(0f, st.rotation, 0f));
                return;
            }
            float dt = Mathf.Max(0f, Time.deltaTime);
            float k = 1f - Mathf.Exp(-12f * dt);
            car.transform.position = Vector3.Lerp(car.transform.position, st.position, k);
            car.transform.rotation = Quaternion.Slerp(car.transform.rotation, Quaternion.Euler(0f, st.rotation, 0f), 1f - Mathf.Exp(-10f * dt));
        }
    }
}
