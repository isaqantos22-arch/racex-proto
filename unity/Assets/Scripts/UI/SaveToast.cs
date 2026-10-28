using System;
using RaceX.Career;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Aviso de salvamento (canto inferior direito, tempo não-dilatado).</summary>
    public class SaveToast : MonoBehaviour
    {
        static SaveToast inst;

        Text label;
        Image bg;
        double seqStart;
        double endAt;
        bool savingPhase;

        public static void EnsureInstance()
        {
            if (inst != null) return;
            try
            {
                var go = new GameObject("SaveToast");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<SaveToast>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SaveToast] falha ao criar: " + ex.Message);
            }
        }

        public static void Show(bool ok)
        {
            EnsureInstance();
            if (inst != null) inst.RunShow(ok);
        }

        void Awake()
        {
            try
            {
                var canvas = UIFactory.CreateCanvas("SaveToastCanvas", 500);
                DontDestroyOnLoad(canvas.gameObject);
                bg = UIFactory.Panel("SaveToastBg", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 30f), new Vector2(480f, 44f), new Color(0f, 0f, 0f, 0.6f));
                bg.raycastTarget = false;
                label = UIFactory.Label("SaveToastLabel", bg.transform, "", 26, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 44f), Color.white);
                label.raycastTarget = false;
                bg.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SaveToast] falha ao criar UI: " + ex.Message);
                label = null;
                bg = null;
            }
        }

        void RunShow(bool ok)
        {
            if (label == null || bg == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            // Throttle: ignora chamadas repetidas na fase "guardando" (< 0,3 s desde o início).
            if (ok && now - seqStart < 0.3 && savingPhase) return;
            seqStart = now;
            endAt = now + (ok ? 2.0 : 2.5);
            savingPhase = ok;
            bg.gameObject.SetActive(true);
            label.color = ok ? Color.white : new Color(1f, 0.35f, 0.35f, 1f);
            label.text = ok ? "SALVANDO PROGRESSO..." : "FALHA AO SALVAR PROGRESSO";
        }

        void Update()
        {
            if (label == null || bg == null || !bg.gameObject.activeSelf) return;
            float t = (float)(Time.realtimeSinceStartupAsDouble - seqStart);
            if (savingPhase && t >= 0.6)
            {
                savingPhase = false;
                label.text = "PROGRESSO SALVO";
                label.color = new Color(0.3f, 1f, 0.4f, 1f);
            }
            if (t >= (float)(endAt - seqStart)) Hide();
        }

        void Hide()
        {
            if (bg != null) bg.gameObject.SetActive(false);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && CareerMode.data != null) CareerMode.Save();
        }

        void OnApplicationQuit()
        {
            if (CareerMode.data != null) CareerMode.Save();
        }
    }
}
