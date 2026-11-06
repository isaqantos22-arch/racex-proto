using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Pit stop v1: desgaste de pneu, compostos e pit lane procedural na reta principal.
    /// Criado por RaceBootstrap quando RaceSettings.pitStops = true.</summary>
    public class PitLaneSystem : MonoBehaviour
    {
        public const float LaneSpeedKmh = 80f; // F1 2026: 80 km/h em todas as sessões (formula1.com, "Explained: pit lane speed limits")
        public const float LaneSpeedMonacoKmh = 60f; // pit lane apertado: o diretor de prova baixa para 60 (Mônaco)
        public const float LaneSpeedMs = LaneSpeedKmh / 3.6f;
        /// <summary>Limite do pit lane DESTA pista (m/s): 60 km/h no Principado (inspirado em Mônaco), 80 nas demais. F2: 60 km/h no
        /// evento inteiro (regulamento esportivo F2 2026, art. 22.9 — pesquisa de 06/11, Claude outputs/PESQUISA_FILA_DUPLA_BOX.md).</summary>
        float LaneLimitMs => race != null && (race.Settings.track == RaceX.Track.TrackId.Principado || RaceBootstrap.F2Race) ? LaneSpeedMonacoKmh / 3.6f : LaneSpeedMs;
        public const float StopSeconds = 4f;
        // FIA: pit lane >= 12 m (fast lane + working lane), separado da pista por muro.
        // Centro do pit a 14 m do centro da pista: pista (~6 m de meia-estrada) + muro + faixa.
        const float laneOffset = 14f;   // centro da FAIXA DO PIT inteira (12 m: −8 a −20 m — mínimo da FIA, Apêndice O)
        /// <summary>FAIXAS REAIS (regulamento esportivo da F1, art. 34.4): a faixa RÁPIDA, junto ao muro, tem no máximo
        /// 3,5 m (aqui −7,95 a −11,45 m; linha de passagem a −10,3 m: 1,5 m entre a lateral do carro e o muro); a faixa INTERNA, junto às garagens, é o único
        /// lugar de trabalho no carro (−11,45 a −20 m, 8,5 m — a FIA recomenda ~7 m de área de trabalho). Na corrida o
        /// carro para PARALELO na faixa interna, em frente à sua garagem (centro a −15,5 m): a equipe trabalha entre
        /// −12,3 e −18,7 m e a faixa rápida fica livre para quem passa. No treino o carro entra na garagem (como na F1).</summary>
        // v99 (03/11): 10,3 → 11,0 — a mureta laranja fica em ~−8,5; com a faixa a −10,3 e o carro de 1,9 m sobrava 0,3 m e o pneu
        // raspava na mureta a cada saída do box (relato do usuário; bateu nela ao desviar do carro do box vizinho).
        const float FastLaneLat = 11.0f;
        const float ServiceLat = 15.5f;
        /// <summary>Tempo máximo da aproximação final até a marca do box (depois disso encaixa).</summary>
        const float FinalApproachMax = 6f;
        /// <summary>Fila dupla: o 2º carro da equipe para esta distância ATRÁS da marca enquanto o companheiro é atendido.</summary>
        const float DoubleStackGap = 8f;

        /// <summary>v108 (relato do usuário, 06/11: "meu carro passa do box, não consegue frear onde os mecânicos estão; passa à frente ou
        /// atropela os mecânicos"). Telemetria das 2 paradas: vinha a 80 km/h até ~19 m da marca e chegava a 30–53 km/h (a rota planejava
        /// 0,9 g e só freava com 1–2 m/s acima do alvo); o jogo então "encaixava" o carro de volta na marca. Agora a freada até a marca é
        /// planejada com a aderência do pneu de hoje: ~0,66 g no seco, até ~0,37 g na chuva forte (valores de projeto, abaixo dos 1,3 g
        /// medidos na freada do carro no seco — margem para pneu frio e pista suja do pit lane).</summary>
        static float PitApproachDecel() => 6.5f * (1f - 0.45f * Mathf.Clamp01(WeatherSystem.Wetness / 0.6f));

        public static readonly string[] CompoundNames = TyreModel.Names;

        /// <summary>Letra do composto (S/M/H/I/W) — rótulos compactos do HUD e da estratégia.</summary>
        public static string CompoundLetter(int compound) => TyreModel.Letter(compound);

        static PitLaneSystem instance;
        public static PitLaneSystem Instance => instance;

        class CarState
        {
            public CarController car;
            public bool isHuman;
            public int compound = 1;
            public float wear;
            public float temp = 1f;  // aquecimento normalizado 0..1 (média das 4 rodas) — HUD/compat.
            public readonly float[] tempW = { 70f, 70f, 70f, 70f };   // °C por roda (v80)
            public readonly float[] gripW = { 1f, 1f, 1f, 1f };      // aderência final por roda (v80)
            public readonly float[] pressure = { 1f, 1f, 1f, 1f };   // pressão relativa (1 = normal, 0 = vazio) — v81c
            public readonly float[] leak = new float[4];             // perda de pressão por segundo (furo lento/rápido)
            public readonly float[] dropShown = new float[4];        // rebaixamento já aplicado na roda física
            public float releaseWait;
            public int exitHint = -1;
            public float lapWearMark;                                // desgaste no começo da volta (dados do fim de semana)
            public int lastPacePhase = -99;                          // fase do plano de ritmo já avisada
            public float replanRadioAt = -99f;                                // saída do pit: índice do traçado mais próximo
            public float escapeT;                                    // saída do pit: tempo "fora do corredor" confirmado                                // segurado na marca: faixa rápida ocupada (s)
            public bool tyreVisual;                                  // já teve pneu murcho (efeito visual ativo)
            public string dmgCall = "";                              // chamada da equipe por dano em aberto ("" = nenhuma)
            public float dmgCheckT;
            public readonly bool[] puncture = new bool[4];
            public float wearMul = 1f;    // fator de desgaste do carro/piloto (TeamStrategy.WearFactor)
            public int runIndex;          // treino: índice da saída do box (programa da equipe)
            public int runLaps = 3;       // treino: voltas desta saída antes de recolher
            public float weatherCallTimer = -1f; // reação da equipe à mudança de clima (s)
            public bool pulledBack;               // v108: passou da marca e está sendo puxado de volta pelos mecânicos
            public bool requested;
            /// <summary>Box pedido mas a entrada desta passagem não dava (rápido demais / longe do lado do pit): fica para a
            /// próxima volta (o pedido continua). Volta a valer quando o carro está longe da entrada.</summary>
            public bool entryMissed;
            public bool inPit;
            public float stopTimer;
            public float finalApproachT;   // aproximação final até a marca do box (corrida)
            public float spotWaitT;        // esperando a vaga do box ficar livre (SpotTaken) — no máximo 8 s
            public float stopDuration;   // duração desta parada (pneus da equipe + bico, se a asa dianteira está danificada)
            public bool noseChange;      // esta parada troca o bico (asa dianteira danificada)
            public int plannedLap = -1;
            public bool holding;
            public float releaseTimer;
            public float scanTimer;    // varredura de entrada do pit a 5 Hz (otimização)
            public bool exiting;      // saída guiada: piloto automático leva até a rampa de SAÍDA
            public Vector3 boxTarget; // caixa do PRÓPRIO carro (dentro da sua garagem)
            public float boxS;        // posição ao longo da spline da própria caixa (para rotas)
            public readonly List<Vector3> route = new List<Vector3>(); // rota por waypoints (entrada/saída)
            public int routeIdx;      // waypoint atual da rota
        public float stuckTimer;   // anti-travamento: tempo quase parado seguindo rota
        public float reverseTimer; // manobra de ré em andamento (anti-travamento)
        public float reverseCount; // ré repetidas no MESMO waypoint (quebra o loop contra estrutura)
        public float pitCtrlSince = -1f; // cão de guarda: desde quando pitControlled está ligado
        public float[] wearW = new float[4]; // desgaste POR RODA (DI, DE, TD, TE) — painel do HUD
        public float waitTimer;  // fila do pit: tempo esperando atrás de carro lento/parado (contorna após 4 s)
        public Vector3 stillPos; // posição da última amostra (anti-travamento por POSIÇÃO, não por velocidade)
        public float stillTimer; // tempo sem sair de um raio de 0,6 m (encostado no muro/arquibancada/carro)
        public bool forcedFree;  // liberado à força pelo cão de guarda: não pode ser re-engatado na fila
        public float escapeSide = 1f; // lado para onde virar durante a ré (−1 esquerda, +1 direita)
        public float exitHoldTimer; // saída da garagem: tempo segurando para carro do corredor passar (máx 6 s)
        public int stintStartLap;  // ESTRATEGISTA: volta em que o stint atual começou
        public Vector3 sweepPos;  // VARREDOR: posição de referência do carro de IA parado no pit
        public float sweepT;      // VARREDOR: tempo parado no pit fora do próprio box
        public Vector3 rescuePos; // RESGATE: posição de referência (o carro saiu dela?)
        public float rescueT;     // RESGATE: tempo parado/de lado/na contramão sob controle do pit
        public float lastExitTime = -999f; // instante da última saída do box (bloqueia re-captura imediata)
        // ESTRATÉGIA do JOGADOR: paradas planejadas (0..3), composto por trecho e volta do box de cada parada.
        public int plannedStops;
        public int stopsDone;
        public readonly List<int> strategy = new List<int>(); // strategy[0] = composto da largada; strategy[i>0] = pós-parada i
        public readonly List<int> stopLaps = new List<int>(); // volta (1-based) do box de cada parada planejada
        }

        RaceManager race;
        TrackSpline spline;
        readonly List<CarState> cars = new List<CarState>();
        MeshRenderer entryLineR;   // tracejado azul da entrada do pit

        /// <summary>TRACEJADO AZUL só quando faz sentido (pedido do usuário): o jogador apertou PIT, a equipe chamou o box pelo
        /// rádio (dano/furo) ou é a volta da parada da estratégia. Só indica a entrada — o box só liga com o botão PIT.</summary>
        void UpdateEntryLine()
        {
            if (entryLineR == null) return;
            bool show = false;
            for (int i = 0; i < cars.Count && !show; i++)
                if (cars[i] != null && cars[i].isHuman && GuideWanted(cars[i])) show = true;
            EntryGuideOn = show;
            // Com a linha de traçado ligada, a faixa azul é o PRÓPRIO tracejado (RacingLineGuide fica azul e mais largo e só
            // desvia na entrada do box — pedido do usuário). A faixa separada ao lado só aparece com a linha desligada.
            bool sep = show && !RaceX.Track.RacingLineGuide.Enabled;
            if (entryLineR.enabled != sep) entryLineR.enabled = sep;
        }

        /// <summary>Autoteste: recolocações no corredor na saída do box / encaixes de quem passou da marca.</summary>
        public static int EscapeResets, PassedSnaps;

        /// <summary>O traço na distância d fica AZUL? Só nos ~100 m antes da entrada e dentro do pit lane (pedido do usuário:
        /// não a pista inteira).</summary>
        public static bool EntryGuideBlueAt(float d)
        {
            if (!EntryGuideOn || instance == null || instance.spline == null) return false;
            float L = instance.spline.Length;
            float rel = Mathf.Repeat(d - instance.laneStart + L * 0.5f, L) - L * 0.5f;
            return rel > -170f && rel < instance.laneLength;   // v105: caminho de entrada começa 170 m antes (era 100)
        }

        /// <summary>Box pedido/chamado para o jogador: o tracejado do traçado vira o caminho AZUL até o box.</summary>
        public static bool EntryGuideOn;

        /// <summary>Caminho até o box faz sentido para este carro: o jogador apertou PIT, a equipe chamou pelo rádio (dano/furo) ou
        /// é a volta da parada da estratégia. Fora do box, saindo ou segurado na garagem = não.</summary>
        bool GuideWanted(CarState st)
        {
            if (st == null || st.car == null || st.inPit || st.exiting || st.holding) return false;
            if (st.requested || st.dmgCall.Length > 0) return true;
            if (race != null && race.Settings.laps < 900 && st.stopsDone < st.stopLaps.Count)
            {
                var pg = race.GetProgress(race.GetEntry(st.car));
                if (pg != null && pg.lap >= st.stopLaps[st.stopsDone]) return true;
            }
            return false;
        }

        /// <summary>HUD: mostra "PIT: ENTRE NA FAIXA LARANJA"/"PIT A N m" só quando o box foi pedido ou chamado para ESTE carro
        /// (pedido do usuário, 06/11: fora disso fica oculto). Por carro — na tela dividida cada jogador vê o seu.</summary>
        public static bool EntryGuideFor(CarController c)
        {
            if (instance == null || c == null) return false;
            foreach (var st in instance.cars) if (st != null && st.car == c) return instance.GuideWanted(st);
            return false;
        }

        /// <summary>Lateral do caminho azul na distância d da pista — o MESMO caminho para o jogador (tracejado azul), para a IA e o
        /// piloto automático (CarAI, preparação para o box) e para a rota que o box assume (BuildEntryRoute): 0 (em cima do
        /// tracejado principal) até 100 m antes do pit lane; daí uma curva suave em S até 7 m para dentro 30 m antes (~0,35 g a
        /// 95 km/h) e a entrada até a faixa rápida. Antes dobrava 7 m em só 15 m (~3,5 g a 95 km/h — ninguém conseguia seguir) e a
        /// IA ia para a borda do lado do pit ~300 m antes, fora do caminho mostrado (pedido do usuário: seguir o tracejado azul).</summary>
        public static float EntryGuideLat(float d)
        {
            if (instance == null || instance.spline == null) return 0f;
            float L = instance.spline.Length;
            float rel = Mathf.Repeat(d - instance.laneStart + L * 0.5f, L) - L * 0.5f;
            // v105 (pedido do usuário em 05/11, depois da saída: "faça o mesmo na entrada do pit" — entrar quase paralelo, não na
            // diagonal): vai para a BORDA do lado do pit cedo (170 → 95 m antes), segue colado nela até 50 m antes (a grama acaba em
            // −45 m) e entra no corredor numa diagonal reta de ~6° (−5,5 → −11,6 m em 58 m), passando do lado de dentro do bico da
            // mureta (−7,8 m). Antes: 0 → −7 m em 70 m e −7 → −10,6 m em 18 m (~11°).
            if (rel <= -170f || rel > instance.laneLength) return 0f;
            float halfG = instance.race != null && instance.race.Track != null && instance.race.Track.Definition != null ? instance.race.Track.Definition.roadWidth * 0.5f : 6f;
            float edgeG = Mathf.Max(1.5f, halfG - 1.0f), hugG = Mathf.Max(1.5f, halfG - 0.5f);
            if (rel <= -95f) return -Mathf.Lerp(0f, edgeG, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-170f, -95f, rel)));
            if (rel <= -50f) return -Mathf.Lerp(edgeG, hugG, Mathf.InverseLerp(-95f, -50f, rel));
            if (rel <= 8f) return -Mathf.Lerp(hugG, FastLaneLat + 0.6f, Mathf.InverseLerp(-50f, 8f, rel));
            return -(FastLaneLat + 0.6f);
        }
        float laneStart;
        float laneLength = 200f;   // corredor esticado: boxes maiores (pedido do usuário: mais espaço entre carros)
        Vector3 boxPos;
        public bool Enabled { get; private set; }
        /// <summary>Total de boxes/garagens ao longo do lane (setado pelo bootstrap antes dos spawns).</summary>
        public int boxCount = 16;

        void Awake() { instance = this; }
        void OnDestroy() { RaceX.Car.CarDamage.TyreHit -= OnTyreHit; if (instance == this) instance = null; }

        /// <summary>FURO POR CONTATO (v81c). Determinístico (nada sorteado), pelo tipo do toque e pela força:
        /// • roda, lateral ou traseira de outro carro: o pneu aguenta — não fura (é o "encostar rodas" comum nas corridas);
        /// • ASA DIANTEIRA de outro carro (placa lateral) no flanco: a partir de 2,5 g de impacto (≈ 25 km/h perpendiculares
        ///   entre os carros) corta e vira FURO LENTO (esvazia em ~50 s — o do Verstappen no México 2019 só apareceu meia
        ///   volta depois); a partir de 8 g, FURO RÁPIDO (esvazia em ~6 s, como Hamilton em Spa 2014);
        /// • muro na roda: a partir de 6 g (≈ 28 km/h perpendiculares) o aro amassa e vaza devagar; a partir de 20 g estoura;
        /// • destroço (g já reduzido pela massa da peça): lento a partir de 1 g, rápido a partir de 6 g.
        /// Valores de projeto (não há medição pública de força de corte de pneu de F1). Teste 30/10 com 0,8 g e qualquer parte
        /// da carroceria contando como cortante: 22 furos no 1º minuto — limites subidos.</summary>
        void OnTyreHit(RaceX.Car.CarController car, int wheel, float g, int kind)
        {
            var st = Find(car);
            if (st == null || wheel < 0 || wheel > 3 || st.puncture[wheel]) return;
            float rate = 0f;
            if (kind == RaceX.Car.CarDamage.HitSharp) rate = g >= 8f ? 1f / 6f : g >= 2.5f ? 1f / 50f : 0f;
            else if (kind == RaceX.Car.CarDamage.HitWall) rate = g >= 20f ? 1f / 3f : g >= 6f ? 1f / 70f : 0f;
            else if (kind == RaceX.Car.CarDamage.HitDebris) rate = g >= 6f ? 1f / 6f : g >= 1f ? 1f / 50f : 0f;
            if (rate <= st.leak[wheel]) return;
            st.leak[wheel] = rate;
            if (st.isHuman)
                Radio(st, rate >= 0.1f ? "FURO no pneu " + WheelName(wheel) + " — venha para o BOX" : "Perda de pressão no " + WheelName(wheel) + " — furo lento, BOX nesta volta");
            Debug.Log("[TYRE] furo " + (rate >= 0.1f ? "rápido" : "lento") + " roda " + wheel + " " + car.name + " (" + (kind == RaceX.Car.CarDamage.HitWall ? "muro" : kind == RaceX.Car.CarDamage.HitDebris ? "destroço" : "asa") + ", " + g.ToString("0.0") + " g)");
        }

        /// <summary>CHAMADA DA EQUIPE POR DANO (jogador): bico/asa dianteira ou furo (o que a parada conserta) → rádio "BOX"
        /// uma vez por dano novo e a faixa PITSTOP da silhueta pisca até ele pedir o box ou consertar.
        /// Só avisa (quem decide é o piloto) — mesma regra do aviso de desgaste. Limiar do bico = o mesmo que faz a equipe
        /// trocar o bico na parada (25% de dano ou peça perdida).</summary>
        void TeamDamageCall(CarState st)
        {
            st.dmgCheckT -= Time.deltaTime;
            if (st.dmgCheckT > 0f) return;
            st.dmgCheckT = 0.5f;
            var d = st.car.damage;
            string why = "";
            if (d != null && !d.Totaled)
            {
                // Só o que a PARADA conserta: bico/asa dianteira (e o furo, abaixo). Asa traseira, suspensão e lateral não se
                // consertam numa parada (pesquisa 30/10) — chamar o box por elas seria inútil.
                if (d.Detached((int)RaceX.Car.DamageZone.FrontWing) || d.FrontLevel >= 0.25f) why = "bico danificado";
            }
            if (why.Length == 0) for (int i = 0; i < 4; i++) if (st.puncture[i] || st.leak[i] > 0f) { why = "furo no " + WheelName(i); break; }
            if (why.Length == 0) { st.dmgCall = ""; return; }
            if (why != st.dmgCall)
            {
                st.dmgCall = why;
                if (!st.requested && !st.inPit && !st.exiting) Radio(st, "BOX, BOX — " + why + ".");
            }
        }

        /// <summary>Box (garagem) do carro — o sistema de mecânicos põe UMA equipe por box.</summary>
        public static int BoxIndexOf(CarController car)
        {
            if (instance == null || instance.race == null || car == null) return -1;
            var e = instance.race.GetEntry(car);
            return e != null ? instance.BoxOf(e) : -1;
        }

        /// <summary>Outro carro (ativo) a menos de 2,6 m do ponto, no plano — a vaga do "encaixe" está ocupada. 2,6 m: largura do
        /// carro + folga; o companheiro do outro lado da mesma garagem fica a ≥ 4,6 m.</summary>
        bool SpotTaken(Vector3 p, CarController self)
        {
            foreach (var en in race.entries)
            {
                var c = en.car;
                if (c == null || c == self || !c.gameObject.activeInHierarchy || c.IsRemote) continue;
                Vector3 d = c.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude < 2.6f * 2.6f) return true;
            }
            return false;
        }

        /// <summary>Grupo da EQUIPE de mecânicos: no treino = o box do carro (um por carro); na corrida = a dupla de boxes da
        /// equipe (os dois carros param na MESMA marca da faixa — double stack).</summary>
        public static int CrewGroupOf(CarController car)
        {
            int b = BoxIndexOf(car);
            return b < 0 ? b : (PracticeSession ? b : b / 2);
        }

        /// <summary>DOUBLE STACK: o outro carro da MESMA EQUIPE está parado na marca (ou encostando nela)?</summary>
        bool BoxBusyByOther(CarState st, RaceEntry e)
        {
            int myBox = BoxOf(e);
            float myD = Vector3.Distance(st.car.transform.position, st.boxTarget);
            foreach (var en in race.entries)
            {
                if (en == e || en.car == null || BoxOf(en) / 2 != myBox / 2) continue;   // mesma EQUIPE (dupla de boxes)
                var os = Find(en.car);
                if (os == null || !os.inPit || os.exiting || os.holding) continue;
                float od = Vector3.Distance(os.car.transform.position, os.boxTarget);
                // parado na marca, ou encostando nela MAIS PERTO do que eu (desempate: os dois nunca esperam um pelo outro)
                if (os.stopTimer > 0f || (od < 4f && od < myD)) return true;
            }
            return false;
        }

        /// <summary>A equipe está chamando o box por dano (a faixa PITSTOP pisca)?</summary>
        public static bool TeamCalling(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.dmgCall.Length > 0 && !st.inPit && !st.exiting && !st.holding;
        }

        /// <summary>Autoteste: fura uma roda (taxa de vazamento por segundo) para ver o pneu murchar e se desfazer.</summary>
        public static void DebugPuncture(CarController car, int wheel, float rate)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null || wheel < 0 || wheel > 3) return;
            st.leak[wheel] = Mathf.Max(st.leak[wheel], rate);
            Debug.Log("[TYRE] teste: furo roda " + wheel + " " + car.name);
        }

        static string WheelName(int w) => w == 0 ? "dianteiro esquerdo" : w == 1 ? "dianteiro direito" : w == 2 ? "traseiro esquerdo" : "traseiro direito";

        /// <summary>Pressão relativa de uma roda (1 = normal) — HUD.</summary>
        public static float PressureOf(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 ? st.pressure[wheel] : 1f;
        }

        /// <summary>Furo lento em andamento (pressão caindo, ainda não vazio)?</summary>
        public static bool IsLeaking(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 && st.leak[wheel] > 0f && st.pressure[wheel] > 0.02f;
        }

        readonly Dictionary<CarController, CarState> byCar = new Dictionary<CarController, CarState>();

        /// <summary>Soma da troca de bico/asa dianteira (pesquisa: +8 a +12 s; ~11 s no total no caso do Hamilton).</summary>
        public const float NoseChangeSeconds = 9f;

        /// <summary>Troca de 4 pneus parado: 2,2 s na melhor equipe a ~2,9 s na pior (pesquisa: 2,0-2,3 s nas melhores;
        /// recorde 1,80 s) + variação de ±0,15 s de uma parada para outra.</summary>
        float TyreStopSeconds(RaceEntry e)
        {
            float tier01 = 0.5f;
            if (RaceX.Career.CareerData.raceActive && e != null)
                tier01 = Mathf.Clamp01(RaceX.Career.CareerMode.TeamTier(RaceX.Career.CareerMode.TeamOfDriver(e.name)) / 11f);
            return 2.2f + 0.7f * tier01 + Random.Range(-0.15f, 0.15f);
        }

        /// <summary>Parada em andamento (mecânicos/câmera): tempo parado, duração total e se troca o bico.</summary>
        public static bool StopInfo(CarController car, out float t, out float duration, out bool nose)
        {
            t = 0f; duration = 0f; nose = false;
            var st = instance != null ? instance.Find(car) : null;
            if (st == null || st.stopTimer <= 0f) return false;
            t = st.stopTimer; duration = st.stopDuration > 0f ? st.stopDuration : StopSeconds; nose = st.noseChange;
            return true;
        }

        /// <summary>No pit lane indo para a PRÓPRIA garagem (ainda não parou): a equipe já sai para esperar.</summary>
        public static bool HeadingToBox(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.inPit && !st.exiting && st.stopTimer <= 0f;
        }

        /// <summary>Pose do carro parado na caixa da própria garagem.</summary>
        public static bool BoxPoseOf(CarController car, out Pose pose)
        {
            pose = default;
            if (instance == null || instance.race == null) return false;
            var e = instance.race.entries.Find(x => x.car == car);
            if (e == null) return false;
            pose = instance.StopPose(e);
            return true;
        }

        CarState Find(CarController car)
        {
            if (car == null) return null;
            return byCar.TryGetValue(car, out var st) ? st : null;
        }

        /// <summary>Box da garagem do carro: o fixo da carreira (por equipe, fim de semana inteiro)
        /// ou, fora dela, o grid slot (comportamento histórico).</summary>
        int BoxOf(global::RaceX.Core.RaceEntry e)
        {
            if (e == null) return 0;
            // Fora da carreira: dupla de grid = equipe virtual (mesma cor, CarPalette slot/2) e UM box
            // por equipe. Antes era o slot do grid (0..23) preso em 16 boxes — 9 carros na garagem 15
            // e a fila do pit travava a corrida (autoteste v80: 73 liberações forçadas).
            return e.pitBox >= 0 ? e.pitBox : BoxForSlot(e.gridSlot);
        }

        /// <summary>Grip atual do pneu (multiplicador por composto e desgaste).</summary>
        /// <summary>Pit ativo (o sistema existe e a corrida tem pit habilitado).</summary>
        public static bool PitEnabled => instance != null && instance.race != null && instance.race.Settings.pitStops;

        public static bool IsPitRequested(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.requested;
        }

        /// <summary>RÁDIO DA EQUIPE para o jogador (o HUD mostra a mensagem).</summary>
        public static event System.Action<CarController, string> TeamRadio;

        /// <summary>ESTRATEGISTA DA EQUIPE (jogador, corrida): a cada passagem pela linha confere (1) o CLIMA — pneu errado
        /// para a água para onde a chuva leva a pista → chama o box e refaz a estratégia (mesmo que ela tenha sido montada
        /// à mão); (2) o DESGASTE — se o pneu só aguenta mais UMA volta, chama o box nesta volta. A mudança aparece no
        /// menu de estratégia ("alterada pela equipe").</summary>
        /// <summary>Treino/classificação da carreira: guarda o desgaste por volta do composto do jogador (base do plano).</summary>
        void OnLapWeekendData(RaceEntry e, float lapTime, bool best)
        {
            if (e == null || !e.isHuman || race == null || race.Settings.laps < 900 || race.Settings.careerPhase < 0) return;
            var st = Find(e.car);
            if (st == null) return;
            float d = st.wear - st.lapWearMark;
            st.lapWearMark = st.wear;
            if (!st.inPit && !st.exiting && !st.holding && d > 0f) RaceX.Career.CareerMode.RecordCompoundWear(st.compound, d);
        }

        /// <summary>RITMO SUGERIDO PELA EQUIPE (pedido do usuário, 04/11: antes era sempre EQUILIBRADO, o padrão do save). Regra de
        /// projeto a partir do DESGASTE MEDIDO nos treinos (CareerMode.WeekendCompound: vida em voltas = 0,92 / desgaste por volta):
        /// pneus durando ≤ 80% do esperado → CAUTELOSO; chuva prevista no meio → ATAQUE NO INÍCIO (abre vantagem antes da pista mudar);
        /// pneus durando ≥ 115% → ATAQUE NO FIM (sobra pneu para o fim); senão EQUILIBRADO. Sem medição = EQUILIBRADO.</summary>
        public static int SuggestPace(int laps, WeatherForecast fc, out string why)
        {
            why = "";
            float ratioSum = 0f; int n = 0;
            for (int c = 0; c < 3; c++)
                if (RaceX.Career.CareerMode.WeekendCompound(c, out _, out _, out float lf, out _) && lf > 0f) { ratioSum += lf / CompoundLife(c); n++; }
            float ratio = n > 0 ? ratioSum / n : 1f;
            RainLaps(fc, Mathf.Max(2, laps), out int rainLap, out _);
            if (n > 0 && ratio <= 0.80f) { why = "seus pneus duraram " + Mathf.RoundToInt(ratio * 100f) + "% do esperado nos treinos"; return 4; }
            if (rainLap > 0 && fc.kind < WeatherKind.LightRain) { why = "chuva prevista por volta da volta " + rainLap + " — vantagem antes da pista mudar"; return 1; }
            if (n > 0 && ratio >= 1.15f) { why = "seus pneus duraram " + Mathf.RoundToInt(ratio * 100f) + "% do esperado — sobra pneu para o fim"; return 3; }
            why = n > 0 ? "desgaste medido dentro do esperado" : "sem medição de desgaste nos treinos";
            return 0;
        }

        static readonly string[] PaceNames = { "EQUILIBRADO", "ATAQUE NO INÍCIO", "ATAQUE NO MEIO", "ATAQUE NO FIM", "CAUTELOSO", "ATAQUE TOTAL" };
        public static string PaceName(int p) => PaceNames[Mathf.Clamp(p, 0, PaceNames.Length - 1)];
        public static int PaceCount => PaceNames.Length;

        /// <summary>Fase de ritmo na volta (0 = normal, 1 = ataque, -1 = cuidado) pelo plano do jogador.</summary>
        public static int PacePhase(int pace, int lap, int laps)
        {
            float f = laps > 0 ? (lap - 1f) / laps : 0f;
            int third = f < 1f / 3f ? 0 : f < 2f / 3f ? 1 : 2;
            switch (pace)
            {
                case 1: return third == 0 ? 1 : 0;
                case 2: return third == 1 ? 1 : 0;
                case 3: return third == 2 ? 1 : 0;
                case 4: return -1;
                case 5: return 1;
                default: return 0;
            }
        }

        /// <summary>PLANO DA EQUIPE (pedido do usuário: pneu escolhido pelos DADOS do treino e da classificação, nunca
        /// sorteado/fixo; clima; ritmo planejado). Testa todas as sequências de 1 a 3 paradas com S/M/H (regra da F1: 2
        /// compostos secos na corrida seca) e escolhe a de menor tempo estimado: Σ stints [voltas × ritmo do composto +
        /// perda por desgaste (≈3 s com o pneu no fim, crescendo linear)] + 22 s por parada. Ritmo e vida vêm das SUAS
        /// voltas no fim de semana; sem dados, da tabela do jogo (macio −0,5 s, duro +0,45 s; vida 18/28/40). Fases de
        /// ataque gastam ~15% mais pneu; cauteloso ~15% menos. Valores de projeto (não medidos), rotulados assim.</summary>
        public static string TeamProposal(int laps, WeatherForecast fc, int pace, out int startC, out List<int> stints)
        {
            stints = new List<int>();
            laps = Mathf.Max(2, laps);
            bool wetRace = fc.kind >= WeatherKind.LightRain;
            RainLaps(fc, laps, out int rainLap, out int dryLap);
            if (wetRace)
            {
                startC = fc.kind >= WeatherKind.HeavyRain ? TyreModel.Wet : TyreModel.Inter;
                // Chuva que PARA no meio da corrida: a volta ao slick já fica no plano, na volta prevista (pedido do usuário, 04/11).
                if (dryLap > 0)
                {
                    int left = laps - dryLap;
                    int dc = DryForLaps(left);
                    stints.Add(dc);
                    if (left > CompoundLife(dc) * 1.05f && dc < 2) stints.Add(2);
                    return "PLANO DA EQUIPE: largada de " + TyreModel.Name(startC) + "; a pista deve SECAR por volta da volta " + dryLap
                         + " — troca para " + TyreModel.Name(dc) + " marcada nela" + (stints.Count > 1 ? " e depois DURO" : "") + ".";
                }
                return "EQUIPE: corrida molhada prevista — largada de " + TyreModel.Name(startC) + "; a equipe chama a troca se a pista secar.";
            }
            // CHUVA PREVISTA NO MEIO DA CORRIDA: plano seco só até a volta da chuva, parada para INTERMEDIÁRIO/CHUVA marcada NELA
            // (antes o plano era todo de slick e só avisava "a equipe chama a troca na hora").
            if (rainLap > 0)
            {
                int wetC = fc.changeTo >= WeatherKind.HeavyRain ? TyreModel.Wet : TyreModel.Inter;
                int dryLaps = rainLap;                       // voltas de slick antes da chuva (largada até a parada)
                int sc = DryForLaps(dryLaps);
                var pre = new List<int>();
                if (dryLaps > CompoundLife(sc) * 1.05f)
                {
                    // não dá para ir de um pneu só até a chuva: um stint a mais (o que cobre o resto)
                    int rest = dryLaps - Mathf.RoundToInt(CompoundLife(sc));
                    pre.Add(DryForLaps(rest));
                }
                startC = sc;
                stints.AddRange(pre);
                stints.Add(wetC);
                string tail = "";
                if (dryLap > 0 && stints.Count < 3)
                {
                    int dc = DryForLaps(laps - dryLap);
                    stints.Add(dc);
                    tail = "; pista secando por volta da volta " + dryLap + " → " + TyreModel.Name(dc);
                }
                var lapsAtR = StopLapsFor(laps, startC, stints, fc);
                string planR = TyreModel.Letter(startC);
                foreach (int c in stints) planR += "→" + TyreModel.Letter(c);
                return "PLANO DA EQUIPE: " + planR + ", box na volta " + string.Join(", ", lapsAtR) + " — ritmo " + PaceName(pace)
                     + ". CHUVA prevista por volta da volta " + rainLap + ": parada para " + TyreModel.Name(wetC) + " marcada nela" + tail + ".";
            }
            float[] p = new float[3], life = new float[3];
            bool[] measured = new bool[3];
            float anchor = 0f; int anchorC = -1;
            for (int c = 0; c < 3; c++)
                if (RaceX.Career.CareerMode.WeekendCompound(c, out float avg, out _, out float lf, out int n))
                {
                    if (n >= 2) { p[c] = avg; measured[c] = true; if (anchorC < 0) { anchor = avg; anchorC = c; } }
                    life[c] = lf > 0f ? lf : CompoundLife(c);
                }
                else life[c] = CompoundLife(c);
            float[] off = { -0.5f, 0f, 0.45f };
            for (int c = 0; c < 3; c++)
                if (!measured[c]) p[c] = (anchorC >= 0 ? anchor - off[anchorC] : 90f) + off[c];
            float bestT = float.MaxValue; int bestStart = 1; var bestSeq = new List<int>();
            var seq = new List<int>();
            for (int stops = 1; stops <= 3; stops++)
            {
                int combos = 1; for (int k = 0; k <= stops; k++) combos *= 3;
                for (int m = 0; m < combos; m++)
                {
                    seq.Clear(); int x = m;
                    for (int k = 0; k <= stops; k++) { seq.Add(x % 3); x /= 3; }
                    bool two = false; for (int k = 1; k < seq.Count; k++) if (seq[k] != seq[0]) two = true;
                    if (!two) continue;
                    float t = EstimateRace(seq, p, life, laps, pace);
                    if (t < bestT) { bestT = t; bestStart = seq[0]; bestSeq = new List<int>(seq.GetRange(1, seq.Count - 1)); }
                }
            }
            startC = bestStart;
            stints = bestSeq;
            var lapsAt = new List<int>();
            FillStopLaps(lapsAt, laps, startC, stints);
            string plan = TyreModel.Letter(startC);
            foreach (int c in stints) plan += "→" + TyreModel.Letter(c);
            string src = "";
            for (int c = 0; c < 3; c++)
                src += (src.Length > 0 ? " · " : "") + TyreModel.Letter(c) + (measured[c] ? " " + FormatLap(p[c]) + " (seus dados)" : " estimado") + ", ~" + Mathf.RoundToInt(life[c]) + "v";
            string rain = fc.rainStart >= 0f ? " CHUVA prevista por volta da volta " + Mathf.Max(1, Mathf.RoundToInt(fc.rainStart * laps)) + ": a equipe chama a troca na hora." : "";
            return "PLANO DA EQUIPE: " + plan + ", box na volta " + string.Join(", ", lapsAt) + " — ritmo " + PaceName(pace) + ". Base: " + src + "." + rain;
        }

        static string FormatLap(float s) { int m = Mathf.FloorToInt(s / 60f); return m + ":" + (s - m * 60f).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture); }

        static float EstimateRace(List<int> seq, float[] p, float[] life, int laps, int pace)
        {
            // voltas por stint proporcionais à vida (ajustada pela fase de ritmo no meio provisório do stint)
            int n = seq.Count;
            var eff = new float[n];
            float sum = 0f, acc = 0f;
            for (int i = 0; i < n; i++) sum += life[seq[i]];
            for (int i = 0; i < n; i++)
            {
                float mid = (acc + life[seq[i]] * 0.5f) / Mathf.Max(1f, sum) * laps;
                int ph = PacePhase(pace, Mathf.RoundToInt(mid) + 1, laps);
                eff[i] = life[seq[i]] * (ph > 0 ? 0.85f : ph < 0 ? 1.15f : 1f);
                acc += life[seq[i]];
            }
            float sumE = 0f; for (int i = 0; i < n; i++) sumE += eff[i];
            float t = 0f; int used = 0;
            for (int i = 0; i < n; i++)
            {
                int L = i == n - 1 ? laps - used : Mathf.Max(1, Mathf.RoundToInt(laps * eff[i] / sumE));
                used += L;
                float over = Mathf.Max(0f, L - eff[i]);
                t += L * p[seq[i]] + 3f * L * L / (2f * Mathf.Max(1f, eff[i])) + over * over * 1.5f;   // passar da vida custa caro
                int ph = PacePhase(pace, used - L / 2, laps);
                t += L * (ph > 0 ? -0.25f : ph < 0 ? 0.35f : 0f);
            }
            return t + (n - 1) * 22f;
        }

        /// <summary>DEPOIS DE CADA PARADA NA CORRIDA (pedido do usuário: parou cedo por furo na volta 4 de 53 e a equipe
        /// dizia "sem mais paradas"): refaz a conta com as voltas que FALTAM e a vida do pneu novo; acrescenta parada(s)
        /// se não chega ao fim, tira se sobra um stint inteiro, redistribui as voltas de box e avisa pelo rádio.</summary>
        void ReplanAfterStop(CarState st, RaceEntry e)
        {
            if (race == null || race.Settings.laps >= 900) return;
            var pg = race.GetProgress(e);
            if (pg == null) return;
            int laps = race.Settings.laps;
            int lapNow = Mathf.Max(1, pg.lap);
            int remain = laps - lapNow + 1;
            if (remain <= 1) return;
            var fut = new List<int>();
            for (int i = st.stopsDone + 1; i < st.strategy.Count; i++) fut.Add(st.strategy[i]);
            int before = fut.Count;
            float cover = CompoundLife(st.compound);
            foreach (int c in fut) cover += CompoundLife(c);
            bool wet = TyreModel.IsWetTyre(st.compound);
            while (cover < remain * 0.97f && fut.Count < 3)
            {
                int left = Mathf.RoundToInt(remain - cover);
                int c = wet ? st.compound : DryForLaps(left);
                fut.Add(c); cover += CompoundLife(c);
            }
            while (fut.Count > 0 && cover - CompoundLife(fut[fut.Count - 1]) >= remain * 1.03f) { cover -= CompoundLife(fut[fut.Count - 1]); fut.RemoveAt(fut.Count - 1); }
            // reconstrói o plano: paradas feitas + atual + futuras
            while (st.strategy.Count > st.stopsDone + 1) st.strategy.RemoveAt(st.strategy.Count - 1);
            while (st.strategy.Count < st.stopsDone + 1) st.strategy.Add(st.compound);
            st.strategy[st.stopsDone] = st.compound;
            st.strategy.AddRange(fut);
            while (st.stopLaps.Count > st.stopsDone) st.stopLaps.RemoveAt(st.stopLaps.Count - 1);
            FillStopLapsFrom(st.stopLaps, lapNow, laps, st.compound, 0f, fut);
            st.plannedStops = st.stopLaps.Count;
            st.plannedLap = st.stopsDone < st.stopLaps.Count ? st.stopLaps[st.stopsDone] : -1;
            if (!st.isHuman) return;
            SyncCareerStrategy(st, "parada na volta " + lapNow);
            int lifeNow = CompoundLife(st.compound);
            string msg;
            if (fut.Count > before)
                msg = "Estratégia recalculada: faltam " + remain + " voltas e o " + TyreModel.Name(st.compound).ToLowerInvariant() + " aguenta ~" + lifeNow +
                      " — +" + (fut.Count - before) + " parada(s). Próximo box na volta " + st.plannedLap + " (pneu " + TyreModel.Letter(fut[0]) + ")";
            else if (fut.Count < before)
                msg = "Estratégia recalculada: o pneu novo leva até o fim (" + remain + " voltas) — uma parada a menos" + (fut.Count > 0 ? ", próximo box na volta " + st.plannedLap : "");
            else if (fut.Count > 0)
                msg = "Faltam " + remain + " voltas: próximo box na volta " + st.plannedLap + " (pneu " + TyreModel.Letter(fut[0]) + ")";
            else
                msg = "Faltam " + remain + " voltas e o pneu aguenta ~" + lifeNow + ": sem mais paradas";
            Radio(st, msg);
            st.replanRadioAt = Time.time;
        }

        void OnLapStrategist(RaceEntry e, float lapTime, bool best)
        {
            if (e == null || !e.isHuman || race == null || !race.Settings.pitStops || race.Settings.laps >= 900) return;
            var st = Find(e.car);
            var pg = race.GetProgress(e);
            if (st == null || pg == null || st.inPit || st.exiting || st.holding) return;
            int lapNow = pg.lap;                              // volta que começou agora
            int lapsLeft = race.Settings.laps - lapNow + 1;   // incluindo esta
            // PLANO DE RITMO: avisa quando muda de fase (ataque / normal / cuidado).
            {
                var cdP = RaceX.Career.CareerMode.data;
                int paceP = cdP != null && race.Settings.careerPhase >= 0 ? cdP.stratPace : 0;
                int ph = PacePhase(paceP, lapNow, race.Settings.laps);
                if (ph != st.lastPacePhase)
                {
                    if (st.lastPacePhase != -99 || ph != 0)
                        Radio(st, ph > 0 ? "Fase de ATAQUE (plano " + PaceName(paceP) + "): pode forçar o ritmo" : ph < 0 ? "Ritmo CAUTELOSO: cuide do carro e dos pneus" : "Ritmo normal: administre a vantagem");
                    st.lastPacePhase = ph;
                }
            }
            if (lapsLeft <= 1) return;                        // última volta: não compensa parar
            bool pending = st.stopsDone < st.stopLaps.Count;
            if (pending && st.stopLaps[st.stopsDone] < lapNow)
            {
                // Passou a parada sem entrar: a equipe insiste nesta volta.
                st.stopLaps[st.stopsDone] = lapNow;
                SyncCareerStrategy(st, "");
                Radio(st, "BOX NO FIM DESTA VOLTA — a parada ficou para trás");
                return;
            }
            // (1) CLIMA (a equipe olha o radar).
            float wet = WeatherWetness();
            int nextC = pending && st.strategy.Count > st.stopsDone + 1 ? st.strategy[st.stopsDone + 1] : -1;
            int dryRef = DryForLaps(lapsLeft);
            if (TyreModel.WrongForWetness(st.compound, wet, dryRef))
            {
                int c = TyreModel.BestForWetness(wet, dryRef);
                bool alreadyPlanned = pending && st.stopLaps[st.stopsDone] == lapNow && nextC == c;
                if (!alreadyPlanned)
                {
                    ReplanFromNow(st, lapNow, c);
                    string why = TyreModel.IsWetTyre(c) ? (TyreModel.IsWetTyre(st.compound) ? "a chuva aumentou" : "começou a chover") : "a pista está secando";
                    SyncCareerStrategy(st, why + " (volta " + lapNow + ")");
                    Radio(st, "BOX NO FIM DESTA VOLTA — " + why + ": pneu " + TyreModel.Name(c) + ". Estratégia atualizada");
                }
                return;
            }
            // (2) DESGASTE no limite: só mais uma volta possível.
            int done = Mathf.Max(1, lapNow - 1 - st.stintStartLap);
            float rate = st.wear / done;
            if (rate > 1e-4f)
            {
                float lapsToLimit = (1f - st.wear) / rate;
                bool stopThisLap = pending && st.stopLaps[st.stopsDone] == lapNow;
                if (lapsToLimit < 2f && lapsToLimit < lapsLeft && !stopThisLap)
                {
                    int c = nextC >= 0 ? nextC : TyreModel.IsWetTyre(st.compound) ? st.compound : DryForLaps(lapsLeft - 1);
                    ReplanFromNow(st, lapNow, c);
                    SyncCareerStrategy(st, "pneu no limite (volta " + lapNow + ")");
                    Radio(st, "BOX NO FIM DESTA VOLTA — o pneu só aguenta mais uma volta");
                }
            }
        }

        /// <summary>Composto seco que cobre as voltas que faltam (macio se der, senão médio, senão duro).
        /// v106: com a pista abaixo de HardMinTrackC o duro NÃO entra (fica o médio; o pneu gasto chama outra parada) — corrida de 05/11 a
        /// 32 °C: os 10 carros que montaram o duro rodaram com ele abaixo de 80 °C (janela 105–135) por até 11 voltas e 2 deles tiveram
        /// perda total; no autoteste a 26 °C, idem. Na F1 as equipes fogem do duro em dia frio pelo mesmo motivo.</summary>
        static int DryForLaps(int laps) => laps <= CompoundLife(0) ? 0 : (laps <= CompoundLife(1) || HardTooCold) ? 1 : 2;

        /// <summary>Pista fria demais para o duro entrar na janela no ritmo de corrida (valor de projeto, pelo modelo térmico e pelas medições
        /// de 05–06/11: a 32 °C a IA não passa de ~80–90 °C no duro).</summary>
        const float HardMinTrackC = 38f;
        static bool HardTooCold => WeatherSystem.Active && WeatherSystem.TrackTemp < HardMinTrackC;

        /// <summary>Parada nesta volta com o composto c; com pneu de chuva o resto da prova segue nele (a equipe
        /// refaz de novo se o tempo mudar). No seco, um stint até o fim com o composto que cobre as voltas.</summary>
        void ReplanFromNow(CarState st, int lapNow, int c)
        {
            int keepStops = st.stopsDone;                                   // paradas já feitas ficam
            while (st.stopLaps.Count > keepStops) st.stopLaps.RemoveAt(st.stopLaps.Count - 1);
            while (st.strategy.Count > keepStops + 1) st.strategy.RemoveAt(st.strategy.Count - 1);
            st.stopLaps.Add(lapNow);
            st.strategy.Add(TyreModel.Clamp(c));
            st.plannedStops = st.stopLaps.Count;
            st.plannedLap = st.stopLaps[keepStops];
        }

        /// <summary>Espelha o plano vivo do jogador no save (o menu de estratégia mostra a alteração da equipe).</summary>
        void SyncCareerStrategy(CarState st, string note)
        {
            var d = RaceX.Career.CareerMode.data;
            if (d == null || race.Settings.careerPhase < 0) return;
            d.stratStops = st.plannedStops;
            if (st.strategy.Count > 0) d.stratStart = st.strategy[0];
            d.stratStints.Clear();
            for (int i = 1; i < st.strategy.Count; i++) d.stratStints.Add(st.strategy[i]);
            if (!string.IsNullOrEmpty(note)) d.stratTeamNote = "ALTERADA PELA EQUIPE: " + note;
            RaceX.Career.CareerMode.SaveQuiet();
        }

        /// <summary>Rádio da equipe vindo de fora do pit (freios, motor, combustível — CarSystems).</summary>
        public static void SendRadio(CarController car, string msg)
        {
            Debug.Log("[RADIO] " + msg);
            TeamRadio?.Invoke(car, msg);
        }

        void Radio(CarState st, string msg)
        {
            Debug.Log("[RADIO] " + msg);
            TeamRadio?.Invoke(st.car, msg);
        }

        /// <summary>Posição dentro da ÁREA DO PIT (corredor, faixa de trabalho e garagens, do lado do pit, entre o início e o
        /// fim do lane)? Usado para o pneu NÃO sujar: o piso do box é asfalto/concreto, mesmo quando a pista em volta
        /// tem grama (o detector de superfície via o box como "fora da pista").</summary>
        public static bool InPitArea(Vector3 pos, ref int hint)
        {
            if (instance == null || instance.spline == null || instance.laneLength <= 0f) return false;
            var sp = instance.spline;
            int i = sp.ClosestIndex(pos, hint);
            hint = i;
            float d = i * sp.spacing;
            float rel = Mathf.Repeat(d - instance.laneStart + 40f, sp.Length) - 40f;   // −40 m antes da entrada até o fim
            if (rel < -40f || rel > instance.laneLength + 40f) return false;
            float lat = Vector3.Dot(pos - sp.points[i], sp.rights[i]);
            return lat < -4f && lat > -(ServiceLat + 25f);                               // lado do pit (−right)
        }

        /// <summary>Início e comprimento do corredor do pit (distância na linha da pista) — garagem do safety car.</summary>
        /// <summary>Sessão de treino/classificação: paradas na GARAGEM (dois carros por box, cada um na sua vaga).</summary>
        public static bool GarageStops => PracticeSession;

        public static bool LaneInfo(out float start, out float length)
        {
            start = 0f; length = 0f;
            if (instance == null || instance.spline == null) return false;
            start = instance.laneStart; length = instance.laneLength;
            return true;
        }

        /// <summary>Box pedido, mas a entrada desta passagem não dá mais (ver entryMissed): a IA não se prepara à toa.</summary>
        public static bool EntryMissed(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.entryMissed;
        }

        /// <summary>IA/piloto automático concluiu que não chega na entrada nesta passagem: deixa para a próxima volta.</summary>
        public static void MarkEntryMissed(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null || st.entryMissed || st.inPit) return;
            st.entryMissed = true;
            Debug.Log("[PIT] " + car.name + ": entrada do box fica para a próxima volta (pedido tarde demais)");
            if (st.isHuman) instance.Radio(st, "ENTRADA DO BOX PERDIDA — box na próxima volta");
        }

        /// <summary>Volta em que o carro vai CHEGAR na próxima entrada do pit (entrada logo depois da linha de chegada = volta
        /// seguinte). Usado para pedir o box na hora certa.</summary>
        public static int LapAtNextEntry(CarController car)
        {
            if (instance == null || instance.race == null || car == null) return 0;
            var e = instance.race.GetEntry(car);
            var p = e != null ? instance.race.GetProgress(e) : null;
            int lap = p != null ? p.lap : 1;
            float L = instance.spline.Length;
            float dist = instance.race.Track.Checkpoints.RaceDistance(car);
            float toEntry = Mathf.Repeat(instance.laneStart - dist, L);
            return Mathf.Repeat(dist, L) + toEntry >= L ? lap + 1 : lap;
        }

        /// <summary>A rota do box pode assumir o carro AGORA? (1) Perto do lado do pit — ângulo até o 1º ponto da rota (30 m antes
        /// do corredor, 7 m para dentro) de no máx. ~23°; (2) consegue frear até ~97 km/h no bico da mureta (12 m antes do
        /// corredor) a ~1 g. Antes assumia a 145 km/h no meio da pista a ~40 m da entrada: a rota virava fechado demais e o carro
        /// batia no bico da mureta laranja (teste 02/11, relato do usuário).</summary>
        static bool CaptureFeasible(float v, float dToLane, float lat)
        {
            float dA = dToLane - 30f;
            if (dA < 4f) return false;
            if (Mathf.Abs(lat + 7f) > Mathf.Max(2.5f, dA * 0.42f)) return false;
            // v99: ~79 km/h no bico (era ~97) e freada de 9,5 m/s² (era 10): com o pneu 2,3 (era 2,9) o carro escorregava na curva
            // de entrada e batia no bico da mureta (autoteste 03/11).
            return v <= Mathf.Sqrt(22f * 22f + 2f * 9.5f * Mathf.Max(0f, dToLane - 12f));
        }

        /// <summary>Distância do carro até a ENTRADA da faixa de pit (m). <0 = dentro da janela do pit.</summary>
        public static float DistanceToPitEntry(CarController car)
        {
            if (instance == null || instance.race == null) return -1f;
            float dist = instance.race.Track.Checkpoints.RaceDistance(car);
            float rel = Mathf.Repeat(instance.laneStart - dist, instance.spline.Length);
            return rel > instance.spline.Length * 0.5f ? -1f : rel;
        }

        /// <summary>Aderência média do pneu (4 rodas): composto × desgaste × temperatura × água.
        /// Usada pela IA para planejar curvas. 1 = macio novo, na janela, pista seca.</summary>
        public static float TireGrip(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return 1f;
            return (st.gripW[0] + st.gripW[1] + st.gripW[2] + st.gripW[3]) * 0.25f;
        }

        /// <summary>Aderência de UMA roda (0=DE,1=DD,2=TE,3=TD) — o CarController aplica por roda:
        /// dianteiras gastas = sai de frente; traseiras gastas = sai de traseira.</summary>
        public static float WheelGrip(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 ? st.gripW[wheel] : 1f;
        }

        /// <summary>Temperatura de UMA roda em °C (HUD).</summary>
        public static float WheelTempC(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 ? st.tempW[wheel] : 95f;
        }

        /// <summary>Furo em alguma roda (pneu passou de 0%).</summary>
        public static bool HasPuncture(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return false;
            for (int i = 0; i < 4; i++) if (st.puncture[i] || st.leak[i] > 0f) return true;   // furo lento também (a IA vai ao box)
            return false;
        }

        /// <summary>Voltas desta saída do box no treino (programa da equipe).</summary>
        public static int PracticeRunLaps(CarController car)
        {
            // Autoteste: runlaps=N fixa as voltas por saída (dica do usuário em 05/11: "para testar mais voltas, uma ou duas paradas —
            // toda troca de pneu custa ~2 voltas para aquecer").
            if (RaceX.Core.AutoTest.Active) { int o = RaceX.Core.AutoTest.GetIntPublic("runlaps", 0); if (o > 0) return o; }
            var st = instance != null ? instance.Find(car) : null;
            return st != null ? Mathf.Max(2, st.runLaps) : 3;
        }

        /// <summary>Temperatura do pneu 0..1 (0 = frio ao sair do box, 1 = aquecido após ~1 volta).</summary>
        public static float TireTemp(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? 1f : st.temp;
        }

        /// <summary>Composto atual (0 macio, 1 médio, 2 duro). -1 = sem pit ativo.</summary>
        public static int Compound(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? -1 : st.compound;
        }

        /// <summary>Desgaste 0..1 (HUD).</summary>
        public static float Wear(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? 0f : st.wear;
        }

        public static string WearText(CarController car)
        {
            int c = Compound(car);
            if (c < 0) return "";
            var st = instance.Find(car);
            return "PNEU " + CompoundLetter(c) + " " + Mathf.RoundToInt((1f - st.wear) * 100f) + "%";
        }

        /// <summary>Jogador pede pit (HUD/tecla P). Vale na próxima passagem pela entrada.</summary>
        public static void RequestPit(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st != null && !st.inPit && !st.holding && !st.exiting)
                st.requested = !st.requested;
        }

        /// <summary>Fim do programa de voltas do treino (IA): pede o box SEM alternar. Com RequestPit (que alterna) um pedido
        /// já feito — dano, preta e laranja — era CANCELADO e o carro danificado passava direto.</summary>
        public static void RequestPitOn(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st != null && !st.inPit && !st.holding && !st.exiting) st.requested = true;
        }

        /// <summary>Direção de prova (preta e laranja): pede o box SEM alternar — a IA danificada vai consertar.</summary>
        public static void ForcePitRequest(CarController car)
        {
            if (car != null) Debug.Log("[PIT-IA] " + car.name + " motivo=dano (bandeira preta e laranja)");
            var st = instance != null ? instance.Find(car) : null;
            if (st != null && !st.inPit && !st.holding && !st.exiting) st.requested = true;
        }

        /// <summary>Indica se o botão PIT deve ficar marcado/destacado no HUD (pedido ativo ou já no pit).</summary>
        public static bool IsPitActiveOrRequested(CarController car)
        {
            if (instance == null || car == null) return false;
            var st = instance.Find(car);
            return st != null && (st.requested || st.inPit || st.holding);
        }

        /// <summary>Carro está no fluxo do pit (na caixa, na fila ou saindo guiado)? TrackBounds NÃO reseta.</summary>
        public static bool IsInPitFlow(CarController car)
        {
            if (instance == null) return false;
            var st = instance.Find(car);
            return st != null && (st.inPit || st.holding || st.exiting);
        }

        /// <summary>Indica se o carro está dentro/saindo da garagem para a câmera de 3ª pessoa aproximar e não atravessar a parede de fundo.</summary>
        public static bool IsInGarageView(CarController car)
        {
            if (instance == null || car == null) return false;
            var st = instance.Find(car);
            if (st == null) return false;
            if (st.holding) return true;
            if (st.exiting && st.routeIdx <= 1) return true;
            if (st.inPit && !st.exiting && Vector3.Distance(car.transform.position, st.boxTarget) < 14f) return true;
            return false;
        }

        /// <summary>Retorna o composto atual do carro (0=Macio/S, 1=Médio/M, 2=Duro/H).</summary>
        public static int CompoundOf(CarController car)
        {
            if (instance == null || car == null) return 1;
            var st = instance.Find(car);
            return st != null ? st.compound : 1;
        }

        /// <summary>Solta o JOGADOR do fluxo do pit IMEDIATAMENTE (fechou o menu da carreira, quer voltar
        /// à pista já). Sem isso o carro continuava em fila/rota com o limite de 80 km/h do pit depois
        /// do menu — a sensação de "carro limitado ao voltar". Se estiver apenas na rota de ENTRADA,
        /// deixa completar (ela termina no box e para — não há atalho seguro do meio da rota).</summary>
        /// <summary>CONTINUAR SESSÃO no hub da carreira (treino/classificação): o carro esteve na GARAGEM — a equipe conserta
        /// tudo o que tem conserto (bico, asas, suspensão, carroceria) e troca o pneu furado/vazio, sem precisar dar a volta
        /// e entrar no pit (pedido do usuário). Desgaste dos pneus fica (é o mesmo jogo). Corrida: não conserta (seria
        /// trapaça — na corrida só a parada no box). Perda total não tem conserto.</summary>
        public static void GarageRepairPlayer()
        {
            if (instance == null || instance.race == null || instance.race.Settings.laps < 900) return;
            foreach (var e in instance.race.entries)
            {
                if (!e.isHuman || e.car == null || e.car.wrecked) continue;
                e.car.GarageRepair();
                var st = instance.Find(e.car);
                if (st == null) continue;
                for (int i = 0; i < 4; i++) { st.puncture[i] = false; st.pressure[i] = 1f; st.leak[i] = 0f; st.dropShown[i] = 0f; }
                for (int i = 0; i < 4; i++) RaceX.Car.TyreDamageVisual.Set(e.car, i, 1f, 0f, false);
                st.dmgCall = "";
            }
        }

        public static void ReleasePlayerNow()
        {
            if (instance == null || instance.race == null) return;
            foreach (var e in instance.race.entries)
            {
                if (!e.isHuman) continue;
                var st = instance.Find(e.car);
                if (st == null) continue;
                if (st.holding) { st.holding = false; instance.Release(st); }   // fila/garagem → rota de saída JÁ
                else if (st.exiting) { /* rota de saída em andamento: v59 já devolve o controle ao pisar na pista */ }
                else if (st.inPit)
                {
                    // CASO SEM FILA: carro navegando a rota de ENTRADA / aproximando da caixa
                    // (inPit sem holding nem exiting) — antes ficava preso ao autopilot e ao
                    // limite de 80 km/h sem nenhuma forma de soltar. Solta JÁ na hora.
                    st.inPit = false;
                    st.requested = false;
                    st.car.PitReleaseReset();
                    instance.race.Track.Checkpoints.ReSyncAfterPit(st.car);
                    e.lapStartTime = Time.time;
                    e.outLap = instance.race.Settings.laps >= 900;
                }
            }
        }

        public void Setup(RaceManager raceManager)
        {
            race = raceManager;
            spline = race.Track.Spline;
            race.LapCompleted += OnLapStrategist;
            race.LapCompleted += OnLapWeekendData;
            RaceX.Car.CarDamage.TyreHit += OnTyreHit;
            Enabled = true;
            // Reta principal: maior trecho de baixa curvatura da volta.
            float best = 0f, bestAt = 0f;
            for (float s = 0f; s < spline.Length; s += 10f)
            {
                float run = 0f;
                for (float t = s; t < s + 240f; t += 10f)
                {
                    float tt = Mathf.Repeat(t, spline.Length);
                    if (CurvatureAt(tt) > 0.0015f) break;
                    run += 10f;
                }
                if (run > best) { best = run; bestAt = s; }
            }
            if (race.Track.PitStart >= 0f) { bestAt = race.Track.PitStart; best = race.Track.PitRun; }
            laneStart = bestAt;
            if (boxCount < 4) boxCount = 16;
            // Espaço REAL por box: ~9,5 m por garagem + 40 m de balanço (entrada/saída).
            // Reta curta: o lane se estica pelo spline — nunca mais espremer os carros no spawn.
            // v99: UM carro por box, estacionado de frente (90°): 7 m por box bastam (carro 1,9 m + equipe até 2,65 m de cada lado).
            laneLength = Mathf.Max(Mathf.Clamp(best * 0.75f, 120f, 300f), boxCount * 7f + 40f);
            var mid = spline.PoseAtDistance(laneStart + laneLength * 0.5f);
            boxPos = mid.position - mid.rotation * new Vector3(laneOffset, 0f, 0f) + Vector3.up * 0.1f;
            BuildGeometry();
            try
            {
                // VÃO no guard-rail da pista no lado do pit: a cerca contínua (~16,7 m do centro)
                // atravessava a faixa do pit (−8 a −20 m) — quem saía da garagem batia nela e não
                // havia caminho até a pista. Sem cerca no trecho INTEIRO do pit + margens.
                race.Track.OpenWallGap(laneStart - 35f, laneLength + 95f, true);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[PitLane] vao no guard-rail ignorado: {ex.Message}"); }
            // Sem GRAMA embaixo do pit (rampa de entrada −40 m até a de saída +45 m): era a mesma altura do asfalto do corredor
            // e aparecia em listras verdes no pit lane.
            try { race.Track.OpenRunoffGap(laneStart - 45f, laneLength + 90f, true); }
            catch (System.Exception ex) { Debug.LogWarning($"[PitLane] grama sob o pit ignorada: {ex.Message}"); }
            InitCars();
        }

        /// <summary>Treino livre (sex/sáb): saída escalonada + ciclos de pit. Identificado por voltas "infinitas".</summary>
        public static bool PracticeSession => instance != null && instance.race != null && instance.race.Settings.laps >= 900;

        /// <summary>Posição de largada dentro do pit lane (treinos): 2 colunas alternadas com 12,5 m
        /// entre carros da mesma fileira e ±1,8 m do centro (4,2 m de folga até cada parede).</summary>
        /// <summary>Largada do treino: cada carro nasce DENTRO DA SUA GARAGEM (casca), virado para a
        /// saída (corredor) — sai de ré nunca: frente é para fora.</summary>
        /// <summary>Lado da garagem do carro (0/1): os DOIS carros da equipe dividem a garagem lado a lado (no treino o 2º
        /// encaixava em cima do 1º — print do usuário).</summary>
        /// <summary>Box de quem não tem box de equipe (fora da carreira): a dupla de grid divide um box. É a MESMA conta
        /// do RaceBootstrap ao nascer o carro — antes ele nascia na garagem "slot" e voltava para a "slot/2" (o carro do
        /// slot 9 voltava para a garagem 4, em cima do carro do slot 4: print "física empilhado" do teste de treino).</summary>
        /// <summary>v99 (pedido do usuário): UM box por carro — o slot do grid; a dupla de grid (equipe virtual) fica em boxes
        /// VIZINHOS (2k, 2k+1). Antes slot/2 (dois carros por garagem).</summary>
        public int BoxForSlot(int slot) => Mathf.Clamp(slot, 0, Mathf.Max(0, (boxCount < 4 ? 16 : boxCount) - 1));

        /// <summary>Lado da garagem (0/1; −1 = sozinho, no centro) pela ORDEM em que os carros foram criados — a mesma
        /// regra do RaceBootstrap ao nascer o carro. Antes o nascimento seguia a ordem de criação e a volta seguia o
        /// gridSlot: os dois da equipe trocavam de lado e um parava em cima do outro (print 51 do usuário).</summary>
        int GarageLane(RaceEntry e) => -1;   // v99: um carro por box, sempre no centro da garagem

        public Pose PitSpawnPose(int slot) => PitSpawnPose(slot, -1);

        public Pose PitSpawnPose(int slot, int lane)
        {
            // MESMA fórmula das garagens e do boxTarget: box = BoxOf(carro) (fim de semana: fixo por
            // equipe), centro −25.5 m, angulado 45° para a saída.
            if (boxCount < 4) boxCount = 16;
            float bs = (laneLength - 30f) / boxCount;
            float firstS = laneStart + 15f;
            var p = spline.PoseAtDistance(Mathf.Repeat(firstS + slot * bs + bs * 0.5f, spline.Length));
            Vector3 right = p.rotation * Vector3.right;
            Vector3 fwd = p.rotation * Vector3.forward;
            p.position = p.position - right * 25.5f; // garagem (abertura −20 m, fundo −36 m)
            // v99 (pedido do usuário): UM carro por box, de FRENTE para o corredor (90°). Com box de ~7,4 m o carro a 45° e a equipe
            // atravessavam as paredes; `lane` ficou sem efeito (box único). A curva de 90° da saída é feita na boca da garagem.
            p.rotation = Quaternion.LookRotation(right, Vector3.up);
            // Cola no chão REAL do pit (o terreno varia; sem isso o carro nasce no ar/afundado).
            // O raio IGNORA carros e destroços: antes acertava o teto do carro do companheiro parado na garagem e o carro
            // novo era colocado EM CIMA dele.
            int noCars = ~((1 << RaceX.Car.CarFactory.CarBodyLayer) | (1 << RaceX.Car.CarFactory.WheelLayer) | (1 << RaceX.Car.CarFactory.DebrisLayer));
            var up = Physics.Raycast(p.position + Vector3.up * 4f, Vector3.down, out var hit, 12f, noCars, QueryTriggerInteraction.Ignore)
                ? hit.point
                : p.position;
            p.position = up + Vector3.up * 0.35f;
            return p;
        }

        readonly Dictionary<int, Pose> serviceCache = new Dictionary<int, Pose>();

        /// <summary>Chão de verdade num ponto, IGNORANDO carros (o carro já está quase na marca quando a parada começa:
        /// um raio simples batia no teto dele e o recolocava ~1 m no ar).</summary>
        static Vector3 GroundIgnoringCars(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Vector3 g = p;
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.GetComponentInParent<CarController>() != null) continue;
                if (h.distance < best) { best = h.distance; g = h.point; }
            }
            return g;
        }

        /// <summary>Marca da PARADA na corrida: faixa interna em frente à garagem, carro paralelo ao pit lane.</summary>
        public Pose ServicePose(int slot)
        {
            if (serviceCache.TryGetValue(slot, out var cached)) return cached;
            if (boxCount < 4) boxCount = 16;
            float bs = (laneLength - 30f) / boxCount;
            float firstS = laneStart + 15f;
            // Corrida: a marca da parada é da EQUIPE (os dois carros param no MESMO ponto, double stack, como na F1) — fica na
            // divisa entre os dois boxes da dupla (2k, 2k+1).
            var p = spline.PoseAtDistance(Mathf.Repeat(firstS + (slot / 2) * 2f * bs + bs, spline.Length));
            Vector3 right = p.rotation * Vector3.right;
            Vector3 fwd = p.rotation * Vector3.forward; fwd.y = 0f;
            p.position = p.position - right * ServiceLat;
            p.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward, Vector3.up);
            p.position = GroundIgnoringCars(p.position) + Vector3.up * 0.35f;
            serviceCache[slot] = p;
            return p;
        }

        /// <summary>Onde o carro para: na corrida, a marca da faixa interna; no treino/classificação, dentro da garagem.</summary>
        Pose StopPose(RaceEntry e) => PracticeSession ? PitSpawnPose(BoxOf(e), GarageLane(e)) : ServicePose(BoxOf(e));

        /// <summary>Carros de OUTROS aparelhos no online: sem estado de pit aqui (o pit deles roda no aparelho dono; antes o
        /// pit local "capturava" o fantasma, montava rota e até o teletransportava no resgate).</summary>
        int remoteCars;
        static bool carsDirty;

        /// <summary>Online: um carro mudou de dono (anfitrião assumiu o carro de quem caiu, ou devolveu) — refaz os estados.</summary>
        public static void CarsChanged() => carsDirty = true;

        void InitCars()
        {
            if (race == null || race.entries.Count == 0) return;
            bool practice = race.Settings.laps >= 900;
            int slot = cars.Count;
            remoteCars = 0;
            foreach (var e in race.entries)
            {
                if (e.car != null && e.car.IsRemote)
                {
                    remoteCars++;
                    // Virou fantasma de novo (devolvido ao dono): o pit daqui não mexe mais nele.
                    var old = Find(e.car);
                    if (old != null) { byCar.Remove(e.car); cars.Remove(old); }
                    continue;
                }
                if (Find(e.car) != null) continue;
                var st = new CarState { car = e.car, isHuman = e.isHuman, compound = 1 };
                byCar[e.car] = st;
                var aiP = e.car.GetComponent<CarAI>();
                float tier01 = RaceX.Career.CareerData.raceActive ? Mathf.Clamp01(RaceX.Career.CareerMode.TeamTier(RaceX.Career.CareerMode.TeamOfDriver(e.name)) / 11f) : 0.5f;
                st.wearMul = TeamStrategy.WearFactor(e.car.spec, aiP != null ? aiP.Aggression : 0.5f, aiP != null ? aiP.TyreManagement : 0.5f, tier01);
                if (!e.isHuman) PlanNpc(st, e, tier01);
                else AutoStrategy(st);
                float bs = (laneLength - 30f) / boxCount;
                int bIdx = Mathf.Clamp(BoxOf(e), 0, Mathf.Max(0, boxCount - 1));
                float firstS = laneStart + 15f;
                // Treino: centro do PRÓPRIO box (garagem). Corrida: a marca da equipe (divisa da dupla de boxes) — ver ServicePose.
                st.boxS = Mathf.Repeat(firstS + (practice ? bIdx * bs + bs * 0.5f : (bIdx / 2) * 2f * bs + bs), spline.Length);
                var bp = spline.PoseAtDistance(st.boxS);
                Vector3 bRight = bp.rotation * Vector3.right;
                // Alvo do box: centro da garagem no treino; marca da faixa interna na corrida (ver FastLaneLat).
                st.boxTarget = practice ? bp.position - bRight * 28f + Vector3.up * 0.1f : ServicePose(bIdx).position;
                if (practice)
                {
                    if (e.isHuman)
                    {
                        st.releaseTimer = 2f;
                    }
                    else
                    {
                        // SEXTA E SÁBADO (treino e qualificação): cada equipe (par de carros) tem sua
                        // própria janela estratégica de saída — não saem todos juntos para não lotar a pista,
                        // e o 2º carro da mesma equipe sai defasado do 1º.
                        int teamIdx = bIdx / 2;
                        int mateIdx = bIdx % 2;
                        int wave = teamIdx % 4; // 4 ondas de equipes
                        float teamStrategyDelay = wave * 14f + (teamIdx / 4) * 4.5f + mateIdx * 8.5f;
                        st.releaseTimer = 3.5f + teamStrategyDelay + Random.Range(0f, 3.5f);
                        ++slot;
                    }
                    st.holding = true;
                    st.inPit = true;
                    SetTyreTemps(st, TyreModel.BlanketTemp); // treino/quali: sai da garagem com pneu abaixo da janela (cobertor 70 °C)
                    ApplyHold(st);
                }
                else SetTyreTemps(st, TyreModel.BlanketTemp + 15f); // largada do grid: volta de apresentação aquece um pouco
                if (e.isHuman) ApplyCareerHubStrategy(st);
                if (e.isHuman && practice) ApplyHumanPracticeCompound(st);
                // Largada com pista molhada: todos com o pneu certo para a água.
                if (!practice && TyreModel.NeedsWetTyre(WeatherWetness()) && !TyreModel.IsWetTyre(st.compound))
                {
                    st.compound = TyreModel.BestForWetness(WeatherWetness(), st.compound);
                    if (st.strategy.Count > 0) st.strategy[0] = st.compound;
                }
                // Temperatura de saída pelo composto FINAL (estratégia do hub, pneu do treino e clima já aplicados).
                // Largada do grid: a volta de apresentação aquece um pouco (+15 °C).
                Blanket(st, practice ? 0f : 15f);
                cars.Add(st);
            }
        }

        int CarsOnTrackCount()
        {
            int count = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != null && c.car != null && (!c.inPit || c.exiting)) count++;
            }
            return count;
        }

        /// <summary>Outro carro do MESMO box já pediu, está no corredor ou parado na garagem.</summary>
        bool TeammateInPit(CarState st, RaceEntry e)
        {
            int myBox = BoxOf(e);
            for (int i = 0; i < race.entries.Count; i++)
            {
                var o = race.entries[i];
                if (o == e || o.car == null || BoxOf(o) != myBox) continue;
                var os = Find(o.car);
                if (os != null && (os.requested || os.inPit || os.exiting)) return true;
            }
            return false;
        }

        int CarsInPitLaneMovingCount()
        {
            int count = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c != null && c.car != null && (c.exiting || (c.inPit && !c.holding))) count++;
            }
            return count;
        }

        // Prioridade no corredor: quem sai da garagem (início da rota) cede a vez; todo o resto tem prioridade 1.
        int PitPriority(CarState s)
        {
            if (s.exiting && s.routeIdx < 2) return 0;
            return 1;
        }

        /// <summary>Faixa rápida livre para soltar o carro da marca: ninguém andando nela de 30 m antes até 3 m depois do box.</summary>
        bool FastLaneClearForRelease(CarState st)
        {
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            for (int i = 0; i < race.entries.Count; i++)
            {
                var e = race.entries[i];
                if (e.car == null || e.car == st.car || e.retired) continue;
                if (e.car.SpeedKmh < 5f) continue;
                float d = race.Track.Checkpoints.RaceDistance(e.car);
                float rel = Mathf.Repeat(d - laneStart, spline.Length);
                if (rel < boxRel - 30f || rel > boxRel + 3f) continue;
                var p = spline.PoseAtDistance(Mathf.Repeat(d, spline.Length));
                float lat = Vector3.Dot(e.car.transform.position - p.position, p.rotation * Vector3.right);
                if (lat <= -8f && lat >= -13.5f) return false;
            }
            return true;
        }

        /// <summary>Alguém no corredor (faixa −8..−13,5, > 5 km/h) que CHEGA à boca do box em até `seconds` (pela velocidade dele),
        /// ou já está ao lado (até 3 m depois do box)? Janela curta de propósito: na F1 o carro é solto no instante em que a
        /// troca termina e só segura se está passando alguém naquele momento.</summary>
        bool LaneBusyAtBox(CarState st, float seconds)
        {
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            for (int i = 0; i < race.entries.Count; i++)
            {
                var e = race.entries[i];
                if (e.car == null || e.car == st.car || e.retired || e.car.wrecked) continue;
                float v = e.car.ForwardSpeed;
                if (v < 1.4f) continue;
                float d = race.Track.Checkpoints.RaceDistance(e.car);
                float rel = Mathf.Repeat(d - laneStart, spline.Length);
                if (rel < boxRel - v * seconds - 4f || rel > boxRel + 3f) continue;
                var p = spline.PoseAtDistance(Mathf.Repeat(d, spline.Length));
                float lat = Vector3.Dot(e.car.transform.position - p.position, p.rotation * Vector3.right);
                if (lat <= -8f && lat >= -13.5f) return true;
            }
            return false;
        }

        bool IsCorridorClearNearBox(CarState st)
        {
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            for (int i = 0; i < race.entries.Count; i++)
            {
                var e = race.entries[i];
                if (e.car == null || e.car == st.car) continue;
                var ot = Find(e.car);
                if (ot != null)
                {
                    if (ot.holding) continue;
                    if (Vector3.Distance(ot.car.transform.position, ot.boxTarget) < 6.5f) continue;
                    if (ot.requested && boxRel < 90f) return false;
                }
                float d = race.Track.Checkpoints.RaceDistance(e.car);
                float rel = Mathf.Repeat(d - laneStart, spline.Length);
                if (rel < boxRel - 70f || rel > boxRel + 15f) continue;
                var p = spline.PoseAtDistance(Mathf.Repeat(d, spline.Length));
                float lat = Vector3.Dot(e.car.transform.position - p.position, p.rotation * Vector3.right);
                if (lat >= -22f && lat <= -8.5f) return false;
                if (e.isHuman && lat < -7f) return false;
            }
            return true;
        }

        float SessionRemainingSeconds()
        {
            if (race != null && race.Settings.careerPhase >= 0 && RaceX.Career.CareerMode.SessionClockActive)
                return Mathf.Max(0f, RaceX.Career.CareerMode.SessionSecondsLeft());
            return 600f;
        }

        /// <summary>Mantém o carro parado no pit enquanto aguarda a liberação (fila do treino).</summary>
        void ApplyHold(CarState st)
        {
            if (st.car == null) return;
            st.car.pitControlled = true;
            st.car.extThrottle = 0f;
            st.car.extBrake = 1f;
            st.car.extSteer = 0f;
        }

        /// <summary>Libera o carro da caixa: SAÍDA GUIADA por ROTA limpa:
        /// sai RETO da garagem (−28 → −16 m), alinha no corredor (−14 m), segue até a rampa e entra na pista.</summary>
        void Release(CarState st, float fromRel = -1f)
        {
            // Treino/classificação: saída da garagem no modo AUTO monta o pneu do clima de AGORA (antes ficava o que
            // foi montado antes da chuva — saía de duro na chuva). Composto escolhido à mão no hub é respeitado.
            if (PracticeSession && fromRel < 0f)
            {
                var cd = RaceX.Career.CareerMode.data;
                bool manual = st.isHuman && cd != null && cd.practiceCompound >= 0;
                if (manual) st.compound = ManualForWater(st, TyreModel.Clamp(cd.practiceCompound));   // escolha à mão vale só se combina com a água
                else
                {
                    int c = AutoCompoundForWeather(st.compound);
                    if (c != st.compound)
                    {
                        st.compound = c;
                        if (st.strategy.Count > 0) st.strategy[0] = c;
                    }
                }
                // Saindo da garagem: o pneu estava no cobertor — temperatura de saída do composto montado.
                Blanket(st);
            }
            st.holding = false;
            st.exiting = true;
            st.lastExitTime = Time.time;
            st.car.pitControlled = true;
            st.route.Clear();
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            if (fromRel >= 0f)
            {
                // Reenquadrado no meio do corredor: rota de saída já a partir do rel atual.
                for (float r = fromRel + 8f; r < laneLength - 8f; r += 10f)
                {
                    var pm = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, spline.Length));
                    st.route.Add(pm.position - pm.rotation * new Vector3(FastLaneLat, 0f, 0f) + Vector3.up * 0.05f);
                }
            }
            else
            {
            Vector3 X(float rel, float lat) { var pp = spline.PoseAtDistance(Mathf.Repeat(st.boxS + rel, spline.Length)); return pp.position - pp.rotation * new Vector3(lat, 0f, 0f) + Vector3.up * 0.05f; }
            if (PracticeSession)
            {
                // Treino: boca da garagem (−19 m) → faixa interna → FAIXA RÁPIDA.
                // v99: carro estacionado de FRENTE para o corredor (90°): sai reto até a boca da garagem e só então curva.
                st.route.Add(X(0.5f, 21.5f));
                st.route.Add(X(4.0f, 18.5f));
                if (boxRel + 10f < laneLength - 10f) st.route.Add(X(10f, 15.6f));
                if (boxRel + 15f < laneLength - 10f) st.route.Add(X(15f, 12.0f));
            }
            else
            {
                // Corrida: sai da marca em DIAGONAL e chega à faixa rápida antes do box da frente (9,5 m), sem passar
                // pela área de trabalho da equipe vizinha.
                st.route.Add(X(3.0f, ServiceLat - 1.5f));
                if (boxRel + 6.0f < laneLength - 10f) st.route.Add(X(6.0f, FastLaneLat + 1.0f));
            }
            // Faixa rápida de 10 em 10 m até o fim do pit lane (nunca corta pela mureta laranja em −7,8 m).
            for (float r = boxRel + (PracticeSession ? 22f : 10f); r < laneLength - 8f; r += 10f)
            {
                var pm = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, spline.Length));
                st.route.Add(pm.position - pm.rotation * new Vector3(FastLaneLat, 0f, 0f) + Vector3.up * 0.05f);
            }
            }
            // 4) Fim da mureta laranja -> rampa de saída gradual até a pista;
            var pe = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength - 4f, spline.Length));
            st.route.Add(pe.position - pe.rotation * new Vector3(FastLaneLat, 0f, 0f) + Vector3.up * 0.05f);
            // Rampa longa e quase paralela à pista (v105): −11 m → −5,8 m em ~50 m (antes −11 → −5,5 em 16 m, ~19°: "entra na
            // diagonal"). A rampa de asfalto vai até +45 m (BuildVisuals).
            float[] rampRel = { 10f, 22f, 34f, 45f };
            float[] rampLat = { 9.6f, 8.2f, 6.9f, 5.8f };
            for (int i = 0; i < rampRel.Length; i++)
            {
                var px = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + rampRel[i], spline.Length));
                st.route.Add(px.position - px.rotation * new Vector3(rampLat[i], 0f, 0f) + Vector3.up * 0.05f);
            }
            // 5) Na pista — ponto de merge adiante, RENTE À BORDA DO LADO DO PIT (v105, pedido do usuário em 05/11: "ao sair do box o
            // carro deve sair rente à zebra, não caminhando para o centro"). Antes o último ponto era o CENTRO da pista: a própria rota
            // atravessava o carro de −5,5 m até o meio em 16 m e o soltava apontando para o outro lado (autoteste de 05/11: −10 → −0,5 m
            // em 2 s, depois fora da pista pelo lado do pit ao corrigir). A IA segura essa borda até o fim da 1ª curva (CarAI.StartExitHold).
            float halfR = race != null && race.Track != null && race.Track.Definition != null ? race.Track.Definition.roadWidth * 0.5f : 6f;
            float mergeLat = Mathf.Max(1.5f, halfR - 1.2f);
            var pMerge = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 65f, spline.Length));
            st.route.Add(pMerge.position - pMerge.rotation * new Vector3(mergeLat, 0f, 0f) + Vector3.up * 0.05f);
            st.routeIdx = 0;
            st.stuckTimer = 0f; st.reverseTimer = 0f; st.stillTimer = 0f; st.waitTimer = 0f; st.reverseCount = 0f; st.exitHoldTimer = 0f;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
        }

        /// <summary>Pontos (m em relação ao início do pit lane) da rota de ENTRADA, todos em cima do caminho azul (EntryGuideLat).</summary>
        static readonly float[] EntryRouteRels = { -90f, -75f, -60f, -45f, -30f, -21f, -12f, -2f, 8f };

        void BuildEntryRoute(CarState st, float rel, bool approachingFromTrack)
        {
            st.route.Clear();
            float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
            if (approachingFromTrack)
            {
                // ENTRADA: segue o TRACEJADO AZUL (EntryGuideLat) — pontos do mesmo caminho que o jogador vê, à frente do carro: curva
                // suave até 7 m para dentro 30 m antes do lane, por fora do bico da mureta (rel −10, ~2,8 m de folga) e alinhado no
                // corredor. Antes ia em diagonal direto ao ponto de −30 m, fora do caminho desenhado.
                float len = spline.Length;
                float carRel = Mathf.Repeat(race.Track.Checkpoints.RaceDistance(st.car) - laneStart + len * 0.5f, len) - len * 0.5f;
                foreach (float r in EntryRouteRels)
                {
                    if (r < carRel + 8f) continue;   // ponto já alcançado (ou perto demais para mirar)
                    var pg = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, len));
                    st.route.Add(pg.position + pg.rotation * new Vector3(EntryGuideLat(laneStart + r), 0f, 0f) + Vector3.up * 0.05f);
                }
            }
            float startRel = approachingFromTrack ? 26f : Mathf.Max(12f, rel + 14f);
            // Pela FAIXA RÁPIDA até perto do próprio box (nunca pela faixa interna, onde outras equipes trabalham).
            for (float r = startRel; r < boxRel - (PracticeSession ? 12f : 24f); r += 24f)
            {
                var pStep = spline.PoseAtDistance(Mathf.Repeat(laneStart + r, spline.Length));
                st.route.Add(pStep.position - pStep.rotation * new Vector3(FastLaneLat, 0f, 0f) + Vector3.up * 0.05f);
            }
            Vector3 L(float rel, float lat) { var pp = spline.PoseAtDistance(Mathf.Repeat(st.boxS + rel, spline.Length)); return pp.position - pp.rotation * new Vector3(lat, 0f, 0f) + Vector3.up * 0.05f; }
            if (PracticeSession)
            {
                // Treino: da faixa rápida para dentro da garagem (−28 m), passando pela boca (−18,5 m).
                st.route.Add(L(-8f, 13.5f));
                st.route.Add(L(-2f, 18.5f));
            }
            else
            {
                // Corrida (v108, pesquisa da fila dupla de 06/11): sai da faixa rápida 20 m antes da marca e chega à faixa interna
                // DoubleStackGap (8 m) antes dela; os últimos 8 m são retos na faixa interna. O 2º carro da equipe (fila dupla) espera
                // exatamente nesse ponto, inteiro FORA da faixa rápida (regra: não se para na faixa rápida — F1 Anexo L cap. IV art. 5,
                // F2 art. 28.6; Lawson, Holanda 2023: 10 s por atrapalhar) e dentro do espaço da própria equipe (marcas a ~37 m).
                // Antes a diagonal começava 10 m antes e o carro em espera ficava meio dentro da faixa rápida.
                st.route.Add(L(-20f, FastLaneLat + 0.3f));
                st.route.Add(L(-15f, 12.8f));
                st.route.Add(L(-11f, 14.6f));
                st.route.Add(L(-DoubleStackGap, ServiceLat));
                st.route.Add(L(-4f, ServiceLat));
                st.route.Add(L(-1.2f, ServiceLat));
            }
            st.route.Add(st.boxTarget);
            st.routeIdx = 0;
            st.stuckTimer = 0f; st.reverseTimer = 0f; st.stillTimer = 0f; st.waitTimer = 0f; st.exitHoldTimer = 0f;
        }

        /// <summary>[sem uso atual] Distância ao longo da spline de um carro — mantido para debug/uso futuro.</summary>
        float LaneSOf(CarController c)
        {
            float d = race.Track.Checkpoints.RaceDistance(c);
            return Mathf.Repeat(d, spline.Length);
        }

        /// <summary>Pilota por ROTA (waypoints em sequência). Retorna false quando a rota acabou.
        /// Guarda anti-NaN: waypoint inválido é pulado (não congela o carro).</summary>
        bool DriveRoute(CarState st, float speedLimit)
        {
            if (st.routeIdx >= st.route.Count) return false;
            float relNow = Mathf.Repeat(race.Track.Checkpoints.RaceDistance(st.car) - laneStart, spline.Length);
            var spLat = spline.PoseAtDistance(Mathf.Repeat(race.Track.Checkpoints.RaceDistance(st.car), spline.Length));
            float latNow = Vector3.Dot(st.car.transform.position - spLat.position, spLat.rotation * Vector3.right);
            bool corridorZone = (st.inPit || st.exiting) && relNow >= 8f && relNow <= laneLength - 8f;
            // ANTI-TRAVAMENTO POR POSIÇÃO: velocidade para frente engana (carro encostado e deslizando
            // de lado dava >0,6 km/h e o contador zerava). Se não sai de 0,6 m por 1,2 s → ré JÁ,
            // em qualquer caso (inclusive com carro parado à frente), virando para o lado oposto ao muro/carro.
            if (Vector3.Distance(st.car.transform.position, st.stillPos) > 0.6f)
            {
                st.stillPos = st.car.transform.position;
                st.stillTimer = 0f;
            }
            else st.stillTimer += Time.deltaTime;
            bool queued = false;
            Vector3 fwdNow = st.car.transform.forward; fwdNow.y = 0f; fwdNow.Normalize();
            foreach (var ot in cars)
            {
                if (ot == st || ot.car == null || ot.holding || ot.car.wrecked || !ot.car.gameObject.activeInHierarchy) continue;
                Vector3 dq = ot.car.transform.position - st.car.transform.position; dq.y = 0f;
                if (dq.magnitude < 4.5f && Vector3.Angle(fwdNow, dq) < 38f) { queued = true; break; }
            }
            // SAÍDA DO BOX NA CORRIDA (2 primeiros pontos): a liberação segura (FastLaneClearForRelease) já esperou a faixa
            // rápida ficar livre — sem "ceder a vez" nem ré aqui (teste 30/10: o carro ficou ~9 s parado no box, engatando
            // ré, e foi "resgatado" duas vezes depois das luzes verdes). No treino (saída da garagem) continua como antes.
            bool raceBoxExit = st.exiting && !PracticeSession && st.routeIdx < 2;
            if (!raceBoxExit && st.stillTimer > (queued ? 4.0f : 1.4f) && st.reverseTimer <= 0f && !(corridorZone && latNow > -9.9f))
            {
                // Se for o jogador entrando no próprio box e já estiver perto da sua garagem, encaixa no box sem dar ré aleatória.
                if (st.isHuman && st.inPit && !st.holding && !st.exiting && Vector3.Distance(st.car.transform.position, st.boxTarget) < 22f)
                {
                    st.routeIdx = st.route.Count;
                    return false;
                }
                st.stillTimer = 0f;
                st.stuckTimer = 0f;
                st.reverseCount++;
                st.reverseTimer = Mathf.Min(2.6f, 1.3f + 0.35f * st.reverseCount);
                st.escapeSide = st.car.transform.InverseTransformPoint(st.route[st.routeIdx]).x >= 0f ? -1f : 1f;
            }
            Vector3 target = st.route[st.routeIdx];
            Vector3 to = target - st.car.transform.position; to.y = 0f;
            float reach = (st.exiting && st.routeIdx < 2) ? 3.2f
                        : (!st.exiting && st.routeIdx >= st.route.Count - 2) ? 3.5f
                        : 5.0f;
            // PONTO JÁ PASSADO: o carro cruzou a perpendicular do ponto no sentido do próximo trecho (ou o ponto ficou para
            // trás e o próximo está à frente) → conta como cumprido. Antes, passando a mais de 3–5 m do ponto, o carro
            // VIRAVA PARA TRÁS para buscá-lo — a "contramão" na entrada/saída do pit (teste 01/11).
            bool passedPoint = false;
            if (st.routeIdx + 1 < st.route.Count && !float.IsNaN(to.x))
            {
                Vector3 seg = st.route[st.routeIdx + 1] - target; seg.y = 0f;
                Vector3 rel = st.car.transform.position - target; rel.y = 0f;
                Vector3 fwdP = st.car.transform.forward; fwdP.y = 0f;
                Vector3 toNext = st.route[st.routeIdx + 1] - st.car.transform.position; toNext.y = 0f;
                if (seg.sqrMagnitude > 0.01f && Vector3.Dot(rel, seg.normalized) > 0f && rel.magnitude < 12f) passedPoint = true;
                else if (to.magnitude < 12f && Vector3.Dot(fwdP, to) < 0f && Vector3.Dot(fwdP, toNext) > 0f) passedPoint = true;
            }
            if (float.IsNaN(to.x) || float.IsNaN(to.z) || (to.magnitude < reach) || passedPoint)
            {
                st.routeIdx++;
                st.stuckTimer = 0f;
                st.reverseCount = 0f;
                return DriveRoute(st, speedLimit);
            }
            // TRÁFEGO NO PIT (visão de 20 metros): ignora carros guardados dentro das garagens (ot.holding)
            // e enxerga qualquer carro no corredor até 20 m à frente para desviar ou reduzir a tempo.
            Vector3 myFwd = st.car.transform.forward; myFwd.y = 0f; myFwd.Normalize();
            // v99: a visão cresce com a velocidade (dá para parar a ~6,5 m/s² antes do carro da frente). A rota assume a ~90 km/h
            // e com 20 m fixos o carro só via o da frente quando já não dava para parar — subiu em cima do companheiro parado na
            // entrada do pit (relato do usuário 03/11; subida medida 4 m/s).
            float vNow = Mathf.Max(0f, st.car.ForwardSpeed);
            CarController blocker = null; float blockD = Mathf.Max(20f, vNow * vNow / (2f * 6.5f) + 10f); Vector3 blockDir = Vector3.zero;
            foreach (var ot in cars)
            {
                if (ot == st || ot.car == null || ot.holding || ot.car.wrecked || !ot.car.gameObject.activeInHierarchy) continue;
                Vector3 dto = ot.car.transform.position - st.car.transform.position; dto.y = 0f;
                float dLen = dto.magnitude;
                if (dLen < 0.8f || dLen > blockD) continue;
                if (Vector3.Angle(myFwd, dto) > 42f) continue;
                // Além dos 20 m de antes, só conta quem está NO CAMINHO (até 3,5 m de lado): com a visão longa o cone de 42° pegava
                // carro destruído FORA do caminho, ao lado da entrada, e todo mundo parava ali e era "resgatado" (autoteste 03/11).
                if (dLen > 20f && Mathf.Abs(Vector3.Cross(myFwd, dto).y) > 3.5f) continue;
                // Ignora carros que já estão recolhidos fundo dentro da garagem (< -21 m).
                if (Vector3.Distance(ot.car.transform.position, ot.boxTarget) < 6.5f) continue;
                // Carro no fundo da garagem VIZINHA (fora do raio de colisão): não bloqueia quem está
                // saindo da própria garagem — era a fila de boxes "cedendo" um pro outro, com o carro
                // rastejando a garagem inteira a ~3 m/s e preso 45 s até o cão de guarda soltar.
                // Carro do lado da PISTA (lateral > −8,5) só é ignorado com ESTE carro já no corredor (a mureta separa os dois);
                // na entrada do pit, o carro parado/lento junto da mureta está no caminho.
                if (dLen > 7f)
                {
                    float dO = race.Track.Checkpoints.RaceDistance(ot.car);
                    var pO = spline.PoseAtDistance(Mathf.Repeat(dO, spline.Length));
                    float latO = Vector3.Dot(ot.car.transform.position - pO.position, pO.rotation * Vector3.right);
                    if (latO < -21.5f || (latO > -8.5f && corridorZone)) continue;
                }
                blocker = ot.car; blockD = dLen; blockDir = dto.normalized;
            }
            float relMe = Mathf.Repeat(race.Track.Checkpoints.RaceDistance(st.car) - laneStart, spline.Length);
            if (!raceBoxExit && PitPriority(st) == 0 && st.reverseTimer <= 0f && st.exitHoldTimer < 6f)
            {
                foreach (var en in race.entries)
                {
                    if (en.car == null || en.car == st.car || en.retired || en.car.wrecked || !en.car.gameObject.activeInHierarchy) continue;
                    var os = Find(en.car);
                    if (os != null && os.holding) continue;
                    bool inFlow = os != null && (os.inPit || os.exiting);
                    if (!inFlow)
                    {
                        if (!en.isHuman) continue;
                        float dh = race.Track.Checkpoints.RaceDistance(en.car);
                        var ph = spline.PoseAtDistance(Mathf.Repeat(dh, spline.Length));
                        if (Vector3.Dot(en.car.transform.position - ph.position, ph.rotation * Vector3.right) > -8f) continue;
                    }
                    else
                    {
                        // Só cede para quem está DE FATO no corredor (lat −21,5..−8,5 e no trecho do
                        // lane) — carro parado no fundo da garagem, mesmo em exiting, NÃO segura a
                        // saída do box (era a fila dos boxes parada por 45 s).
                        float dR = race.Track.Checkpoints.RaceDistance(en.car);
                        float relO = Mathf.Repeat(dR - laneStart, spline.Length);
                        if (relO < 8f || relO > laneLength - 8f) continue;
                        var pO = spline.PoseAtDistance(Mathf.Repeat(dR, spline.Length));
                        float latO = Vector3.Dot(en.car.transform.position - pO.position, pO.rotation * Vector3.right);
                        if (latO > -8.5f || latO < -21.5f) continue;
                    }
                    Vector3 toMe = st.car.transform.position - en.car.transform.position; toMe.y = 0f;
                    float dMe = toMe.magnitude;
                    if (dMe > 20f || dMe < 0.5f) continue; // "perto" = < 20 m (era 28 m)
                    if (Vector3.Dot(en.car.Velocity, toMe / dMe) < 1.5f) continue; // só se MOVENDO na direção
                    // PRIORIDADE no corredor: quem está MAIS ADIANTE segue; o de trás cede — nunca os
                    // dois esperam um pelo outro (o de adiante não cede para o de trás).
                    if (inFlow && relMe >= Mathf.Repeat(race.Track.Checkpoints.RaceDistance(en.car) - laneStart, spline.Length)) continue;
                    st.car.extThrottle = 0f;
                    st.car.extSteer = 0f;
                    st.car.extBrake = st.car.ForwardSpeed > 0.3f ? 0.9f : 0f;
                    st.stuckTimer = 0f; st.stillTimer = 0f;
                    st.exitHoldTimer += Time.deltaTime;
                    WaitingOnPurpose(st);   // esperar a vez na saída não é travamento (v99: o resgate disparava aos 5 s e o "recolocava" no mesmo ponto)
                    return true;
                }
            }
            float avoidBias = 0f;
            bool yielding = false;
            if (blocker != null)
            {
                float side = Vector3.Dot(st.car.transform.right, blockDir);
                float sideSign = Mathf.Abs(side) > 0.05f ? -Mathf.Sign(side) : 1f;
                var bSt = Find(blocker);
                int myPri = PitPriority(st);
                int bPri = bSt != null ? PitPriority(bSt) : 1;
                float bRel = Mathf.Repeat(race.Track.Checkpoints.RaceDistance(blocker) - laneStart, spline.Length);
                yielding = (myPri < bPri || (myPri == bPri && bRel > relNow)) && st.waitTimer < 4f;
                if (yielding)
                {
                    // Sem prioridade: não desvia — cede a vez; dá ré reta se o outro vier de frente.
                    if (blockD < 3.2f && Vector3.Dot(blocker.Velocity, (st.car.transform.position - blocker.transform.position).normalized) > 0.5f && !(corridorZone && latNow > -9.9f))
                    {
                        st.stuckTimer = 0f;
                        if (st.reverseTimer <= 0f) st.reverseTimer = 1.2f;
                        st.reverseTimer -= Time.deltaTime;
                        if (st.reverseTimer > 0f)
                        {
                            st.escapeSide = 0f;
                            st.car.extSteer = 0f;
                            st.car.extThrottle = -0.5f;
                            st.car.extBrake = 0f;
                            return true;
                        }
                    }
                    if (blocker.ForwardSpeed < 2.0f && blockD < 4.5f && st.waitTimer < 2.2f)
                    {
                        st.stuckTimer = 0f;
                        st.waitTimer += Time.deltaTime;
                        st.car.extSteer = Mathf.Clamp(sideSign * 0.7f, -1f, 1f);
                        st.car.extThrottle = 0f;
                        st.car.extBrake = st.car.ForwardSpeed > 0.4f ? 0.85f : 0f;
                        WaitingOnPurpose(st);
                        return true;
                    }
                }
                else if (blockD < 18f && blocker.ForwardSpeed < speedLimit * 0.85f)
                {
                    float weight = Mathf.Clamp01(1f - (blockD - 3f) / 15f);
                    avoidBias = sideSign * (0.45f + 0.35f * weight);
                }
            }
            else st.waitTimer = 0f;
            float latCorr = 0f;
            if (corridorZone)
            {
                // Só bem perto da mureta: acima de −9,4 m (a linha da rota é −10,3 m). Antes o limite era −10 m com a rota
                // em −9,7 m — a correção e a rota brigavam e o carro zigue-zagueava rente à mureta laranja.
                if (latNow > -10.1f)
                {
                    latCorr = -Mathf.Clamp((latNow + 10.1f) * 0.35f, 0f, 0.9f); // puxa para longe da mureta laranja (faixa a −11)
                    if (avoidBias > 0f) avoidBias = 0f;
                }
                else if (Vector3.Distance(st.car.transform.position, st.boxTarget) > 16f && latNow < -19.5f)
                {
                    latCorr = Mathf.Clamp((-19.5f - latNow) * 0.35f, 0f, 0.9f); // puxa de volta para o corredor
                }
                // TRAVA LATERAL DO CORREDOR: enquanto rel < laneLength−10, NENHUM desvio/reversão/
                // correção pode empurrar o carro do pit em direção à PISTA (mureta laranja) — a
                // reentrada na pista só acontece pela rampa de SAÍDA no fim do lane.
                if (relNow < laneLength - 10f && latNow > -10.5f) avoidBias = Mathf.Min(avoidBias, 0f);
            }
            if (st.reverseTimer > 0f)
            {
                if (corridorZone && latNow > -9.9f)
                {
                    // Perto da mureta: nenhuma ré — rasteja reto corrigindo o lateral.
                    st.reverseTimer = 0f;
                    st.car.extThrottle = 0.3f;
                    st.car.extBrake = 0f;
                    st.car.extSteer = Mathf.Clamp(latCorr, -1f, 1f);
                    return true;
                }
                st.reverseTimer -= Time.deltaTime;
                if (st.car.Gear < 0)
                {
                    st.car.extThrottle = 0.3f;
                    st.reverseTimer = Mathf.Max(st.reverseTimer, 1.1f);
                }
                else
                {
                    st.car.extThrottle = -0.55f;
                }
                st.car.extBrake = 0f;
                st.car.extSteer = st.escapeSide * 0.9f;
                return true;
            }
            if (st.car.ForwardSpeed < 0.6f) st.stuckTimer += Time.deltaTime; else st.stuckTimer = 0f;
            if (st.stuckTimer > 2.4f)
            {
                if (st.exiting) Debug.Log("[PIT] " + st.car.name + " preso 2,4 s na saída: " + RouteDiag(st));
                st.stuckTimer = 0f;
                if (st.isHuman && st.inPit && !st.holding && !st.exiting && Vector3.Distance(st.car.transform.position, st.boxTarget) < 24f)
                {
                    st.routeIdx = st.route.Count;
                    return false;
                }
                if (st.reverseCount >= 2f && st.exiting)
                {
                    var pFree = spline.PoseAtDistance(Mathf.Repeat(laneStart + laneLength + 18f, spline.Length));
                    st.car.ResetTo(pFree.position + Vector3.up * 0.4f, pFree.rotation);
                    st.routeIdx = st.route.Count;
                    return false;
                }
                if (st.reverseCount >= 1f && st.routeIdx < st.route.Count &&
                    !(st.exiting && st.routeIdx >= st.route.Count - 3))
                {
                    st.reverseCount = 0f;
                    st.routeIdx++;
                    return true;
                }
                st.reverseCount++;
                st.reverseTimer = Mathf.Min(2.2f, 1.1f + 0.35f * st.reverseCount);
                return true;
            }
            float steer = Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up) / 45f;
            if (float.IsNaN(steer)) steer = 0f;
            st.car.extSteer = Mathf.Clamp(steer + avoidBias + latCorr, -1f, 1f);
            bool ultimo = st.routeIdx >= st.route.Count - 1;
            bool penultimoEntrada = !st.exiting && st.routeIdx == st.route.Count - 2;
            float targetSpeed;
            // CORRIDA: o carro chega no limitador e freia forte só nos últimos metros (como na F1 — no vídeo de referência ele
            // entra na posição em ~0,5 s). Antes rastejava a 2–5 m/s os últimos ~10 m. Treino (garagem apertada) como antes.
            bool fastBox = !PracticeSession;
            if (fastBox && !st.exiting)
            {
                // CORRIDA, entrando (v108): velocidade pela distância que FALTA ATÉ A MARCA (rota + fim da rota até a marca), menos a folga
                // da fila dupla (companheiro sendo atendido → para DoubleStackGap m atrás), com a freada pela aderência real.
                float remainMark = to.magnitude;
                for (int ri = st.routeIdx; ri < st.route.Count - 1; ri++) remainMark += Vector3.Distance(st.route[ri], st.route[ri + 1]);
                if (st.route.Count > 0) { Vector3 endToMark = st.boxTarget - st.route[st.route.Count - 1]; endToMark.y = 0f; remainMark += endToMark.magnitude; }
                var enDr = race.GetEntry(st.car);
                bool busy = enDr != null && st.stopTimer <= 0f && BoxBusyByOther(st, enDr);
                float stopAt = busy ? DoubleStackGap : 0.9f;
                float vStop = Mathf.Sqrt(2f * PitApproachDecel() * Mathf.Max(0f, remainMark - stopAt));
                targetSpeed = Mathf.Min(speedLimit, busy ? vStop : Mathf.Max(1.2f, vStop));
            }
            else if (!st.exiting && ultimo)
                targetSpeed = Mathf.Clamp(to.magnitude * 0.40f, 2.0f, 5.0f);
            else if (penultimoEntrada)
                targetSpeed = Mathf.Clamp(to.magnitude * 0.45f, 4.0f, 7.5f);
            else if (st.exiting)
                targetSpeed = speedLimit; // saída: limitador até a pista (curvas da garagem ficam com o teto de 16 km/h abaixo)
            else
            {
                // Entrada: limitador (80 km/h; 60 no Principado) no corredor e freada só para a garagem, pela distância
                // que FALTA na rota. Antes: 0,55 × distância até o PRÓXIMO ponto (5–10 m) → 6 m/s ≈ 22 km/h no pit inteiro.
                float remain = to.magnitude;
                for (int ri = st.routeIdx; ri < st.route.Count - 1; ri++) remain += Vector3.Distance(st.route[ri], st.route[ri + 1]);
                targetSpeed = Mathf.Min(speedLimit, 2.0f + Mathf.Sqrt(2f * 4.5f * Mathf.Max(0f, remain - 6f)));   // treino (garagem)
            }
            float turnAngle = Mathf.Abs(Vector3.SignedAngle(st.car.transform.forward, to.normalized, Vector3.up));
            // 30 km/h só na DIAGONAL DENTRO DO PRÓPRIO BOX (últimos pontos da rota de entrada, ~36°). Na curva de entrada do pit
            // lane (vindo da pista) e nas demais continua 16 km/h — teste 30/10: com 30 km/h ali o carro errou a entrada
            // ("CONTRAMÃO"), voltou para a pista e bateu.
            bool boxDiagonal = fastBox && !st.exiting && st.routeIdx >= st.route.Count - 7;
            if (!ultimo && turnAngle > 22f)
                targetSpeed = Mathf.Min(targetSpeed, (boxDiagonal && turnAngle < 45f ? 30f : 16f) / 3.6f);
            if (blocker != null)
            {
                if (yielding)
                {
                    // Cedeu a vez: para 5 m antes do bloqueador (freia de vez quando chega perto).
                    float safeBrakeSpeed = Mathf.Max(0f, blocker.ForwardSpeed) + Mathf.Sqrt(2f * 6.5f * Mathf.Max(0f, blockD - 5f - 4.0f));
                    targetSpeed = Mathf.Min(targetSpeed, safeBrakeSpeed);
                    if (blockD < 6f)
                    {
                        st.waitTimer += Time.deltaTime;
                        st.car.extThrottle = 0f;
                        st.car.extBrake = st.car.ForwardSpeed > 0.4f ? 0.85f : 0f;
                        WaitingOnPurpose(st);
                        return true;
                    }
                }
                else
                {
                    float safeBrakeSpeed = Mathf.Max(0f, blocker.ForwardSpeed) + Mathf.Sqrt(2f * 6.5f * Mathf.Max(0f, blockD - 4.0f));
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(2.0f, safeBrakeSpeed));
                }
            }
            float speed = st.car.ForwardSpeed;
            if (fastBox && !st.exiting)
            {
                // v108: segue a curva de freada de perto (antes só freava com 1–2 m/s acima do alvo, proporcional /4 — chegava rápido)
                st.car.extThrottle = speed < targetSpeed - 0.3f ? 0.85f : 0f;
                st.car.extBrake = speed > targetSpeed + 0.3f ? Mathf.Clamp((speed - targetSpeed) / 1.5f, 0.4f, 1f) : 0f;
                return true;
            }
            st.car.extThrottle = speed < targetSpeed ? 0.85f : 0f;
            // Entrando no box: freia já com 1 m/s acima do alvo (com 2 m/s de folga chegava rápido e passava da equipe).
            float brakeGap = !st.exiting && st.routeIdx >= st.route.Count - 2 ? 1.0f : 2.0f;
            st.car.extBrake = speed > targetSpeed + brakeGap ? Mathf.Clamp((speed - targetSpeed) / 4f, 0.35f, 1f) : 0f;
            return true;
        }

        /// <summary>Duração real do composto em voltas (Macio ~18, Médio ~28, Duro ~40 — pedido do usuário, v79).</summary>
        static int CompoundLife(int compound) => Mathf.RoundToInt(TyreModel.LifeLaps(compound));

        /// <summary>Vida do composto em voltas (para a UI de estratégia).</summary>
        public static int CompoundLifeLaps(int compound) => CompoundLife(TyreModel.Clamp(compound));

        void SetTyreTemps(CarState st, float c)
        {
            for (int i = 0; i < 4; i++) st.tempW[i] = c;
        }

        /// <summary>Pneu novo na temperatura de saída do composto ATUAL (cobertor por composto; chuva sem cobertor).</summary>
        void Blanket(CarState st, float extra = 0f)
        {
            float track = WeatherSystem.Active ? WeatherSystem.TrackTemp : 32f;
            SetTyreTemps(st, TyreModel.BlanketFor(st.compound, track) + extra);
        }

        /// <summary>IA: estratégia da EQUIPE para este carro (corrida) ou programa de treino.</summary>
        void PlanNpc(CarState st, RaceEntry e, float tier01)
        {
            st.strategy.Clear();
            st.stopLaps.Clear();
            st.stopsDone = 0;
            bool practice = race.Settings.laps >= 900;
            if (practice)
            {
                bool quali = race.Settings.careerPhase >= RaceX.Career.CareerMode.PH_SQ1 && race.Settings.careerPhase != RaceX.Career.CareerMode.PH_FP3
                             && race.Settings.careerPhase != RaceX.Career.CareerMode.PH_SPRINT && race.Settings.careerPhase != RaceX.Career.CareerMode.PH_RACE;
                st.runIndex = Mathf.Abs(e.name != null ? e.name.GetHashCode() : 0) % 3; // equipes começam o programa em pontos diferentes
                TeamStrategy.PracticeRun(st.runIndex, tier01, quali, out int c, out int l);
                st.compound = TyreModel.NeedsWetTyre(WeatherWetness()) ? TyreModel.BestForWetness(WeatherWetness(), c) : c;
                st.runLaps = l;
                st.strategy.Add(st.compound);
                return;
            }
            int laps = Mathf.Max(2, race.Settings.laps);
            float jitter = ((Mathf.Abs(e.name != null ? e.name.GetHashCode() : 7) % 200) / 100f) - 1f;
            // Sprint (e corridas curtas do Single Player) não têm troca obrigatória de composto, como a Sprint da F1.
            bool sprintLike = race.Settings.careerPhase == RaceX.Career.CareerMode.PH_SPRINT || laps <= TeamStrategy.SprintMaxLaps;
            var plan = TyreModel.NeedsWetTyre(WeatherWetness()) ? TeamStrategy.WetPlan(laps, WeatherWetness()) : TeamStrategy.BestDryPlan(laps, st.wearMul, jitter, sprintLike);
            st.compound = plan.compounds[0];
            st.strategy.AddRange(plan.compounds);
            st.stopLaps.AddRange(plan.stopLaps);
            st.plannedStops = plan.stopLaps.Count;
            st.plannedLap = st.stopLaps.Count > 0 ? st.stopLaps[0] : int.MaxValue;
        }

        /// <summary>Umidade para decidir o pneu: a da pista OU a que a chuva atual vai dar em instantes (a equipe olha o
        /// radar — a pista leva ~1,5 min para passar de 20% com chuva média e antes disso ia todo mundo de slick).</summary>
        public static float WeatherWetness() => Mathf.Max(WeatherSystem.Wetness, WeatherSystem.EquilibriumWetness);

        /// <summary>Pneu do modo AUTO para o clima: chuva → intermediário/chuva; pista seca com pneu de chuva → médio.</summary>
        public static int AutoCompoundForWeather(int current)
        {
            float w = WeatherWetness();
            if (TyreModel.NeedsWetTyre(w)) return TyreModel.BestForWetness(w, current);
            return TyreModel.IsWetTyre(current) ? TyreModel.Medium : current;
        }

        /// <summary>Treino: o jogador escolhe no HUB o composto da próxima saída (testes de pneu).</summary>
        void ApplyHumanPracticeCompound(CarState st)
        {
            var d = RaceX.Career.CareerMode.data;
            if (d == null || d.practiceCompound < 0) return;
            st.compound = ManualForWater(st, TyreModel.Clamp(d.practiceCompound));
            st.strategy.Clear();
            st.strategy.Add(st.compound);
        }

        /// <summary>Pneu escolhido À MÃO no hub × água da pista: slick com a pista pedindo pneu de chuva (ou pneu de chuva
        /// com a pista seca) a equipe NÃO monta — monta o certo para a água e avisa no rádio (relato do usuário, 01/11:
        /// Q3 com 18% de água e o carro saiu de macio porque o macio estava marcado no hub). Tempo compatível: vale a escolha.</summary>
        int ManualForWater(CarState st, int manual)
        {
            float w = WeatherWetness();
            if (TyreModel.NeedsWetTyre(w) == TyreModel.IsWetTyre(manual)) return manual;
            int c = AutoCompoundForWeather(manual);
            if (c != manual && st.isHuman)
                Radio(st, (TyreModel.IsWetTyre(c) ? "Pista molhada" : "Pista seca") + ": montamos " + TyreModel.Name(c) + " no lugar do " + TyreModel.Name(manual) + " escolhido");
            return c;
        }

        /// <summary>Hub: troca o composto do jogador AGORA se ele estiver parado na garagem do treino.</summary>
        public static bool FitPlayerPracticeCompoundNow(int compound)
        {
            if (instance == null || instance.race == null || instance.race.Settings.laps < 900) return false;
            foreach (var e in instance.race.entries)
            {
                if (!e.isHuman) continue;
                var st = instance.Find(e.car);
                if (st == null || !st.holding) return false;
                st.compound = instance.ManualForWater(st, TyreModel.Clamp(compound));
                st.wear = 0f;
                for (int i = 0; i < 4; i++) { st.wearW[i] = 0f; st.puncture[i] = false; st.pressure[i] = 1f; st.leak[i] = 0f; st.dropShown[i] = 0f; }
                instance.Blanket(st);
                return true;
            }
            return false;
        }

        /// <summary>Hub/pausa: leva o jogador para a GARAGEM (treino/classificação) — o carro não fica
        /// parado na pista levando batida enquanto o menu está aberto.</summary>
        public static void SendPlayerToGarage()
        {
            if (instance == null || instance.race == null || instance.race.Settings.laps < 900) return;
            foreach (var e in instance.race.entries)
            {
                if (!e.isHuman) continue;
                var st = instance.Find(e.car);
                if (st == null || st.holding) return;
                var pose = instance.PitSpawnPose(instance.BoxOf(e), instance.GarageLane(e));
                e.car.ResetTo(pose.position, pose.rotation);
                st.exiting = false;
                st.requested = false;
                st.inPit = true;
                st.holding = true;
                st.releaseTimer = 2f;
                st.route.Clear();
                st.routeIdx = 0;
                instance.ApplyHold(st);
                instance.race.Track.Checkpoints.ReSyncAfterPit(e.car);
                e.outLap = true;
                return;
            }
        }

        /// <summary>Quantas paradas a estratégia precisa: soma das vidas dos stints até cobrir as voltas.</summary>
        static int StopsNeeded(int laps, int startC, int stopC)
        {
            int covered = CompoundLife(startC);
            int stops = 0;
            while (covered < laps && stops < 3) { covered += CompoundLife(stopC); stops++; }
            return stops;
        }

        /// <summary>Volta de cada parada, proporcional à vida de cada stint (o macio sai antes do duro).</summary>
        static void FillStopLaps(List<int> stopLaps, int laps, int startC, int stopC, int stops)
        {
            var cs = new List<int>();
            for (int i = 0; i < stops; i++) cs.Add(stopC);
            FillStopLaps(stopLaps, laps, startC, cs);
        }

        /// <summary>Mesma conta com um composto POR PARADA (estratégia de verdade: M→H→S etc.).</summary>
        static void FillStopLaps(List<int> stopLaps, int laps, int startC, List<int> stopCs)
        {
            stopLaps.Clear();
            int stops = stopCs != null ? Mathf.Min(3, stopCs.Count) : 0;
            if (stops <= 0) return;
            float total = CompoundLife(startC);
            for (int i = 0; i < stops; i++) total += CompoundLife(stopCs[i]);
            float acc = CompoundLife(startC);
            int prev = 1;
            for (int i = 0; i < stops; i++)
            {
                int lap = Mathf.Clamp(Mathf.RoundToInt(laps * acc / total), prev + 1, laps - 1);
                stopLaps.Add(lap);
                prev = lap;
                acc += CompoundLife(stopCs[i]);
            }
        }

        /// <summary>Corrida JÁ EM ANDAMENTO: mudar a estratégia NÃO troca o pneu montado (bug relatado: pedi duro e o pneu
        /// mudou de cor no meio da pista; o carro seguia com o pneu antigo). As paradas feitas ficam; as que faltam viram
        /// as do novo plano (stopCs[i] = composto da parada i), redistribuídas nas voltas que restam.</summary>
        bool ApplyPlanMidRace(CarState st, List<int> stopCs)
        {
            if (race == null || race.Settings.laps >= 900) return false;
            var e = race.GetEntry(st.car);
            var pg = e != null ? race.GetProgress(e) : null;
            bool started = race.State == RaceState.Racing || race.State == RaceState.Finished || st.stopsDone > 0 || (pg != null && pg.lap > 1);
            if (!started) return false;
            int done = st.stopsDone;
            var hist = new List<int>();
            for (int i = 0; i <= done; i++) hist.Add(i < st.strategy.Count ? st.strategy[i] : st.compound);
            hist[done] = st.compound;   // o que está montado AGORA
            for (int i = done; i < stopCs.Count; i++) hist.Add(TyreModel.Clamp(stopCs[i]));
            st.strategy.Clear();
            st.strategy.AddRange(hist);
            while (st.stopLaps.Count > done) st.stopLaps.RemoveAt(st.stopLaps.Count - 1);
            var future = new List<int>();
            for (int i = done + 1; i < st.strategy.Count; i++) future.Add(st.strategy[i]);
            int lapNow = pg != null ? Mathf.Max(1, pg.lap) : 1;
            FillStopLapsFrom(st.stopLaps, lapNow, Mathf.Max(2, race.Settings.laps), st.compound, st.wear, future);
            st.plannedStops = st.stopLaps.Count;
            while (st.strategy.Count > st.plannedStops + 1) st.strategy.RemoveAt(st.strategy.Count - 1);
            st.plannedLap = st.stopsDone < st.stopLaps.Count ? st.stopLaps[st.stopsDone] : -1;
            return true;
        }

        /// <summary>Paradas a partir da volta atual: divide as voltas que FALTAM pela vida que resta do pneu montado (pelo
        /// desgaste já gasto) e a vida de cada composto futuro. Acrescenta à lista (as paradas já feitas ficam antes).</summary>
        static void FillStopLapsFrom(List<int> stopLaps, int lapNow, int laps, int curC, float curWear, List<int> futureCs)
        {
            int stops = futureCs != null ? Mathf.Min(3, futureCs.Count) : 0;
            if (stops <= 0) return;
            float cur = CompoundLife(curC) * Mathf.Clamp01(1f - curWear);
            float total = cur;
            for (int i = 0; i < stops; i++) total += CompoundLife(futureCs[i]);
            int remain = Mathf.Max(1, laps - lapNow + 1);
            float acc = cur;
            int prev = lapNow - 1;
            for (int i = 0; i < stops; i++)
            {
                int lap = Mathf.Clamp(lapNow + Mathf.RoundToInt(remain * acc / Mathf.Max(1f, total)) - 1, Mathf.Max(lapNow, prev + 1), laps - 1);
                if (lap <= prev || lap >= laps) break;
                stopLaps.Add(lap);
                prev = lap;
                acc += CompoundLife(futureCs[i]);
            }
        }

        /// <summary>Voltas de box com composto por parada (hub da carreira).</summary>
        public static List<int> StopLapsFor(int laps, int startC, List<int> stopCs)
        {
            var l = new List<int>();
            var cs = new List<int>();
            foreach (int c in stopCs) cs.Add(TyreModel.Clamp(c));
            FillStopLaps(l, Mathf.Max(2, laps), TyreModel.Clamp(startC), cs);
            return l;
        }

        /// <summary>Idem, com a PREVISÃO: a parada para pneu de chuva fica na volta em que a chuva chega (e a volta ao slick na volta
        /// em que para). Pedido do usuário (04/11): "se a equipe já sabe quando a chuva vem, que fique no plano", não só o aviso.</summary>
        public static List<int> StopLapsFor(int laps, int startC, List<int> stopCs, WeatherForecast fc)
        {
            var l = StopLapsFor(laps, startC, stopCs);
            PinRainStops(l, Mathf.Max(2, laps), TyreModel.Clamp(startC), stopCs, fc);
            return l;
        }

        /// <summary>Voltas da chuva pela previsão da sessão (-1 = não há): início e fim, em voltas da corrida.</summary>
        static void RainLaps(WeatherForecast fc, int laps, out int rainLap, out int dryLap)
        {
            rainLap = -1; dryLap = -1;
            if (fc.rainStart < 0f) return;
            bool toWet = fc.changeTo >= WeatherKind.LightRain;       // seco → chuva no meio da corrida
            bool toDry = fc.kind >= WeatherKind.LightRain && !toWet; // começa na chuva e seca
            int at = Mathf.Clamp(Mathf.RoundToInt(fc.rainStart * laps), 1, laps - 1);
            int end = fc.rainEnd >= 0f ? Mathf.Clamp(Mathf.RoundToInt(fc.rainEnd * laps), at + 1, laps - 1) : -1;
            if (toWet) { rainLap = at; dryLap = end; }
            else if (toDry) { dryLap = at; }
        }

        /// <summary>Fixa as paradas de pneu de chuva/slick nas voltas da previsão; as demais se redistribuem entre elas.</summary>
        static void PinRainStops(List<int> stopLaps, int laps, int startC, List<int> stopCs, WeatherForecast fc)
        {
            if (stopLaps == null || stopCs == null || stopLaps.Count == 0) return;
            RainLaps(fc, laps, out int rainLap, out int dryLap);
            if (rainLap < 0 && dryLap < 0) return;
            int n = Mathf.Min(stopLaps.Count, stopCs.Count);
            int prevC = startC, lo = 1;
            for (int i = 0; i < n; i++)
            {
                int c = TyreModel.Clamp(stopCs[i]);
                bool wetIn = TyreModel.IsWetTyre(c) && !TyreModel.IsWetTyre(prevC);   // slick → chuva
                bool wetOut = !TyreModel.IsWetTyre(c) && TyreModel.IsWetTyre(prevC);  // chuva → slick
                int pin = wetIn && rainLap > 0 ? rainLap : wetOut && dryLap > 0 ? dryLap : -1;
                if (pin > 0)
                {
                    pin = Mathf.Clamp(pin, lo, laps - 1 - (n - 1 - i));
                    // paradas anteriores ainda não fixadas ficam proporcionais entre a última fixa e esta
                    int first = i; while (first > 0 && stopLaps[first - 1] >= pin) first--;
                    for (int k = first; k < i; k++) stopLaps[k] = Mathf.Clamp(lo + Mathf.RoundToInt((pin - lo) * (k - first + 1f) / (i - first + 1f)), lo, pin - 1);
                    stopLaps[i] = pin;
                }
                else if (stopLaps[i] < lo) stopLaps[i] = Mathf.Min(laps - 1, lo);
                lo = stopLaps[i] + 1;
                prevC = c;
            }
        }

        /// <summary>Estratégia do jogador com composto POR PARADA.</summary>
        public static void SetPlayerStrategy(CarController car, int startC, List<int> stopCs)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return;
            startC = TyreModel.Clamp(startC);
            var cs = new List<int>();
            for (int i = 0; i < stopCs.Count && i < 3; i++) cs.Add(TyreModel.Clamp(stopCs[i]));
            if (instance.ApplyPlanMidRace(st, cs)) return;
            st.compound = startC;
            st.plannedStops = cs.Count;
            st.strategy.Clear();
            st.stopLaps.Clear();
            st.strategy.Add(startC);
            st.strategy.AddRange(cs);
            var rm = instance.race;
            if (rm == null || rm.Settings.laps >= 900) return;
            FillStopLaps(st.stopLaps, Mathf.Max(2, rm.Settings.laps), startC, cs);
            PinRainStops(st.stopLaps, Mathf.Max(2, rm.Settings.laps), startC, cs, WeatherSystem.CurrentForecast);
            if (st.stopsDone > st.plannedStops) st.stopsDone = st.plannedStops;
        }

        /// <summary>Carreira: aplica a estratégia escolhida no HUB (paradas e compostos) ao jogador.</summary>
        void ApplyCareerHubStrategy(CarState st)
        {
            if (race == null || race.Settings.careerPhase < 0 || race.Settings.laps >= 900) return;
            var d = RaceX.Career.CareerMode.data;
            if (d == null) return;
            if (d.stratStops < 0)
            {
                // Sem plano escolhido: vale o PLANO DA EQUIPE (dados do fim de semana + clima + ritmo), não um pneu fixo.
                var fcA = WeatherSystem.ForecastFor(d.season, d.round, d.weekendPhase, (int)RaceX.Career.CareerMode.RoundTrack(d.round));
                d.stratPace = SuggestPace(race.Settings.laps, fcA, out _);   // ritmo sugerido pelo desgaste medido
                string noteA = TeamProposal(race.Settings.laps, fcA, d.stratPace, out int sA, out var csA);
                d.stratStart = sA; d.stratStints.Clear(); d.stratStints.AddRange(csA); d.stratStops = csA.Count;
                d.stratTeamNote = noteA;
            }
            int laps = Mathf.Max(2, race.Settings.laps);
            int startC = TyreModel.Clamp(d.stratStart), stopC = TyreModel.Clamp(d.stratStop);
            st.compound = startC;
            st.plannedStops = Mathf.Clamp(d.stratStops, 0, 3);
            st.stopsDone = 0;
            st.strategy.Clear();
            st.strategy.Add(startC);
            var cs = new List<int>();
            for (int i = 0; i < st.plannedStops; i++) cs.Add(TyreModel.Clamp(i < d.stratStints.Count ? d.stratStints[i] : stopC));
            st.strategy.AddRange(cs);
            FillStopLaps(st.stopLaps, laps, startC, cs);
            PinRainStops(st.stopLaps, laps, startC, cs, WeatherSystem.CurrentForecast);   // parada de chuva na volta prevista
        }

        /// <summary>Estratégia automática sugerida (usada pelo hub da carreira para mostrar o padrão).</summary>
        public static int SuggestedStops(int laps, int startC, int stopC) => StopsNeeded(Mathf.Max(2, laps), TyreModel.Clamp(startC), TyreModel.Clamp(stopC));

        /// <summary>Voltas de box de uma estratégia (texto do hub: "BOX V24").</summary>
        public static List<int> StopLapsFor(int laps, int startC, int stopC, int stops)
        {
            var l = new List<int>();
            FillStopLaps(l, Mathf.Max(2, laps), TyreModel.Clamp(startC), TyreModel.Clamp(stopC), Mathf.Clamp(stops, 0, 3));
            return l;
        }

        /// <summary>Estratégia padrão do JOGADOR: paradas calculadas das voltas da corrida e da vida do
        /// composto (Médio ~14 voltas, Duro ~22), box distribuído uniformemente; sessões de tempo
        /// (treino/classificação) não têm estratégia.</summary>
        void AutoStrategy(CarState st)
        {
            st.plannedStops = 0;
            st.stopsDone = 0;
            st.strategy.Clear();
            st.stopLaps.Clear();
            st.strategy.Add(st.compound);
            if (race == null || race.Settings.laps >= 900) return; // treino/quali: pneus até o fim
            int laps = Mathf.Max(2, race.Settings.laps);
            int startC = st.compound;
            int stopC = 2; // M → H (padrão conservador)
            int stops = StopsNeeded(laps, startC, stopC);
            st.plannedStops = stops;
            for (int i = 0; i < stops; i++) st.strategy.Add(stopC);
            FillStopLaps(st.stopLaps, laps, startC, stopC, stops);
        }

        /// <summary>Paradas planejadas do JOGADOR (0..3). -1 = sem pit ativo.</summary>
        public static int PlannedStops(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st == null ? -1 : st.plannedStops;
        }

        /// <summary>Paradas já feitas pelo JOGADOR.</summary>
        public static int StopsDone(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null ? st.stopsDone : 0;
        }

        /// <summary>Volta da PRÓXIMA parada planejada (1-based); -1 = sem paradas restantes.</summary>
        public static int NextStopLap(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null || st.stopsDone >= st.stopLaps.Count) return -1;
            return st.stopLaps[st.stopsDone];
        }

        /// <summary>Composto que a equipe vai montar NESTA parada (próximo da estratégia; no treino, o atual) — pneus
        /// novos nas mãos dos mecânicos (PitCrewSystem).</summary>
        public static int FittingCompound(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return TyreModel.Medium;
            // MESMA regra do fim da parada (workDone): composto do próximo stint e, na corrida, a troca forçada pela água.
            // Antes a equipe mostrava um pneu e o carro saía com outro.
            int c = st.compound;
            if (!PracticeSession && st.stopsDone < st.strategy.Count - 1) c = TyreModel.Clamp(st.strategy[st.stopsDone + 1]);
            if (!PracticeSession)
            {
                float wet = WeatherWetness();
                int best = TyreModel.BestForWetness(wet, TyreModel.IsWetTyre(c) ? 1 : c);
                if (best != c && (TyreModel.IsWetTyre(best) || TyreModel.IsWetTyre(c))) c = best;
            }
            return c;
        }

        /// <summary>Composto de largada da estratégia do JOGADOR (strategy[0]).</summary>
        public static int StrategyStartCompound(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.strategy.Count > 0 ? st.strategy[0] : 1;
        }

        /// <summary>Composto usado nas paradas da estratégia do JOGADOR (strategy[1]).</summary>
        public static int StrategyStopCompound(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && st.strategy.Count > 1 ? st.strategy[1] : 2;
        }

        /// <summary>Mensagem inicial do rádio: "1 parada, M → H, box na volta 12".</summary>
        public static string StrategyText(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return "";
            if (st.plannedStops <= 0) return "Sem paradas, " + CompoundLetter(st.compound) + " até o fim";
            var seq = new System.Text.StringBuilder();
            for (int i = 0; i < st.strategy.Count; i++)
            {
                if (i > 0) seq.Append(" → ");
                seq.Append(CompoundLetter(st.strategy[i]));
            }
            string p = st.plannedStops == 1 ? " parada" : " paradas";
            return st.plannedStops + p + ", " + seq + ", box na volta " + st.stopLaps[0];
        }

        /// <summary>Linha curta do HUD sob o pneu: "PARADAS 0/1 · BOX V12".</summary>
        public static string PitPlanText(CarController car)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return "";
            if (st.plannedStops <= 0) return "PARADAS 0/0 · SEM PARADAS";
            int next = st.stopsDone < st.stopLaps.Count ? st.stopLaps[st.stopsDone] : -1;
            return "PARADAS " + st.stopsDone + "/" + st.plannedStops + (next > 0 ? " · BOX V" + next : " · BOX —");
        }

        /// <summary>Jogador reconfigura a estratégia no menu de pausa (não vale para a IA).</summary>
        public static void SetPlayerStrategy(CarController car, int stops, int startC, int stopC)
        {
            var st = instance != null ? instance.Find(car) : null;
            if (st == null) return;
            stops = Mathf.Clamp(stops, 0, 3);
            startC = TyreModel.Clamp(startC);
            stopC = TyreModel.Clamp(stopC);
            {
                var csm = new List<int>();
                for (int i = 0; i < stops; i++) csm.Add(stopC);
                if (instance.ApplyPlanMidRace(st, csm)) return;
            }
            st.compound = startC;
            st.plannedStops = stops;
            st.strategy.Clear();
            st.stopLaps.Clear();
            st.strategy.Add(startC);
            for (int i = 0; i < stops; i++) st.strategy.Add(stopC);
            var rm = instance.race;
            if (rm == null || rm.Settings.laps >= 900) return; // sessão por tempo: sem paradas
            int laps = Mathf.Max(2, rm.Settings.laps);
            FillStopLaps(st.stopLaps, laps, startC, stopC, stops);
            if (st.stopsDone > st.plannedStops) st.stopsDone = st.plannedStops;
        }

        RaceEntry EntryOf(CarController c)
        {
            foreach (var e in race.entries) if (e.car == c) return e;
            return null;
        }

        float CurvatureAt(float s)
        {
            const float span = 8f;
            Vector3 a = spline.PoseAtDistance(s - span).rotation * Vector3.forward;
            Vector3 b = spline.PoseAtDistance(s + span).rotation * Vector3.forward;
            return Vector3.Angle(a, b) * Mathf.Deg2Rad / (2f * span);
        }

        void BuildGeometry()
        {
            // Material físico SEM bounce para todo o pit: encostou na parede, desliza — não ricocheteia.
            // Unity 6: PhysicMaterial virou PhysicsMaterial (o antigo é erro de compilação).
            var pitPhys = new PhysicsMaterial("PitNoBounce")
            {
                bounciness = 0f,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            try
            {
                // Shader por FALLBACK: no APK (IL2CPP) Shader.Find pode vir null para shaders
                // não referenciados em cena (mesmo bug histórico do SkidMarks). Sem fallback,
                // ArgumentNullException mata o RaceBootstrap.Start -> loading travado em 95%.
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Sprites/Default")
                          ?? Shader.Find("Unlit/Color");
                if (shader == null) return; // sem shader válido: corrida funciona sem visual do pit
                int steps = 24;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= steps; i++)
            {
                float s = Mathf.Repeat(laneStart + laneLength * i / steps, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                // Faixa asfáltica de 12 m (FIA): 8 m de FAST LANE (lado do muro/pista) + 4 m de WORKING LANE (lado das garagens).
                // LADO DO PIT: mesmo lado do laneCenter (−right). Antes estava no lado OPOSTO da pista.
                verts.Add(p.position - right * (laneOffset + 6f) + Vector3.up * 0.02f);
                verts.Add(p.position - right * (laneOffset - 6f) + Vector3.up * 0.02f);
                if (i < steps)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            var go = new GameObject("PitLane");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(shader) { color = new Color(0.13f, 0.13f, 0.15f) };
            // SUPERFÍCIE REAL: collider + asfalto — o corredor do pit tem o MESMO grip da pista
            // (antes era o Ground de grama por baixo: o carro deslizava e não virava na zona do pit).
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;

            // ROTA VISUAL: linha tracejada LARANJA no meio da faixa (da entrada ao box) — o caminho do pit.
            var dashes = new List<Vector3>();
            var dashTris = new List<int>();
            int n = 0;
            for (int i = 0; i < steps; i += 2)
            {
                float s = Mathf.Repeat(laneStart + laneLength * i / steps, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                Vector3 c = p.position - right * FastLaneLat + Vector3.up * 0.07f;   // rota = faixa rápida
                dashes.Add(c + right * 0.35f); dashes.Add(c - right * 0.35f);
                dashes.Add(c + right * 0.35f + Vector3.up * 0.01f); dashes.Add(c - right * 0.35f + Vector3.up * 0.01f);
                if (i + 2 < steps)
                {
                    int b = n * 4;
                    dashTris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
                n++;
            }
            var dmesh = new Mesh { vertices = dashes.ToArray(), triangles = dashTris.ToArray() };
            dmesh.RecalculateNormals();
            var dgo = new GameObject("PitRoute");
            dgo.transform.SetParent(transform, false);
            dgo.AddComponent<MeshFilter>().sharedMesh = dmesh;
            var dmr = dgo.AddComponent<MeshRenderer>();
            dmr.sharedMaterial = new Material(shader) { color = new Color(1f, 0.55f, 0.1f) };

            // TRACEJADO AZUL até a entrada do box (vídeo de referência, 6,5–14 s): faixa pintada no asfalto do lado do
            // pit nos ~280 m antes da entrada, inclusive na última curva. Traços de 3 m com 3 m de vão, 25 cm de largura
            // (medidas de projeto). Só visual, dupla face.
            // v81c: a linha DOBRA para dentro do pit lane pelo mesmo caminho da rota de entrada (sai da pista 30 m antes e
            // termina na faixa rápida) — antes terminava reta na beirada da pista e "passava direto" pela entrada. E só
            // aparece com o box pedido / chamado pela equipe (UpdateEntryLine).
            try
            {
                float halfRoad = race != null && race.Track != null && race.Track.Definition != null ? race.Track.Definition.roadWidth * 0.5f : 6f;
                var bv = new List<Vector3>();
                var bt = new List<int>();
                float off = Mathf.Max(0.5f, halfRoad - 0.9f);
                // lateral (lado do pit) por distância até o início do lane: beirada da pista → pontos da rota de entrada
                float LatAt(float rel)
                {
                    if (rel <= -45f) return off;
                    if (rel <= -30f) return Mathf.Lerp(off, 7.0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-45f, -30f, rel)));
                    if (rel <= -12f) return Mathf.Lerp(7.0f, 10.6f, Mathf.InverseLerp(-30f, -12f, rel));
                    if (rel <= 8f) return Mathf.Lerp(10.6f, FastLaneLat + 0.6f, Mathf.InverseLerp(-12f, 8f, rel));
                    return FastLaneLat + 0.6f;
                }
                for (float d = 280f; d > -25f; d -= 6f)
                {
                    var p0 = spline.PoseAtDistanceSmooth(Mathf.Repeat(laneStart - d, spline.Length));
                    var p1 = spline.PoseAtDistanceSmooth(Mathf.Repeat(laneStart - d + 3f, spline.Length));
                    Vector3 r0 = p0.rotation * Vector3.right, r1 = p1.rotation * Vector3.right;
                    Vector3 a0 = p0.position - r0 * LatAt(-d) + Vector3.up * 0.03f, a1 = p1.position - r1 * LatAt(-d + 3f) + Vector3.up * 0.03f;
                    int b0 = bv.Count;
                    bv.Add(a0 - r0 * 0.125f); bv.Add(a0 + r0 * 0.125f); bv.Add(a1 - r1 * 0.125f); bv.Add(a1 + r1 * 0.125f);
                    bt.AddRange(new[] { b0, b0 + 2, b0 + 1, b0 + 1, b0 + 2, b0 + 3, b0, b0 + 1, b0 + 2, b0 + 1, b0 + 3, b0 + 2 });
                }
                var bmesh = new Mesh { vertices = bv.ToArray(), triangles = bt.ToArray() };
                bmesh.RecalculateNormals();
                var bgo = new GameObject("PitEntryLine");
                bgo.transform.SetParent(transform, false);
                bgo.AddComponent<MeshFilter>().sharedMesh = bmesh;
                var bmr = bgo.AddComponent<MeshRenderer>();
                bmr.sharedMaterial = new Material(shader) { color = new Color(0.1f, 0.38f, 0.95f) };
                bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bmr.enabled = false;   // aparece só com o box pedido / chamado (UpdateEntryLine)
                entryLineR = bmr;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Pit] tracejado azul ignorado: " + ex.Message); }

            // PIT COMPLETO + ACESSÍVEL: estrutura real do paddock (garagens, portas, muros,
            // gantries) e RAMPAS de asfalto ligando a PISTA ao pit lane na entrada e na saída
            // (sem rampa ninguém consegue entrar — o muro nunca cobre entrada/saída).
            var wall = new Material(shader) { color = new Color(0.85f, 0.3f, 0.12f) };
            var building = new Material(shader) { color = new Color(0.62f, 0.63f, 0.66f) };
            var door = new Material(shader) { color = new Color(0.18f, 0.2f, 0.24f) };
            var floor = new Material(shader) { color = new Color(0.24f, 0.25f, 0.28f) };
            // LAJE DO PADDOCK: concreto contínuo, mesma cor do piso das garagens, do corredor (−8 m) até atrás das
            // garagens (−37 m) e por todo o comprimento do pit. Antes a grama aparecia entre as garagens (vãos de 0,4 m),
            // antes da primeira e depois da última — verde colado no cinza. Só visual (corredor e garagens têm colisor).
            {
                var sv = new List<Vector3>();
                var st2 = new List<int>();
                int slabSteps = steps * 3;   // mais segmentos: em pit lane curvo os vãos da corda mostravam grama
                for (int i = 0; i <= slabSteps; i++)
                {
                    float s0 = Mathf.Repeat(laneStart - 6f + (laneLength + 12f) * i / slabSteps, spline.Length);
                    var p0 = spline.PoseAtDistance(s0);
                    Vector3 r0 = p0.rotation * Vector3.right;
                    sv.Add(p0.position - r0 * 37.5f + Vector3.up * 0.012f);
                    sv.Add(p0.position - r0 * (laneOffset - 6f) + Vector3.up * 0.012f);
                    if (i < slabSteps) { int b0 = i * 2; st2.AddRange(new[] { b0, b0 + 2, b0 + 1, b0 + 1, b0 + 2, b0 + 3 }); }
                }
                var slabMesh = new Mesh { vertices = sv.ToArray(), triangles = st2.ToArray() };
                slabMesh.RecalculateNormals();
                var slab = new GameObject("PaddockSlab");
                slab.transform.SetParent(transform, false);
                slab.AddComponent<MeshFilter>().sharedMesh = slabMesh;
                var slabR = slab.AddComponent<MeshRenderer>();
                slabR.sharedMaterial = floor;
                slabR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // (SEM colisor: a laje com colisor criou quinas nas rampas de entrada/saída e o carro tropeçava no pit —
                // teste 01/11. A aderência de asfalto e o pneu limpo no box vêm da POSIÇÃO: InPitArea, no CarController
                // e no TyreDirt.)
            }
            float entryS = laneStart, exitS = laneStart + laneLength;
            // Box count UNIFICADO (campo da classe, setado pelo bootstrap ANTES do Setup com o nº de carros).
            // Nunca usar race.entries.Count aqui: na hora do Setup os carros ainda não nasceram (=0).
            if (boxCount < 4) boxCount = 16;
            float boxSize = (laneLength - 30f) / boxCount;
            float boxFirst = laneStart + 15f;
            for (int i = 0; i < boxCount; i++)
            {
                float s = Mathf.Repeat(boxFirst + (i + 0.5f) * boxSize, spline.Length);
                var p = spline.PoseAtDistance(s);
                Vector3 right = p.rotation * Vector3.right;
                Vector3 laneCenter = p.position - right * laneOffset;

                // GARAGEM REAL (frente aberta p/ o carro ENTRAR): piso + teto + 3 paredes finas.
                // Alinhada com PitSpawnPose/boxTarget (−28 m da pista; fundo −36 m, abertura −20 m).
                // Em p.rotation, X = right (profundidade 16 m) e Z = forward (largura boxSize).
                Vector3 garageCenter = p.position - right * 28f;
                var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                f.name = "PitFloor" + i;
                f.transform.SetParent(transform, false);
                f.transform.rotation = p.rotation;
                f.transform.position = garageCenter + Vector3.up * 0.005f;
                f.transform.localScale = new Vector3(16f, 0.02f, boxSize - 0.4f);
                f.GetComponent<MeshRenderer>().sharedMaterial = floor;
                f.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;
                // Fundo (−36 m) — puramente visual para nunca prender o para-choque na manobra.
                var gB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gB.name = "PitBuilding" + i;
                gB.transform.SetParent(transform, false);
                gB.transform.position = p.position - right * 36f + Vector3.up * 2.4f;
                gB.transform.rotation = p.rotation;
                gB.transform.localScale = new Vector3(0.5f, 4.8f, boxSize - 0.4f);
                var c_gB = gB.GetComponent<Collider>(); if (c_gB != null) DestroyImmediate(c_gB);
                gB.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Divisórias laterais entre garagens: orientadas em X (16 m de profundidade) e sem collider.
                for (int side = -1; side <= 1; side += 2)
                {
                    var gS = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gS.name = "PitBuilding" + i + (side < 0 ? "L" : "R");
                    gS.transform.SetParent(transform, false);
                    gS.transform.position = garageCenter + p.rotation * Vector3.forward * (side * (boxSize - 0.5f) * 0.5f) + Vector3.up * 2.4f;
                    gS.transform.rotation = p.rotation;
                    gS.transform.localScale = new Vector3(16f, 4.8f, 0.35f);
                    var c_gS = gS.GetComponent<Collider>(); if (c_gS != null) DestroyImmediate(c_gS);
                    gS.GetComponent<MeshRenderer>().sharedMaterial = building;
                }
                // Teto.
                var gT = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gT.name = "PitRoof" + i;
                gT.transform.SetParent(transform, false);
                gT.transform.position = garageCenter + Vector3.up * 4.8f;
                gT.transform.rotation = p.rotation;
                gT.transform.localScale = new Vector3(16f, 0.4f, boxSize - 0.4f);
                var c_gT = gT.GetComponent<Collider>(); if (c_gT != null) DestroyImmediate(c_gT);
                gT.GetComponent<MeshRenderer>().sharedMaterial = building;
                // Portão RECOLHIDO (no teto da abertura em −20 m) — VISUAL APENAS.
                var dr = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dr.name = "PitDoor" + i;
                dr.transform.SetParent(transform, false);
                dr.transform.position = p.position - right * 20f + Vector3.up * 4.55f;
                dr.transform.rotation = p.rotation;
                dr.transform.localScale = new Vector3(0.25f, 0.5f, boxSize - 0.8f);
                var c_dr = dr.GetComponent<Collider>(); if (c_dr != null) DestroyImmediate(c_dr);
                dr.GetComponent<MeshRenderer>().sharedMaterial = door;

                // Muro interno com VÃO: contínuo no meio da faixa, sempre afastado das rotas
                // de entrada (rel < 14) e de saída (rel > laneLength − 10) para não bloquear o carro.
                float wallRel = Mathf.Repeat(s - laneStart, spline.Length);
                bool midLane = wallRel - boxSize * 0.5f >= 14f && wallRel + boxSize * 0.5f <= laneLength - 10f;
                if (midLane)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    w.name = "PitWall" + i;
                    w.transform.SetParent(transform, false);
                    w.transform.position = laneCenter + right * 6.2f + Vector3.up * 0.55f;
                    w.transform.rotation = p.rotation;
                    // MURO AO LONGO do lane: fino no transversal (0,3 m), comprido na direção da pista.
                    // Antes estava INVERTIDO (10,6 m transversal) — virava cancela atravessada no lane.
                    w.transform.localScale = new Vector3(0.3f, 1.1f, boxSize - 0.4f);
                    var c_w = w.GetComponent<Collider>(); if (c_w != null) c_w.sharedMaterial = pitPhys;
                w.GetComponent<MeshRenderer>().sharedMaterial = wall;
                }
            }
                // MURETA NAS PONTAS (v79): o muro por box só começava em rel 15 e acabava em laneLength−15 —
                // o vão aberto na frente dos boxes 0/1 deixava carro sair do box direto para a pista (e carro
                // da pista entrar na contramão). Fecha de rel 5 (fim da rampa de entrada) até laneLength−6
                // (antes da rampa de saída). As rotas de entrada/saída passam a ≥ 3 m da mureta (lat −7,8).
                float[,] ends = { { 5f, 15f }, { laneLength - 15f, laneLength - 6f } };
                // BICO DA MURETA (v80): começa ANTES do lane (rel −10), logo depois da zebra,
                // inclinado até encontrar a mureta reta em rel 5 (lat −7,8) — separa a faixa de entrada.
                try
                {
                    var tdef = race != null && race.Track != null ? race.Track.Definition : null;
                    float noseLat = tdef != null ? Mathf.Max(6.9f, tdef.roadWidth * 0.5f + tdef.kerbWidth + 0.35f) : 7.2f; // sempre FORA da zebra
                    var pn0 = spline.PoseAtDistance(Mathf.Repeat(laneStart - 10f, spline.Length));
                    var pn1 = spline.PoseAtDistance(Mathf.Repeat(laneStart + 5f, spline.Length));
                    Vector3 a0 = pn0.position - (pn0.rotation * Vector3.right) * noseLat;
                    Vector3 a1 = pn1.position - (pn1.rotation * Vector3.right) * 7.8f;
                    Vector3 mid = (a0 + a1) * 0.5f;
                    Vector3 dirN = a1 - a0; dirN.y = 0f;
                    var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    nose.name = "PitWallNose";
                    nose.transform.SetParent(transform, false);
                    nose.transform.position = mid + Vector3.up * 0.55f;
                    nose.transform.rotation = Quaternion.LookRotation(dirN.normalized, Vector3.up);
                    nose.transform.localScale = new Vector3(0.3f, 1.1f, dirN.magnitude + 0.2f);
                    var c_n = nose.GetComponent<Collider>(); if (c_n != null) c_n.sharedMaterial = pitPhys;
                    nose.GetComponent<MeshRenderer>().sharedMaterial = wall;
                }
                catch (System.Exception ex) { Debug.LogWarning("[PitLane] bico da mureta ignorado: " + ex.Message); }
                for (int k = 0; k < 2; k++)
                {
                    float r0 = ends[k, 0], r1 = ends[k, 1];
                    if (r1 - r0 < 1f) continue;
                    var pw = spline.PoseAtDistance(Mathf.Repeat(laneStart + (r0 + r1) * 0.5f, spline.Length));
                    Vector3 rw = pw.rotation * Vector3.right;
                    var we = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    we.name = "PitWallEnd" + k;
                    we.transform.SetParent(transform, false);
                    we.transform.position = pw.position - rw * (laneOffset - 6.2f) + Vector3.up * 0.55f;
                    we.transform.rotation = pw.rotation;
                    we.transform.localScale = new Vector3(0.3f, 1.1f, r1 - r0);
                    var c_we = we.GetComponent<Collider>(); if (c_we != null) c_we.sharedMaterial = pitPhys;
                    we.GetComponent<MeshRenderer>().sharedMaterial = wall;
                }
                // RAMPAS DE ACESSO: triângulos de asfalto da borda da pista até o pit lane (entrada e saída).
                // LADO DO PIT: borda da pista (−6,5 m) até o CENTRO do lane (−14 m) — antes atravessava a pista toda.
                for (int k = 0; k < 2; k++)
                {
                    float baseS = Mathf.Repeat(k == 0 ? entryS - 40f : exitS - 15f, spline.Length);
                    var p0 = spline.PoseAtDistance(baseS);
                    // v105: rampa de SAÍDA de 30 → 60 m (até +45 m, onde acaba o vão sem grama — OpenRunoffGap): o carro entra na
                    // pista quase PARALELO (~7°) em vez de atravessado (pedido do usuário: "entra quase na diagonal da pista").
                    var p1 = spline.PoseAtDistance(Mathf.Repeat(baseS + (k == 0 ? 50f : 60f), spline.Length));
                    Vector3 r0 = p0.rotation * Vector3.right;
                    Vector3 r1 = p1.rotation * Vector3.right;
                    var apron = new GameObject("PitApron" + k, typeof(MeshFilter), typeof(MeshRenderer));
                    apron.transform.SetParent(transform, false);
                    var mf = apron.GetComponent<MeshFilter>();
                    var vA = p0.position - r0 * 6.5f + Vector3.up * 0.03f;   // borda da pista (lado do pit)
                    var vB = p1.position - r1 * laneOffset + Vector3.up * 0.03f; // centro do pit lane
                    var vC = p0.position - r0 * 6.5f + (p1.position - p0.position) + Vector3.up * 0.03f;
                    var vD = p1.position - r1 * laneOffset + (p0.position - p1.position) + Vector3.up * 0.03f;
                // v105: os dois triângulos eram (A,B,C) e (B,D,C) — ambos do mesmo lado da diagonal: sobrava um BURACO triangular no
                // começo da rampa (grama), justamente onde o caminho de entrada passa (print do usuário de 05/11: carro na grama
                // entrando no box). Agora (A,B,C) + (A,D,B) = o retângulo inteiro, normal para cima.
                mf.sharedMesh = new Mesh { vertices = new[] { vA, vB, vC, vD }, triangles = new[] { 0, 1, 2, 0, 3, 1 } };
                mf.sharedMesh.RecalculateNormals();
                apron.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = new Color(0.16f, 0.16f, 0.18f) };
                // Collider + asfalto: rampa de entrada/saída com grip da pista (não grama).
                apron.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                apron.AddComponent<TrackSurface>().type = SurfaceType.Asphalt;
            }
            // Gantries (pórtico SOBRE O PIT LANE) na entrada e saída: postes nas duas bordas
            // do lane (−8 e −20 m) e viga transversal ligando os dois — NUNCA sobre a pista.
            for (int k = 0; k < 2; k++)
            {
                float gs = Mathf.Repeat(k == 0 ? entryS + 4f : exitS - 4f, spline.Length);
                var gp = spline.PoseAtDistance(gs);
                Vector3 gr = gp.rotation * Vector3.right;
                var post1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post1.name = "GantryPost" + k + "a";
                post1.transform.SetParent(transform, false);
                post1.transform.position = gp.position - gr * 7.5f + Vector3.up * 2.5f;
                post1.transform.rotation = gp.rotation;
                post1.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                if (post1.GetComponent<Collider>() != null) DestroyImmediate(post1.GetComponent<Collider>()); // VISUAL: poste na borda era armadilha na travessia de entrada/saída
                post1.GetComponent<MeshRenderer>().sharedMaterial = building;
                var post2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post2.name = "GantryPost" + k + "b";
                post2.transform.SetParent(transform, false);
                post2.transform.position = gp.position - gr * 20.5f + Vector3.up * 2.5f;
                post2.transform.rotation = gp.rotation;
                post2.transform.localScale = new Vector3(0.5f, 5f, 0.5f);
                if (post2.GetComponent<Collider>() != null) DestroyImmediate(post2.GetComponent<Collider>()); // VISUAL: idem post1
                post2.GetComponent<MeshRenderer>().sharedMaterial = building;
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = "GantryBeam" + k;
                beam.transform.SetParent(transform, false);
                beam.transform.position = gp.position - gr * 14f + Vector3.up * 5.2f;
                beam.transform.rotation = gp.rotation;
                // Viga TRANSVERSAL ao lane (13,5 m de −7,25 a −20,75) — antes estava 25 m AO LONGO da pista.
                beam.transform.localScale = new Vector3(13.5f, 0.6f, 0.6f);
                var c_beam = beam.GetComponent<Collider>(); if (c_beam != null) c_beam.sharedMaterial = pitPhys;
                beam.GetComponent<MeshRenderer>().sharedMaterial = building;
            }
            // (Sem as SETAS ">" laranja no chão da entrada/saída: eram texto 3D, que o shader de fonte desenha POR CIMA de tudo —
            // a faixa laranja passava por cima do carro (print do usuário, 02/11). Entrada marcada pelo pórtico, letreiro PIT e
            // tracejado azul.)
            }
            catch (System.Exception ex)
            {
                // Visual do pit NUNCA pode quebrar o carregamento da corrida (histórico: loading em loop 95%).
                Debug.LogWarning($"[PitLane] geometria ignorada: {ex.Message}");
            }
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            // Estados dos carros criados LAZY e SEMPRE que houver carro sem estado (o bootstrap
            // spawna em lotes: humano primeiro, NPCs depois — rodar 1x só dava pit ao humano).
            if (race != null && (carsDirty || race.entries.Count > cars.Count + remoteCars)) { carsDirty = false; InitCars(); }
            if (race == null || race.State != RaceState.Racing) return;
            bool pitOn = race.Settings.pitStops;
            UpdateEntryLine();
            foreach (var e in race.entries)
            {
                var st = Find(e.car);
                if (st == null) continue;
                // Pneus de TODOS os carros (jogador E IA) em TODOS os modos: temperatura, desgaste, água.
                UpdateTyres(st);
                if (!pitOn) continue;
                // Carro DESTRUÍDO/abandonado/retirado da cena (v99): sai do fluxo do pit na hora. Antes continuava "no corredor": o
                // vigia recolocava o destroço ponto a ponto da rota (Hiro Tanaka: 49 resgates em 4,5 min, destruído na entrada) e
                // todo mundo parava atrás dele — inclusive depois de a direção de prova retirar o carro (invisível, mas contado).
                if (e.retired || st.car.wrecked || !st.car.gameObject.activeInHierarchy) { DropFromFlow(st); continue; }
                if (st.isHuman) TeamDamageCall(st);
                ReEngageIfTeleported(st, e);
                // Fila do treino: parado no pit até chegar a vez de sair (o UpdatePit trata holding/exiting).
                UpdatePit(st, e);
                WatchdogPitControl(st, e);
            }
            if (pitOn) { SweepPitBlockers(); UpdatePitPairCollisions(); }
        }

        // IA×IA SEM COLISÃO NO CORREDOR (v99, pedido do usuário): carros sob controle da rota do pit ignoram a colisão entre si (como a
        // fila do SC ao desdobrar) — um toque no corredor mandava o companheiro de equipe para dentro da parede da garagem (03/11).
        readonly HashSet<long> pitIgnoredPairs = new HashSet<long>();
        readonly Dictionary<CarController, Collider[]> pitColliders = new Dictionary<CarController, Collider[]>();
        float pairClock;

        static bool InPitCorridorFlow(CarState st) => st != null && st.car != null && st.car.pitControlled && (st.inPit || st.exiting) && !st.holding && !st.car.wrecked;

        Collider[] CollidersOf(CarController c)
        {
            if (!pitColliders.TryGetValue(c, out var arr))
            {
                var list = new List<Collider>();
                foreach (var col in c.GetComponentsInChildren<Collider>(true)) if (!(col is WheelCollider)) list.Add(col);
                arr = list.ToArray();
                pitColliders[c] = arr;
            }
            return arr;
        }

        void SetPairIgnore(CarController a, CarController b, bool ignore)
        {
            foreach (var ca in CollidersOf(a))
                foreach (var cb in CollidersOf(b))
                    if (ca != null && cb != null) Physics.IgnoreCollision(ca, cb, ignore);
        }

        void UpdatePitPairCollisions()
        {
            pairClock += Time.deltaTime;
            if (pairClock < 0.2f) return;
            pairClock = 0f;
            for (int i = 0; i < cars.Count; i++)
            {
                var a = cars[i];
                if (a.car == null) continue;
                for (int j = i + 1; j < cars.Count; j++)
                {
                    var b = cars[j];
                    if (b.car == null) continue;
                    long key = ((long)a.car.GetInstanceID() << 32) ^ (uint)b.car.GetInstanceID();
                    bool both = InPitCorridorFlow(a) && InPitCorridorFlow(b);
                    bool has = pitIgnoredPairs.Contains(key);
                    if (both && !has) { SetPairIgnore(a.car, b.car, true); pitIgnoredPairs.Add(key); }
                    else if (!both && has) { SetPairIgnore(a.car, b.car, false); pitIgnoredPairs.Remove(key); }
                }
            }
        }

        /// <summary>Espera DELIBERADA (ceder a vez, liberação segura, bloqueador parado à frente): o relógio do resgate anda a 1/3 —
        /// uma espera normal (até 6 s) não vira resgate; só uma de ~15 s. Antes o resgate disparava aos 5 s parado e "recolocava"
        /// o carro no mesmo ponto 0 da saída (4 vezes no treino de 03/11, build 13).</summary>
        void WaitingOnPurpose(CarState st)
        {
            st.rescueT = Mathf.Max(0f, st.rescueT - Time.deltaTime * 0.67f);
        }

        /// <summary>Tira o carro do fluxo do pit (destruído/abandonado): sem rota, sem resgate, sem controle externo.</summary>
        void DropFromFlow(CarState st)
        {
            if (!st.inPit && !st.exiting && !st.holding && !st.car.pitControlled) return;
            st.inPit = false; st.exiting = false; st.holding = false;
            st.stopTimer = 0f; st.rescueT = 0f; st.sweepT = 0f;
            st.route.Clear(); st.routeIdx = 0;
            st.car.pitControlled = false;
            st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
        }

        float sweepClock;
        /// <summary>VARREDOR DO PIT: carro de IA parado > 8 s na área do pit FORA do próprio box (bateu na mureta,
        /// tentou sair pelo muro, encalhou de lado) é recolocado — entrando: na parada do próprio box; saindo: no fim
        /// da rampa; fora do fluxo do pit: no último ponto bom da pista. Antes ele travava o corredor e quem vinha
        /// atrás (o jogador, no piloto automático) nunca chegava à garagem.</summary>
        void SweepPitBlockers()
        {
            sweepClock += Time.deltaTime;
            if (sweepClock < 0.5f) return;
            float dt = sweepClock; sweepClock = 0f;
            foreach (var st in cars)
            {
                if (st.car == null) continue;
                var pos = st.car.transform.position;
                if (st.isHuman || st.holding || st.stopTimer > 0f || st.car.wrecked) { st.sweepPos = pos; st.sweepT = 0f; continue; }
                if (Vector3.Distance(pos, st.sweepPos) > 2f) { st.sweepPos = pos; st.sweepT = 0f; continue; }
                bool inFlow = st.inPit || st.exiting;
                if (!inFlow && !race.Track.InPitZone(pos, 3f)) { st.sweepT = 0f; continue; }
                if (!st.exiting && Vector3.Distance(pos, st.boxTarget) < 7f) { st.sweepT = 0f; continue; } // no próprio box
                st.sweepT += dt;
                if (st.sweepT < 8f) continue;
                st.sweepT = 0f;
                SweepOne(st);
            }
        }

        void SweepOne(CarState st)
        {
            var car = st.car;
            if ((st.inPit || st.exiting) && st.route.Count >= 2)
            {
                int last = st.route.Count - 1;
                Vector3 p = st.route[last], a = st.route[last - 1];
                Vector3 dir = p - a; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = car.transform.forward;
                if (!SpotClear(p, car, st.exiting ? 10f : 4f)) return; // tenta de novo no próximo ciclo
                car.ResetTo(p + Vector3.up * 0.3f, Quaternion.LookRotation(dir.normalized, Vector3.up));
                st.routeIdx = last;
            }
            else
            {
                var tb = car.GetComponent<RaceX.Track.TrackBounds>();
                if (tb == null) return;
                tb.ResetToCheckpoint();
            }
            st.stillTimer = 0f; st.stuckTimer = 0f; st.reverseTimer = 0f; st.reverseCount = 0f; st.waitTimer = 0f; st.rescueT = 0f;
            st.sweepPos = car.transform.position;
            Debug.Log("[PITWATCH] VARREDOR: " + car.name + " parado no pit fora do box foi recolocado");
        }

        bool SpotClear(Vector3 p, CarController self, float r)
        {
            foreach (var e in race.entries)
                if (e.car != null && e.car != self && (e.car.transform.position - p).sqrMagnitude < r * r) return false;
            return true;
        }

        /// <summary>CÃO DE GUARDA do piloto automático: nenhuma rota de pit legítima passa de 45 s.
        /// Se pitControlled sobreviver sem estado válido (ou por tempo demais), solta o carro e
        /// registra UMA linha no log — o "carro limitado desde o início" nunca mais fica preso,
        /// e se repetir teremos o estado exato impresso (dados reais, não suposição).</summary>
        void WatchdogPitControl(CarState st, RaceEntry e)
        {
            if (st.forcedFree && !st.car.pitControlled && st.car.SpeedKmh > 20f) st.forcedFree = false;
            if (!st.car.pitControlled)
            {
                st.pitCtrlSince = -1f;
                return;
            }
            if (st.pitCtrlSince < 0f)
            {
                st.pitCtrlSince = Time.time;
                if (st.isHuman)
                    Debug.Log($"[PITWATCH] auto-piloto LIGOU no humano: holding={st.holding} exiting={st.exiting} inPit={st.inPit} kmh={st.car.SpeedKmh:0}");
                return;
            }
            // Fila do treino: esperar a vez NÃO é travamento (a fila chega a 60 s; o limite era 45 s
            // e soltava à força os últimos carros, que batiam na garagem sem rota).
            if (st.holding) { st.pitCtrlSince = Time.time; return; }
            bool semEstado = !st.holding && !st.exiting && !st.inPit;
            float dW = race.Track.Checkpoints.RaceDistance(st.car);
            var spw = spline.PoseAtDistance(Mathf.Repeat(dW, spline.Length));
            float latW = Vector3.Dot(st.car.transform.position - spw.position, spw.rotation * Vector3.right);
            if (semEstado || Time.time - st.pitCtrlSince > 45f)
            {
                // DEMOROU DEMAIS NO PIT (regra do usuário): nunca solta o carro no meio do corredor nem o joga na pista.
                // • Chegando ao box: encaixa na marca da equipe e faz a parada (jogador e IA).
                if (st.inPit && !st.holding && !st.exiting)
                {
                    var pose = StopPose(e);
                    st.car.ResetTo(pose.position, pose.rotation);
                    st.routeIdx = st.route.Count;
                    st.pitCtrlSince = Time.time;
                    Debug.LogWarning("[PITWATCH] entrada demorou: encaixado na marca do box " + e.name);
                    return;
                }
                // • Saindo do box: RESPAWN NO CORREDOR, na faixa rápida, ~10 m à frente da própria equipe, alinhado com o pit
                //   lane — e refaz a rota de saída dirigindo até a pista.
                if (st.exiting && !semEstado)
                {
                    float fromR = Mathf.Clamp(Mathf.Repeat(st.boxS - laneStart, spline.Length) + 10f, 10f, laneLength - 14f);
                    var spR = spline.PoseAtDistance(Mathf.Repeat(laneStart + fromR, spline.Length));
                    st.car.ResetTo(spR.position - (spR.rotation * Vector3.right) * FastLaneLat + Vector3.up * 0.4f, spR.rotation);
                    Release(st, fromR);
                    st.pitCtrlSince = Time.time;
                    Debug.LogWarning("[PITWATCH] saída demorou: respawn no corredor à frente da equipe " + e.name);
                    return;
                }
                Debug.LogWarning($"[PITWATCH] LIBERADO FORÇADO {e.name}: holding={st.holding} exiting={st.exiting} inPit={st.inPit} lat={latW:0.0} kmh={st.car.SpeedKmh:0} t={Time.time - st.pitCtrlSince:0}s");
                st.holding = false; st.exiting = false; st.inPit = false;
                st.forcedFree = true;
                st.car.PitReleaseReset();
                st.pitCtrlSince = -1f;
            }
        }

        /// <summary>RE-ENGATE de teletransporte: um carro resetado PARA O PIT depois de já ter
        /// sido liberado (ex.: TrackBounds devolveu ao spawn do box antes de cruzar o checkpoint 0)
        /// ficava parado no box para sempre — sem holding, sem rota, sem nada. Se ele está
        /// parado dentro da área do box e fora do fluxo, re-engata a fila (holding → saída).</summary>
        void ReEngageIfTeleported(CarState st, RaceEntry e)
        {
            if (st.holding || st.exiting || st.inPit || st.forcedFree) return;
            if (st.car.pitControlled || st.car.SpeedKmh > 2f) return;
            float d = race.Track.Checkpoints.RaceDistance(st.car);
            float rel = Mathf.Repeat(d - laneStart, spline.Length);
            bool noBox = rel > 5f && rel < 30f && st.car.transform.position.y < 6f; // área da garagem
            if (!noBox) return;
            st.inPit = true;
            st.holding = true;
            st.releaseTimer = e.isHuman ? 2f : 2f + Random.value * 2f;
            ApplyHold(st);
            if (st.isHuman) Debug.Log("[PITWATCH] carro resetado no box — fila re-engatada (saída em ~2s)");
        }

        readonly float[] tmpEnergy = new float[4];
        /// <summary>Ganho de calor dos pneus (v81c): 4,5 × 0,52. Com o resfriamento novo (pela velocidade), simulado numa volta
        /// de ~95 s (scratchpad sys/tyresim.py): equilíbrio de corrida ~108 °C no médio a 32 °C de pista — os dianteiros, que
        /// ficavam em 86–91 °C (na borda da janela de 90 °C), sobem para ~93 °C. Do box (70 °C) à janela em ~50 s de ritmo de
        /// corrida (a "volta de saída" da F1); atrás do SC 3 min: ~60 °C reto / ~80 °C em zigue-zague. Valor de projeto.</summary>
        const float HeatScale = 4.5f * 0.52f;

        /// <summary>Pneus v80, POR RODA: temperatura (aquece com carga × escorregamento × velocidade,
        /// esfria com água/ar), desgaste (a roda mais carregada — ex. dianteira esquerda em pista com
        /// muitas curvas à direita, traseiras em pista de tração — gasta mais), furo a 0% e aderência
        /// final = composto × desgaste × temperatura × água. Por DISTÂNCIA, não por tempo.</summary>
        void UpdateTyres(CarState st)
        {
            var car = st.car;
            if (car == null || car.spec == null || car.wheels == null || car.wheels.Length < 4) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (st.weatherCallTimer > 0f) st.weatherCallTimer = Mathf.Max(0f, st.weatherCallTimer - dt);
            float wet = WeatherSystem.Wetness;
            float track = WeatherSystem.Active ? WeatherSystem.TrackTemp : 32f;
            float speed = Mathf.Max(0f, car.ForwardSpeed);
            float lapLen = spline != null ? Mathf.Max(100f, spline.Length) : 3000f;
            float life = Mathf.Max(2f, TyreModel.LifeLaps(st.compound));
            float baseAdd = speed * dt / lapLen / life * st.wearMul * TyreModel.DryWearPenalty(st.compound, wet);
            if (car.IsOffTrack) baseAdd *= 1.6f;
            float wheelLoadRef = Mathf.Max(1f, car.spec.massKg * 9.81f * 0.25f);
            Vector3 la = car.LocalAcceleration;
            float g = Mathf.Min(3f, Mathf.Sqrt(la.x * la.x + la.z * la.z) / 9.81f);
            float sumE = 0f;
            for (int i = 0; i < 4; i++)
            {
                float load = 0.2f;
                var wc = car.wheels[i];
                if (wc != null && wc.GetGroundHit(out WheelHit hit)) load = Mathf.Clamp(hit.force / wheelLoadRef, 0.2f, 3f);
                tmpEnergy[i] = load * (0.12f + Mathf.Min(1.5f, car.WheelSlip[i]));
                sumE += tmpEnergy[i];
            }
            float meanE = Mathf.Max(0.0001f, sumE * 0.25f);
            float frontDmgWear = 1f + 0.5f * car.DmgFront; // asa dianteira danificada: carro "empurra" e gasta a frente (Williams/Albon)
            // Água esfria conforme o COMPOSTO: slick sem sulco esfria de vez; intermediário metade; pneu de CHUVA quase não
            // (sulcos fundos flexionam e geram calor — é feito para trabalhar a 40–70 °C no aguaceiro). Antes a água esfriava
            // todos igual e o pneu de chuva SAÍA da janela justamente quando a pista chegava a 90% molhada.
            int cmp = TyreModel.Clamp(st.compound);
            float waterCool = cmp == TyreModel.Wet ? 0.15f : cmp == TyreModel.Inter ? 0.45f : 1f;
            float eq = track - wet * 8f * waterCool;
            // RESFRIAMENTO POR CONVECÇÃO, proporcional à velocidade (v81c). Calibrado em dados da Pirelli: em Las Vegas
            // 2023 (pista 15 °C) os dianteiros perderam 35–40 °C numa reta de ~2 km a ~300 km/h, e em Baku ~30 °C — os dois
            // dão ~0,018/s a ~80 m/s. Parado/devagar o pneu perde pouco (0,004/s; τ ≈ 4 min). Antes era 0,03/s fixo: o pneu
            // caía de 70 °C (cobertor) para ~55 °C só no pit lane e para ~42 °C atrás do safety car.
            // ÁGUA resfria em qualquer velocidade (película sobre a banda de rodagem): termo próprio, calibrado no teste de chuva
            // de 30/10 — só com o termo de velocidade o pneu de chuva ia a ~100 °C na pista alagada (janela 40–70 °C); com
            // ele volta a ~60 °C. Slick na chuva esfria bem mais rápido (como na F1). Pista seca: termo zero.
            float cool = (0.004f + 0.00018f * speed) * (1f + 2f * wet * waterCool) + 0.09f * wet * waterCool;
            // Flexão dos sulcos: aquecimento extra dos pneus de chuva/intermediário com o carro andando (na pista secando
            // é o que SUPERAQUECE o pneu de chuva, como na F1).
            float treadHeat = (cmp == TyreModel.Wet ? 0.10f : cmp == TyreModel.Inter ? 0.05f : 0f) * Mathf.Clamp01(speed / 50f);
            float warmSum = 0f, maxWear = 0f;
            for (int i = 0; i < 4; i++)
            {
                // PRESSÃO: o furo esvazia o pneu; vazio, ele roda sobre o flanco e o aro (o carro arria e puxa para aquele
                // lado), esquenta (o flanco flexiona muito mais), se desfaz em ~1 volta e a borracha solta bate no assoalho.
                if (st.leak[i] > 0f && st.pressure[i] > 0f) st.pressure[i] = Mathf.Max(0f, st.pressure[i] - st.leak[i] * dt);
                if (st.wearW[i] >= 0.999f) st.pressure[i] = 0f;        // pneu gasto até a lona estoura
                float flat = 1f - st.pressure[i];
                if (Mathf.Abs(flat - st.dropShown[i]) > 0.02f && car.damage != null)
                {
                    st.dropShown[i] = flat;
                    car.damage.SetDeflation(i, flat);
                }
                // Calor de cada pneu = 65% o esforço DELE + 35% a média do carro (calor que chega pelos freios/aro, carcaça e ar
                // quente do carro). Antes era só o próprio: numa pista de curvas à direita o traseiro direito (pouca carga) não
                // chegava à janela (relato: 98/93/91/85 °C). A ordem real se mantém — dianteiro de FORA da curva mais quente
                // (Pirelli: dianteiro esquerdo é o mais exigido em circuitos horários como Silverstone e Suzuka).
                float mixE = 0.65f * tmpEnergy[i] + 0.35f * meanE;
                float work = (mixE * (0.6f + g * 0.5f) * Mathf.Clamp01(speed / 60f + 0.15f) + treadHeat) * (1f + 1.5f * flat);
                st.tempW[i] += TyreModel.Response(cmp) * (HeatScale * TyreModel.HeatGain(cmp) * work - cool * (st.tempW[i] - eq)) * dt;
                st.tempW[i] = Mathf.Clamp(st.tempW[i], eq - 5f, 160f);
                float share = Mathf.Clamp(tmpEnergy[i] / meanE, 0.55f, 1.75f);
                float add = baseAdd * share * TyreModel.OverheatWear(st.compound, st.tempW[i]) * (i <= 1 ? frontDmgWear : 1f);
                // Vazio (abaixo de 30%) e andando: a borracha se rasga — destruída em ~1 volta (Hamilton, Silverstone 2020,
                // terminou a última meia volta sobre o aro). Por distância: 1 / comprimento da volta por metro.
                if (st.pressure[i] < 0.3f) add += speed * dt / lapLen;
                st.wearW[i] = Mathf.Clamp01(st.wearW[i] + add);
                // Sem pit stop não há como trocar: o pneu gasta até 90% e nunca fura.
                if (!race.Settings.pitStops) st.wearW[i] = Mathf.Min(st.wearW[i], 0.9f);
                if (st.wearW[i] >= 0.999f && !st.puncture[i])
                {
                    st.puncture[i] = true;
                    Debug.Log("[TYRE] furo/pneu destruído roda " + i + " " + (st.car != null ? st.car.name : ""));
                }
                // EFEITO VISUAL do pneu furado (murcho → tiras soltas → aro com faíscas). Só liga depois do 1º furo do carro.
                if (flat > 0.05f || st.puncture[i] || st.tyreVisual)
                {
                    st.tyreVisual = true;
                    float shredNow = st.pressure[i] < 0.3f ? Mathf.InverseLerp(0.25f, 0.95f, st.wearW[i]) : 0f;
                    RaceX.Car.TyreDamageVisual.Set(car, i, st.pressure[i], shredNow, st.puncture[i]);
                }
                float grip = TyreModel.Base(st.compound) * TyreModel.WearFactor(1f - st.wearW[i])
                           * TyreModel.TempFactor(st.compound, st.tempW[i]) * TyreModel.WaterFactor(st.compound, wet, speed);
                if (st.puncture[i]) grip *= 0.35f;
                // Aderência pela pressão: até ~85% quase igual; abaixo disso o flanco perde rigidez lateral e o pneu
                // escorrega (vazio = 35%, igual ao pneu destruído). Valores de projeto.
                else grip *= Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, st.pressure[i])));
                // Pneu vazio batendo na carroceria a mais de ~110 km/h: danifica a lateral daquele lado (o assoalho do
                // Hamilton em Spa 2014 e o carro dele em Silverstone 2020).
                if (st.pressure[i] < 0.2f && speed > 30f && car.damage != null) car.damage.AddWear(i % 2 == 0 ? 2 : 3, 0.015f * dt * speed / 30f);
                st.gripW[i] = grip;
                warmSum += Mathf.Clamp01((st.tempW[i] - (TyreModel.Low(st.compound) - 25f)) / 25f);
                if (st.wearW[i] > maxWear) maxWear = st.wearW[i];
            }
            st.wear = maxWear;   // o pneu MAIS gasto decide a parada
            st.temp = warmSum * 0.25f;
        }

        /// <summary>Desgaste de UMA roda (0..1) para o painel do HUD. Ordem: 0=DI, 1=DE, 2=TD, 3=TE.</summary>
        public static float WearOf(CarController car, int wheel)
        {
            var st = instance != null ? instance.Find(car) : null;
            return st != null && wheel >= 0 && wheel < 4 ? st.wearW[wheel] : 0f;
        }

        /// <summary>RESGATE do fluxo do pit: carro sob controle do pit parado (não saiu de 1,5 m) por 5 s, ou virado
        /// contra a rota por 3 s, é RECOLOCADO na rota alinhado para seguir. Antes, batido de ré na mureta da
        /// entrada e na contramão, ficava parado para sempre (sem ré, sem respawn) — a única saída era reiniciar.</summary>
        void RescueIfStuck(CarState st)
        {
            if (st.holding || st.stopTimer > 0f || (!st.inPit && !st.exiting)) { st.rescueT = 0f; st.rescuePos = st.car.transform.position; return; }
            if (Vector3.Distance(st.car.transform.position, st.rescuePos) > 1.5f) { st.rescuePos = st.car.transform.position; st.rescueT = 0f; return; }
            bool wrongWay = false;
            if (st.route.Count > 0)
            {
                var tgt = st.route[Mathf.Clamp(st.routeIdx, 0, st.route.Count - 1)] - st.car.transform.position; tgt.y = 0f;
                wrongWay = tgt.sqrMagnitude > 4f && Vector3.Angle(st.car.transform.forward, tgt) > 110f;
            }
            // Parado no próprio box à espera do pit stop não é "preso".
            if (!st.exiting && Vector3.Distance(st.car.transform.position, st.boxTarget) < 6f) { st.rescueT = 0f; return; }
            st.rescueT += Time.deltaTime * (wrongWay ? 1.7f : 1f);
            if (st.rescueT < 5f) return;
            RescueNow(st);
        }

        void RescueNow(CarState st)
        {
            st.rescueT = 0f;
            Vector3 pos; Quaternion rot;
            if (st.route.Count > 0)
            {
                int i = Mathf.Clamp(st.routeIdx, 0, st.route.Count - 1);
                // Nunca recoloca EM CIMA de outro carro (o que trava o corredor é tirado pelo varredor).
                while (i < st.route.Count - 1 && !SpotClear(st.route[i], st.car, 4.5f)) i++;
                if (!SpotClear(st.route[i], st.car, 4.5f)) return;
                st.routeIdx = i;
                int j = Mathf.Min(i + 1, st.route.Count - 1);
                Vector3 dir = st.route[j] - st.route[i];
                if (dir.sqrMagnitude < 1f && i > 0) dir = st.route[i] - st.route[i - 1];
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = st.car.transform.forward;
                pos = st.route[i];
                rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            else
            {
                var p = StopPose(race.GetEntry(st.car));
                pos = p.position; rot = p.rotation;
            }
            string diag = RouteDiag(st);
            st.car.ResetTo(pos + Vector3.up * 0.3f, rot);
            st.rescuePos = pos;
            st.stillTimer = 0f; st.stuckTimer = 0f; st.reverseTimer = 0f; st.reverseCount = 0f; st.waitTimer = 0f;
            Debug.Log("[PITWATCH] RESGATE: " + st.car.name + " recolocado na rota do pit (idx " + st.routeIdx + "/" + st.route.Count + ") | antes: " + diag);
        }

        /// <summary>Estado do carro sob a rota do pit, para o log de resgate/travamento (v99: o autoteste ficava 5 s parado na
        /// garagem depois de "liberado" e era resgatado no ponto 0 — sem registro do porquê).</summary>
        string RouteDiag(CarState st)
        {
            var c = st.car;
            if (c == null) return "-";
            float d0 = st.route.Count > 0 ? Vector3.Distance(c.transform.position, st.route[Mathf.Clamp(st.routeIdx, 0, st.route.Count - 1)]) : -1f;
            return "hold=" + (st.holding ? 1 : 0) + " exit=" + (st.exiting ? 1 : 0) + " inPit=" + (st.inPit ? 1 : 0)
                + " idx=" + st.routeIdx + "/" + st.route.Count + " dAlvo=" + d0.ToString("0.0")
                + " thr=" + c.extThrottle.ToString("0.00") + " brk=" + c.extBrake.ToString("0.00") + " steer=" + c.extSteer.ToString("0.00")
                + " thrIA=" + c.throttleInput.ToString("0.00") + " brkIA=" + c.brakeInput.ToString("0.00")
                + " v=" + c.ForwardSpeed.ToString("0.0") + " marcha=" + c.Gear + " rodasNoChao=" + c.GroundedWheels
                + " travado=" + (c.inputLocked ? 1 : 0) + " pitCtrl=" + (c.pitControlled ? 1 : 0) + " freioMao=" + (c.handbrake ? 1 : 0)
                + " presoT=" + st.stuckTimer.ToString("0.0") + " reCnt=" + st.reverseCount.ToString("0") + " reT=" + st.reverseTimer.ToString("0.0")
                + " esperaT=" + st.waitTimer.ToString("0.0") + " cedeT=" + st.exitHoldTimer.ToString("0.0") + " resgT=" + st.rescueT.ToString("0.0")
                + " stopT=" + st.stopTimer.ToString("0.0") + " solto=" + (Time.time - st.lastExitTime).ToString("0.0") + "s";
        }

        /// <summary>Botão VOLTAR À PISTA (pausa): no fluxo do pit recoloca na rota; na pista, volta ao último ponto bom.</summary>
        public static void RescueCar(CarController car)
        {
            if (car == null) return;
            var st = instance != null ? instance.Find(car) : null;
            if (st != null && (st.inPit || st.exiting) && !st.holding) { instance.RescueNow(st); return; }
            var tb = car.GetComponent<RaceX.Track.TrackBounds>();
            if (tb != null) tb.ResetToCheckpoint();
        }

        void UpdatePit(CarState st, RaceEntry e)
        {
            RescueIfStuck(st);
            // CAPOTADO no pit: desvirar NO LUGAR (mesma posição, sem teleport). O respawn comum
            // (TrackBounds) é propositalmente ignorado no fluxo do pit — um capotado ali ficava
            // preso para sempre; e resetar com rota velha dava carro andando na contramão sozinho.
            if (Vector3.Dot(st.car.transform.up, Vector3.up) < 0.3f && st.car.SpeedKmh < 15f)
            {
                var eu = st.car.transform.eulerAngles;
                st.car.ResetTo(st.car.transform.position, Quaternion.Euler(0f, eu.y, 0f));
                st.stuckTimer = 0f; st.reverseTimer = 0f;
                return;
            }
            // SAÍDA GUIADA por ROTA (waypoints: corredor → rampa → pista) — sem atravessar fila nem muro.
            if (st.exiting)
            {
                // De volta à PISTA (lat > −6.2 = pisou na borda da pista): solta o controle NA HORA.
                // Sem isso o carro ficava preso ao limite de 80 km/h do pit até o fim da rota
                // (o "voltou reduzido do pit" na reta).
                // Lateral pelo PONTO MAIS PRÓXIMO do traçado (geometria real). Antes vinha da distância de corrida (checkpoints),
                // imprecisa a 15 m da pista: o cálculo "via" o carro na pista no meio do corredor, achava que ele tinha
                // escapado pela fresta do muro e o recolocava no corredor ("andou 10 m, parou e respawnou no corredor").
                int ciOut = spline.ClosestIndex(st.car.transform.position, st.exitHint);
                st.exitHint = ciOut;
                float dNow = ciOut * spline.spacing;
                float latOut = Vector3.Dot(st.car.transform.position - spline.points[ciOut], spline.rights[ciOut]);
                float relOut = Mathf.Repeat(dNow - laneStart, spline.Length);
                if (latOut > -6.2f && relOut >= laneLength - 12f && relOut <= laneLength + 80f)
                {
                    st.exiting = false;
                    st.inPit = false;
                    st.lastExitTime = Time.time;
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
                    st.forcedFree = true;
                    // VOLTA CONTA (regra do usuário): realinha os checkpoints com a posição ATUAL —
                    // sem isso o carro saía do pit com nextCheckpoint velho e o próximo cruzamento
                    // da linha não disparava (o cronômetro acumulava 2 voltas antes de zerar).
                    race.Track.Checkpoints.ReSyncAfterPit(st.car);
                    e.lapStartTime = Time.time;
                    e.outLap = race.Settings.laps >= 900;
                    var aiFast = st.car != null ? st.car.GetComponent<CarAI>() : null;
                    if (aiFast != null) aiFast.OnPracticeRelease();
                    return;
                }
                if (latOut > -6.2f) st.escapeT += Time.deltaTime; else st.escapeT = 0f;
                if (st.escapeT > 0.6f)   // confirmado por 0,6 s (um quadro de leitura ruim não teleporta ninguém)
                {
                    st.escapeT = 0f;
                    EscapeResets++;
                    Debug.Log("[PIT] saída: carro recolocado no corredor (escapou pela mureta) " + st.car.name);
                    // Escapou pela fresta do muro no meio do corredor: recoloca no corredor e refaz a rota de saída.
                    float from = Mathf.Clamp(relOut, 10f, laneLength - 12f);
                    var spR = spline.PoseAtDistance(Mathf.Repeat(laneStart + from, spline.Length));
                    st.car.ResetTo(spR.position - spR.rotation * new Vector3(15.2f, 0f, 0f) + Vector3.up * 0.4f, spR.rotation);
                    Release(st, from);
                    return;
                }
                if (DriveRoute(st, LaneLimitMs))
                {
                    // rota em andamento (DriveRoute já aplicou steer/throttle/brake)
                }
                else
                {
                    // Chegou NA PISTA pela rota: devolve o controle (a IA/humano segue o traçado).
                    st.exiting = false;
                    st.inPit = false;
                    st.lastExitTime = Time.time;
                    st.car.PitReleaseReset(); // zera pitControlled + pedais ext* + marchas residuais
                    st.forcedFree = true;
                    race.Track.Checkpoints.ReSyncAfterPit(st.car); // volta zera na próxima linha (ver latOut)
                    e.lapStartTime = Time.time;
                    e.outLap = race.Settings.laps >= 900;
                    var ai = st.car != null ? st.car.GetComponent<CarAI>() : null;
                    if (ai != null) ai.OnPracticeRelease();
                }
                return;
            }

            if (!st.inPit)
            {
                // Varredura de entrada a 5 Hz (evita RaceDistance+PoseAtDistance por frame por carro:
                // 24 carros × 50 Hz = 1200 consultas/s → 120). Detecção de pit não precisa de 50 Hz.
                st.scanTimer -= Time.deltaTime;
                if (st.scanTimer > 0f) return;
                st.scanTimer = 0.2f;
                float dist = race.Track.Checkpoints.RaceDistance(st.car);
                float rel = Mathf.Repeat(dist - laneStart, spline.Length);
                // Longe da entrada (resto da volta): a entrada perdida na passagem anterior volta a valer.
                if (st.entryMissed && rel > laneLength + 60f && rel < spline.Length - Mathf.Min(600f, spline.Length * 0.3f)) st.entryMissed = false;
                // NPC: pit na volta planejada — pedido na APROXIMAÇÃO da entrada, nunca depois da boca.
                // Decisão ~560 m antes (antes 300; antes ainda 90): a IA precisa de espaço para ir ao lado do pit e frear de
                // 300+ km/h — decidindo em cima da entrada ela cruzava a pista em velocidade e passava do box (print 50).
                // Decide só ENTRE 560 e 250 m antes: depois disso fica para a próxima volta — antes reavaliava até a entrada e,
                // quando o "colega no box" se resolvia em cima da hora, pedia a ~45 m, longe do lado do pit, e passava direto.
                if (!st.isHuman && !st.requested && race.Settings.laps >= 4 && race.Settings.laps < 900 && rel > spline.Length - 560f && rel < spline.Length - 250f)
                {
                    int lapNow = race.GetProgress(e).lap;
                    // Entrada logo DEPOIS da linha de chegada (ex.: Parque Real): decide pela volta em que o carro vai CHEGAR na
                    // entrada — antes o pedido saía em cima da linha, a ~40 m da entrada, rápido demais para entrar.
                    if (Mathf.Repeat(dist, spline.Length) + (spline.Length - rel) >= spline.Length) lapNow++;
                    bool planned = st.stopsDone < st.stopLaps.Count && lapNow >= st.stopLaps[st.stopsDone];
                    bool scCheap = !planned && SafetyCar.Deployed && !SafetyCar.InThisLap && st.stopsDone < st.stopLaps.Count
                                   && st.stopLaps[st.stopsDone] - lapNow <= 8 && race.Settings.laps - lapNow > 5;
                    if (scCheap) { planned = true; Debug.Log("[PIT-IA] " + e.name + " antecipa a parada sob SAFETY CAR (planejada na volta " + st.stopLaps[st.stopsDone] + ")"); }
                    // Clima: pneu errado para a água → a equipe chama (com tempo de reação próprio).
                    bool wrongTyre = TyreModel.WrongForWetness(st.compound, WeatherSystem.Wetness, st.compound);
                    if (!wrongTyre) st.weatherCallTimer = -1f;
                    else if (st.weatherCallTimer < 0f) st.weatherCallTimer = 4f + (Mathf.Abs((e.name ?? "x").GetHashCode()) % 30);
                    bool weatherCall = wrongTyre && st.weatherCallTimer == 0f;
                    bool worn = st.wear > 0.92f || HasPuncture(st.car);
                    int lapsLeft = race.Settings.laps - lapNow;
                    // Parada PLANEJADA espera uma volta se o colega de box ainda está no pit (sem
                    // "double stack" na garagem) ou se o corredor já tem 4+ carros. Pneu no fim/furo
                    // e chamada de clima entram mesmo assim.
                    if (planned && !worn && !weatherCall && (TeammateInPit(st, e) || CarsInPitLaneMovingCount() >= 4))
                        planned = false;
                    if ((planned || weatherCall || worn) && lapsLeft >= 2)
                    {
                        st.requested = true;
                        Debug.Log("[PIT-IA] " + e.name + " volta " + lapNow + "/" + race.Settings.laps + " motivo=" + (worn ? (HasPuncture(st.car) ? "furo" : "desgaste " + Mathf.RoundToInt(st.wear * 100f) + "%") : weatherCall ? "clima (pneu " + TyreModel.Letter(st.compound) + ", água " + Mathf.RoundToInt(WeatherSystem.Wetness * 100f) + "%)" : "planejada (parada " + (st.stopsDone + 1) + ")"));
                    }
                }
                // ENTRADA REAL (regra do usuário): quem DIRIGE para dentro do corredor (lado do
                // pit, lat < −8.5 m, na direção da pista e antes do próprio box) tem o fluxo assumido — com ou sem box pedido
                // (pediu, chegou rápido demais para a rota assumir, freou e entrou dirigindo: vale).
                if (!st.inPit && !st.holding && !st.exiting && st.car.SpeedKmh > 1f && Time.time - st.lastExitTime > 25f)
                {
                    var spNow = spline.PoseAtDistance(Mathf.Repeat(dist, spline.Length));
                    float latIn = Vector3.Dot(st.car.transform.position - spNow.position, spNow.rotation * Vector3.right);
                    float fwdDot = Vector3.Dot(st.car.transform.forward, spNow.rotation * Vector3.forward);
                    float boxRel = Mathf.Repeat(st.boxS - laneStart, spline.Length);
                    if (latIn < -8.5f && latIn > -22f && fwdDot > 0.45f && rel > 4f && rel < Mathf.Min(38f, boxRel + 4f))
                    {
                        st.inPit = true;
                        st.requested = false;
                        st.car.pitControlled = true;
                        BuildEntryRoute(st, rel, false); // conserto só com o carro PARADO no box (fim do StopSeconds)
                        if (st.isHuman) Debug.Log("[PITWATCH] carro entrou dirigindo no corredor — fluxo assumido (auto até o box)");
                        return;
                    }
                }
                // Pedido de box: só entra se estiver ANTES da entrada (32–75 m antes do lane) E se der para entrar (perto do lado
                // do pit e numa velocidade que freia a tempo — CaptureFeasible). Passou da janela sem dar? Fica para a próxima
                // volta (o pedido continua) — antes assumia a 145 km/h no meio da pista e o carro batia no bico da mureta laranja.
                if (st.requested && !st.entryMissed && rel > spline.Length - 75f && rel < spline.Length - 32f)
                {
                    var spCap = spline.PoseAtDistance(Mathf.Repeat(dist, spline.Length));
                    float latCap = Vector3.Dot(st.car.transform.position - spCap.position, spCap.rotation * Vector3.right);
                    if (CaptureFeasible(st.car.ForwardSpeed, spline.Length - rel, latCap))
                    {
                        st.inPit = true;
                        st.requested = false;
                        st.car.pitControlled = true;
                        BuildEntryRoute(st, 0f, true); // conserto só com o carro PARADO no box
                        if (st.isHuman) Debug.Log("[PITWATCH] rota do box assumiu a " + st.car.SpeedKmh.ToString("0") + " km/h, " + (spline.Length - rel).ToString("0") + " m antes do corredor, lateral " + latCap.ToString("0.0"));
                        return;
                    }
                }
                if (st.requested && !st.entryMissed && (rel >= spline.Length - 32f || rel < 40f))
                {
                    st.entryMissed = true;
                    Debug.Log("[PIT] " + e.name + ": passou da entrada do box a " + st.car.SpeedKmh.ToString("0") + " km/h — box na próxima volta");
                    if (st.isHuman) Radio(st, "ENTRADA DO BOX PERDIDA — box na próxima volta");
                }
                return;
            }

            // Fila do treino/qualificação (SEXTA E SÁBADO): parado na PRÓPRIA CAIXA (garagem).
            // Cada equipe tem sua estratégia e ciência de quantos carros estão na pista para sair em
            // janela limpa sem lotar a pista, mas garantindo saída a tempo de fechar suas voltas.
            if (st.holding)
            {
                st.releaseTimer -= Time.deltaTime;
                // Jogador na garagem com o MENU DA CARREIRA aberto: NUNCA sai sozinho (antes saía após
                // 3 s e ficava parado na pista levando batida). Sai ao fechar o hub (ReleasePlayerNow).
                if (st.isHuman && RaceX.UI.CareerFlow.IsOpen) { st.releaseTimer = Mathf.Max(st.releaseTimer, 1.5f); ApplyHold(st); return; }
                if (st.releaseTimer <= 0f)
                {
                    if (!st.isHuman && PracticeSession)
                    {
                        // 1) Corredor perto do box ocupado ou muitos carros manobrando no pit lane → aguarda abrir.
                        if (!IsCorridorClearNearBox(st) || CarsInPitLaneMovingCount() >= 3)
                        {
                            st.releaseTimer = 1.6f;
                            ApplyHold(st);
                            return;
                        }
                        // 2) Ciência de tráfego na pista vs. tempo restante da sessão:
                        int onTrack = CarsOnTrackCount();
                        float rem = SessionRemainingSeconds();
                        int maxDesiredTraffic = 7 + (e.gridSlot % 5); // 7..11 carros conforme a equipe
                        bool hasTimedLap = e.bestLap < float.MaxValue && e.bestLap > 1f;
                        // Se a pista está cheia E ainda há tempo seguro para não ficar sem volta (ou já tem volta marcada),
                        // espera uma janela mais limpa na pista.
                        if (onTrack >= maxDesiredTraffic && (rem > 240f || (hasTimedLap && rem > 130f)))
                        {
                            st.releaseTimer = Random.Range(6f, 14f);
                            ApplyHold(st);
                            return;
                        }
                    }
                    // Jogador no treino (v99, pedido do usuário): sai NO INSTANTE em que a troca termina — segura só se está passando
                    // alguém na frente do box naquele momento (30 m antes até 3 m depois, na faixa), no máximo 6 s como na corrida.
                    // Build 16 medido: 30 m de janela + 6 s, somados à cessão de vez da saída (+6 s), deixavam o carro ~10 s parado e o
                    // resgate disparava 2×. Agora: só quem CHEGA em ~1,5 s segura, no máximo 3 s, e a espera conta como deliberada.
                    if (st.isHuman && PracticeSession && LaneBusyAtBox(st, 1.5f) && st.releaseWait < 3f)
                    {
                        st.releaseWait += Time.deltaTime;
                        WaitingOnPurpose(st);
                        ApplyHold(st);
                        return;
                    }
                    Release(st);
                }
                else ApplyHold(st);
                return;
            }

            // DOUBLE STACK (corrida): o companheiro ainda está parado no box → espera atrás (a ~10 m da marca) até ele sair,
            // como na F1. Antes o 2º carro parava em cima do 1º e ganhava a equipe dele.
            // v101 (relato do usuário, 04/11): a espera só começava a < 10 m da marca em qualquer direção — o carro chegava pela faixa
            // rápida, PASSAVA do companheiro, parava à frente dele e, liberado, dava a volta pela garagem vizinha (atravessou paredes).
            // Agora a conta é AO LONGO da faixa: com a marca ainda à frente (até 35 m), reduz pela distância e para ~8 m ATRÁS dela;
            // já tendo passado da marca, não espera — encaixa quando a vaga vagar (SpotTaken abaixo).
            if (!PracticeSession && st.stopTimer <= 0f && BoxBusyByOther(st, e))
            {
                Vector3 laneFwd = StopPose(e).rotation * Vector3.forward; laneFwd.y = 0f; laneFwd.Normalize();
                Vector3 toMark = st.boxTarget - st.car.transform.position; toMark.y = 0f;
                float ahead = Vector3.Dot(toMark, laneFwd);
                // v108 (relato do usuário: "mesmo com o parceiro no box e eu atrás no corredor, meu carro passa dele"): a espera começava a
                // 35 m da marca com ~0,3 g — de 80 km/h não dava para parar. A rota já freia para DoubleStackGap atrás desde o corredor;
                // aqui segura a partir de 60 m com a mesma freada.
                if (ahead > 0f && ahead < 60f)
                {
                    const float holdAt = DoubleStackGap;
                    float vCap = ahead > holdAt + 0.5f ? Mathf.Sqrt(2f * PitApproachDecel() * (ahead - holdAt)) : 0f;
                    if (vCap > 0.8f)
                    {
                        if (!DriveRoute(st, Mathf.Min(LaneLimitMs, vCap)))
                        {
                            float steerW = Vector3.SignedAngle(st.car.transform.forward, toMark.normalized, Vector3.up) / 30f;
                            st.car.extSteer = Mathf.Clamp(steerW, -1f, 1f);
                            st.car.extThrottle = st.car.ForwardSpeed < vCap ? 0.25f : 0f;
                            st.car.extBrake = st.car.ForwardSpeed > vCap + 0.3f ? 1f : 0f;
                        }
                        else if (st.car.ForwardSpeed > vCap + 0.3f) { st.car.extThrottle = 0f; st.car.extBrake = 1f; }
                    }
                    else { st.car.extThrottle = 0f; st.car.extBrake = 1f; st.car.extSteer = 0f; }
                    st.stuckTimer = 0f; st.stillTimer = 0f; st.finalApproachT = 0f;
                    st.pitCtrlSince = Time.time;   // esperar a vez não é travamento
                    return;
                }
            }
            // Piloto automático do pit (na caixa/navegação por rota): limita velocidade, para NA SUA GARAGEM.
            Vector3 toBox = st.car.transform.position; toBox.y = 0f;
            if (!DriveRoute(st, LaneLimitMs))
            {
                toBox = st.boxTarget - st.car.transform.position; toBox.y = 0f;
                float dBox = toBox.magnitude;
                float speed = st.car.ForwardSpeed;
                // Treino (garagem): encaixa a 5,5 m como antes. Corrida (faixa interna, à vista): dirige devagar até
                // 0,9 m da marca para o encaixe final não aparecer; limite de 6 s para nunca travar.
                bool garageStop = PracticeSession;
                float snapAt = garageStop ? 5.5f : 0.9f;
                if (dBox > snapAt && st.stopTimer <= 0f)
                {
                    st.finalApproachT += Time.deltaTime;
                    // Passou da marca (ela ficou para trás) → encaixa JÁ. Antes o carro passava da equipe, dava meia-volta e
                    // demorava para voltar (relato do usuário).
                    Vector3 fwdA = st.car.transform.forward; fwdA.y = 0f;
                    bool passed = !garageStop && dBox < 7f && Vector3.Dot(fwdA, toBox) < 0f;
                    // v108 (pesquisa: ré com o próprio motor é PROIBIDA no pit lane — F1 2026 B1.6.3b, F2 2026 art. 28.3; quem passa da
                    // marca é puxado de volta pelos mecânicos): passou da marca → a equipe PUXA o carro de volta a 1,2 m/s até a marca.
                    // Antes o carro era "teleportado" para a marca (parecia atropelar os mecânicos e reaparecer — relato do usuário).
                    if (passed && dBox > 0.6f && !SpotTaken(StopPose(e).position, st.car))
                    {
                        if (!st.pulledBack) { st.pulledBack = true; PassedSnaps++; }
                        st.car.extThrottle = 0f; st.car.extBrake = 0f; st.car.extSteer = 0f;
                        var rbP = st.car.Body;
                        if (rbP != null)
                        {
                            Vector3 pull = toBox / Mathf.Max(0.01f, dBox) * Mathf.Min(1.2f, 0.6f + dBox);
#if UNITY_6000_0_OR_NEWER
                            rbP.linearVelocity = new Vector3(pull.x, rbP.linearVelocity.y, pull.z);
#else
                            rbP.velocity = new Vector3(pull.x, rbP.velocity.y, pull.z);
#endif
                        }
                        st.stuckTimer = 0f; st.stillTimer = 0f; st.finalApproachT = 0f;
                        st.pitCtrlSince = Time.time;   // ser puxado de volta não é travamento
                        return;
                    }
                    bool snap = passed || (garageStop ? ((st.isHuman && st.stillTimer > 1.0f && dBox < 24f) || dBox < 8.5f)
                                           : (st.finalApproachT > FinalApproachMax || dBox > 12f));
                    // Vaga OCUPADA (outro carro parado em cima da marca): espera em vez de "encaixar" — o teletransporte por cima
                    // de outro carro fazia a física separar os dois jogando um para cima (v99, relato do usuário 03/11).
                    if (!snap) st.spotWaitT = 0f;
                    // Só na CORRIDA (marca da faixa interna): no treino a garagem vizinha se sobrepõe (boxes de ~10 m) e o carro do
                    // box ao lado "ocupava" a vaga — o jogador parava fora do centro da equipe por 8 s (relato do usuário, 03/11).
                    // Vaga do COMPANHEIRO (double stack): espera o tempo que for — ele vai sair; os 4 s valem só para carro estranho/quebrado.
                    if (snap && !garageStop && (st.spotWaitT < 4f || BoxBusyByOther(st, e)) && SpotTaken(StopPose(e).position, st.car))
                    {
                        st.spotWaitT += Time.deltaTime;   // 4 s sem liberar (carro quebrado na vaga): encaixa como antes
                        st.car.extThrottle = 0f; st.car.extSteer = 0f;
                        st.car.extBrake = 1f;
                        st.stuckTimer = 0f; st.stillTimer = 0f;
                        st.pitCtrlSince = Time.time;   // esperar a vaga não é travamento
                        return;
                    }
                    if (snap)
                    {
                        if (passed) PassedSnaps++;
                        var pose = StopPose(e);
                        st.car.ResetTo(pose.position, pose.rotation);
                        dBox = 0f;
                    }
                    else
                    {
                        float steer = Vector3.SignedAngle(st.car.transform.forward, toBox.normalized, Vector3.up) / 30f;
                        st.car.extSteer = Mathf.Clamp(steer, -1f, 1f);
                        // Corrida: velocidade pela distância de FREADA que falta até a marca (~0,7 g, para em 0,9 m).
                        float target = garageStop ? Mathf.Clamp(dBox * 0.38f, 2.0f, 5.0f)
                                                  : Mathf.Clamp(Mathf.Sqrt(2f * PitApproachDecel() * Mathf.Max(0f, dBox - 0.9f)), 0.8f, 8.0f);
                        st.car.extThrottle = speed < target ? 0.45f : 0f;
                        st.car.extBrake = speed > target + 0.3f ? Mathf.Clamp((speed - target) / 2f, 0.4f, 1f) : 0f;
                        return;
                    }
                }
                st.finalApproachT = 0f;
                st.pulledBack = false;
                if (st.stopTimer <= 0f)
                {
                    var pose = StopPose(e);
                    st.car.ResetTo(pose.position, pose.rotation);
                    // TEMPO DA PARADA (pesquisa 30/10): troca de 4 pneus ~2,0-2,3 s nas melhores equipes; asa
                    // dianteira/bico soma ~8-12 s (Hamilton, Áustria: ~11 s com troca de asa) — usamos +9 s.
                    var dmgNow = st.car.damage;
                    st.noseChange = dmgNow != null && (dmgNow.Detached((int)RaceX.Car.DamageZone.FrontWing) || dmgNow.FrontLevel >= 0.25f);
                    st.stopDuration = TyreStopSeconds(e) + (st.noseChange ? NoseChangeSeconds : 0f);
                }
                ApplyHold(st);
                st.stopTimer += Time.deltaTime;
                bool workDone = st.stopTimer >= (st.stopDuration > 0f ? st.stopDuration : StopSeconds);
                // LIBERAÇÃO SEGURA (corrida): a equipe segura o carro na marca enquanto vem alguém pela faixa rápida logo
                // atrás (na F1 a "unsafe release" é punida). Antes soltava na hora e o carro que passava batia no que saía.
                // No máximo 6 s de espera (o corredor nunca trava).
                if (workDone && !PracticeSession && !FastLaneClearForRelease(st) && st.releaseWait < 6f)
                {
                    st.releaseWait += Time.deltaTime;
                    workDone = false;
                }
                if (workDone)
                {
                    st.pitCtrlSince = Time.time;   // a saída tem o próprio prazo (fila na entrada sob SC não "gasta" a saída)
                    st.releaseWait = 0f;
                    st.stopDuration = 0f;
                    st.wear = 0f;
                    var pg = race.GetProgress(e);
                    st.stintStartLap = pg != null ? pg.lap : 0;
                    SetTyreTemps(st, TyreModel.BlanketTemp); // jogo novo: cobertor a 70 °C → sai do box abaixo da janela (azul) e aquece em ~1 volta
                    for (int wi = 0; wi < 4; wi++) { st.wearW[wi] = 0f; st.puncture[wi] = false; st.pressure[wi] = 1f; st.leak[wi] = 0f; st.dropShown[wi] = 0f; }
                    st.car.Repair();
                    st.stopTimer = 0f;
                    st.car.extThrottle = 0f;
                    st.weatherCallTimer = -1f;
                    float wetNow = WeatherWetness();
                    if (PracticeSession)
                    {
                        if (!st.isHuman)
                        {
                            // Programa de treino da equipe: próxima saída (curta de classificação / longa de corrida).
                            st.runIndex++;
                            float tier01 = RaceX.Career.CareerData.raceActive ? Mathf.Clamp01(RaceX.Career.CareerMode.TeamTier(RaceX.Career.CareerMode.TeamOfDriver(e.name)) / 11f) : 0.5f;
                            bool quali = race.Settings.careerPhase >= RaceX.Career.CareerMode.PH_SQ1 && race.Settings.careerPhase != RaceX.Career.CareerMode.PH_FP3;
                            TeamStrategy.PracticeRun(st.runIndex, tier01, quali, out int pc, out int pl);
                            st.compound = TyreModel.BestForWetness(wetNow, pc);
                            st.runLaps = pl;
                        }
                        else
                        {
                            ApplyHumanPracticeCompound(st);   // escolha à mão já passa pela água (ManualForWater)
                            if (TyreModel.NeedsWetTyre(wetNow) && !TyreModel.IsWetTyre(st.compound))
                                st.compound = TyreModel.BestForWetness(wetNow, st.compound);
                        }
                    }
                    else if (st.stopsDone < st.strategy.Count - 1)
                    {
                        // Estratégia (IA e jogador): troca para o composto do próximo stint.
                        st.stopsDone++;
                        st.compound = TyreModel.Clamp(st.strategy[Mathf.Min(st.stopsDone, st.strategy.Count - 1)]);
                    }
                    else st.stopsDone++;
                    // Pista molhada/seca manda: pneu certo para a água (a equipe nunca monta slick no aguaceiro).
                    if (!PracticeSession)
                    {
                        int best = TyreModel.BestForWetness(wetNow, TyreModel.IsWetTyre(st.compound) ? 1 : st.compound);
                        if (best != st.compound && (TyreModel.IsWetTyre(best) || TyreModel.IsWetTyre(st.compound))) st.compound = best;
                    }
                    Blanket(st);   // pneu novo na temperatura de saída do composto que foi montado
                    st.lapWearMark = 0f;
                    if (!PracticeSession) { try { ReplanAfterStop(st, e); } catch (System.Exception ex) { Debug.LogWarning("[PIT] replanejar: " + ex.Message); } }
                    if (PracticeSession)
                    {
                        st.holding = true;
                        // Jogador (v99, pedido do usuário): sai NO INSTANTE em que a troca termina — só segura se houver carro passando na
                        // frente do box naquele momento (gate na liberação). Antes: 3 s fixos de espera na garagem, sem olhar nada.
                        st.releaseTimer = st.isHuman ? 0f : Random.Range(24f, 52f) + (e.gridSlot % 4) * 5f;
                        ApplyHold(st);
                        if (st.isHuman && !AutoTest.Active)   // autoteste: o hub travava o teste do treino
                        {
                            RaceX.UI.HUD.ResumeTime();
                            RaceX.UI.CareerFlow.Open();
                        }
                    }
                    else
                    {
                        Release(st);
                    }
                }
            }
    }
}
}
