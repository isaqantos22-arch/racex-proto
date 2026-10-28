using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>
    /// Comemoração de pódio na tela de resultados: garrafa inclinando e espirrando champanhe (gotas douradas),
    /// faixa VITÓRIA/PÓDIO. Só UI (sem shader/material), tempo não escalado, pool fixo de gotas.
    /// </summary>
    public class PodiumCelebration : MonoBehaviour
    {
        const int Drops = 70;
        RectTransform bottle;
        RectTransform[] drops;
        Image[] dropImgs;
        Vector2[] vel;
        float[] life;
        float t;
        Vector2 nozzle;

        public static void Play(RectTransform parent, int position)
        {
            if (parent == null || position < 1 || position > 3) return;
            var go = new GameObject("PodiumCelebration", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            go.AddComponent<PodiumCelebration>().Build(rt, position);
        }

        void Build(RectTransform root, int position)
        {
            var gold = new Color(1f, 0.82f, 0.25f);
            string title = position == 1 ? "VITÓRIA!" : position == 2 ? "PÓDIO — 2º LUGAR" : "PÓDIO — 3º LUGAR";
            var top = new Vector2(0.5f, 1f);
            var banner = UIFactory.Panel("PodiumBanner", root, top, top, new Vector2(0f, 70f), new Vector2(760f, 84f), new Color(0.05f, 0.06f, 0.09f, 0.95f));
            banner.sprite = MenuTheme.RoundedSprite(190, 21, 10);
            UIFactory.Label("PodiumTitle", banner.transform, title, 50, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 80f), position == 1 ? gold : MenuTheme.Text);

            var bl = new Vector2(0f, 0f);
            bottle = UIFactory.Rect("Bottle", root, bl, bl, new Vector2(0.5f, 0f), new Vector2(-70f, 40f), new Vector2(60f, 220f));
            var glass = UIFactory.Panel("Glass", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(60f, 150f), new Color(0.08f, 0.3f, 0.14f, 1f));
            glass.sprite = MenuTheme.RoundedSprite(30, 75, 12);
            var label = UIFactory.Panel("Label", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(60f, 46f), new Color(0.95f, 0.93f, 0.85f, 1f));
            label.sprite = MenuTheme.RoundedSprite(30, 23, 4);
            var neck = UIFactory.Panel("Neck", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 146f), new Vector2(22f, 62f), new Color(0.08f, 0.3f, 0.14f, 1f));
            neck.sprite = MenuTheme.RoundedSprite(11, 31, 5);
            var foil = UIFactory.Panel("Foil", bottle, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(26f, 30f), gold);
            foil.sprite = MenuTheme.RoundedSprite(13, 15, 5);

            var dropSprite = MenuTheme.RoundedSprite(8, 8, 4);
            drops = new RectTransform[Drops];
            dropImgs = new Image[Drops];
            vel = new Vector2[Drops];
            life = new float[Drops];
            for (int i = 0; i < Drops; i++)
            {
                var d = UIFactory.Panel("Drop" + i, root, bl, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f), gold);
                d.sprite = dropSprite;
                d.enabled = false;
                drops[i] = (RectTransform)d.transform;
                dropImgs[i] = d;
                life[i] = -Random.Range(0f, 1.2f);
            }
        }

        void Update()
        {
            if (bottle == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            t += dt;
            float tilt = Mathf.Lerp(0f, -48f, Mathf.Clamp01(t / 0.8f)) + Mathf.Sin(t * 9f) * 4f;
            bottle.localRotation = Quaternion.Euler(0f, 0f, tilt);
            nozzle = bottle.anchoredPosition + (Vector2)(bottle.localRotation * new Vector3(0f, 215f, 0f));
            Vector2 dir = bottle.localRotation * Vector2.up;
            bool spraying = t > 0.6f && t < 6f;
            for (int i = 0; i < Drops; i++)
            {
                life[i] += dt;
                if (life[i] < 0f) continue;
                if (!dropImgs[i].enabled)
                {
                    if (!spraying) continue;
                    dropImgs[i].enabled = true;
                    drops[i].anchoredPosition = nozzle;
                    float spread = Random.Range(-14f, 14f);
                    vel[i] = (Vector2)(Quaternion.Euler(0f, 0f, spread) * dir) * Random.Range(650f, 1050f);
                    life[i] = 0f;
                    float sc = Random.Range(0.6f, 1.4f);
                    drops[i].localScale = new Vector3(sc, sc, 1f);
                }
                vel[i] += new Vector2(0f, -900f) * dt;
                drops[i].anchoredPosition += vel[i] * dt;
                var c = dropImgs[i].color;
                c.a = Mathf.Clamp01(1.4f - life[i]);
                dropImgs[i].color = c;
                if (life[i] > 1.4f)
                {
                    dropImgs[i].enabled = false;
                    life[i] = spraying ? -Random.Range(0f, 0.15f) : -999f;
                }
            }
        }
    }
}
