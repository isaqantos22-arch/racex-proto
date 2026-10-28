using System.IO;
using System.Text;
using RaceX.Car;
using UnityEditor;
using UnityEngine;

namespace RaceX.EditorTools
{
    public static class RaceXDiag
    {
        public static void ModelReport()
        {
            var sb = new StringBuilder();
            foreach (var name in new[] { "Ferrari", "R8", "Mustang", "Revuelto", "GtrNismo", "McLarenP1", "Corvette" })
            {
                var prefab = Resources.Load<GameObject>("CarModels/" + name);
                if (prefab == null) { sb.AppendLine("MISSING " + name); continue; }
                var go = Object.Instantiate(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                sb.AppendLine("MODEL " + name + " rootRot=" + prefab.transform.localRotation.eulerAngles + " rootScale=" + prefab.transform.localScale);
                Bounds all = default; bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    int v = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                    sb.AppendLine("  R " + r.name + " c=" + r.bounds.center.ToString("F2") + " s=" + r.bounds.size.ToString("F2") + " v=" + v + " mats=" + r.sharedMaterials.Length);
                    if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
                }
                var mats = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    int v = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                    foreach (var m in r.sharedMaterials) { string k = m == null ? "null" : m.name + "|" + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString("F2") : "-") + "|" + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "notex"); mats.TryGetValue(k, out int c0); mats[k] = c0 + v; }
                }
                foreach (var kv in mats) sb.AppendLine("  M " + kv.Key + " v=" + kv.Value);
                sb.AppendLine("  ALL c=" + all.center.ToString("F2") + " s=" + all.size.ToString("F2"));
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "../../model_report.txt"), sb.ToString());
            EditorApplication.Exit(0);
        }

        public static void RenderCars()
        {
            var dir = Path.Combine(Application.dataPath, "../../shots");
            Directory.CreateDirectory(dir);
            var light = new GameObject("L").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = Color.gray;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.35f, 0.45f);
            cam.fieldOfView = 40f;
            var rt = new RenderTexture(640, 400, 24);
            cam.targetTexture = rt;
            foreach (CarModel m in System.Enum.GetValues(typeof(CarModel)))
            {
                var spec = CarSpec.CreateDefault(m);
                var car = CarFactory.Create(spec, 0, "Car_" + m, new Vector3(0f, spec.wheelRadius, 0f), Quaternion.identity);
                var rb = car.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                Shot(cam, rt, new Vector3(6f, 3f, 8f), Vector3.up * 0.6f, Path.Combine(dir, m + "_front.png"));
                Shot(cam, rt, new Vector3(9f, 1.2f, 0f), Vector3.up * 0.6f, Path.Combine(dir, m + "_side.png"));
                var eye = car.GetComponent<CarVisuals>().eyeAnchor;
                cam.fieldOfView = 70f;
                camGo.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(car.transform.forward) * Quaternion.Euler(-3f, 0f, 0f));
                cam.nearClipPlane = 0.05f;
                SaveRt(cam, rt, Path.Combine(dir, m + "_cockpit.png"));
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.3f;
                Object.DestroyImmediate(car.gameObject);
            }
            EditorApplication.Exit(0);
        }

        public static void RenderDrivers()
        {
            var dir = Path.Combine(Application.dataPath, "../../shots_driver");
            Directory.CreateDirectory(dir);
            var light = new GameObject("L").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 5f;
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.35f, 0.45f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(800, 500, 24);
            cam.targetTexture = rt;
            var models = new[] { CarModel.F1, CarModel.F2008, CarModel.F1Studio, CarModel.Ferrari, CarModel.Titan, CarModel.Mustang };
            foreach (var m in models)
            {
                var spec = CarSpec.CreateDefault(m);
                var car = CarFactory.Create(spec, 0, "Car_" + m, new Vector3(0f, spec.wheelRadius, 0f), Quaternion.identity);
                var rb = car.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                var eye = car.GetComponent<CarVisuals>().eyeAnchor.position;
                var drv = car.transform.Find("Driver");
                Debug.Log("DRVDIAG " + m + " eye=" + eye.ToString("F2") + " driver=" + (drv != null ? drv.position.ToString("F2") + " active=" + drv.gameObject.activeInHierarchy : "NULL") + " head=" + (car.GetComponent<CarVisuals>().driverHead != null));
                Shot(cam, rt, eye + new Vector3(1.6f, 0.9f, -2.4f), eye + new Vector3(0f, -0.15f, 0f), Path.Combine(dir, m + "_rear34.png"));
                Shot(cam, rt, eye + new Vector3(2.6f, 0.25f, 0.2f), eye + new Vector3(0f, -0.15f, 0f), Path.Combine(dir, m + "_side.png"));
                Shot(cam, rt, eye + new Vector3(-1.2f, 1.1f, 2.2f), eye + new Vector3(0f, -0.15f, 0f), Path.Combine(dir, m + "_front34.png"));
                Shot(cam, rt, eye + new Vector3(0f, 0.8f, -6f), eye + new Vector3(0f, -0.1f, 0f), Path.Combine(dir, m + "_chase.png"));
                Object.DestroyImmediate(car.gameObject);
            }
            EditorApplication.Exit(0);
        }

        static void Shot(Camera cam, RenderTexture rt, Vector3 pos, Vector3 target, string path)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(target);
            SaveRt(cam, rt, path);
        }

        static void SaveRt(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
