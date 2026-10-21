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

        public static int UpgradeLevel(CarModel model, int kind)
        {
            return PlayerPrefs.GetInt(Prefix + "Upg." + kind + "." + (int)model, 0);
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
            PlayerPrefs.Save();
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
            PlayerPrefs.Save();
        }
    }
}
