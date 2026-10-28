using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    public static class ForestDecoration
    {
        public static void Build(TrackBuilder track, float wallOffset)
        {
            var rng = new System.Random(1234);
            var root = new GameObject("Decoration").transform;
            root.SetParent(track.transform, false);

            var trunk = MaterialLibrary.Get("Trunk", new Color(0.2f, 0.12f, 0.06f), 0.2f);
            var trunkB = MaterialLibrary.Get("TrunkB", new Color(0.27f, 0.18f, 0.1f), 0.2f);
            var conifer = MaterialLibrary.Get("Conifer", new Color(0.08f, 0.25f, 0.1f), 0.1f);
            var leafy = MaterialLibrary.Get("Leafy", new Color(0.2f, 0.42f, 0.12f), 0.1f);
            var stone = MaterialLibrary.GetPbr("Stone", new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.1f);
            var bush = MaterialLibrary.Get("Bush", new Color(0.16f, 0.32f, 0.1f), 0.1f);
            var mountain = MaterialLibrary.Get("Mountain", new Color(0.32f, 0.38f, 0.42f), 0.05f);
            var stand = MaterialLibrary.Get("Grandstand", new Color(0.55f, 0.55f, 0.6f), 0.3f);
            var seats = MaterialLibrary.Get("Seats", new Color(0.1f, 0.3f, 0.7f), 0.3f);
            var board = MaterialLibrary.Get("Billboard", new Color(0.2f, 0.22f, 0.26f), 0.3f, 0f, Color.black);
            var flag = MaterialLibrary.Get("Flag", new Color(0.85f, 0.1f, 0.1f), 0.2f);

            var spline = track.Spline;
            int placed = 0;
            for (int attempt = 0; attempt < 3800 && placed < 630; attempt++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float dist = wallOffset + 6f + (float)rng.NextDouble() * 78f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * dist;
                if (!FarFromTrack(spline, pos, wallOffset + 3f)) continue;

                float scl = 0.8f + (float)rng.NextDouble() * 0.4f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float trunkH = (2f + (float)rng.NextDouble() * 4f) * scl;
                float trunkR = (0.12f + (float)rng.NextDouble() * 0.18f) * scl;
                var bark = rng.NextDouble() < 0.5 ? trunk : trunkB;
                track.Static(PrimitiveType.Cylinder, "Trunk", pos + Vector3.up * trunkH * 0.5f, yaw, new Vector3(trunkR, trunkH * 0.5f, trunkR), bark, false).transform.SetParent(root, true);
                int layers = 2 + rng.Next(2);
                float canopyH = trunkH * (0.75f + (float)rng.NextDouble() * 0.5f);
                float baseR = trunkR * (2.4f + (float)rng.NextDouble() * 1.6f);
                for (int layer = 0; layer < layers; layer++)
                {
                    float t = layers == 1 ? 0f : (float)layer / (layers - 1);
                    float r = baseR * Mathf.Lerp(1f, 0.45f, t);
                    float lh = canopyH / layers;
                    Vector3 c = pos + Vector3.up * (trunkH + t * (canopyH - lh) + lh * 0.5f);
                    var foliage = rng.NextDouble() < 0.5 ? conifer : leafy;
                    track.Static(PrimitiveType.Cylinder, "Canopy" + layer, c, yaw, new Vector3(r, lh * 0.5f, r), foliage, false).transform.SetParent(root, true);
                }
                if (rng.NextDouble() < 0.18)
                {
                    float sr = 0.35f + (float)rng.NextDouble() * 0.7f;
                    Vector3 sp = pos + new Vector3((float)(rng.NextDouble() - 0.5) * 2.6f, 0f, (float)(rng.NextDouble() - 0.5) * 2.6f);
                    track.Static(PrimitiveType.Capsule, "Stone", sp + Vector3.up * sr * 0.15f, yaw, new Vector3(sr, sr * 0.3f, sr), stone, false).transform.SetParent(root, true);
                }
                placed++;
            }

            for (int b = 0; b < 180; b++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * (wallOffset + 1.5f + (float)rng.NextDouble() * 3f);
                if (!FarFromTrack(spline, pos, wallOffset + 1f)) continue;
                float s = 0.7f + (float)rng.NextDouble() * 1.2f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                track.Static(PrimitiveType.Sphere, "Bush", pos + Vector3.up * s * 0.28f, yaw, new Vector3(s * 1.3f, s * 0.7f, s * 1.3f), bush, false).transform.SetParent(root, true);
            }

            Vector3 center = Vector3.zero;
            foreach (var p in spline.points) center += p;
            center /= spline.Count;
            for (int m = 0; m < 14; m++)
            {
                float a = m / 14f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                float r = 900f + (float)rng.NextDouble() * 250f;
                float h = 160f + (float)rng.NextDouble() * 200f;
                Vector3 pos = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                track.Static(PrimitiveType.Sphere, "Mountain", pos, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), new Vector3(520f, h * 2f, 380f), mountain, false).transform.SetParent(root, true);
            }

            Pose start = spline.PoseAtDistance(40f);
            Vector3 right = start.rotation * Vector3.right;
            Vector3 standPos = start.position - right * (wallOffset + 9f);
            for (int row = 0; row < 5; row++)
            {
                track.Static(PrimitiveType.Cube, "Stand" + row, standPos - right * row * 1.6f + Vector3.up * (0.6f + row * 0.8f), start.rotation, new Vector3(1.6f, 1.2f + row * 1.6f, 40f), row % 2 == 0 ? stand : seats, true).transform.SetParent(root, true);
            }
            track.Static(PrimitiveType.Cube, "StandRoof", standPos - right * 3.2f + Vector3.up * 7.5f, start.rotation * Quaternion.Euler(0f, 0f, -8f), new Vector3(10f, 0.3f, 42f), stand, false).transform.SetParent(root, true);

            for (int k = 0; k < 10; k++)
            {
                Pose p = spline.PoseAtDistance(spline.Length * k / 10f + 60f);
                Vector3 r = p.rotation * Vector3.right;
                float s = k % 2 == 0 ? 1f : -1f;
                track.Static(PrimitiveType.Cube, "Billboard" + k, p.position + r * s * (wallOffset + 1.2f) + Vector3.up * 1.6f, p.rotation, new Vector3(0.2f, 1.6f, 8f), board, true).transform.SetParent(root, true);
            }

            Pose sl = spline.PoseAtDistance(0f);
            for (int f = -3; f <= 3; f++)
            {
                if (f == 0) continue;
                Vector3 r = sl.rotation * Vector3.right;
                Vector3 pole = sl.position + r * Mathf.Sign(f) * (wallOffset + 2f) + sl.rotation * Vector3.forward * (Mathf.Abs(f) * 6f);
                track.Static(PrimitiveType.Cylinder, "FlagPole", pole + Vector3.up * 3f, Quaternion.identity, new Vector3(0.12f, 3f, 0.12f), stand, false).transform.SetParent(root, true);
                track.Static(PrimitiveType.Cube, "Flag", pole + Vector3.up * 5.4f + sl.rotation * Vector3.forward * 0.7f, sl.rotation, new Vector3(0.03f, 0.9f, 1.4f), flag, false).transform.SetParent(root, true);
            }
        }

        static bool FarFromTrack(TrackSpline spline, Vector3 pos, float minDist)
        {
            float minSq = minDist * minDist;
            for (int i = 0; i < spline.Count; i += 3)
            {
                if ((spline.points[i] - pos).sqrMagnitude < minSq) return false;
            }
            return true;
        }
    }
}
