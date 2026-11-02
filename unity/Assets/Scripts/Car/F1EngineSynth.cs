using System;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Motor dos monopostos com o som do vídeo de referência (pit stop F1 2012), aprovado na prévia.
    /// WSOLA guiado pela rotação: cada pedaço de saída sai do trecho do vídeo em que o motor estava na MESMA
    /// rotação e é alinhado pela forma de onda com o anterior — mantém as linhas do motor (a versão com grãos de
    /// fase aleatória desmanchava o som). Medido no vídeo e reproduzido aqui:
    ///  - troca para cima: o volume cai para ~65% por ~12 ms e volta com um leve repique (vídeo: mínimo 36-53%,
    ///    no máximo ~15 ms abaixo de 60%); a rotação cai no mesmo instante (marcha nova);
    ///  - redução: "blip" de acelerador de 70 ms;
    ///  - alívio (pé fora): trecho do vídeo com o pé fora, volume 0,885 do acelerando;
    ///  - box: limitador real do vídeo (~9.000 rpm), volume 1,30 do acelerando;
    ///  - rodando devagar: ruído de pneu da chegada ao box, sem as linhas do motor.
    /// Carro do jogador: busca completa (2 estágios). IA: versão leve (sem busca) e só perto da câmera.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class F1EngineSynth : MonoBehaviour
    {
        // ---------------- dados compartilhados (carregados uma vez) ----------------
        static float[] src;
        static int srcRate;
        static bool tried;
        static float[] segFmin, segFmax;
        static AudioClip dcClip;

        static bool Load()
        {
            if (tried) return src != null;
            tried = true;
            try
            {
                var clip = Resources.Load<AudioClip>(F1EngineData.Clip);
                if (clip == null) { Debug.LogWarning("[F1ENGINE] clipe não encontrado: " + F1EngineData.Clip); return false; }
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var d = new float[clip.samples * clip.channels];
                if (!clip.GetData(d, 0)) { Debug.LogWarning("[F1ENGINE] GetData falhou (tipo de carga do clipe)"); return false; }
                if (clip.channels > 1)
                {
                    var m = new float[clip.samples];
                    for (int i = 0; i < m.Length; i++)
                    {
                        float s = 0f;
                        for (int c = 0; c < clip.channels; c++) s += d[i * clip.channels + c];
                        m[i] = s / clip.channels;
                    }
                    d = m;
                }
                src = d;
                srcRate = clip.frequency;
                int n = F1EngineData.Kind.Length;
                segFmin = new float[n]; segFmax = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var f in F1EngineData.F[i]) { lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f); }
                    segFmin[i] = lo; segFmax[i] = hi;
                }
                Debug.Log("[F1ENGINE] som do vídeo carregado: " + src.Length + " amostras a " + srcRate + " Hz, " + n + " trechos");
            }
            catch (Exception e) { Debug.LogWarning("[F1ENGINE] falha ao carregar: " + e.Message); src = null; }
            return src != null;
        }

        /// <summary>Clipe de 1 s com valor 1 (corrente contínua): a fonte toca isso e o filtro multiplica pelo motor —
        /// assim volume, distância e posição 3D do AudioSource continuam valendo.</summary>
        static AudioClip DcClip()
        {
            if (dcClip != null) return dcClip;
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            dcClip = AudioClip.Create("F1EngineDC", rate, 1, rate, false);
            var ones = new float[rate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            dcClip.SetData(ones, 0);
            return dcClip;
        }

        static float ReadAt(double pos)
        {
            int i = (int)pos;
            if (i < 0 || i + 1 >= src.Length) return 0f;
            float fr = (float)(pos - i);
            return src[i] + (src[i + 1] - src[i]) * fr;
        }

        static float Table(float[] tab, int seg, double posSrc)
        {
            double tLoc = (posSrc - F1EngineData.Start[seg]) / srcRate;
            double x = (tLoc - F1EngineData.Valid0[seg]) / F1EngineData.Step;
            if (x <= 0) return tab[0];
            int i = (int)x;
            if (i >= tab.Length - 1) return tab[tab.Length - 1];
            float fr = (float)(x - i);
            return tab[i] + (tab[i + 1] - tab[i]) * fr;
        }

        // ---------------- WSOLA por tipo de trecho ----------------
        const int N = 2048, HA = 1024, HALF = N / 2;
        /// <summary>Busca do WSOLA em 2 estágios: grossa a cada CoarseStep amostras comparando 1 de cada CoarseK, depois
        /// fina amostra a amostra em volta da melhor.</summary>
        public static int CoarseStep = 6, CoarseK = 4;   // medido: mesma qualidade da busca completa com metade da CPU
        static float[] win;

        sealed class Stream
        {
            readonly byte kind;
            readonly bool search;
            readonly float[] ola = new float[N];
            readonly float[] refBuf = new float[HALF];
            int outIdx = HA;
            int prevSeg = -1;
            double prevPos = -1, prevR = 1;
            public int silentSamples;
            public float outRateF = 48000f;

            public Stream(byte kind, bool search) { this.kind = kind; this.search = search; }

            public void Reset() { prevPos = -1; prevSeg = -1; Array.Clear(ola, 0, N); outIdx = HA; }

            public float Next(float f)
            {
                if (outIdx >= HA)
                {
                    Array.Copy(ola, HA, ola, 0, N - HA);
                    Array.Clear(ola, N - HA, HA);
                    MakeGrain(f);
                    outIdx = 0;
                }
                return ola[outIdx++];
            }

            int Pick(float f)
            {
                int n = F1EngineData.Kind.Length, best = -1;
                if (prevSeg >= 0 && segFmin[prevSeg] <= f && f <= segFmax[prevSeg]) return prevSeg;
                for (int i = 0; i < n; i++)
                {
                    if (F1EngineData.Kind[i] != kind || f < segFmin[i] || f > segFmax[i]) continue;
                    if (best < 0) { best = i; continue; }
                    // subindo: o trecho que vai mais alto (fica nele até a troca); alívio: o que desce mais
                    if (kind == 0 ? segFmax[i] > segFmax[best] + 0.5f : segFmin[i] < segFmin[best] - 0.5f) best = i;
                }
                if (best >= 0) return best;
                float bd = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (F1EngineData.Kind[i] != kind) continue;
                    float d = Mathf.Min(Mathf.Abs(Mathf.Log(f / segFmin[i])), Mathf.Abs(Mathf.Log(f / segFmax[i])));
                    if (d < bd) { bd = d; best = i; }
                }
                return best;
            }

            /// <summary>Tempo local (s) do trecho em que a rotação era f, e a rotação exata ali.</summary>
            static double Locate(int seg, float f, out float fsrc)
            {
                var tab = F1EngineData.F[seg];
                int bi = 0; float bd = float.MaxValue;
                for (int i = 0; i < tab.Length; i++)
                {
                    float d = Mathf.Abs(tab[i] - f);
                    if (d < bd) { bd = d; bi = i; }
                }
                double t = bi;
                fsrc = tab[bi];
                // refina entre os vizinhos que cercam f
                int j = bi + 1 < tab.Length && (tab[bi + 1] - f) * (tab[bi] - f) <= 0 ? bi + 1
                      : bi - 1 >= 0 && (tab[bi - 1] - f) * (tab[bi] - f) <= 0 ? bi - 1 : -1;
                if (j >= 0 && Mathf.Abs(tab[j] - tab[bi]) > 1e-4f)
                {
                    float w = (f - tab[bi]) / (tab[j] - tab[bi]);
                    t = bi + (j - bi) * w;
                    fsrc = f;
                }
                return F1EngineData.Valid0[seg] + t * F1EngineData.Step;
            }

            void FillRef(double pos, double r)
            {
                for (int k = 0; k < HALF; k++) refBuf[k] = ReadAt(pos + k * r);
            }

            double Search(double p, double r)
            {
                int delta = (int)(0.008 * srcRate);
                double best = p; float bestScore = float.NegativeInfinity;
                for (int d = -delta; d <= delta; d += CoarseStep)
                {
                    double q = p + d;
                    if (q < 0 || q + HALF * r + 2 >= src.Length) continue;
                    float s = 0f, e = 1e-9f;
                    for (int k = 0; k < HALF; k += CoarseK)
                    {
                        float v = src[(int)(q + k * r)];
                        s += refBuf[k] * v; e += v * v;
                    }
                    float score = s / Mathf.Sqrt(e);
                    if (score > bestScore) { bestScore = score; best = q; }
                }
                double center = best;
                bestScore = float.NegativeInfinity;
                for (int d = -CoarseStep; d <= CoarseStep; d++)
                {
                    double q = center + d;
                    if (q < 0 || q + HALF * r + 2 >= src.Length) continue;
                    float s = 0f, e = 1e-9f;
                    for (int k = 0; k < HALF; k++)
                    {
                        float v = src[(int)(q + k * r)];
                        s += refBuf[k] * v; e += v * v;
                    }
                    float score = s / Mathf.Sqrt(e);
                    if (score > bestScore) { bestScore = score; best = q; }
                }
                return best;
            }

            void MakeGrain(float f)
            {
                int seg = Pick(f);
                if (seg < 0) return;
                double tLoc = Locate(seg, f, out float fsrc);
                double baseR = srcRate / (double)outRateF;
                double r = Math.Abs(Math.Log(f / Mathf.Max(1f, fsrc))) > 0.004 ? baseR * f / fsrc : baseR;
                double p = F1EngineData.Start[seg] + tLoc * srcRate - HALF * r;
                double best = p;
                if (prevPos >= 0)
                {
                    double cont = prevPos + HA * prevR;
                    if (search)
                    {
                        FillRef(cont, prevR);
                        best = Search(p, r);
                    }
                    else if (prevSeg == seg && Mathf.Abs(Table(F1EngineData.F[seg], seg, cont + HALF * r) / f - 1f) < 0.02f)
                        best = cont;   // versão leve: continua a leitura enquanto a rotação bate
                }
                double lo = F1EngineData.Start[seg] + 2, hi = F1EngineData.Start[seg] + F1EngineData.Length[seg] - N * r - 2;
                if (best < lo) best = lo;
                if (best > hi) best = hi;
                // cada pedaço sai no mesmo volume (no vídeo, depois da troca o som volta ao nível de antes)
                float lv = Table(F1EngineData.Lv[seg], seg, best + HALF * r);
                float g = 1f / Mathf.Max(lv, 1e-4f);
                for (int k = 0; k < N; k++) ola[k] += ReadAt(best + k * r) * win[k] * g;
                prevPos = best; prevR = r; prevSeg = seg;
            }
        }

        // ---------------- estado ----------------
        CarController car;
        AudioSource srcAudio;
        bool human;
        Stream onS, offS;
        float outRate = 48000f;
        AudioListener listener;
        int lastGear = 1;

        // escrito no Update (thread principal), lido no áudio
        volatile float tgtF = F1EngineData.LimF;
        volatile float tgtThr;
        volatile float tgtLim;
        volatile float tgtRoll;
        volatile float rollRate = 1f;
        volatile float master;
        volatile bool active = true;
        volatile int shiftSeq, downSeq;

        // só no áudio
        float thrS, limS, rollS, masterS;
        int seenShift, seenDown;
        int envT = int.MaxValue, blipT = int.MaxValue;
        double limPos, rollPos;

        public static bool TryAttach(CarController car, bool isHuman)
        {
            if (car == null || car.spec == null || !CarSpec.IsFormula(car.spec.model)) return false;
            if (!Load()) return false;
            if (win == null)
            {
                win = new float[N];
                for (int i = 0; i < N; i++) win[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / N);
            }
            // objeto filho só do motor: o filtro de áudio vale para a fonte do MESMO objeto, e o carro tem outras fontes
            var go = new GameObject("F1Engine");
            go.transform.SetParent(car.transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = DcClip();
            a.loop = true;
            a.spatialBlend = isHuman ? 0f : 1f;
            a.maxDistance = 130f;
            a.minDistance = isHuman ? 0f : 6f;
            a.dopplerLevel = 0f;
            a.volume = 1f;
            var s = go.AddComponent<F1EngineSynth>();
            s.car = car; s.srcAudio = a; s.human = isHuman;
            s.outRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            s.onS = new Stream(0, isHuman) { outRateF = s.outRate };
            s.offS = new Stream(1, isHuman) { outRateF = s.outRate };
            s.lastGear = car.Gear;
            if (!isHuman) aiSynths.Add(s);
            a.Play();
            return true;
        }

        // IA: só os carros mais perto da câmera rodam o motor sintetizado (no grid de largada os 23 estariam a menos
        // de 140 m — custo demais para o celular); os demais ficam em silêncio, como já ficariam pela distância.
        const int MaxActiveAi = 5;
        static readonly System.Collections.Generic.List<F1EngineSynth> aiSynths = new System.Collections.Generic.List<F1EngineSynth>();
        static int rankFrame = -1;

        void OnDestroy() { aiSynths.Remove(this); }

        static void RankAi(Vector3 lp)
        {
            aiSynths.RemoveAll(x => x == null);
            aiSynths.Sort((a, b) => (a.transform.position - lp).sqrMagnitude.CompareTo((b.transform.position - lp).sqrMagnitude));
            for (int i = 0; i < aiSynths.Count; i++)
                aiSynths[i].active = i < MaxActiveAi && (aiSynths[i].transform.position - lp).sqrMagnitude < 140f * 140f;
        }

        void Update()
        {
            if (car == null || car.spec == null) return;
            var spec = car.spec;
            bool replay = ReplaySystem.IsPlaying;
            float speed = car.SpeedKmh;

            // rotação para o som: pela roda e pela marcha engatada (a física do jogo leva a rotação ao corte durante os
            // 150 ms da troca; o som tem de cair junto com a marcha nova, como no vídeo)
            float rpm = car.Rpm;
            if (!replay && car.Gear >= 1 && spec.gearRatios != null && car.Gear <= spec.gearRatios.Length && Mathf.Abs(car.ForwardSpeed) > 2f)
            {
                float wheelRpm = Mathf.Abs(car.ForwardSpeed) / (2f * Mathf.PI * spec.wheelRadius) * 60f;
                rpm = Mathf.Clamp(wheelRpm * spec.gearRatios[car.Gear - 1] * spec.finalDrive, spec.idleRpm, spec.rpmLimit * 1.02f);
            }
            float f = F1EngineData.FTop * rpm / Mathf.Max(1000f, spec.rpmLimit);
            if (replay) f *= EngineAudio.ReplayPitch;
            tgtF = Mathf.Clamp(f, 20f, F1EngineData.FTop * 1.05f);

            float thr = car.pitControlled ? car.extThrottle : car.throttleInput;
            if (car.wrecked || (car.inputLocked && !car.pitControlled)) thr = 0f;
            tgtThr = Mathf.Clamp01(thr);

            int g = car.Gear;
            if (g != lastGear)
            {
                if (g > lastGear && g >= 2 && !replay) shiftSeq++;
                else if (g < lastGear && g >= 1 && !replay) downSeq++;
                lastGear = g;
            }

            bool inPit = PitLaneSystem.IsInPitFlow(car);
            tgtLim = inPit && speed > 30f ? 1f : 0f;
            // pneu rodando bem devagar: entra de 1 a 8 km/h e some entre 35 e 60 km/h
            tgtRoll = speed < 1f ? 0f : speed < 8f ? speed / 8f : speed < 35f ? 1f : Mathf.Max(0f, 1f - (speed - 35f) / 25f);
            rollRate = 0.8f + 0.4f * Mathf.Min(1f, speed / 30f);

            float vol = human ? 0.20f : 0.32f;
            if (car.inputLocked && !replay && !car.pitControlled) vol = 0f;   // mesmo comportamento do som antigo
            if (replay) vol *= EngineAudio.ReplayVolume;
            // Jogo PARADO (hub da carreira por cima, pausa, resultados → CONTINUAR): o sintetizador continuava tocando o
            // último som com o mundo congelado — o "motor travado" que só parava no menu principal. Silencia.
            if (!replay && (Time.timeScale < 0.01f || RaceX.UI.CareerFlow.IsOpen)) vol = 0f;
            master = vol;

            if (!human)
            {
                if (listener == null) listener = FindAnyObjectByType<AudioListener>();
                if (rankFrame != Time.frameCount)
                {
                    rankFrame = Time.frameCount;
                    RankAi(listener != null ? listener.transform.position : transform.position);
                }
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (src == null || onS == null) { Array.Clear(data, 0, data.Length); return; }
            if (!active && masterS < 1e-4f) { Array.Clear(data, 0, data.Length); onS.Reset(); offS.Reset(); return; }
            int frames = data.Length / channels;
            float aThr = 1f - Mathf.Exp(-1f / (0.040f * outRate));
            float aSlow = 1f - Mathf.Exp(-1f / (0.25f * outRate));
            float aLim = 1f - Mathf.Exp(-1f / (0.08f * outRate));   // limitador solta de uma vez (saída do box)
            float aMaster = 1f - Mathf.Exp(-1f / (0.05f * outRate));
            float fT = tgtF, thrT = tgtThr, limT = tgtLim, rollT = tgtRoll, mT = active ? master : 0f;
            float rr = rollRate;
            int ss = shiftSeq, ds = downSeq;
            if (ss != seenShift) { seenShift = ss; envT = 0; }
            if (ds != seenDown) { seenDown = ds; blipT = 0; }
            int s5 = (int)(0.005f * outRate), s17 = (int)(0.017f * outRate), s27 = (int)(0.027f * outRate), s67 = (int)(0.067f * outRate);
            int blipLen = (int)(0.070f * outRate);
            double baseR = srcRate / (double)outRate;
            int limXf = (int)(0.05f * srcRate), rollXf = (int)(0.06f * srcRate);
            for (int i = 0; i < frames; i++)
            {
                float thrTarget = blipT < blipLen ? 1f : thrT;
                thrS += (thrTarget - thrS) * aThr;
                limS += (limT - limS) * aLim;
                rollS += (rollT - rollS) * aSlow;
                masterS += (mT - masterS) * aMaster;

                float env = 1f;
                if (envT < s67)
                {
                    if (envT < s5) env = 1f - 0.35f * envT / s5;
                    else if (envT < s17) env = 0.65f;
                    else if (envT < s27) env = 0.65f + 0.43f * (envT - s17) / (float)(s27 - s17);
                    else env = 1.08f - 0.08f * (envT - s27) / (float)(s67 - s27);
                    envT++;
                }
                if (blipT < blipLen) blipT++;

                float gOn = thrS * env, gOff = 1f - thrS;
                float on = 0f, off = 0f;
                if (gOn > 0.002f || onS.silentSamples < 4096) { on = onS.Next(fT); onS.silentSamples = gOn > 0.002f ? 0 : onS.silentSamples + 1; }
                else if (onS.silentSamples++ == 4096) onS.Reset();
                if (gOff > 0.002f || offS.silentSamples < 4096) { off = offS.Next(fT); offS.silentSamples = gOff > 0.002f ? 0 : offS.silentSamples + 1; }
                else if (offS.silentSamples++ == 4096) offS.Reset();
                float drive = on * gOn + off * gOff * F1EngineData.ROff;

                float lim = 0f;
                if (limS > 0.002f)
                {
                    double step = baseR * Mathf.Clamp(fT / F1EngineData.LimF, 0.92f, 1.08f);
                    lim = Loop(F1EngineData.LimStart, F1EngineData.LimLen, ref limPos, step, limXf) * F1EngineData.KLim * F1EngineData.RLim;
                }
                float roll = 0f;
                if (rollS > 0.002f)
                    roll = Loop(F1EngineData.RollStart, F1EngineData.RollLen, ref rollPos, baseR * rr, rollXf) * F1EngineData.KRoll * F1EngineData.RRoll;

                float y = masterS * (drive * (1f - limS) + lim * limS + roll * rollS);
                if (y > 0.9f || y < -0.9f) y = (float)Math.Tanh(y);
                for (int c = 0; c < channels; c++) data[i * channels + c] *= y;
            }
        }

        static float Loop(int start, int len, ref double pos, double step, int xf)
        {
            double p = pos;
            float a = ReadAt(start + p);
            if (p > len - xf)
            {
                double q = p - (len - xf);
                float w = (float)(q / xf);
                a = a * (1f - w) + ReadAt(start + q) * w;
            }
            pos += step;
            if (pos >= len) pos -= len - xf;
            return a;
        }
    }
}
