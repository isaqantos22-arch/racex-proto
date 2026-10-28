using RaceX.Car;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Carreira local: creditos, estatisticas e upgrades por carro (persistidos em PlayerPrefs).</summary>
    public static class CareerStats
    {
        const string Prefix = "RaceX.";
        /// <summary>v79: 10 níveis (antes 5). Os níveis 6–10 rendem metade de cada um dos 5 primeiros —
        /// quem já tinha nível 5 mantém exatamente o mesmo carro.</summary>
        public const int MaxUpgrade = 10;
        public const int KindEngine = 0, KindBrakes = 1, KindTires = 2, KindAero = 3, KindWeight = 4;
        public const int KindCount = 5;
        public static readonly string[] KindNames = { "Motor", "Freios", "Pneus", "Aerodinâmica", "Redução de peso" };
        public static readonly string[] KindEffects = { "+vel. máx e torque", "+força de frenagem", "+aderência lateral", "+downforce nas curvas", "−peso do carro" };

        /// <summary>Efeito acumulado em "níveis cheios": 1..5 valem 1 cada; 6..10 valem 0,5 cada.</summary>
        public static float EffectiveLevel(int lvl) => Mathf.Min(lvl, 5) + Mathf.Max(0, lvl - 5) * 0.5f;

        /// <summary>Prêmio em créditos por posição (mesma fórmula do OnRaceFinished) — tela PROGRESSO.</summary>
        public static int RewardFor(int position, int totalCars)
        {
            int baseReward = Mathf.Max(3, totalCars) * 400;
            return Mathf.Max(400, baseReward - (position - 1) * 350);
        }

        public static int Credits
        {
            get => PlayerPrefs.GetInt(Prefix + "Credits", 6000);
            private set => PlayerPrefs.SetInt(Prefix + "Credits", value);
        }
        public static int Races
        {
            get => PlayerPrefs.GetInt(Prefix + "Races", 0);
            private set => PlayerPrefs.SetInt(Prefix + "Races", value);
        }
        public static int Wins
        {
            get => PlayerPrefs.GetInt(Prefix + "Wins", 0);
            private set => PlayerPrefs.SetInt(Prefix + "Wins", value);
        }
        public static int Podiums
        {
            get => PlayerPrefs.GetInt(Prefix + "Podiums", 0);
            private set => PlayerPrefs.SetInt(Prefix + "Podiums", value);
        }
        public static int Titles
        {
            get => PlayerPrefs.GetInt(Prefix + "Titles", 0);
            set => PlayerPrefs.SetInt(Prefix + "Titles", value);
        }
        public static long LastUpdatedTicks
        {
            get
            {
                string s = PlayerPrefs.GetString(Prefix + "UpdatedTicks", "0");
                return long.TryParse(s, out long v) ? v : 0L;
            }
            set => PlayerPrefs.SetString(Prefix + "UpdatedTicks", value.ToString());
        }

        public static void TouchUpdated()
        {
            LastUpdatedTicks = System.DateTime.UtcNow.Ticks;
            PlayerPrefs.Save();
        }

        public static int UpgradeLevel(CarModel model, int kind)
        {
            return PlayerPrefs.GetInt(Prefix + "Upg." + kind + "." + (int)model, 0);
        }

        public static int TotalUpgradePoints()
        {
            var models = (CarModel[])System.Enum.GetValues(typeof(CarModel));
            int sum = 0;
            for (int m = 0; m < models.Length; m++)
                for (int k = 0; k < KindCount; k++)
                    sum += UpgradeLevel(models[m], k);
            return sum;
        }

        public static int[] ExportUpgradesArray()
        {
            var models = (CarModel[])System.Enum.GetValues(typeof(CarModel));
            int maxModel = 0;
            for (int i = 0; i < models.Length; i++)
                if ((int)models[i] > maxModel) maxModel = (int)models[i];
            // Layout compatível: [modelos × 3 kinds antigos] + [modelos × 2 kinds novos (aero, peso)].
            int mc = maxModel + 1;
            int[] arr = new int[mc * KindCount];
            for (int m = 0; m < mc; m++)
                for (int k = 0; k < 3; k++)
                    arr[m * 3 + k] = PlayerPrefs.GetInt(Prefix + "Upg." + k + "." + m, 0);
            for (int m = 0; m < mc; m++)
                for (int k = 3; k < KindCount; k++)
                    arr[mc * 3 + m * 2 + (k - 3)] = PlayerPrefs.GetInt(Prefix + "Upg." + k + "." + m, 0);
            return arr;
        }

        public static void SyncFromCloud(int credits, int wins, int podiums, int titles, int races, int[] upgrades, long cloudTicks)
        {
            if (credits >= 0) Credits = credits;
            if (wins > Wins) Wins = wins;
            if (podiums > Podiums) Podiums = podiums;
            if (titles > Titles) Titles = titles;
            if (races > Races) Races = races;
            if (upgrades != null && upgrades.Length >= 3)
            {
                var models = (CarModel[])System.Enum.GetValues(typeof(CarModel));
                int maxModel = 0;
                for (int i = 0; i < models.Length; i++) if ((int)models[i] > maxModel) maxModel = (int)models[i];
                int mc = maxModel + 1;
                bool extended = upgrades.Length == mc * KindCount;
                int modelCount = extended ? mc : upgrades.Length / 3;
                for (int m = 0; m < modelCount; m++)
                    for (int k = 0; k < 3; k++)
                        MergeLevel(m, k, upgrades[m * 3 + k]);
                if (extended)
                    for (int m = 0; m < mc; m++)
                        for (int k = 3; k < KindCount; k++)
                            MergeLevel(m, k, upgrades[mc * 3 + m * 2 + (k - 3)]);
            }
            if (cloudTicks > LastUpdatedTicks) LastUpdatedTicks = cloudTicks;
            PlayerPrefs.Save();
        }

        static void MergeLevel(int model, int kind, int cloudLevel)
        {
            int cloudLvl = Mathf.Clamp(cloudLevel, 0, MaxUpgrade);
            int localLvl = PlayerPrefs.GetInt(Prefix + "Upg." + kind + "." + model, 0);
            if (cloudLvl > localLvl) PlayerPrefs.SetInt(Prefix + "Upg." + kind + "." + model, cloudLvl);
        }

        /// <summary>Custo para comprar o proximo nivel (level atual -> atual+1).</summary>
        public static int UpgradeCost(int currentLevel)
        {
            return 1200 + currentLevel * 900;
        }

        public static bool BuyUpgrade(CarModel model, int kind)
        {
            int lvl = UpgradeLevel(model, kind);
            if (lvl >= MaxUpgrade) return false;
            int cost = UpgradeCost(lvl);
            if (Credits < cost) return false;
            Credits -= cost;
            PlayerPrefs.SetInt(Prefix + "Upg." + kind + "." + (int)model, lvl + 1);
            TouchUpdated();
            if (AccountClient.IsLoggedIn)
                AccountClient.PushRaceStats(Wins, Podiums, Titles, Races);
            return true;
        }

        /// <summary>Aplica os bonus de upgrade no spec (só para carros do jogador).</summary>
        public static void ApplyUpgrades(CarSpec spec)
        {
            if (spec == null) return;
            float engine = EffectiveLevel(UpgradeLevel(spec.model, KindEngine));
            float brakes = EffectiveLevel(UpgradeLevel(spec.model, KindBrakes));
            float tires = EffectiveLevel(UpgradeLevel(spec.model, KindTires));
            float aero = EffectiveLevel(UpgradeLevel(spec.model, KindAero));
            float weight = EffectiveLevel(UpgradeLevel(spec.model, KindWeight));
            spec.topSpeedKmh *= 1f + 0.02f * engine;
            spec.maxTorqueNm *= 1f + 0.03f * engine;
            spec.brakeTorque *= 1f + 0.04f * brakes;
            spec.sidewaysStiffness *= 1f + 0.03f * tires;
            spec.downforceCoefficient *= 1f + 0.03f * aero;
            spec.massKg *= 1f - 0.01f * weight;
        }

        /// <summary>Recompensa de fim de corrida (melhor posicao = mais creditos).</summary>
        public static void OnRaceFinished(int position, int totalCars)
        {
            Races++;
            if (position <= 1) Wins++;
            if (position <= 3) Podiums++;
            int baseReward = Mathf.Max(3, totalCars) * 400;
            int reward = Mathf.Max(400, baseReward - (position - 1) * 350);
            Credits += reward;
            TouchUpdated();
            if (AccountClient.IsLoggedIn)
            {
                AccountClient.PushRaceStats(Wins, Podiums, Titles, Races);
            }
        }
    }
}
