using System.Collections;
using System.Collections.Generic;
using RaceX.Car;
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
            "Ajuste a sensibilidade dos controles nas configurações para o seu ritmo.",
            "CURIOSIDADE: Ayrton Senna venceu 41 Grandes Prêmios na carreira de F1.",
            "CURIOSIDADE: O Bugatti Veyron foi o 1º carro de produção a passar de 400 km/h.",
            "CURIOSIDADE: A Ferrari é a escuderia mais antiga ainda ativa na Fórmula 1 (desde 1950).",
            "CURIOSIDADE: O motor do Ford Mustang Shelby GT500 chega a mais de 760 cv.",
            "CURIOSIDADE: Michael Schumacher tem 7 títulos mundiais de F1, recorde empatado com Hamilton.",
            "CURIOSIDADE: O Porsche 911 está em produção contínua desde 1963.",
            "CURIOSIDADE: Carros de F1 moderno fazem uma curva a mais de 5G de força lateral.",
            "CURIOSIDADE: O recorde de volta em Le Mans foi quebrado por carros híbridos nos anos 2010.",
            "CURIOSIDADE: O Lamborghini Temerario combina V8 biturbo com motores elétricos."
        };

        public static void Show(string cenaAlvo, RaceSettings settings)
        {
            Time.timeScale = 1f;
            var go = new GameObject("LoadingScreen");
            Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<LoadingRunner>();
            runner.targetScene = cenaAlvo;
            if (settings != null && settings.players != null && settings.players.Length > 0)
            {
                runner.playerModel = (CarModel)settings.players[0].car; // pré-carga o FBX do jogador AINDA no loading (barra real), nunca na ativação
                runner.formulaOnly = settings.formulaOnly;
            }
        }
    }

    class LoadingRunner : MonoBehaviour
    {
        public string targetScene;
        public CarModel playerModel = (CarModel)(-1);
        public bool formulaOnly;

        Text dicaText, pctText;
        Image fill;
        RectTransform fillRt;
        Canvas canvas;
        const float BarWidth = 720f;
        float dicaTimer;
        int lastDicaIndex = -1;

        void Start()
        {
            canvas = UIFactory.CreateCanvas("LoadingCanvas", 1000);
            Object.DontDestroyOnLoad(canvas.gameObject);
            var root = UIFactory.Stretch("Root", canvas.transform);
            var bg = UIFactory.Panel("Bg", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, MenuTheme.Bg);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.sizeDelta = Vector2.zero;

            UIFactory.Label("Logo", root, "RaceX Proto", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 180f), MenuTheme.Accent);

            var barBg = UIFactory.Panel("BarBg", root, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BarWidth, 18f), new Color(1f, 1f, 1f, 0.12f));
            fill = UIFactory.Panel("BarFill", barBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 18f), MenuTheme.Accent);
            fill.raycastTarget = false;
            fillRt = (RectTransform)fill.transform;
            pctText = UIFactory.Label("Pct", root, "0%", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.41f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 36f), MenuTheme.TextDim);
            lastDicaIndex = Random.Range(0, LoadingScreen.Dicas.Length);
            dicaText = UIFactory.Label("Dica", root, "DICA  " + LoadingScreen.Dicas[lastDicaIndex], 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 60f), MenuTheme.TextDim);
            StartCoroutine(Load());
        }

        void Update()
        {
            // Dica troca por TEMPO (10s) ou por PROGRESSO (a cada 15%) — o que vier primeiro.
            // Só por tempo não trocava: a carga real dura <10s e o resto é a ativação congelada.
            dicaTimer += Time.unscaledDeltaTime;
            if (dicaTimer >= 10f)
            {
                dicaTimer = 0f;
                TrocaDica();
            }
        }

        void TrocaDica()
        {
            int next;
            do { next = Random.Range(0, LoadingScreen.Dicas.Length); } while (next == lastDicaIndex && LoadingScreen.Dicas.Length > 1);
            lastDicaIndex = next;
            if (dicaText != null) dicaText.text = "DICA  " + LoadingScreen.Dicas[next];
        }

        void SetProgress(float p01)
        {
            if (fillRt != null) fillRt.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(p01), 18f);
            if (pctText != null) pctText.text = Mathf.RoundToInt(Mathf.Clamp01(p01) * 100f) + "%";
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
            op.allowSceneActivation = false;
            // Barra REAL em 3 fases: (A) carga da cena 0-55%, (B) pré-carga do modelo do carro
            // 55-90% (progresso MEDIDO do LoadAsync — era isto que congelava ~1min na ativação,
            // pois o FBX era carregado síncrono DEPOIS da cena pronta), (C) ativação 90-100%.
            float lastBucket = 0f;
            while (op.progress < 0.9f)
            {
                SetProgress(op.progress / 0.9f * 0.55f);
                float bucket = Mathf.Floor(op.progress / 0.9f / 0.15f);
                if (bucket > lastBucket) { lastBucket = bucket; TrocaDica(); }
                yield return null;
            }
            SetProgress(0.55f);
            {
                // Pré-carga REAL de todos os modelos que a corrida usa (jogador + pool de NPCs).
                // Cada modelo reporta o progresso do LoadAsync — a barra anda com carga verdadeira,
                // e a ativação da cena fica LEVE (nada de Resources.Load síncrono de FBX pesado).
                var models = new List<CarModel>();
                if (playerModel >= 0) models.Add(playerModel);
                if (formulaOnly)
                {
                    if (!models.Contains(CarModel.F1)) models.Add(CarModel.F1);
                }
                else
                {
                    foreach (CarModel m in (CarModel[])System.Enum.GetValues(typeof(CarModel)))
                        if ((m != CarModel.F1 || SettingsManager.AllowF1) && !models.Contains(m))
                            models.Add(m);
                }
                if (models.Count > 0)
                {
                    if (dicaText != null) dicaText.text = "CARREGANDO CARROS...";
                    float span = 0.35f / models.Count;
                    for (int i = 0; i < models.Count; i++)
                    {
                        int idx = i;
                        yield return ImportedCarModels.PrewarmAsync(models[idx],
                            p => SetProgress(0.55f + (idx + Mathf.Clamp01(p)) * span));
                    }
                }
            }
            SetProgress(0.9f);
            if (pctText != null) pctText.text = "90%";
            if (dicaText != null) dicaText.text = "ATIVANDO CENA — AGUARDE...";
            yield return new WaitForSecondsRealtime(0.15f);
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null; // congelamento curto (modelos já estão em memória)
            SetProgress(1f); // 100% só com a cena viva de verdade
            yield return new WaitForSecondsRealtime(0.3f);
            Destroy(canvas.gameObject);
            Destroy(gameObject);
        }
    }
}
