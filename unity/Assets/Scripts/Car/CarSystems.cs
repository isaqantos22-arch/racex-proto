using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>
    /// FREIOS, MOTOR e COMBUSTÍVEL — sistemas que mudam a corrida e aparecem na silhueta do HUD.
    ///
    /// Números com fonte:
    ///  • Disco de carbono F1: ~1,4 kg (Ø278 mm, 22–28 mm, Brembo pré-2022; 2022: Ø até 328 mm, +700 g por roda).
    ///    Trabalha a ~1000 °C e passa de 1200 °C só na frenagem mais severa (Brembo).
    ///  • Disco de ferro (GT): temperatura de trabalho 400–600 °C; não operar acima de 610 °C (AP Racing).
    ///  • Combustível F1: no máximo 110 kg na corrida e vazão máxima de 100 kg/h (FIA). F2: tanque FT5 de 125 L (FIA).
    ///  • 10 kg de combustível ≈ 0,3–0,4 s por volta: a massa entra no Rigidbody e o efeito sai da física.
    ///  • Cotas de motor: 4 por temporada; o 5º dá 10 posições no grid, os seguintes 5 (regra 2025, CareerMode).
    ///
    /// Valores de PROJETO, sem fonte e ajustáveis: capacidade térmica e resfriamento do disco, calibrados por
    /// simulação para picos de 700–900 °C (F1) e 490–680 °C (GT) em pista de frenagem pesada. Também o calor e a
    /// perda de potência do motor e o consumo por km fora da F1.
    ///
    /// "SÓ EM CASO EXTREMO" — definição usada aqui (nenhuma quebra é sorteada):
    ///  • Freio: disco acima de 1200 °C (carbono) ou 700 °C (ferro) por 5 s SEGUIDOS, ou disco/pastilha 100% gastos.
    ///    Na simulação isso só acontece com o duto de freio destruído (dano vermelho na roda). Duto pela metade
    ///    esquenta e perde freio, mas não quebra.
    ///  • Motor: água acima de 140 °C por 10 s SEGUIDOS, ou motor com 100% de desgaste. Na simulação só acontece com
    ///    os 2 radiadores destruídos (laterais pretas) num dia quente. Vácuo, calor e 1 lateral danificada só
    ///    esquentam e tiram potência.
    ///  • Combustível: acabar no meio da corrida. A equipe abastece para a distância com margem e liga o modo
    ///    econômico sozinha quando a conta não fecha.
    /// </summary>
    public class CarSystems : MonoBehaviour
    {
        public enum Kind { F1, F2, GT }

        // ---------------- Freios (por roda) ----------------
        public readonly float[] brakeTempC = new float[4];
        public readonly float[] brakeWear = new float[4];
        public readonly bool[] brakeFailed = new bool[4];
        public readonly float[] ductEff = { 1f, 1f, 1f, 1f };
        readonly float[] overTime = new float[4];
        readonly float[] uneven = new float[4];

        // ---------------- Motor ----------------
        public float engineTempC = 85f;
        public float engineWear;
        public bool engineFailed;
        public float radiatorEff = 1f;
        float engineOverTime;

        // ---------------- Combustível ----------------
        public float fuelKg;
        public float fuelCapacityKg;
        /// <summary>Modo econômico: 0 = normal, 1 = economia (−12% consumo, −3% potência), 2 = economia forte
        /// (−22% consumo, −7% potência; tirar o pé antes da freada). Valores de projeto.</summary>
        public int fuelSaveLevel;
        public bool FuelSaving => fuelSaveLevel > 0;
        float fuelScale = 1f;       // < 1 só quando a corrida (fora da F1 real) é maior que um tanque
        float sessionLoadKg;        // sessão por tempo: carga reabastecida a cada parada no box
        float usedKg, traveledM;

        public Kind kind;
        public bool Carbon => kind != Kind.GT;
        public float BrakeWindowLow => Carbon ? 350f : 400f;
        public float BrakeFadeFrom => Carbon ? 1000f : 610f;
        public float BrakeFailAt => Carbon ? 1200f : 700f;
        public const float EngineHotFrom = 125f, EngineFailAt = 140f;

        CarController car;
        RaceManager race;
        RaceEntry entry;
        bool timed, career, retireQueued;
        float trackLen, kgPerKm, discC, k0, k1;
        float pMaxW = 1f;           // potência máxima do motor (W), para medir o esforço real
        float tick, retireAt = -1f;
        string retireReason, retireMsg;
        // avisos por rádio (só o jogador): cada condição avisa uma vez por episódio
        bool warnDuct, warnRadiator, warnEngineLife, warnFuelLow;
        int warnedSaveLevel;
        float warnBrakeUntil, warnEngineUntil;
        float massTimer;

        public static CarSystems Of(CarController c) => c != null ? c.GetComponent<CarSystems>() : null;

        public static CarSystems Attach(CarController car, RaceManager race, RaceEntry entry)
        {
            if (car == null || race == null || race.Settings == null) return null;
            var s = car.gameObject.AddComponent<CarSystems>();
            s.Init(car, race, entry);
            return s;
        }

        void Init(CarController c, RaceManager r, RaceEntry e)
        {
            car = c; race = r; entry = e;
            var m = c.spec != null ? c.spec.model : CarModel.F1;
            kind = !CarSpec.IsFormula(m) ? Kind.GT : System.Array.IndexOf(CarSpec.F1Lineup, m) >= 0 ? Kind.F1 : Kind.F2;
            timed = r.Settings.laps >= 900;
            career = r.Settings.careerPhase >= 0 && RaceX.Career.CareerMode.data != null;
            float mass = c.spec != null ? c.spec.massKg : 800f;
            // Potência máxima pela curva de torque do próprio carro (o mesmo 0,9 do ApplyDrive)
            if (c.spec != null)
            {
                float best = 0f;
                for (float rpm = Mathf.Max(500f, c.spec.idleRpm); rpm <= c.spec.rpmLimit; rpm += 100f)
                    best = Mathf.Max(best, c.spec.EvaluateTorque(rpm) * rpm * (Mathf.PI / 30f));
                pMaxW = Mathf.Max(1000f, best * 0.9f);
            }
            if (Carbon)
            {
                // Disco de carbono ~1,4 kg: capacidade térmica ~1,3 kJ/K e τ ≈ 10 s a 300 km/h (projeto).
                discC = 1300f; k0 = 4.3f; k1 = 3.66f;
            }
            else
            {
                // Ferro: disco maior quanto mais pesado o carro (3 J/K por kg do carro; resfriamento junto) — projeto.
                float sc = mass / 1400f;
                discC = 3f * mass; k0 = 9.25f * sc; k1 = 6.2f * sc;
            }
            float air = WeatherSystem.AirTemp;
            var rng = new System.Random((c.GetInstanceID() * 7919) ^ 0x5bd1);
            for (int i = 0; i < 4; i++)
            {
                // Corrida: freios aquecidos na volta de apresentação (borda de baixo da janela). Treino/classificação:
                // saída do box com freio frio — o carbono morde menos na primeira freada (valores de projeto).
                brakeTempC[i] = timed ? air + 80f : (Carbon ? 300f : 350f);
                uneven[i] = ((float)rng.NextDouble() * 2f - 1f) * 0.08f;   // até ±8% de mordida desigual com o disco frio
            }
            engineTempC = 85f;   // água pré-aquecida na garagem antes de ligar
            if (career && e != null && e.isHuman) engineWear = Mathf.Clamp01(RaceX.Career.CareerMode.data.engineWear);

            // Combustível: capacidade e consumo de planejamento por km.
            // F1: 110 kg (FIA); ~0,33 kg/km (110 kg para ~305 km com margem — estimativa).
            // F2: 125 L × 0,745 kg/L ≈ 93 kg (FIA); 0,45 kg/km (estimativa). GT: ~120 L ≈ 89 kg; 0,38 kg/km (estimativas).
            switch (kind)
            {
                case Kind.F1: fuelCapacityKg = 110f; kgPerKm = 0.33f; break;
                case Kind.F2: fuelCapacityKg = 93f; kgPerKm = 0.45f; break;
                default: fuelCapacityKg = 89f; kgPerKm = 0.38f; break;
            }
            trackLen = r.Track != null && r.Track.Waypoints != null ? r.Track.Waypoints.Length : 4000f;
            float lapKg = trackLen / 1000f * kgPerKm;
            if (timed)
            {
                // Treino/classificação: carga para uma saída (~8 voltas); a equipe reabastece no box (permitido fora da corrida).
                sessionLoadKg = Mathf.Min(fuelCapacityKg, lapKg * 8f + 1f);
                fuelKg = sessionLoadKg;
            }
            else
            {
                // Corrida: distância + 1 volta (apresentação) + 1 kg da amostra da FIA. Sem reabastecimento.
                float need = lapKg * (r.Settings.laps + 1f) * 1.03f + 1f;
                fuelKg = Mathf.Min(fuelCapacityKg, need);
                // Regra de JOGO: corrida maior que um tanque (só possível fora das distâncias reais) consome
                // proporcionalmente menos, para ninguém ficar parado por causa da distância escolhida no menu.
                if (need > fuelCapacityKg) fuelScale = fuelCapacityKg / need;
            }
            car.SetExtraMass(fuelKg);
            tick = Random.Range(0f, 0.1f);
        }

        void Update()
        {
            if (car == null || race == null || ReplaySystem.IsPlaying) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Retirada já agendada (quebra): o carro para e sai da corrida.
            if (retireAt > 0f && Time.time >= retireAt)
            {
                retireAt = -1f;
                RaceControl.MechanicalRetire(car, retireReason, retireMsg);
            }
            tick -= dt;
            if (tick > 0f) return;
            float step = 0.1f - tick;   // 10 Hz escalonado (tick negativo = atraso do frame)
            tick = 0.1f;
            if (entry != null && (entry.finished || entry.retired)) { ApplyOutputs(); return; }
            bool running = race.State == RaceState.Racing || race.State == RaceState.Countdown;
            if (!running) { ApplyOutputs(); return; }
            float v = car.Velocity.magnitude;
            float air = WeatherSystem.AirTemp;
            float slip = car.Slipstream;   // 0..1: carro logo à frente (ar sujo)
            UpdateDamageLinks();
            UpdateBrakes(step, v, air, slip);
            UpdateEngine(step, v, air, slip);
            UpdateFuel(step, v);
            ApplyOutputs();
            if (career && entry != null && entry.isHuman) RaceX.Career.CareerMode.data.engineWear = engineWear;
            massTimer -= step;
            if (massTimer <= 0f) { massTimer = 0.5f; car.SetExtraMass(fuelKg); }
        }

        /// <summary>Dano → peças: dano na RODA amassa o duto de freio daquele canto; dano na LATERAL atinge o radiador
        /// (os radiadores ficam nos sidepods, um de cada lado). Reparo no box volta tudo ao normal.</summary>
        void UpdateDamageLinks()
        {
            var d = car.damage;
            if (d == null) return;
            for (int i = 0; i < 4; i++)
            {
                float lvl = d.Level((int)DamageZone.WheelFL + i);
                float side = (i == CarController.FL || i == CarController.RL) ? d.LeftLevel : d.RightLevel;
                float worst = Mathf.Max(lvl, side * 0.5f);
                ductEff[i] = 1f - 0.7f * Mathf.Clamp01((worst - 0.15f) / 0.75f);
            }
            float eL = 1f - 0.4f * Mathf.Clamp01((d.LeftLevel - 0.15f) / 0.6f);
            float eR = 1f - 0.4f * Mathf.Clamp01((d.RightLevel - 0.15f) / 0.6f);
            radiatorEff = 0.5f * (eL + eR);
        }

        void UpdateBrakes(float dt, float v, float air, float slip)
        {
            // Resfriamento por convecção forçada (∝ v^0,8): ar sujo atrás de outro carro tira ~25% (projeto).
            float flow = Mathf.Pow(Mathf.Max(0f, v), 0.8f) * (1f - 0.25f * slip);
            // Desgaste: energia até 100% (projeto) — corrida de frenagem pesada gasta ~metade; acima da fadiga oxida 5× mais rápido.
            float lifeJ = Carbon ? 550e6f : 600e6f;
            for (int i = 0; i < 4; i++)
            {
                var w = car.wheels[i];
                if (w == null) continue;
                // Rotação para o CALOR limitada ao que a velocidade do carro permite (rola sem patinar ×1,5): com o carro
                // PARADO no box e preso no freio, a roda avariada/furada dava rotação falsa altíssima na física e o disco ia
                // a ~1950 °C sem o carro andar — falha de freio na saída do box (autoteste 01/11).
                float omegaMax = Mathf.Abs(v) / Mathf.Max(0.2f, w.radius) * 1.5f + 2f;
                float omega = Mathf.Min(Mathf.Abs(w.rpm) * (Mathf.PI / 30f), omegaMax);
                float heatW = car.lastBrakeTorque[i] * omega;   // potência dissipada no freio (W)
                float k = k0 + k1 * ductEff[i] * flow;
                float t = brakeTempC[i];
                t += (heatW - k * (t - air)) / discC * dt;
                brakeTempC[i] = Mathf.Clamp(t, air - 5f, 2000f);
                float ox = 1f + 4f * Mathf.Clamp01((brakeTempC[i] - BrakeFadeFrom) / (BrakeFailAt - BrakeFadeFrom));
                brakeWear[i] = Mathf.Min(1f, brakeWear[i] + heatW * dt * ox / lifeJ);
                // Falha só no extremo: acima do limite por 5 s seguidos ou gasto até o fim.
                if (brakeTempC[i] > BrakeFailAt) overTime[i] += dt; else overTime[i] = 0f;
                if (!brakeFailed[i] && (overTime[i] >= 5f || brakeWear[i] >= 1f))
                {
                    brakeFailed[i] = true;
                    Fail("FREIO", "FALHA DE FREIO (" + WheelName(i) + ")" + (overTime[i] >= 5f ? " — disco superaquecido" : " — freio gasto"));
                }
            }
            if (IsHuman)
            {
                for (int i = 0; i < 4; i++)
                    if (!warnDuct && ductEff[i] < 0.75f) { warnDuct = true; Radio("Duto de freio danificado (" + WheelName(i) + ") — o freio vai esquentar"); }
                for (int i = 0; i < 4; i++)
                    if (brakeTempC[i] > BrakeFadeFrom + (BrakeFailAt - BrakeFadeFrom) * 0.5f && Time.time > warnBrakeUntil)
                    {
                        warnBrakeUntil = Time.time + 25f;
                        Radio("Freio " + WheelName(i) + " superaquecendo — frear mais cedo e sair do vácuo");
                        break;
                    }
            }
        }

        void UpdateEngine(float dt, float v, float air, float slip)
        {
            bool alive = !engineFailed && fuelKg > 0f;
            // ESFORÇO REAL = potência entregue às rodas (torque × rotação de cada roda) ÷ potência máxima. Antes era
            // acelerador × giro: atrás do safety car, em marcha baixa e giro alto, contava como esforço grande — no
            // teste de 30/10 a água ficou em 133–138 °C a corrida inteira, até sob SC, e todos perderam potência.
            float pW = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = car.wheels[i];
                if (w != null) pW += Mathf.Abs(w.motorTorque * w.rpm * (Mathf.PI / 30f));
            }
            float load = alive ? Mathf.Max(0.12f, Mathf.Clamp01(pW / pMaxW)) : 0f;
            // Água: calor ∝ esforço real; resfriamento ∝ fluxo de ar no radiador (parado ~20%), radiador danificado e ar
            // sujo (–15% no vácuo) tiram resfriamento; τ ≈ 60 s (projeto). Simulado numa volta sintética (scratchpad
            // sys/enginesim.py, 30/10): ar limpo 97 °C a 25 °C e 107 °C a 35 °C; vácuo a corrida toda a 35 °C 120 °C;
            // 1 lateral vermelha + 35 °C + vácuo 130 °C (97,6% de potência); 2 laterais vermelhas a 35 °C 139 °C (85%),
            // sem quebrar; só com as 2 laterais destruídas a 35 °C quebra (~2 min).
            float airflow = Mathf.Clamp(0.2f + 0.8f * v / 55f, 0.2f, 1.3f) * radiatorEff * (1f - 0.15f * slip);
            // PARADO NO BOX/GARAGEM: a equipe liga os ventiladores no radiador (motor em marcha lenta) — antes o carro com o
            // radiador batido esquentava parado na garagem e QUEBRAVA o motor (autoteste 01/11, rodadas 10 e 13). Valores de
            // projeto: esforço de marcha lenta 0,05 e fluxo de ar 0,6 dos ventiladores, mesmo com o radiador danificado.
            if (alive && v < 2f && RaceX.Core.PitLaneSystem.IsInPitFlow(car))
            {
                load = Mathf.Min(load, 0.05f);
                airflow = Mathf.Max(airflow, 0.6f);
            }
            // 105: com esforço médio de corrida ~0,7 e fluxo ~0,95 a água assenta em ar + ~77 °C (≈107 °C a 30 °C);
            // sob SC (esforço ~0,15) esfria. Valor de projeto, ajustado pela medição do teste (ver acima).
            engineTempC += (105f * load - airflow * (engineTempC - air)) / 60f * dt;
            engineTempC = Mathf.Clamp(engineTempC, air, 180f);
            // Desgaste por distância: ~0,14 por fim de semana completo → ~4 motores por temporada (projeto);
            // quente gasta mais (×4 a 135 °C, ×10 acima).
            float heatMul = engineTempC < 120f ? 1f : engineTempC < 135f ? Mathf.Lerp(1f, 4f, (engineTempC - 120f) / 15f) : 10f;
            if (alive) engineWear = Mathf.Min(1f, engineWear + 3.4e-7f * v * dt * (0.5f + 0.5f * load) * heatMul);
            if (engineTempC > EngineFailAt) engineOverTime += dt; else engineOverTime = 0f;
            if (!engineFailed && (engineOverTime >= 10f || engineWear >= 1f))
            {
                engineFailed = true;
                Fail("MOTOR", engineOverTime >= 10f ? "QUEBRA DE MOTOR — superaquecido" : "QUEBRA DE MOTOR — fim da vida útil");
            }
            if (IsHuman)
            {
                if (!warnRadiator && radiatorEff < 0.85f) { warnRadiator = true; Radio("Radiador danificado — o motor vai esquentar"); }
                if (engineTempC > EngineHotFrom - 3f && Time.time > warnEngineUntil)
                {
                    warnEngineUntil = Time.time + 30f;
                    Radio("Motor a " + Mathf.RoundToInt(engineTempC) + " °C — sair do vácuo e aliviar nas retas");
                }
                if (career && !warnEngineLife && engineWear > 0.9f) { warnEngineLife = true; Radio("Motor no fim da vida útil — cuidado com o giro"); }
            }
        }

        void UpdateFuel(float dt, float v)
        {
            if (fuelKg <= 0f) return;
            float thr = engineFailed ? 0f : Mathf.Clamp01(car.pitControlled ? car.extThrottle : car.throttleInput);
            // Consumo por distância, pesado pelo acelerador (0,85 ≈ média de uma volta de corrida); parado: marcha lenta
            // ~1,8 kg/h (estimativa). Modo econômico: −12% de consumo e −3% de potência (projeto).
            float perM = kgPerKm / 1000f * (0.45f + 0.55f * thr) / 0.85f * fuelScale * (fuelSaveLevel == 2 ? 0.78f : fuelSaveLevel == 1 ? 0.88f : 1f);
            float use = perM * v * dt + (v < 1f ? 0.0005f * dt : 0f);
            fuelKg = Mathf.Max(0f, fuelKg - use);
            usedKg += use; traveledM += v * dt;

            if (timed)
            {
                // Treino: a equipe chama de volta e reabastece no box; nunca fica sem combustível na pista.
                float lapKg = trackLen / 1000f * kgPerKm;
                if (PitLaneSystem.StopInfo(car, out _, out _, out _)) { fuelKg = sessionLoadKg; warnFuelLow = false; }
                if (IsHuman && !warnFuelLow && fuelKg < lapKg * 1.5f) { warnFuelLow = true; Radio("Combustível baixo — volte ao box para reabastecer"); }
                fuelKg = Mathf.Max(fuelKg, lapKg * 0.5f);
                return;
            }
            if (entry == null) return;
            // Corrida: compara o que sobra com o que falta (consumo MEDIDO desde a largada após 1 volta).
            float remaining = Mathf.Max(0f, race.Settings.laps * trackLen - entry.raceDistance);
            float rate = traveledM > trackLen ? usedKg / traveledM : kgPerKm / 1000f * fuelScale;
            // rate já inclui a economia em uso: compara com o consumo NORMAL para decidir o nível.
            float normalRate = rate / (fuelSaveLevel == 2 ? 0.78f : fuelSaveLevel == 1 ? 0.88f : 1f);
            float ratio = (remaining * normalRate + 0.5f) / Mathf.Max(0.01f, fuelKg);
            int level = ratio > 1.08f ? 2 : ratio > 1.0f ? 1 : ratio < 0.95f ? 0 : Mathf.Min(fuelSaveLevel, 1);
            if (level > fuelSaveLevel && IsHuman && level > warnedSaveLevel)
            {
                warnedSaveLevel = level;
                Radio(level == 2 ? "Economia forte de combustível — tirar o pé antes das freadas" : "Economize combustível — modo econômico ligado");
            }
            fuelSaveLevel = level;
            if (fuelKg <= 0f) Fail("COMB.", "SEM COMBUSTÍVEL");
        }

        /// <summary>Saída para a física: potência e eficiência de freio por roda.</summary>
        void ApplyOutputs()
        {
            float p = 1f;
            p *= 1f - 0.02f * Mathf.Clamp01((engineWear - 0.5f) / 0.5f);                  // motor gasto: até −2% (projeto)
            p *= 1f - 0.08f * Mathf.Clamp01((engineTempC - EngineHotFrom) / 10f)          // quente: até −8% a 135 °C
                   - 0.12f * Mathf.Clamp01((engineTempC - 135f) / 5f);                       // proteção: mais −12% até 140 °C (projeto)
            if (fuelSaveLevel == 2) p *= 0.93f; else if (fuelSaveLevel == 1) p *= 0.97f;
            if (engineFailed || fuelKg <= 0f) p = 0f;
            car.powerFactor = p;
            for (int i = 0; i < 4; i++)
            {
                float t = brakeTempC[i];
                float e = 1f;
                // Frio morde menos e de forma desigual (carbono precisa de calor; ferro sente pouco) — projeto.
                float coldness = Mathf.Clamp01((BrakeWindowLow - t) / (BrakeWindowLow - 100f));
                e *= Mathf.Lerp(1f, Carbon ? 0.80f : 0.92f, coldness) * (1f + uneven[i] * coldness);
                // Fadiga acima da faixa: até −20% no limite de falha.
                e *= Mathf.Lerp(1f, 0.8f, Mathf.Clamp01((t - BrakeFadeFrom) / (BrakeFailAt - BrakeFadeFrom)));
                if (brakeFailed[i]) e = 0.2f;
                car.brakeEff[i] = e;
            }
        }

        void Fail(string reason, string msg)
        {
            if (retireQueued) return;
            retireQueued = true;
            if (entry != null) RaceControl.Tell(entry, msg);
            Debug.Log("[SISTEMAS] " + (entry != null ? entry.name : car.name) + ": " + msg);
            retireReason = reason;
            retireMsg = msg;
            retireAt = Time.time + 4f;   // o carro perde o freio/motor e sai da corrida em seguida
        }

        bool IsHuman => entry != null && entry.isHuman;
        void Radio(string msg) { if (IsHuman) PitLaneSystem.SendRadio(car, msg); }

        public static string WheelName(int i) => i == 0 ? "dianteiro esquerdo" : i == 1 ? "dianteiro direito" : i == 2 ? "traseiro esquerdo" : "traseiro direito";

        /// <summary>Combustível em voltas (+ sobra / − falta) na corrida; voltas restantes no tanque nas sessões por tempo.</summary>
        public float FuelLapsDelta()
        {
            float lapKg = trackLen / 1000f * (traveledM > trackLen ? usedKg / traveledM * 1000f : kgPerKm * fuelScale);
            if (lapKg <= 0f) return 0f;
            if (timed || entry == null) return fuelKg / lapKg;
            float remaining = Mathf.Max(0f, race.Settings.laps * trackLen - entry.raceDistance);
            return (fuelKg - remaining / trackLen * lapKg) / lapKg;
        }

        public bool Timed => timed;
    }
}
