using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Zonas de dano do carro (silhueta do HUD, física e peças que se soltam).</summary>
    public enum DamageZone { FrontWing = 0, RearWing = 1, Left = 2, Right = 3, WheelFL = 4, WheelFR = 5, WheelRL = 6, WheelRR = 7 }

    /// <summary>
    /// Dano por ZONA calibrado por desaceleração estimada do impacto (g), com base em acidentes reais:
    /// • Grosjean, Bahrein 2020: 192 km/h a 29° na barreira (≈93 km/h perpendiculares) = 67 g (FIA);
    ///   carro partido ao meio — perda total.
    /// • Verstappen, Silverstone 2021: 51 g; carro "completamente destruído" (Red Bull).
    /// Estimativa (projeto): deformação de ~0,5 m → g ≈ v⊥² / 9,81 (v⊥ em m/s). Com isso os 93 km/h
    /// perpendiculares do Grosjean dão ~68 g (bate com os 67 g medidos). Carro contra carro: os dois
    /// deformam → metade da desaceleração.
    /// v81c (pesquisa 30/10, Claude outputs/PESQUISA_PERDA_TOTAL.md): limites POR ZONA pela energia que cada estrutura de
    /// impacto é homologada para absorver (LimitsFor): bico 22,9 g · lateral 10,2 g · traseira 12,3 g → acima disso PERDA
    /// TOTAL; suspensão (roda no muro) quebra a 1,55 g (estimativa) → abandono; chão: só a aterrissagem (componente vertical).
    /// Cores da silhueta pelo nível acumulado da zona (verde → amarelo → laranja → vermelho → preto = peça perdida).
    /// A parada conserta só o bico/asa dianteira e troca os pneus; lateral, suspensão e asa traseira continuam como estão.
    /// Peça arrancada vira destroço físico na pista.
    /// </summary>
    public class CarDamage : MonoBehaviour
    {
        public const int ZoneCount = 8;
        // PERDA TOTAL a 50 g contra MURO/BARREIRA. Não existe limite oficial da FIA para perda total; os casos reais de batida em
        // barreira que destruíram o carro: Verstappen, Silverstone 2021, 51 g; Grosjean, Bahrein 2020, 67 g — 50 g fica logo
        // abaixo do menor. (Os 45 g de Hamilton em Spa 2022 foram numa ATERRISSAGEM, não em muro — não servem para esta regra.)
        // Único limite oficial: o registrador de acidentes da FIA acende o alerta médico acima de 15–18 g. O "g" aqui é v²/9,81 da
        // velocidade perpendicular (= desaceleração com ~0,5 m de estrutura amassando).
        public const float GYellow = 5f, GOrange = 15f, GRed = 30f, GDetach = 40f, GTotal = 50f;
        public static readonly string[] ZoneNames =
            { "BICO / ASA DIANTEIRA", "ASA TRASEIRA", "LATERAL ESQUERDA", "LATERAL DIREITA", "RODA DIANT. ESQ.", "RODA DIANT. DIR.", "RODA TRAS. ESQ.", "RODA TRAS. DIR." };

        /// <summary>Peça arrancada (carro, zona, posição) — vira destroço (bandeira amarela e vermelha).</summary>
        public static event Action<CarController, int, Vector3> PartDetached;
        /// <summary>Perda total (bandeira preta, abandono).</summary>
        public static event Action<CarController> CarTotaled;
        /// <summary>Impacto registrado (carro, índice de energia em "g" = v⊥²/9,81, zona atingida, foi muro/objeto fixo).</summary>
        public static event Action<CarController, float, int, bool> Impact;
        /// <summary>Conserto no box (o replay volta a mostrar as peças a partir daí).</summary>
        public static event Action<CarController> Repaired;
        /// <summary>Batida entre carros: (quem bateu com a FRENTE, quem levou, g estimado).</summary>
        public static event Action<CarController, CarController, float> CarHitCar;
        /// <summary>PNEU atingido (roda, g do impacto, tipo): 0 = borracha/carroceria lisa (roda, lateral ou traseira de outro
        /// carro — não corta), 1 = ASA DIANTEIRA de outro carro (placa lateral cortante), 2 = muro, 3 = destroço solto.
        /// O PitLaneSystem decide o furo.</summary>
        public static event Action<CarController, int, float, int> TyreHit;
        public const int HitRubber = 0, HitSharp = 1, HitWall = 2, HitDebris = 3;

        readonly float[] level = new float[ZoneCount];
        readonly bool[] off = new bool[ZoneCount];
        readonly float[] baseRadius = new float[4];
        readonly List<Renderer> frontParts = new List<Renderer>();
        readonly List<Renderer> rearParts = new List<Renderer>();
        readonly List<Renderer> hidden = new List<Renderer>();
        /// <summary>Peças escondidas por zona (replay: o carro aparece inteiro até a hora da batida).</summary>
        readonly List<Renderer>[] zoneHidden = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>(), new List<Renderer>(),
                                                 new List<Renderer>(), new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        public List<Renderer> ZoneParts(int zone) => zoneHidden[zone];

        /// <summary>Replay: mostra/esconde as peças de uma zona como estavam no instante reproduzido.</summary>
        public void ReplayShowZone(int zone, bool detached)
        {
            foreach (var r in zoneHidden[zone]) if (r != null && r.enabled == detached) r.enabled = !detached;
        }

        /// <summary>Fim do replay: volta ao dano real de agora.</summary>
        public void ReplayRestoreLive()
        {
            for (int z = 0; z < ZoneCount; z++) ReplayShowZone(z, off[z]);
        }
        CarController car;
        float length = 4.8f;
        float width = 1.9f;
        static int debrisAlive;
        const int MaxDebris = 40;

        public bool Totaled { get; private set; }
        public float LastImpactG { get; private set; }
        public float Level(int zone) => off[zone] ? 1f : level[zone];
        public bool Detached(int zone) => off[zone];
        public float FrontLevel => Level((int)DamageZone.FrontWing);
        public float RearLevel => Level((int)DamageZone.RearWing);
        public float LeftLevel => Level((int)DamageZone.Left);
        public float RightLevel => Level((int)DamageZone.Right);

        /// <summary>Bandeira preta e laranja = dano que a parada CONSERTA e que é perigoso: só o bico/asa dianteira (troca na
        /// parada, ~9 s). Lateral, suspensão e asa traseira não têm conserto numa parada (pesquisa 30/10): lateral segue com o
        /// carro mais lento (e o radiador amassado pode ferver o motor); suspensão/asa traseira destruídas = abandono na hora.</summary>
        public bool NeedsPit
        {
            get
            {
                if (Totaled) return false;
                return off[(int)DamageZone.FrontWing] || level[(int)DamageZone.FrontWing] >= 0.75f;
            }
        }

        /// <summary>Motivo do abandono por dano (vazio = perda total por impacto).</summary>
        public string RetireReason { get; private set; } = "";

        /// <summary>Dano que tira o carro da corrida (FIA 2025, art. 26.10: carro com dano perigoso para na hora): roda solta
        /// ou suspensão destruída (≥ 75%), asa traseira perdida ou destruída. Nenhuma das duas se troca numa parada.</summary>
        string RetireDamageReason()
        {
            for (int w = 0; w < 4; w++)
            {
                int z = (int)DamageZone.WheelFL + w;
                if (off[z]) return "roda solta — suspensão quebrada";
                if (level[z] >= 0.75f) return "suspensão destruída (" + ZoneNames[z] + ")";
            }
            int rw = (int)DamageZone.RearWing;
            if (off[rw]) return "asa traseira perdida (não se troca na parada)";
            if (level[rw] >= 0.75f) return "asa traseira destruída (não se troca na parada)";
            return "";
        }

        public int DetachedCount { get { int n = 0; foreach (var o in off) if (o) n++; return n; } }

        public float MaxLevel { get { float m = 0f; for (int i = 0; i < ZoneCount; i++) m = Mathf.Max(m, Level(i)); return m; } }

        /// <summary>Cor da silhueta por nível: verde, amarelo, laranja, vermelho e PRETO = peça perdida.</summary>
        public static Color ColorFor(float lvl, bool detached)
        {
            if (detached || lvl >= 0.999f) return new Color(0.02f, 0.02f, 0.02f, 1f);
            if (lvl < 0.10f) return new Color(0.25f, 0.85f, 0.35f);
            if (lvl < 0.30f) return new Color(1f, 0.85f, 0.15f);
            if (lvl < 0.55f) return new Color(1f, 0.55f, 0.1f);
            return new Color(1f, 0.15f, 0.12f);
        }

        public static CarDamage Attach(CarController car, Transform bodyRoot, Vector3 bodySize)
        {
            var d = car.gameObject.AddComponent<CarDamage>();
            d.car = car;
            d.length = Mathf.Max(2f, bodySize.z);
            d.width = Mathf.Max(1f, bodySize.x);
            for (int i = 0; i < 4; i++) d.baseRadius[i] = car.wheels[i] != null ? car.wheels[i].radius : 0.34f;
            try { d.ClassifyParts(bodyRoot, bodySize); }
            catch (Exception ex) { Debug.LogWarning("[Damage] peças não classificadas: " + ex.Message); }
            return d;
        }

        /// <summary>Peças do bico/asa dianteira e da asa traseira: renderers pequenos nas pontas do carro
        /// (a carroceria inteira, que atravessa o carro, nunca entra).</summary>
        void ClassifyParts(Transform bodyRoot, Vector3 bodySize)
        {
            float half = length * 0.5f;
            float groundY = float.MaxValue, topY = float.MinValue;
            var rs = bodyRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (!r.enabled) continue;
                var b = LocalBounds(r);
                groundY = Mathf.Min(groundY, b.min.y); topY = Mathf.Max(topY, b.max.y);
            }
            float h = Mathf.Max(0.3f, topY - groundY);
            foreach (var r in rs)
            {
                if (!r.enabled || r.GetComponent<MeshFilter>() == null) continue;
                var b = LocalBounds(r);
                if (b.size.z > length * 0.38f) continue; // carroceria/assoalho inteiro
                if (b.center.z > half * 0.62f) frontParts.Add(r);
                else if (b.center.z < -half * 0.62f && b.center.y > groundY + h * 0.42f) rearParts.Add(r);
            }
        }

        Bounds LocalBounds(Renderer r)
        {
            var w = r.bounds;
            var c = transform.InverseTransformPoint(w.center);
            var e = w.extents;
            // AABB do mundo é rotacionada com o carro; no spawn o carro está alinhado ao grid, basta.
            var le = transform.InverseTransformVector(e);
            return new Bounds(c, new Vector3(Mathf.Abs(le.x), Mathf.Abs(le.y), Mathf.Abs(le.z)) * 2f);
        }

        /// <summary>LIMITES POR ZONA (pesquisa 30/10, Claude outputs/PESQUISA_PERDA_TOTAL.md), em "g equivalente"
        /// (g = v⊥²/9,81 da velocidade perpendicular equivalente a muro rígido — reproduz os 67 g de Grosjean com os 93 km/h
        /// perpendiculares dele; carro×carro = metade; destroço pela razão de massas). Cada estrutura de impacto do F1 é
        /// HOMOLOGADA pela FIA para absorver sozinha, SEM dano na célula de sobrevivência:
        ///   bico/frontal: 780 kg a 15 m/s (88 kJ) → 22,9 g · lateral: 780 kg a 10 m/s (39 kJ) → 10,2 g ·
        ///   traseira: 780 kg a 11 m/s (47 kJ, dano só atrás do eixo) → 12,3 g.
        /// Acima disso a energia chega ao chassi/câmbio → PERDA TOTAL. Casos reais (todos acima do limite do bico): Schumacher,
        /// Jeddah 2022, 33 g (carro partido); Alonso, Austrália 2016, 45 g; Verstappen, Silverstone 2021, 51 g; Grosjean,
        /// Bahrein 2020, 67 g; Kubica, Canadá 2007, ~75 g.
        /// SUSPENSÃO (sem teste público da FIA — ESTIMATIVA): a roda batendo no muro quebra a ~14 km/h perpendiculares (1,55 g);
        /// Button (Canadá 2005) só raspou o muro e quebrou a dianteira direita. Roda com roda (borracha dos dois lados) aguenta
        /// 4× mais. Os cabos (2 × 6 kJ, 70 kN) só seguram a roda DEPOIS que a suspensão já quebrou.
        /// light = abaixo disso nada; detach = peça perdida (bico: troca na parada; asa traseira/roda: abandono);
        /// total = perda total (−1 = a zona não tem).</summary>
        struct Limits { public float light, detach, total; }

        static Limits LimitsFor(int zone, bool wheelRubber)
        {
            switch (zone)
            {
                case (int)DamageZone.FrontWing: return new Limits { light = 1.6f, detach = 10.2f, total = 22.9f };
                // Traseira: quem absorve é a estrutura de impacto baixa atrás do câmbio; a ASA (presa em cima dela) só se perde em
                // batida forte (9 g ≈ 75% da capacidade). Estimativa — com 6,2 g toques por trás no pelotão arrancavam a asa.
                case (int)DamageZone.RearWing: return new Limits { light = 1.0f, detach = 9.0f, total = 12.3f };
                case (int)DamageZone.Left:
                case (int)DamageZone.Right: return new Limits { light = 1.0f, detach = -1f, total = 10.2f };
                default:
                    return wheelRubber ? new Limits { light = 1.5f, detach = 6.2f, total = -1f }
                                       : new Limits { light = 0.37f, detach = 1.55f, total = -1f };
            }
        }

        static readonly Limits CarCarLimits = new Limits { light = GYellow, detach = GDetach, total = GTotal };

        static readonly string[] TotalWhy =
        {
            "PERDA TOTAL — o bico não absorveu a batida (acima de 88 kJ) e o chassi foi atingido",
            "PERDA TOTAL — a estrutura traseira não absorveu a batida (acima de 47 kJ): câmbio e suspensão traseira destruídos",
            "PERDA TOTAL — a lateral não absorveu a batida (acima de 39 kJ) e o chassi foi atingido",
            "PERDA TOTAL — a lateral não absorveu a batida (acima de 39 kJ) e o chassi foi atingido",
        };

        float lastLandingT = -9f;

        /// <summary>ATERRISSAGEM (o carro decolou e caiu, ou o assoalho bateu no chão): Hamilton, Spa 2022 — 45 g VERTICAIS no
        /// registrador; o arrefecimento vazou e a equipe mandou abandonar. Acima de 5 m/s de queda (~1,3 m de altura) amassa
        /// suspensões e radiadores (laterais); ~7,5 m/s quebra a suspensão (abandono). Valores de projeto calibrados no caso
        /// Hamilton (sem dado público de resistência vertical).</summary>
        void Landing(float vy, Vector3 at)
        {
            if (Time.time - lastLandingT < 0.6f) return;   // um pouso = um evento (vários contatos no mesmo quique)
            lastLandingT = Time.time;
            float k = Mathf.InverseLerp(5f, 8f, vy);
            Debug.Log("[DANO] aterrissagem " + car.name + " " + vy.ToString("0.0") + " m/s");
            for (int w = 0; w < 4; w++)
            {
                int z = (int)DamageZone.WheelFL + w;
                if (!off[z]) level[z] = Mathf.Clamp01(level[z] + 0.25f + 0.5f * k);
            }
            level[(int)DamageZone.Left] = Mathf.Min(0.95f, level[(int)DamageZone.Left] + 0.2f + 0.4f * k);
            level[(int)DamageZone.Right] = Mathf.Min(0.95f, level[(int)DamageZone.Right] + 0.2f + 0.4f * k);
            string why = RetireDamageReason();
            if (why.Length > 0) { RetireReason = why + " (aterrissagem)"; Total(-1, at); }
        }

        /// <summary>Regra da corrida "Perda total" (RaceSettings.totalLoss, definida no RaceBootstrap). Desligada: nenhuma
        /// batida tira o carro da corrida — o dano aparece na silhueta e no carro, mas sem abandono e sem perder roda.</summary>
        public static bool AllowTotalLoss = true;

        /// <summary>Chamado pelo CarController em cada colisão.</summary>
        public void OnImpact(Collision col)
        {
            if (Totaled || car == null || col.contactCount == 0) return;
            if (car.IsRemote) return;   // online: o dano do carro de outro aparelho é calculado lá
            if (car.pitControlled) return; // manobra no box não danifica
            var contact = col.GetContact(0);
            // CHÃO NÃO É MURO: asfalto, zebra, grama e pit (TrackSurface) e contato vindo de baixo (normal para cima)
            // não contam como batida. Antes, num toque roda com roda a 142 km/h o carro pegou a quina da malha da pista
            // com a normal quase na direção do movimento: a velocidade inteira virou "impacto em muro" (162 g) e deu
            // perda total no meio da curva, sem muro nenhum (replay de 30/10).
            if (col.rigidbody == null && (contact.normal.y > 0.6f || col.collider.GetComponentInParent<RaceX.Track.TrackSurface>() != null))
            {
                // No chão só conta a componente VERTICAL (aterrissagem / assoalho batendo) — nunca vira "muro".
                float vy = Mathf.Abs(Vector3.Dot(col.relativeVelocity, Vector3.up));
                if (contact.normal.y > 0.6f && vy > 5f) Landing(vy, contact.point);
                return;
            }
            float vn = Mathf.Abs(Vector3.Dot(col.relativeVelocity, contact.normal)); // m/s perpendicular
            var otherCar = col.rigidbody != null ? col.rigidbody.GetComponent<CarController>() : null;
            // SOB SAFETY CAR toque entre carros da fila não danifica (rede de segurança da fila: a ~150 km/h atrás do SC
            // nenhum toque vira perda total). Muro, chão e destroços continuam valendo. Só existe com o SC acionado.
            if (otherCar != null && RaceX.Core.SafetyCar.Deployed) return;
            float g = vn * vn / 9.81f;
            if (otherCar != null) g *= 0.5f; // carro x carro: os dois deformam
            // Destroço (peça de 4–12 kg): quem sente é a PEÇA, não o carro — antes bater num destroço a 200 km/h
            // contava ~150 g e arrancava peças. Escala pela razão de massas (6 kg / 800 kg ≈ 3%).
            else if (col.rigidbody != null && car.Body != null) g *= Mathf.Clamp01(col.rigidbody.mass / car.Body.mass * 4f);
            Apply(contact.point, g, col.rigidbody == null, otherCar);
        }

        /// <summary>Teste automático (autotest crashtest=1): impacto sintético no ponto local, pelo MESMO caminho.</summary>
        public void TestImpact(Vector3 localPoint, float normalKmh, bool wall)
        {
            float vn = normalKmh / 3.6f;
            float g = vn * vn / 9.81f * (wall ? 1f : 0.5f);
            Debug.Log("[DANO] teste " + car.name + " ponto=" + localPoint + " " + normalKmh.ToString("0") + " km/h -> " + g.ToString("0") + " g");
            Apply(transform.TransformPoint(localPoint), g, wall, null);
        }

        void Apply(Vector3 worldPoint, float g, bool wall, CarController otherCar)
        {
            if (Totaled) return;
            LastImpactG = g;
            int zone = ZoneOf(worldPoint);
            if (g >= 3f) Impact?.Invoke(car, g, zone, wall);
            // PNEU: avisado ANTES do corte de 5 g — um toque leve da asa de outro carro no flanco já corta o pneu (Spa 2014:
            // a placa lateral da asa de Rosberg furou o traseiro de Hamilton; México 2019: Bottas tocou o traseiro de
            // Verstappen e o furo apareceu meia volta depois).
            if (zone >= (int)DamageZone.WheelFL && TyreHit != null)
            {
                // Só a ASA DIANTEIRA (placa lateral) do outro carro corta — nos casos reais é ela (Spa 2014, México 2019).
                // Roda, lateral (sidepod liso) e traseira de outro carro só empurram. Teste 30/10: contando qualquer parte
                // da carroceria como cortante, o pelotão da largada teve 22 furos no 1º minuto.
                int kind = wall ? HitWall
                    : otherCar == null ? HitDebris
                    : otherCar.damage != null && otherCar.damage.ZoneOf(worldPoint) == (int)DamageZone.FrontWing ? HitSharp
                    : HitRubber;
                TyreHit(car, zone - (int)DamageZone.WheelFL, g, kind);
            }
            // Roda atingida por OUTRO CARRO (qualquer parte dele): o pneu atingido é flexível — quem quebra é a asa de quem bate.
            // Limite de muro rígido só contra muro/destroço. (Teste de largada 30/10: tratando bico/lateral do outro carro como
            // muro, o pelotão perdeu rodas com toques de 1–3 g e 4 carros abandonaram no primeiro segundo.)
            bool rubber = otherCar != null && zone >= (int)DamageZone.WheelFL;
            // CARRO × CARRO: limites antigos (dano a partir de 5 g, peça perdida em 40 g, perda total em 50 g). A regra por energia
            // (pesquisa) vale para MURO, destroço e chão. Motivo (teste de largada 30/10): o contato entre carros é calculado de
            // forma simplificada e a IA bate muito mais que pilotos reais (306 contatos em 50 s) — com a regra real houve 5 perdas
            // totais no 1º minuto. Volta para a regra real quando a IA parar de bater por trás no pelotão.
            var lim = otherCar != null ? CarCarLimits : LimitsFor(zone, rubber);
            if (g < lim.light) return;

            if (otherCar != null && zone == (int)DamageZone.FrontWing) CarHitCar?.Invoke(car, otherCar, g);

            // Energia acima do que a estrutura daquela zona absorve → PERDA TOTAL (muro ou carro: a regra é a energia).
            if (lim.total > 0f && g >= lim.total)
            {
                RetireReason = zone < TotalWhy.Length ? TotalWhy[zone] : "";
                // Diagnóstico (v99): contra o QUE bateu, a que velocidade e o que a IA fazia — para achar a causa das perdas totais.
                var ai = car.Ai;
                // vImpacto: a velocidade perpendicular do choque (g = v⊥²/9,81; carro×carro conta metade); "depois" é a velocidade já
                // depois do choque (o registro é feito com a colisão resolvida).
                float vImp = Mathf.Sqrt(g * 9.81f * (otherCar != null ? 2f : 1f)) * 3.6f;
                string ptCause = "PT-CAUSA " + car.name + " contra=" + (otherCar != null ? otherCar.name : wall ? "MURO" : "OBJETO")
                             + " vImpacto=" + vImp.ToString("0") + "km/h depois=" + car.SpeedKmh.ToString("0") + "km/h g=" + g.ToString("0")
                             + " zona=" + zone + (ai != null ? " ia=" + ai.Diag() : "");
                if (RaceX.Core.AutoTest.Active) RaceX.Core.AutoTest.Log(ptCause); else Debug.Log("[DANO] " + ptCause);
                Total(zone, worldPoint);
                return;
            }
            float top = lim.detach > 0f ? lim.detach : lim.total;
            float gain = g >= top ? 1f : Mathf.Lerp(0.10f, 0.85f, Mathf.InverseLerp(lim.light, top, g));
            if (gain >= 1f || level[zone] + gain >= 1f)
            {
                // Perda total desligada: roda não sai (sem roda o carro não anda — seria abandono do mesmo jeito).
                if (lim.detach > 0f && (AllowTotalLoss || zone < (int)DamageZone.WheelFL)) Detach(zone, worldPoint);
                else level[zone] = 0.95f;   // lateral: sem peça para soltar — vermelho (radiador amassado: o motor esquenta)
            }
            else level[zone] = Mathf.Clamp01(level[zone] + gain);
            // Dano sem conserto e perigoso (roda/suspensão, asa traseira): abandono na hora (antes só com 2 rodas soltas, e
            // o carro sem roda ou sem asa traseira ia ao box e voltava inteiro).
            string why = RetireDamageReason();
            if (why.Length > 0) { RetireReason = why; Total(-1, worldPoint); }
        }

        /// <summary>Raio original da roda (antes de murchar) — efeito visual do pneu furado.</summary>
        public float BaseRadius(int wheel) => wheel >= 0 && wheel < 4 ? baseRadius[wheel] : 0.34f;

        /// <summary>Dano gradual numa zona (pneu vazio batendo na lateral). Não arranca peça: para em 0,74.</summary>
        public void AddWear(int zone, float amount)
        {
            if (zone < 0 || zone >= ZoneCount || off[zone] || Totaled) return;
            level[zone] = Mathf.Min(0.74f, level[zone] + amount);
        }

        /// <summary>Pneu vazio: o carro arria naquela roda (até −25% do raio = rodando sobre o flanco amassado e o aro).
        /// Pneu de 18" da F1: 720 mm de diâmetro e aro de 457 mm → flanco de ~130 mm; a queda usada fica dentro disso.</summary>
        public void SetDeflation(int wheel, float drop01)
        {
            int z = (int)DamageZone.WheelFL + wheel;
            if (wheel < 0 || wheel > 3 || off[z] || car.wheels[wheel] == null) return;
            car.wheels[wheel].radius = baseRadius[wheel] * (1f - 0.25f * Mathf.Clamp01(drop01));
        }

        public int ZoneOf(Vector3 worldPoint)
        {
            var p = transform.InverseTransformPoint(worldPoint);
            for (int i = 0; i < 4; i++)
            {
                var wc = car.wheels[i];
                if (wc == null) continue;
                var wp = transform.InverseTransformPoint(wc.transform.position);
                float dx = p.x - wp.x, dz = p.z - wp.z;
                if (dx * dx + dz * dz < (wc.radius * 1.3f) * (wc.radius * 1.3f)) return (int)DamageZone.WheelFL + i;
            }
            float half = length * 0.5f;
            if (p.z > half * 0.5f) return (int)DamageZone.FrontWing;
            if (p.z < -half * 0.5f) return (int)DamageZone.RearWing;
            return p.x < 0f ? (int)DamageZone.Left : (int)DamageZone.Right;
        }

        void Detach(int zone, Vector3 at)
        {
            if (off[zone]) return;
            off[zone] = true;
            level[zone] = 1f;
            int hiddenBefore = hidden.Count;
            try
            {
                if (zone == (int)DamageZone.FrontWing) SpawnDebris(frontParts, false, true);
                else if (zone == (int)DamageZone.RearWing) SpawnDebris(rearParts, false, false);
                else if (zone >= (int)DamageZone.WheelFL)
                {
                    int w = zone - (int)DamageZone.WheelFL;
                    var pivot = car.wheelMeshes[w];
                    if (pivot != null)
                    {
                        var list = new List<Renderer>(pivot.GetComponentsInChildren<Renderer>(false));
                        SpawnDebris(list, true);
                    }
                    // Sem a roda o cubo arrasta no chão: raio mínimo, quase sem aderência (WheelGripMul).
                    if (car.wheels[w] != null) car.wheels[w].radius = baseRadius[w] * 0.35f;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Damage] destroço ignorado: " + ex.Message); }
            for (int i = hiddenBefore; i < hidden.Count; i++) zoneHidden[zone].Add(hidden[i]);
            Debug.Log("[DANO] " + car.name + " perdeu " + ZoneNames[zone]);
            PartDetached?.Invoke(car, zone, at);
        }

        /// <summary>Copia as malhas da peça como destroço físico (some em 30 s) e esconde a original.</summary>
        void SpawnDebris(List<Renderer> parts, bool wheel = false, bool front = true)
        {
            var v = car.Body != null ? car.Velocity : Vector3.zero;
            GameObject group = null;
            Bounds gb = default;
            bool first = true;
            foreach (var r in parts)
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
                if (debrisAlive >= MaxDebris) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (group == null)
                {
                    group = new GameObject(wheel ? "Destroco_Roda" : "Destroco");
                    group.transform.position = r.bounds.center;
                }
                var piece = new GameObject(r.name);
                piece.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                piece.transform.localScale = r.transform.lossyScale;
                piece.transform.SetParent(group.transform, true);
                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = piece.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (first) { gb = r.bounds; first = false; } else gb.Encapsulate(r.bounds);
            }
            // Modelo com o bico/asa grudado na carroceria (nenhuma peça separada): placa de carbono do tamanho
            // da asa — antes não saía destroço nenhum e a peça "soltava" só na silhueta.
            if (group == null && !wheel && debrisAlive < MaxDebris)
            {
                group = new GameObject("Destroco_Asa");
                float z = (front ? 0.47f : -0.45f) * length;
                group.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(0f, front ? 0.2f : 0.8f, z)), transform.rotation);
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var pc = plate.GetComponent<Collider>();
                if (pc != null) Destroy(pc);
                plate.transform.SetParent(group.transform, false);
                plate.transform.localScale = new Vector3(width * (front ? 0.95f : 0.6f), 0.05f, front ? 0.42f : 0.3f);
                plate.GetComponent<Renderer>().sharedMaterial = RaceX.Core.MaterialLibrary.Get("DebrisCarbon", new Color(0.07f, 0.07f, 0.08f), 0.5f);
                gb = plate.GetComponent<Renderer>().bounds;
            }
            if (group == null) return;
            debrisAlive++;
            var rb = group.AddComponent<Rigidbody>();
            rb.mass = wheel ? 12f : 6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // carro a 300 km/h não atravessa
            if (wheel)
            {
                var sc = group.AddComponent<SphereCollider>();
                sc.center = group.transform.InverseTransformPoint(gb.center);
                sc.radius = Mathf.Max(0.15f, Mathf.Max(gb.extents.y, gb.extents.z));
            }
            else
            {
                var bc = group.AddComponent<BoxCollider>();
                bc.center = group.transform.InverseTransformPoint(gb.center);
                // Mínimo de 22 cm de altura: placa de 5 cm passava por baixo do carro e ninguém a tocava.
                bc.size = new Vector3(Mathf.Max(0.1f, gb.size.x), Mathf.Max(0.22f, gb.size.y), Mathf.Max(0.1f, gb.size.z));
            }
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v * 0.4f + UnityEngine.Random.insideUnitSphere * 3f + Vector3.up * 3f; // 40%: a 75% sumiam da tela num instante
#else
            rb.velocity = v * 0.4f + UnityEngine.Random.insideUnitSphere * 3f + Vector3.up * 3f;
#endif
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;
            group.AddComponent<CarDebris>().owner = car;
            // DESTROÇO não vira "chão" para a suspensão: a roda passava por cima, o raio da WheelCollider apoiava na peça e
            // a traseira LEVANTAVA, caindo de bico (dano "do nada" num carro sozinho — relato do usuário). A peça continua
            // batendo na carroceria (é arremessada) e furando pneu pelo caminho normal.
            foreach (var t in group.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = CarFactory.DebrisLayer;
            Physics.IgnoreLayerCollision(CarFactory.WheelLayer, CarFactory.DebrisLayer, true);
        }

        /// <summary>Zonas vizinhas destruídas junto na perda total (bico leva as rodas dianteiras etc.).</summary>
        static readonly int[][] Neighbors =
        {
            new[] { (int)DamageZone.WheelFL, (int)DamageZone.WheelFR },                       // bico
            new[] { (int)DamageZone.WheelRL, (int)DamageZone.WheelRR },                       // asa traseira
            new[] { (int)DamageZone.WheelFL, (int)DamageZone.WheelRL },                       // lado esquerdo
            new[] { (int)DamageZone.WheelFR, (int)DamageZone.WheelRR },                       // lado direito
            new[] { (int)DamageZone.FrontWing, (int)DamageZone.Left },                        // roda DE
            new[] { (int)DamageZone.FrontWing, (int)DamageZone.Right },                       // roda DD
            new[] { (int)DamageZone.RearWing, (int)DamageZone.Left },                         // roda TE
            new[] { (int)DamageZone.RearWing, (int)DamageZone.Right },                        // roda TD
        };

        /// <summary>Perda total: a zona do impacto e as vizinhas ficam DESTRUÍDAS (preto na silhueta); o resto fica
        /// como estava. Antes punha TODAS as zonas em 90% (vermelho/laranja) — batida de frente marcava a traseira
        /// e nada ficava preto. zone = -1: abandono por 2 rodas perdidas (já pretas).</summary>
        void Total(int zone, Vector3 at)
        {
            if (Totaled) return;
            if (!AllowTotalLoss)
            {
                // Regra desligada: a batida fica marcada (zona no máximo), mas o carro continua na corrida.
                if (zone >= 0) level[zone] = Mathf.Max(level[zone], 0.95f);
                RetireReason = "";
                return;
            }
            Totaled = true;
            if (zone >= 0)
            {
                Detach(zone, at);
                foreach (int n in Neighbors[zone]) Detach(n, at);
            }
            car.wrecked = true;
            Debug.Log("[DANO] PERDA TOTAL " + car.name + " (" + LastImpactG.ToString("0") + " g)");
            CarTotaled?.Invoke(car);
        }

        /// <summary>Aderência de uma roda pelo dano: roda arrancada quase não segura; dano alto = furo/suspensão torta.</summary>
        public float WheelGripMul(int wheel)
        {
            int z = (int)DamageZone.WheelFL + wheel;
            if (off[z]) return 0.05f;
            float l = level[z];
            return l >= 0.75f ? 0.5f : 1f - 0.35f * l;
        }

        /// <summary>PARADA NO BOX (corrida/treino): troca o BICO/ASA DIANTEIRA e os pneus — é o que uma parada consegue fazer.
        /// Lateral, suspensão e asa traseira continuam como estão (pesquisa 30/10: não há conserto disso numa parada).</summary>
        public void RepairAtPitStop()
        {
            if (Totaled) return;
            int fw = (int)DamageZone.FrontWing;
            level[fw] = 0f;
            if (off[fw])
            {
                off[fw] = false;
                foreach (var r in zoneHidden[fw]) if (r != null) { r.enabled = true; hidden.Remove(r); }
                zoneHidden[fw].Clear();
            }
            // pneus novos: a roda volta ao raio normal (o furo murchava a roda física)
            for (int i = 0; i < 4; i++)
                if (!off[(int)DamageZone.WheelFL + i] && car.wheels[i] != null) car.wheels[i].radius = baseRadius[i];
            Repaired?.Invoke(car);
        }

        /// <summary>Conserto COMPLETO (fora da corrida — reiniciar/garagem). Perda total não tem conserto.</summary>
        public void Repair()
        {
            if (Totaled) return;
            for (int i = 0; i < ZoneCount; i++) { level[i] = 0f; off[i] = false; }
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            foreach (var zl in zoneHidden) zl.Clear();
            Repaired?.Invoke(car);
            for (int i = 0; i < 4; i++) if (car.wheels[i] != null) car.wheels[i].radius = baseRadius[i];
        }

        internal static void DebrisGone() { debrisAlive = Mathf.Max(0, debrisAlive - 1); }
    }

    /// <summary>Destroço na pista: some em 60 s (os fiscais recolhem).</summary>
    public class CarDebris : MonoBehaviour
    {
        /// <summary>Destroços vivos (a direção de prova mostra a bandeira só onde eles estão).</summary>
        public static readonly List<CarDebris> Live = new List<CarDebris>();
        public int hint;
        /// <summary>Carro de onde a peça saiu: não a arremessa no primeiro segundo (estava encostado nela).</summary>
        public CarController owner;
        float t = 60f, born;
        Rigidbody rb;

        void Awake() { Live.Add(this); rb = GetComponent<Rigidbody>(); born = Time.time; }
        void Update() { t -= Time.deltaTime; if (t <= 0f) Destroy(gameObject); }
        void OnDestroy() { Live.Remove(this); CarDamage.DebrisGone(); }

        /// <summary>Carro que passa por cima ARREMESSA a peça (troca de quantidade de movimento com 800 kg): sai na
        /// velocidade do carro, para o lado e para cima — física real, não fica parada no lugar.</summary>
        void OnCollisionEnter(Collision c)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            var hitter = c.rigidbody != null ? c.rigidbody.GetComponent<CarController>() : null;
            if (rb == null || hitter == null || (hitter == owner && Time.time - born < 1f)) return;
#if UNITY_6000_0_OR_NEWER
            Vector3 v = c.rigidbody.linearVelocity;
#else
            Vector3 v = c.rigidbody.velocity;
#endif
            if (v.magnitude < 3f) return;
            Vector3 side = Vector3.Cross(Vector3.up, v.normalized) * (Vector3.Dot(transform.position - c.transform.position, Vector3.Cross(Vector3.up, v.normalized)) >= 0f ? 1f : -1f);
            Vector3 kick = v * 1.15f + side * v.magnitude * 0.25f + Vector3.up * Mathf.Min(8f, v.magnitude * 0.15f);
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = kick;
#else
            rb.velocity = kick;
#endif
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 15f;
        }
    }
}
