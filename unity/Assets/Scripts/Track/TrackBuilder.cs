using System.Collections.Generic;
using RaceX.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaceX.Track
{
    public class TrackBuilder : MonoBehaviour
    {
        public const int GridSlots = 24;
        public const float CheckpointSpacing = 150f;
        public const float WaypointSpacing = 10f;

        public TrackDefinition Definition { get; private set; }
        public TrackSpline Spline { get; private set; }
        public WaypointSystem Waypoints { get; private set; }
        public CheckpointManager Checkpoints { get; private set; }

        /// <summary>Início (m ao longo do spline) da reta escolhida para o pit; -1 = não planejado.</summary>
        public float PitStart { get; private set; } = -1f;
        /// <summary>Comprimento da reta de baixa curvatura que começa em PitStart (m).</summary>
        public float PitRun { get; private set; }
        /// <summary>Comprimento máximo reservado ao pit (corredor + garagens) no planejamento.</summary>
        public const float PitPlanLength = 300f;
        readonly List<Vector3> pitZoneC = new List<Vector3>();
        readonly List<Vector3> pitZoneR = new List<Vector3>();
        readonly List<Vector3> pitZoneF = new List<Vector3>();
        readonly List<float> pitZoneRel = new List<float>();

        public static TrackBuilder Build(TrackDefinition def)
        {
            var go = new GameObject("Track_" + def.id);
            var builder = go.AddComponent<TrackBuilder>();
            builder.Generate(def);
            return builder;
        }

        void Generate(TrackDefinition def)
        {
            Definition = def;
            Spline = TrackSpline.Build(def.controlPoints, def.targetLength);

            float half = def.roadWidth * 0.5f;
            var road = MaterialLibrary.GetPbr("Asphalt_" + def.id, def.asphaltColor, 0f, def.asphaltSmoothness);
            var roadDark = MaterialLibrary.GetPbr("AsphaltDark_" + def.id, def.asphaltColor * 0.96f, 0f, def.asphaltSmoothness);
            var kerbRed = MaterialLibrary.Get("KerbRed", new Color(0.8f, 0.08f, 0.08f), 0.4f);
            var kerbWhite = MaterialLibrary.Get("KerbWhite", new Color(0.92f, 0.92f, 0.92f), 0.4f);
            var line = MaterialLibrary.Get("RoadLine", new Color(0.95f, 0.95f, 0.95f), 0.4f);
            var grassA = MaterialLibrary.Get("GrassA_" + def.id, def.grassBandA, 0.1f);
            var grassB = MaterialLibrary.Get("GrassB_" + def.id, def.grassBandB, 0.1f);
            var ground = MaterialLibrary.Get("Ground_" + def.id, def.groundColor, 0.05f);
            var armco = MaterialLibrary.GetPbr("Armco", new Color(0.8f, 0.81f, 0.83f), 0.7f, 0.5f);

            BuildGround(ground);
            AddStripBanded("Road", -half, half, 0.03f, road, roadDark, SurfaceType.Asphalt, true, 4);
            AddStrip("LineL", -half + 0.3f, -half + 0.45f, 0.035f, line, SurfaceType.Asphalt, false);
            AddStrip("LineR", half - 0.45f, half - 0.3f, 0.035f, line, SurfaceType.Asphalt, false);
            AddKerbs(-half - def.kerbWidth, -half, kerbRed, kerbWhite, "KerbL");
            AddKerbs(half, half + def.kerbWidth, kerbRed, kerbWhite, "KerbR");
            AddStripBanded("RunoffL", -half - def.kerbWidth - def.runoffWidth, -half - def.kerbWidth, 0.02f, grassA, grassB, def.runoffSurface, true, 6);
            AddStripBanded("RunoffR", half + def.kerbWidth, half + def.kerbWidth + def.runoffWidth, 0.02f, grassA, grassB, def.runoffSurface, true, 6);
            AddZebras();
            float wallOffset = half + def.kerbWidth + def.runoffWidth;
            AddWall("BarrierL", -wallOffset, armco);
            AddWall("BarrierR", wallOffset, armco);
            BuildStartLine(half);

            Waypoints = gameObject.AddComponent<WaypointSystem>();
            Waypoints.Initialize(Spline, WaypointSpacing);
            Checkpoints = gameObject.AddComponent<CheckpointManager>();
            Checkpoints.Initialize(this, BuildCheckpoints(wallOffset));
            PlanPit(wallOffset);

            switch (def.id)
            {
                case TrackId.DesertCanyon:
                case TrackId.GolfoReal: DesertDecoration.Build(this, wallOffset); break;
                case TrackId.NightCity:
                case TrackId.Principado:
                case TrackId.JardimDaBaia:
                case TrackId.CornicheDaNoite:
                case TrackId.PortoSul:
                case TrackId.IlhaDasCascatas:
                case TrackId.Fortaleza:
                case TrackId.EstadioCentral:
                case TrackId.NeonStrip: CityDecoration.Build(this, wallOffset); break;
                default: ForestDecoration.Build(this, wallOffset); break;
            }
            try { ClearPitZoneDecoration(); }
            catch (System.Exception ex) { Debug.LogWarning("[Track] limpeza da area do pit ignorada: " + ex.Message); }
            try { CombineDecoration(); }
            catch (System.Exception ex) { Debug.LogWarning("[Track] combinar decoracao ignorado: " + ex.Message); }
        }

        // Decoração sem collider (árvores, arbustos, montanhas) vira poucas malhas por material e
        // célula de 150 m: de ~2.500 objetos/draw calls para algumas dezenas, mantendo o culling.
        void CombineDecoration()
        {
            var roots = new List<Transform>();
            foreach (Transform t in transform) if (t.name == "Decoration") roots.Add(t);
            foreach (var root in roots)
            {
                var groups = new Dictionary<(Material, int, int), List<CombineInstance>>();
                var old = new List<GameObject>();
                var toLocal = root.worldToLocalMatrix;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null || mr.sharedMaterial == null || mf.GetComponent<Collider>() != null) continue;
                    Vector3 wp = mf.transform.position;
                    var key = (mr.sharedMaterial, Mathf.FloorToInt(wp.x / 150f), Mathf.FloorToInt(wp.z / 150f));
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * mf.transform.localToWorldMatrix });
                    old.Add(mf.gameObject);
                }
                foreach (var kv in groups)
                {
                    var m = new Mesh { name = "DecoCombined", indexFormat = IndexFormat.UInt32 };
                    m.CombineMeshes(kv.Value.ToArray(), true, true);
                    var go = new GameObject("DecoCombined");
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key.Item1;
                    go.isStatic = true;
                }
                foreach (var g in old) Destroy(g);
            }
        }


        void BuildGround(Material mat)
        {
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var p in Spline.points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min + new Vector3(600f, 0f, 600f);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Ground";
            g.transform.SetParent(transform, false);
            g.transform.position = new Vector3(center.x, -0.5f, center.z);
            g.transform.localScale = new Vector3(size.x, 1f, size.z);
            g.GetComponent<Renderer>().sharedMaterial = mat;
            g.AddComponent<TrackSurface>().type = SurfaceType.Grass;
            g.isStatic = true;
        }

        GameObject AddStrip(string name, float from, float to, float height, Material mat, SurfaceType surface, bool collider, int start = 0, int end = -1)
        {
            int n = Spline.Count;
            if (end < 0) end = n;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float width = Mathf.Abs(to - from);
            for (int k = start; k <= end; k++)
            {
                int i = Spline.Wrap(k);
                Vector3 p = Spline.points[i] + Vector3.up * height;
                verts.Add(p + Spline.rights[i] * from);
                verts.Add(p + Spline.rights[i] * to);
                float v = k * Spline.spacing / Mathf.Max(1f, width);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                if (k < end)
                {
                    int b = (k - start) * 2;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            return CreateMeshObject(name, verts, uvs, tris, mat, surface, collider);
        }

        GameObject AddStripBanded(string name, float from, float to, float height, Material matA, Material matB, SurfaceType surface, bool collider, int bandSegments)
        {
            bandSegments = Mathf.Max(1, bandSegments);
            int n = Spline.Count;
            int bandCount = Mathf.Max(1, Mathf.CeilToInt(n / (float)bandSegments));
            var vertsA = new List<Vector3>(); var uvsA = new List<Vector2>(); var trisA = new List<int>();
            var vertsB = matB == null ? null : new List<Vector3>();
            var uvsB = matB == null ? null : new List<Vector2>();
            var trisB = matB == null ? null : new List<int>();
            float width = Mathf.Abs(to - from);
            for (int b = 0; b < bandCount; b++)
            {
                var verts = b % 2 == 0 || matB == null ? vertsA : vertsB;
                var uvs = b % 2 == 0 || matB == null ? uvsA : uvsB;
                var tris = b % 2 == 0 || matB == null ? trisA : trisB;
                int vbase = verts.Count;
                int start = b * bandSegments;
                for (int q = 0; q <= bandSegments; q++)
                {
                    int i = Spline.Wrap(start + q);
                    Vector3 p = Spline.points[i] + Vector3.up * height;
                    verts.Add(p + Spline.rights[i] * from);
                    verts.Add(p + Spline.rights[i] * to);
                    float v = (start + q) * Spline.spacing / Mathf.Max(1f, width);
                    uvs.Add(new Vector2(0f, v));
                    uvs.Add(new Vector2(1f, v));
                }
                for (int q = 0; q < bandSegments; q++)
                {
                    int b0 = vbase + q * 2;
                    tris.AddRange(new[] { b0, b0 + 2, b0 + 1, b0 + 1, b0 + 2, b0 + 3 });
                }
            }
            if (matB != null) CreateMeshObject(name + "_B", vertsB, uvsB, trisB, matB, surface, collider);
            return CreateMeshObject(name + "_A", vertsA, uvsA, trisA, matA, surface, collider);
        }

        void AddZebras()
        {
            var red = MaterialLibrary.Get("ZebraRed", new Color(0.75f, 0.1f, 0.1f), 0.3f);
            var white = MaterialLibrary.Get("ZebraWhite", new Color(0.92f, 0.92f, 0.92f), 0.3f);
            int step = Mathf.Max(1, Mathf.RoundToInt(8f / Spline.spacing));
            float roadY = 0.03f + 0.011f;
            int stripes = Mathf.Max(1, Mathf.FloorToInt(Spline.Length / (step * Spline.spacing)));
            for (int b = 0; b < stripes; b++)
            {
                float d = b * step * Spline.spacing;
                if (Curvature(Spline, d) <= 0.006f) continue;
                Pose pose = Spline.PoseAtDistance(d);
                Static(PrimitiveType.Cube, "Zebra" + b, pose.position + Vector3.up * roadY, pose.rotation,
                    new Vector3(Definition.roadWidth, 0.01f, 1f), b % 2 == 0 ? red : white, false);
            }
        }

        static float Curvature(TrackSpline spline, float distance)
        {
            const float span = 8f;
            Vector3 a = spline.PoseAtDistance(distance - span).rotation * Vector3.forward;
            Vector3 b = spline.PoseAtDistance(distance + span).rotation * Vector3.forward;
            float radians = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            return radians / (2f * span);
        }

        GameObject CreateMeshObject(string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris, Material mat, SurfaceType surface, bool collider)
        {
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (collider)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<TrackSurface>().type = surface;
            }
            go.isStatic = true;
            return go;
        }

        void AddKerbs(float from, float to, Material red, Material white, string name)
        {
            int step = Mathf.Max(1, Mathf.RoundToInt(3f / Spline.spacing));
            var redV = new List<Vector3>(); var redT = new List<int>();
            var whiteV = new List<Vector3>(); var whiteT = new List<int>();
            for (int k = 0, seg = 0; k < Spline.Count; k += step, seg++)
            {
                var v = seg % 2 == 0 ? redV : whiteV;
                var t = seg % 2 == 0 ? redT : whiteT;
                for (int j = k; j < k + step; j++)
                {
                    int i0 = Spline.Wrap(j), i1 = Spline.Wrap(j + 1);
                    int b = v.Count;
                    Vector3 up = Vector3.up * 0.04f;
                    v.Add(Spline.points[i0] + Spline.rights[i0] * from + up);
                    v.Add(Spline.points[i0] + Spline.rights[i0] * to + up);
                    v.Add(Spline.points[i1] + Spline.rights[i1] * from + up);
                    v.Add(Spline.points[i1] + Spline.rights[i1] * to + up);
                    t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            CreateMeshObject(name + "_Red", redV, null, redT, red, SurfaceType.Kerb, true);
            CreateMeshObject(name + "_White", whiteV, null, whiteT, white, SurfaceType.Kerb, true);
        }

        /// <summary>Abre um VÃO no guard-rail de um lado da pista no trecho [fromDist, fromDist+span]
        /// ao longo do spline (com wrap). O pit usa isso porque a cerca contínua (~16,7 m do centro)
        /// atravessava a faixa do pit (−8 a −20 m) — não havia caminho da garagem até a pista.</summary>
        public void OpenWallGap(float fromDist, float span, bool leftSide)
        {
            string side = leftSide ? "BarrierL" : "BarrierR";
            var kill = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith(side, System.StringComparison.Ordinal)) continue;
                // Nomes reais: "<side>_Post<k>" e "<side>_Plank<k>" (AddWall). Antes tentava
                // int.TryParse("Post120") — falava SEMPRE e o VÃO nunca abria (cerca bloqueava o pit).
                string tag = child.name.Substring(child.name.LastIndexOf('_') + 1);
                int d0 = tag.Length;
                while (d0 > 0 && tag[d0 - 1] >= '0' && tag[d0 - 1] <= '9') d0--;
                if (d0 >= tag.Length || !int.TryParse(tag.Substring(d0), out int k)) continue;
                float rel = Mathf.Repeat(k * Spline.spacing - fromDist, Spline.Length);
                if (rel <= span) kill.Add(child.gameObject);
            }
            foreach (var g in kill) Destroy(g);
        }

        /// <summary>Escolhe a reta do pit ANTES da decoração: mesma busca de retas do PitLaneSystem
        /// (maior trecho de baixa curvatura), mas só aceita a reta se o corredor + garagens (lado
        /// esquerdo, 6,5–40 m) não invadirem outro trecho da pista nem a própria pista numa curva.
        /// Preferência: pit inteiro antes do grid (sem cruzar a linha de largada).</summary>
        void PlanPit(float wallOffset)
        {
            float L = Spline.Length;
            var cands = new List<(float run, float s)>();
            for (float s = 0f; s < L; s += 10f)
            {
                float run = 0f;
                for (float t = s; t < s + 240f; t += 10f)
                {
                    if (Curvature(Spline, Mathf.Repeat(t, L)) > 0.0015f) break;
                    run += 10f;
                }
                cands.Add((run, s));
            }
            if (cands.Count == 0) return;
            cands.Sort((a, b) => a.run != b.run ? b.run.CompareTo(a.run) : a.s.CompareTo(b.s));
            float chosen = cands[0].s, chosenRun = cands[0].run;
            bool found = false;
            var clearCache = new Dictionary<float, bool>();
            for (int pass = 0; pass < 2 && !found; pass++)
            {
                for (int c = 0; c < cands.Count && c < 60; c++)
                {
                    var (run, s) = cands[c];
                    if (run < 40f) break;
                    if (pass == 0 && !(s - 25f >= 10f && s + 315f <= L - 105f)) continue;
                    if (!clearCache.TryGetValue(s, out bool clear)) clearCache[s] = clear = PitClear(s, wallOffset);
                    if (!clear) continue;
                    chosen = s; chosenRun = run; found = true;
                    break;
                }
            }
            // Sem reta livre: varre a pista toda e pega o trecho livre menos curvo.
            for (int pass = 0; pass < 2 && !found; pass++)
            {
                float bestK = float.MaxValue;
                for (float s = 0f; s < L; s += 10f)
                {
                    if (pass == 0 && !(s - 25f >= 10f && s + 315f <= L - 105f)) continue;
                    float k = 0f;
                    for (float t = 0f; t < 300f; t += 10f) k += Curvature(Spline, Mathf.Repeat(s + t, L));
                    if (k >= bestK || !PitClear(s, wallOffset)) continue;
                    bestK = k; chosen = s; chosenRun = 40f; found = true;
                }
            }
            PitStart = chosen;
            PitRun = chosenRun;
            Debug.Log($"[PitLane] plano pista={Definition.id} inicio={chosen:F0} reta={chosenRun:F0} livre={found} L={L:F0}");

            pitZoneC.Clear(); pitZoneR.Clear(); pitZoneF.Clear(); pitZoneRel.Clear();
            for (float rel = -30f; rel <= PitPlanLength + 20f; rel += 2f)
            {
                Pose p = Spline.PoseAtDistance(Mathf.Repeat(PitStart + rel, L));
                pitZoneC.Add(p.position);
                pitZoneR.Add(p.rotation * Vector3.right);
                pitZoneF.Add(p.rotation * Vector3.forward);
                pitZoneRel.Add(rel);
            }
        }

        void ClearPitZoneDecoration()
        {
            if (pitZoneC.Count == 0) return;
            var kill = new List<GameObject>();
            foreach (Transform root in transform)
            {
                if (root.name != "Decoration") continue;
                foreach (Transform t in root)
                {
                    Vector3 sc = t.lossyScale;
                    float half = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)) * 0.5f;
                    if (half > 60f) continue;
                    if (InPitZone(t.position, half + 1f)) kill.Add(t.gameObject);
                }
            }
            foreach (var g in kill) DestroyImmediate(g);
            if (kill.Count > 0) Debug.Log($"[PitLane] decoracao removida da area do pit: {kill.Count}");
        }

        bool PitClear(float start, float wall)
        {
            float L = Spline.Length;
            float sp = Spline.spacing;
            float lo = start - 45f, hi = start + PitPlanLength + 30f;
            float[] lats = { 6.5f, 10f, 14f, 20f, 28f, 36f, 40f };
            if (pitGrid == null) BuildPitGrid();
            float otherMin = wall + 4f;
            for (float s = lo; s < hi; s += 4f)
            {
                int ci = Spline.Wrap(Mathf.FloorToInt(Mathf.Repeat(s, L) / sp));
                Vector3 c = Spline.points[ci], r = Spline.rights[ci];
                for (int k = 0; k < lats.Length; k++)
                {
                    float ownMin = 0.9f * lats[k];
                    float ownSq = ownMin * ownMin, otherSq = otherMin * otherMin;
                    float qx = c.x - r.x * lats[k], qz = c.z - r.z * lats[k];
                    int gx = Mathf.FloorToInt(qx / PitCell), gz = Mathf.FloorToInt(qz / PitCell);
                    for (int ox = -1; ox <= 1; ox++)
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        if (!pitGrid.TryGetValue(CellKey(gx + ox, gz + oz), out var cell)) continue;
                        foreach (int i in cell)
                        {
                            float dx = Spline.points[i].x - qx, dz = Spline.points[i].z - qz;
                            float d2 = dx * dx + dz * dz;
                            bool inside = Mathf.Repeat(i * sp - (lo - 60f), L) <= (hi - lo) + 120f;
                            if (d2 < (inside ? ownSq : otherSq)) return false;
                        }
                    }
                }
            }
            return true;
        }

        // Grade espacial dos pontos do traçado (célula >= maior raio consultado) para o PitClear.
        const float PitCell = 40f;
        Dictionary<long, List<int>> pitGrid;
        static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        void BuildPitGrid()
        {
            pitGrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < Spline.Count; i++)
            {
                Vector3 p = Spline.points[i];
                long key = CellKey(Mathf.FloorToInt(p.x / PitCell), Mathf.FloorToInt(p.z / PitCell));
                if (!pitGrid.TryGetValue(key, out var list)) pitGrid[key] = list = new List<int>();
                list.Add(i);
            }
        }

        /// <summary>Ponto (com folga) dentro da área reservada ao pit: corredor/rampas (6–21 m à esquerda)
        /// ou garagens (até 38 m). A decoração consulta isso para nunca nascer dentro do box.</summary>
        public bool InPitZone(Vector3 pos, float margin = 0f)
        {
            int best = -1;
            float bestSq = 60f * 60f;
            for (int i = 0; i < pitZoneC.Count; i++)
            {
                float dx = pos.x - pitZoneC[i].x, dz = pos.z - pitZoneC[i].z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestSq) { bestSq = d2; best = i; }
            }
            if (best < 0) return false;
            Vector3 d = pos - pitZoneC[best];
            d.y = 0f;
            float lat = -Vector3.Dot(d, pitZoneR[best]);
            float lon = Vector3.Dot(d, pitZoneF[best]);
            if (Mathf.Abs(lon) > 2f + margin) return false;
            float rel = pitZoneRel[best];
            if (lat < 6f - margin) return false;
            if (lat <= 21f + margin) return true;
            return lat <= 38f + margin && rel >= 12f - margin && rel <= PitPlanLength + margin;
        }

        void AddWall(string name, float offset, Material mat)
        {
            var post = MaterialLibrary.GetPbr("ArmcoPost", new Color(0.24f, 0.25f, 0.27f), 0.6f, 0.4f);
            int step = Mathf.Max(1, Mathf.RoundToInt(4f / Spline.spacing));
            int n = Spline.Count;
            float len = step * Spline.spacing + 0.4f;
            for (int k = 0; k < n; k += step)
            {
                Pose pp = Spline.PoseAtDistance(k * Spline.spacing);
                Pose pm = Spline.PoseAtDistance((k + step * 0.5f) * Spline.spacing);
                Vector3 latP = pp.rotation * Vector3.right * offset;
                Vector3 latM = pm.rotation * Vector3.right * offset;
                Static(PrimitiveType.Cube, name + "_Post" + k, pp.position + latP + Vector3.up * 0.35f, pp.rotation, new Vector3(0.08f, 0.7f, 0.08f), post, false);
                Static(PrimitiveType.Cube, name + "_Plank" + k, pm.position + latM + Vector3.up * 0.55f, pm.rotation, new Vector3(0.05f, 0.3f, len), mat, true);
            }
        }

        void BuildStartLine(float half)
        {
            Pose pose = Spline.PoseAtDistance(0f);
            var checker = MaterialLibrary.Get("Checker", Color.white, 0.3f, 0f, null, MaterialLibrary.Checker(8, Color.white, Color.black));
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "StartLine";
            Destroy(line.GetComponent<Collider>());
            line.transform.SetParent(transform, false);
            line.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.04f, pose.rotation);
            line.transform.localScale = new Vector3(Definition.roadWidth, 0.01f, 1.5f);
            line.GetComponent<Renderer>().sharedMaterial = checker;

            var tower = MaterialLibrary.GetPbr("GantryTower", new Color(0.36f, 0.36f, 0.38f), 0.35f, 0.5f);
            var beam = MaterialLibrary.GetPbr("GantryBeam", new Color(0.05f, 0.05f, 0.06f), 0.5f, 0.6f);
            var accent = MaterialLibrary.Get("Accent", new Color(1f, 0.48f, 0.1f), 0.4f);
            float towerSpan = Definition.roadWidth * 0.5f + 2f;
            Static(PrimitiveType.Cube, "GantryTowerL", pose.position - pose.rotation * Vector3.right * towerSpan + Vector3.up * 4.5f, pose.rotation, new Vector3(1.2f, 9f, 1.2f), tower, false);
            Static(PrimitiveType.Cube, "GantryTowerR", pose.position + pose.rotation * Vector3.right * towerSpan + Vector3.up * 4.5f, pose.rotation, new Vector3(1.2f, 9f, 1.2f), tower, false);
            Static(PrimitiveType.Cube, "GantryBeam", pose.position + Vector3.up * 10f, pose.rotation, new Vector3(Definition.roadWidth + 4f, 2.2f, 1.2f), beam, false);
            Static(PrimitiveType.Cube, "GantryAccent", pose.position + Vector3.up * 10.05f + pose.rotation * Vector3.forward * 0.65f, pose.rotation, new Vector3(Definition.roadWidth + 3f, 0.35f, 0.05f), accent, false);
            Static(PrimitiveType.Cube, "PanelL", pose.position + pose.rotation * Vector3.right * -2.05f + Vector3.up * 9f + pose.rotation * Vector3.forward * 0.62f, pose.rotation, new Vector3(3.4f, 1.2f, 0.15f), beam, false);
            Static(PrimitiveType.Cube, "PanelR", pose.position + pose.rotation * Vector3.right * 2.05f + Vector3.up * 9f + pose.rotation * Vector3.forward * 0.8f, pose.rotation, new Vector3(3.4f, 1.2f, 0.15f), beam, false);
            Static(PrimitiveType.Cube, "PanelAccentL", pose.position + pose.rotation * Vector3.right * -2.05f + Vector3.up * 8.68f + pose.rotation * Vector3.forward * 0.69f, pose.rotation, new Vector3(2.9f, 0.18f, 0.05f), accent, false);
            Static(PrimitiveType.Cube, "PanelAccentR", pose.position + pose.rotation * Vector3.right * 2.05f + Vector3.up * 8.68f + pose.rotation * Vector3.forward * 0.87f, pose.rotation, new Vector3(2.9f, 0.18f, 0.05f), accent, false);

            var paint = MaterialLibrary.Get("GridPaint", new Color(0.9f, 0.9f, 0.9f), 0.3f);
            for (int slot = 0; slot < GridSlots; slot++)
            {
                Pose g = GetGridPose(slot);
                Static(PrimitiveType.Cube, "Grid" + slot, g.position + g.rotation * new Vector3(0f, 0f, 2.4f) - Vector3.up * 0.555f,
                    g.rotation, new Vector3(2.2f, 0.01f, 0.2f), paint, false);
            }
        }

        public GameObject Static(PrimitiveType type, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        List<CheckpointTrigger> BuildCheckpoints(float halfWidth)
        {
            var list = new List<CheckpointTrigger>();
            int count = Mathf.Max(4, Mathf.RoundToInt(Spline.Length / CheckpointSpacing));
            float spacing = Spline.Length / count;
            for (int c = 0; c < count; c++)
            {
                Pose p = Spline.PoseAtDistance(c * spacing);
                var go = new GameObject("Checkpoint_" + c);
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(p.position + Vector3.up * 3f, p.rotation);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(halfWidth * 2f + 4f, 8f, 2f);
                var trigger = go.AddComponent<CheckpointTrigger>();
                trigger.index = c;
                trigger.splineIndex = Spline.Wrap(Mathf.FloorToInt(c * spacing / Spline.spacing));
                list.Add(trigger);
            }
            return list;
        }

        public Pose GetGridPose(int slot)
        {
            int row = slot / 2;
            bool left = slot % 2 == 0;
            float back = 8f + row * 8f + (left ? 0f : 4f);
            Pose p = Spline.PoseAtDistance(Spline.Length - back, left ? -2.8f : 2.8f);
            p.position += Vector3.up * 0.6f;
            return p;
        }
    }
}
