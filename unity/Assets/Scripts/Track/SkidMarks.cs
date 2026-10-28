using System.Collections.Generic;
using RaceX.Car;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    /// <summary>Marcas de pneu persistentes e cumulativas: um quad dinâmico por carro; sobrepor marca nova na mesma célula aumenta a intensidade (até o teto). Marcas duram a corrida toda.</summary>
    public static class SkidMarks
    {
        const float CellSize = 0.35f;
        const float QuadWidth = 0.24f;
        const float MaxIntensity = 1f;
        const int MaxQuads = 4096;

        class Layer
        {
            public Mesh mesh;
            public Material material;
            public readonly List<Vector3> verts = new List<Vector3>(MaxQuads * 4);
            public readonly List<Vector2> uvs = new List<Vector2>(MaxQuads * 4);
            public readonly List<Color> colors = new List<Color>(MaxQuads * 4);
            public readonly List<int> tris = new List<int>(MaxQuads * 6);
            public readonly Dictionary<long, int> cells = new Dictionary<long, int>();
            public int count;
        }

        static readonly Dictionary<int, Layer> layers = new Dictionary<int, Layer>();
        static readonly Dictionary<int, SkidEmitter> emitters = new Dictionary<int, SkidEmitter>();

        public static void Attach(CarController car)
        {
            if (car == null || car.spec == null) return;
            var emitter = car.gameObject.AddComponent<SkidEmitter>();
            emitter.car = car;
            emitters[car.gameObject.GetInstanceID()] = emitter;
        }

        public static void ClearAll()
        {
            foreach (var l in layers.Values)
            {
                l.count = 0;
                l.verts.Clear(); l.uvs.Clear(); l.colors.Clear(); l.tris.Clear();
                l.cells.Clear();
                l.mesh?.Clear();
                if (l.mesh != null) l.mesh.RecalculateBounds();
            }
        }

        static Layer LayerFor(int surfaceId, string surfaceName)
        {
            if (layers.TryGetValue(surfaceId, out var l)) return l;
            l = new Layer();
            l.mesh = new Mesh { name = "SkidMarks_" + surfaceName };
            l.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            l.material = new Material(MaterialLibrary.Get("SkidBase_" + surfaceName, new Color(0.05f, 0.05f, 0.05f), 0.8f).shader)
            {
                name = "SkidMat_" + surfaceName,
            };
            if (l.material.HasProperty("_BaseColor")) l.material.SetColor("_BaseColor", Color.white);
            l.material.SetFloat("_Surface", 1f);
            l.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            l.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            l.material.SetInt("_ZWrite", 0);
            l.material.renderQueue = 2950;
            l.mesh.MarkDynamic();
            var go = new GameObject("SkidMarks_" + surfaceName);
            go.AddComponent<MeshFilter>().sharedMesh = l.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = l.material;
            go.transform.position = Vector3.zero;
            layers[surfaceId] = l;
            return l;
        }

        /// <summary>Adiciona um segmento de marca. intensity 0..1; célula repetida soma intensidade (acumula).</summary>
        public static void AddMark(int surfaceId, string surfaceName, Vector3 pos, Vector3 dir, float intensity)
        {
            var l = LayerFor(surfaceId, surfaceName);
            long key = ((long)Mathf.RoundToInt(pos.x / CellSize) << 20) ^ Mathf.RoundToInt(pos.z / CellSize);
            float alpha = Mathf.Clamp01(intensity);
            if (l.cells.TryGetValue(key, out int quad))
            {
                int vi = quad * 4;
                for (int k = 0; k < 4; k++)
                {
                    var c = l.colors[vi + k];
                    c.a = Mathf.Min(MaxIntensity, c.a + alpha * 0.6f);
                    l.colors[vi + k] = c;
                }
                Commit(l);
                return;
            }
            if (l.count >= MaxQuads) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            dir.Normalize();
            var side = new Vector3(-dir.z, 0f, dir.x) * (QuadWidth * 0.5f);
            float y = pos.y + 0.02f;
            var v0 = new Vector3(pos.x - side.x, y, pos.z - side.z);
            var v1 = new Vector3(pos.x + side.x, y, pos.z + side.z);
            int baseIdx = l.count * 4;
            l.verts.Add(v0); l.verts.Add(v1); l.verts.Add(v1 + dir * CellSize); l.verts.Add(v0 + dir * CellSize);
            for (int k = 0; k < 4; k++) { l.uvs.Add(new Vector2(k == 0 || k == 3 ? 0f : 1f, 0f)); l.colors.Add(new Color(0.05f, 0.05f, 0.05f, alpha)); }
            l.tris.Add(baseIdx); l.tris.Add(baseIdx + 2); l.tris.Add(baseIdx + 1);
            l.tris.Add(baseIdx); l.tris.Add(baseIdx + 3); l.tris.Add(baseIdx + 2);
            l.cells[key] = l.count;
            l.count++;
            Commit(l);
        }

        static void Commit(Layer l)
        {
            l.mesh.SetVertices(l.verts);
            l.mesh.SetUVs(0, l.uvs);
            l.mesh.SetColors(l.colors);
            l.mesh.SetTriangles(l.tris, 0);
            l.mesh.RecalculateNormals();
            l.mesh.RecalculateBounds();
        }
    }

    class SkidEmitter : MonoBehaviour
    {
        public CarController car;
        readonly Vector3[] lastPos = new Vector3[2];
        readonly bool[] hasLast = { false, false };
        readonly float[] carry = new float[2];
        readonly int[] surfaceIds = new int[2];
        readonly string[] surfaceNames = new string[2];
        float cooldown;

        static readonly Dictionary<string, int> nameIds = new Dictionary<string, int>();

        static int SurfaceId(string name)
        {
            if (!nameIds.TryGetValue(name, out int id)) { id = nameIds.Count + 1; nameIds[name] = id; }
            return id;
        }

        void Update()
        {
            try { Run(); }
            catch (System.Exception) { }
        }

        void Run()
        {
            if (car == null || car.spec == null) return;
            cooldown -= Time.deltaTime;
            if (cooldown > 0f || car.MaxWheelSlip <= 0.35f || car.GroundedWheels <= 0) { hasLast[0] = hasLast[1] = false; return; }
            if (cooldown > 0f) return;
            cooldown = 1f / 30f;

            var f = car.transform.forward;
            float strength = Mathf.Clamp01((car.MaxWheelSlip - 0.35f) / 1.2f) * Mathf.Clamp01(car.SpeedKmh / 60f);
            if (strength <= 0.02f) return;

            var rear = car.spec.wheelbase * 0.5f - 0.2f;
            var track = car.spec.trackWidth * 0.5f;
            for (int w = 0; w < 2; w++)
            {
                float x = (w == 0 ? -1f : 1f) * track;
                var pos = car.transform.position + car.transform.rotation * new Vector3(x, -0.05f, -rear);
                if (hasLast[w])
                {
                    var d = pos - lastPos[w];
                    float dist = d.magnitude;
                    if (dist < 0.05f) continue;
                    int steps = Mathf.CeilToInt(dist / 0.3f);
                    for (int s = 1; s <= steps; s++)
                        SkidMarks.AddMark(surfaceIds[w], surfaceNames[w],
                            Vector3.Lerp(lastPos[w], pos, s / (float)steps), d, strength * 0.5f);
                }
                else
                {
                    var surface = car.SurfaceName();
                    surfaceIds[w] = SurfaceId(surface);
                    surfaceNames[w] = surface;
                    SkidMarks.AddMark(surfaceIds[w], surface, pos, f, strength * 0.5f);
                }
                lastPos[w] = pos;
                hasLast[w] = true;
            }
        }
    }
}
