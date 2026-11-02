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
        MeshRenderer mr;

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
            // Só os carros PRÓXIMOS (até 70 m) e à frente da câmera têm nome — na largada os 24 nomes
            // empilhados viravam um borrão ilegível. Escala menor (máx. 1,8).
            bool visible = dist < 70f && Vector3.Dot(cam.transform.forward, label.position - cam.transform.position) > 0f;
            if (mr == null) mr = label.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled != visible) mr.enabled = visible;
            if (!visible) return;
            label.localScale = Vector3.one * Mathf.Clamp(dist * 0.08f, 0.8f, 1.8f);
        }
    }
}
