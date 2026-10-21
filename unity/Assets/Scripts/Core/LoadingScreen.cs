using System.Collections;
using RaceX.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaceX.Core
{
    public static class LoadingScreen
    {
        internal static readonly string[] Dicas =
        {
            "Use o freio antes da curva e acelere na saída para ganhar tempo.",
            "Freie em linha reta: frear curvando pode fazer o carro rodar.",
            "O freio de mão ajuda em curvas fechadas, mas derruba a velocidade.",
            "Na tela, segure o acelerador e toque no freio para controlar a derrapagem.",
            "Acelere aos poucos em pista molhada para não girar o carro.",
            "Ajuste a sensibilidade dos controles nas configurações para o seu ritmo."
        };

        public static void Show(string cenaAlvo, RaceSettings settings)
        {
            Time.timeScale = 1f;
            var go = new GameObject("LoadingScreen");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<LoadingRunner>().targetScene = cenaAlvo;
        }
    }

    class LoadingRunner : MonoBehaviour
    {
        public string targetScene;

        Text dicaText;
        Image fill;
        Canvas canvas;

        void Start()
        {
            canvas = UIFactory.CreateCanvas("LoadingCanvas", 1000);
            Object.DontDestroyOnLoad(canvas.gameObject);
            var root = UIFactory.Stretch("Root", canvas.transform);
            var bg = UIFactory.Panel("Bg", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.Bg);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero;

            UIFactory.Label("Logo", root, "RaceX Proto", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 180f), MenuTheme.Accent);

            var barBg = UIFactory.Panel("BarBg", root, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 18f), new Color(1f, 1f, 1f, 0.12f));
            fill = UIFactory.Panel("BarFill", barBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 18f), MenuTheme.Accent);
            fill.raycastTarget = false;
            dicaText = UIFactory.Label("Dica", root, "DICA  " + LoadingScreen.Dicas[Random.Range(0, LoadingScreen.Dicas.Length)], 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 60f), MenuTheme.TextDim);
            StartCoroutine(Load());
        }

        void Update()
        {
            if (fill != null)
            {
                var rt = (RectTransform)fill.transform;
                float w = 720f * Mathf.PingPong(Time.time * 0.8f, 1f);
                rt.sizeDelta = new Vector2(w, 18f);
            }
        }

        IEnumerator Load()
        {
            var op = SceneManager.LoadSceneAsync(targetScene);
            if (op == null)
            {
                Debug.LogWarning("RaceX: cena '" + targetScene + "' não encontrada no Build Settings.");
                Destroy(canvas.gameObject);
                Destroy(gameObject);
                yield break;
            }
            while (!op.isDone) yield return null;
            Destroy(canvas.gameObject);
            Destroy(gameObject);
        }
    }
}
