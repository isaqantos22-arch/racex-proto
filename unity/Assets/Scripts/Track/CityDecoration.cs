using System.Collections.Generic;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    /// <summary>Cidade (v99): prédios com FACHADA FOTOGRÁFICA (ambientCG, CC0, Resources/Env/Facades) em caixas com UV por face
    /// (1 repetição da textura = Tile metros, para as janelas saírem no tamanho real); à noite as janelas acendem pelo mapa de
    /// emissão. Sem textura no build cai na cor lisa antiga. Letreiros neon só à noite; postes de rua só de dia (à noite o
    /// NightLighting põe os postes com luz de verdade).</summary>
    public static class CityDecoration
    {
        // id ambientCG, metros por repetição da textura, usa de dia, usa à noite (as "night" são torres com janelas acesas)
        static readonly (string id, float tile, bool day, bool night)[] FacadeSet =
        {
            ("Facade001", 14f, true, false), ("Facade003", 14f, true, true), ("Facade005", 14f, true, false),
            ("Facade018A", 20f, true, true), ("Facade019A", 20f, true, true), ("Facade020A", 20f, true, true),
            ("Facade009", 30f, false, true), ("Facade011", 30f, false, true), ("Facade013", 36f, false, true), ("Facade016", 36f, false, true),
        };

        struct Facade { public Material mat; public float tile; }

        public static void Build(TrackBuilder track, float wallOffset)
        {
            var rng = new System.Random(999);
            var root = new GameObject("Decoration").transform;
            root.SetParent(track.transform, false);
            bool night = NightLighting.IsNight(track.Definition);

            var facadeA = MaterialLibrary.Get("C_FacadeA", new Color(0.07f, 0.075f, 0.09f));
            var facadeB = MaterialLibrary.Get("C_FacadeB", new Color(0.1f, 0.1f, 0.12f));
            var facadeC = MaterialLibrary.Get("C_FacadeC", new Color(0.14f, 0.13f, 0.14f));
            var winWarm = MaterialLibrary.Get("C_WinWarm", new Color(1f, 0.82f, 0.5f), 0.3f, 0f, new Color(0.9f, 0.7f, 0.35f));
            var neonCyan = MaterialLibrary.Get("C_NeonCyan", new Color(0.2f, 0.9f, 1f), 0.3f, 0f, new Color(0.2f, 1.2f, 1.4f));
            var neonMagenta = MaterialLibrary.Get("C_NeonMag", new Color(1f, 0.25f, 0.6f), 0.3f, 0f, new Color(1.4f, 0.2f, 0.7f));
            var lampHead = MaterialLibrary.Get("C_Lamp", new Color(1f, 0.9f, 0.7f), 0.3f, 0f, new Color(1.1f, 0.95f, 0.6f));
            var pole = MaterialLibrary.Get("C_Pole", new Color(0.05f, 0.05f, 0.06f), 0.5f);
            var concrete = MaterialLibrary.Get("C_Concrete", new Color(0.25f, 0.25f, 0.27f), 0.2f);
            var facades = LoadFacades(night);
            Debug.Log("[City] fachadas com foto: " + facades.Count + (night ? " (noite)" : " (dia)"));

            Facade Pick()
            {
                if (facades.Count > 0) return facades[rng.Next(facades.Count)];
                var plain = rng.NextDouble() < 0.5 ? facadeA : rng.NextDouble() < 0.5 ? facadeB : facadeC;
                return new Facade { mat = plain, tile = 0f };
            }

            var spline = track.Spline;
            for (float d = 0f; d < spline.Length; d += 30f)
            {
                Pose p = spline.PoseAtDistance(d);
                for (int side = 0; side < 2; side++)
                {
                    if (rng.NextDouble() < 0.3) continue;
                    float s = side == 0 ? 1f : -1f;
                    float off = wallOffset + 16f + (float)rng.NextDouble() * 34f;
                    Vector3 pos = p.position + p.rotation * Vector3.right * s * off;
                    if (!FarFromTrack(spline, pos, wallOffset + 10f)) continue;

                    float w = 10f + (float)rng.NextDouble() * 14f;
                    float dep = 10f + (float)rng.NextDouble() * 14f;
                    float h = 14f + (float)rng.NextDouble() * 46f;
                    var facade = Pick();
                    Box(track, root, "Building" + Mathf.RoundToInt(d) + "_" + side, pos + Vector3.up * h * 0.5f, p.rotation, new Vector3(dep, h, w), facade);

                    // sorteios mantidos na mesma ordem da versão antiga (semente 999) para a cidade não mudar de lugar
                    int wins = 2 + rng.Next(3);
                    rng.NextDouble();
                    for (int ww = 0; ww < wins; ww++) { rng.Next(4); rng.NextDouble(); }

                    if (rng.NextDouble() < 0.25)
                    {
                        var neon = rng.NextDouble() < 0.5 ? neonCyan : neonMagenta;
                        float nOff = (float)(rng.NextDouble() - 0.5) * 2f * w * 0.3f;
                        if (night)
                        {
                            Vector3 neonPos = pos - p.rotation * Vector3.right * s * (dep * 0.5f + 0.12f) + p.rotation * Vector3.forward * nOff + Vector3.up * h * 0.5f;
                            track.Static(PrimitiveType.Cube, "Neon" + Mathf.RoundToInt(d) + "_" + side, neonPos, p.rotation, new Vector3(0.15f, h * 0.55f, 0.35f), neon, false).transform.SetParent(root, true);
                        }
                    }

                    float roofH = 0f;
                    if (rng.NextDouble() < 0.4)
                    {
                        float rs = 3f + (float)rng.NextDouble() * 3f;
                        roofH = rs;
                        track.Static(PrimitiveType.Cube, "Roof" + Mathf.RoundToInt(d) + "_" + side, pos + Vector3.up * (h + rs * 0.4f), p.rotation, new Vector3(rs, rs, rs), concrete, false).transform.SetParent(root, true);
                    }
                    if (rng.NextDouble() < 0.2)
                    {
                        float ah = 4f + (float)rng.NextDouble() * 5f;
                        track.Static(PrimitiveType.Cylinder, "Antenna" + Mathf.RoundToInt(d) + "_" + side, pos + Vector3.up * (h + roofH + ah * 0.5f), Quaternion.identity, new Vector3(0.15f, ah * 0.5f, 0.15f), concrete, false).transform.SetParent(root, true);
                    }
                }
            }

            if (!night)
            {
                int light = 0;
                for (float d = 0f; d < spline.Length; d += 25f)
                {
                    Pose p = spline.PoseAtDistance(d);
                    float s = light % 2 == 0 ? 1f : -1f;
                    Vector3 basePos = p.position + p.rotation * Vector3.right * s * (wallOffset + 0.8f);
                    track.Static(PrimitiveType.Cylinder, "Pole" + light, basePos + Vector3.up * 3f, p.rotation, new Vector3(0.08f, 3f, 0.08f), pole, false).transform.SetParent(root, true);
                    track.Static(PrimitiveType.Cube, "Arm" + light, basePos - p.rotation * Vector3.right * s * 1.0f + Vector3.up * 5.9f, p.rotation, new Vector3(2.4f, 0.12f, 0.12f), pole, false).transform.SetParent(root, true);
                    track.Static(PrimitiveType.Cube, "Lamp" + light, basePos - p.rotation * Vector3.right * s * 2.2f + Vector3.up * 5.9f, p.rotation, new Vector3(0.9f, 0.18f, 0.45f), lampHead, false).transform.SetParent(root, true);
                    light++;
                }
            }

            Vector3 center = Vector3.zero;
            foreach (var p in spline.points) center += p;
            center /= spline.Count;
            for (int b = 0; b < 26; b++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 750f + (float)rng.NextDouble() * 200f;
                Vector3 pos = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float w = 30f + (float)rng.NextDouble() * 30f;
                float dep = 20f + (float)rng.NextDouble() * 20f;
                float h = 80f + (float)rng.NextDouble() * 140f;
                var facade = Pick();
                Quaternion rot = Quaternion.LookRotation((center - pos).normalized, Vector3.up);
                Box(track, root, "Sky" + b, pos + Vector3.up * h * 0.5f, rot, new Vector3(w, h, dep), facade);
                if (rng.NextDouble() < 0.6)
                {
                    var neon = rng.NextDouble() < 0.5 ? neonCyan : neonMagenta;
                    if (night)
                    {
                        Vector3 stripPos = pos + rot * new Vector3(0f, h * 0.5f, dep * 0.5f - 0.15f);
                        track.Static(PrimitiveType.Cube, "SkyNeon" + b, stripPos, rot, new Vector3(3f, h * 0.5f, 0.3f), neon, false).transform.SetParent(root, true);
                    }
                }
            }

            Material[] billboardMats = { neonCyan, neonMagenta, winWarm };
            for (int k = 0; k < 8; k++)
            {
                Pose p = spline.PoseAtDistance(spline.Length * k / 8f + 60f);
                Vector3 r = p.rotation * Vector3.right;
                float s = k % 2 == 0 ? 1f : -1f;
                Vector3 basePos = p.position + r * s * (wallOffset + 2.5f);
                track.Static(PrimitiveType.Cube, "Billboard" + k, basePos + Vector3.up * 5.5f, p.rotation, new Vector3(0.2f, 4f, 8f), billboardMats[k % 3], true).transform.SetParent(root, true);
                track.Static(PrimitiveType.Cylinder, "BoardPoleA" + k, basePos + p.rotation * Vector3.forward * -3.4f + Vector3.up * 1.75f, Quaternion.identity, new Vector3(0.15f, 1.75f, 0.15f), pole, false).transform.SetParent(root, true);
                track.Static(PrimitiveType.Cylinder, "BoardPoleB" + k, basePos + p.rotation * Vector3.forward * 3.4f + Vector3.up * 1.75f, Quaternion.identity, new Vector3(0.15f, 1.75f, 0.15f), pole, false).transform.SetParent(root, true);
            }
        }

        /// <summary>Materiais das fachadas disponíveis no build para dia ou noite (à noite com o mapa de emissão = janelas acesas).</summary>
        static List<Facade> LoadFacades(bool night)
        {
            var list = new List<Facade>();
            foreach (var f in FacadeSet)
            {
                if (night ? !f.night : !f.day) continue;
                try
                {
                    var tex = Resources.Load<Texture2D>("Env/Facades/" + f.id + "_Color");
                    if (tex == null) continue;
                    // À noite o material nasce do modelo EMISSIVO (RaceX_LitEmissive, variante _EMISSION garantida no build) e
                    // recebe o mapa de janelas acesas; de dia é o material comum com a foto.
                    var em = night ? Resources.Load<Texture2D>("Env/Facades/" + f.id + "_Emission") : null;
                    var m = MaterialLibrary.Get("C_" + f.id + (night ? "_N" : "_D"), Color.white * (night ? 0.75f : 0.95f), 0.3f, 0f,
                        em != null ? (Color?)new Color(1.3f, 1.2f, 1.0f) : null, tex);
                    if (m == null) continue;
                    if (em != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", em);
                    list.Add(new Facade { mat = m, tile = f.tile });
                }
                catch (System.Exception ex) { Debug.LogWarning("[City] fachada " + f.id + ": " + ex.Message); }
            }
            return list;
        }

        /// <summary>Caixa com a fachada: o UV de cada face é escalado pelo tamanho real (largura/tile, altura/tile). O cubo do
        /// Unity tem u ao longo de x nas faces ±z, ao longo de z nas faces ±x, e v para cima; sem tile (cor lisa) fica o cubo normal.
        /// size = escala local (x = profundidade, y = altura, z = largura).</summary>
        static GameObject Box(TrackBuilder track, Transform root, string name, Vector3 center, Quaternion rot, Vector3 size, Facade f)
        {
            var go = track.Static(PrimitiveType.Cube, name, center, rot, size, f.mat, false);
            go.transform.SetParent(root, true);
            if (f.tile <= 0f) return go;
            try
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return go;
                var mesh = Object.Instantiate(mf.sharedMesh);
                mesh.name = "FacadeBox";
                var uv = mesh.uv; var nrm = mesh.normals;
                for (int i = 0; i < uv.Length && i < nrm.Length; i++)
                {
                    Vector3 n = nrm[i];
                    float su, sv;
                    if (Mathf.Abs(n.x) > 0.5f) { su = size.z / f.tile; sv = size.y / f.tile; }
                    else if (Mathf.Abs(n.z) > 0.5f) { su = size.x / f.tile; sv = size.y / f.tile; }
                    else { su = size.x / f.tile; sv = size.z / f.tile; }
                    uv[i] = new Vector2(uv[i].x * su, uv[i].y * sv);
                }
                mesh.uv = uv;
                mf.sharedMesh = mesh;
            }
            catch (System.Exception ex) { Debug.LogWarning("[City] UV da fachada: " + ex.Message); }
            return go;
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
