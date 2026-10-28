using UnityEngine;

namespace RaceX.Track
{
    /// <summary>Calendário completo: 14 traçados extras inspirados em sedes reais da F1 (nomes originais).
    /// Total do jogo: 24 circuitos únicos — uma corrida cada, sem reuso.</summary>
    public static class TrackLibraryExtra
    {
        public static bool TryGet(TrackId id, out TrackDefinition d)
        {
            switch (id)
            {
                case TrackId.ParqueReal: // Melbourne: parque arborizado, curvas rápidas e muros
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Parque Real", sceneName = "Track_ParqueReal",
                        targetLength = 2100f, roadWidth = 12.5f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 180), V(60, 300), V(190, 340), V(310, 310), V(390, 220), V(510, 200),
                            V(610, 250), V(720, 230), V(770, 130), V(700, 40), V(560, -10), V(430, 30), V(310, -30),
                            V(180, -70), V(60, -50)),
                        groundColor = G(0.19f, 0.37f, 0.14f), sunEuler = E(40, -30), skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.72f, 0.78f, 0.85f), fogDensity = 0.0011f
                    };
                    return true;
                case TrackId.DragaoOriental: // Xangai: retão longo + gancho gigante de 360°
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Dragão Oriental", sceneName = "Track_DragaoOriental",
                        targetLength = 2500f, roadWidth = 13f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 260), V(20, 420), V(120, 520), V(280, 560), V(430, 520), V(500, 400),
                            V(430, 300), V(300, 290), V(260, 180), V(360, 110), V(500, 120), V(640, 160), V(760, 120),
                            V(810, 10), V(740, -100), V(580, -150), V(390, -140), V(200, -150), V(60, -110)),
                        groundColor = G(0.24f, 0.42f, 0.18f), sunEuler = E(42, -35), skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.75f, 0.8f, 0.86f), fogDensity = 0.0012f
                    };
                    return true;
                case TrackId.CornicheDaNoite: // Jeddah: rua noturna mais rápida, muros próximos
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Corniche da Noite", sceneName = "Track_CornicheDaNoite",
                        targetLength = 2300f, roadWidth = 11.5f, runoffWidth = 3f, runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0, 0), V(0, 240), V(30, 420), V(120, 540), V(270, 590), V(430, 570), V(560, 490),
                            V(620, 370), V(590, 250), V(660, 160), V(790, 140), V(880, 40), V(830, -90), V(680, -160),
                            V(500, -170), V(330, -200), V(170, -170), V(60, -100)),
                        groundColor = G(0.1f, 0.11f, 0.13f), asphaltColor = C(0.13f, 0.13f, 0.15f),
                        grassBandA = C(0.3f, 0.32f, 0.36f), grassBandB = C(0.26f, 0.28f, 0.32f),
                        sunColor = C(0.6f, 0.7f, 1f), sunEuler = E(36, 145), sunIntensity = 0.5f,
                        ambientColor = C(0.18f, 0.2f, 0.3f), fogColor = C(0.03f, 0.05f, 0.09f), fogDensity = 0.0015f,
                        skyboxExr = "Skyboxes/sky_city.hdr"
                    };
                    return true;
                case TrackId.PortoSul: // Miami: complexo de estádio + retas, entardecer
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Porto Sul", sceneName = "Track_PortoSul",
                        targetLength = 2000f, roadWidth = 12f, runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0, 0), V(0, 160), V(50, 280), V(160, 330), V(280, 300), V(340, 190), V(280, 90),
                            V(350, 10), V(470, 0), V(560, 70), V(680, 60), V(740, 160), V(690, 260), V(560, 300),
                            V(430, 330), V(300, 390), V(150, 370), V(40, 300), V(-20, 180)),
                        groundColor = G(0.26f, 0.34f, 0.3f), sunEuler = E(30, -60), sunColor = C(1f, 0.75f, 0.5f),
                        sunIntensity = 1.2f, skyboxExr = "Skyboxes/sky_city.hdr",
                        fogColor = C(0.85f, 0.7f, 0.6f), fogDensity = 0.001f
                    };
                    return true;
                case TrackId.IlhaDasCascatas: // Montreal: ilha com muros, chicanes e o Muro dos Campeões
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Ilha das Cascatas", sceneName = "Track_IlhaDasCascatas",
                        targetLength = 1800f, roadWidth = 11.5f, runoffWidth = 3.5f, runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0, 0), V(0, 190), V(70, 300), V(190, 330), V(300, 280), V(360, 170), V(470, 150),
                            V(560, 210), V(670, 190), V(720, 90), V(660, -20), V(530, -60), V(400, -40), V(280, -80),
                            V(150, -90), V(50, -60)),
                        groundColor = G(0.2f, 0.36f, 0.2f), sunEuler = E(38, -25), skyboxExr = "Skyboxes/sky_city.hdr",
                        fogColor = C(0.75f, 0.8f, 0.86f), fogDensity = 0.0012f
                    };
                    return true;
                case TrackId.ValeDourado: // Barcelona: traçado equilibrado com curvão final
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Vale Dourado", sceneName = "Track_ValeDourado",
                        targetLength = 2200f, roadWidth = 13f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 170), V(50, 300), V(160, 370), V(300, 360), V(390, 270), V(520, 250),
                            V(620, 310), V(740, 290), V(790, 180), V(730, 70), V(600, 30), V(560, -70), V(440, -120),
                            V(300, -100), V(160, -130), V(50, -100)),
                        groundColor = G(0.32f, 0.4f, 0.2f), sunEuler = E(45, -40), sunColor = C(1f, 0.87f, 0.65f),
                        sunIntensity = 1.4f, skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.84f, 0.79f, 0.68f), fogDensity = 0.001f
                    };
                    return true;
                case TrackId.ColinasVerdes: // Red Bull Ring: curta, poucas curvas, declividade
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Colinas Verdes", sceneName = "Track_ColinasVerdes",
                        targetLength = 1400f, roadWidth = 13.5f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 200), V(-40, 380), V(60, 500), V(220, 530), V(340, 460), V(400, 340),
                            V(350, 210), V(430, 110), V(560, 80), V(650, -20), V(590, -140), V(430, -190), V(250, -170),
                            V(100, -120)),
                        groundColor = G(0.2f, 0.42f, 0.16f), sunEuler = E(40, -20), skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.74f, 0.8f, 0.87f), fogDensity = 0.001f
                    };
                    return true;
                case TrackId.CampoDoNorte: // Silverstone: curvas de alta velocidade em sequência
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Campo do Norte", sceneName = "Track_CampoDoNorte",
                        targetLength = 2300f, roadWidth = 13f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 190), V(70, 340), V(200, 420), V(340, 400), V(420, 300), V(560, 290),
                            V(680, 350), V(800, 320), V(850, 200), V(790, 90), V(660, 40), V(620, -60), V(700, -150),
                            V(600, -230), V(440, -220), V(290, -240), V(140, -190), V(40, -110)),
                        groundColor = G(0.22f, 0.38f, 0.17f), sunEuler = E(42, -30), skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.73f, 0.79f, 0.85f), fogDensity = 0.0011f
                    };
                    return true;
                case TrackId.CurvasDoDanubio: // Hungaroring: travado, "Mônaco sem muros"
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Curvas do Danúbio", sceneName = "Track_CurvasDoDanubio",
                        targetLength = 1600f, roadWidth = 12f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 140), V(70, 230), V(170, 200), V(210, 110), V(310, 80), V(400, 130),
                            V(420, 230), V(520, 260), V(590, 180), V(550, 80), V(590, -20), V(490, -90), V(350, -70),
                            V(220, -110), V(100, -90)),
                        groundColor = G(0.28f, 0.42f, 0.2f), sunEuler = E(45, -35), sunColor = C(1f, 0.88f, 0.68f),
                        sunIntensity = 1.35f, skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.82f, 0.8f, 0.74f), fogDensity = 0.0011f
                    };
                    return true;
                case TrackId.DunasDoNorte: // Zandvoort: litoral, curvas com "bancos" (largas e rápidas)
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Dunas do Norte", sceneName = "Track_DunasDoNorte",
                        targetLength = 1500f, roadWidth = 13f,
                        controlPoints = Pts(
                            V(0, 0), V(-30, 160), V(30, 300), V(150, 370), V(290, 350), V(380, 260), V(370, 150),
                            V(460, 80), V(580, 110), V(640, 20), V(570, -90), V(430, -130), V(280, -120), V(120, -140),
                            V(20, -90)),
                        groundColor = G(0.42f, 0.46f, 0.3f), grassBandA = C(0.5f, 0.52f, 0.34f), grassBandB = C(0.44f, 0.46f, 0.3f),
                        sunEuler = E(38, -30), sunColor = C(0.95f, 0.92f, 0.85f), skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.78f, 0.82f, 0.86f), fogDensity = 0.0013f
                    };
                    return true;
                case TrackId.Fortaleza: // Baku: castela travada + retão de 2 km
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Fortaleza Antiga", sceneName = "Track_Fortaleza",
                        targetLength = 2200f, roadWidth = 10.5f, runoffWidth = 3f, runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0, 0), V(0, 300), V(-20, 560), V(60, 700), V(210, 740), V(330, 670), V(350, 540),
                            V(280, 450), V(330, 350), V(450, 330), V(520, 230), V(470, 120), V(360, 90), V(300, -10),
                            V(180, -40), V(70, -30)),
                        groundColor = G(0.3f, 0.3f, 0.33f), grassBandA = C(0.34f, 0.35f, 0.38f), grassBandB = C(0.3f, 0.31f, 0.34f),
                        sunEuler = E(42, -40), sunColor = C(1f, 0.85f, 0.6f), sunIntensity = 1.3f,
                        skyboxExr = "Skyboxes/sky_city.hdr", fogColor = C(0.8f, 0.78f, 0.76f), fogDensity = 0.0011f
                    };
                    return true;
                case TrackId.ColinasDoTexas: // Austin: complexo de S rápido na subida
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Colinas do Texas", sceneName = "Track_ColinasDoTexas",
                        targetLength = 2400f, roadWidth = 13f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 170), V(60, 300), V(180, 350), V(290, 320), V(330, 210), V(260, 110),
                            V(330, 20), V(460, 10), V(570, 80), V(700, 60), V(790, 140), V(760, 260), V(640, 320),
                            V(600, 430), V(470, 480), V(320, 450), V(180, 470), V(60, 430), V(-20, 320), V(-30, 160)),
                        groundColor = G(0.3f, 0.36f, 0.22f), sunEuler = E(46, -50), sunColor = C(1f, 0.86f, 0.62f),
                        sunIntensity = 1.4f, skyboxExr = "Skyboxes/sky_forest.hdr",
                        fogColor = C(0.85f, 0.78f, 0.64f), fogDensity = 0.001f
                    };
                    return true;
                case TrackId.EstadioCentral: // México: retão gigante + forquilha do estádio
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Estádio Central", sceneName = "Track_EstadioCentral",
                        targetLength = 2000f, roadWidth = 13.5f,
                        controlPoints = Pts(
                            V(0, 0), V(0, 400), V(-10, 640), V(90, 760), V(250, 790), V(370, 720), V(400, 590),
                            V(330, 490), V(390, 380), V(520, 360), V(620, 270), V(570, 150), V(430, 120), V(320, 60),
                            V(180, 30), V(60, -20)),
                        groundColor = G(0.36f, 0.4f, 0.26f), sunEuler = E(48, -35), sunColor = C(1f, 0.9f, 0.7f),
                        sunIntensity = 1.45f, skyboxExr = "Skyboxes/sky_city.hdr",
                        fogColor = C(0.86f, 0.82f, 0.7f), fogDensity = 0.001f
                    };
                    return true;
                case TrackId.NeonStrip: // Las Vegas: faixa neon noturna, retões longos
                    d = new TrackDefinition
                    {
                        id = id, displayName = "Faixa de Neon", sceneName = "Track_NeonStrip",
                        targetLength = 2400f, roadWidth = 12.5f, runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = Pts(
                            V(0, 0), V(0, 300), V(0, 620), V(60, 800), V(200, 860), V(350, 820), V(420, 700),
                            V(390, 570), V(460, 460), V(590, 440), V(690, 520), V(810, 490), V(850, 360), V(780, 240),
                            V(640, 210), V(560, 110), V(420, 60), V(260, 80), V(110, 40)),
                        groundColor = G(0.1f, 0.1f, 0.12f), asphaltColor = C(0.13f, 0.13f, 0.16f),
                        grassBandA = C(0.32f, 0.3f, 0.4f), grassBandB = C(0.28f, 0.26f, 0.36f),
                        sunColor = C(0.85f, 0.6f, 1f), sunEuler = E(35, 160), sunIntensity = 0.55f,
                        ambientColor = C(0.22f, 0.16f, 0.32f), fogColor = C(0.05f, 0.03f, 0.09f), fogDensity = 0.0014f,
                        skyboxExr = "Skyboxes/sky_city.hdr"
                    };
                    return true;
                default:
                    d = null;
                    return false;
            }
        }

        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
        static Vector3[] Pts(params Vector3[] p) => p;
        static Color C(float r, float g, float b) => new Color(r, g, b);
        static Color G(float r, float g, float b) => new Color(r, g, b);
        static Vector3 E(float x, float y) => new Vector3(x, y, 0f);
    }
}
