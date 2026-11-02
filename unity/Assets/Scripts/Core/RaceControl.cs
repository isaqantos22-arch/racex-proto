using System;
using System.Collections.Generic;
using RaceX.Car;
using RaceX.Track;
using UnityEngine;

namespace RaceX.Core
{
    public enum RaceFlag { Green, Yellow, DoubleYellow, Blue, White, Debris, BlackOrange, BlackWhite, Black, VSC, SafetyCar }

    /// <summary>
    /// DIREÇÃO DE PROVA (bandeiras + regras), pesquisa de 29/10 (PESQUISA_racex_v80.md, seção 11):
    /// • Amarela: perigo à frente (carro parado/rodado fora da pista) — reduzir e NÃO ultrapassar.
    ///   Amarela dupla: carro parado NA pista — reduzir bem, pronto para parar, sem ultrapassar.
    /// • Azul: carro com 1 volta de vantagem vindo atrás — deixar passar (ignorar 3 vezes = penalidade).
    /// • Branca: carro muito lento à frente. • Amarela e vermelha: destroço/óleo (peça arrancada na pista).
    /// • Preta e laranja: dano que torna o carro inseguro — ir ao box. • Preta e branca: advertência
    ///   (limites de pista, 3ª infração). • Preta: desclassificado. • VSC: neutralização (volta 1,37–1,42× a normal, medido),
    ///   sem ultrapassagem (introduzido em 2015). Perda total aciona VSC.
    /// Regras: limites de pista = 3 advertências (3ª com preta e branca) e +5 s a partir da 4ª;
    /// ganhar posição fora da pista ou sob amarela/VSC = devolver em 10 s ou +5 s; causar colisão
    /// (bater a frente na traseira de quem está à frente, ≥ 15 g) = +5 s; ignorar a azul 15 s = +5 s;
    /// ignorar a preta e laranja por 2 voltas = desclassificação. Valores de tempo são escolha de projeto
    /// (a FIA usa faixas: 5 s, 10 s, drive-through, stop-go, pontos na licença).
    /// </summary>
    public class RaceControl : MonoBehaviour
    {
        public static RaceControl Instance { get; private set; }
        /// <summary>Aviso da direção de prova para um carro (o HUD mostra os do jogador).</summary>
        public static event Action<RaceEntry, string> Notice;

        public const float PenaltySeconds = 5f;
        public const float GiveBackSeconds = 10f;
        /// <summary>VSC: teto = esta fração da velocidade FINAL do carro. Medido (OpenF1, 2023–2025): volta 1,37–1,42× a normal; nas
        /// retas 45–60% da velocidade normal, nas curvas lentas 80–100% (pesquisa de 02/11, research_notes/…/vsc.md). 0,52 da final
        /// dá ~177 km/h para um F1 (retas ~55% da normal; curva lenta sem efeito). Antes 0,68: ~230 km/h na reta, pouco para VSC.</summary>
        public const float VscSpeedFactor = 0.52f;
        /// <summary>SC acionado, carro ainda indo para a fila (delta do SC até a linha 1): parecido com a VSC, um pouco mais alto
        /// para alcançar a fila (valor de projeto; a volta do acionamento real perde 8–29 s). Antes 0,78.</summary>
        public const float SafetyCarCatchUpFactor = 0.56f;
        /// <summary>AMARELA: fator sobre a velocidade DAQUELE trecho (CarAI), não sobre a final. Medido em corrida (OpenF1, 2023–2025,
        /// 47 passagens por carro parado): simples ~−5% (−13 km/h); dupla mediana −6% (−10 km/h), os 10% mais cautelosos −25% —
        /// 0,88 = "reduzir significativamente, pronto para parar". Ninguém para na pista (parar só sob bandeira vermelha, no pit
        /// lane). Pesquisa de 02/11 (research_notes/…/amarela_e_amarela_dupla.md). Antes 0,9/0,75 da velocidade FINAL: só pesava
        /// perto da velocidade máxima.</summary>
        public const float DoubleYellowFactor = 0.88f;
        public const float YellowFactor = 0.95f;
        /// <summary>Carro destruído NA PISTA até os fiscais retirarem (s). Era 12 s; o usuário liberou aumentar — os outros
        /// contornam (CarAI.ObstacleGapLat) em vez de parar atrás. Valor de projeto.</summary>
        public const float WreckClearSeconds = 30f;

        class Incident { public float along; public bool dbl, debris; public float until; public CarController car; }

        class State
        {
            public RaceEntry e;
            public int hint;
            public float along, lat;
            public bool wasOff;
            /// <summary>Saiu da pista FORÇADO (toque de outro carro ou espremido por um emparelhado): não conta como limite de pista
            /// nem obriga a devolver posição.</summary>
            public bool forcedOff;
            public float lastContact = -99f;
            public readonly List<RaceEntry> aheadAtOff = new List<RaceEntry>();
            public RaceEntry giveBackTo;
            public float giveBackUntil;
            public string giveBackWhy;
            public float blueTime;
            public int blueWarn;
            /// <summary>Já punido por esta sequência de bandeira azul — não pune de novo até o líder passar.</summary>
            public bool bluePenalized;
            /// <summary>Quem está colocando a volta (texto da bandeira azul).</summary>
            public string blueBy = "";
            public int meatballLap = -1;
            public float bwUntil;
            public RaceFlag flag;
            public float greenUntil;
            public string greenText = "";
            public bool pitAsked;
            public float wreckAt = -1f;
        }

        RaceManager race;
        WaypointSystem wp;
        float trackLen = 1000f;
        float half = 7f;
        float timer;
        float vscUntil;
        readonly List<Incident> incidents = new List<Incident>();
        readonly Dictionary<CarController, State> states = new Dictionary<CarController, State>();
        readonly Dictionary<long, float> pairCooldown = new Dictionary<long, float>();
        readonly List<RaceEntry> order = new List<RaceEntry>();

        public static bool VscActive => Instance != null && Time.time < Instance.vscUntil;

        /// <summary>Regra da corrida "Penalidades" (RaceSettings.penalties, definida no RaceBootstrap). Desligada: avisos e
        /// bandeiras continuam, mas ninguém leva tempo, "devolva a posição" nem desclassificação.</summary>
        public static bool PenaltiesEnabled = true;

        bool Online => race != null && race.Settings != null && race.Settings.mode == GameMode.Online;

        public static RaceControl Create(RaceManager race)
        {
            var go = new GameObject("RaceControl");
            var rc = go.AddComponent<RaceControl>();
            rc.race = race;
            rc.wp = race.Track != null ? race.Track.Waypoints : null;
            if (rc.wp != null && rc.wp.Spline != null) rc.trackLen = Mathf.Max(100f, rc.wp.Spline.Length);
            Instance = rc;
            CarDamage.CarTotaled += rc.OnTotaled;
            CarDamage.PartDetached += rc.OnPartDetached;
            CarDamage.CarHitCar += rc.OnCarHitCar;
            CarController.CarTouch += rc.OnCarTouch;
            return rc;
        }

        void OnDestroy()
        {
            CarDamage.CarTotaled -= OnTotaled;
            CarDamage.PartDetached -= OnPartDetached;
            CarDamage.CarHitCar -= OnCarHitCar;
            CarController.CarTouch -= OnCarTouch;
            if (Instance == this) Instance = null;
        }

        bool IsRace => race != null && race.Settings != null && race.Settings.laps < 900;
        bool Running => race != null && race.State == RaceState.Racing;

        State S(CarController c)
        {
            if (c == null) return null;
            if (states.TryGetValue(c, out var s)) return s;
            var e = race.GetEntry(c);
            if (e == null) return null;
            s = new State { e = e, hint = -1 };
            states[c] = s;
            return s;
        }

        void Say(RaceEntry e, string msg)
        {
            if (e == null) return;
            if (e.isHuman) Debug.Log("[DIREÇÃO] " + e.name + ": " + msg);
            Notice?.Invoke(e, msg);
        }

        /// <summary>Há perigo na pista (carro destruído/destroços com amarela dupla ainda ativa)? O SC só sai depois.</summary>
        public static bool HazardActive
        {
            get
            {
                if (Instance == null) return false;
                foreach (var i in Instance.incidents) if ((i.dbl || i.debris) && Time.time < i.until) return true;
                return false;
            }
        }

        /// <summary>Aviso da direção de prova para UM piloto.</summary>
        public static void Tell(RaceEntry e, string msg) { if (Instance != null) Instance.Say(e, msg); }

        /// <summary>Abandono por QUEBRA (freio, motor, combustível — CarSystems): fora da corrida; o carro da IA
        /// sai da pista em 3 s, como no desclassificado.</summary>
        public static void MechanicalRetire(CarController car, string reason, string msg)
        {
            var rc = Instance;
            if (rc == null || rc.race == null || car == null) return;
            var s = rc.S(car);
            if (s == null || s.e.retired || s.e.finished) return;
            rc.race.Retire(s.e, reason);
            s.wreckAt = Time.time - (WreckClearSeconds - 3f);   // quebra: sai da pista em 3 s
            var ai = car.GetComponent<CarAI>();
            if (ai != null) ai.enabled = false;
            car.wrecked = true;
            rc.Say(s.e, msg + " — fora da corrida");
        }

        /// <summary>Aviso da direção de prova para TODOS os pilotos.</summary>
        public static void Broadcast(string msg)
        {
            if (Instance == null) return;
            foreach (var kv in Instance.states) Instance.Say(kv.Value.e, msg);
        }

        float lastTotaledAt = -99f;

        void Penalize(RaceEntry e, string why)
        {
            if (!IsRace || e == null || e.retired || !PenaltiesEnabled) return;
            if (e.car != null && e.car.IsRemote) return;   // online: quem pune é o aparelho dono do carro
            race.AddPenalty(e, PenaltySeconds);
            Debug.Log("[PENALIDADE] " + e.name + " +" + PenaltySeconds + " s — " + why);
            Say(e, "PENALIDADE +" + PenaltySeconds.ToString("0") + " s — " + why);
        }

        // ---------------- Eventos de dano ----------------
        void OnTotaled(CarController car)
        {
            var s = S(car);
            if (s == null) return;
            race.Retire(s.e, "PT");
            s.flag = RaceFlag.Black;
            s.wreckAt = Time.time;
            var ai = car.GetComponent<CarAI>();
            if (ai != null) ai.enabled = false;
            var input = car.GetComponent<RaceX.Input.PlayerInputHandler>();
            if (input != null) input.enabled = false;
            UpdateTrackPos(s);
            // SAFETY CAR quando o carro destruído ficou NA PISTA ou houve 2+ perdas totais em 20 s (acidente múltiplo:
            // fiscais precisam entrar). Fora da pista, sozinho: VSC, como antes.
            bool onTrack = Mathf.Abs(s.lat) <= half + 1.5f;
            bool multi = Time.time - lastTotaledAt < 20f;
            lastTotaledAt = Time.time;
            bool sc = (onTrack || multi) && (SafetyCar.Deployed || SafetyCar.Deploy(s.e.name + " (perda total" + (onTrack ? " na pista" : "") + ")"));
            // Amarela dupla enquanto o destroço estiver NA pista (os fiscais levam WreckClearSeconds) — antes 15 s, com o carro
            // ainda lá a bandeira sumia.
            incidents.Add(new Incident { along = s.along, dbl = true, until = Time.time + (sc ? 45f : onTrack ? WreckClearSeconds + 2f : 15f), car = car });
            string why = car.damage != null ? car.damage.RetireReason : "";
            Say(s.e, why.Length == 0 ? "PERDA TOTAL — BANDEIRA PRETA: fora da corrida"
                   : why.StartsWith("PERDA TOTAL") ? why + " — fora da corrida" : "ABANDONO — " + why);
            if (sc)
            {
                vscUntil = 0f;
                // Texto claro: o SC entrou POR CAUSA da perda total de outro carro ("SAFETY CAR — X sofreu perda total" foi
                // lido como "o safety car sofreu perda total").
                foreach (var kv in states) if (kv.Value.e != s.e) Say(kv.Value.e, "SAFETY CAR NA PISTA — acidente de " + s.e.name + " (perda total" + (multi ? ", acidente múltiplo" : "") + ")");
            }
            else if (!Online)
            {
                // Online sem VSC: valeria só neste aparelho (cada um tem a própria direção de prova) e a corrida dessincronizaria.
                vscUntil = Mathf.Max(vscUntil, Time.time + (onTrack ? WreckClearSeconds + 2f : 25f));
                foreach (var kv in states) if (kv.Value.e != s.e) Say(kv.Value.e, "VSC — acidente de " + s.e.name + " (perda total)");
            }
        }

        void OnPartDetached(CarController car, int zone, Vector3 at)
        {
            var s = S(car);
            if (s == null) return;
            UpdateTrackPos(s);
            // A bandeira de destroços segue os DESTROÇOS DE VERDADE (RefreshDebrisIncidents): antes ficava 20 s no ponto
            // da batida mesmo com a peça já longe/sumida ("bandeira de destroços sem nada na pista").
            Say(s.e, "VOCÊ PERDEU: " + CarDamage.ZoneNames[zone]);
        }

        /// <summary>QUALQUER toque entre dois carros (leve, roda com roda, lateral): marca o contato dos dois. Sem isso só a batida
        /// da ASA DIANTEIRA a partir do dano laranja contava — quem era empurrado para fora num toque leve levava advertência de
        /// limite de pista (relato do usuário; FIA: forçado para fora não conta).</summary>
        void OnCarTouch(CarController a, CarController b)
        {
            float now = Time.time;
            var sa = S(a); if (sa != null) sa.lastContact = now;
            var sb = S(b); if (sb != null) sb.lastContact = now;
        }

        void OnCarHitCar(CarController striker, CarController victim, float g)
        {
            if (!IsRace || !Running || g < CarDamage.GOrange) return;
            var a = S(striker); var b = S(victim);
            if (a == null || b == null) return;
            a.lastContact = b.lastContact = Time.time;
            // Bateu a FRENTE em quem estava à frente (traseira do outro): culpa de quem vem atrás.
            if (b.e.raceDistance <= a.e.raceDistance) return;
            if (victim.SpeedKmh < 15f) return; // carro parado = incidente de corrida (já há amarela)
            long key = ((long)striker.GetInstanceID() << 32) ^ (uint)victim.GetInstanceID();
            if (pairCooldown.TryGetValue(key, out float until) && Time.time < until) return;
            pairCooldown[key] = Time.time + 10f;
            Penalize(a.e, "causou colisão com " + b.e.name);
        }

        // ---------------- Laço da direção de prova (5 Hz) ----------------
        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (race == null || wp == null || wp.Spline == null) return;
            if (Running) PushWrecks(Time.deltaTime);
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            float dt = 0.2f - timer;
            timer = 0.2f;
            if (!Running) return;
            float now = Time.time;
            incidents.RemoveAll(i => now > i.until);
            RefreshDebrisIncidents(now);

            order.Clear();
            foreach (var e in race.entries)
            {
                if (e.car == null) continue;
                var s = S(e.car);
                if (s == null) continue;
                // Destroço retirado: o carro destruído sai da pista depois de WreckClearSeconds (os outros contornam até lá).
                if (e.retired && !e.isHuman && s.wreckAt > 0f && now - s.wreckAt > WreckClearSeconds && e.car.gameObject.activeSelf) e.car.gameObject.SetActive(false);
                if (e.retired || !e.car.gameObject.activeInHierarchy) continue;
                UpdateTrackPos(s);
                order.Add(e);
                bool inPit = PitLaneSystem.IsInPitFlow(e.car);
                // Carro parado/rodado depois da largada = incidente (amarela; dupla se estiver NA pista).
                if (!inPit && race.RaceTime > 8f && e.car.SpeedKmh < 20f && !e.finished)
                    AddOrRefresh(s, Mathf.Abs(s.lat) <= half + 1f);
            }

            foreach (var e in order)
            {
                var s = S(e.car);
                var before = s.flag;
                s.flag = ComputeFlag(s);
                // VERDE quando o PERIGO acaba (fim de SC/VSC/amarela), mesmo que a próxima bandeira do carro seja azul
                // ou branca — antes só aparecia se a bandeira virasse "verde" e, saindo do SC levando volta, ia direto
                // para a azul e a verde nunca era mostrada.
                if (IsHazard(before) && !IsHazard(s.flag))
                {
                    string g = before == RaceFlag.SafetyCar ? "BANDEIRA VERDE — relargada: corrida liberada"
                             : before == RaceFlag.VSC ? "BANDEIRA VERDE — fim do VSC: corrida liberada"
                             : before == RaceFlag.Yellow || before == RaceFlag.DoubleYellow || before == RaceFlag.Debris ? "BANDEIRA VERDE — fim da zona de amarela: pode ultrapassar"
                             : "";
                    if (g.Length > 0) { s.greenText = g; s.greenUntil = now + 4f; }
                }
                if (e.finished) continue;
                Rules(s, dt, now);
            }
        }

        void AddOrRefresh(State s, bool onTrack)
        {
            foreach (var i in incidents)
                if (i.car == s.e.car) { i.until = Time.time + 3f; i.along = s.along; i.dbl = onTrack; return; }
            incidents.Add(new Incident { along = s.along, dbl = onTrack, until = Time.time + 3f, car = s.e.car });
        }

        /// <summary>Amarela e vermelha só onde HÁ destroço EM CIMA do asfalto (até 1,5 m da borda); some quando a
        /// peça é retirada ou sai da pista (jogada para a grama por outro carro).</summary>
        void RefreshDebrisIncidents(float now)
        {
            incidents.RemoveAll(i => i.debris);
            foreach (var d in CarDebris.Live)
            {
                if (d == null) continue;
                var pos = d.transform.position;
                float along = wp.DistanceAlong(pos, ref d.hint);
                var p = wp.Spline.PoseAtDistance(along);
                float lat = Vector3.Dot(pos - p.position, p.rotation * Vector3.right);
                if (Mathf.Abs(lat) <= half + 1.5f) incidents.Add(new Incident { along = along, debris = true, until = now + 1f });
            }
        }

        void UpdateTrackPos(State s)
        {
            var c = s.e.car;
            s.along = wp.DistanceAlong(c.transform.position, ref s.hint);
            var p = wp.Spline.PoseAtDistance(s.along);
            s.lat = Vector3.Dot(c.transform.position - p.position, p.rotation * Vector3.right);
        }

        float Ahead(float from, float to) => Mathf.Repeat(to - from, trackLen);

        RaceFlag ComputeFlag(State s)
        {
            var e = s.e;
            if (e.retired) return RaceFlag.Black;
            if (e.car.damage != null && e.car.damage.NeedsPit) return RaceFlag.BlackOrange;
            if (SafetyCar.Deployed) return RaceFlag.SafetyCar;
            if (VscActive) return RaceFlag.VSC;
            bool dbl = false, yel = false, deb = false;
            foreach (var i in incidents)
            {
                if (i.car == e.car) continue;
                float d = Ahead(s.along, i.along);
                bool inZone = d < 300f || d > trackLen - 30f;
                if (!inZone) continue;
                if (i.debris) deb = true;
                else if (i.dbl) dbl = true;
                else yel = true;
            }
            if (dbl) return RaceFlag.DoubleYellow;
            if (yel) return RaceFlag.Yellow;
            if (BlueFor(s)) return RaceFlag.Blue;
            if (deb) return RaceFlag.Debris;
            if (SlowAhead(s)) return RaceFlag.White;
            if (Time.time < s.bwUntil) return RaceFlag.BlackWhite;
            return RaceFlag.Green;
        }

        bool BlueFor(State s)
        {
            if (!IsRace) return false;
            // No pit (entrando, parado no box ou saindo) não existe bandeira azul: quem está no corredor não
            // atrapalha quem vem na pista — antes o jogador levava +5 s PARADO no box.
            if (PitLaneSystem.IsInPitFlow(s.e.car) || s.e.car.pitControlled || IsInPitArea(s.e.car)) return false;
            foreach (var o in order)
            {
                if (o == s.e || o.car == null) continue;
                if (PitLaneSystem.IsInPitFlow(o.car) || IsInPitArea(o.car)) continue;
                if (o.raceDistance < s.e.raceDistance + trackLen * 0.95f) continue; // não tem volta de vantagem
                var os = S(o.car);
                float behind = Ahead(os.along, s.along);
                // Azul com QUALQUER carro que vai colocar volta (não só o líder: 2º, 3º... também), colado (~1 s).
                if (behind < 40f) { s.blueBy = o.name; return true; }
            }
            return false;
        }

        bool IsInPitArea(RaceX.Car.CarController c) => race != null && race.Track != null && race.Track.InPitZone(c.transform.position, 1f);

        bool SlowAhead(State s)
        {
            float me = s.e.car.SpeedKmh;
            if (me < 60f) return false;
            foreach (var o in order)
            {
                if (o == s.e) continue;
                var os = S(o.car);
                float d = Ahead(s.along, os.along);
                if (d < 200f && o.car.SpeedKmh > 15f && o.car.SpeedKmh < me * 0.45f && !PitLaneSystem.IsInPitFlow(o.car)) return true;
            }
            return false;
        }

        // ---------------- Regras / penalidades ----------------
        void Rules(State s, float dt, float now)
        {
            var e = s.e;
            var car = e.car;
            if (car.IsRemote) return;   // online: as regras do carro de outro aparelho valem lá (senão cada um puniria diferente)
            bool inPit = PitLaneSystem.IsInPitFlow(car) || car.pitControlled;

            // Preta e laranja: dano inseguro = box. IA vai sozinha; jogador tem 2 voltas, senão bandeira preta.
            int lapNow = race.GetProgress(e) != null ? race.GetProgress(e).lap : 1;
            if (s.flag == RaceFlag.BlackOrange)
            {
                if (s.meatballLap < 0) { s.meatballLap = lapNow; s.pitAsked = false; Say(e, "BANDEIRA PRETA E LARANJA — dano: vá para o BOX"); }
                if (!e.isHuman && !s.pitAsked && IsRace) { PitLaneSystem.ForcePitRequest(car); s.pitAsked = true; }
                if (IsRace && PenaltiesEnabled && lapNow - s.meatballLap >= 2 && !inPit)
                {
                    race.Retire(e, "DSQ");
                    s.wreckAt = now - (WreckClearSeconds - 3f); // IA desclassificada sai da pista em 3 s
                    var ai = car.GetComponent<CarAI>();
                    if (ai != null) ai.enabled = false;
                    car.wrecked = true;
                    Say(e, "BANDEIRA PRETA — desclassificado (não foi ao box com o carro inseguro)");
                    return;
                }
            }
            else s.meatballLap = -1;

            if (!IsRace) return;
            // Penalidades desligadas: sem advertência de limites, "devolva a posição" nem punição de azul.
            if (!PenaltiesEnabled) { s.giveBackTo = null; s.wasOff = car.IsOffTrack && !inPit; RememberOrder(s); return; }

            // Limites de pista: só conta saída "sozinho". FORÇADO para fora — qualquer toque de outro carro nos últimos 2 s (mesmo
            // leve) ou ESPREMIDO por um carro emparelhado do lado de dentro — não conta nem obriga a devolver posição (FIA, art. 33.3:
            // sair da pista "com motivo justificável"; notas da direção de prova: quem é forçado para fora não entra na contagem).
            bool off = car.IsOffTrack && !inPit;
            if (off && !s.wasOff)
            {
                s.aheadAtOff.Clear();
                foreach (var o in order)
                    if (o != e && o.raceDistance > e.raceDistance && o.raceDistance - e.raceDistance < 80f) s.aheadAtOff.Add(o);
                s.forcedOff = now - s.lastContact <= 2f || SqueezedOff(s) || AvoidingIncident(s, now);
                if (car.SpeedKmh > 60f && !s.forcedOff)
                {
                    e.trackLimitStrikes++;
                    if (e.trackLimitStrikes < 3) Say(e, "LIMITES DE PISTA — advertência " + e.trackLimitStrikes + "/3");
                    else if (e.trackLimitStrikes == 3) { s.bwUntil = now + 6f; Say(e, "BANDEIRA PRETA E BRANCA — limites de pista (3/3): a próxima é penalidade"); }
                    else Penalize(e, "limites de pista (" + e.trackLimitStrikes + "ª)");
                }
            }
            if (!off && s.wasOff && s.giveBackTo == null && !s.forcedOff)
            {
                // Voltou para a pista: ganhou posição de alguém que estava à frente? Devolve em 10 s.
                RaceEntry gained = null;
                foreach (var o in s.aheadAtOff)
                    if (!o.retired && o.raceDistance < e.raceDistance && (gained == null || o.raceDistance > gained.raceDistance)) gained = o;
                if (gained != null) Demand(s, gained, "ganhou posição fora da pista", now);
            }
            s.wasOff = off;

            // Ultrapassagem sob amarela / VSC (exceto carro com problema óbvio, bem mais lento).
            if (s.giveBackTo == null && !SafetyCar.Unlapping(car) && (s.flag == RaceFlag.Yellow || s.flag == RaceFlag.DoubleYellow || s.flag == RaceFlag.VSC || s.flag == RaceFlag.SafetyCar))
            {
                foreach (var o in order)
                {
                    if (o == e || o.retired) continue;
                    float gap = e.raceDistance - o.raceDistance;
                    if (gap <= 0f || gap > 12f) continue; // acabou de passar
                    var os = S(o.car);
                    if (os.flag != RaceFlag.Yellow && os.flag != RaceFlag.DoubleYellow && os.flag != RaceFlag.VSC && os.flag != RaceFlag.SafetyCar) continue;
                    if (ObviousProblem(o.car)) continue;
                    // Ele me DEVIA a posição e abriu: isso é a devolução, não ultrapassagem (antes virava pingue-pongue de
                    // "devolva a posição" entre os dois — log de 02/11, Max Keller × Diego Morales).
                    if (os.giveBackTo == e) continue;
                    if (!PassedRecently(s, o)) continue;
                    Demand(s, o, SafetyCar.Deployed ? "ultrapassou sob SAFETY CAR" : VscActive ? "ultrapassou sob VSC" : "ultrapassou sob bandeira amarela", now);
                    break;
                }
            }
            RememberOrder(s);

            // Devolver posição: resolvido se o outro voltou à frente; senão +5 s.
            if (s.giveBackTo != null)
            {
                if (s.giveBackTo.retired || s.giveBackTo.raceDistance > e.raceDistance + 1f)
                {
                    Say(e, "Posição devolvida a " + s.giveBackTo.name + " — sem penalidade");
                    s.giveBackTo = null;
                }
                else if (now > s.giveBackUntil)
                {
                    Penalize(e, s.giveBackWhy + " (não devolveu)");
                    s.giveBackTo = null;
                }
            }

            // Bandeira azul ignorada: 3 avisos (5 s cada) = penalidade — UMA por sequência (antes repetia +5 s a
            // cada 15 s enquanto o líder seguia atrás: 10 punições no mesmo retardatário em 4 min).
            if (s.flag == RaceFlag.Blue)
            {
                if (!s.bluePenalized)
                {
                    s.blueTime += dt;
                    int warn = Mathf.FloorToInt(s.blueTime / 5f);
                    if (warn > s.blueWarn)
                    {
                        s.blueWarn = warn;
                        if (warn < 3) Say(e, "BANDEIRA AZUL (" + warn + "/3) — deixe " + s.blueBy + " passar");
                        else { Penalize(e, "ignorou a bandeira azul"); s.bluePenalized = true; }
                    }
                }
            }
            else
            {
                s.blueTime = Mathf.Max(0f, s.blueTime - dt);
                if (s.blueTime <= 0f) { s.blueWarn = 0; s.bluePenalized = false; }
            }
        }

        /// <summary>FISCAIS EMPURRAM o carro destruído para fora do asfalto: MarshalDelay s depois da batida começa a levá-lo para a borda
        /// mais perto a MarshalPushSpeed m/s, até ficar fora da pista (continua visível até WreckClearSeconds). Na F1 é o que os fiscais
        /// fazem com carro parado na pista. Pior caso do autoteste (dois carros atravessados de borda a borda): a fila esperava os 30 s
        /// inteiros — agora espera alguns segundos. Valores de projeto.</summary>
        const float MarshalDelay = 4f, MarshalPushSpeed = 1.5f;
        void PushWrecks(float dt)
        {
            float now = Time.time;
            foreach (var kv in states)
            {
                var s = kv.Value;
                var e = s.e;
                if (e == null || !e.retired || e.isHuman || s.wreckAt <= 0f || e.car == null || !e.car.gameObject.activeSelf) continue;
                if (now - s.wreckAt < MarshalDelay || (now - s.wreckAt > WreckClearSeconds)) continue;
                var c = e.car;
                if (c.IsRemote) continue;   // online: o carro de outro aparelho é tratado lá
                int h = s.hint;
                float a = wp.DistanceAlong(c.transform.position, ref h);
                s.hint = h;
                var pose = wp.Spline.PoseAtDistance(a);
                Vector3 right = pose.rotation * Vector3.right;
                float lat = Vector3.Dot(c.transform.position - pose.position, right);
                if (Mathf.Abs(lat) >= half + 3f) continue;   // já fora do asfalto (com folga)
                var rb = c.Body;
                if (rb != null && !rb.isKinematic) rb.isKinematic = true;   // empurrado pelos fiscais: não "briga" com a física
                Vector3 np = c.transform.position + right * (lat >= 0f ? 1f : -1f) * MarshalPushSpeed * dt;
                if (rb != null) rb.MovePosition(np); else c.transform.position = np;
            }
        }

        /// <summary>Saiu da pista DESVIANDO de um incidente (carro parado/destruído ou destroços a até 60 m): motivo justificável —
        /// a IA usa a zebra/área de escape para contornar carro parado quando a pista está fechada.</summary>
        bool AvoidingIncident(State s, float now)
        {
            foreach (var i in incidents)
            {
                if (!(i.dbl || i.debris) || now > i.until) continue;
                float d = Mathf.Repeat(i.along - s.along + trackLen * 0.5f, trackLen) - trackLen * 0.5f;
                if (d > -20f && d < 60f) return true;
            }
            return false;
        }

        /// <summary>Saiu da pista ESPREMIDO: carro emparelhado (até 6 m) do lado de dentro, a menos de 3,2 m — sem o espaço de um
        /// carro, mesmo sem toque.</summary>
        bool SqueezedOff(State s)
        {
            float mySide = s.lat >= 0f ? 1f : -1f;
            foreach (var o in order)
            {
                if (o == s.e || o.car == null) continue;
                if (Mathf.Abs(o.raceDistance - s.e.raceDistance) > 6f) continue;
                var os = S(o.car);
                if (os == null) continue;
                float inward = (s.lat - os.lat) * mySide;   // > 0: o outro está entre mim e o centro da pista
                if (inward > 0f && inward < 3.2f) return true;
            }
            return false;
        }

        readonly Dictionary<CarController, Dictionary<RaceEntry, bool>> wasAhead = new Dictionary<CarController, Dictionary<RaceEntry, bool>>();

        /// <summary>O outro estava À FRENTE na leitura anterior (5 Hz) e agora está atrás = ultrapassagem.</summary>
        bool PassedRecently(State s, RaceEntry other)
        {
            return wasAhead.TryGetValue(s.e.car, out var m) && m.TryGetValue(other, out bool ahead) && ahead;
        }

        void RememberOrder(State s)
        {
            if (!wasAhead.TryGetValue(s.e.car, out var m)) { m = new Dictionary<RaceEntry, bool>(); wasAhead[s.e.car] = m; }
            foreach (var o in order)
            {
                if (o == s.e) continue;
                float gap = o.raceDistance - s.e.raceDistance;
                m[o] = gap > 0f && gap < 40f;
            }
        }

        void Demand(State s, RaceEntry to, string why, float now)
        {
            s.giveBackTo = to;
            s.giveBackWhy = why;
            s.giveBackUntil = now + GiveBackSeconds;
            Say(s.e, "DEVOLVA A POSIÇÃO a " + to.name + " (" + why + ") — " + GiveBackSeconds.ToString("0") + " s");
        }

        // ---------------- Consultas (IA e HUD) ----------------
        public static RaceFlag FlagOf(CarController c)
        {
            if (Instance == null || c == null) return RaceFlag.Green;
            var s = Instance.S(c);
            return s != null ? s.flag : RaceFlag.Green;
        }

        /// <summary>Carro a quem este carro tem que DEVOLVER a posição agora (null = nenhum).</summary>
        public static CarController GiveBackTo(CarController c)
        {
            if (Instance == null || c == null) return null;
            var s = Instance.S(c);
            return s != null && s.giveBackTo != null ? s.giveBackTo.car : null;
        }

        /// <summary>Fator de velocidade pedido pela bandeira (a IA multiplica o alvo dela).</summary>
        public static float SpeedFactor(CarController c)
        {
            if (Instance == null || c == null) return 1f;
            var s = Instance.S(c);
            if (s == null) return 1f;
            switch (s.flag)
            {
                case RaceFlag.VSC: return VscSpeedFactor;
                case RaceFlag.SafetyCar: return SafetyCar.Unlapping(c) ? 0.9f : SafetyCarCatchUpFactor; // desdobrando anda mais para passar a fila
                case RaceFlag.DoubleYellow: return DoubleYellowFactor;
                case RaceFlag.Yellow: return YellowFactor;
                case RaceFlag.Blue: return 0.94f;            // tira o pé para quem vai dar volta passar
                case RaceFlag.BlackOrange: return 0.8f;      // carro inseguro indo ao box
            }
            return s.giveBackTo != null ? 0.85f : 1f;       // devolvendo posição
        }

        /// <summary>Neutralização (SC ou VSC) valendo para este carro.</summary>
        public static bool Neutralized(CarController c)
        {
            if (Instance == null || c == null) return false;
            var s = Instance.S(c);
            return s != null && (s.flag == RaceFlag.VSC || s.flag == RaceFlag.SafetyCar);
        }

        /// <summary>Carro com problema ÓBVIO, que pode ser passado sob amarela/VSC/SC: destruído, fora da corrida, no box ou quase
        /// parado (abaixo de 40 km/h; com o SC na pista, abaixo de 18 km/h — a fila anda devagar). Antes era "abaixo de 60% da MINHA
        /// velocidade": parado atrás do destroço (0 contra 0) ninguém podia contorná-lo, e na freada da VSC quem reduzia antes virava
        /// "problema".</summary>
        public static bool ObviousProblem(CarController t)
        {
            if (t == null) return false;
            if (t.wrecked || PitLaneSystem.IsInPitFlow(t)) return true;
            var e = Instance != null && Instance.race != null ? Instance.race.GetEntry(t) : null;
            if (e != null && e.retired) return true;
            return t.SpeedKmh < (SafetyCar.Deployed ? 18f : 40f);
        }

        /// <summary>Pode atacar este carro? Sem ultrapassagem sob amarela/VSC nem sobre quem tem que ser devolvido.</summary>
        public static bool MayOvertake(CarController c, CarController target)
        {
            if (Instance == null || c == null) return true;
            if (SafetyCar.Unlapping(c)) return true;          // desdobramento: passa a fila e o SC
            if (SafetyCar.Is(target)) return false;            // ninguém mais passa o safety car
            var s = Instance.S(c);
            if (s == null) return true;
            // O da frente me deve a posição: ele abre e eu passo (mesmo sob bandeira) — sem isso os dois andavam juntos até a punição.
            var ts = target != null ? Instance.S(target) : null;
            if (ts != null && ts.giveBackTo != null && ts.giveBackTo.car == c) return true;
            if (s.flag == RaceFlag.Yellow || s.flag == RaceFlag.DoubleYellow || s.flag == RaceFlag.VSC || s.flag == RaceFlag.SafetyCar)
                return ObviousProblem(target); // só carro com problema óbvio
            if (s.giveBackTo != null && target != null && s.giveBackTo.car == target) return false;
            return true;
        }

        public static bool BlueFlag(CarController c) => FlagOf(c) == RaceFlag.Blue;

        /// <summary>Devolução pendente (HUD): nome e segundos restantes.</summary>
        public static bool GiveBack(CarController c, out string name, out float secondsLeft)
        {
            name = null; secondsLeft = 0f;
            if (Instance == null || c == null) return false;
            var s = Instance.S(c);
            if (s == null || s.giveBackTo == null) return false;
            name = s.giveBackTo.name;
            secondsLeft = Mathf.Max(0f, s.giveBackUntil - Time.time);
            return true;
        }

        /// <summary>Texto da bandeira VERDE que acabou de aparecer para este carro (vazio = nenhuma).</summary>
        public static string GreenText(CarController c)
        {
            if (Instance == null || c == null) return "";
            var s = Instance.S(c);
            return s != null && Time.time < s.greenUntil ? s.greenText : "";
        }

        /// <summary>Bandeiras de PERIGO (neutralização/amarela): o fim delas é a bandeira verde.</summary>
        public static bool IsHazard(RaceFlag f) =>
            f == RaceFlag.Yellow || f == RaceFlag.DoubleYellow || f == RaceFlag.Debris || f == RaceFlag.VSC || f == RaceFlag.SafetyCar;

        /// <summary>Ícone da bandeira no HUD (UI.FlagIcons).</summary>
        public static string IconKey(RaceFlag f)
        {
            switch (f)
            {
                case RaceFlag.Yellow: return "yellow";
                case RaceFlag.DoubleYellow: return "yellow2";
                case RaceFlag.Blue: return "blue";
                case RaceFlag.White: return "white";
                case RaceFlag.Debris: return "debris";
                case RaceFlag.BlackOrange: return "bo";
                case RaceFlag.BlackWhite: return "bw";
                case RaceFlag.Black: return "black";
                case RaceFlag.VSC: return "vsc";
                case RaceFlag.SafetyCar: return "sc";
            }
            return "green";
        }

        public static string FlagText(RaceFlag f)
        {
            switch (f)
            {
                case RaceFlag.Yellow: return "BANDEIRA AMARELA — reduza, sem ultrapassar";
                case RaceFlag.DoubleYellow: return "AMARELA DUPLA — carro na pista, pronto para parar";
                case RaceFlag.Blue: return "BANDEIRA AZUL — deixe passar quem vai te dar volta";
                case RaceFlag.White: return "BANDEIRA BRANCA — carro lento à frente";
                case RaceFlag.Debris: return "AMARELA E VERMELHA — destroços na pista";
                case RaceFlag.BlackOrange: return "PRETA E LARANJA — carro danificado: BOX";
                case RaceFlag.BlackWhite: return "PRETA E BRANCA — advertência";
                case RaceFlag.Black: return "BANDEIRA PRETA — fora da corrida";
                case RaceFlag.VSC: return "VSC — safety car virtual: volta ~40% mais lenta, sem ultrapassar";
                case RaceFlag.SafetyCar: return SafetyCar.StatusText();
            }
            return "";
        }

        public static Color FlagColor(RaceFlag f)
        {
            switch (f)
            {
                case RaceFlag.Yellow: case RaceFlag.DoubleYellow: case RaceFlag.VSC: case RaceFlag.SafetyCar: return new Color(1f, 0.85f, 0.1f);
                case RaceFlag.Blue: return new Color(0.2f, 0.5f, 1f);
                case RaceFlag.White: return Color.white;
                case RaceFlag.Debris: return new Color(1f, 0.45f, 0.2f);
                case RaceFlag.BlackOrange: return new Color(1f, 0.5f, 0.05f);
                case RaceFlag.BlackWhite: return new Color(0.85f, 0.85f, 0.85f);
                case RaceFlag.Black: return new Color(0.1f, 0.1f, 0.1f);
            }
            return new Color(0.25f, 0.85f, 0.35f);
        }
    }
}
