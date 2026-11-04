using System;
using System.Collections;
using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    public enum RaceState { Setup, Countdown, Racing, Finished }

    public class RaceEntry
    {
        public CarController car;
        public string name;
        public bool isHuman;
        public int playerIndex = -1;
        public int gridSlot;
        /// <summary>Box da garagem no pit (fim de semana da carreira: fixo por equipe, -1 = usa gridSlot).</summary>
        public int pitBox = -1;
        public CarModel model;
        public int colorIndex;
        public readonly List<float> lapTimes = new List<float>();
        public float lapStartTime;
        public float bestLap;
        public bool outLap;
        public bool finished;
        public float totalTime;
        public int position;
        public float raceDistance;
        /// <summary>Fora da corrida: perda total ("PT") ou desclassificado ("DSQ", bandeira preta).</summary>
        public bool retired;
        public string retireReason = "";
        /// <summary>Penalidades de tempo (s) somadas ao tempo final — regras da F1 (RaceControl).</summary>
        public float penaltySeconds;
        public int penaltyCount;
        public int trackLimitStrikes;
        /// <summary>Online: id do carro no servidor (jogador "p_..." ou IA "ai_N") — liga o placar oficial ao carro.</summary>
        public string netId;
        public int CurrentLap(int laps, RaceProgress p) => Mathf.Clamp(p != null ? p.lap : 1, 1, laps);
    }

    public class RaceManager : MonoBehaviour
    {
        public const float CountdownStep = 1f;
        public const float FinishScreenDelay = 3f;

        public TrackBuilder Track { get; private set; }
        public RaceSettings Settings { get; private set; }
        public RaceState State { get; private set; } = RaceState.Setup;
        public int CountdownValue { get; private set; }
        public float RaceTime => State == RaceState.Racing || State == RaceState.Finished ? Time.time - raceStartTime : 0f;

        /// <summary>Limite REAL de tempo da sessão (0 = sem limite/por distância). Expira encerra a sessão com a ordem atual.</summary>
        public float SessionTimeLeft
        {
            get
            {
                if (sessionTimeLimit <= 0f || State != RaceState.Racing) return -1f;
                return Mathf.Max(0f, sessionTimeLimit - (Time.time - sessionStart));
            }
        }
        float sessionTimeLimit;
        float sessionStart;
        bool timeExpired;
        int lapAtExpiry;
        float flagAt;
        bool flyingAtFlag;
        /// <summary>Tempo da sessão acabou e a quadriculada está fora: só termina quem já estava em volta lançada.</summary>
        public bool ChequeredFlag => timeExpired && State == RaceState.Racing;

        /// <summary>O jogador está numa volta LANÇADA (fora do box e da volta de saída)?</summary>
        bool HumanOnFlyingLap()
        {
            foreach (var e in entries)
            {
                if (!e.isHuman || e.finished || e.retired || e.car == null) continue;
                if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car) || e.car.pitControlled || e.outLap) continue;
                return true;
            }
            return false;
        }

        /// <summary>Volta atual do humano (0 se não há).</summary>
        int HumanLapNow()
        {
            foreach (var e in entries)
            {
                if (!e.isHuman) continue;
                var p = GetProgress(e);
                return p?.lap ?? 0;
            }
            return 0;
        }

        /// <summary>Volta atual de um carro qualquer (IA/treino).</summary>
        public int LapOf(CarController c)
        {
            foreach (var e in entries)
            {
                if (e.car != c) continue;
                var p = GetProgress(e);
                return p?.lap ?? 0;
            }
            return 0;
        }

        bool HumanFinished()
        {
            foreach (var e in entries)
                if (e.isHuman && e.finished) return true;
            return false;
        }
        public readonly List<RaceEntry> entries = new List<RaceEntry>();
        public readonly List<RaceEntry> finishOrder = new List<RaceEntry>();

        public event Action<int> CountdownTick;
        public event Action RaceStarted;
        public event Action<RaceEntry, float, bool> LapCompleted;
        public event Action<RaceEntry> EntryFinished;
        public event Action<RaceEntry> EntryRetired;
        public event Action RaceFinished;

        float raceStartTime;
        readonly Dictionary<CarController, RaceEntry> byCar = new Dictionary<CarController, RaceEntry>();

        public void Initialize(TrackBuilder track, RaceSettings settings)
        {
            Track = track;
            Settings = settings;
            track.Checkpoints.LapCompleted += OnLapCompleted;
        }

        public RaceEntry SpawnCar(CarSpec spec, int colorIndex, string driverName, int slot, bool human, int playerIndex, Pose? customPose = null)
        {
            Pose pose = customPose ?? Track.GetGridPose(slot);
            // v100: nível de apoio do F1 2026 pela PISTA (asa de Monza ≠ asa de Mônaco) — todos os carros da corrida, todos os
            // modos. O F2 (carro único) fica com o apoio dele: não há dado público de asa por pista na F2.
            if (spec != null && CarSpec.IsFormula(spec.model) && spec.displayName != CarSpec.F2Name && GameManager.Settings != null)
                spec.downforceCoefficient *= RaceX.Track.TrackLibrary.F1AeroLevel(GameManager.Settings.track);
            var car = CarFactory.Create(spec, colorIndex, (human ? "Player_" : "NPC_") + driverName, pose.position, pose.rotation);
            car.inputLocked = true;
            var bounds = car.gameObject.AddComponent<TrackBounds>();
            bounds.checkpoints = Track.Checkpoints;
            Track.Checkpoints.Register(car, pose);

            var entry = new RaceEntry
            {
                car = car, name = driverName, isHuman = human, playerIndex = playerIndex, gridSlot = slot,
                model = spec.model, colorIndex = colorIndex, position = slot + 1
            };
            entries.Add(entry);
            byCar[car] = entry;
            // Treino da carreira: restaura a melhor volta JÁ gravada no save — sair pro menu/hub
            // não apaga o que foi feito (não precisa refazer as voltas).
            if (Settings.laps >= 900 && Settings.careerPhase >= 0)
                entry.bestLap = RaceX.Career.CareerMode.LoadPracticeBest(driverName);
            return entry;
        }

        public RaceEntry GetEntry(CarController car) => car != null && byCar.TryGetValue(car, out var e) ? e : null;

        public RaceProgress GetProgress(RaceEntry e) => Track.Checkpoints.Get(e.car);

        /// <summary>fixedHold ≥ 0: espera com as 5 luzes acesas definida de fora (online: a MESMA para todos, vinda do
        /// servidor). Negativo = sorteio local de 0,2–3 s.</summary>
        public void BeginCountdown(float fixedHold = -1f) => StartCoroutine(CountdownRoutine(fixedHold));

        /// <summary>Online: o servidor fechou a corrida (resultado oficial) com o jogador ainda na pista — encerra aqui também.</summary>
        public void EndNow()
        {
            if (State == RaceState.Finished || endQueued) return;
            endQueued = true;
            StartCoroutine(EndRaceRoutine());
        }

        /// <summary>Volta de apresentação em andamento (HUD mostra o aviso e o botão PULAR).</summary>
        public bool FormationLap { get; private set; }
        /// <summary>Pedido de pular a volta de apresentação (os carros vão direto às posições do grid).</summary>
        public static bool SkipFormationRequested;
        /// <summary>Luzes da largada: -1 apagado/oculto, 0 painel sem luz, 1..5 vermelhas acesas, 6 TODAS VERDES (vai!).</summary>
        public int LightsOn { get; private set; } = -1;
        public float LightsGreenAt { get; private set; } = -1f;

        /// <summary>LARGADA (v81, procedimento da F1): carros no grid → VOLTA DE APRESENTAÇÃO (pole na frente, fila, sem
        /// ultrapassar, zigue-zague nas retas) → cada um para na SUA posição → 5 luzes vermelhas, uma por segundo →
        /// espera aleatória → todas VERDES de uma vez (pedido do usuário; na F1 real elas se apagam). Treino e
        /// classificação (sessões por tempo) saem do box: sinal verde direto.</summary>
        IEnumerator CountdownRoutine(float fixedHold)
        {
            State = RaceState.Countdown;
            yield return new WaitForSeconds(0.5f);
            bool timed = Settings.laps >= 900;
            if (timed)
            {
                LightsOn = 6; LightsGreenAt = Time.time;
                CountdownValue = 0;
                CountdownTick?.Invoke(0);
                StartRace();
                yield break;
            }
            bool formation = Settings.mode != GameMode.Online && SettingsManager.FormationLap && !AutoTest.Active; // autoteste mede a corrida
            if (formation) yield return FormationLapRoutine();
            LightsOn = 0;
            yield return new WaitForSeconds(1f);
            for (int i = 1; i <= 5; i++)
            {
                LightsOn = i;
                CountdownValue = 6 - i;
                CountdownTick?.Invoke(6 - i);
                yield return new WaitForSeconds(CountdownStep);
            }
            // Espera aleatória com as 5 acesas (ninguém adivinha o "vai").
            yield return new WaitForSeconds(fixedHold >= 0f ? fixedHold : UnityEngine.Random.Range(0.2f, 3.0f)); // F1: 0,2–3 s, sorteado por computador
            LightsOn = 6; LightsGreenAt = Time.time;
            CountdownValue = 0;
            CountdownTick?.Invoke(0);
            StartRace();
        }

        /// <summary>VOLTA DE APRESENTAÇÃO: os carros andam em fila pela pista inteira (velocidade pela curva, máx.
        /// ~150 km/h; o líder dita o ritmo) e param EXATAMENTE nas suas posições do grid. Movimento guiado
        /// (cinemático, sem colisão) — ninguém bate nem erra a vaga. Pular = vai direto ao grid.</summary>
        IEnumerator FormationLapRoutine()
        {
            var sp = Track != null ? Track.Spline : null;
            if (sp == null || sp.Count < 20) yield break;
            var list = new List<RaceEntry>();
            foreach (var e in entries) if (e.car != null) list.Add(e);
            int n = list.Count;
            if (n == 0) yield break;
            float L = sp.Length;
            var s0 = new float[n]; var lat0 = new float[n]; var dy = new float[n];
            var pose0 = new Pose[n]; var kin = new bool[n]; var det = new bool[n];
            var interp = new RigidbodyInterpolation[n];
            var straightS = new float[n];   // fator "reta" suavizado por carro (0 curva … 1 reta)
            int lead = 0;
            for (int i = 0; i < n; i++)
            {
                var c = list[i].car;
                pose0[i] = new Pose(c.transform.position, c.transform.rotation);
                s0[i] = sp.ClosestIndex(c.transform.position) * sp.spacing;
                var p = sp.PoseAtDistance(s0[i]);
                lat0[i] = Vector3.Dot(c.transform.position - p.position, p.rotation * Vector3.right);
                dy[i] = c.transform.position.y - p.position.y;
                kin[i] = c.Body.isKinematic; det[i] = c.Body.detectCollisions;
                c.Body.isKinematic = true; c.Body.detectCollisions = false;
                // Movimento por POSIÇÃO a cada quadro: com a interpolação ligada o Unity redesenhava o carro entre os
                // passos de física (50 Hz) e brigava com a posição escrita — os carros andavam "aos saltos" com o
                // cenário liso. Mesmo cuidado que o replay já tinha.
                interp[i] = c.Body.interpolation;
                c.Body.interpolation = RigidbodyInterpolation.None;
                if (list[i].gridSlot < list[lead].gridSlot) lead = i;
            }
            // Os slots ficam ATRÁS da linha: o pole tem o maior s (relativo). A fila anda junto (mesma distância d).
            FormationLap = true;
            SkipFormationRequested = false;
            float d = 0f, v = 0f, total = L;
            int look = Mathf.Max(2, Mathf.RoundToInt(12f / Mathf.Max(0.5f, sp.spacing)));
            while (d < total - 0.05f && !SkipFormationRequested)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                float sLead = s0[lead] + d;
                // Velocidade pela curvatura à frente do líder (lat. ~9 m/s²) e freada para parar no grid.
                float vMax = 41f;
                for (float ahead = 0f; ahead <= 120f; ahead += 20f)
                {
                    int ci = sp.Wrap(Mathf.RoundToInt((sLead + ahead) / sp.spacing));
                    Vector3 ta = sp.tangents[sp.Wrap(ci - look)], tb = sp.tangents[sp.Wrap(ci + look)];
                    float ang = Vector3.Angle(ta, tb) * Mathf.Deg2Rad;
                    float kappa = ang / Mathf.Max(1f, 2f * look * sp.spacing);
                    float vc = kappa > 1e-4f ? Mathf.Sqrt(9f / kappa) : 99f;
                    vMax = Mathf.Min(vMax, Mathf.Sqrt(vc * vc + 2f * 6f * ahead));
                }
                vMax = Mathf.Max(8f, vMax);
                vMax = Mathf.Min(vMax, Mathf.Sqrt(2f * 3.2f * Mathf.Max(0f, total - d)) + 0.4f);
                v = Mathf.MoveTowards(v, vMax, (v < vMax ? 4.5f : 7f) * dt);
                d = Mathf.Min(total, d + v * dt);
                float edge = Mathf.Clamp01(Mathf.Min(d, total - d) / 160f);
                for (int i = 0; i < n; i++)
                {
                    var c = list[i].car;
                    if (c == null) continue;
                    float s = s0[i] + d;
                    // Zigue-zague nas retas (aquece um pouco a superfície e limpa sujeira — efeito modesto).
                    int si = sp.Wrap(Mathf.RoundToInt(s / sp.spacing));
                    // Fator CONTÍNUO (3°–9°) e suavizado no tempo (~2 s para virar): antes era 0/1 no limite de 6° e o
                    // zigue-zague (1,1 m) e o giro (6°) entravam/saíam num quadro — o carro "pulava" de lado e voltava.
                    float ang = Vector3.Angle(sp.tangents[sp.Wrap(si - look)], sp.tangents[sp.Wrap(si + look)]);
                    straightS[i] = Mathf.MoveTowards(straightS[i], 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 9f, ang)), 0.5f * dt);
                    float straight = straightS[i];
                    float weave = 1.1f * straight * Mathf.Sin(s / 38f * 6.283f + i * 1.3f);
                    float lat = Mathf.Lerp(lat0[i], weave, edge);
                    // Pose INTERPOLADA: a PoseAtDistance anda em degraus de ~2 m (o "andar por quadros distantes").
                    var p = sp.PoseAtDistanceSmooth(Mathf.Repeat(s, L), lat);
                    float yaw = straight * edge * 6f * Mathf.Cos(s / 38f * 6.283f + i * 1.3f);
                    c.transform.SetPositionAndRotation(p.position + Vector3.up * dy[i], p.rotation * Quaternion.Euler(0f, yaw, 0f));
                }
                yield return null;
            }
            // Chegada (ou PULAR): cada carro EXATAMENTE na sua posição do grid, parado.
            for (int i = 0; i < n; i++)
            {
                var c = list[i].car;
                if (c == null) continue;
                c.Body.isKinematic = kin[i];
                c.Body.detectCollisions = det[i];
                c.Body.interpolation = interp[i];
                c.ResetTo(pose0[i].position, pose0[i].rotation);
                Track.Checkpoints.Register(c, pose0[i]); // a volta de apresentação NÃO conta volta
            }
            FormationLap = false;
            SkipFormationRequested = false;
        }

        bool IsCareerSession => Settings.careerPhase >= 0 && RaceX.Career.CareerMode.data != null;

        // Relógio da sessão da carreira: anda só com esta cena aberta e o jogo em primeiro plano.
        void OnDestroy()
        {
            if (IsCareerSession) RaceX.Career.CareerMode.PauseSessionClock();
        }

        void OnApplicationPause(bool paused)
        {
            if (!IsCareerSession) return;
            if (paused) RaceX.Career.CareerMode.PauseSessionClock();
            // voltou ao jogo: retoma se a sessão está rodando (ou com o hub aberto por cima); com o menu de pausa aberto
            // (tempo parado sem o hub), continua pausado até fechar o menu.
            else if (State == RaceState.Racing && (Time.timeScale > 0f || RaceX.UI.CareerFlow.IsOpen)) RaceX.Career.CareerMode.ResumeSessionClock();
        }

        void OnApplicationQuit()
        {
            if (IsCareerSession) RaceX.Career.CareerMode.PauseSessionClock();
        }

        void StartRace()
        {
            State = RaceState.Racing;
            if (IsCareerSession) RaceX.Career.CareerMode.ResumeSessionClock();   // começou na pista: o relógio anda
            raceStartTime = Time.time;
            ConfigureSessionTime(Settings.sessionTimeLimit);
            foreach (var e in entries)
            {
                e.lapStartTime = raceStartTime;
                e.car.inputLocked = false;
            }
            RaceStarted?.Invoke();
        }

        void OnLapCompleted(CarController car, int lapNumber)
        {
            if (!byCar.TryGetValue(car, out var e) || e.finished) return;
            // ONLINE: depois que a corrida DESTE aparelho acabou, os carros que ele pilota (IA do anfitrião, carro de quem caiu)
            // continuam cruzando a linha e a chegada vale para o placar oficial — antes a IA que chegava depois do anfitrião
            // ficava "na pista" e caía atrás de convidados que chegaram depois dela.
            bool lateOnline = State == RaceState.Finished && Settings.mode == GameMode.Online && !e.isHuman && car != null && !car.IsRemote;
            if (State != RaceState.Racing && !lateOnline) return;
            float now = Time.time;
            float lapTime = now - e.lapStartTime;
            e.lapStartTime = now;
            // Out-lap (saída do pit → primeira linha): nunca conta como volta/melhor volta.
            if (e.outLap) { e.outLap = false; SectorTimer.Instance?.BeginTimedLap(e); return; }
            // Cruzamento ESPEÇURJO pós-pit (saída deposita o carro perto da linha): conta a volta
            // (legítima — foi completada antes do pit), zera o cronômetro, mas NÃO registra tempo
            // de 2-9 s como volta/melhor volta.
            if (lapTime < 10f) return;
            e.lapTimes.Add(lapTime);
            bool best = e.bestLap <= 0f || lapTime < e.bestLap;
            if (best) e.bestLap = lapTime;
            // Treino da carreira: grava a melhor volta no SAVE — sair pro hub/menu não perde as voltas feitas.
            if (best && Settings.laps >= 900 && Settings.careerPhase >= 0)
                Career.CareerMode.SavePracticeBest(e.name, e.bestLap);
            if (e.isHuman) SettingsManager.TrySaveBestLap((int)Settings.track, (int)e.model, lapTime);
            LapCompleted?.Invoke(e, lapTime, best);

            if (lapNumber >= Settings.laps) Finish(e, now);
        }

        /// <summary>Configura o limite de tempo da sessão (chamado no início da corrida).</summary>
        public void ConfigureSessionTime(float limitSeconds)
        {
            sessionTimeLimit = Mathf.Max(0f, limitSeconds);
            sessionStart = Time.time;
        }

        /// <summary>Fim de sessão por tempo: congela a ordem atual e mostra resultados.</summary>
        void EndByTime()
        {
            State = RaceState.Finished;
            foreach (var e in entries)
            {
                if (!e.isHuman || e.car == null) continue;
                var input = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                if (input != null) input.enabled = false;
                if (e.car.GetComponent<CarAI>() == null)
                    e.car.gameObject.AddComponent<CarAI>().Initialize(Track.Waypoints, this, Settings.difficulty);
                e.car.inputLocked = false;
            }
            RaceFinished?.Invoke();
        }

        void Finish(RaceEntry e, float now)
        {
            if (e.retired) return;
            e.finished = true;
            e.totalTime = now - raceStartTime + e.penaltySeconds;
            finishOrder.Add(e);
            EntryFinished?.Invoke(e);
            if (e.isHuman && HumansDone()) StartCoroutine(EndRaceRoutine());
        }

        bool HumansDone() => entries.TrueForAll(x => !x.isHuman || x.finished || x.retired);
        bool endQueued;

        /// <summary>Tira o carro da corrida (perda total / desclassificação). O jogador fora = fim da corrida para ele.</summary>
        public void Retire(RaceEntry e, string reason)
        {
            if (e == null || e.finished || e.retired) return;
            e.retired = true;
            e.retireReason = reason;
            EntryRetired?.Invoke(e);
            if (e.isHuman && HumansDone() && !endQueued && State == RaceState.Racing) { endQueued = true; StartCoroutine(EndRaceRoutine()); }
        }

        /// <summary>Penalidade de tempo: soma no tempo final (se já cruzou a linha, soma direto no tempo).</summary>
        public void AddPenalty(RaceEntry e, float seconds)
        {
            if (e == null || e.retired) return;
            e.penaltySeconds += seconds;
            e.penaltyCount++;
            if (e.finished) e.totalTime += seconds;
        }

        IEnumerator EndRaceRoutine()
        {
            yield return new WaitForSeconds(FinishScreenDelay);
            State = RaceState.Finished;
            foreach (var e in entries)
            {
                if (!e.isHuman || e.car == null) continue;
                var input = e.car.GetComponent<RaceX.Input.PlayerInputHandler>();
                if (input != null) input.enabled = false;
                if (e.car.GetComponent<CarAI>() == null)
                    e.car.gameObject.AddComponent<CarAI>().Initialize(Track.Waypoints, this, Settings.difficulty);
                e.car.inputLocked = false;
            }
            RaceFinished?.Invoke();
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (State != RaceState.Racing) return;
            // Relógio REAL da carreira: tempo passado em menu/hub também desconta (deadline UTC).
            if (Settings.careerPhase >= 0 && sessionTimeLimit > 0f)
            {
                // A PISTA ESPELHA o relógio da carreira (nos dois sentidos, em tempo de jogo): os dois nunca divergem.
                int careerLeft = RaceX.Career.CareerMode.SessionGameSecondsLeft();
                if (careerLeft > 0 && RaceX.Career.CareerMode.SessionClockActive && !RaceX.Career.CareerMode.SessionClockPaused)
                    sessionTimeLimit = (Time.time - sessionStart) + careerLeft;
            }
            // Fim POR TEMPO. Treino/CLASSIFICAÇÃO (laps>=900 = sessão por tempo): o tempo acabou
            // = FIM IMEDIATO com a classificação na tela (a Q2 não espera "volta de cortesia" —
            // quem estava no pit/menu quando o tempo zera sai e VÊ o resultado). A volta de
            // cortesia continua só em CORRIDA com limite de tempo.
            // RELÓGIO REAL da carreira (deadline UTC): venceu mesmo com o jogo congelado no
            // menu/hub (timeScale=0 congela o Time.time local) = FIM IMEDIATO — era o buraco que
            // deixava a Q2 "nunca acabar" quando o tempo vencia com o hub aberto.
            bool careerClockUp = Settings.careerPhase >= 0 && sessionTimeLimit > 0f
                && RaceX.Career.CareerMode.SessionClockActive
                && RaceX.Career.CareerMode.SessionSecondsLeft() <= 0;
            // Relógio venceu com o hub aberto (jogo congelado): fim imediato, como antes.
            if (careerClockUp && RaceX.UI.CareerFlow.IsOpen) { EndByTime(); return; }
            bool sessionTimeUp = sessionTimeLimit > 0f && Time.time - sessionStart >= sessionTimeLimit;
            if (Settings.laps >= 900 && (careerClockUp || sessionTimeUp))
            {
                // BANDEIRA QUADRICULADA (regra da F1): quem já está numa volta lançada termina a volta e o tempo
                // vale; no box ou na volta de saída a sessão fecha na hora. Teto de 3 min por segurança.
                if (!timeExpired)
                {
                    timeExpired = true;
                    lapAtExpiry = HumanLapNow();
                    flagAt = Time.time;
                    flyingAtFlag = HumanOnFlyingLap();
                }
                if (!flyingAtFlag || HumanLapNow() > lapAtExpiry || HumanFinished() || !HumanOnFlyingLap() || Time.time - flagAt > 180f)
                {
                    EndByTime();
                    return;
                }
            }
            else if (careerClockUp) { EndByTime(); return; }
            else if (sessionTimeUp)
            {
                if (!timeExpired)
                {
                    timeExpired = true;
                    lapAtExpiry = HumanLapNow();
                }
                if (HumanLapNow() > lapAtExpiry || HumanFinished()) { EndByTime(); }
                return;
            }
            foreach (var e in entries) e.raceDistance = Track.Checkpoints.RaceDistance(e.car);
            if (Settings.laps >= 900)
            {
                // SESSÃO CRONOMETRADA (treino/classi): placar AO VIVO é por MELHOR VOLTA —
                // quem tem tempo sobe na hora; quem está com 0 mantém a ordem INICIAL (grid).
                // Não espera ultrapassagem nem distância — isso é corrida, não sessão de tempo.
                entries.Sort((a, b) =>
                {
                    bool ha = a.bestLap > 0f, hb = b.bestLap > 0f;
                    if (ha && hb) return a.bestLap.CompareTo(b.bestLap);
                    if (ha != hb) return ha ? -1 : 1;
                    return a.gridSlot.CompareTo(b.gridSlot);
                });
            }
            else
                entries.Sort((a, b) =>
                {
                    if (a.finished && b.finished) return finishOrder.IndexOf(a).CompareTo(finishOrder.IndexOf(b));
                    if (a.finished != b.finished) return a.finished ? -1 : 1;
                    if (a.retired != b.retired) return a.retired ? 1 : -1; // abandono vai para o fim
                    return b.raceDistance.CompareTo(a.raceDistance);
                });
            for (int i = 0; i < entries.Count; i++) entries[i].position = i + 1;
        }

        public List<RaceEntry> FinalStandings()
        {
            // Corrida: quem terminou, pelo tempo FINAL (com penalidades — como na F1, a penalidade
            // pode tirar posição); depois quem não terminou (por distância); abandonos (PT/DSQ) no fim.
            var list = new List<RaceEntry>(finishOrder);
            if (Settings.laps < 900) list.Sort((a, b) => a.totalTime.CompareTo(b.totalTime));
            var rest = new List<RaceEntry>();
            foreach (var e in entries) if (!list.Contains(e)) rest.Add(e);
            rest.Sort((a, b) => a.retired != b.retired ? (a.retired ? 1 : -1) : b.raceDistance.CompareTo(a.raceDistance));
            list.AddRange(rest);
            // Sessão só por tempo (treinos/classificação): ordem SEMPRE por MELHOR VOLTA,
            // nunca por distância/ultrapassagem (o quem andou mais tempo não é o mais rápido).
            if (Settings.laps >= 900)
                list.Sort((a, b) =>
                {
                    bool ha = a.bestLap > 0f, hb = b.bestLap > 0f;
                    if (ha && hb) return a.bestLap.CompareTo(b.bestLap);
                    if (ha != hb) return ha ? -1 : 1;
                    return a.gridSlot.CompareTo(b.gridSlot);
                });
            return list;
        }
    }
}
