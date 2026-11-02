using RaceX.Car;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    public class RacingLineGuide : MonoBehaviour
    {
        const int DashCount = 28;
        const float SampleStep = 4f;
        const float DashSpacing = 6f;
        const float Decel = 11f;

        public static bool Enabled => PlayerPrefs.GetInt("RaceX.RacingLine", 1) == 1;

        CarController car;
        RaceManager race;
        TrackSpline spline;
        float[] profile;
        Transform[] dashes;
        MeshRenderer[] renderers;
        int[] dashColor;
        Material[] mats;
        int hint = -1;
        float timer;
        bool shown;
        Camera ownerCam; // tela dividida: só esta câmera desenha estes traços (null = todas)

        public void Initialize(CarController car, RaceManager race, Camera ownerCamera = null)
        {
            this.car = car;
            this.race = race;
            ownerCam = ownerCamera;
            if (ownerCam != null) UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            try
            {
                if (!Enabled || race == null || race.Track == null || race.Track.Waypoints == null) { enabled = false; return; }
                spline = race.Track.Waypoints.Spline;
                if (spline == null || spline.Length < 50f) { enabled = false; return; }
                BuildProfile();
                if (!BuildDashes()) { enabled = false; return; }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[RacingLine] desativada: " + ex.Message);
                enabled = false;
            }
        }

        void BuildProfile()
        {
            int n = Mathf.Max(8, Mathf.CeilToInt(spline.Length / SampleStep));
            profile = new float[n];
            int trackId = race.Settings != null ? (int)race.Settings.track : 0;
            for (int i = 0; i < n; i++) profile[i] = CarAI.IdealSpeedAt(spline, i * SampleStep, car, trackId);
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--)
                {
                    float next = profile[(i + 1) % n];
                    profile[i] = Mathf.Min(profile[i], Mathf.Sqrt(next * next + 2f * Decel * SampleStep));
                }
        }

        bool BuildDashes()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null) return false;
            // 4ª cor: AZUL do caminho até o box (pit pedido/chamado)
            var colors = new[] { new Color(0.15f, 0.95f, 0.3f), new Color(1f, 0.85f, 0.1f), new Color(1f, 0.15f, 0.1f), new Color(0.1f, 0.42f, 1f) };
            mats = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                var m = new Material(shader);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colors[i]);
                if (m.HasProperty("_Color")) m.SetColor("_Color", colors[i]);
                mats[i] = m;
            }
            var mesh = new Mesh { name = "RacingDash" };
            mesh.vertices = new[] { new Vector3(-0.25f, 0f, -1.1f), new Vector3(0.25f, 0f, -1.1f), new Vector3(-0.25f, 0f, 1.1f), new Vector3(0.25f, 0f, 1.1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            var root = new GameObject("RacingLineGuide").transform;
            dashes = new Transform[DashCount];
            renderers = new MeshRenderer[DashCount];
            dashColor = new int[DashCount];
            for (int i = 0; i < DashCount; i++)
            {
                var go = new GameObject("Dash" + i);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats[0];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                dashes[i] = go.transform;
                renderers[i] = r;
            }
            return true;
        }

        /// <summary>URP chama antes de cada câmera: liga os traços só na câmera do dono.</summary>
        void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (renderers == null || ownerCam == null) return;
            bool on = shown && cam == ownerCam;
            for (int i = 0; i < DashCount; i++) if (renderers[i] != null) renderers[i].enabled = on;
        }

        void OnDestroy()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (dashes != null && dashes.Length > 0 && dashes[0] != null) Destroy(dashes[0].parent.gameObject);
        }

        void SetShown(bool on)
        {
            if (shown == on) return;
            shown = on;
            for (int i = 0; i < DashCount; i++) renderers[i].enabled = on;
        }

        void Update()
        {
            if (car == null || race == null || dashes == null) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 1f / 15f;
            bool active = !car.pitControlled && (race.State == RaceState.Racing || race.State == RaceState.Countdown);
            SetShown(active);
            if (!active) return;
            float L = spline.Length;
            float dist = race.Track.Waypoints.DistanceAlong(car.transform.position, ref hint);
            float speed = car.SpeedKmh / 3.6f;
            bool human = car.GetComponent<RaceX.Input.PlayerInputHandler>() != null || RaceX.Core.SafetyCar.AutopilotOn(car);
            for (int i = 0; i < DashCount; i++)
            {
                float d = Mathf.Repeat(dist + 8f + i * DashSpacing, L);
                // CAMINHO DO BOX: o próprio tracejado fica AZUL e mais largo, em cima da linha principal, e só desvia na
                // entrada do pit (antes era uma faixa fina separada, correndo ao lado da principal).
                bool blue = human && RaceX.Core.PitLaneSystem.EntryGuideBlueAt(d);
                float lat = blue ? RaceX.Core.PitLaneSystem.EntryGuideLat(d) : 0f;
                var pose = spline.PoseAtDistance(d, lat);
                Quaternion rot = pose.rotation;
                if (blue && lat != 0f)
                {
                    var ahead = spline.PoseAtDistance(Mathf.Repeat(d + 2f, L), RaceX.Core.PitLaneSystem.EntryGuideLat(d + 2f));
                    Vector3 dir = ahead.position - pose.position; dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
                dashes[i].SetPositionAndRotation(pose.position + Vector3.up * (blue ? 0.045f : 0.04f), rot);
                dashes[i].localScale = blue ? new Vector3(2.0f, 1f, 1.25f) : Vector3.one;
                float v = profile[Mathf.Clamp(Mathf.RoundToInt(d / SampleStep), 0, profile.Length - 1) % profile.Length];
                int c = blue ? 3 : speed > v * 1.04f ? 2 : speed > v * 0.9f ? 1 : 0;
                if (c != dashColor[i] || renderers[i].sharedMaterial != mats[c])
                {
                    dashColor[i] = c;
                    renderers[i].sharedMaterial = mats[c];
                }
            }
        }
    }
}
