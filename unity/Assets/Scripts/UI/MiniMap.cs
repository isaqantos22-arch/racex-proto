using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Mini-mapa: contorno da pista em textura gerada + pontos por carro.</summary>
    public class MiniMap : MonoBehaviour
    {
        const int TexSize = 256;
        const int Pad = 14;

        RaceManager race;
        RaceEntry self;
        Image mapImage;
        RectTransform mapRect;
        RectTransform[] dots;
        Vector2 center;
        float worldScale;
        bool forceFull;   // pista inteira (sem zoom dinâmico)
        float mapSize = 230f;

        public static void Create(Transform parent, RaceManager race, RaceEntry self, bool compact)
        {
            float size = compact ? 150f : 230f;
            float s = compact ? 0.7f : 1f;
            CreateAt(parent, race, self, new Vector2(0f, 1f), new Vector2(24f, -24f - 120f * s), size, false);
        }

        public static MiniMap CreateAt(Transform parent, RaceManager race, RaceEntry self, Vector2 anchor, Vector2 anchoredPos, float size, bool forceFull)
        {
            var bg = UIFactory.Panel("MiniMap", parent, anchor, anchor, anchoredPos, new Vector2(size, size), new Color(0f, 0f, 0f, 0.45f));
            bg.raycastTarget = false;
            bg.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var image = UIFactory.Panel("MiniMapTrack", bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), Color.white);
            image.raycastTarget = false;
            var mm = bg.gameObject.AddComponent<MiniMap>();
            mm.race = race;
            mm.self = self;
            mm.forceFull = forceFull;
            mm.mapImage = image;
            mm.mapRect = image.rectTransform;
            mm.mapSize = size;
            mm.Build(race.Track.Waypoints.Spline, self);
            return mm;
        }

        void Build(TrackSpline spline, RaceEntry self)
        {
            float length = spline.Length;
            int steps = Mathf.Max(256, Mathf.CeilToInt(length * 0.5f));
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            var points = new Vector2[steps];
            for (int i = 0; i < steps; i++)
            {
                Vector3 p = spline.PoseAtDistance(length * i / steps).position;
                points[i] = new Vector2(p.x, p.z);
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }
            float range = Mathf.Max(max.x - min.x, max.y - min.y);
            worldScale = (TexSize - 2f * Pad) / Mathf.Max(1f, range);
            center = (min + max) * 0.5f;

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(new Color32[TexSize * TexSize]);
            for (int i = 0; i <= steps; i++)
            {
                var a = points[i % steps];
                var b = points[(i + 1) % steps];
                int n = Mathf.CeilToInt(Vector2.Distance(a, b) * worldScale);
                for (int k = 0; k <= n; k++)
                    Plot(tex, Vector2.Lerp(a, b, (float)k / Mathf.Max(1, n)), new Color(0.85f, 0.9f, 0.95f, 1f), 3);
            }
            Plot(tex, points[0], new Color(0.55f, 0.55f, 0.62f, 1f), 3); // linha de largada (cinza: nao confundir com o carro do jogador)
            tex.Apply();

            var dotParent = mapRect.transform;
            int count = race.entries.Count;
            dots = new RectTransform[count];
            dotOwners = new CarController[count];
            for (int i = 0; i < count; i++)
            {
                dotOwners[i] = race.entries[i].car;
                bool me = race.entries[i] == self;
                float d = me ? 19f : 10f;
                var dot = UIFactory.Panel("Dot" + i, dotParent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(d, d),
                    me ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.75f));
                dot.raycastTarget = false;
                dots[i] = (RectTransform)dot.transform;
            }

            var sr = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), TexSize / mapSize, 0, SpriteMeshType.FullRect);
            mapImage.sprite = sr;
            mapImage.type = Image.Type.Simple;
        }

        void Plot(Texture2D tex, Vector2 world, Color c, int radius)
        {
            int px = Mathf.RoundToInt((world.x - center.x) * worldScale + TexSize * 0.5f);
            int py = Mathf.RoundToInt((world.y - center.y) * worldScale + TexSize * 0.5f);
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = px + dx, y = py + dy;
                    if (x < 0 || y < 0 || x >= TexSize || y >= TexSize) continue;
                    tex.SetPixel(x, y, c);
                }
        }

        CarController[] dotOwners;
        Vector2 zoomCenter;
        float zoomScale = 1f;
        bool zoomInit;

        void LateUpdate()
        {
            if (dots == null || race == null || dotOwners == null) return;
            float factor = mapRect.rect.width / TexSize;

            // Modo dinamico (padrao): mostra apenas um trecho da pista centrado no SEU carro.
            if (!forceFull && SettingsManager.MinimapDynamic && self != null && self.car != null)
            {
                Vector3 sp = self.car.transform.position;
                if (!zoomInit) { zoomCenter = new Vector2(sp.x, sp.z); zoomInit = true; }
                zoomCenter = Vector2.Lerp(zoomCenter, new Vector2(sp.x, sp.z), Time.deltaTime * 4f);
                zoomScale = Mathf.MoveTowards(zoomScale, 3.2f, Time.deltaTime * 2f);
                mapRect.localScale = Vector3.one * zoomScale;
                mapRect.anchoredPosition = -(zoomCenter - center) * worldScale * factor * zoomScale;
            }
            else
            {
                zoomScale = 1f;
                zoomInit = false;
                mapRect.localScale = Vector3.one;
                mapRect.anchoredPosition = Vector2.zero;
            }

            for (int i = 0; i < dots.Length; i++)
            {
                var car = dotOwners[i];
                if (car == null) { dots[i].gameObject.SetActive(false); continue; }
                dots[i].gameObject.SetActive(true);
                Vector3 p = car.transform.position;
                float u = (p.x - center.x) * worldScale + TexSize * 0.5f;
                float v = (p.z - center.y) * worldScale + TexSize * 0.5f;
                dots[i].anchoredPosition = new Vector2((u - TexSize * 0.5f) * factor, (v - TexSize * 0.5f) * factor);
            }
        }
    }
}
