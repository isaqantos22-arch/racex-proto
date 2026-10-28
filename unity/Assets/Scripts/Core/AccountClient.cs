using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RaceX.Core
{
    /// <summary>Conta RaceX via Supabase: cadastro, login, sessão persistente e perfil sincronizado.</summary>
    public static class AccountClient
    {
        const string SupabaseUrl = "https://loyxhoowgonlgrlrykrd.supabase.co";
        const string AnonKey = "sb_publishable_zmE7e4kGKYXbR4yojs0fjg_BKuyT-EV";
        const string KeyAccess = "RaceX.Auth.Access";
        const string KeyRefresh = "RaceX.Auth.Refresh";
        const string KeyEmail = "RaceX.Auth.Email";
        const string KeyName = "RaceX.Auth.Name";
        const string KeyPhoto = "RaceX.Auth.Photo";
        const string KeyColor = "RaceX.Auth.Color";

        public static bool IsLoggedIn { get; private set; }
        public static string UserId { get; private set; }
        public static string Email { get; private set; }
        public static string Name { get; private set; }
        public static string Photo { get; private set; }
        public static int AvatarColor { get; private set; }
        public static int Wins { get; private set; }
        public static int Podiums { get; private set; }
        public static int Titles { get; private set; }
        public static int Races { get; private set; }

        static string accessToken;
        static string refreshToken;
        static bool busy;

        public static void RestoreSession()
        {
            accessToken = PlayerPrefs.GetString(KeyAccess, "");
            refreshToken = PlayerPrefs.GetString(KeyRefresh, "");
            Email = PlayerPrefs.GetString(KeyEmail, "");
            Name = PlayerPrefs.GetString(KeyName, "");
            Photo = PlayerPrefs.GetString(KeyPhoto, "");
            AvatarColor = PlayerPrefs.GetInt(KeyColor, 0);
            IsLoggedIn = !string.IsNullOrEmpty(accessToken);
            if (IsLoggedIn) Runner.Run(RefreshSession(ok =>
            {
                if (!ok) Logout();
            }));
        }

        public static void Logout()
        {
            IsLoggedIn = false;
            UserId = accessToken = refreshToken = null;
            Email = Name = Photo = null;
            PlayerPrefs.DeleteKey(KeyAccess);
            PlayerPrefs.DeleteKey(KeyRefresh);
            PlayerPrefs.DeleteKey(KeyEmail);
            PlayerPrefs.DeleteKey(KeyName);
            PlayerPrefs.DeleteKey(KeyPhoto);
            PlayerPrefs.DeleteKey(KeyColor);
            PlayerPrefs.Save();
        }

        public static void SignUp(string email, string password, string name, Action<bool, string> done)
        {
            var body = "{\"email\":" + Json(email) + ",\"password\":" + Json(password) +
                       ",\"data\":{\"name\":" + Json(name) + "}}";
            Post("/auth/v1/signup", body, done);
        }

        public static void LogIn(string email, string password, Action<bool, string> done)
        {
            var body = "{\"email\":" + Json(email) + ",\"password\":" + Json(password) + "}";
            Post("/auth/v1/token?grant_type=password", body, done);
        }

        [Serializable]
        class CloudBundle
        {
            public long updatedTicks;
            public int credits;
            public int[] upgrades;
            public string careerJson;
            public string playerPace;
        }

        static string BuildCloudCareerField()
        {
            var bundle = new CloudBundle
            {
                updatedTicks = CareerStats.LastUpdatedTicks > 0 ? CareerStats.LastUpdatedTicks : DateTime.UtcNow.Ticks,
                credits = CareerStats.Credits,
                upgrades = CareerStats.ExportUpgradesArray(),
                careerJson = RaceX.Career.CareerMode.ExportRawJson(),
                playerPace = RaceX.Car.PlayerPaceLearner.ExportAll()
            };
            string raw = JsonUtility.ToJson(bundle);
            return "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        /// <summary>Salva perfil (nome/foto/cor), estatísticas, créditos, melhorias dos carros e carreira na nuvem.</summary>
        public static void SaveProfile(Action<bool, string> done)
        {
            if (!IsLoggedIn || string.IsNullOrEmpty(UserId))
            {
                if (done != null) done(false, "Não autenticado");
                return;
            }
            Wins = Mathf.Max(Wins, CareerStats.Wins);
            Podiums = Mathf.Max(Podiums, CareerStats.Podiums);
            Titles = Mathf.Max(Titles, CareerStats.Titles);
            Races = Mathf.Max(Races, CareerStats.Races);
            string careerField = BuildCloudCareerField();
            var body = "{\"name\":" + Json(Name) + ",\"photo\":" + Json(Photo) +
                       ",\"avatar_color\":" + AvatarColor +
                       ",\"email\":" + Json(Email) +
                       ",\"career\":" + Json(careerField) +
                       ",\"wins\":" + Wins + ",\"podiums\":" + Podiums +
                       ",\"titles\":" + Titles + ",\"races\":" + Races + "}";
            Runner.Run(PatchProfile(body, done));
        }

        /// <summary>Sincroniza imediatamente todo o estado da conta (carreira + créditos + upgrades + stats) na nuvem.</summary>
        public static void PushFullState()
        {
            if (IsLoggedIn && !string.IsNullOrEmpty(UserId))
                SaveProfile(null);
        }

        /// <summary>Edita perfil localmente e empurra para a nuvem.</summary>
        public static void SetProfile(string name, string photo, int color, Action<bool, string> done)
        {
            if (IsLoggedIn)
            {
                if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
                if (photo != null) Photo = photo;
                AvatarColor = color & 7;
                PersistProfile();
                SaveProfile(done);
            }
            else if (done != null) done(false, "Não autenticado");
        }

        /// <summary>Aplica as estatísticas da corrida terminada e sincroniza na nuvem.</summary>
        public static void PushRaceStats(int wins, int podiums, int titles, int races)
        {
            Wins = wins; Podiums = podiums; Titles = titles; Races = races;
            if (IsLoggedIn) SaveProfile(null);
        }

        public static IEnumerator RefreshSession(Action<bool> done)
        {
            if (!string.IsNullOrEmpty(accessToken))
            {
                var req = UnityWebRequest.Get(SupabaseUrl + "/auth/v1/user");
                req.SetRequestHeader("apikey", AnonKey);
                req.SetRequestHeader("Authorization", "Bearer " + accessToken);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var user = JsonUser(req.downloadHandler.text);
                    ApplyUser(user);
                    req.Dispose();
                    yield return LoadProfileCoroutine(null);
                    if (done != null) done(true);
                    yield break;
                }
                req.Dispose();
            }
            if (string.IsNullOrEmpty(refreshToken))
            {
                if (done != null) done(false);
                yield break;
            }
            var body = "{\"refresh_token\":" + Json(refreshToken) + "}";
            yield return PostCoroutine("/auth/v1/token?grant_type=refresh_token", body, (ok, msg) =>
            {
                if (done != null) done(ok);
            });
        }

        static IEnumerator PatchProfile(string body, Action<bool, string> done)
        {
            var req = new UnityWebRequest(SupabaseUrl + "/rest/v1/profiles?id=eq." + UserId, "PATCH");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", AnonKey);
            req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Prefer", "return=minimal");
            yield return req.SendWebRequest();
            var ok = req.result == UnityWebRequest.Result.Success;
            var err = ok ? "" : req.error;
            req.Dispose();
            if (done != null) done(ok, err);
        }

        static void Post(string path, string body, Action<bool, string> done)
        {
            if (busy)
            {
                if (done != null) done(false, "Aguarde a operação anterior");
                return;
            }
            busy = true;
            Runner.Run(PostCoroutine(path, body, (ok, msg) => { busy = false; if (done != null) done(ok, msg); }));
        }

        static IEnumerator PostCoroutine(string path, string body, Action<bool, string> done)
        {
            var req = new UnityWebRequest(SupabaseUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", AnonKey);
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            var text = req.downloadHandler != null ? req.downloadHandler.text : "";
            var ok = req.result == UnityWebRequest.Result.Success;
            req.Dispose();
            if (!ok)
            {
                string reason = "Falha de conexão";
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Contains("email_not_confirmed")) reason = "Confirme seu e-mail (link enviado)";
                    else if (text.Contains("Invalid login credentials")) reason = "E-mail ou senha incorretos";
                    else if (text.Contains("already registered")) reason = "E-mail já cadastrado";
                    else if (text.Contains("Password")) reason = "Senha muito curta (mínimo 6)";
                    else if (text.Contains("unable to validate email")) reason = "E-mail inválido";
                    else reason = text.Length > 120 ? text.Substring(0, 120) : text;
                }
                if (done != null) done(false, reason);
                yield break;
            }
            var user = JsonUser(text);
            accessToken = user.access;
            refreshToken = user.refresh;
            if (string.IsNullOrEmpty(user.id))
            {
                if (done != null) done(false, "Resposta inesperada do servidor");
                yield break;
            }
            if (string.IsNullOrEmpty(accessToken))
            {
                if (done != null) done(false, "Conta criada! Confirme o e-mail (link enviado) e faça login");
                yield break;
            }
            if (accessToken != null) PlayerPrefs.SetString(KeyAccess, accessToken);
            if (refreshToken != null) PlayerPrefs.SetString(KeyRefresh, refreshToken);
            PlayerPrefs.Save();
            ApplyUser(user);
            IsLoggedIn = true;
            yield return Runner.Run(LoadProfileCoroutine(ok2 =>
            {
                if (done != null) done(true, "");
            }));
        }

        static IEnumerator LoadProfileCoroutine(Action<bool> done)
        {
            var req = UnityWebRequest.Get(SupabaseUrl + "/rest/v1/profiles?id=eq." + UserId + "&select=*");
            req.SetRequestHeader("apikey", AnonKey);
            req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var json = req.downloadHandler.text;
                var name = JsonStringField(json, "name");
                if (!string.IsNullOrEmpty(name)) Name = name;
                var photo = JsonStringField(json, "photo");
                Photo = photo ?? "";
                var color = JsonIntField(json, "avatar_color");
                if (color >= 0) AvatarColor = color;
                var w = JsonIntField(json, "wins");
                if (w >= 0) Wins = Mathf.Max(w, CareerStats.Wins);
                var p = JsonIntField(json, "podiums");
                if (p >= 0) Podiums = Mathf.Max(p, CareerStats.Podiums);
                var t = JsonIntField(json, "titles");
                if (t >= 0) Titles = Mathf.Max(t, CareerStats.Titles);
                var r = JsonIntField(json, "races");
                if (r >= 0) Races = Mathf.Max(r, CareerStats.Races);
                string careerField = JsonStringField(json, "career");
                bool pushedBack = false;
                if (!string.IsNullOrEmpty(careerField) && careerField.StartsWith("B64:", StringComparison.Ordinal))
                {
                    try
                    {
                        string raw = Encoding.UTF8.GetString(Convert.FromBase64String(careerField.Substring(4)));
                        var bundle = JsonUtility.FromJson<CloudBundle>(raw);
                        if (bundle != null)
                        {
                            RaceX.Car.PlayerPaceLearner.ImportMerge(bundle.playerPace);
                            long localTicks = CareerStats.LastUpdatedTicks;
                            bool cloudNewer = bundle.updatedTicks >= localTicks || !RaceX.Career.CareerMode.HasSave;
                            int creditsToApply = cloudNewer ? bundle.credits : CareerStats.Credits;
                            CareerStats.SyncFromCloud(creditsToApply, Wins, Podiums, Titles, Races, bundle.upgrades, bundle.updatedTicks);
                            if (!string.IsNullOrEmpty(bundle.careerJson) && (cloudNewer || !RaceX.Career.CareerMode.HasSave) && !RaceX.Career.CareerMode.LocalCareerAheadOf(bundle.careerJson))
                            {
                                RaceX.Career.CareerMode.ImportRawJson(bundle.careerJson);
                            }
                            else if (localTicks > bundle.updatedTicks || (!string.IsNullOrEmpty(bundle.careerJson) && RaceX.Career.CareerMode.LocalCareerAheadOf(bundle.careerJson)))
                            {
                                pushedBack = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Conta] falha ao ler dados da nuvem: " + ex.Message);
                    }
                }
                else
                {
                    CareerStats.SyncFromCloud(-1, Wins, Podiums, Titles, Races, null, 0L);
                    if (RaceX.Career.CareerMode.HasSave || CareerStats.TotalUpgradePoints() > 0 || CareerStats.Races > 0)
                        pushedBack = true;
                }
                PersistProfile();
                if (pushedBack) SaveProfile(null);
            }
            req.Dispose();
            if (done != null) done(true);
        }

        static void ApplyUser(AuthUser user)
        {
            if (user.id != null) UserId = user.id;
            if (user.email != null) Email = user.email;
            if (user.name != null && user.name.Length > 0) Name = user.name;
            if (user.access != null) accessToken = user.access;
            if (user.refresh != null) refreshToken = user.refresh;
            IsLoggedIn = true;
            PersistProfile();
        }

        static void PersistProfile()
        {
            if (Email != null) PlayerPrefs.SetString(KeyEmail, Email);
            if (Name != null) PlayerPrefs.SetString(KeyName, Name);
            if (Photo != null) PlayerPrefs.SetString(KeyPhoto, Photo);
            PlayerPrefs.SetInt(KeyColor, AvatarColor);
            PlayerPrefs.Save();
        }

        class AuthUser
        {
            public string id;
            public string email;
            public string name;
            public string access;
            public string refresh;
        }

        static AuthUser JsonUser(string json)
        {
            var u = new AuthUser();
            if (string.IsNullOrEmpty(json)) return u;
            u.id = JsonStringField(json, "id");
            u.email = JsonStringField(json, "email");
            u.access = JsonStringField(json, "access_token");
            u.refresh = JsonStringField(json, "refresh_token");
            var meta = JsonStringField(json, "name");
            if (meta == null)
            {
                int di = json.IndexOf("user_metadata");
                if (di >= 0)
                {
                    var sub = json.Substring(di);
                    u.name = JsonStringField(sub, "name");
                }
            }
            else u.name = meta;
            return u;
        }

        static string JsonStringField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var needle = "\"" + field + "\":";
            var i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '"') return null;
            var sb = new System.Text.StringBuilder();
            i++;
            for (; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    sb.Append(json[i] == 'n' ? '\n' : json[i]);
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static int JsonIntField(string json, string field)
        {
            var needle = "\"" + field + "\":";
            var i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return -1;
            i += needle.Length;
            var sb = new System.Text.StringBuilder();
            for (; i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'); i++) sb.Append(json[i]);
            int v;
            return int.TryParse(sb.ToString(), out v) ? v : -1;
        }

        static string Json(string s)
        {
            if (s == null) return "\"\"";
            var sb = new System.Text.StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        static AccountRunner runner;
        static AccountRunner Runner
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("AccountClientRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    runner = go.AddComponent<AccountRunner>();
                }
                return runner;
            }
        }

        public static Coroutine Run(IEnumerator routine) { return Runner.Run(routine); }

        sealed class AccountRunner : MonoBehaviour
        {
            public Coroutine Run(IEnumerator routine) { return StartCoroutine(routine); }
            void Awake() { DontDestroyOnLoad(gameObject); }
        }
    }
}
