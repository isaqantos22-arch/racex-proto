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
        /// <summary>Peças de roda DO MODELO (pneu, aro, disco) por roda FL/FR/RL/RR — o CarFactory as
        /// prende no eixo da roda (giram e esterçam) no lugar do cilindro procedural.</summary>
        public readonly List<Renderer>[] wheelParts = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        /// <summary>Raio do pneu do modelo na escala do jogo (medido nas malhas de pneu), ou 0.</summary>
        public float modelTyreRadius;
        /// <summary>Fração da face externa do pneu em que o flanco fica a 81% (x) e 91% (y) do raio (faixa do composto).</summary>
        public Vector2 bandFace = Vector2.one;
        public bool HasModelWheels => wheelParts[0].Count > 0 && wheelParts[1].Count > 0 && wheelParts[2].Count > 0 && wheelParts[3].Count > 0;
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
            /// <summary>Usa as rodas cilíndricas do jogo em vez das rodas do modelo (modelo antigo cujas rodas
            /// não montam limpas nos pivôs: F1Legacy ficava com roda duplicada e a carroceria fora do eixo).</summary>
            public bool cylinderWheels;
            /// <summary>Peças soltas do arquivo que não pertencem ao carro (começo do nome, minúsculo) — escondidas.</summary>
            public string[] hide;
        }

        /// <summary>Rodas medidas no modelo (espaço do chassi): altura do EIXO e raio do pneu. O corpo é
        /// posicionado pelo eixo (e não pela peça mais baixa do arquivo — o F1_2022 tem uma peça 1,45 m abaixo
        /// do pneu, que deixava o carro 41 cm alto sobre as rodas).</summary>
        class WheelFit { public float axleY; public float radius; }

        /// <summary>Inclinação do flanco na faixa do composto (81-91% do raio), medida no Blender nas malhas de pneu de
        /// cada arquivo: lateral máxima a 81% (x) e estimada a 91% (y), como fração da meia-largura do pneu.
        /// Sem medida = flanco reto (1, 1).</summary>
        static readonly Dictionary<string, Vector2> BandFace = new Dictionary<string, Vector2>
        {
            { "F1Photo22", new Vector2(1.000f, 1.000f) },
            { "F1_RB22", new Vector2(0.992f, 0.900f) },
            { "McLarenMP45", new Vector2(0.958f, 0.921f) },
            { "F1Concept26", new Vector2(0.983f, 0.810f) },
            { "F2008", new Vector2(0.951f, 0.810f) },
            { "F1CarConcept", new Vector2(0.988f, 0.946f) },
            { "F1Custom22", new Vector2(0.987f, 0.875f) },
            { "FerrariF60", new Vector2(0.940f, 0.938f) },
            { "Lotus77", new Vector2(0.918f, 0.859f) },
            { "F1Sponsor", new Vector2(0.915f, 0.798f) },
            { "F4British", new Vector2(0.969f, 0.876f) },
            { "F1Y2026", new Vector2(1.000f, 1.000f) },
            { "FormulaC1", new Vector2(1.000f, 1.000f) },
            { "FormulaC2", new Vector2(1.000f, 1.000f) },
            { "F1Studio", new Vector2(0.990f, 0.886f) },
            { "McLarenF1", new Vector2(0.962f, 0.836f) },
        };
        static readonly Dictionary<CarModel, WheelFit> fits = new Dictionary<CarModel, WheelFit>();

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
            // F1 = Red Bull RB18 2022 (RED+BULL+2022+F1+CAR.blend, convert_f1_rb22.py) — mesmo pipeline do F2008/F1Studio.
            // O F1_2022.fbx antigo fica no projeto como referência, sem uso.
            { CarModel.F1, new Config { file = "F1_RB22", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.Ferrari, new Config { file = "Ferrari", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.R8, new Config { file = "R8", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.Mustang, new Config { file = "Mustang", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.Revuelto, new Config { file = "Revuelto", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.GtrNismo, new Config { file = "GtrNismo", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.McLarenP1, new Config { file = "McLarenP1", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.82f, eyeForward = -0.05f } },
            { CarModel.Bugatti, new Config { file = "Bugatti", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.80f, eyeForward = -0.05f } },
            { CarModel.F2008, new Config { file = "F2008", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            // F1Studio: "Circle" é um aro solto 1,2–1,6 m acima do chão, atrás do cockpit (medido no Blender) — flutuava sobre o carro.
            { CarModel.F1Studio, new Config { file = "F1Studio", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f, hide = new[] { "circle" } } },
            // F1 antigo (o "F1" até a v80) e os monopostos novos de 28/10 (convert_f1_batch.py, mesmo pipeline do F2008).
            { CarModel.F1Legacy, new Config { file = "F1_2022", forward = Vector3.right, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f, cylinderWheels = true } },
            { CarModel.F1Basic, new Config { file = "F1Basic", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Concept26, new Config { file = "F1Concept26", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Custom22, new Config { file = "F1Custom22", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.McLarenF1, new Config { file = "McLarenF1", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.FerrariF60, new Config { file = "FerrariF60", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Sponsor, new Config { file = "F1Sponsor", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.Lotus77, new Config { file = "Lotus77", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            // FP04: o .blend tem a frente em -Y (ao contrário dos outros convertidos) — conferido em vista lateral no Blender.
            // Viseira/entradas de ar do capacete original (o piloto vem do FP04_Driver) ficavam soltas no ar.
            { CarModel.FP04, new Config { file = "FP04", forward = Vector3.forward, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f, hide = new[] { "vizor", "top vent" } } },
            { CarModel.F4British, new Config { file = "F4British", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.McLarenMP45, new Config { file = "McLarenMP45", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Photo22, new Config { file = "F1Photo22", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1CarConcept, new Config { file = "F1CarConcept", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.F1Y2026, new Config { file = "F1Y2026", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.FormulaC1, new Config { file = "FormulaC1", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
            { CarModel.FormulaC2, new Config { file = "FormulaC2", forward = Vector3.back, up = Vector3.up, eyeHeight = 0.58f, eyeForward = 0.06f } },
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

        /// <summary>Peça avulsa do pipeline (piloto/volante do FP04): instancia o FBX com a frente em +Z,
        /// troca os materiais por papel e ajusta a escala para que a dimensão `axis` (0=X, 2=Z) meça
        /// `worldSize` metros NO MUNDO (compensa a escala do pai). Origem do FBX = ponto de referência
        /// (centro do capacete / do volante). Retorna null se o arquivo não existir.</summary>
        public static Transform InstantiatePart(string file, Transform parent, int axis, float worldSize, Dictionary<ModelMaterialRole, Material> materials)
        {
            var prefab = Resources.Load<GameObject>("CarModels/" + file);
            if (prefab == null) return null;
            var holder = new GameObject(file).transform;
            var go = Object.Instantiate(prefab, holder, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // pipeline exporta a frente em -Z (config forward = back)
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Object.Destroy(holder.gameObject); return null; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float size = axis == 0 ? b.size.x : axis == 1 ? b.size.y : b.size.z;
            if (size > 1e-5f) go.transform.localScale *= worldSize / size;
            foreach (var r in rs)
            {
                var shared = r.sharedMaterials;
                var replaced = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    var role = shared[i] == null ? ModelMaterialRole.Black : RoleFor(shared[i].name);
                    replaced[i] = materials != null && materials.TryGetValue(role, out var mat) ? mat : shared[i];
                }
                r.sharedMaterials = replaced;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // custo mobile: piloto/volante sem sombra
            }
            holder.SetParent(parent, false);
            float ps = parent.lossyScale.x;
            if (ps > 1e-5f) holder.localScale = Vector3.one / ps;
            return holder;
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
                var measured = CollectWheelAnchors(holder.transform, pivot, go, out float axleY, out float radius);
                if (measured == null) return null;
                fits[model] = new WheelFit { axleY = axleY, radius = radius };
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
            // Eixo do modelo na altura do eixo do jogo (y = 0): roda e para-lama ficam juntos.
            fits.TryGetValue(model, out var fit);
            float lift = fit != null ? -fit.axleY : 0f;
            offset.y += lift;
            pivot.localPosition = offset;

            var result = new ImportedBodyResult
            {
                root = pivot,
                localBounds = new Bounds(new Vector3(0f, groundY + lift + b.size.y * scale * 0.5f, 0f), b.size * scale),
                modelTyreRadius = fit != null ? fit.radius : 0f,
                bandFace = BandFace.TryGetValue(entry.config.file, out var bf) ? bf : Vector2.one,
                eyeHeightFraction = entry.config.eyeHeight,
                eyeForwardFraction = entry.config.eyeForward
            };

            var anchors = anchorsOverride != null && anchorsOverride.Length == 4 ? anchorsOverride : CollectWheelAnchors(parent, pivot, go, out _, out _);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (entry.junk.Contains(r.name) || Hidden(entry.config, r.name))
                {
                    r.enabled = false;
                    continue;
                }
                var shared = r.sharedMaterials;
                string nm = r.name.ToLowerInvariant();
                // Peça "de roda" pelo nome/material que NÃO é concêntrica ao eixo nem do tamanho do pneu (para-lama
                // do F1 Concept com 1,6 m, frisos de borracha do Temerário a 0,8 m do chão): fica na carroceria.
                bool notWheel = fit != null && anchors != null && !entry.config.cylinderWheels && !WheelShaped(r, fit, anchors, parent, pivot);
                if (nm.Contains("steering") || nm.Contains("volante")) { r.enabled = false; continue; }
                if (notWheel && IsWheelPart(r))
                {
                    // Mesma regra de antes para peça "de roda" solta no centro ou no alto (sobra do arquivo): escondida.
                    var lc0 = pivot.InverseTransformPoint(r.bounds.center);
                    if (Mathf.Abs(lc0.x) < Mathf.Abs(entry.bounds.size.x) * 0.12f || lc0.y > entry.bounds.size.y * 0.55f) { r.enabled = false; continue; }
                }
                bool modelWheel = !notWheel && IsWheelPart(r);
                if (modelWheel)
                {
                    var lc = pivot.InverseTransformPoint(r.bounds.center);
                    bool central = Mathf.Abs(lc.x) < Mathf.Abs(entry.bounds.size.x) * 0.12f;
                    bool tooHigh = lc.y > entry.bounds.size.y * 0.55f;
                    bool nearAnchor = anchors != null && NearWheel(anchors, parent, pivot, r.bounds.center);
                    if (!nearAnchor && (central || tooHigh)) { r.enabled = false; continue; }
                    if (anchors == null) modelWheel = false;
                }
                else if (anchors != null && !notWheel)
                    for (int i = 0; i < shared.Length; i++)
                    {
                        var role = shared[i] == null ? ModelMaterialRole.Black : RoleFor(shared[i].name);
                        if (role == ModelMaterialRole.Metal && NearWheel(anchors, parent, pivot, r.bounds.center)) { modelWheel = true; break; }
                    }
                if (!modelWheel && !notWheel && WheelPartNearAnchor(r, anchors, parent, pivot)) modelWheel = true;
                int wheelIdx = modelWheel && anchors != null ? NearestAnchor(anchors, parent, pivot, r.bounds.center) : -1;
                if (modelWheel && wheelIdx < 0)
                {
                    r.enabled = false;
                    continue;
                }
                if (wheelIdx >= 0) result.wheelParts[wheelIdx].Add(r);
                var replaced = new Material[shared.Length];
                bool paintedTyre = !notWheel && (nm.Contains("tire") || nm.Contains("tyre"));
                for (int i = 0; i < shared.Length; i++)
                {
                    var role = shared[i] == null ? ModelMaterialRole.Black : RoleFor(shared[i].name);
                    // Peça de RODA com material de pintura (McLaren F1: pneu "carpaintmain") vira pneu — senão a
                    // roda saía na cor da equipe.
                    // Vale também para peça de pneu que não entrou no grupo da roda (McLaren F1: pneu traseiro laranja).
                    if (role == ModelMaterialRole.Paint && (wheelIdx >= 0 || paintedTyre)) role = ModelMaterialRole.Tire;
                    replaced[i] = materials.TryGetValue(role, out var mat) ? mat : shared[i];
                    if (role == ModelMaterialRole.Paint && wheelIdx < 0) result.paintSlots.Add(new KeyValuePair<Renderer, int>(r, i));
                }
                r.sharedMaterials = replaced;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (entry.config.cylinderWheels)
            {
                // Sem rodas do modelo: esconde as peças de roda e o CarFactory fica com os cilindros.
                foreach (var list in result.wheelParts) { foreach (var r in list) if (r != null) r.enabled = false; list.Clear(); }
            }
            result.wheelCenters = anchors;
            return result;
        }

        static bool Hidden(Config c, string name)
        {
            if (c.hide == null) return false;
            string n = name.ToLowerInvariant();
            foreach (var h in c.hide) if (n.StartsWith(h)) return true;
            return false;
        }

        /// <summary>Peça que pode girar com a roda: não maior que 1,3 diâmetro de pneu e com algum ponto dentro de
        /// 1,2 raio do eixo (vista lateral). Regra conferida nas malhas de 26 modelos (Blender): mantém aro, raios,
        /// porcas e blocos da banda; tira o para-lama de 1,6 m do F1 Concept e os frisos do Temerário a 0,8 m do chão.</summary>
        static bool WheelShaped(Renderer r, WheelFit fit, Vector3[] anchors, Transform root, Transform pivot)
        {
            var b = LocalBounds(r, root);
            float R = Mathf.Max(0.05f, fit.radius);
            if (Mathf.Max(b.size.y, b.size.z) > R * 2.6f) return false;
            float dy = Mathf.Max(0f, Mathf.Abs(b.center.y) - b.extents.y);
            foreach (var a in anchors)
            {
                float dz = Mathf.Max(0f, Mathf.Abs(b.center.z - a.z) - b.extents.z);
                if (dy * dy + dz * dz <= R * R * 1.44f) return true;
            }
            return false;
        }

        /// <summary>Limites de um renderer no espaço de `space` (pela malha — não depende da rotação do carro no mundo).</summary>
        public static Bounds LocalBounds(Renderer r, Transform space)
        {
            var m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return TransformBounds(m, mf.sharedMesh.bounds);
            var sk = r as SkinnedMeshRenderer;
            if (sk != null) return TransformBounds(m, sk.localBounds);
            var wb = r.bounds;
            return new Bounds(space.InverseTransformPoint(wb.center), space.InverseTransformVector(wb.size));
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

        static Vector3[] CollectWheelAnchors(Transform root, Transform pivot, GameObject instance, out float axleY, out float radius)
        {
            axleY = 0f; radius = 0f;
            var cands = new List<Bounds>();
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsTireMesh(r)) continue;
                var world = TransformBounds(Matrix4x4.identity, r.bounds);
                var s = world.size;
                float d = Mathf.Max(s.y, s.z);
                if (d < 0.25f || d > 1.3f || Mathf.Min(s.y, s.z) / d < 0.7f || s.x > d * 0.85f) continue;
                cands.Add(new Bounds(ToRootLocal(root, pivot, world.center), s));
            }
            if (cands.Count == 0) return null;
            // Pneu de verdade TOCA O CHÃO: descarta malha redonda "de borracha" que fica no alto (frisos da
            // carroceria do Temerário com material de pneu, a 0,4 m do chão) e puxava os eixos para o meio do carro.
            float ground = float.MaxValue;
            foreach (var c in cands) ground = Mathf.Min(ground, c.min.y);
            var frontBoxes = new List<Bounds>();
            var rearBoxes = new List<Bounds>();
            float dMax = 0f;
            foreach (var c in cands)
            {
                float d = Mathf.Max(c.size.y, c.size.z);
                if (c.min.y - ground > d * 0.2f) continue;
                dMax = Mathf.Max(dMax, d);
                if (c.center.z >= 0f) frontBoxes.Add(c); else rearBoxes.Add(c);
            }
            // Eixo e raio pelos PNEUS INTEIROS (≥ 90% do maior diâmetro): pneu feito de pedaços (Mustang, Ferrari)
            // tem blocos da banda perto do chão que puxariam a média do eixo para baixo.
            float sumY = 0f, sumR = 0f; int n = 0;
            foreach (var c in cands)
            {
                float d = Mathf.Max(c.size.y, c.size.z);
                if (c.min.y - ground > d * 0.2f || d < dMax * 0.9f) continue;
                sumY += c.center.y; sumR += d * 0.5f; n++;
            }
            if (n > 0) { axleY = sumY / n; radius = sumR / n; }
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

        /// <summary>Roda (0..3) mais próxima no plano X-Z, ou -1 se a peça não está perto de nenhuma.</summary>
        static int NearestAnchor(Vector3[] anchors, Transform root, Transform pivot, Vector3 world)
        {
            var p = ToRootLocal(root, pivot, world);
            int best = -1; float bestD = 0.9f;
            for (int i = 0; i < anchors.Length; i++)
            {
                var d = p - anchors[i]; d.y = 0f;
                if (d.magnitude < bestD) { bestD = d.magnitude; best = i; }
            }
            return best;
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
