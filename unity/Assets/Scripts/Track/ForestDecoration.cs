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
            var treeMats = TreeMaterials();   // null = sem foto/recorte no build → cilindros antigos
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
                if (treeMats != null)
                {
                    // 75% árvores pequenas (4,6 m no modelo, 1,3–2,2×) e 25% jacarandás (19,5 m, 0,45–0,7×)
                    bool big = rng.NextDouble() < 0.25;
                    int sp = big ? 1 : 0;
                    float k = big ? 0.45f + (float)rng.NextDouble() * 0.25f : 1.3f + (float)rng.NextDouble() * 0.9f;
                    PhotoTree(root, treeMats[sp], Species[sp], pos, yaw, k, "Tree" + placed);
                }
                else
                {
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

        // Árvores com FOTO (v99, pedido do usuário: "árvores realistas, reduzidas como os carros"): impostores renderizados no
        // Blender a partir dos modelos do Poly Haven (CC0; scratchpad trees/render_impostor.py — frente e lado, fundo
        // transparente, atlas 2048×1024 em Resources/Env/Trees/<nome>.png). Cada árvore = 2 planos cruzados (4 triângulos)
        // com recorte alfa: 630 árvores = 2.520 triângulos e 1 material por espécie (o jacarandá de 1,85 M triângulos
        // virou 4). size = lado do quadro em metros (maior dimensão do modelo); ground = fração do quadro abaixo da base.
        public static readonly (string name, float size, float ground)[] Species =
        {
            ("tree_small_02", 4.56f, 0.019f),    // 4,6 m de altura (medido no render: base na linha 1004 de 1024)
            ("jacaranda_tree", 24.42f, 0.116f),  // 24,4 m de largura × 19,5 m de altura (base na linha 904)
        };

        /// <summary>Um material de recorte por espécie (cópia de Resources/RaceX_Cutout, criado no build — a variante de
        /// recorte alfa do URP Lit só entra no APK por ele). null se faltar o material ou alguma foto.</summary>
        static Material[] TreeMaterials()
        {
            try
            {
                var tpl = Resources.Load<Material>("RaceX_Cutout");
                if (tpl == null) { Debug.Log("[Forest] sem material de recorte no build: árvores de cilindro"); return null; }
                var mats = new Material[Species.Length];
                for (int i = 0; i < Species.Length; i++)
                {
                    mats[i] = PhotoTreeMaterial(Species[i].name);
                    if (mats[i] == null) { Debug.Log("[Forest] sem foto " + Species[i].name + ": árvores de cilindro"); return null; }
                }
                Debug.Log("[Forest] árvores com foto: " + mats.Length + " espécies");
                return mats;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Forest] árvores com foto indisponíveis: " + ex.Message); return null; }
        }

        /// <summary>Material de recorte com a foto de uma espécie (null se faltar o material ou a foto no build).
        /// Também usado pela DesertDecoration (quiver tree).</summary>
        public static Material PhotoTreeMaterial(string name)
        {
            try
            {
                var tpl = Resources.Load<Material>("RaceX_Cutout");
                var tex = Resources.Load<Texture2D>("Env/Trees/" + name);
                if (tpl == null || tex == null) return null;
                var m = new Material(tpl) { name = "Tree_" + name };
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                return m;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Forest] foto " + name + ": " + ex.Message); return null; }
        }

        /// <summary>Dois planos cruzados (A no eixo x local, B no eixo z) com o atlas: metade esquerda = frente, direita = lado.
        /// Normais para cima: iluminação igual à do chão, sem face escura.</summary>
        public static void PhotoTree(Transform root, Material mat, (string name, float size, float ground) sp, Vector3 pos, Quaternion yaw, float k, string name)
        {
            float S = sp.size * k;
            float h = S * 0.5f;
            var go = new GameObject(name);
            go.transform.SetParent(root, true);
            go.transform.SetPositionAndRotation(pos - Vector3.up * (sp.ground * S), yaw);
            var mesh = new Mesh { name = "TreeCard" };
            var v = new Vector3[8]; var uv = new Vector2[8]; var n = new Vector3[8];
            v[0] = new Vector3(-h, 0f, 0f); v[1] = new Vector3(h, 0f, 0f); v[2] = new Vector3(h, S, 0f); v[3] = new Vector3(-h, S, 0f);
            v[4] = new Vector3(0f, 0f, -h); v[5] = new Vector3(0f, 0f, h); v[6] = new Vector3(0f, S, h); v[7] = new Vector3(0f, S, -h);
            uv[0] = new Vector2(0f, 0f); uv[1] = new Vector2(0.5f, 0f); uv[2] = new Vector2(0.5f, 1f); uv[3] = new Vector2(0f, 1f);
            uv[4] = new Vector2(0.5f, 0f); uv[5] = new Vector2(1f, 0f); uv[6] = new Vector2(1f, 1f); uv[7] = new Vector2(0.5f, 1f);
            for (int j = 0; j < 8; j++) n[j] = Vector3.up;
            mesh.vertices = v; mesh.uv = uv; mesh.normals = n;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
