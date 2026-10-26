using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace RaceX.EditorTools
{
    /// <summary>ARNÊS DE TESTE EM LOCALHOST (Editor batchmode, SEM play mode, SEM cena):
    /// valida a CADEIA DE FASES da carreira em lógica pura — o que provava o bug "Q2 nunca
    /// acaba" (deadline vencido + ninguém checando). Roda em segundos.
    /// Uso: Unity -batchmode -nographics -projectPath ... -executeMethod RaceX.EditorTools.RaceXLogicTests.RunPhaseChainTest -quit
    /// Resultado: racex_logic_test.json na raiz do projeto. Save real do usuário: backup + restauração.</summary>
    public static class RaceXLogicTests
    {
        static string saveBackup;
        static bool hadSave;
        static string savePath;
        static int exitCode = 1;
        static string result = "FALHOU";
        static string detail = "";

        public static void RunPhaseChainTest()
        {
            savePath = Path.Combine(Application.persistentDataPath, "racex_career.json");
            hadSave = File.Exists(savePath);
            if (hadSave) saveBackup = File.ReadAllText(savePath);
            Debug.Log("[LOGICTEST] save real: " + (hadSave ? "BACKUP FEITO" : "não existia"));

            try
            {
                // ===== CENÁRIO 1: jogador VIVO na corrente, Q2 com deadline VENCIDO =====
                var d = NewTestSave(Career.CareerMode.PH_Q2, withPlayerInChain: true);
                d.sessionDeadlineUtc = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond; // venceu
                Career.CareerMode.data = d;
                int before = d.weekendPhase;
                // O mesmo gatilho que o RaceManager usa ao perceber o prazo vencido:
                Career.CareerMode.NotifySessionExpired();
                int after = Career.CareerMode.data.weekendPhase;
                Check(after == Career.CareerMode.PH_Q3,
                    $"C1 Q2->Q3 por relógio real (fase {before} -> {after})", after == Career.CareerMode.PH_Q3);
                Check(!Career.CareerData.raceActive, "C1 raceActive=false após encerrar", !Career.CareerData.raceActive);
                Check(Career.CareerMode.data.qLocked.Count > 0 || Career.CareerMode.data.qChain.Count > 0,
                    "C1 cadeia Q2 preservada (locked=" + Career.CareerMode.data.qLocked.Count + ")", true);

                // ===== CENÁRIO 2: jogador ELIMINADO na Q1 (não está na corrente) — Q2 vencida pula para Q3 =====
                var d2 = NewTestSave(Career.CareerMode.PH_Q2, withPlayerInChain: false);
                d2.sessionDeadlineUtc = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond;
                Career.CareerMode.data = d2;
                Career.CareerMode.NotifySessionExpired();
                Check(Career.CareerMode.data.weekendPhase == Career.CareerMode.PH_Q3,
                    "C2 eliminado: Q2 vencida avança direto (simulada) -> fase=" + Career.CareerMode.data.weekendPhase,
                    Career.CareerMode.data.weekendPhase == Career.CareerMode.PH_Q3);

                // ===== CENÁRIO 3: Q3 vencida avança para CORRIDA =====
                var d3 = NewTestSave(Career.CareerMode.PH_Q3, withPlayerInChain: true);
                d3.sessionDeadlineUtc = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond;
                Career.CareerMode.data = d3;
                Career.CareerMode.NotifySessionExpired();
                Check(Career.CareerMode.data.weekendPhase == Career.CareerMode.PH_RACE,
                    "C3 Q3 vencida -> CORRIDA (fase=" + Career.CareerMode.data.weekendPhase + ")",
                    Career.CareerMode.data.weekendPhase == Career.CareerMode.PH_RACE);

                // ===== CENÁRIO 4: PULAR com voltas registradas usa a ordem REAL =====
                // (SkipSession com RaceManager VIVO usa rm.FinalStandings() como ordem; aqui
                // validamos o motor de corte ApplyElimination com essa ordem real, sem cena.)
                var d4 = NewTestSave(Career.CareerMode.PH_Q2, withPlayerInChain: true);
                Career.CareerMode.data = d4;
                var ordemReal = new List<string>(d4.qChain);          // jogador 7º das 15 (índice 6)
                ordemReal.Remove(d4.pilotName);
                ordemReal.Insert(6, d4.pilotName);
                Career.CareerMode.data.qChain.Clear();
                Career.CareerMode.data.qChain.AddRange(ordemReal);
                var refletor = typeof(Career.CareerMode);
                var metodo = refletor.GetMethod("ApplyElimination", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                metodo.Invoke(null, new object[] { Career.CareerMode.data.qChain, Career.CareerMode.data.qLocked, ordemReal, 6 });
                int posNoLocked = Career.CareerMode.data.qLocked.IndexOf(d4.pilotName);
                int posNaChain = Career.CareerMode.data.qChain.IndexOf(d4.pilotName);
                Check(posNaChain == 6 && posNoLocked < 0,
                    $"C4 ordem REAL respeitada no corte (jogador 7º sobrevive na chain, pos={posNaChain}; locked sem ele: {posNoLocked})",
                    posNaChain == 6 && posNoLocked < 0);
            }
            catch (Exception ex)
            {
                detail = "exceção: " + ex.Message + " @ " + ex.StackTrace;
                Debug.LogWarning("[LOGICTEST] " + detail);
            }
            finally
            {
                // RESTAURA o save real do usuário (o teste poluiu os dados).
                try
                {
                    if (hadSave) File.WriteAllText(savePath, saveBackup);
                    else if (File.Exists(savePath)) File.Delete(savePath);
                    Debug.Log("[LOGICTEST] save real restaurado.");
                }
                catch (Exception ex) { Debug.LogWarning("[LOGICTEST] falha restaurando save: " + ex.Message); }

                var json = "{\"resultado\":\"" + result + "\",\"detalhe\":\"" + detail.Replace("\"", "'").Replace("\n", " ") + "\"}";
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "racex_logic_test.json"), json);
                Debug.Log("[LOGICTEST] RESULTADO: " + result + " — " + detail);
                Career.CareerData.raceActive = false;
                EditorApplication.Exit(exitCode);
            }
        }

        static Career.CareerData NewTestSave(int phase, bool withPlayerInChain)
        {
            var d = new Career.CareerData
            {
                pilotName = "TESTE_FASE",
                category = "F2",
                season = 1,
                round = 0,
                difficulty = 1,
                weekendPhase = phase
            };
            d.qChain.Clear();
            d.qLocked.Clear();
            if (withPlayerInChain)
            {
                d.qChain.Add(d.pilotName);
                int add = 0;
                foreach (var n in Career.CareerMode.RivalNamesAccess)
                    if (n != d.pilotName && add++ < 14) d.qChain.Add(n);
            }
            Career.CareerData.raceActive = false;
            return d;
        }

        static void Check(bool ok, string msg, bool cond)
        {
            detail += (detail == "" ? "" : " | ") + (ok ? "OK " : "XX ") + msg;
            if (!ok) { result = "FALHOU"; exitCode = 2; }
            else if (result != "PASSOU") { result = "PASSOU"; exitCode = 0; }
        }
    }
}
