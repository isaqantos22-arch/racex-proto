using System.Linq;
using RaceX.Car;
using UnityEngine;

namespace RaceX.EditorTools
{
    public static class RaceXWheelDiag
    {
        public static void Measure()
        {
            Debug.Log("[WDIAG] BEGIN");
            foreach (CarModel m in System.Enum.GetValues(typeof(CarModel)))
            {
                var spec = CarSpec.CreateDefault(m);
                var a = ImportedCarModels.TryMeasureWheels(m, spec.bodySize.z, -spec.wheelRadius);
                if (a == null) { Debug.Log("[WDIAG] " + m + ": NULL (caira no fallback da spec)"); continue; }
                Debug.Log(string.Format("[WDIAG] {0}: FL=({1:F2},0,{2:F2}) FR=({3:F2},0,{4:F2}) RL=({5:F2},0,{6:F2}) RR=({7:F2},0,{8:F2})",
                    m, a[0].x, a[0].z, a[1].x, a[1].z, a[2].x, a[2].z, a[3].x, a[3].z));
            }
            Debug.Log("[WDIAG] END");
        }

        static readonly System.Collections.Generic.Dictionary<CarModel, string> fileByModel = new System.Collections.Generic.Dictionary<CarModel, string>
        {
            { CarModel.Falcon, "Corvette" }, { CarModel.Titan, "Porsche" }, { CarModel.Storm, "Temerario" }, { CarModel.F1, "F1_2022" },
            { CarModel.Ferrari, "Ferrari" }, { CarModel.R8, "R8" }, { CarModel.Mustang, "Mustang" }, { CarModel.Revuelto, "Revuelto" },
            { CarModel.GtrNismo, "GtrNismo" }, { CarModel.McLarenP1, "McLarenP1" }, { CarModel.Bugatti, "Bugatti" },
            { CarModel.F2008, "F2008" }, { CarModel.F1Studio, "F1Studio" }
        };

        public static void DumpTires()
        {
            Debug.Log("[TDUMP] BEGIN");
            foreach (CarModel m in System.Enum.GetValues(typeof(CarModel)))
            {
                if (!fileByModel.TryGetValue(m, out var file)) continue;
                var prefab = Resources.Load<GameObject>("CarModels/" + file);
                if (prefab == null) { Debug.Log("[TDUMP] " + m + ": prefab null"); continue; }
                var go = Object.Instantiate(prefab);
                var rt = go.transform;
                Debug.Log(string.Format("[TDUMP] {0} | ROOT pos=({1:F2},{2:F2},{3:F2}) rot=({4:F1},{5:F1},{6:F1}) scale={7:F4}",
                    m, rt.localPosition.x, rt.localPosition.y, rt.localPosition.z,
                    rt.localEulerAngles.x, rt.localEulerAngles.y, rt.localEulerAngles.z, rt.lossyScale.x));
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name.ToLowerInvariant();
                    if (!n.Contains("tire") && !n.Contains("tyre")) continue;
                    var b = r.bounds;
                    if (b.size.x < 0.05f && b.size.y < 0.05f && b.size.z < 0.05f) continue;
                    Debug.Log(string.Format("[TDUMP] {0} | {1} | center=({2:F2},{3:F2},{4:F2}) size=({5:F2},{6:F2},{7:F2})",
                        m, r.name, b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z));
                }
                Object.Destroy(go);
            }
            Debug.Log("[TDUMP] END");
        }
    }
}
