using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Track
{
    public enum TrackId
    {
        ForestCircuit = 0, DesertCanyon = 1, NightCity = 2,
        Velocita = 3, Principado = 4, Ardenne = 5, JardimDaBaia = 6,
        GolfoReal = 7, SerraDoCafe = 8, CircuitoDoSol = 9,
        ParqueReal = 10, DragaoOriental = 11, CornicheDaNoite = 12, PortoSul = 13,
        IlhaDasCascatas = 14, ValeDourado = 15, ColinasVerdes = 16, CampoDoNorte = 17,
        CurvasDoDanubio = 18, DunasDoNorte = 19, Fortaleza = 20, ColinasDoTexas = 21,
        EstadioCentral = 22, NeonStrip = 23
    }

    public class TrackDefinition
    {
        public TrackId id;
        public string displayName;
        public string sceneName;
        public Vector3[] controlPoints;
        public float targetLength;
        public float roadWidth = 12f;
        public float kerbWidth = 1.2f;
        public float runoffWidth = 9f;
        public SurfaceType runoffSurface = SurfaceType.Grass;
        public Color groundColor;
        public Color roadColor = new Color(0.22f, 0.22f, 0.23f);
        public Color asphaltColor = new Color(0.16f, 0.16f, 0.18f);
        public float asphaltSmoothness = 0.25f;
        public Color grassBandA = new Color(0.22f, 0.5f, 0.18f);
        public Color grassBandB = new Color(0.18f, 0.44f, 0.15f);
        public Color sunColor;
        public Vector3 sunEuler;
        public float sunIntensity = 1.2f;
        public Color ambientColor;
        public Color fogColor;
        public float fogDensity;
        public string skyboxExr;
    }

    public static class TrackLibrary
    {
        public static readonly TrackId[] All =
        {
            TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.NightCity,
            TrackId.Velocita, TrackId.Principado, TrackId.Ardenne, TrackId.JardimDaBaia,
            TrackId.GolfoReal, TrackId.SerraDoCafe, TrackId.CircuitoDoSol,
            TrackId.ParqueReal, TrackId.DragaoOriental, TrackId.CornicheDaNoite, TrackId.PortoSul,
            TrackId.IlhaDasCascatas, TrackId.ValeDourado, TrackId.ColinasVerdes, TrackId.CampoDoNorte,
            TrackId.CurvasDoDanubio, TrackId.DunasDoNorte, TrackId.Fortaleza, TrackId.ColinasDoTexas,
            TrackId.EstadioCentral, TrackId.NeonStrip
        };

        /// <summary>Voltas de prova REAL (regra F1: 305 km de corrida, Mônaco 260 km). Pista curta → muitas voltas.</summary>
        public static int RealLaps(TrackId id)
        {
            float km = Get(id).targetLength / 1000f;
            if (km <= 0.01f) km = 2f;
            float raceKm = id == TrackId.Principado ? 260f : 305f;
            return Mathf.Max(3, Mathf.CeilToInt(raceKm / km));
        }

        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
        static Vector3[] Pts(params Vector3[] p) => p;

        public static TrackDefinition Get(TrackId id)
        {
            switch (id)
            {
                case TrackId.DesertCanyon:
                    return new TrackDefinition
                    {
                        id = TrackId.DesertCanyon,
                        displayName = "Desfiladeiro Solar",
                        sceneName = "Track_DesertCanyon",
                        targetLength = 2000f,
                        roadWidth = 13f,
                        runoffSurface = SurfaceType.SandDirt,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 150), new Vector3(45, 0, 255), new Vector3(150, 0, 305),
                            new Vector3(260, 0, 290), new Vector3(320, 0, 205), new Vector3(305, 0, 115), new Vector3(360, 0, 45),
                            new Vector3(455, 0, 25), new Vector3(540, 0, 75), new Vector3(560, 0, 175), new Vector3(625, 0, 240),
                            new Vector3(715, 0, 215), new Vector3(740, 0, 120), new Vector3(695, 0, 30), new Vector3(590, 0, -20),
                            new Vector3(480, 0, -5), new Vector3(395, 0, -65), new Vector3(285, 0, -85), new Vector3(175, 0, -70),
                            new Vector3(70, 0, -40)
                        },
                        groundColor = new Color(0.74f, 0.56f, 0.36f),
                        asphaltColor = new Color(0.24f, 0.21f, 0.18f),
                        asphaltSmoothness = 0.15f,
                        grassBandA = new Color(0.79f, 0.61f, 0.4f),
                        grassBandB = new Color(0.71f, 0.53f, 0.33f),
                        sunColor = new Color(1f, 0.86f, 0.62f),
                        sunEuler = new Vector3(48f, -30f, 0f),
                        sunIntensity = 1.45f,
                        ambientColor = new Color(0.6f, 0.55f, 0.48f),
                        fogColor = new Color(0.85f, 0.73f, 0.55f),
                        fogDensity = 0.0011f,
                        skyboxExr = "Skyboxes/sky_desert.hdr",
                    };
                case TrackId.NightCity:
                    return new TrackDefinition
                    {
                        id = TrackId.NightCity,
                        displayName = "Metrópole Noturna",
                        sceneName = "Track_NightCity",
                        targetLength = 1700f,
                        roadWidth = 12f,
                        runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 115), new Vector3(0, 0, 215), new Vector3(65, 0, 265),
                            new Vector3(165, 0, 265), new Vector3(165, 0, 185), new Vector3(265, 0, 185), new Vector3(265, 0, 300),
                            new Vector3(385, 0, 320), new Vector3(445, 0, 240), new Vector3(425, 0, 140), new Vector3(445, 0, 45),
                            new Vector3(365, 0, -20), new Vector3(255, 0, -10), new Vector3(155, 0, -35), new Vector3(65, 0, -55)
                        },
                        groundColor = new Color(0.09f, 0.09f, 0.11f),
                        asphaltColor = new Color(0.12f, 0.12f, 0.14f),
                        asphaltSmoothness = 0.5f,
                        grassBandA = new Color(0.33f, 0.34f, 0.37f),
                        grassBandB = new Color(0.29f, 0.3f, 0.33f),
                        sunColor = new Color(0.62f, 0.72f, 1f),
                        sunEuler = new Vector3(38f, 150f, 0f),
                        sunIntensity = 0.55f,
                        ambientColor = new Color(0.17f, 0.19f, 0.28f),
                        fogColor = new Color(0.03f, 0.04f, 0.08f),
                        fogDensity = 0.0016f,
                        skyboxExr = "Skyboxes/sky_city.hdr",
                    };
                case TrackId.Velocita: // Inspirada em Monza: retões, dois chicanes e curvão final
                    return new TrackDefinition
                    {
                        id = TrackId.Velocita,
                        displayName = "Velocità Park",
                        sceneName = "Track_Velocita",
                        targetLength = 2834f,
                        roadWidth = 13f,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 20.1f), V(0f, 40.1f), V(0f, 60.2f), V(0f, 80.2f), V(0f, 100.3f), V(0f, 120.3f), V(0f, 140.4f),
                            V(0f, 160.4f), V(0f, 180.5f), V(0f, 200.5f), V(0f, 220.6f), V(0f, 240.6f), V(0f, 260.7f), V(0f, 280.8f), V(0f, 300.8f),
                            V(0f, 320.9f), V(0f, 340.9f), V(0f, 361f), V(0f, 381f), V(0f, 401.1f), V(0f, 421.1f), V(0f, 441.2f), V(0f, 461.2f),
                            V(0f, 481.3f), V(0f, 501.3f), V(0f, 521.4f), V(0f, 541.4f), V(0f, 561.5f), V(0f, 581.6f), V(0f, 601.6f), V(0f, 621.7f),
                            V(0f, 641.7f), V(0.7f, 646.9f), V(2.7f, 651.7f), V(5.9f, 655.9f), V(20.2f, 670.2f), V(34.5f, 684.5f), V(37.6f, 688.4f), V(39.5f, 692.9f),
                            V(40.4f, 697.7f), V(41.3f, 716.7f), V(42.2f, 735.6f), V(43.1f, 754.6f), V(44f, 773.5f), V(44.9f, 792.5f), V(46.9f, 811f), V(51.3f, 829.2f),
                            V(57.8f, 846.7f), V(66.5f, 863.2f), V(77.1f, 878.5f), V(89.6f, 892.4f), V(103.7f, 904.6f), V(119.2f, 915f), V(135.9f, 923.3f), V(153.5f, 929.6f),
                            V(172.4f, 935f), V(191.3f, 940.4f), V(210.2f, 945.8f), V(229.2f, 951.2f), V(248.1f, 956.6f), V(255.7f, 957.5f), V(263.2f, 956.2f), V(269.9f, 952.6f),
                            V(283.5f, 942.4f), V(297f, 932.3f), V(299.8f, 929.8f), V(302.2f, 927f), V(314.5f, 910.1f), V(326.7f, 893.3f), V(339f, 876.4f), V(351.2f, 859.6f),
                            V(363.5f, 842.7f), V(371f, 827.5f), V(373f, 810.6f), V(369.2f, 794.1f), V(362.1f, 776.9f), V(354.9f, 759.8f), V(347.8f, 742.6f), V(340.6f, 725.5f),
                            V(333.5f, 708.3f), V(330.9f, 699.8f), V(330f, 691f), V(330f, 671.1f), V(330f, 651.3f), V(330f, 631.4f), V(330f, 611.6f), V(330f, 591.7f),
                            V(330f, 571.9f), V(330f, 552f), V(330f, 532.2f), V(330f, 512.3f), V(330f, 492.5f), V(330f, 472.6f), V(330f, 452.8f), V(330f, 432.9f),
                            V(330f, 413.1f), V(330f, 393.2f), V(330f, 373.4f), V(330f, 353.5f), V(330f, 333.7f), V(330f, 313.8f), V(328.2f, 300.5f), V(322.9f, 288.1f),
                            V(310.5f, 267.5f), V(305.8f, 255.9f), V(305f, 243.4f), V(308.1f, 231.2f), V(317f, 210.3f), V(326f, 189.4f), V(329f, 179.8f), V(330f, 169.7f),
                            V(330f, 149.8f), V(330f, 129.8f), V(330f, 109.8f), V(330f, 89.8f), V(330f, 69.8f), V(330f, 49.8f), V(330f, 29.9f), V(330f, 9.9f),
                            V(330f, -10.1f), V(330f, -30.1f), V(330f, -50.1f), V(330f, -70f), V(330f, -90f), V(330f, -110f), V(328.6f, -129.9f), V(324.3f, -149.4f),
                            V(317.3f, -168.2f), V(307.8f, -185.7f), V(295.8f, -201.7f), V(281.7f, -215.8f), V(265.7f, -227.8f), V(248.2f, -237.3f), V(229.4f, -244.3f), V(209.9f, -248.6f),
                            V(190f, -250f), V(173.3f, -250f), V(156.7f, -250f), V(140f, -250f), V(120.1f, -248.6f), V(100.6f, -244.3f), V(81.8f, -237.3f), V(64.3f, -227.8f),
                            V(48.3f, -215.8f), V(34.2f, -201.7f), V(22.2f, -185.7f), V(12.7f, -168.2f), V(5.7f, -149.4f), V(1.4f, -129.9f), V(0f, -110f), V(0f, -91.7f),
                            V(0f, -73.3f), V(0f, -55f), V(0f, -36.7f), V(0f, -18.3f)),
                        groundColor = new Color(0.2f, 0.38f, 0.15f),
                        asphaltSmoothness = 0.35f,
                        grassBandA = new Color(0.25f, 0.48f, 0.2f),
                        grassBandB = new Color(0.21f, 0.42f, 0.17f),
                        sunColor = new Color(1f, 0.9f, 0.72f),
                        sunEuler = new Vector3(42f, -25f, 0f),
                        sunIntensity = 1.35f,
                        ambientColor = new Color(0.55f, 0.6f, 0.68f),
                        fogColor = new Color(0.72f, 0.79f, 0.86f),
                        fogDensity = 0.0009f,
                        skyboxExr = "Skyboxes/sky_forest.hdr",
                    };
                case TrackId.Principado: // Inspirada em Mônaco: rua estreita, 90° e um gancho
                    return new TrackDefinition
                    {
                        id = TrackId.Principado,
                        displayName = "Principado à Beira-Mar",
                        sceneName = "Track_Principado",
                        targetLength = 2152f,
                        roadWidth = 10f,
                        kerbWidth = 0.8f,
                        runoffWidth = 3f,
                        runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 20.3f), V(0f, 40.5f), V(0f, 60.8f), V(0f, 81f), V(0f, 101.3f), V(0f, 121.5f), V(0f, 141.8f),
                            V(0f, 162f), V(0f, 182.3f), V(0f, 202.5f), V(0f, 222.8f), V(0f, 243f), V(0f, 263.3f), V(0f, 283.5f), V(0f, 303.8f),
                            V(0f, 324.1f), V(0.8f, 329.8f), V(3f, 335.1f), V(13.1f, 352.4f), V(23.2f, 369.7f), V(33.3f, 387f), V(43.4f, 404.4f), V(53.5f, 421.7f),
                            V(63.6f, 439f), V(68.1f, 450.4f), V(68.8f, 462.7f), V(67f, 482.6f), V(65.2f, 502.5f), V(63.4f, 522.4f), V(61.6f, 542.2f), V(62f, 550.2f),
                            V(64.4f, 557.9f), V(68.8f, 564.6f), V(74.9f, 569.9f), V(91f, 580.7f), V(107.1f, 591.4f), V(123.2f, 602.2f), V(139.4f, 612.9f), V(144.6f, 615.5f),
                            V(150.2f, 616.6f), V(156f, 616.2f), V(161.4f, 614.3f), V(177.7f, 606.1f), V(194f, 598f), V(210.3f, 589.8f), V(215.9f, 585.8f), V(220f, 580.3f),
                            V(222.2f, 573.8f), V(222.2f, 566.9f), V(220.2f, 560.3f), V(209.4f, 538.8f), V(198.7f, 517.4f), V(196.7f, 511.3f), V(196.5f, 505f), V(198.1f, 498.9f),
                            V(201.4f, 493.5f), V(206.2f, 489.2f), V(224f, 477.3f), V(241.9f, 465.4f), V(247.4f, 462.8f), V(253.4f, 461.7f), V(259.4f, 462.4f), V(278.7f, 467.2f),
                            V(297.9f, 472f), V(317.2f, 476.8f), V(323.1f, 477.4f), V(328.9f, 476.5f), V(334.3f, 474f), V(338.9f, 470.2f), V(353.4f, 454f), V(367.9f, 437.9f),
                            V(382.4f, 421.8f), V(396.9f, 405.7f), V(406.7f, 392.5f), V(414f, 377.7f), V(418.5f, 361.9f), V(420f, 345.5f), V(420f, 323.5f), V(420f, 301.6f),
                            V(420f, 279.7f), V(420f, 257.7f), V(418.8f, 250.4f), V(415.2f, 244f), V(403.2f, 229f), V(391.2f, 214f), V(387.6f, 207.5f), V(386.4f, 200.1f),
                            V(387.8f, 192.7f), V(391.5f, 186.2f), V(404.9f, 170.1f), V(418.3f, 154.1f), V(422f, 147.5f), V(423.4f, 140f), V(422f, 132.5f), V(418.3f, 125.9f),
                            V(402.7f, 107.2f), V(387.2f, 88.6f), V(383.6f, 82.7f), V(382.1f, 76f), V(382.7f, 69.2f), V(387.2f, 51.1f), V(391.7f, 33f), V(396.3f, 15f),
                            V(397f, 8.1f), V(395.8f, 1.3f), V(392.8f, -4.9f), V(388.3f, -10.1f), V(372.8f, -23.3f), V(357.3f, -36.6f), V(341.8f, -49.9f), V(336.1f, -53.5f),
                            V(329.5f, -55.1f), V(322.8f, -54.7f), V(316.5f, -52.3f), V(297.2f, -41.2f), V(277.8f, -30.2f), V(271.7f, -27.8f), V(265.1f, -27.4f), V(258.7f, -28.9f),
                            V(253f, -32.2f), V(248.6f, -37.1f), V(237.6f, -53.5f), V(226.7f, -70f), V(215.7f, -86.5f), V(204.7f, -102.9f), V(201.2f, -107f), V(196.8f, -110.1f),
                            V(191.7f, -112.1f), V(172.4f, -116.9f), V(153.1f, -121.7f), V(133.8f, -126.5f), V(114.5f, -131.4f), V(95.2f, -136.2f), V(75.9f, -141f), V(56.6f, -145.9f),
                            V(37.3f, -150.7f), V(28.3f, -151.5f), V(19.4f, -149.7f), V(11.5f, -145.2f), V(5.3f, -138.6f), V(1.4f, -130.5f), V(0f, -121.6f), V(0f, -101.3f),
                            V(0f, -81.1f), V(0f, -60.8f), V(0f, -40.5f), V(0f, -20.3f)),
                        groundColor = new Color(0.32f, 0.34f, 0.38f),
                        asphaltSmoothness = 0.55f,
                        grassBandA = new Color(0.36f, 0.4f, 0.44f),
                        grassBandB = new Color(0.32f, 0.36f, 0.4f),
                        sunColor = new Color(1f, 0.88f, 0.7f),
                        sunEuler = new Vector3(35f, -45f, 0f),
                        sunIntensity = 1.3f,
                        ambientColor = new Color(0.58f, 0.62f, 0.7f),
                        fogColor = new Color(0.74f, 0.8f, 0.88f),
                        fogDensity = 0.001f,
                        skyboxExr = "Skyboxes/sky_city.hdr",
                    };
                case TrackId.Ardenne: // Inspirada em Spa: pista longa, curvas rápidas e um gancho
                    return new TrackDefinition
                    {
                        id = TrackId.Ardenne,
                        displayName = "Floresta de Ardenne",
                        sceneName = "Track_Ardenne",
                        targetLength = 2965f,
                        roadWidth = 13f,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 19.9f), V(0f, 39.8f), V(0f, 59.7f), V(0f, 79.6f), V(0f, 99.5f), V(0f, 119.5f), V(0f, 139.4f),
                            V(0f, 159.3f), V(0f, 179.2f), V(0f, 199.1f), V(0f, 219f), V(0f, 238.9f), V(0f, 258.8f), V(0f, 278.7f), V(0f, 298.6f),
                            V(0f, 318.5f), V(0f, 338.5f), V(0f, 358.4f), V(0f, 378.3f), V(0f, 398.2f), V(0f, 418.1f), V(0f, 438f), V(1.1f, 444.8f),
                            V(4.2f, 450.9f), V(9.1f, 455.8f), V(15.2f, 458.9f), V(22f, 460f), V(38f, 460f), V(44.8f, 458.9f), V(50.9f, 455.8f), V(55.8f, 450.9f),
                            V(58.9f, 444.8f), V(60f, 438f), V(60f, 417.5f), V(60f, 396.9f), V(60f, 376.4f), V(60f, 355.9f), V(60f, 335.3f), V(60f, 314.8f),
                            V(61.1f, 305.4f), V(64.4f, 296.6f), V(69.6f, 288.8f), V(84.3f, 271.7f), V(98.9f, 254.7f), V(113.5f, 237.6f), V(118.9f, 229.4f), V(122.2f, 220.2f),
                            V(127.1f, 198.1f), V(132f, 176f), V(136.9f, 153.9f), V(142.1f, 141.3f), V(151.2f, 131.2f), V(166.7f, 119f), V(182.2f, 106.7f), V(197.6f, 94.5f),
                            V(213.1f, 82.3f), V(228.6f, 70f), V(244.1f, 57.8f), V(259.6f, 45.6f), V(275.1f, 33.4f), V(290.6f, 21.1f), V(306.1f, 8.9f), V(321.6f, -3.3f),
                            V(337f, -15.6f), V(352.5f, -27.8f), V(368f, -40f), V(383.5f, -52.2f), V(399f, -64.5f), V(414.5f, -76.7f), V(430f, -88.9f), V(445.5f, -101.2f),
                            V(461f, -113.4f), V(476.4f, -125.6f), V(491.9f, -137.8f), V(507.4f, -150.1f), V(518.3f, -161.3f), V(526f, -174.9f), V(533f, -192.6f), V(540.1f, -210.3f),
                            V(547.2f, -228f), V(554.3f, -245.8f), V(559.1f, -253.8f), V(566.2f, -260f), V(574.9f, -263.7f), V(592.6f, -268.1f), V(610.3f, -272.6f), V(627.9f, -277f),
                            V(635.2f, -279.9f), V(641.5f, -284.5f), V(646.4f, -290.7f), V(658.4f, -310.6f), V(670.4f, -330.6f), V(682.4f, -350.6f), V(688.1f, -364.5f), V(689.4f, -379.4f),
                            V(686.3f, -394f), V(679f, -407f), V(667.4f, -422f), V(655.7f, -436.9f), V(644.1f, -451.8f), V(632.5f, -466.8f), V(620.9f, -481.7f), V(609.3f, -496.7f),
                            V(597.7f, -511.6f), V(586f, -526.5f), V(574.4f, -541.5f), V(569.2f, -546.3f), V(562.6f, -549.2f), V(555.5f, -549.9f), V(548.5f, -548.2f), V(542.4f, -544.4f),
                            V(527.5f, -531.1f), V(512.6f, -517.9f), V(497.7f, -504.6f), V(490.8f, -500.3f), V(483f, -498.4f), V(474.9f, -499.1f), V(467.5f, -502.3f), V(461.5f, -507.8f),
                            V(457.5f, -514.9f), V(450.6f, -534.3f), V(443.6f, -553.8f), V(436.7f, -573.3f), V(428.9f, -589.2f), V(417.8f, -603f), V(403.9f, -614.1f), V(388f, -621.8f),
                            V(370.7f, -625.8f), V(348.7f, -628.4f), V(326.7f, -631f), V(304.8f, -633.6f), V(282.8f, -636.1f), V(267.7f, -636f), V(253.2f, -632.1f), V(240.1f, -624.8f),
                            V(229.2f, -614.4f), V(217.3f, -599.7f), V(205.4f, -585.1f), V(193.4f, -570.4f), V(181.5f, -555.7f), V(169.6f, -541.1f), V(157.7f, -526.4f), V(145.8f, -511.7f),
                            V(133.9f, -497.1f), V(122f, -482.4f), V(119.9f, -480.1f), V(117.8f, -477.8f), V(103.4f, -463.4f), V(88.9f, -448.9f), V(74.5f, -434.5f), V(60f, -420f),
                            V(42.7f, -402.7f), V(25.5f, -385.5f), V(21.2f, -379.4f), V(19.1f, -372.3f), V(17f, -353.4f), V(14.9f, -334.4f), V(12.8f, -315.4f), V(10.7f, -296.5f),
                            V(8.6f, -277.5f), V(6.5f, -258.5f), V(4.4f, -239.6f), V(2.3f, -220.6f), V(0.2f, -201.7f), V(0f, -198.3f), V(0f, -178.5f), V(0f, -158.7f),
                            V(0f, -138.8f), V(0f, -119f), V(0f, -99.2f), V(0f, -79.3f), V(0f, -59.5f), V(0f, -39.7f), V(0f, -19.8f)),
                        groundColor = new Color(0.16f, 0.3f, 0.13f),
                        grassBandA = new Color(0.2f, 0.4f, 0.17f),
                        grassBandB = new Color(0.16f, 0.34f, 0.14f),
                        sunColor = new Color(0.95f, 0.92f, 0.85f),
                        sunEuler = new Vector3(38f, -20f, 0f),
                        sunIntensity = 1.15f,
                        ambientColor = new Color(0.5f, 0.56f, 0.62f),
                        fogColor = new Color(0.68f, 0.74f, 0.8f),
                        fogDensity = 0.0014f,
                        skyboxExr = "Skyboxes/sky_forest.hdr",
                    };
                case TrackId.JardimDaBaia: // Inspirada em Singapura: urbana noturna em blocos
                    return new TrackDefinition
                    {
                        id = TrackId.JardimDaBaia,
                        displayName = "Jardim da Baía",
                        sceneName = "Track_JardimDaBaia",
                        targetLength = 2642f,
                        roadWidth = 12f,
                        runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 19.9f), V(0f, 39.8f), V(0f, 59.7f), V(0f, 79.6f), V(0f, 99.5f), V(0f, 119.4f), V(0f, 139.3f),
                            V(0f, 159.2f), V(0f, 179.1f), V(0f, 199f), V(0f, 218.9f), V(0f, 238.8f), V(0f, 258.7f), V(0f, 278.6f), V(0f, 298.5f),
                            V(0f, 318.4f), V(0f, 338.3f), V(0f, 358.2f), V(0f, 378.1f), V(0f, 398f), V(-1.1f, 404.8f), V(-4.2f, 410.9f), V(-9.1f, 415.8f),
                            V(-15.2f, 418.9f), V(-22f, 420f), V(-41f, 420f), V(-60f, 420f), V(-79f, 420f), V(-98f, 420f), V(-104.8f, 421.1f), V(-110.9f, 424.2f),
                            V(-115.8f, 429.1f), V(-118.9f, 435.2f), V(-120f, 442f), V(-120f, 460.7f), V(-120f, 479.3f), V(-120f, 498f), V(-121.1f, 504.8f), V(-124.2f, 510.9f),
                            V(-129.1f, 515.8f), V(-135.2f, 518.9f), V(-142f, 520f), V(-161f, 520f), V(-180f, 520f), V(-199f, 520f), V(-218f, 520f), V(-237f, 520f),
                            V(-256f, 520f), V(-275f, 520f), V(-282.7f, 518.8f), V(-289.7f, 515.2f), V(-295.2f, 509.7f), V(-298.8f, 502.7f), V(-300f, 495f), V(-300f, 476.4f),
                            V(-300f, 457.8f), V(-300f, 439.2f), V(-300f, 420.6f), V(-300f, 402f), V(-301.1f, 395.2f), V(-304.2f, 389.1f), V(-309.1f, 384.2f), V(-315.2f, 381.1f),
                            V(-322f, 380f), V(-341f, 380f), V(-360f, 380f), V(-379f, 380f), V(-398f, 380f), V(-404.8f, 378.9f), V(-410.9f, 375.8f), V(-415.8f, 370.9f),
                            V(-418.9f, 364.8f), V(-420f, 358f), V(-420f, 337.1f), V(-420f, 316.2f), V(-420f, 295.3f), V(-420f, 274.4f), V(-420f, 253.6f), V(-420f, 232.7f),
                            V(-420f, 211.8f), V(-420f, 190.9f), V(-420f, 170f), V(-418.5f, 160.7f), V(-414.3f, 152.4f), V(-407.6f, 145.7f), V(-399.3f, 141.5f), V(-390f, 140f),
                            V(-367.3f, 140f), V(-344.7f, 140f), V(-322f, 140f), V(-315.2f, 138.9f), V(-309.1f, 135.8f), V(-304.2f, 130.9f), V(-301.1f, 124.8f), V(-300f, 118f),
                            V(-300f, 99.3f), V(-300f, 80.7f), V(-300f, 62f), V(-301.1f, 55.2f), V(-304.2f, 49.1f), V(-309.1f, 44.2f), V(-315.2f, 41.1f), V(-322f, 40f),
                            V(-341.4f, 40f), V(-360.9f, 40f), V(-380.3f, 40f), V(-399.7f, 40f), V(-419.1f, 40f), V(-438.6f, 40f), V(-458f, 40f), V(-464.8f, 38.9f),
                            V(-470.9f, 35.8f), V(-475.8f, 30.9f), V(-478.9f, 24.8f), V(-480f, 18f), V(-480f, -1.1f), V(-480f, -20.2f), V(-480f, -39.4f), V(-480f, -58.5f),
                            V(-480f, -77.6f), V(-480f, -96.8f), V(-480f, -115.9f), V(-480f, -135f), V(-478.8f, -142.7f), V(-475.2f, -149.7f), V(-469.7f, -155.2f), V(-462.7f, -158.8f),
                            V(-455f, -160f), V(-435.6f, -160f), V(-416.2f, -160f), V(-396.8f, -160f), V(-377.3f, -160f), V(-357.9f, -160f), V(-338.5f, -160f), V(-319.1f, -160f),
                            V(-299.7f, -160f), V(-280.2f, -160f), V(-260.8f, -160f), V(-241.4f, -160f), V(-222f, -160f), V(-215.2f, -158.9f), V(-209.1f, -155.8f), V(-204.2f, -150.9f),
                            V(-201.1f, -144.8f), V(-200f, -138f), V(-200f, -120f), V(-200f, -102f), V(-198.9f, -95.2f), V(-195.8f, -89.1f), V(-190.9f, -84.2f), V(-184.8f, -81.1f),
                            V(-178f, -80f), V(-159.3f, -80f), V(-140.7f, -80f), V(-122f, -80f), V(-115.2f, -81.1f), V(-109.1f, -84.2f), V(-104.2f, -89.1f), V(-101.1f, -95.2f),
                            V(-100f, -102f), V(-100f, -121f), V(-100f, -140f), V(-100f, -159f), V(-100f, -178f), V(-98.9f, -184.8f), V(-95.8f, -190.9f), V(-90.9f, -195.8f),
                            V(-84.8f, -198.9f), V(-78f, -200f), V(-60.3f, -200f), V(-42.7f, -200f), V(-25f, -200f), V(-17.3f, -198.8f), V(-10.3f, -195.2f), V(-4.8f, -189.7f),
                            V(-1.2f, -182.7f), V(0f, -175f), V(0f, -155.6f), V(0f, -136.1f), V(0f, -116.7f), V(0f, -97.2f), V(0f, -77.8f), V(0f, -58.3f),
                            V(0f, -38.9f), V(0f, -19.4f)),
                        groundColor = new Color(0.1f, 0.11f, 0.13f),
                        asphaltColor = new Color(0.13f, 0.13f, 0.15f),
                        asphaltSmoothness = 0.5f,
                        grassBandA = new Color(0.3f, 0.32f, 0.35f),
                        grassBandB = new Color(0.26f, 0.28f, 0.31f),
                        sunColor = new Color(0.6f, 0.7f, 1f),
                        sunEuler = new Vector3(36f, 145f, 0f),
                        sunIntensity = 0.5f,
                        ambientColor = new Color(0.18f, 0.2f, 0.3f),
                        fogColor = new Color(0.03f, 0.05f, 0.09f),
                        fogDensity = 0.0015f,
                        skyboxExr = "Skyboxes/sky_city.hdr",
                    };
                case TrackId.GolfoReal: // Inspirada no Bahrain: deserto, freadas fortes
                    return new TrackDefinition
                    {
                        id = TrackId.GolfoReal,
                        displayName = "Golfo Real",
                        sceneName = "Track_GolfoReal",
                        targetLength = 2831f,
                        roadWidth = 13f,
                        runoffSurface = SurfaceType.SandDirt,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 19.8f), V(0f, 39.6f), V(0f, 59.3f), V(0f, 79.1f), V(0f, 98.9f), V(0f, 118.7f), V(0f, 138.5f),
                            V(0f, 158.3f), V(0f, 178f), V(0f, 197.8f), V(0f, 217.6f), V(0f, 237.4f), V(0f, 257.2f), V(0f, 277f), V(0f, 296.7f),
                            V(0f, 316.5f), V(0f, 336.3f), V(0f, 356.1f), V(0f, 375.9f), V(0f, 395.7f), V(0f, 415.4f), V(0f, 435.2f), V(0f, 455f),
                            V(1.2f, 462.7f), V(4.8f, 469.7f), V(10.3f, 475.2f), V(17.3f, 478.8f), V(25f, 480f), V(44.3f, 480f), V(63.6f, 480f), V(82.9f, 480f),
                            V(102.2f, 480f), V(121.5f, 480f), V(140.8f, 480f), V(152.5f, 478.2f), V(163.3f, 473.1f), V(172f, 465f), V(183f, 451.2f), V(194f, 437.5f),
                            V(205f, 423.7f), V(216f, 410f), V(224.7f, 395.3f), V(228.8f, 378.8f), V(228.2f, 361.8f), V(222.8f, 345.7f), V(214.3f, 328.5f), V(205.7f, 311.4f),
                            V(197.1f, 294.3f), V(188.6f, 277.2f), V(185.9f, 269.1f), V(185.6f, 260.6f), V(187.7f, 252.3f), V(192f, 245f), V(204.1f, 229.9f), V(216.1f, 214.8f),
                            V(228.2f, 199.7f), V(240.3f, 184.6f), V(252.4f, 169.5f), V(257.9f, 164.2f), V(264.6f, 160.4f), V(272.1f, 158.5f), V(293.2f, 155.8f), V(314.4f, 153.2f),
                            V(335.6f, 150.6f), V(356.7f, 147.9f), V(377.9f, 145.3f), V(396.3f, 145.8f), V(413.6f, 151.9f), V(428.3f, 163f), V(439f, 178f), V(447.3f, 194.6f),
                            V(455.6f, 211.3f), V(464f, 227.9f), V(472.3f, 244.6f), V(480.6f, 261.2f), V(488.9f, 277.9f), V(495.2f, 287f), V(503.7f, 294f), V(513.8f, 298.5f),
                            V(524.7f, 300f), V(546.3f, 300f), V(567.9f, 300f), V(589.6f, 300f), V(611.2f, 300f), V(623.5f, 298.1f), V(634.6f, 292.4f), V(643.4f, 283.7f),
                            V(649.1f, 272.6f), V(655.2f, 254.5f), V(661.2f, 236.3f), V(667.3f, 218.1f), V(673.4f, 199.9f), V(679.4f, 181.7f), V(685.5f, 163.6f), V(691.5f, 145.4f),
                            V(694.1f, 129.3f), V(691.3f, 113.2f), V(683.6f, 98.9f), V(671.8f, 83.8f), V(660.1f, 68.7f), V(648.4f, 53.7f), V(636.7f, 38.6f), V(624.9f, 23.5f),
                            V(613.2f, 8.4f), V(601.5f, -6.6f), V(589.8f, -21.7f), V(578f, -36.8f), V(566.3f, -51.9f), V(561.6f, -60.6f), V(560f, -70.3f), V(560f, -92.3f),
                            V(560f, -114.4f), V(560f, -136.4f), V(560f, -158.4f), V(557.3f, -176.3f), V(549.3f, -192.6f), V(536.9f, -205.8f), V(521.1f, -214.6f), V(501.2f, -222.1f),
                            V(481.3f, -229.5f), V(461.4f, -237f), V(441.5f, -244.5f), V(421.6f, -251.9f), V(412f, -253.8f), V(402.3f, -252.5f), V(393.5f, -248.2f), V(386.6f, -241.3f),
                            V(374.2f, -223.8f), V(361.7f, -206.4f), V(349.2f, -188.9f), V(336.8f, -171.5f), V(324.3f, -154f), V(311.9f, -136.6f), V(305.8f, -130.6f), V(298.1f, -127f),
                            V(289.6f, -126.2f), V(281.4f, -128.3f), V(262.2f, -136.8f), V(243f, -145.4f), V(223.8f, -153.9f), V(204.6f, -162.4f), V(185.4f, -171f), V(166.2f, -179.5f),
                            V(147f, -188f), V(127.8f, -196.6f), V(119.8f, -199.1f), V(111.5f, -200f), V(93.6f, -200f), V(75.8f, -200f), V(57.9f, -200f), V(40f, -200f),
                            V(27.6f, -198f), V(16.5f, -192.4f), V(7.6f, -183.5f), V(2f, -172.4f), V(0f, -160f), V(0f, -140f), V(0f, -120f), V(0f, -100f),
                            V(0f, -80f), V(0f, -60f), V(0f, -40f), V(0f, -20f)),
                        groundColor = new Color(0.72f, 0.52f, 0.3f),
                        asphaltColor = new Color(0.26f, 0.22f, 0.19f),
                        asphaltSmoothness = 0.2f,
                        grassBandA = new Color(0.78f, 0.58f, 0.36f),
                        grassBandB = new Color(0.7f, 0.5f, 0.3f),
                        sunColor = new Color(1f, 0.83f, 0.58f),
                        sunEuler = new Vector3(50f, -40f, 0f),
                        sunIntensity = 1.5f,
                        ambientColor = new Color(0.62f, 0.52f, 0.44f),
                        fogColor = new Color(0.87f, 0.7f, 0.48f),
                        fogDensity = 0.0012f,
                        skyboxExr = "Skyboxes/sky_desert.hdr",
                    };
                case TrackId.SerraDoCafe: // Inspirada em Interlagos: compacta, anti-horária, S inicial
                    return new TrackDefinition
                    {
                        id = TrackId.SerraDoCafe,
                        displayName = "Serra do Café",
                        sceneName = "Track_SerraDoCafe",
                        targetLength = 2092f,
                        roadWidth = 13f,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 20f), V(0f, 39.9f), V(0f, 59.9f), V(0f, 79.8f), V(0f, 99.8f), V(0f, 119.8f), V(0f, 139.7f),
                            V(0f, 159.7f), V(0f, 179.6f), V(0f, 199.6f), V(0f, 219.6f), V(0f, 239.5f), V(0f, 259.5f), V(0f, 279.4f), V(0f, 299.4f),
                            V(0f, 319.4f), V(0f, 339.3f), V(0f, 359.3f), V(0f, 379.2f), V(0f, 399.2f), V(-1.4f, 408.2f), V(-5.3f, 416.3f), V(-11.6f, 422.9f),
                            V(-19.5f, 427.3f), V(-44.3f, 436.6f), V(-51.8f, 438f), V(-59.4f, 436.8f), V(-66f, 433f), V(-71f, 427.2f), V(-73.7f, 420f), V(-73.7f, 412.4f),
                            V(-70.7f, 394.1f), V(-67.6f, 375.8f), V(-64.6f, 357.5f), V(-64.4f, 345.4f), V(-67.9f, 333.8f), V(-74.7f, 323.7f), V(-84.2f, 316.2f), V(-102.1f, 306f),
                            V(-120f, 295.7f), V(-137.9f, 285.5f), V(-155.7f, 275.3f), V(-173.6f, 265.1f), V(-191.1f, 258.4f), V(-209.7f, 257.5f), V(-227.8f, 262.3f), V(-245.7f, 270.3f),
                            V(-263.7f, 278.3f), V(-281.7f, 286.3f), V(-299.7f, 294.3f), V(-317.6f, 302.3f), V(-335.6f, 310.3f), V(-353.6f, 318.3f), V(-365.6f, 321.5f), V(-378.1f, 320.9f),
                            V(-389.7f, 316.4f), V(-399.4f, 308.6f), V(-412.8f, 293.9f), V(-426.1f, 279.2f), V(-439.5f, 264.5f), V(-452.9f, 249.9f), V(-461.9f, 235.6f), V(-465.8f, 219.2f),
                            V(-463.9f, 202.4f), V(-456.5f, 187.2f), V(-445.7f, 171.9f), V(-434.8f, 156.7f), V(-423.9f, 141.5f), V(-413f, 126.2f), V(-402.2f, 111f), V(-391.3f, 95.8f),
                            V(-387.3f, 88.2f), V(-385.7f, 79.7f), V(-386.6f, 71.2f), V(-389.8f, 63.2f), V(-399f, 47.5f), V(-408.2f, 31.7f), V(-417.4f, 15.9f), V(-426.6f, 0.2f),
                            V(-435.8f, -15.6f), V(-439.1f, -24f), V(-439.8f, -33f), V(-437.7f, -41.8f), V(-433.2f, -49.6f), V(-426.5f, -55.7f), V(-411.5f, -65.7f), V(-396.4f, -75.7f),
                            V(-381.4f, -85.7f), V(-366.4f, -95.8f), V(-351.3f, -105.8f), V(-341.1f, -110.7f), V(-329.9f, -112.5f), V(-318.6f, -111.1f), V(-308.2f, -106.6f), V(-291.8f, -96.5f),
                            V(-275.5f, -86.4f), V(-259.1f, -76.4f), V(-242.7f, -66.3f), V(-226.4f, -56.2f), V(-217.5f, -52.6f), V(-208f, -51.9f), V(-198.7f, -54.3f), V(-190.6f, -59.5f),
                            V(-184.6f, -66.9f), V(-175.4f, -83f), V(-166.2f, -99.1f), V(-157.1f, -115.2f), V(-147.9f, -131.2f), V(-138.7f, -147.3f), V(-129.5f, -163.4f), V(-122.2f, -173.1f),
                            V(-112.8f, -180.8f), V(-101.9f, -186f), V(-79f, -193.7f), V(-60.4f, -196.8f), V(-41.7f, -193.9f), V(-24.9f, -185.4f), V(-11.5f, -172.1f), V(-3f, -155.4f),
                            V(0f, -136.8f), V(0f, -117.2f), V(0f, -97.7f), V(0f, -78.1f), V(0f, -58.6f), V(0f, -39.1f), V(0f, -19.5f)),
                        groundColor = new Color(0.2f, 0.42f, 0.16f),
                        grassBandA = new Color(0.24f, 0.5f, 0.2f),
                        grassBandB = new Color(0.2f, 0.44f, 0.17f),
                        sunColor = new Color(1f, 0.88f, 0.66f),
                        sunEuler = new Vector3(44f, -55f, 0f),
                        sunIntensity = 1.4f,
                        ambientColor = new Color(0.55f, 0.58f, 0.62f),
                        fogColor = new Color(0.74f, 0.78f, 0.8f),
                        fogDensity = 0.0011f,
                        skyboxExr = "Skyboxes/sky_forest.hdr",
                    };
                case TrackId.CircuitoDoSol: // Inspirada em Suzuka (sem cruzamento): serpentário + alça longa
                    return new TrackDefinition
                    {
                        id = TrackId.CircuitoDoSol,
                        displayName = "Circuito do Sol",
                        sceneName = "Track_CircuitoDoSol",
                        targetLength = 2860f,
                        roadWidth = 12f,
                        controlPoints = Pts(
                            V(0f, 0f), V(0f, 19.6f), V(0f, 39.3f), V(0f, 58.9f), V(0f, 78.5f), V(0f, 98.1f), V(0f, 117.8f), V(0f, 137.4f),
                            V(0f, 157f), V(0f, 176.7f), V(0f, 196.3f), V(0f, 215.9f), V(0f, 235.6f), V(0f, 255.2f), V(0f, 274.8f), V(0f, 294.4f),
                            V(0f, 314.1f), V(0f, 333.7f), V(0f, 353.3f), V(0f, 373f), V(0f, 392.6f), V(0f, 412.2f), V(0f, 431.8f), V(1.6f, 443.1f),
                            V(6.3f, 453.4f), V(13.7f, 462f), V(29.3f, 475.6f), V(44.8f, 489.2f), V(60.4f, 502.9f), V(69.9f, 508.8f), V(80.7f, 511.4f), V(91.8f, 510.5f),
                            V(102f, 506.2f), V(120f, 495f), V(138f, 483.8f), V(148.2f, 479.5f), V(159.3f, 478.6f), V(170.1f, 481.2f), V(179.6f, 487.1f), V(193f, 498.9f),
                            V(206.4f, 510.6f), V(219.8f, 522.4f), V(229.5f, 528.4f), V(240.6f, 530.9f), V(251.9f, 529.8f), V(262.3f, 525.1f), V(281.2f, 512.6f), V(300f, 500f),
                            V(310.2f, 495.4f), V(321.2f, 494.2f), V(332.1f, 496.5f), V(341.8f, 502.2f), V(349.1f, 510.6f), V(364f, 534.5f), V(372.5f, 544.1f), V(383.6f, 550.6f),
                            V(396.2f, 553.2f), V(409f, 551.7f), V(427.6f, 546.4f), V(446.3f, 541f), V(465f, 535.7f), V(483.7f, 530.4f), V(498.3f, 523.5f), V(509.9f, 512.4f),
                            V(517.4f, 498.2f), V(520f, 482.3f), V(520f, 463.9f), V(520f, 445.6f), V(520f, 427.3f), V(520f, 408.9f), V(520f, 390.6f), V(521f, 384.1f),
                            V(523.8f, 378.2f), V(528.3f, 373.4f), V(542.6f, 361.9f), V(557f, 350.4f), V(571.4f, 338.9f), V(585.8f, 327.3f), V(600.2f, 315.8f), V(613.9f, 307.6f),
                            V(629.2f, 303.3f), V(645.1f, 303.1f), V(665f, 305.6f), V(684.9f, 308.1f), V(704.8f, 310.6f), V(724.7f, 313.1f), V(742.6f, 312.6f), V(759.5f, 306.9f),
                            V(774.1f, 296.4f), V(784.8f, 282.2f), V(790.9f, 265.3f), V(795.1f, 244.4f), V(799.3f, 223.5f), V(803.5f, 202.6f), V(807.7f, 181.7f), V(811.8f, 160.8f),
                            V(816f, 139.9f), V(816.3f, 131.4f), V(813.6f, 123.3f), V(808.5f, 116.6f), V(801.4f, 112f), V(783.1f, 104.2f), V(764.8f, 96.4f), V(746.6f, 88.5f),
                            V(728.3f, 80.7f), V(710f, 72.9f), V(691.8f, 65f), V(685.1f, 60.9f), V(680.1f, 54.8f), V(677.2f, 47.5f), V(672.9f, 28.2f), V(668.6f, 8.9f),
                            V(664.3f, -10.5f), V(660.1f, -29.8f), V(655.8f, -49.1f), V(650.3f, -67.4f), V(642.5f, -84.9f), V(632.3f, -101.1f), V(620f, -115.7f), V(605.8f, -128.6f),
                            V(590.1f, -139.4f), V(572.9f, -147.9f), V(555f, -155.4f), V(537.1f, -162.9f), V(519.2f, -170.3f), V(501.3f, -177.8f), V(483.4f, -185.3f), V(465.5f, -192.7f),
                            V(447.6f, -200.2f), V(429.6f, -207.6f), V(410.6f, -212.1f), V(391.1f, -210.2f), V(373.3f, -202.2f), V(357.6f, -191.7f), V(341.9f, -181.3f), V(326.2f, -170.8f),
                            V(310.5f, -160.3f), V(294.8f, -149.9f), V(279.1f, -139.4f), V(263.4f, -128.9f), V(257.1f, -126f), V(250.2f, -125.2f), V(243.5f, -126.6f), V(237.4f, -130f),
                            V(221.2f, -143.1f), V(204.9f, -156.1f), V(188.6f, -169.1f), V(172.3f, -182.2f), V(156f, -195.2f), V(149.6f, -198.8f), V(142.3f, -200f), V(121.8f, -200f),
                            V(101.4f, -200f), V(80.9f, -200f), V(60.5f, -200f), V(40f, -200f), V(27.6f, -198f), V(16.5f, -192.4f), V(7.6f, -183.5f), V(2f, -172.4f),
                            V(0f, -160f), V(0f, -140f), V(0f, -120f), V(0f, -100f), V(0f, -80f), V(0f, -60f), V(0f, -40f), V(0f, -20f)),
                        groundColor = new Color(0.24f, 0.4f, 0.18f),
                        grassBandA = new Color(0.28f, 0.46f, 0.22f),
                        grassBandB = new Color(0.24f, 0.4f, 0.19f),
                        sunColor = new Color(1f, 0.92f, 0.75f),
                        sunEuler = new Vector3(40f, -30f, 0f),
                        sunIntensity = 1.3f,
                        ambientColor = new Color(0.56f, 0.6f, 0.66f),
                        fogColor = new Color(0.73f, 0.79f, 0.85f),
                        fogDensity = 0.001f,
                        skyboxExr = "Skyboxes/sky_forest.hdr",
                    };
                default:
                    if (TrackLibraryExtra.TryGet(id, out var extra)) return extra;
                    return new TrackDefinition
                    {
                        id = TrackId.ForestCircuit,
                        displayName = "Circuito Verde",
                        sceneName = "Track_ForestCircuit",
                        targetLength = 1800f,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 125), new Vector3(0, 0, 250), new Vector3(30, 0, 330),
                            new Vector3(110, 0, 360), new Vector3(190, 0, 330), new Vector3(220, 0, 260), new Vector3(260, 0, 220),
                            new Vector3(330, 0, 230), new Vector3(380, 0, 290), new Vector3(450, 0, 300), new Vector3(500, 0, 250),
                            new Vector3(500, 0, 150), new Vector3(460, 0, 80), new Vector3(380, 0, 60), new Vector3(330, 0, 0),
                            new Vector3(300, 0, -80), new Vector3(220, 0, -120), new Vector3(130, 0, -110), new Vector3(60, 0, -75),
                            new Vector3(15, 0, -40)
                        },
                        groundColor = new Color(0.22f, 0.36f, 0.14f),
                        sunColor = new Color(1f, 0.93f, 0.8f),
                        sunEuler = new Vector3(40f, -35f, 0f),
                        sunIntensity = 1.3f,
                        ambientColor = new Color(0.55f, 0.62f, 0.7f),
                        fogColor = new Color(0.7f, 0.78f, 0.85f),
                        fogDensity = 0.0012f,
                        skyboxExr = "Skyboxes/sky_forest.hdr",
                    };
            }
        }
    }

    public class TrackSpline
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<Vector3> tangents = new List<Vector3>();
        public readonly List<Vector3> rights = new List<Vector3>();
        public float spacing;
        public float Length => points.Count * spacing;
        public int Count => points.Count;

        public static TrackSpline Build(Vector3[] control, float targetLength, float spacing = 2f)
        {
            var dense = new List<Vector3>();
            int n = control.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = control[(i - 1 + n) % n], p1 = control[i], p2 = control[(i + 1) % n], p3 = control[(i + 2) % n];
                for (int s = 0; s < 40; s++) dense.Add(Centripetal(p0, p1, p2, p3, s / 40f));
            }

            float rawLength = 0f;
            for (int i = 0; i < dense.Count; i++) rawLength += Vector3.Distance(dense[i], dense[(i + 1) % dense.Count]);
            float scale = targetLength > 0f ? targetLength / rawLength : 1f;
            for (int i = 0; i < dense.Count; i++) dense[i] = new Vector3(dense[i].x * scale, dense[i].y, dense[i].z * scale);

            var spline = new TrackSpline { spacing = spacing };
            float total = rawLength * scale;
            int count = Mathf.Max(8, Mathf.RoundToInt(total / spacing));
            spline.spacing = total / count;
            int seg = 0;
            float segStart = 0f;
            for (int k = 0; k < count; k++)
            {
                float d = k * spline.spacing;
                float segLen = Vector3.Distance(dense[seg], dense[(seg + 1) % dense.Count]);
                while (segStart + segLen < d && seg < dense.Count - 1)
                {
                    segStart += segLen;
                    seg++;
                    segLen = Vector3.Distance(dense[seg], dense[(seg + 1) % dense.Count]);
                }
                float t = segLen > 0f ? (d - segStart) / segLen : 0f;
                spline.points.Add(Vector3.Lerp(dense[seg], dense[(seg + 1) % dense.Count], t));
            }

            for (int i = 0; i < count; i++)
            {
                Vector3 tan = (spline.points[(i + 1) % count] - spline.points[(i - 1 + count) % count]).normalized;
                spline.tangents.Add(tan);
                spline.rights.Add(Vector3.Cross(Vector3.up, tan).normalized);
            }
            return spline;
        }

        static Vector3 Centripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Pow(Vector3.Distance(p0, p1), 0.5f);
            float t2 = t1 + Mathf.Pow(Vector3.Distance(p1, p2), 0.5f);
            float t3 = t2 + Mathf.Pow(Vector3.Distance(p2, p3), 0.5f);
            float tt = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        public int Wrap(int i) => ((i % Count) + Count) % Count;

        public Pose PoseAtDistance(float distance, float lateral = 0f)
        {
            int i = Wrap(Mathf.FloorToInt(distance / spacing));
            Vector3 pos = points[i] + rights[i] * lateral;
            return new Pose(pos, Quaternion.LookRotation(tangents[i], Vector3.up));
        }

        public int ClosestIndex(Vector3 position, int hint = -1, int window = 40)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            if (hint >= 0)
            {
                for (int o = -window; o <= window; o++)
                {
                    int i = Wrap(hint + o);
                    float d = (points[i] - position).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (bestDist < 30f * 30f) return best;
            }
            for (int i = 0; i < Count; i++)
            {
                float d = (points[i] - position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
