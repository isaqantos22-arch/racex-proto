using RaceX.Car;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Carreira local: creditos, estatisticas e upgrades por carro (persistidos em PlayerPrefs).</summary>
    public static class CareerStats
    {
        const string Prefix = "RaceX.";
        public const int MaxUpgrade = 5;
        public const int KindEngine = 0, KindBrakes = 1, KindTires = 2;

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
                for (int k = 0; k < 3; k++)
                    sum += UpgradeLevel(models[m], k);
            return sum;
        }

        public static int[] ExportUpgradesArray()
        {
            var models = (CarModel[])System.Enum.GetValues(typeof(CarModel));
            int maxModel = 0;
            for (int i = 0; i < models.Length; i++)
                if ((int)models[i] > maxModel) maxModel = (int)models[i];
            int[] arr = new int[(maxModel + 1) * 3];
            for (int m = 0; m <= maxModel; m++)
                for (int k = 0; k < 3; k++)
                    arr[m * 3 + k] = PlayerPrefs.GetInt(Prefix + "Upg." + k + "." + m, 0);
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
                int modelCount = upgrades.Length / 3;
                for (int m = 0; m < modelCount; m++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int cloudLvl = Mathf.Clamp(upgrades[m * 3 + k], 0, MaxUpgrade);
                        int localLvl = PlayerPrefs.GetInt(Prefix + "Upg." + k + "." + m, 0);
                        if (cloudLvl > localLvl || cloudTicks >= LastUpdatedTicks)
                            PlayerPrefs.SetInt(Prefix + "Upg." + k + "." + m, Mathf.Max(cloudLvl, localLvl));
                    }
                }
            }
            if (cloudTicks > LastUpdatedTicks) LastUpdatedTicks = cloudTicks;
            PlayerPrefs.Save();
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
            int engine = UpgradeLevel(spec.model, KindEngine);
            int brakes = UpgradeLevel(spec.model, KindBrakes);
            int tires = UpgradeLevel(spec.model, KindTires);
            spec.topSpeedKmh *= 1f + 0.02f * engine;
            spec.maxTorqueNm *= 1f + 0.03f * engine;
            spec.brakeTorque *= 1f + 0.04f * brakes;
            spec.sidewaysStiffness *= 1f + 0.03f * tires;
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
