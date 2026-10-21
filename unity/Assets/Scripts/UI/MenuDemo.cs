using System.Collections;
using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RaceX.UI
{
    /// <summary>Modo vitrine do menu principal: enquanto o jogador não interage, mostra corridas de IA (10s por pista, fade, loop). Qualquer clique/tecla/toque volta ao carro de exposição.</summary>
    public class MenuDemo : MonoBehaviour
    {
        const float CycleSeconds = 10f;
        const float FadeSeconds = 0.6f;
        const float IdleResumeSeconds = 30f;
        const int DemoCars = 6;

        MainMenuUI menu;
        Camera cam;
        Image fade;
        GameObject demoRoot;
        Transform follow;
        int trackIndex;
        bool allowed;
        bool running;
        bool everStarted;
        float idle;
        Coroutine cycle;
        Vector3 camPos;
        Vector3 camLook;

        public bool Running => running;

        public static MenuDemo Create(MainMenuUI menu, Canvas canvas, Camera cam)
        {
            var go = new GameObject("MenuDemo");
            var demo = go.AddComponent<MenuDemo>();
            demo.menu = menu;
            demo.cam = cam;
            demo.fade = UIFactory.Panel("DemoFade", canvas.transform, Vector2.one / 2f, Vector2.one / 2f, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
            var rt = (RectTransform)demo.fade.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            demo.fade.raycastTarget = false;
            demo.fade.enabled = false;
            return demo;
        }

        /// <summary>Liga/desliga o modo vitrine (só na tela principal). Na primeira vez começa na hora; depois, só após 30s sem interação.</summary>
        public void SetAllowed(bool on)
        {
            allowed = on && !Application.isMobilePlatform;
            idle = 0f;
            if (!allowed) Stop();
            else if (!everStarted) Begin();
        }

        void Update()
        {
            if (!allowed) return;
            if (UserInput())
            {
                idle = 0f;
                if (running) Stop();
                return;
            }
            if (running) return;
            idle += Time.unscaledDeltaTime;
            if (idle >= IdleResumeSeconds) Begin();
        }

        static bool UserInput()
        {
            return UnityEngine.Input.anyKeyDown || UnityEngine.Input.touchCount > 0 || UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1);
        }

        void Begin()
        {
            if (running) return;
            running = true;
            everStarted = true;
            fade.transform.SetAsLastSibling();
            fade.enabled = true;
            fade.color = Color.black;
            menu.SetShowroomVisible(false);
            if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
            trackIndex = Random.Range(0, 3);
            cycle = StartCoroutine(CycleRoutine());
        }

        void Stop()
        {
            if (cycle != null) StopCoroutine(cycle);
            cycle = null;
            fade.enabled = false;
            DestroyWorld();
            if (!running) return;
            running = false;
            if (cam != null) cam.clearFlags = CameraClearFlags.SolidColor;
            RenderSettings.sun = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);
            menu.SetShowroomVisible(true);
        }

        void DestroyWorld()
        {
            if (demoRoot != null) DestroyImmediate(demoRoot);
            demoRoot = null;
            follow = null;
        }

        void OnDestroy()
        {
            DestroyWorld();
        }

        IEnumerator CycleRoutine()
        {
            while (true)
            {
                DestroyWorld();
                yield return null;
                BuildTrack(trackIndex);
                for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
                {
                    FollowCamera(true);
                    yield return null;
                }
                yield return FadeAlpha(fade.color.a, 0f, FadeSeconds);
                fade.enabled = false;
                for (float t = 0f; t < CycleSeconds; t += Time.deltaTime)
                {
                    FollowCamera(false);
                    yield return null;
                }
                yield return FadeAlpha(0f, 1f, FadeSeconds);
                trackIndex = (trackIndex + 1) % 3;
            }
        }

        void BuildTrack(int index)
        {
            demoRoot = new GameObject("MenuDemoRoot");
            var def = TrackLibrary.Get((TrackId)index);
            var builder = TrackBuilder.Build(def);
            builder.transform.SetParent(demoRoot.transform, true);

            var sun = new GameObject("DemoSun").AddComponent<Light>();
            sun.transform.SetParent(demoRoot.transform, false);
            sun.type = LightType.Directional;
            sun.color = def.sunColor;
            sun.intensity = def.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(def.sunEuler);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = def.ambientColor;
            RenderSettings.fog = def.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = def.fogColor;
            RenderSettings.fogDensity = def.fogDensity;

            var models = System.Enum.GetValues(typeof(CarModel));
            Transform[] cars = new Transform[DemoCars];
            for (int i = 0; i < DemoCars; i++)
            {
                var model = (CarModel)models.GetValue(Random.Range(0, models.Length));
                Pose pose = builder.GetGridPose(i);
                var car = CarFactory.Create(CarSpec.CreateDefault(model), Random.Range(0, CarPalette.Colors.Length), "DemoCar_" + i, pose.position, pose.rotation);
                car.transform.SetParent(demoRoot.transform, true);
                car.gameObject.AddComponent<CarAI>().Initialize(builder.Waypoints, null, Difficulty.Hard);
                cars[i] = car.transform;
            }
            follow = cars[Random.Range(0, DemoCars)];
        }

        void FollowCamera(bool snap)
        {
            if (follow == null || cam == null) return;
            Vector3 desired = follow.position - follow.forward * 8.5f + Vector3.up * 3f;
            Vector3 lookDesired = follow.position + follow.forward * 6f + Vector3.up * 1f;
            float k = snap ? 1f : 1f - Mathf.Exp(-4f * Time.deltaTime);
            camPos = Vector3.Lerp(camPos, desired, k);
            camLook = Vector3.Lerp(camLook, lookDesired, snap ? 1f : 1f - Mathf.Exp(-6f * Time.deltaTime));
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation((camLook - camPos).normalized));
        }

        IEnumerator FadeAlpha(float from, float to, float duration)
        {
            fade.enabled = true;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            fade.color = new Color(0f, 0f, 0f, to);
        }
    }
}
