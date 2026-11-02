using System;
using System.Collections.Generic;
using System.Globalization;
using RaceX.Car;
using RaceX.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaceX.Core
{
    /// <summary>Replay de verdade (v80). GRAVA a sessão inteira a 10 Hz (todos os carros: posição, rotação, velocidade,
    /// giro, esterço, posição na corrida, box/fora) + LANCES (voltas do líder, toques, perda total, abandono, box,
    /// avisos da direção de prova, ultrapassagens no top 3/do jogador, volta mais rápida, largada e bandeirada).
    /// REPRODUZ com o mundo CONGELADO (timeScale 0 e sistemas da corrida em espera — nada conta volta, punição ou
    /// setor com as posições reproduzidas): play/pausa, ré, 0,1x–8x, ±10 s, quadro a quadro, linha do tempo arrastável
    /// com marcas de volta e de lance, pular para o lance anterior/próximo, 9 câmeras (TV na beira da pista,
    /// perseguição, onboard, T-cam, capô, volante, helicóptero, LIVRE girando com o dedo e DIRETOR automático que
    /// procura as brigas), escolher o carro (setas ou tocando na torre), torre de posições DO INSTANTE, som do motor
    /// pelo giro gravado e interface ocultável. Abre no fim da sessão e na pausa (replay instantâneo, últimos 30 s).
    /// Memória: ~600 B por amostra com 24 carros (~22 MB/h); passou de 40 MB, as amostras são dizimadas para 5 Hz.</summary>
    public class ReplaySystem : MonoBehaviour
    {
        // ---------------- Gravação ----------------
        const int ChunkSize = 1024;
        const float RecordInterval = 0.1f;
        const long MaxBytes = 40L * 1024 * 1024;
        const int InfoStride = 6; // 0 velocidade/2 (km/h) · 1 giro 0–255 · 2 esterço*2+128 · 3 posição · 4 flags (1 freio, 2 fora, 4 box) · 5 acelerador 0–255
        const float GapSnap = 60f; // salto > 60 m entre amostras = respawn/teletransporte: não interpola

        class Chunk { public float[] t; public float[] pos; public short[] rot; public byte[] info; }

        enum EvKind { Lap, Contact, Totaled, Retired, Pit, Notice, Overtake, FastLap, Start, Finish }
        struct ReplayEvent { public float t; public EvKind kind; public int car; public string text; }

        /// <summary>DANO NO REPLAY: peça que soltou (ou conserto, zone = -1) no instante t. As peças da zona ficam
        /// gravadas aqui (o conserto no box limpa a lista do carro) e a velocidade do carro dá o voo do destroço.</summary>
        class DamageEv
        {
            public float t; public int car; public int zone; public Vector3 vel; public Vector3 pos; public Quaternion rot;
            public Renderer[] parts; public GameObject visual; public Vector3 spin;
            public Matrix4x4[] local;   // pose de cada peça em relação ao carro, no instante da batida
        }
        readonly List<DamageEv> damageEvs = new List<DamageEv>();
        readonly List<GameObject> liveDebrisHidden = new List<GameObject>();

        enum Cam { TV, Chase, Onboard, TCam, Hood, Wheel, Rear, Heli, Free, Director }
        static readonly string[] CamNames = { "TV", "PERSEGUIÇÃO", "ONBOARD", "T-CAM", "CAPÔ", "VOLANTE", "TRASEIRA", "HELICÓPTERO", "LIVRE", "DIRETOR" };
        static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f, 8f };
        static readonly string[] SpeedNames = { "0,1x", "0,25x", "0,5x", "1x", "2x", "4x", "8x" };

        static ReplaySystem active;
        static bool staticHooks;
        int exitFrame = -1;

        /// <summary>Replay na tela: sistemas da corrida (pit, direção de prova, voltas, setores, limites) ficam em espera.</summary>
        public static bool IsPlaying => active != null && (active.playing || active.exitFrame == Time.frameCount);

        /// <summary>Relógio do replay na base de Time.time da gravação (−1 fora do replay) — os mecânicos do pit usam
        /// para reconstituir as paradas gravadas.</summary>
        public static float PlaybackClock => active != null && active.playing && active.count > 1 ? active.T(0) + active.playT : -1f;

        RaceManager race;
        CarController[] carList = new CarController[0];
        RaceEntry[] entryOf = new RaceEntry[0];
        CarVisuals[] visuals = new CarVisuals[0];
        int nCars;
        readonly List<Chunk> chunks = new List<Chunk>();
        int count;
        float lastStoreT = -999f;
        bool recording;
        float stopAt = -1f;
        readonly List<ReplayEvent> events = new List<ReplayEvent>();
        byte[] prevPos = new byte[0], prevFlags = new byte[0];
        float[] lastOvertakeT = new float[0];
        int markerLaps;
        float bestLapAll = float.MaxValue;
        string lastContactKey = "";
        float lastContactT = -99f;
        string lastNotice = "";
        float lastNoticeT = -99f;
        bool winnerSeen;

        public static void StartRecording(RaceManager race)
        {
            if (race == null) return;
            if (active == null)
            {
                active = new GameObject("ReplayRecorder").AddComponent<ReplaySystem>();
                DontDestroyOnLoad(active.gameObject);
            }
            if (active.playing) active.Exit();
            if (active.race != race) active.ResetFor(race);
            active.recording = true;
            active.stopAt = -1f;
            HookStatic();
        }

        /// <summary>Há gravação suficiente desta sessão para abrir o replay?</summary>
        public static bool Available(RaceManager race) => active != null && race != null && active.race == race && active.count >= 20;

        /// <summary>Compatível com o botão REPLAY dos resultados: abre do começo da sessão.</summary>
        public static ReplaySystem Begin(RaceManager race, CarCamera cam, RectTransform resultsPanel) => Play(race, cam, false, null);

        /// <summary>Abre o replay. instant = começa nos últimos 30 s (replay instantâneo da pausa).</summary>
        public static ReplaySystem Play(RaceManager race, CarCamera cam, bool instant, Action onExit)
        {
            if (!Available(race) || active.playing) return null;
            active.StartPlayback(cam, instant, onExit);
            return active;
        }

        void ResetFor(RaceManager r)
        {
            race = r;
            chunks.Clear(); count = 0; nCars = 0; events.Clear(); lastStoreT = -999f;
            ClearDamageVisuals(); damageEvs.Clear();
            markerLaps = 0; bestLapAll = float.MaxValue; winnerSeen = false;
            carList = new CarController[0]; entryOf = new RaceEntry[0]; visuals = new CarVisuals[0];
            r.LapCompleted += OnLap;
            r.EntryRetired += OnRetired;
            r.EntryFinished += OnFinished;
            r.RaceStarted += OnStart;
            r.RaceFinished += OnRaceFinished;
        }

        static void HookStatic()
        {
            if (staticHooks) return;
            staticHooks = true;
            CarDamage.CarHitCar += (a, b, g) => { if (active != null) active.OnContact(a, b); };
            CarDamage.CarTotaled += c => { if (active != null) active.Add(EvKind.Totaled, active.IndexOf(c), "PERDA TOTAL: " + active.NameOf(active.IndexOf(c))); };
            CarDamage.PartDetached += (c, zone, at) => { if (active != null) active.OnDamage(c, zone); };
            CarDamage.Repaired += c => { if (active != null) active.OnDamage(c, -1); };
            RaceControl.Notice += (e, msg) => { if (active != null) active.OnNotice(e, msg); };
        }

        // ---------------- Amostras ----------------
        int BytesPerSample => 4 + nCars * (12 + 8 + InfoStride);
        float T(int i) => chunks[i / ChunkSize].t[i % ChunkSize];
        Vector3 P(int i, int k)
        {
            var c = chunks[i / ChunkSize];
            int b = ((i % ChunkSize) * nCars + k) * 3;
            return new Vector3(c.pos[b], c.pos[b + 1], c.pos[b + 2]);
        }
        Quaternion R(int i, int k)
        {
            var c = chunks[i / ChunkSize];
            int b = ((i % ChunkSize) * nCars + k) * 4;
            var q = new Quaternion(c.rot[b] / 32767f, c.rot[b + 1] / 32767f, c.rot[b + 2] / 32767f, c.rot[b + 3] / 32767f);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-4f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
        byte Info(int i, int k, int f) => chunks[i / ChunkSize].info[((i % ChunkSize) * nCars + k) * InfoStride + f];

        Chunk NewChunk() => new Chunk
        {
            t = new float[ChunkSize],
            pos = new float[ChunkSize * nCars * 3],
            rot = new short[ChunkSize * nCars * 4],
            info = new byte[ChunkSize * nCars * InfoStride],
        };

        int CarsNow() { int n = 0; foreach (var e in race.entries) if (e.car != null) n++; return n; }

        void InitCars()
        {
            var cars = new List<CarController>();
            var ents = new List<RaceEntry>();
            foreach (var e in race.entries) if (e.car != null) { cars.Add(e.car); ents.Add(e); }
            carList = cars.ToArray();
            entryOf = ents.ToArray();
            nCars = carList.Length;
            visuals = new CarVisuals[nCars];
            for (int k = 0; k < nCars; k++) visuals[k] = carList[k].GetComponent<CarVisuals>();
            prevPos = new byte[nCars]; prevFlags = new byte[nCars]; lastOvertakeT = new float[nCars];
            for (int k = 0; k < nCars; k++) lastOvertakeT[k] = -99f;
            chunks.Clear(); count = 0; events.Clear(); markerLaps = 0; lastStoreT = -999f;
        }

        void Store()
        {
            if (race == null) return;
            // O bootstrap cria os carros em lotes (humano primeiro, NPCs depois): enquanto o grid se forma,
            // a gravação recomeça com a lista completa.
            if (nCars == 0 || (count < 600 && CarsNow() > nCars)) { InitCars(); if (nCars == 0) return; }
            float now = Time.time;
            if (count > 0 && now - lastStoreT < RecordInterval) return;
            lastStoreT = now;
            if ((long)count * BytesPerSample >= MaxBytes) Decimate();
            int ci = count / ChunkSize, o = count % ChunkSize;
            while (ci >= chunks.Count) chunks.Add(NewChunk());
            var c = chunks[ci];
            c.t[o] = now;
            for (int k = 0; k < nCars; k++)
            {
                int b3 = (o * nCars + k) * 3, b4 = (o * nCars + k) * 4, bi = (o * nCars + k) * InfoStride;
                var car = carList[k];
                if (car == null)
                {
                    if (count > 0) CopyCar(count - 1, count, k);
                    continue;
                }
                var tr = car.transform;
                Vector3 p = tr.position;
                Quaternion q = tr.rotation;
                c.pos[b3] = p.x; c.pos[b3 + 1] = p.y; c.pos[b3 + 2] = p.z;
                c.rot[b4] = (short)Mathf.RoundToInt(q.x * 32767f); c.rot[b4 + 1] = (short)Mathf.RoundToInt(q.y * 32767f);
                c.rot[b4 + 2] = (short)Mathf.RoundToInt(q.z * 32767f); c.rot[b4 + 3] = (short)Mathf.RoundToInt(q.w * 32767f);
                float limit = car.spec != null ? Mathf.Max(1f, car.spec.rpmLimit) : 8000f;
                var e = entryOf[k];
                byte fl = 0;
                if (car.brakeInput > 0.15f) fl |= 1;
                if (e != null && e.retired) fl |= 2;
                if (PitLaneSystem.IsInPitFlow(car)) fl |= 4;
                c.info[bi] = (byte)Mathf.Clamp(Mathf.RoundToInt(car.SpeedKmh * 0.5f), 0, 255);
                c.info[bi + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(car.Rpm / limit * 255f), 0, 255);
                c.info[bi + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(car.SteerAngle * 2f + 128f), 0, 255);
                c.info[bi + 3] = (byte)Mathf.Clamp(e != null ? e.position : 0, 0, 255);
                c.info[bi + 4] = fl;
                // acelerador de verdade (o som do replay alternava entre 0,85 e 0,05 só pela flag de freio: picotava)
                c.info[bi + 5] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(car.pitControlled ? car.extThrottle : car.throttleInput) * 255f), 0, 255);
            }
            count++;
            PollEvents(c, o);
        }

        void CopyCar(int from, int to, int k)
        {
            var a = chunks[from / ChunkSize]; var b = chunks[to / ChunkSize];
            int fo = from % ChunkSize, tt = to % ChunkSize;
            Array.Copy(a.pos, (fo * nCars + k) * 3, b.pos, (tt * nCars + k) * 3, 3);
            Array.Copy(a.rot, (fo * nCars + k) * 4, b.rot, (tt * nCars + k) * 4, 4);
            Array.Copy(a.info, (fo * nCars + k) * InfoStride, b.info, (tt * nCars + k) * InfoStride, InfoStride);
        }

        /// <summary>Memória cheia: fica 1 amostra a cada 2 (o trecho antigo passa a 5 Hz, a gravação segue a 10 Hz).</summary>
        void Decimate()
        {
            int n = 0;
            for (int i = 0; i < count; i += 2, n++)
            {
                if (i == n) continue;
                var a = chunks[i / ChunkSize]; var b = chunks[n / ChunkSize];
                int io = i % ChunkSize, no = n % ChunkSize;
                b.t[no] = a.t[io];
                Array.Copy(a.pos, io * nCars * 3, b.pos, no * nCars * 3, nCars * 3);
                Array.Copy(a.rot, io * nCars * 4, b.rot, no * nCars * 4, nCars * 4);
                Array.Copy(a.info, io * nCars * InfoStride, b.info, no * nCars * InfoStride, nCars * InfoStride);
            }
            count = n;
            int keep = count / ChunkSize + 1;
            while (chunks.Count > keep) chunks.RemoveAt(chunks.Count - 1);
        }

        // ---------------- Lances ----------------
        void Add(EvKind kind, int car, string text)
        {
            if (!recording || race == null || count == 0) return;
            events.Add(new ReplayEvent { t = Time.time, kind = kind, car = car, text = text });
        }

        int IndexOf(CarController c) { for (int k = 0; k < nCars; k++) if (carList[k] == c) return k; return -1; }
        int IndexOf(RaceEntry e) { for (int k = 0; k < nCars; k++) if (entryOf[k] == e) return k; return -1; }
        string NameOf(int k) => k >= 0 && k < nCars && entryOf[k] != null && !string.IsNullOrEmpty(entryOf[k].name) ? entryOf[k].name.ToUpperInvariant() : "CARRO " + (k + 1);
        bool Timed => race != null && race.Settings != null && race.Settings.laps >= 900;

        void OnStart() => Add(EvKind.Start, -1, "LARGADA");

        void OnLap(RaceEntry e, float lapTime, bool best)
        {
            if (e == null) return;
            int k = IndexOf(e);
            // Marcas de volta: do LÍDER na corrida; nas sessões por tempo (treino/classificação), as do jogador.
            bool marker = Timed ? e.isHuman : e.position == 1;
            if (marker && e.lapTimes.Count > markerLaps)
            {
                markerLaps = e.lapTimes.Count;
                Add(EvKind.Lap, k, "VOLTA " + (markerLaps + 1));
            }
            if (lapTime > 1f && lapTime < bestLapAll)
            {
                bestLapAll = lapTime;
                if (markerLaps >= 2) Add(EvKind.FastLap, k, "VOLTA MAIS RÁPIDA: " + NameOf(k) + " " + LapFmt(lapTime));
            }
        }

        void OnRetired(RaceEntry e)
        {
            int k = IndexOf(e);
            for (int i = events.Count - 1; i >= 0 && Time.time - events[i].t < 4f; i--)
                if (events[i].car == k && events[i].kind == EvKind.Totaled) return; // PT já marcada
            Add(EvKind.Retired, k, "FORA: " + NameOf(k) + (e != null && !string.IsNullOrEmpty(e.retireReason) ? " (" + e.retireReason + ")" : ""));
        }

        void OnFinished(RaceEntry e)
        {
            if (Timed || winnerSeen) return;
            winnerSeen = true;
            Add(EvKind.Finish, IndexOf(e), "BANDEIRADA: " + NameOf(IndexOf(e)) + " VENCE");
        }

        void OnRaceFinished() => stopAt = Time.time + 15f; // grava a volta de desaceleração e para (memória)

        void OnContact(CarController a, CarController b)
        {
            int ka = IndexOf(a), kb = IndexOf(b);
            if (ka < 0 || kb < 0) return;
            string key = Mathf.Min(ka, kb) + "-" + Mathf.Max(ka, kb);
            if (key == lastContactKey && Time.time - lastContactT < 3f) return;
            lastContactKey = key; lastContactT = Time.time;
            Add(EvKind.Contact, ka, "TOQUE: " + NameOf(ka) + " × " + NameOf(kb));
        }

        void OnNotice(RaceEntry e, string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (msg == lastNotice && Time.time - lastNoticeT < 8f) return;
            lastNotice = msg; lastNoticeT = Time.time;
            int k = IndexOf(e);
            Add(EvKind.Notice, k, (k >= 0 ? NameOf(k) + ": " : "") + msg);
        }

        void PollEvents(Chunk c, int o)
        {
            bool racing = race.State == RaceState.Racing && !Timed;
            for (int k = 0; k < nCars; k++)
            {
                int bi = (o * nCars + k) * InfoStride;
                byte pos = c.info[bi + 3], fl = c.info[bi + 4];
                if (count > 1)
                {
                    if ((fl & 4) != 0 && (prevFlags[k] & 4) == 0) Add(EvKind.Pit, k, "BOX: " + NameOf(k));
                    if (racing && pos > 0 && prevPos[k] > 0 && pos < prevPos[k] && (fl & 6) == 0 && Time.time - lastOvertakeT[k] > 4f)
                    {
                        int victim = -1;
                        for (int j = 0; j < nCars; j++)
                        {
                            if (j == k || prevPos[j] != pos) continue;
                            int bj = (o * nCars + j) * InfoStride;
                            if (c.info[bj + 3] > pos && (c.info[bj + 4] & 6) == 0) { victim = j; break; }
                        }
                        bool human = entryOf[k] != null && entryOf[k].isHuman || victim >= 0 && entryOf[victim] != null && entryOf[victim].isHuman;
                        if (victim >= 0 && (pos <= 3 || human))
                        {
                            lastOvertakeT[k] = Time.time;
                            Add(EvKind.Overtake, k, "ULTRAPASSAGEM: " + NameOf(k) + " passa " + NameOf(victim) + " (P" + pos + ")");
                        }
                    }
                }
                prevPos[k] = pos; prevFlags[k] = fl;
            }
        }

        // ---------------- Reprodução ----------------
        bool playing, running, scrubbing, wasRunning;
        float playT, endT, prevPlayT;
        int dir = 1, speedIdx = 3, target, curSample, dirIdx;
        Cam cam = Cam.TV, dirCam = Cam.TV;
        float directorNext, manualCarUntil;
        Camera camRef;
        Vector3 camPos0; Quaternion camRot0; float camFov0; Rect camRect0;
        readonly List<Behaviour> disabled = new List<Behaviour>();
        Vector3[] savedPos; Quaternion[] savedRot; RigidbodyInterpolation[] savedInterp; bool[] savedHead; float[] savedThrottle, savedBrake, steer0, wheelSpin, wheelR;
        Vector3[,] wheelLp; Quaternion[,] wheelLr; bool[,] wheelOk;
        float prevTimeScale; bool prevAudioPause, recordingWas;
        Action onExit;
        Vector3 smoothPos; Quaternion smoothRot; bool snapCam = true;
        int tvSpot = -1, splineHint = -1;
        readonly List<Vector3> tvSpotPos = new List<Vector3>();
        readonly List<int> tvSpotIdx = new List<int>();
        float freeYaw = 150f, freePitch = 16f, freeDist = 9f;
        float lastPinch = -1f;
        string toastText = "";
        float toastUntil;

        void StartPlayback(CarCamera carCam, bool instant, Action exitCb)
        {
            onExit = exitCb;
            recordingWas = recording;
            recording = false;
            endT = T(count - 1) - T(0);
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            prevAudioPause = AudioListener.pause;
            AudioListener.pause = false;

            int n = nCars;
            savedPos = new Vector3[n]; savedRot = new Quaternion[n]; savedInterp = new RigidbodyInterpolation[n]; savedHead = new bool[n];
            savedThrottle = new float[n]; savedBrake = new float[n]; steer0 = new float[n]; wheelSpin = new float[n]; wheelR = new float[n];
            wheelLp = new Vector3[n, 4]; wheelLr = new Quaternion[n, 4]; wheelOk = new bool[n, 4];
            for (int k = 0; k < n; k++)
            {
                var c = carList[k];
                if (c == null) continue;
                var tr = c.transform;
                savedPos[k] = tr.position; savedRot[k] = tr.rotation;
                if (c.Body != null) { savedInterp[k] = c.Body.interpolation; c.Body.interpolation = RigidbodyInterpolation.None; }
                savedHead[k] = visuals[k] != null && visuals[k].driverHead != null && visuals[k].driverHead.activeSelf;
                savedThrottle[k] = c.throttleInput; savedBrake[k] = c.brakeInput; steer0[k] = c.SteerAngle;
                wheelR[k] = c.wheels.Length > 0 && c.wheels[0] != null ? c.wheels[0].radius : 0.33f;
                for (int w = 0; w < 4 && w < c.wheelMeshes.Length; w++)
                {
                    var wm = c.wheelMeshes[w];
                    if (wm == null || !wm.IsChildOf(tr)) continue; // roda solta (destroço) não volta ao carro
                    wheelLp[k, w] = tr.InverseTransformPoint(wm.position);
                    wheelLr[k, w] = Quaternion.Inverse(tr.rotation) * wm.rotation;
                    wheelOk[k, w] = true;
                }
            }

            camRef = carCam != null && carCam.Cam != null ? carCam.Cam : Camera.main;
            foreach (var cc in FindObjectsByType<CarCamera>(FindObjectsSortMode.None))
                if (cc.enabled) { cc.enabled = false; disabled.Add(cc); }
            foreach (var cm in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cm != camRef && cm.enabled && cm.targetTexture == null) { cm.enabled = false; disabled.Add(cm); }
            if (camRef != null)
            {
                camPos0 = camRef.transform.position; camRot0 = camRef.transform.rotation;
                camFov0 = camRef.fieldOfView; camRect0 = camRef.rect;
                camRef.rect = new Rect(0f, 0f, 1f, 1f);
            }
            // HUD da corrida, controles de toque, pausa e resultados somem (voltam ao sair).
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.isRootCanvas && cv.enabled && cv.renderMode != RenderMode.WorldSpace) { cv.enabled = false; disabled.Add(cv); }

            // destroços físicos da corrida ao vivo somem no replay (os do replay voam na hora certa)
            liveDebrisHidden.Clear();
            foreach (var d in CarDebris.Live)
                if (d != null && d.gameObject.activeSelf) { d.gameObject.SetActive(false); liveDebrisHidden.Add(d.gameObject); }
            target = 0;
            for (int k = 0; k < n; k++) if (entryOf[k] != null && entryOf[k].isHuman) { target = k; break; }
            playT = instant ? Mathf.Max(0f, endT - 30f) : 0f;
            prevPlayT = playT;
            running = true; dir = 1; speedIdx = 3; cam = instant ? Cam.Chase : Cam.TV; snapCam = true; scrubbing = false;
            directorNext = 0f; manualCarUntil = 0f; tvSpot = -1; splineHint = -1;
            BuildTvSpots();
            playing = true;
            BuildUI();
            ApplyCars(playT);
        }

        void Exit()
        {
            if (!playing) return;
            playing = false;
            exitFrame = Time.frameCount; // a tecla ESC deste quadro não abre a pausa
            for (int k = 0; k < nCars; k++)
            {
                var c = carList[k];
                if (c == null) continue;
                c.transform.SetPositionAndRotation(savedPos[k], savedRot[k]);
                if (c.Body != null) c.Body.interpolation = savedInterp[k];
                if (visuals[k] != null && visuals[k].driverHead != null) visuals[k].driverHead.SetActive(savedHead[k]);
                c.throttleInput = savedThrottle[k]; c.brakeInput = savedBrake[k];
            }
            ClearDamageVisuals();
            for (int k = 0; k < nCars; k++) if (carList[k] != null && carList[k].damage != null) carList[k].damage.ReplayRestoreLive();
            foreach (var g in liveDebrisHidden) if (g != null) g.SetActive(true);
            liveDebrisHidden.Clear();
            foreach (var b in disabled) if (b != null) b.enabled = true;
            disabled.Clear();
            if (camRef != null)
            {
                camRef.transform.SetPositionAndRotation(camPos0, camRot0);
                camRef.fieldOfView = camFov0; camRef.rect = camRect0;
            }
            if (ui != null) Destroy(ui.gameObject);
            ui = null;
            EngineAudio.ReplayVolume = 1f; EngineAudio.ReplayPitch = 1f;
            Time.timeScale = prevTimeScale;
            AudioListener.pause = prevAudioPause;
            recording = recordingWas;
            var cb = onExit; onExit = null;
            cb?.Invoke();
        }

        void Update()
        {
            if (!playing)
            {
                if (race == null) { if (count > 0) { chunks.Clear(); count = 0; events.Clear(); nCars = 0; } recording = false; return; }
                if (!recording) return;
                if (stopAt > 0f && Time.time > stopAt) { recording = false; return; }
                Store();
                return;
            }
            if (race == null) { Exit(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Keys();
            if (!scrubbing && running)
            {
                playT += dir * Speeds[speedIdx] * dt;
                if (playT >= endT) { playT = endT; running = false; }
                else if (playT <= 0f) { playT = 0f; running = false; }
            }
            ApplyCars(playT);
            CheckToast();
            RefreshUI();
        }

        void LateUpdate()
        {
            if (!playing) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            ApplyWheels();
            UpdateCamera(dt);
            prevPlayT = playT;
        }

        int Find(float t)
        {
            int lo = 0, hi = count - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (T(mid) <= t) lo = mid; else hi = mid - 1;
            }
            return Mathf.Clamp(lo, 0, Mathf.Max(0, count - 2));
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        void ApplyCars(float tRel)
        {
            if (count < 2) return;
            float t = T(0) + tRel;
            int i = Find(t);
            float t0 = T(i), t1 = T(i + 1);
            float f = Mathf.Clamp01((t - t0) / Mathf.Max(1e-4f, t1 - t0));
            curSample = f < 0.5f ? i : i + 1;
            for (int k = 0; k < nCars; k++)
            {
                var c = carList[k];
                if (c == null) continue;
                Vector3 p1 = P(i, k), p2 = P(i + 1, k), pos;
                if ((p2 - p1).sqrMagnitude > GapSnap * GapSnap) pos = f < 0.5f ? p1 : p2;
                else
                {
                    Vector3 p0 = i > 0 ? P(i - 1, k) : p1, p3 = i + 2 < count ? P(i + 2, k) : p2;
                    if ((p1 - p0).sqrMagnitude > GapSnap * GapSnap) p0 = p1;
                    if ((p3 - p2).sqrMagnitude > GapSnap * GapSnap) p3 = p2;
                    pos = CatmullRom(p0, p1, p2, p3, f);
                }
                c.transform.SetPositionAndRotation(pos, Quaternion.Slerp(R(i, k), R(i + 1, k), f));
                float limit = c.spec != null ? Mathf.Max(1f, c.spec.rpmLimit) : 8000f;
                // Giro e acelerador INTERPOLADOS entre as amostras (10 Hz): antes vinha a amostra mais próxima e o som
                // subia em degraus. Queda grande de giro (troca de marcha) continua em degrau, no meio do intervalo.
                float rA = Info(i, k, 1), rB = Info(i + 1, k, 1);
                float rpmB = rA - rB > 40f ? (f < 0.5f ? rA : rB) : Mathf.Lerp(rA, rB, f);
                c.SetReplayRpm(rpmB / 255f * limit);
                c.throttleInput = Mathf.Lerp(Info(i, k, 5), Info(i + 1, k, 5), f) / 255f;
            }
            try { ApplyDamage(t); } catch (Exception ex) { Debug.LogWarning("[Replay] dano: " + ex.Message); }
            EngineAudio.ReplayPitch = Mathf.Clamp(Speeds[speedIdx], 0.5f, 1.5f);
            EngineAudio.ReplayVolume = running && !scrubbing ? 1f : 0f;
        }

        void OnDamage(CarController c, int zone)
        {
            if (!recording || playing || c == null) return;
            int k = IndexOf(c);
            if (k < 0) return;
            var ev = new DamageEv { t = Time.time, car = k, zone = zone, vel = c.Velocity, pos = c.transform.position, rot = c.transform.rotation };
            if (zone >= 0 && c.damage != null)
            {
                var list = c.damage.ZoneParts(zone);
                ev.parts = list.ToArray();
                ev.local = new Matrix4x4[ev.parts.Length];
                var inv = c.transform.worldToLocalMatrix;
                for (int i = 0; i < ev.parts.Length; i++)
                    if (ev.parts[i] != null) ev.local[i] = inv * ev.parts[i].transform.localToWorldMatrix;
                // giro do destroço: fixo por evento (o mesmo a cada vez que o replay passa por ali)
                var rnd = new System.Random(k * 31 + zone * 7 + damageEvs.Count);
                ev.spin = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f) * 360f;
            }
            damageEvs.Add(ev);
        }

        /// <summary>Carros como estavam no instante t: peça solta só depois da batida (e de volta depois do conserto),
        /// destroço voando a partir da batida (sobe, cai, quica e arrasta até parar — conta pelo tempo do replay, então
        /// funciona para frente, para trás e em câmera lenta).</summary>
        void ApplyDamage(float t)
        {
            if (damageEvs.Count == 0) return;
            for (int k = 0; k < nCars; k++)
            {
                var c = carList[k];
                if (c == null || c.damage == null) continue;
                for (int z = 0; z < CarDamage.ZoneCount; z++)
                {
                    bool off = false;
                    foreach (var ev in damageEvs)
                    {
                        if (ev.car != k || ev.t > t) continue;
                        if (ev.zone == z) off = true;
                        else if (ev.zone < 0) off = false;   // conserto depois da batida
                    }
                    ShowZone(k, z, off);
                }
            }
            foreach (var ev in damageEvs)
            {
                if (ev.zone < 0 || ev.parts == null || ev.parts.Length == 0) continue;
                float dt = t - ev.t;
                bool fly = dt >= 0f && dt < 8f;
                if (!fly) { if (ev.visual != null && ev.visual.activeSelf) ev.visual.SetActive(false); continue; }
                if (ev.visual == null) ev.visual = BuildDebrisVisual(ev);
                if (ev.visual == null) continue;
                if (!ev.visual.activeSelf) ev.visual.SetActive(true);
                Vector3 v0 = ev.vel * 0.4f + Vector3.up * 3f;
                float ground = ev.pos.y - 0.3f;
                // voo balístico até o chão, depois arrasta (6 m/s²) até parar
                float tl = (v0.y + Mathf.Sqrt(v0.y * v0.y + 2f * 9.81f * Mathf.Max(0f, ev.pos.y - ground))) / 9.81f;
                Vector3 hv = new Vector3(v0.x, 0f, v0.z);
                Vector3 p;
                float spinT;
                if (dt <= tl)
                {
                    p = ev.pos + v0 * dt + 0.5f * Physics.gravity * dt * dt;
                    spinT = dt;
                }
                else
                {
                    float sl = dt - tl, sp = hv.magnitude, stop = sp / 6f;
                    float u = Mathf.Min(sl, stop);
                    Vector3 land = ev.pos + hv * tl; land.y = ground;
                    p = land + (sp > 0.01f ? hv / sp : Vector3.zero) * (sp * u - 0.5f * 6f * u * u);
                    spinT = tl + u * 0.5f;
                }
                ev.visual.transform.SetPositionAndRotation(p, ev.rot * Quaternion.Euler(ev.spin * spinT));
            }
        }

        void ShowZone(int k, int zone, bool off)
        {
            // as peças desta zona que algum evento gravou (mesmo que o conserto já tenha limpado a lista do carro)
            foreach (var ev in damageEvs)
                if (ev.car == k && ev.zone == zone && ev.parts != null)
                    foreach (var r in ev.parts) if (r != null && r.enabled == off) r.enabled = !off;
        }

        GameObject BuildDebrisVisual(DamageEv ev)
        {
            try
            {
                var g = new GameObject("ReplayDebris");
                for (int i = 0; i < ev.parts.Length; i++)
                {
                    var r = ev.parts[i];
                    var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                    if (mf == null || mf.sharedMesh == null) continue;
                    var piece = new GameObject("p");
                    piece.transform.SetParent(g.transform, false);
                    var m = ev.local[i];
                    piece.transform.localPosition = m.GetColumn(3);
                    piece.transform.localRotation = m.rotation;
                    piece.transform.localScale = m.lossyScale;
                    piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var mr = piece.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = r.sharedMaterials;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                g.SetActive(false);
                return g;
            }
            catch (Exception ex) { Debug.LogWarning("[Replay] destroço ignorado: " + ex.Message); return null; }
        }

        void ClearDamageVisuals()
        {
            foreach (var ev in damageEvs) if (ev.visual != null) { Destroy(ev.visual); ev.visual = null; }
        }

        void ApplyWheels()
        {
            float dT = playT - prevPlayT;
            for (int k = 0; k < nCars; k++)
            {
                var c = carList[k];
                if (c == null) continue;
                float ms = Info(curSample, k, 0) * 2f / 3.6f;
                float steer = (Info(curSample, k, 2) - 128f) * 0.5f;
                wheelSpin[k] = Mathf.Repeat(wheelSpin[k] + ms / Mathf.Max(0.2f, wheelR[k]) * Mathf.Rad2Deg * dT, 360f);
                var tr = c.transform;
                for (int w = 0; w < 4 && w < c.wheelMeshes.Length; w++)
                {
                    if (!wheelOk[k, w] || c.wheelMeshes[w] == null) continue;
                    Quaternion steerQ = w < 2 ? Quaternion.Euler(0f, steer - steer0[k], 0f) : Quaternion.identity;
                    c.wheelMeshes[w].SetPositionAndRotation(tr.TransformPoint(wheelLp[k, w]), tr.rotation * steerQ * wheelLr[k, w] * Quaternion.Euler(wheelSpin[k], 0f, 0f));
                }
            }
        }

        // ---------------- Câmeras ----------------
        void BuildTvSpots()
        {
            tvSpotPos.Clear(); tvSpotIdx.Clear();
            var sp = race != null && race.Track != null ? race.Track.Spline : null;
            if (sp == null || sp.Count < 20) return;
            float width = race.Track.Definition != null ? race.Track.Definition.roadWidth : 12f;
            float spacing = Mathf.Max(0.5f, sp.spacing);
            int step = Mathf.Max(5, Mathf.RoundToInt(110f / spacing));
            int look = Mathf.Max(2, Mathf.RoundToInt(20f / spacing));
            for (int i = 0; i < sp.Count; i += step)
            {
                Vector3 ta = sp.tangents[sp.Wrap(i - look)], tb = sp.tangents[sp.Wrap(i + look)];
                float turn = Vector3.Cross(ta, tb).y;      // > 0: curva para a direita
                float side = turn > 0f ? -1f : 1f;          // câmera do lado de FORA da curva
                tvSpotPos.Add(sp.points[i] + sp.rights[i] * side * (width * 0.5f + 11f) + Vector3.up * 5.5f);
                tvSpotIdx.Add(i);
            }
        }

        Vector3 TvSpot(Vector3 carPos)
        {
            var sp = race.Track != null ? race.Track.Spline : null;
            if (sp == null || tvSpotPos.Count == 0) return carPos + Vector3.up * 25f - Vector3.forward * 20f;
            int ci = sp.ClosestIndex(carPos, snapCam ? -1 : splineHint);
            splineHint = ci;
            int n = sp.Count, back = Mathf.RoundToInt(35f / Mathf.Max(0.5f, sp.spacing));
            int best = -1, bestD = int.MaxValue;
            for (int s = 0; s < tvSpotIdx.Count; s++)
            {
                int d = ((tvSpotIdx[s] - ci) % n + n) % n;
                if (d > n / 2) d -= n;
                if (d < -back) continue;            // já passou (fica filmando por trás até 35 m)
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best < 0) best = 0;
            if (best != tvSpot) { tvSpot = best; snapCam = true; } // corte para o próximo poste
            return tvSpotPos[best];
        }

        CarController TargetCar()
        {
            if (target >= 0 && target < nCars && carList[target] != null) return carList[target];
            for (int k = 0; k < nCars; k++) if (carList[k] != null) { target = k; return carList[k]; }
            return null;
        }

        void ShowHeads(int hideFor)
        {
            for (int k = 0; k < nCars; k++)
            {
                var v = visuals[k];
                if (v == null || v.driverHead == null) continue;
                bool show = k != hideFor;
                if (v.driverHead.activeSelf != show) v.driverHead.SetActive(show);
            }
        }

        void UpdateCamera(float dt)
        {
            if (camRef == null) return;
            if (cam == Cam.Director) DirectorTick();
            var c = TargetCar();
            if (c == null) return;
            Cam mode = cam == Cam.Director ? dirCam : cam;
            var tr = c.transform;
            var vis = visuals[target];
            bool inside = mode == Cam.Onboard || mode == Cam.Hood || mode == Cam.Wheel;
            ShowHeads(inside ? target : -1);
            Vector3 carPos = tr.position;
            Vector3 flat = Vector3.ProjectOnPlane(tr.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            flat.Normalize();

            if (mode == Cam.TV)
            {
                Vector3 spot = TvSpot(carPos);
                Vector3 look = carPos + Vector3.up * 0.6f - spot;
                float dist = Mathf.Max(1f, look.magnitude);
                Quaternion wantTv = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : flat);
                float fovTv = Mathf.Clamp(2f * Mathf.Atan(6.5f / dist) * Mathf.Rad2Deg, 5f, 55f); // zoom: o carro com o mesmo tamanho na tela
                smoothRot = snapCam ? wantTv : Quaternion.Slerp(smoothRot, wantTv, 1f - Mathf.Exp(-dt * 12f));
                camRef.transform.SetPositionAndRotation(spot, smoothRot);
                camRef.fieldOfView = snapCam ? fovTv : Mathf.Lerp(camRef.fieldOfView, fovTv, 1f - Mathf.Exp(-dt * 6f));
                smoothPos = spot; snapCam = false;
                return;
            }
            if (mode == Cam.Rear)
            {
                // Olhando para TRÁS a partir do bico (quem vem atrás, como o "olhar para trás" na pista).
                Vector3 rp = carPos + flat * 2.4f + Vector3.up * 1.25f;
                Vector3 rl = carPos - flat * 14f + Vector3.up * 0.8f;
                camRef.transform.SetPositionAndRotation(rp, Quaternion.LookRotation(rl - rp, Vector3.up));
                camRef.fieldOfView = 70f;
                smoothPos = rp; smoothRot = camRef.transform.rotation; snapCam = false;
                return;
            }
            if (mode == Cam.Onboard || mode == Cam.Hood || mode == Cam.Wheel || mode == Cam.TCam)
            {
                Vector3 ap = tr.position + tr.up * 1f;
                float pitch = 0f, fovIn = 80f;
                if (mode == Cam.Onboard) { if (vis != null && vis.eyeAnchor != null) ap = vis.eyeAnchor.position; pitch = -3f; fovIn = 80f; }
                else if (mode == Cam.Wheel) { if (vis != null && vis.wheelCamAnchor != null) ap = vis.wheelCamAnchor.position; pitch = -19f; fovIn = 75f; }
                else if (mode == Cam.Hood) { if (vis != null && vis.hoodAnchor != null) ap = vis.hoodAnchor.position; pitch = 2f; fovIn = 85f; }
                else { ap = (vis != null && vis.eyeAnchor != null ? vis.eyeAnchor.position : tr.position + tr.up) + tr.up * 0.55f - tr.forward * 0.45f; pitch = 7f; fovIn = 72f; }
                camRef.transform.SetPositionAndRotation(ap, tr.rotation * Quaternion.Euler(pitch, 0f, 0f));
                camRef.fieldOfView = fovIn;
                smoothPos = ap; smoothRot = camRef.transform.rotation; snapCam = false;
                return;
            }
            Vector3 want2, lookAt;
            float fov = 62f, rate = 7f;
            switch (mode)
            {
                case Cam.Heli:
                    want2 = carPos - flat * 28f + Vector3.up * 30f; lookAt = carPos + flat * 6f; fov = 45f; rate = 4f; break;
                case Cam.Free:
                    float yaw = Quaternion.LookRotation(flat).eulerAngles.y + freeYaw;
                    want2 = carPos + Quaternion.Euler(freePitch, yaw, 0f) * (Vector3.back * freeDist) + Vector3.up * 0.4f;
                    lookAt = carPos + Vector3.up * 0.6f; fov = 58f; rate = 14f; break;
                default: // perseguição
                    want2 = carPos - flat * 7.8f + Vector3.up * 2.6f; lookAt = carPos + flat * 3f + Vector3.up * 0.8f; break;
            }
            float a = snapCam ? 1f : 1f - Mathf.Exp(-dt * rate);
            smoothPos = Vector3.Lerp(smoothPos, want2, a);
            Vector3 dirLook = lookAt - smoothPos;
            Quaternion want = Quaternion.LookRotation(dirLook.sqrMagnitude > 0.01f ? dirLook : flat);
            smoothRot = snapCam ? want : Quaternion.Slerp(smoothRot, want, 1f - Mathf.Exp(-dt * 14f));
            camRef.transform.SetPositionAndRotation(smoothPos, smoothRot);
            camRef.fieldOfView = snapCam ? fov : Mathf.Lerp(camRef.fieldOfView, fov, a);
            snapCam = false;
        }

        /// <summary>DIRETOR: troca de câmera a cada 6–9 s e procura a briga mais à frente (carros colados, posições seguidas).</summary>
        void DirectorTick()
        {
            float now = Time.unscaledTime;
            if (now < directorNext) return;
            directorNext = now + UnityEngine.Random.Range(6f, 9f);
            Cam[] seq = { Cam.TV, Cam.Chase, Cam.TV, Cam.Onboard, Cam.Heli, Cam.TV, Cam.TCam };
            dirCam = seq[dirIdx++ % seq.Length];
            if (now >= manualCarUntil) { int b = BattleCar(); if (b >= 0) target = b; }
            snapCam = true;
        }

        int BattleCar()
        {
            int best = -1, bestScore = int.MaxValue;
            for (int k = 0; k < nCars; k++)
            {
                if (carList[k] == null) continue;
                int pos = Info(curSample, k, 3);
                if (pos <= 1 || (Info(curSample, k, 4) & 6) != 0) continue;
                for (int j = 0; j < nCars; j++)
                {
                    if (j == k || carList[j] == null || Info(curSample, j, 3) != pos - 1) continue;
                    if ((carList[j].transform.position - carList[k].transform.position).sqrMagnitude > 30f * 30f) continue;
                    bool human = entryOf[k] != null && entryOf[k].isHuman || entryOf[j] != null && entryOf[j].isHuman;
                    int score = pos - (human ? 3 : 0);
                    if (score < bestScore) { bestScore = score; best = k; }
                }
            }
            return best;
        }

        // ---------------- Controles ----------------
        void TogglePlay()
        {
            if (!running && dir > 0 && playT >= endT - 0.05f) Seek(0f);
            else if (!running && dir < 0 && playT <= 0.05f) Seek(endT);
            running = !running;
        }

        void Seek(float t)
        {
            float nt = Mathf.Clamp(t, 0f, endT);
            if (Mathf.Abs(nt - playT) > 1f) { snapCam = true; tvSpot = -1; }
            playT = nt;
            prevPlayT = nt;
        }

        void Step(int frames) { running = false; Seek(playT + frames / 30f); }

        void Jump(int direction)
        {
            float t0 = T(0);
            if (direction > 0)
            {
                foreach (var ev in events)
                {
                    if (ev.kind == EvKind.Lap) continue;
                    float rel = ev.t - t0 - 3f;
                    if (rel > playT + 0.2f) { GoTo(ev, rel); return; }
                }
                Toast("SEM MAIS LANCES ADIANTE");
            }
            else
            {
                for (int i = events.Count - 1; i >= 0; i--)
                {
                    var ev = events[i];
                    if (ev.kind == EvKind.Lap) continue;
                    float rel = ev.t - t0 - 3f;
                    if (rel < playT - 0.5f) { GoTo(ev, rel); return; }
                }
                Toast("NENHUM LANCE ANTES");
            }
        }

        void GoTo(ReplayEvent ev, float rel)
        {
            Seek(Mathf.Max(0f, rel));
            if (ev.car >= 0 && ev.car < nCars && carList[ev.car] != null) { target = ev.car; manualCarUntil = Time.unscaledTime + 20f; snapCam = true; }
            Toast(ev.text);
        }

        void NextCar(int d)
        {
            for (int s = 0; s < nCars; s++)
            {
                target = ((target + d) % nCars + nCars) % nCars;
                if (carList[target] != null) break;
            }
            manualCarUntil = Time.unscaledTime + 20f;
            snapCam = true; tvSpot = -1;
        }

        void NextCam(int d)
        {
            int n = CamNames.Length;
            cam = (Cam)((((int)cam + d) % n + n) % n);
            snapCam = true; tvSpot = -1; directorNext = 0f;
        }

        internal void BeginScrub() { wasRunning = running; running = false; scrubbing = true; }
        internal void EndScrub() { scrubbing = false; running = wasRunning; }
        internal void SeekNorm(float n) => Seek(Mathf.Clamp01(n) * endT);

        internal void OnFreeDrag(Vector2 delta)
        {
            if (cam != Cam.Free) { cam = Cam.Free; snapCam = true; }
            freeYaw += delta.x * 0.25f;
            freePitch = Mathf.Clamp(freePitch - delta.y * 0.2f, 2f, 80f);
        }

        internal void OnBackgroundTap() { if (!uiVisible) SetUiVisible(true); }

        void Keys()
        {
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) TogglePlay();
                if (UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow)) Seek(playT - 5f);
                if (UnityEngine.Input.GetKeyDown(KeyCode.RightArrow)) Seek(playT + 5f);
                if (UnityEngine.Input.GetKeyDown(KeyCode.Comma)) Step(-1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.Period)) Step(1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow)) speedIdx = Mathf.Min(Speeds.Length - 1, speedIdx + 1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow)) speedIdx = Mathf.Max(0, speedIdx - 1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.R)) dir = -dir;
                if (UnityEngine.Input.GetKeyDown(KeyCode.C)) NextCar(1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.V)) NextCam(1);
                if (UnityEngine.Input.GetKeyDown(KeyCode.H)) SetUiVisible(!uiVisible);
                float wheel = UnityEngine.Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && cam == Cam.Free) freeDist = Mathf.Clamp(freeDist * (1f - wheel * 0.1f), 3f, 60f);
                if (UnityEngine.Input.touchCount == 2)
                {
                    float d = (UnityEngine.Input.GetTouch(0).position - UnityEngine.Input.GetTouch(1).position).magnitude;
                    if (lastPinch > 0f)
                    {
                        if (cam != Cam.Free) { cam = Cam.Free; snapCam = true; }
                        freeDist = Mathf.Clamp(freeDist * lastPinch / Mathf.Max(1f, d), 3f, 60f);
                    }
                    lastPinch = d;
                }
                else lastPinch = -1f;
            }
            catch (Exception) { } // projeto só com o Input System novo: os botões da tela continuam valendo
        }

        // ---------------- Interface ----------------
        Canvas ui;
        RectTransform chrome, hiddenBar, barRect, fillRect, handleRect, toastPanel;
        Text timeLabel, lapLabel, carLabel, camLabel, speedLabel, playLabel, revLabel, toastLabel;
        Image revImage;
        readonly List<Text> towerLeft = new List<Text>();
        readonly List<Text> towerRight = new List<Text>();
        readonly List<Image> towerBg = new List<Image>();
        readonly List<int> towerCar = new List<int>();
        bool uiVisible = true;
        float nextTowerRefresh;

        static readonly Color Orange = new Color(1f, 0.55f, 0.1f, 1f);

        Text Btn(RectTransform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, int font, Action act)
        {
            var b = MenuTheme.StyledButton(parent, name, label, anchor, new Vector2(0.5f, 0.5f), pos, size, font, act);
            return b != null ? b.GetComponentInChildren<Text>(true) : null;
        }

        void BuildUI()
        {
            UIFactory.EnsureEventSystem();
            ui = UIFactory.CreateCanvas("ReplayCanvas", 900);
            ui.transform.SetParent(transform, false);
            var root = UIFactory.Stretch("Safe", ui.transform);
            UIFactory.ApplySafeArea(root, new Rect(0f, 0f, 1f, 1f));

            // Fundo de toque: arrastar gira a câmera LIVRE; com a interface oculta, um toque a traz de volta.
            var touch = UIFactory.Stretch("Touch", root);
            var timg = touch.gameObject.AddComponent<Image>();
            timg.color = new Color(0f, 0f, 0f, 0f);
            timg.raycastTarget = true;
            touch.gameObject.AddComponent<ReplayTouch>().owner = this;

            chrome = UIFactory.Stretch("Chrome", root);
            var top = new Vector2(0.5f, 1f);
            var bottom = new Vector2(0.5f, 0f);

            // ---- Barra superior ----
            var topBar = UIFactory.Rect("TopBar", chrome, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 104f));
            topBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            topBar.GetComponent<Image>().raycastTarget = false;
            UIFactory.Label("Tag", topBar, "REPLAY", 30, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 18f), new Vector2(220f, 40f), Orange);
            timeLabel = UIFactory.Label("Time", topBar, "", 24, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, -20f), new Vector2(360f, 34f), Color.white);
            Btn(topBar, "CarPrev", "◄", top, new Vector2(-360f, -52f), new Vector2(92f, 78f), 34, () => NextCar(-1));
            carLabel = UIFactory.Label("Car", topBar, "", 28, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -30f), new Vector2(600f, 44f), Color.white);
            UIFactory.Label("CarHint", topBar, "carro seguido · toque na torre para trocar", 18, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -72f), new Vector2(600f, 26f), new Color(1f, 1f, 1f, 0.6f));
            Btn(topBar, "CarNext", "►", top, new Vector2(360f, -52f), new Vector2(92f, 78f), 34, () => NextCar(1));
            var right = new Vector2(1f, 1f);
            camLabel = Btn(topBar, "Cam", "CÂMERA: TV", right, new Vector2(-640f, -52f), new Vector2(330f, 78f), 26, () => NextCam(1));
            Btn(topBar, "Hide", "OCULTAR", right, new Vector2(-360f, -52f), new Vector2(200f, 78f), 26, () => SetUiVisible(false));
            Btn(topBar, "Exit", "SAIR", right, new Vector2(-140f, -52f), new Vector2(200f, 78f), 26, Exit);

            // ---- Torre de posições DO INSTANTE (toque = seguir o carro) ----
            for (int r = 0; r < 11; r++)
            {
                int row = r;
                var b = UIFactory.Button("Tower" + r, chrome, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -122f - r * 46f), new Vector2(380f, 42f), () => { if (row < towerCar.Count) { target = towerCar[row]; manualCarUntil = Time.unscaledTime + 20f; snapCam = true; tvSpot = -1; } }, 20);
                var img = b.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.55f);
                var lt = b.GetComponentInChildren<Text>(true);
                lt.alignment = TextAnchor.MiddleLeft;
                ((RectTransform)lt.transform).sizeDelta = new Vector2(340f, 42f);
                ((RectTransform)lt.transform).anchoredPosition = new Vector2(6f, 0f);
                var rt = UIFactory.Label("R", b.transform, "", 18, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(120f, 42f), new Color(1f, 1f, 1f, 0.8f));
                towerLeft.Add(lt); towerRight.Add(rt); towerBg.Add(img);
            }

            // ---- Aviso do lance ----
            var tp = UIFactory.Panel("Toast", chrome, bottom, bottom, new Vector2(0f, 262f), new Vector2(1100f, 56f), new Color(0f, 0f, 0f, 0.7f));
            toastPanel = (RectTransform)tp.transform;
            toastLabel = UIFactory.Label("T", toastPanel, "", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 56f), Color.white);
            toastPanel.gameObject.SetActive(false);

            // ---- Linha do tempo (arrastável) ----
            var barBg = UIFactory.Rect("TimelineBg", chrome, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(-120f, 92f));
            barBg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            lapLabel = UIFactory.Label("Lap", barBg, "", 22, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -4f), new Vector2(500f, 30f), Color.white);
            UIFactory.Label("Legend", barBg, "<color=#ff5544>■</color> toque  <color=#ff2020>■</color> PT  <color=#5599ff>■</color> box  <color=#ffd633>■</color> direção  <color=#55e066>■</color> ultrapassagem  <color=#b066ff>■</color> volta rápida", 17, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -4f), new Vector2(1100f, 30f), new Color(1f, 1f, 1f, 0.85f)).supportRichText = true;
            barRect = UIFactory.Rect("Bar", barBg, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-48f, 34f));
            var bimg = barRect.gameObject.AddComponent<Image>();
            bimg.color = new Color(1f, 1f, 1f, 0.16f);
            bimg.raycastTarget = true;
            var scrub = barRect.gameObject.AddComponent<ReplayScrub>();
            scrub.owner = this; scrub.bar = barRect;
            fillRect = UIFactory.Rect("Fill", barRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 0f));
            var fimg = fillRect.gameObject.AddComponent<Image>(); fimg.color = new Color(1f, 0.55f, 0.1f, 0.55f); fimg.raycastTarget = false;
            BuildMarkers();
            handleRect = UIFactory.Rect("Handle", barRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 50f));
            var himg = handleRect.gameObject.AddComponent<Image>(); himg.color = Color.white; himg.raycastTarget = false;

            // ---- Controles de reprodução ----
            float[] widths = { 150f, 120f, 170f, 150f, 180f, 170f, 120f, 150f, 180f, 180f };
            float gap = 12f, total = -gap;
            foreach (var w in widths) total += w + gap;
            float x = -total * 0.5f;
            int idx = 0;
            Vector2 NextPos() { float w = widths[idx]; var p = new Vector2(x + w * 0.5f, 62f); x += w + gap; return p; }
            Vector2 Sz() => new Vector2(widths[idx++], 84f);
            Btn(chrome, "Start", "INÍCIO", bottom, NextPos(), Sz(), 24, () => Seek(0f));
            Btn(chrome, "Back10", "-10s", bottom, NextPos(), Sz(), 26, () => Seek(playT - 10f));
            Btn(chrome, "FrameB", "◄ QUADRO", bottom, NextPos(), Sz(), 22, () => Step(-1));
            revLabel = Btn(chrome, "Rev", "◄◄ RÉ", bottom, NextPos(), Sz(), 24, () => { dir = -dir; if (!running) running = true; });
            revImage = revLabel != null ? revLabel.transform.parent.GetComponent<Image>() : null;
            playLabel = Btn(chrome, "Play", "II", bottom, NextPos(), Sz(), 36, TogglePlay);
            Btn(chrome, "FrameF", "QUADRO ►", bottom, NextPos(), Sz(), 22, () => Step(1));
            Btn(chrome, "Fwd10", "+10s", bottom, NextPos(), Sz(), 26, () => Seek(playT + 10f));
            speedLabel = Btn(chrome, "Speed", "VEL 1x", bottom, NextPos(), Sz(), 24, () => speedIdx = (speedIdx + 1) % Speeds.Length);
            Btn(chrome, "EvPrev", "◄ LANCE", bottom, NextPos(), Sz(), 24, () => Jump(-1));
            Btn(chrome, "EvNext", "LANCE ►", bottom, NextPos(), Sz(), 24, () => Jump(1));

            // Interface oculta: só um botão discreto para voltar (ou toque em qualquer lugar).
            hiddenBar = UIFactory.Rect("HiddenBar", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 20f), new Vector2(220f, 70f));
            MenuTheme.StyledButton(hiddenBar, "Show", "MOSTRAR", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 70f), 24, () => SetUiVisible(true));
            hiddenBar.gameObject.SetActive(false);
            uiVisible = true;
            nextTowerRefresh = 0f;
            RefreshUI();
        }

        void BuildMarkers()
        {
            if (count < 2 || endT <= 0.01f) return;
            float t0 = T(0);
            int nonLap = 0;
            foreach (var ev in events) if (ev.kind != EvKind.Lap) nonLap++;
            bool skipOvertakes = nonLap > 400;
            int lapMarks = 0;
            foreach (var ev in events) if (ev.kind == EvKind.Lap) lapMarks++;
            int labelEvery = lapMarks > 60 ? 10 : lapMarks > 20 ? 5 : 1;
            int lapN = 0;
            foreach (var ev in events)
            {
                float u = Mathf.Clamp01((ev.t - t0) / endT);
                if (ev.kind == EvKind.Lap)
                {
                    lapN++;
                    var m = UIFactory.Rect("L", barRect, new Vector2(u, 0f), new Vector2(u, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 0f));
                    var im = m.gameObject.AddComponent<Image>(); im.color = new Color(1f, 1f, 1f, 0.35f); im.raycastTarget = false;
                    if ((lapN + 1) % labelEvery == 0)
                        UIFactory.Label("LN", barRect, "V" + (lapN + 1), 14, TextAnchor.MiddleCenter, new Vector2(u, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -1f), new Vector2(40f, 16f), new Color(1f, 1f, 1f, 0.7f));
                    continue;
                }
                if (skipOvertakes && ev.kind == EvKind.Overtake) continue;
                Color col = ev.kind == EvKind.Contact ? new Color(1f, 0.33f, 0.27f) : ev.kind == EvKind.Totaled ? new Color(1f, 0.12f, 0.12f)
                    : ev.kind == EvKind.Retired ? new Color(0.65f, 0.65f, 0.65f) : ev.kind == EvKind.Pit ? new Color(0.33f, 0.6f, 1f)
                    : ev.kind == EvKind.Notice ? new Color(1f, 0.84f, 0.2f) : ev.kind == EvKind.Overtake ? new Color(0.33f, 0.88f, 0.4f)
                    : ev.kind == EvKind.FastLap ? new Color(0.69f, 0.4f, 1f) : Color.white;
                bool big = ev.kind == EvKind.Totaled || ev.kind == EvKind.Contact || ev.kind == EvKind.Start || ev.kind == EvKind.Finish;
                var mk = UIFactory.Rect("E", barRect, new Vector2(u, 1f), new Vector2(u, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(big ? 8f : 6f, big ? 16f : 11f));
                var mi = mk.gameObject.AddComponent<Image>(); mi.color = col; mi.raycastTarget = false;
            }
        }

        void SetUiVisible(bool v)
        {
            uiVisible = v;
            if (chrome != null) chrome.gameObject.SetActive(v);
            if (hiddenBar != null) hiddenBar.gameObject.SetActive(!v);
        }

        void Toast(string text)
        {
            toastText = text;
            toastUntil = Time.unscaledTime + 3.5f;
        }

        void CheckToast()
        {
            if (running && !scrubbing && dir > 0 && playT > prevPlayT)
            {
                float t0 = T(0);
                foreach (var ev in events)
                {
                    if (ev.kind == EvKind.Lap) continue;
                    float rel = ev.t - t0;
                    if (rel > prevPlayT && rel <= playT) Toast(ev.text);
                }
            }
            if (toastPanel != null)
            {
                bool show = Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toastText);
                if (toastPanel.gameObject.activeSelf != show) toastPanel.gameObject.SetActive(show);
                if (show && toastLabel.text != toastText) toastLabel.text = toastText;
            }
        }

        static string Fmt(float s)
        {
            s = Mathf.Max(0f, s);
            int m = (int)(s / 60f);
            float r = s - m * 60f;
            return m + ":" + r.ToString("00.0", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        static string LapFmt(float s)
        {
            int m = (int)(s / 60f);
            float r = s - m * 60f;
            return m + ":" + r.ToString("00.000", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        void RefreshUI()
        {
            if (ui == null) return;
            if (timeLabel != null) timeLabel.text = Fmt(playT) + " / " + Fmt(endT);
            if (lapLabel != null)
            {
                float t0 = T(0);
                int laps = 0;
                foreach (var ev in events) if (ev.kind == EvKind.Lap && ev.t - t0 <= playT) laps++;
                int total = race != null && race.Settings != null ? race.Settings.laps : 0;
                lapLabel.text = Timed ? "SUA VOLTA " + (laps + 1) : "VOLTA " + Mathf.Clamp(laps + 1, 1, Mathf.Max(1, total)) + (total > 0 ? "/" + total : "");
            }
            SetLabel(playLabel, running ? "II" : "►");
            SetLabel(speedLabel, "VEL " + SpeedNames[speedIdx]);
            SetLabel(camLabel, cam == Cam.Director ? "DIRETOR: " + CamNames[(int)dirCam] : "CÂMERA: " + CamNames[(int)cam]);
            if (revImage != null) revImage.color = dir < 0 ? new Color(1f, 0.55f, 0.1f, 0.85f) : MenuTheme.Panel; // RÉ ligada = botão laranja
            if (carLabel != null && target >= 0 && target < nCars)
            {
                int pos = Info(curSample, target, 3), fl = Info(curSample, target, 4);
                string st = (fl & 2) != 0 ? "  ·  FORA" : (fl & 4) != 0 ? "  ·  BOX" : "";
                carLabel.text = (pos > 0 ? "P" + pos + "  " : "") + NameOf(target) + "  ·  " + (Info(curSample, target, 0) * 2) + " km/h" + st;
            }
            if (barRect != null && endT > 0f)
            {
                float u = Mathf.Clamp01(playT / endT);
                fillRect.anchorMax = new Vector2(u, 1f);
                fillRect.sizeDelta = Vector2.zero;
                handleRect.anchorMin = handleRect.anchorMax = new Vector2(u, 0.5f);
                handleRect.anchoredPosition = Vector2.zero;
            }
            if (Time.unscaledTime >= nextTowerRefresh) { nextTowerRefresh = Time.unscaledTime + 0.2f; RefreshTower(); }
        }

        static void SetLabel(Text t, string s)
        {
            if (t == null) return;
            string v = MenuTheme.Spaced(s);
            if (t.text != v) t.text = v;
        }

        void RefreshTower()
        {
            var order = new List<int>();
            for (int k = 0; k < nCars; k++) if (carList[k] != null && Info(curSample, k, 3) > 0) order.Add(k);
            order.Sort((a, b) => Info(curSample, a, 3).CompareTo(Info(curSample, b, 3)));
            towerCar.Clear();
            for (int i = 0; i < order.Count && towerCar.Count < 10; i++) towerCar.Add(order[i]);
            if (!towerCar.Contains(target) && target >= 0 && target < nCars && carList[target] != null) towerCar.Add(target);
            for (int r = 0; r < towerLeft.Count; r++)
            {
                bool on = r < towerCar.Count;
                var go = towerBg[r].gameObject;
                if (go.activeSelf != on) go.SetActive(on);
                if (!on) continue;
                int k = towerCar[r];
                int pos = Info(curSample, k, 3), fl = Info(curSample, k, 4);
                towerLeft[r].text = "P" + pos + "  " + NameOf(k);
                towerRight[r].text = (fl & 2) != 0 ? "FORA" : (fl & 4) != 0 ? "BOX" : (Info(curSample, k, 0) * 2) + " km/h";
                bool me = k == target;
                towerBg[r].color = me ? new Color(1f, 0.55f, 0.1f, 0.75f) : entryOf[k] != null && entryOf[k].isHuman ? new Color(0.1f, 0.3f, 0.6f, 0.7f) : new Color(0f, 0f, 0f, 0.55f);
            }
        }
    }

    /// <summary>Linha do tempo: tocar/arrastar leva o replay ao ponto (pausa enquanto arrasta).</summary>
    public class ReplayScrub : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        internal ReplaySystem owner;
        internal RectTransform bar;
        public void OnPointerDown(PointerEventData e) { if (owner == null) return; owner.BeginScrub(); Set(e); }
        public void OnDrag(PointerEventData e) { if (owner != null) Set(e); }
        public void OnPointerUp(PointerEventData e) { if (owner != null) owner.EndScrub(); }
        void Set(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(bar, e.position, e.pressEventCamera, out var lp))
                owner.SeekNorm(Mathf.InverseLerp(bar.rect.xMin, bar.rect.xMax, lp.x));
        }
    }

    /// <summary>Fundo do replay: arrastar gira a câmera LIVRE; com a interface oculta, um toque a mostra de novo.</summary>
    public class ReplayTouch : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        internal ReplaySystem owner;
        Vector2 down;
        bool dragged;
        public void OnPointerDown(PointerEventData e) { down = e.position; dragged = false; }
        public void OnDrag(PointerEventData e)
        {
            if (owner == null) return;
            if ((e.position - down).sqrMagnitude > 400f) dragged = true;
            if (dragged) owner.OnFreeDrag(e.delta);
        }
        public void OnPointerUp(PointerEventData e) { if (!dragged && owner != null) owner.OnBackgroundTap(); }
    }
}
