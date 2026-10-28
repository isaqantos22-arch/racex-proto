using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    public enum ModelMaterialRole { Paint, Black, Tire, Glass, Metal, Headlight, Taillight }

    public class ImportedBodyResult
    {
        public Transform root;
        public Bounds localBounds;
        public float eyeHeightFraction;
        public float eyeForwardFraction;
        public Vector3[] wheelCenters;
        public readonly List<KeyValuePair<Renderer, int>> paintSlots = new List<KeyValuePair<Renderer, int>>();
    }

    /// <summary>Substitui o corpo de primitivas por um modelo FBX importado, se existir.</summary>
    public static class ImportedCarModels
    {
        class Config
        {
            public string file;
            public Vector3 forward;
            public Vector3 up;
            public float eyeHeight;
            public float eyeForward;
        }

        class Entry
        {
            public GameObject prefab;
            public Config config;
            public Quaternion rotation;
            public Bounds bounds;
            public HashSet<string> junk;
        }

        static readonly Dictionary<CarModel, Config> configs = new Dictionary<CarModel, Config>
        {
            { CarModel.Falcon, new Config { file = "Corvette", forward = Vector3.up, up = Vector3.forward, eyeHeight = 0.86f, eyeForward = -0.06f } },
            { CarModel.Titan, new Config { file = "Porsche", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.78f, eyeForward = -0.04f } },
            { CarModel.Storm, new Config { file = "Temerario", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.F1, new Config { file = "F1_2022", forward = Vector3.right, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.Ferrari, new Config { file = "Ferrari", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.R8, new Config { file = "R8", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.Mustang, new Config { file = "Mustang", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.Revuelto, new Config { file = "Revuelto", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.GtrNismo, new Config { file = "GtrNismo", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.McLarenP1, new Config { file = "McLarenP1", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.82f, eyeForward = -0.05f } },
            { CarModel.Bugatti, new Config { file = "Bugatti", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.F2008, new Config { file = "F2008", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Studio, new Config { file = "F1Studio", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
        };

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();

        public static bool Available(CarModel model) => GetEntry(model) != null;

        /// <summary>Carrega o modelo em segundo plano (não trava a thread principal). Roda o que GetEntry faz ao terminar.
        /// onProgress (opcional) reporta o progresso REAL do LoadAsync (0-1) — a barra do loading anda com carga verdadeira.</summary>
        public static IEnumerator PrewarmAsync(CarModel model, System.Action<float> onProgress = null)
        {
            if (!configs.TryGetValue(model, out var cfg)) yield break;
            string key = model + ":" + cfg.file;
            if (cache.TryGetValue(key, out var cached) && cached != null) { onProgress?.Invoke(1f); yield break; }
            var req = Resources.LoadAsync<GameObject>("CarModels/" + cfg.file);
            while (!req.isDone)
            {
                onProgress?.Invoke(req.progress);
                yield return null;
            }
            onProgress?.Invoke(1f);
            if (cache.ContainsKey(key) && cache[key] != null) yield break;
            var prefab = req.asset as GameObject;
            if (prefab == null)
            {
                cache[key] = null;
                yield break;
            }
            var e = new Entry { prefab = prefab, config = cfg, rotation = Quaternion.Inverse(Quaternion.LookRotation(cfg.forward, cfg.up)), junk = new HashSet<string>() };
            ComputeBounds(e);
            cache[key] = e;
        }

        static Entry GetEntry(CarModel model)
        {
            if (!configs.TryGetValue(model, out var cfg)) return null;
            string key = model + ":" + cfg.file;
            if (cache.TryGetValue(key, out var e)) return e;
            var prefab = Resources.Load<GameObject>("CarModels/" + cfg.file);
            if (prefab == null)
            {
                cache[key] = null;
                return null;
            }
            e = new Entry { prefab = prefab, config = cfg, rotation = Quaternion.Inverse(Quaternion.LookRotation(cfg.forward, cfg.up)), junk = new HashSet<string>() };
            ComputeBounds(e);
            cache[key] = e;
            return e;
        }

        static void ComputeBounds(Entry e)
        {
            var root = e.prefab.transform;
            var rot = Matrix4x4.Rotate(e.rotation);
            bool first = true;
            var b = new Bounds();
            var filters = e.prefab.GetComponentsInChildren<MeshFilter>(true);
            var nameCount = new Dictionary<string, int>();
            foreach (var mf in filters)
            {
                nameCount.TryGetValue(mf.name, out int c);
                nameCount[mf.name] = c + 1;
            }
            var kept = new Dictionary<string, KeyValuePair<string, int>>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                string n = mf.name.ToLowerInvariant();
                var m = rot * root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                var wb = TransformBounds(m, mb);
                if (n.Contains("floor") || n.Contains("mirror_plane") || wb.size.x > 30f || wb.size.z > 30f || (wb.size.y < 0.001f && Mathf.Max(wb.size.x, wb.size.z) > 1.5f))
                {
                    e.junk.Add(mf.name);
                    continue;
                }
                // Peças sobrepostas idênticas (variantes/LODs duplicados dentro do mesmo FBX): mantém a de maior
                // contagem de vértices e desliga as cópias. Não altera o bounds — as cópias ocupam o mesmo volume.
                // Só atua quando o nome é único no modelo, pois a desativação em BuildInto é feita por nome.
                if (nameCount[mf.name] == 1)
                {
                    string dup = DuplicateKey(wb);
                    int verts = mf.sharedMesh.vertexCount;
                    if (kept.TryGetValue(dup, out var best))
                    {
                        if (verts > best.Value)
                        {
                            e.junk.Add(best.Key);
                            kept[dup] = new KeyValuePair<string, int>(mf.name, verts);
                        }
                        else e.junk.Add(mf.name);
                    }
                    else kept[dup] = new KeyValuePair<string, int>(mf.name, verts);
                }
                if (first) { b = wb; first = false; }
                else b.Encapsulate(wb);
            }
            e.bounds = b;
        }

        static string DuplicateKey(Bounds b)
        {
            return Mathf.RoundToInt(b.center.x * 20f) + "/" + Mathf.RoundToInt(b.center.y * 20f) + "/" + Mathf.RoundToInt(b.center.z * 20f)
                + "|" + Mathf.RoundToInt(b.size.x * 20f) + "/" + Mathf.RoundToInt(b.size.y * 20f) + "/" + Mathf.RoundToInt(b.size.z * 20f);
        }

        static Bounds TransformBounds(Matrix4x4 m, Bounds local)
        {
            var c = local.center;
            var x = local.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var p = c + new Vector3((i & 1) == 0 ? -x.x : x.x, (i & 2) == 0 ? -x.y : x.y, (i & 4) == 0 ? -x.z : x.z);
                result.Encapsulate(m.MultiplyPoint3x4(p));
            }
            return result;
        }

        public static ModelMaterialRole RoleFor(string materialName)
        {
            string n = materialName.ToLowerInvariant();
            if (n.Contains("tire") || n.Contains("tyre") || n.Contains("rubber")) return ModelMaterialRole.Tire;
            if (n.Contains("glass") || n.Contains("windshield") || n.Contains("windscreen") || n.Contains("window") || n.Contains("canopy")) return ModelMaterialRole.Glass;
            if (n.Contains("steering") || n.Contains("black") || n.Contains("plastic") || n.Contains("carbon") || n.Contains("interior")
                || n.Contains("leather") || n.Contains("foam") || n.Contains("felt") || n.Contains("seat") || n.Contains("wiper")
                || n.Contains("fabric") || n.Contains("screen") || n.Contains("number") || n.Contains("logo") || n.Contains("material."))
                return ModelMaterialRole.Black;
            if (n.Contains("carpaintmain") || n.Contains("body_color") || n.Contains("main_body") || n.Contains("front_wing")
                || n.Contains("rear_wing") || n.Contains("mirrors_wheel_wings") || n == "red" || n == "green")
                return ModelMaterialRole.Paint;
            if (n.Contains("taillight") || n.Contains("blinker") || n.Contains("rear_led")) return ModelMaterialRole.Taillight;
            if (n.Contains("glass_clear_headlight")) return ModelMaterialRole.Headlight;
            if (n.Contains("chrome") || n.Contains("metal") || n.Contains("rim") || n.Contains("rotor") || n.Contains("brake")
                || n.Contains("calliper") || n.Contains("caliper") || n.Contains("anodized") || n.Contains("brass") || n.Contains("exhaust")
                || n.Contains("steel") || n.Contains("mirror") || n.Contains("plate"))
                return ModelMaterialRole.Metal;
            return ModelMaterialRole.Black;
        }

        /// <summary>Instancia o modelo importado, alinhado (frente = +Z), na escala do comprimento pedido e com as rodas no chão. Retorna null se não houver modelo.</summary>
        /// <summary>Mede as rodas do modelo do carro (sem montar nada) e devolve as 4 posições no espaço do chassi, na escala real do jogo.</summary>
        public static Vector3[] TryMeasureWheels(CarModel model, float length, float groundY)
        {
            var entry = GetEntry(model);
            if (entry == null) return null;
            var holder = new GameObject("WheelMeasure");
            try
            {
                var go = Object.Instantiate(entry.prefab, holder.transform, false);
                var b = entry.bounds;
                float scale = length / Mathf.Max(0.01f, b.size.z);
                var pivot = new GameObject("Pivot").transform;
                pivot.SetParent(holder.transform, false);
                pivot.localRotation = entry.rotation;
                pivot.localScale = Vector3.one * scale;
                pivot.localPosition = new Vector3(-b.center.x * scale, groundY - b.min.y * scale, -b.center.z * scale);
                go.transform.SetParent(pivot, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var measured = CollectWheelAnchors(holder.transform, pivot, go);
                if (measured == null) return null;
                var result = new Vector3[4];
                for (int i = 0; i < 4; i++) result[i] = new Vector3(measured[i].x, 0f, measured[i].z);
                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WHEELS] falha ao medir " + model + ": " + e.Message);
                return null;
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        public static ImportedBodyResult BuildInto(Transform parent, CarModel model, float length, float groundY, Dictionary<ModelMaterialRole, Material> materials, Vector3[] anchorsOverride = null)
        {
            var entry = GetEntry(model);
            if (entry == null) return null;

            var pivot = new GameObject("ImportedBody").transform;
            pivot.SetParent(parent, false);
            var go = Object.Instantiate(entry.prefab, pivot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var b = entry.bounds;
            float scale = length / Mathf.Max(0.01f, b.size.z);
            pivot.localRotation = entry.rotation;
            pivot.localScale = Vector3.one * scale;
            Vector3 offset = new Vector3(-b.center.x * scale, groundY - b.min.y * scale, -b.center.z * scale);
            pivot.localPosition = offset;

            var result = new ImportedBodyResult
            {
                root = pivot,
                localBounds = new Bounds(new Vector3(0f, groundY + b.size.y * scale * 0.5f, 0f), b.size * scale),
                eyeHeightFraction = entry.config.eyeHeight,
                eyeForwardFraction = entry.config.eyeForward
            };

            var anchors = anchorsOverride != null && anchorsOverride.Length == 4 ? anchorsOverride : CollectWheelAnchors(parent, pivot, go);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (entry.junk.Contains(r.name))
                {
                    r.enabled = false;
                    continue;
                }
                var shared = r.sharedMaterials;
                string nm = r.name.ToLowerInvariant();
                if (nm.Contains("steering") || nm.Contains("volante")) { r.enabled = false; continue; }
                bool modelWheel = IsWheelPart(r);
                if (modelWheel)
                {
                    var lc = pivot.InverseTransformPoint(r.bounds.center);
                    bool central = Mathf.Abs(lc.x) < Mathf.Abs(entry.bounds.size.x) * 0.12f;
                    bool tooHigh = lc.y > entry.bounds.size.y * 0.55f;
                    bool nearAnchor = anchors != null && NearWheel(anchors, parent, pivot, r.bounds.center);
                    if (!nearAnchor && (central || tooHigh)) { r.enabled = false; continue; }
                    if (anchors == null) modelWheel = false;
                }
                else if (anchors != null)
                    for (int i = 0; i < shared.Length; i++)
                    {
                        var role = shared[i] == null ? ModelMaterialRole.Black : RoleFor(shared[i].name);
                        if (role == ModelMaterialRole.Metal && NearWheel(anchors, parent, pivot, r.bounds.center)) { modelWheel = true; break; }
                    }
                if (!modelWheel && WheelPartNearAnchor(r, anchors, parent, pivot)) modelWheel = true;
                if (modelWheel)
                {
                    r.enabled = false;
                    continue;
                }
                var replaced = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    var role = shared[i] == null ? ModelMaterialRole.Black : RoleFor(shared[i].name);
                    replaced[i] = materials.TryGetValue(role, out var mat) ? mat : shared[i];
                    if (role == ModelMaterialRole.Paint) result.paintSlots.Add(new KeyValuePair<Renderer, int>(r, i));
                }
                r.sharedMaterials = replaced;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            result.wheelCenters = anchors;
            return result;
        }

        static Vector3 ToRootLocal(Transform root, Transform pivot, Vector3 world)
        {
            return root.InverseTransformPoint(pivot.TransformPoint(pivot.InverseTransformPoint(world)));
        }

        /// <summary>Detecta as posições reais das rodas do modelo (centros das malhas de pneu) no espaço do chassi.</summary>
        static bool IsWheelPart(Renderer r)
        {
            string n = r.name.ToLowerInvariant();
            if (n.Contains("steering")) return false;
            if (n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire")) return true;
            foreach (var m in r.sharedMaterials)
                if (m != null && RoleFor(m.name) == ModelMaterialRole.Tire) return true;
            return false;
        }

        static Vector3[] CollectWheelAnchors(Transform root, Transform pivot, GameObject instance)
        {
            var frontBoxes = new List<Bounds>();
            var rearBoxes = new List<Bounds>();
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsTireMesh(r)) continue;
                var world = TransformBounds(Matrix4x4.identity, r.bounds);
                var s = world.size;
                float d = Mathf.Max(s.y, s.z);
                if (d < 0.25f || d > 1.3f || Mathf.Min(s.y, s.z) / d < 0.7f || s.x > d * 0.85f) continue;
                var local = ToRootLocal(root, pivot, world.center);
                var ext = world.extents;
                var size = new Vector3(ext.x * 2f, ext.y * 2f, ext.z * 2f);
                var b = new Bounds(local, size);
                if (local.z >= 0f) frontBoxes.Add(b); else rearBoxes.Add(b);
            }
            var front = AnchorPair(frontBoxes);
            var rear = AnchorPair(rearBoxes);
            if (front == null || rear == null) return null;
            return new[]
            {
                new Vector3(-front.Value.x, 0f, front.Value.y), new Vector3(front.Value.x, 0f, front.Value.y),
                new Vector3(-rear.Value.x, 0f, rear.Value.y), new Vector3(rear.Value.x, 0f, rear.Value.y)
            };
        }

        static readonly string[] tireJunkNames = { "hub", "ventil", "flap", "cover", "wing", "mirror", "arm", "connection", "antenna" };

        /// <summary>Pneu de verdade: malha de roda que nao é peca vizinha (cubo de roda, duto, flap, asa) — essas poluem a media dos anchors.</summary>
        static bool IsTireMesh(Renderer r)
        {
            if (!IsWheelPart(r)) return false;
            string n = r.name.ToLowerInvariant();
            foreach (var j in tireJunkNames) if (n.Contains(j)) return false;
            return true;
        }

        /// <summary>Um eixo pode ter 2 pneus (um por lado) ou 1 malha única central que atravessa os dois lados — nesse caso divide ao meio.</summary>
        static Vector2? AnchorPair(List<Bounds> boxes)
        {
            if (boxes.Count == 0) return null;
            if (boxes.Count == 1)
            {
                var b = boxes[0];
                float half = b.size.x * 0.25f;
                return new Vector2(Mathf.Abs(b.center.x) + half, b.center.z);
            }
            float x = 0f, z = 0f;
            foreach (var b in boxes) { x += Mathf.Abs(b.center.x); z += b.center.z; }
            return new Vector2(x / boxes.Count, z / boxes.Count);
        }

        static bool NearWheel(Vector3[] anchors, Transform root, Transform pivot, Vector3 world)
        {
            var p = ToRootLocal(root, pivot, world);
            foreach (var a in anchors)
            {
                var d = p - a;
                d.y = 0f;
                if (d.magnitude < 0.6f) return true;
            }
            return false;
        }

        /// <summary>Roda do modelo que o teste por CENTRO não pega: rim único que atravessa o EIXO inteiro
        /// (F1/F1Studio têm rim_front/rim_rear — malha de 1,8 m com o centro a x=0, longe dos 4 pinos,
        /// mas o BOUNDS cobre as rodas; sem isso fica com "8 rodas" na tela). Restrito por nome para
        /// nunca esconder asa/para-lama (fender/arch/wing) que também orbita o eixo.</summary>
        static bool WheelPartNearAnchor(Renderer r, Vector3[] anchors, Transform root, Transform pivot)
        {
            string n = r.name.ToLowerInvariant();
            if (!n.Contains("wheel") && !n.Contains("tire") && !n.Contains("tyre") && !n.Contains("rim")
                && !n.Contains("hub") && !n.Contains("brake") && !n.Contains("rotor") && !n.Contains("disc")
                && !n.Contains("calip") && !n.Contains("callip") && !n.Contains("duct")) return false;
            var b = r.bounds;
            var c = ToRootLocal(root, pivot, b.center);
            var e = b.extents;
            foreach (var a in anchors)
            {
                if (Mathf.Abs(c.x - a.x) <= e.x + 0.4f && Mathf.Abs(c.y - a.y) <= e.y + 0.4f && Mathf.Abs(c.z - a.z) <= e.z + 0.4f) return true;
            }
            return false;
        }
    }
}
