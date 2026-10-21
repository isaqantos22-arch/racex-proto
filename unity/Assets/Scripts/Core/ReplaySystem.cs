using System.Collections.Generic;
using RaceX.Car;
using RaceX.UI;
using UnityEngine;

namespace RaceX.Core
{
    /// <summary>Replay da corrida: grava posicoes de TODOS os carros (lista fixa por identidade) e reproduz com troca de carro (C), visao (V: 3a pessoa/olho/volante/capo) e orbita. ESC sai.</summary>
    public class ReplaySystem : MonoBehaviour
    {
        class Snapshot
        {
            public float t;
            public Vector3[] pos;
            public Quaternion[] rot;
        }

        RaceManager race;
        RectTransform resultsPanel;
        RectTransform overlay;
        readonly List<Snapshot> snaps = new List<Snapshot>();
        CarController[] carList = new CarController[0];
        float baseT;
        float playT;
        bool playing;
        bool recording;
        int target;
        public CameraView camView = CameraView.ThirdPerson;
        static ReplaySystem active;

        public static void StartRecording(RaceManager race)
        {
            if (race == null) return;
            if (active == null)
            {
                active = new GameObject("ReplayRecorder").AddComponent<ReplaySystem>();
                DontDestroyOnLoad(active.gameObject);
            }
            if (active.race == null || active.race != race)
            {
                active.snaps.Clear();
                active.race = race;
                active.carList = CarsOf(race);
                active.target = 0;
                active.camView = CameraView.ThirdPerson;
            }
            active.recording = true;
            active.playing = false;
        }

        static CarController[] CarsOf(RaceManager race)
        {
            var list = new System.Collections.Generic.List<CarController>();
            foreach (var e in race.entries) if (e.car != null) list.Add(e.car);
            return list.ToArray();
        }

        public static ReplaySystem Begin(RaceManager race, CarCamera cam, RectTransform resultsPanel)
        {
            var rs = active;
            if (rs == null || rs.race != race || rs.snaps.Count < 2)
            {
                rs = new GameObject("ReplaySystem").AddComponent<ReplaySystem>();
                rs.race = race;
                rs.carList = CarsOf(race);
            }
            rs.recording = false;
            rs.playing = true;
            rs.resultsPanel = resultsPanel;
            rs.playT = 0f;
            if (rs.snaps.Count > 0) rs.baseT = rs.snaps[0].t;
            rs.BuildOverlay();
            if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
            return rs;
        }

        void BuildOverlay()
        {
            var canvasGo = new GameObject("ReplayCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var c = new Vector2(0.5f, 0.5f);
            var panel = UIFactory.Panel("ReplayOverlay", canvasGo.transform, c, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1180f, 76f), new Color(0f, 0f, 0f, 0.62f));
            UIFactory.Label("Title", panel.transform, "REPLAY   C: carro   V: camera   ESC: sair", 26, TextAnchor.MiddleCenter,
                c, c, Vector2.zero, new Vector2(760f, 60f), Color.white);
            MenuTheme.StyledButton((RectTransform)panel.transform, "ReplayCam", "CARRO", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(460f, 0f), new Vector2(200f, 58f), 24,
                () => target = (target + 1) % (carList.Length + 1));
            MenuTheme.StyledButton((RectTransform)panel.transform, "ReplayView", "VISAO", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(675f, 0f), new Vector2(200f, 58f), 24,
                () => camView = (CameraView)(((int)camView + 1) % 4));
            MenuTheme.StyledButton((RectTransform)panel.transform, "ReplayExit", "SAIR", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(890f, 0f), new Vector2(200f, 58f), 24,
                Exit);
            overlay = (RectTransform)panel.transform;
        }

        void Exit()
        {
            playing = false;
            if (overlay != null) Destroy(overlay.gameObject);
            if (resultsPanel != null) resultsPanel.gameObject.SetActive(true);
        }

        void Update()
        {
            if (recording) { Store(); return; }
            if (!playing) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) target = (target + 1) % (carList.Length + 1);
            if (UnityEngine.Input.GetKeyDown(KeyCode.V)) camView = (CameraView)(((int)camView + 1) % 4);
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
            if (snaps.Count < 2 || carList.Length < 2) return;

            playT += Time.deltaTime;
            float end = snaps[snaps.Count - 1].t - baseT;
            if (playT > end) playT = 0f;
            float targetT = baseT + playT;
            int b = 0;
            while (b < snaps.Count - 2 && snaps[b + 1].t < targetT) b++;
            var a = snaps[b];
            var c = snaps[b + 1];
            float f = Mathf.Clamp01((targetT - a.t) / Mathf.Max(0.0001f, c.t - a.t));

            for (int i = 0; i < carList.Length; i++)
            {
                if (carList[i] == null) continue;
                carList[i].transform.position = Vector3.Lerp(a.pos[i], c.pos[i], f);
                carList[i].transform.rotation = Quaternion.Slerp(a.rot[i], c.rot[i], f);
            }

            var cam = Camera.main;
            if (cam == null) return;
            if (target < carList.Length && carList[target] != null)
            {
                var t = carList[target].transform;
                var visuals = carList[target].GetComponent<CarVisuals>();
                Transform anchor = t;
                float pitch = 0f;
                switch (camView)
                {
                    case CameraView.FirstPerson:
                        if (visuals != null && visuals.eyeAnchor != null) anchor = visuals.eyeAnchor;
                        pitch = -3f;
                        break;
                    case CameraView.SteeringWheel:
                        if (visuals != null && visuals.wheelCamAnchor != null) anchor = visuals.wheelCamAnchor;
                        pitch = -19f;
                        break;
                    case CameraView.Front:
                        if (visuals != null && visuals.hoodAnchor != null) anchor = visuals.hoodAnchor;
                        pitch = 2f;
                        break;
                }
                if (camView == CameraView.ThirdPerson)
                {
                    cam.transform.position = t.position - t.forward * 7.5f + Vector3.up * 2.8f;
                    cam.transform.LookAt(t.position + Vector3.up * 0.8f);
                }
                else
                {
                    cam.transform.SetPositionAndRotation(anchor.position, t.rotation * Quaternion.Euler(pitch, 0f, 0f));
                }
            }
            else
            {
                float ang = Time.time * 0.2f;
                var center = Vector3.zero;
                int n = 0;
                foreach (var c2 in carList)
                    if (c2 != null) { center += c2.transform.position; n++; }
                center /= Mathf.Max(1, n);
                cam.transform.position = center + new Vector3(Mathf.Sin(ang) * 22f, 9f, Mathf.Cos(ang) * 22f);
                cam.transform.LookAt(center + Vector3.up * 1.2f);
            }
        }

        void Store()
        {
            if (race == null || carList.Length == 0) { carList = CarsOf(race); return; }
            var s = new Snapshot
            {
                t = Time.time,
                pos = new Vector3[carList.Length],
                rot = new Quaternion[carList.Length],
            };
            for (int i = 0; i < carList.Length; i++)
            {
                if (carList[i] == null) continue;
                s.pos[i] = carList[i].transform.position;
                s.rot[i] = carList[i].transform.rotation;
            }
            snaps.Add(s);
            if (snaps.Count == 1) baseT = s.t;
        }
    }
}
