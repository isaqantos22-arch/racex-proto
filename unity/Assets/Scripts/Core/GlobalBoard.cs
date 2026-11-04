using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using RaceX.Car;
using UnityEngine;
using UnityEngine.Networking;

namespace RaceX.Core
{
    /// <summary>
    /// Aba GLOBAL (v99, pedido do usuário: "os players, os tempos recordes em cada pista, carro"): recordes de volta de todas
    /// as contas e ranking de pilotos, no mesmo Supabase das contas. Tabela public.lap_records (leitura pública; escrita só
    /// pela função submit_lap, que guarda o tempo apenas se for MELHOR) e função global_players (sem e-mail nem save) — SQL em
    /// server/supabase_global.sql. Só VOLTA LIMPA conta (mesmo critério do perfil de ritmo: sem sair da pista, bater, box ou
    /// volta de saída) e nunca volta do piloto automático/autoteste. Recordes de outra física não se misturam (Physics).
    /// </summary>
    public static class GlobalBoard
    {
        /// <summary>Versão da física: v99 = pneu µ 1,8 e apoio recalibrado. Tempo de antes não é comparável.</summary>
        public const int Physics = 99;

        [Serializable] public class Rec { public string name; public string car; public string car_name; public string category; public int lap_ms; public int compound; public bool wet; public string set_at; }
        [Serializable] public class PlayerRow { public string name; public string photo; public int avatar_color; public int wins; public int podiums; public int titles; public int races; public int records; }
        [Serializable] class RecList { public List<Rec> items; }
        [Serializable] class PlayerList { public List<PlayerRow> items; }

        /// <summary>Carro do recorde: na carreira da F2 todos têm o carro único (CarSpec.ApplyF2Spec) — vira "F2".</summary>
        static string CarKey(CarSpec s) => s == null ? "?" : s.displayName == CarSpec.F2Name ? "F2" : s.model.ToString();
        static string Category(CarSpec s) => s == null ? "" : s.displayName == CarSpec.F2Name ? "F2" : CarSpec.IsFormula(s.model) ? "F1" : "GT";
        static string PbKey(int track, string car) => "RaceX.GlobalPB" + Physics + "." + track + "." + car;
        static string SentKey(int track, string car) => "RaceX.GlobalSent" + Physics + "." + track + "." + car;

        /// <summary>Volta LIMPA do jogador (PlayerPaceRecorder): melhor pessoal nesta pista com este carro → envia (logado).
        /// Sem conta, guarda e envia quando houver login e outra volta limpa.</summary>
        public static void OnCleanLap(int track, CarSpec spec, float lapTime, int compound, bool wet)
        {
            if (AutoTest.Active || spec == null || lapTime < 10f || lapTime > 1800f) return;
            string car = CarKey(spec);
            float pb = PlayerPrefs.GetFloat(PbKey(track, car), 0f);
            if (pb <= 0f || lapTime < pb - 0.0005f)
            {
                pb = lapTime;
                PlayerPrefs.SetFloat(PbKey(track, car), pb);
                PlayerPrefs.SetInt(SentKey(track, car), 0);
                PlayerPrefs.Save();
            }
            if (!AccountClient.IsLoggedIn || PlayerPrefs.GetInt(SentKey(track, car), 0) == 1) return;
            string body = "{\"p_track\":" + track + ",\"p_car\":" + Json(car) + ",\"p_car_name\":" + Json(spec.displayName)
                          + ",\"p_category\":" + Json(Category(spec)) + ",\"p_lap_ms\":" + Mathf.RoundToInt(pb * 1000f)
                          + ",\"p_compound\":" + compound + ",\"p_wet\":" + (wet ? "true" : "false") + ",\"p_physics\":" + Physics + "}";
            int t = track; string c = car;
            AccountClient.Run(Send("/rpc/submit_lap", body, true, (ok, text) =>
            {
                if (ok) { PlayerPrefs.SetInt(SentKey(t, c), 1); PlayerPrefs.Save(); }
                else Debug.LogWarning("[GLOBAL] recorde não enviado: " + text);
            }));
        }

        /// <summary>Recordes da pista (melhor volta de cada conta com cada carro, física atual), do mais rápido.</summary>
        public static void FetchTrack(int track, Action<bool, List<Rec>, string> done)
        {
            string q = "/lap_records?track=eq." + track + "&physics=eq." + Physics
                       + "&select=name,car,car_name,category,lap_ms,compound,wet,set_at&order=lap_ms.asc&limit=40";
            AccountClient.Run(Get(q, (ok, text) =>
            {
                if (!ok) { done?.Invoke(false, null, Reason(text)); return; }
                RecList list = null;
                try { list = JsonUtility.FromJson<RecList>("{\"items\":" + text + "}"); } catch (Exception) { }
                done?.Invoke(list != null && list.items != null, list != null ? list.items : null, "Resposta inválida do servidor");
            }));
        }

        /// <summary>Ranking de pilotos (contas): vitórias, pódios, títulos, corridas e quantos recordes têm.</summary>
        public static void FetchPlayers(Action<bool, List<PlayerRow>, string> done)
        {
            AccountClient.Run(Send("/rpc/global_players", "{}", false, (ok, text) =>
            {
                if (!ok) { done?.Invoke(false, null, Reason(text)); return; }
                PlayerList list = null;
                try { list = JsonUtility.FromJson<PlayerList>("{\"items\":" + text + "}"); } catch (Exception) { }
                done?.Invoke(list != null && list.items != null, list != null ? list.items : null, "Resposta inválida do servidor");
            }));
        }

        static string Reason(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Sem conexão com o servidor";
            if (text.Contains("lap_records") || text.Contains("global_players") || text.Contains("PGRST")) return "Recordes globais ainda não ativados no servidor";
            return text.Length > 90 ? text.Substring(0, 90) : text;
        }

        static IEnumerator Get(string path, Action<bool, string> done)
        {
            var req = UnityWebRequest.Get(AccountClient.RestUrl + path);
            req.SetRequestHeader("apikey", AccountClient.PublicKey);
            req.timeout = 15;
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success;
            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            req.Dispose();
            done(ok, text);
        }

        static IEnumerator Send(string path, string body, bool auth, Action<bool, string> done)
        {
            var req = new UnityWebRequest(AccountClient.RestUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", AccountClient.PublicKey);
            req.SetRequestHeader("Content-Type", "application/json");
            if (auth && !string.IsNullOrEmpty(AccountClient.AccessToken)) req.SetRequestHeader("Authorization", "Bearer " + AccountClient.AccessToken);
            req.timeout = 15;
            yield return req.SendWebRequest();
            bool ok = req.result == UnityWebRequest.Result.Success;
            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            req.Dispose();
            done(ok, text);
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
