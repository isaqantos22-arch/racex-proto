using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Track
{
    public enum TrackId { ForestCircuit = 0, DesertCanyon = 1, NightCity = 2 }

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
            TrackId.ForestCircuit, TrackId.DesertCanyon, TrackId.NightCity
        };

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
                default:
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
