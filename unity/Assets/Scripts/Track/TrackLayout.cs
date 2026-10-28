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
                        targetLength = 2400f,
                        roadWidth = 13f,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 220), new Vector3(-20, 0, 330), new Vector3(30, 0, 420),
                            new Vector3(110, 0, 460), new Vector3(200, 0, 440), new Vector3(245, 0, 370), new Vector3(330, 0, 385),
                            new Vector3(420, 0, 430), new Vector3(530, 0, 420), new Vector3(610, 0, 350), new Vector3(640, 0, 250),
                            new Vector3(700, 0, 180), new Vector3(800, 0, 170), new Vector3(880, 0, 230), new Vector3(920, 0, 150),
                            new Vector3(880, 0, 40), new Vector3(760, 0, -30), new Vector3(590, 0, -60), new Vector3(420, 0, -55),
                            new Vector3(250, 0, -70), new Vector3(110, 0, -60)
                        },
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
                        targetLength = 1500f,
                        roadWidth = 10f,
                        kerbWidth = 0.8f,
                        runoffWidth = 3f,
                        runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(-10, 0, 140), new Vector3(40, 0, 230), new Vector3(130, 0, 250),
                            new Vector3(210, 0, 215), new Vector3(250, 0, 130), new Vector3(240, 0, 60), new Vector3(300, 0, 20),
                            new Vector3(390, 0, 40), new Vector3(440, 0, 120), new Vector3(430, 0, 210), new Vector3(470, 0, 290),
                            new Vector3(420, 0, 370), new Vector3(300, 0, 380), new Vector3(180, 0, 350), new Vector3(70, 0, 330),
                            new Vector3(-30, 0, 280), new Vector3(-60, 0, 180), new Vector3(-55, 0, 80)
                        },
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
                        targetLength = 2600f,
                        roadWidth = 13f,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 180), new Vector3(35, 0, 330), new Vector3(120, 0, 430),
                            new Vector3(250, 0, 470), new Vector3(370, 0, 430), new Vector3(430, 0, 330), new Vector3(560, 0, 300),
                            new Vector3(690, 0, 340), new Vector3(760, 0, 250), new Vector3(700, 0, 140), new Vector3(590, 0, 110),
                            new Vector3(560, 0, 20), new Vector3(640, 0, -60), new Vector3(560, 0, -160), new Vector3(420, 0, -180),
                            new Vector3(300, 0, -150), new Vector3(180, 0, -170), new Vector3(60, 0, -140), new Vector3(-20, 0, -80)
                        },
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
                        targetLength = 1900f,
                        roadWidth = 12f,
                        runoffSurface = SurfaceType.Sidewalk,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 160), new Vector3(70, 0, 240), new Vector3(190, 0, 250),
                            new Vector3(260, 0, 180), new Vector3(350, 0, 200), new Vector3(380, 0, 300), new Vector3(490, 0, 320),
                            new Vector3(560, 0, 240), new Vector3(540, 0, 130), new Vector3(590, 0, 40), new Vector3(520, 0, -40),
                            new Vector3(390, 0, -30), new Vector3(300, 0, -90), new Vector3(180, 0, -80), new Vector3(80, 0, -60)
                        },
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
                        targetLength = 2100f,
                        roadWidth = 13f,
                        runoffSurface = SurfaceType.SandDirt,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 170), new Vector3(60, 0, 280), new Vector3(170, 0, 310),
                            new Vector3(280, 0, 260), new Vector3(320, 0, 150), new Vector3(300, 0, 60), new Vector3(380, 0, 0),
                            new Vector3(490, 0, 30), new Vector3(560, 0, 120), new Vector3(660, 0, 90), new Vector3(700, 0, -10),
                            new Vector3(640, 0, -110), new Vector3(510, 0, -140), new Vector3(380, 0, -120), new Vector3(240, 0, -140),
                            new Vector3(110, 0, -110)
                        },
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
                        targetLength = 1600f,
                        roadWidth = 13f,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(-40, 0, 130), new Vector3(-120, 0, 190), new Vector3(-220, 0, 160),
                            new Vector3(-260, 0, 60), new Vector3(-220, 0, -40), new Vector3(-240, 0, -140), new Vector3(-160, 0, -220),
                            new Vector3(-40, 0, -210), new Vector3(60, 0, -250), new Vector3(170, 0, -220), new Vector3(220, 0, -120),
                            new Vector3(180, 0, -20), new Vector3(220, 0, 80), new Vector3(150, 0, 160), new Vector3(60, 0, 190),
                            new Vector3(20, 0, 100)
                        },
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
                        targetLength = 2300f,
                        roadWidth = 12f,
                        controlPoints = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 0, 150), new Vector3(50, 0, 250), new Vector3(140, 0, 280),
                            new Vector3(220, 0, 240), new Vector3(250, 0, 150), new Vector3(320, 0, 110), new Vector3(410, 0, 140),
                            new Vector3(450, 0, 230), new Vector3(540, 0, 260), new Vector3(630, 0, 220), new Vector3(670, 0, 120),
                            new Vector3(740, 0, 60), new Vector3(830, 0, 90), new Vector3(860, 0, 0), new Vector3(790, 0, -110),
                            new Vector3(650, 0, -160), new Vector3(500, 0, -130), new Vector3(360, 0, -170), new Vector3(220, 0, -150),
                            new Vector3(90, 0, -100)
                        },
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
