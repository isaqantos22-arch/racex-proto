using RaceX.Car;
using RaceX.Core;
using UnityEngine;

namespace RaceX.UI
{
    /// <summary>Nome do piloto flutuando sobre o carro; vira sempre para a câmera. Liga/desliga em Ajustes.</summary>
    public class Nametag : MonoBehaviour
    {
        TextMesh text;
        Transform label;
        CarController car;
        Camera cam;
        float camTimer;

        public static void Attach(CarController car, string pilotName)
        {
            if (car == null || car.GetComponent<Nametag>() != null) return;
            var tag = car.gameObject.AddComponent<Nametag>();
            tag.car = car;
            var go = new GameObject("Nametag");
            go.transform.SetParent(car.transform, false);
            tag.label = go.transform;
            tag.text = go.AddComponent<TextMesh>();
            tag.text.text = pilotName;
            tag.text.fontSize = 48;
            tag.text.characterSize = 0.085f;
            tag.text.anchor = TextAnchor.MiddleCenter;
            tag.text.alignment = TextAlignment.Center;
            tag.text.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (label == null || text == null) return;
            bool show = SettingsManager.NameTags;
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            if (!show) return;
            camTimer -= Time.deltaTime;
            if (cam == null || camTimer <= 0f)
            {
                cam = Camera.main;
                camTimer = 1f;
            }
            if (cam == null) return;
            float h = car != null && car.spec != null ? car.spec.bodySize.y + 0.75f : 1.6f;
            label.position = transform.position + Vector3.up * h;
            label.rotation = cam.transform.rotation;
            float dist = Vector3.Distance(cam.transform.position, label.position);
            label.localScale = Vector3.one * Mathf.Clamp(dist * 0.09f, 0.9f, 2.4f);
        }
    }
}
