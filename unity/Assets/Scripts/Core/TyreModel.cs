using UnityEngine;

namespace RaceX.Core
{
    /// <summary>
    /// Modelo de pneus v80 — números de projeto ancorados em fontes oficiais (Pirelli / F1):
    /// • Macio = mais aderência e menos durável; Duro = o inverso. Diferença entre compostos
    ///   vizinhos medida pela Pirelli: ~0,6–0,8 s/volta (≈0,7% de tempo de volta) → ~1,4% de aderência.
    /// • Janela de trabalho de ~30 °C por composto (Pirelli "Seeing through the window"); abaixo dela
    ///   o pneu não rende, acima superaquece (perde aderência e o desgaste dispara).
    /// • Cobertores a 70 °C (regra FIA atual) → o pneu sai do box abaixo da janela (frio).
    /// • Intermediário e chuva: a troca slick→inter acontece quando a volta fica ~110–112% da seca
    ///   (Pirelli); inter na pista seca custa ~5–6 s/volta; slick com água acumulada aquaplana.
    /// • Vida (voltas até 0%): Macio ~18, Médio ~28, Duro ~40 (pedido do usuário), Inter ~30, Chuva ~35.
    /// • Queda de rendimento com o desgaste: gradual até ~40% e "penhasco" (cliff) abaixo de ~30%.
    /// </summary>
    public static class TyreModel
    {
        public const int Soft = 0, Medium = 1, Hard = 2, Inter = 3, Wet = 4;
        public const int Count = 5;

        public static readonly string[] Names = { "MACIO", "MÉDIO", "DURO", "INTERMEDIÁRIO", "CHUVA" };
        public static readonly string[] Letters = { "S", "M", "H", "I", "W" };
        public static readonly Color[] Colors =
        {
            new Color(0.95f, 0.18f, 0.16f), // macio: vermelho
            new Color(1f, 0.83f, 0.12f),    // médio: amarelo
            new Color(0.95f, 0.95f, 0.95f), // duro: branco
            new Color(0.2f, 0.8f, 0.3f),    // intermediário: verde
            new Color(0.2f, 0.5f, 1f)       // chuva: azul
        };

        /// <summary>Aderência relativa do pneu NOVO dentro da janela, na pista seca (macio = 1).</summary>
        static readonly float[] BaseGrip = { 1.000f, 0.986f, 0.972f, 0.880f, 0.840f };
        /// <summary>Voltas até 0% na carga nominal.</summary>
        static readonly float[] Life = { 18f, 28f, 40f, 30f, 35f };
        /// <summary>Janela de temperatura (°C).</summary>
        // Janelas = Pirelli (oficial 2019): C5 85–115, C4 90–120, C3 105–135 °C (macio/médio/duro de uma escolha C5-C4-C3).
        static readonly float[] WindowLow = { 85f, 90f, 105f, 50f, 40f };
        static readonly float[] WindowHigh = { 115f, 120f, 135f, 80f, 70f };

        /// <summary>Aderência por vida restante, de 100% a 0% em passos de 10% (índice 10 = 100%).</summary>
        static readonly float[] WearCurve = { 0.72f, 0.84f, 0.895f, 0.93f, 0.952f, 0.965f, 0.976f, 0.985f, 0.992f, 0.997f, 1.0f };

        /// <summary>Geração de calor por composto (v81). Medido no autoteste (pista 32 °C, médio, ritmo de corrida):
        /// a dianteira ficava em 86–91 °C, na borda de baixo da janela (90) — o pneu "não saía do azul". Slick mais
        /// mole gera mais calor (histerese maior); os de chuva seguem como estavam (calibrados com a água).</summary>
        // v81c (resfriamento pela velocidade): macio 1,40 → 1,30 — com 1,40 ele equilibrava a ~117 °C, ACIMA da janela dele
        // (115). Simulado (scratchpad sys/tyresim.py), ritmo de corrida, pista 22/32/42 °C: macio 101/111/121, médio 99/109/119,
        // duro 103/113/123 °C — macio superaquece em pista quente; duro fica abaixo da janela em pista fria (como na F1).
        static readonly float[] HeatGainBy = { 1.30f, 1.27f, 1.34f, 1.0f, 1.0f };

        /// <summary>RESPOSTA TÉRMICA por composto (multiplica aquecimento E resfriamento: muda a VELOCIDADE, não o equilíbrio).
        /// Fontes: compostos mais duros demoram mais para aquecer (macios entram na janela em 1–2 voltas); a Pirelli em Las
        /// Vegas 2023: "religar um duro que esfriou atrás do safety car vai ser muito difícil". Valores de projeto, simulados:
        /// do box até a janela (pista 32 °C) macio ~0,4 volta, médio ~0,4, duro ~1,9; pista 22 °C: duro ~3,5 voltas.</summary>
        static readonly float[] ThermalResponseBy = { 1.30f, 1.0f, 0.65f, 1.0f, 1.0f };
        public static float Response(int c) => ThermalResponseBy[Clamp(c)];

        /// <summary>Temperatura do pneu NOVO saindo do box/garagem: slicks com cobertor (máx. 70 °C, regra FIA); intermediário
        /// 60 °C (fontes conflitam: guia de 2026 diz limite de 60 °C, outras dizem sem cobertor); CHUVA sem cobertor desde
        /// Mônaco 2023 (feito para funcionar a frio) → sai na temperatura da pista.</summary>
        public static float BlanketFor(int c, float trackTempC)
        {
            c = Clamp(c);
            if (c == Wet) return trackTempC;
            if (c == Inter) return Mathf.Max(trackTempC, 60f);
            return Mathf.Max(trackTempC, BlanketTemp);
        }
        public static float HeatGain(int c) => HeatGainBy[Clamp(c)];

        public static int Clamp(int c) => Mathf.Clamp(c, 0, Count - 1);
        public static bool IsWetTyre(int c) => c == Inter || c == Wet;
        public static string Letter(int c) => Letters[Clamp(c)];
        public static string Name(int c) => Names[Clamp(c)];
        public static Color ColorOf(int c) => Colors[Clamp(c)];
        public static float LifeLaps(int c) => Life[Clamp(c)];
        public static float Low(int c) => WindowLow[Clamp(c)];
        public static float High(int c) => WindowHigh[Clamp(c)];
        public static float Base(int c) => BaseGrip[Clamp(c)];
        public const float BlanketTemp = 70f;

        /// <summary>Fator de aderência pela vida restante (1 = novo, 0 = careca).</summary>
        public static float WearFactor(float remaining)
        {
            float r = Mathf.Clamp01(remaining) * 10f;
            int i = Mathf.Clamp(Mathf.FloorToInt(r), 0, 9);
            return Mathf.Lerp(WearCurve[i], WearCurve[i + 1], r - i);
        }

        /// <summary>Fator de temperatura: 1 dentro da janela; frio perde 0,6%/°C (mín. 0,80);
        /// superaquecido perde 0,4%/°C (mín. 0,84).</summary>
        public static float TempFactor(int c, float tempC)
        {
            float lo = Low(c), hi = High(c);
            if (tempC < lo) return Mathf.Max(0.80f, 1f - (lo - tempC) * 0.006f);
            if (tempC > hi) return Mathf.Max(0.84f, 1f - (tempC - hi) * 0.004f);
            return 1f;
        }

        /// <summary>Multiplicador de desgaste por superaquecimento (+5% por °C acima da janela).</summary>
        public static float OverheatWear(int c, float tempC)
        {
            float over = tempC - High(c);
            return over > 0f ? 1f + over * 0.05f : 1f;
        }

        /// <summary>Efeito da ÁGUA na pista (0 seco .. 1 alagado) por composto, com aquaplanagem em
        /// alta velocidade para slick (e para inter com muita água).</summary>
        /// <summary>Separa a água interna (0..1 = textura + lâmina, ver WeatherSystem) em saturação da textura e lâmina (mm).</summary>
        static void SplitWater(float wet, out float texSat, out float filmMm)
        {
            float T = WeatherSystem.TextureMm;
            float total = Mathf.Clamp01(wet) * (T + WeatherSystem.FilmRefMm);
            texSat = Mathf.Clamp01(total / T);
            filmMm = Mathf.Max(0f, total - T);
        }

        /// <summary>Lâmina d'água (mm) acima da textura para este nível interno de água.</summary>
        public static float WaterDepthMm(float wet) { SplitWater(wet, out _, out float f); return f; }

        /// <summary>Lâmina que a banda de rodagem consegue escoar (mm). Pirelli: intermediário 30–40 L/s por pneu a
        /// 300 km/h, chuva 85 L/s; dividindo por (83 m/s × ~0,3 m de banda) ≈ 1,4 mm e 3,4 mm. Slick quase nada.</summary>
        static readonly float[] ClearMm = { 0f, 0f, 0f, 1.4f, 3.4f }; // slick: sem sulco, não escoa lâmina nenhuma

        /// <summary>Aquaplanagem total de pneu liso pela fórmula de Horne (10,35·√psi mph) com as pressões mínimas
        /// oficiais de 2026 (FIA/Pirelli, GP da Austrália: 27,0 psi diant. / 24,5 tras.) → 86,6 / 82,5 km/h ≈ 85 km/h.</summary>
        public const float HorneKmh = 85f;

        /// <summary>Efeito da ÁGUA no atrito (v81, embasado): (1) perda em baixa velocidade pelo filme d'água — ~30%
        /// para borracha com sulco (estudo de vedação da borracha), slick até 45% (razão molhado/seco 0,5–0,75 das
        /// tabelas de engenharia); (2) AQUAPLANAGEM só quando a lâmina passa do que o pneu escoa, começando a ~60%
        /// da velocidade de Horne e total a ~130%. O mesmo atrito vale para acelerar, frear e curvar.</summary>
        public static float WaterFactor(int c, float wet, float speedMs)
        {
            wet = Mathf.Clamp01(wet);
            c = Clamp(c);
            // Pneu de CHUVA com pouca água: sulco fundo se mexe e superaquece — mais lento que o intermediário até
            // haver lâmina d'água (troca inter → chuva perto de 60%).
            // Perda com a TEXTURA cheia, calibrada pela regra da Pirelli (slick → inter quando a volta chega a 110–112%
            // da seca, e o inter também anda ~111% no ponto de troca; velocidade de curva ∝ √atrito → atrito ∝ 1/1,11²):
            // slick perde 19% (0,81) e inter/chuva 8% (0,88×0,92 ≈ 0,81) com a pista molhada sem lâmina.
            SplitWater(wet, out float sat, out float film);
            float low = c <= 2 ? 1f - 0.19f * sat : 1f - 0.08f * sat;
            float excess = Mathf.Clamp01((film - ClearMm[c]) / 1.5f);
            float kmh = speedMs * 3.6f;
            float u = Mathf.Clamp01((kmh - HorneKmh * 0.6f) / (HorneKmh * 0.7f));
            float aq = excess * u * u * (3f - 2f * u);
            return Mathf.Clamp(low * (1f - 0.8f * aq), 0.08f, 1f);
        }

        /// <summary>Pneu de chuva (inter/chuva) na pista seca superaquece: desgaste muito maior.</summary>
        public static float DryWearPenalty(int c, float wet)
        {
            if (!IsWetTyre(c)) return 1f;
            // Pela ÁGUA FÍSICA: o intermediário para de superaquecer com a textura cheia; o de chuva só com lâmina
            // (Pirelli: chuva é para pista "realmente alagada" — a partir do que o inter já não escoa, 1,4 mm).
            SplitWater(wet, out float sat, out float film);
            return c == Wet ? Mathf.Lerp(4.5f, 1f, Mathf.Clamp01(film / ClearMm[Inter])) : Mathf.Lerp(3.5f, 1f, sat);
        }

        /// <summary>Pneu errado para a água, com MARGEM (histerese): perto do ponto de troca a equipe não fica
        /// trocando de pneu a cada oscilação da chuva.</summary>
        public static bool WrongForWetness(int current, float wet, int dryChoice)
        {
            int cur = Clamp(current);
            int dry = IsWetTyre(cur) ? (dryChoice >= 0 && dryChoice <= 2 ? dryChoice : 1) : cur; // de inter/chuva, o seco padrão é o médio
            int best = BestForWetness(wet, dry);
            if (best == cur) return false;
            // Slick entra na conta pelo MÉDIO (mesma referência do BestForWetness): o macio não "aguenta" mais chuva.
            float gBest = IsWetTyre(best) ? ExpectedGrip(best, wet) : ExpectedGrip(Medium, wet);
            float gCur = IsWetTyre(cur) ? ExpectedGrip(cur, wet) : ExpectedGrip(Medium, wet);
            return gBest > gCur * 1.03f; // margem de 3%: não troca por quase nada
        }

        /// <summary>A água pede pneu de chuva (intermediário ou chuva)? Pela comparação de aderência, sem limiar fixo.</summary>
        public static bool NeedsWetTyre(float wet) => IsWetTyre(BestForWetness(wet, 1));

        /// <summary>Aderência média esperada do composto nesta água (base × água em 72, 144 e 216 km/h).</summary>
        public static float ExpectedGrip(int c, float wet)
        {
            c = Clamp(c);
            return Base(c) * (WaterFactor(c, wet, 20f) + WaterFactor(c, wet, 40f) + WaterFactor(c, wet, 60f)) / 3f;
        }

        /// <summary>Composto CERTO para a quantidade de água (regra de crossover usada pela IA e pela UI).</summary>
        public static int BestForWetness(float wet, int dryChoice)
        {
            // O composto mais rápido para a água (comparação direta da aderência esperada — sem limiares inventados).
            // A troca slick → chuva é UMA só para qualquer slick, medida contra o MÉDIO (a mesma do aviso IDEAL do HUD).
            // Antes comparava com o slick planejado: o macio (base maior) "ganhava" do intermediário na chuva e a equipe
            // montava macio com a pista molhada (relato do usuário, 01/11).
            int best = Mathf.Clamp(dryChoice, 0, 2);
            float bg = ExpectedGrip(Medium, wet);
            float gi = ExpectedGrip(Inter, wet), gw = ExpectedGrip(Wet, wet);
            if (gi > bg) { best = Inter; bg = gi; }
            if (gw > bg) best = Wet;
            return best;
        }
    }
}
