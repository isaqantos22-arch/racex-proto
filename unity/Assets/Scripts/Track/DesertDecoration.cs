using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    public static class DesertDecoration
    {
        public static void Build(TrackBuilder track, float wallOffset)
        {
            var rng = new System.Random(4321);
            var root = new GameObject("Decoration").transform;
            root.SetParent(track.transform, false);

            var mesaA = MaterialLibrary.Get("MesaA", new Color(0.62f, 0.33f, 0.2f), 0.05f);
            var mesaB = MaterialLibrary.Get("MesaB", new Color(0.7f, 0.4f, 0.25f), 0.05f);
            var sandstone = MaterialLibrary.Get("Sandstone", new Color(0.78f, 0.6f, 0.4f), 0.1f);
            var cactus = MaterialLibrary.Get("Cactus", new Color(0.12f, 0.34f, 0.14f), 0.15f);
            var dryBush = MaterialLibrary.Get("DryBush", new Color(0.42f, 0.35f, 0.18f), 0.15f);
            var wood = MaterialLibrary.Get("Wood", new Color(0.5f, 0.38f, 0.22f), 0.2f);
            var stand = MaterialLibrary.Get("D_Stand", new Color(0.55f, 0.55f, 0.6f), 0.3f);
            var seats = MaterialLibrary.Get("D_Seats", new Color(0.1f, 0.3f, 0.7f), 0.3f);
            var board = MaterialLibrary.Get("D_Board", new Color(0.95f, 0.75f, 0.1f), 0.4f, 0f, new Color(0.3f, 0.22f, 0f));
            var flag = MaterialLibrary.Get("D_Flag", new Color(0.85f, 0.1f, 0.1f), 0.2f);

            var spline = track.Spline;
            int placed = 0;
            for (int attempt = 0; attempt < 2400 && placed < 55; attempt++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float dist = wallOffset + 16f + (float)rng.NextDouble() * 94f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * dist;
                if (!FarFromTrack(spline, pos, wallOffset + 8f)) continue;

                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                int layers = 2 + rng.Next(2);
                float totalH = 8f + (float)rng.NextDouble() * 14f;
                float r = 9f + (float)rng.NextDouble() * 11f;
                float y = pos.y;
                for (int layer = 0; layer < layers; layer++)
                {
                    float h = totalH / layers * (0.8f + (float)rng.NextDouble() * 0.4f);
                    var mat = rng.NextDouble() < 0.5 ? mesaA : mesaB;
                    track.Static(PrimitiveType.Cylinder, "Mesa" + placed + "_" + layer, new Vector3(pos.x, y + h * 0.5f, pos.z), yaw, new Vector3(r, h * 0.5f, r), mat, false).transform.SetParent(root, true);
                    y += h;
                    r *= 0.62f + (float)rng.NextDouble() * 0.16f;
                }
                placed++;
            }

            for (int c = 0; c < 70; c++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float dist = wallOffset + 3f + (float)rng.NextDouble() * 57f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * dist;
                if (!FarFromTrack(spline, pos, wallOffset + 2f)) continue;

                float bodyR = 0.22f + (float)rng.NextDouble() * 0.18f;
                float bodyH = 2f + (float)rng.NextDouble() * 2.5f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                track.Static(PrimitiveType.Cylinder, "Cactus" + c, pos + Vector3.up * bodyH * 0.5f, yaw, new Vector3(bodyR, bodyH * 0.5f, bodyR), cactus, false).transform.SetParent(root, true);
                int arms = 1 + rng.Next(2);
                for (int a = 0; a < arms; a++)
                {
                    float armR = bodyR * 0.55f;
                    float armH = bodyH * (0.4f + (float)rng.NextDouble() * 0.15f);
                    float armYaw = (float)rng.NextDouble() * 360f;
                    float baseH = bodyH * (0.35f + (float)rng.NextDouble() * 0.25f);
                    Vector3 offset = Quaternion.Euler(0f, armYaw, 0f) * Vector3.forward * (bodyR + armR + 0.15f);
                    track.Static(PrimitiveType.Cylinder, "CactusArm" + c + "_" + a, pos + offset + Vector3.up * (baseH + armH * 0.5f), Quaternion.Euler(0f, armYaw, 0f), new Vector3(armR, armH * 0.5f, armR), cactus, false).transform.SetParent(root, true);
                }
            }

            for (int b = 0; b < 60; b++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * (wallOffset + 2f + (float)rng.NextDouble() * 28f);
                if (!FarFromTrack(spline, pos, wallOffset + 1.5f)) continue;
                float s = 0.5f + (float)rng.NextDouble() * 0.8f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                track.Static(PrimitiveType.Sphere, "Bush" + b, pos + Vector3.up * s * 0.25f, yaw, new Vector3(s, s * 0.5f, s), dryBush, false).transform.SetParent(root, true);
            }

            for (int st = 0; st < 40; st++)
            {
                int i = rng.Next(spline.Count);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 pos = spline.points[i] + spline.rights[i] * side * (wallOffset + 4f + (float)rng.NextDouble() * 66f);
                if (!FarFromTrack(spline, pos, wallOffset + 2f)) continue;
                float s = 0.4f + (float)rng.NextDouble() * 1.8f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                track.Static(PrimitiveType.Sphere, "Stone" + st, pos + Vector3.up * s * 0.1f, yaw, new Vector3(s, s * 0.45f, s), sandstone, false).transform.SetParent(root, true);
            }

            Vector3 center = Vector3.zero;
            foreach (var p in spline.points) center += p;
            center /= spline.Count;
            for (int m = 0; m < 14; m++)
            {
                float a = m / 14f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                float r = 950f + (float)rng.NextDouble() * 300f;
                float h = 90f + (float)rng.NextDouble() * 110f;
                float w = 280f + (float)rng.NextDouble() * 170f;
                Vector3 pos = center + new Vector3(Mathf.Cos(a) * r, h * 0.5f, Mathf.Sin(a) * r);
                track.Static(PrimitiveType.Cylinder, "CanyonWall" + m, pos, Quaternion.identity, new Vector3(w, h * 0.5f, w), m % 2 == 0 ? mesaA : mesaB, false).transform.SetParent(root, true);
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
                track.Static(PrimitiveType.Cylinder, "FlagPole", pole + Vector3.up * 3f, Quaternion.identity, new Vector3(0.12f, 3f, 0.12f), wood, false).transform.SetParent(root, true);
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
