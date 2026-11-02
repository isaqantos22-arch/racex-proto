using System.Collections;
using RaceX.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RaceX.Car
{
    public enum CameraView { ThirdPerson = 0, FirstPerson = 1, SteeringWheel = 2, Front = 3, TCam = 4 }

    public class CarCamera : MonoBehaviour
    {
        [Header("Terceira pessoa")]
        public float distance = 8.4f;
        public float height = 3.4f;
        public float positionLerp = 6f;
        public float rotationLerp = 4f;
        public float thirdPersonFov = 66f;
        public float highSpeedFovBoost = 8f;

        [Header("Primeira pessoa / volante / frontal")]
        public float firstPersonFov = 80f;
        public float wheelFov = 75f;
        public float frontFov = 85f;
        public float maxHeadTilt = 4f;
        public float maxHeadBob = 0.02f;
        public float brakeDipAngle = 3f;
        public float brakeDipDrop = 0.05f;
        public float brakeDipReturn = 0.3f;

        public float fadeDuration = 0.2f;

        public CarController car;
        public CarVisuals visuals;
        public Image fadeImage;
        public bool persistSelection = true;
        public CameraView View { get; private set; }
        /// <summary>OLHAR PARA TRÁS (segurando o botão TRÁS do HUD ou a tecla B): câmera à frente do carro
        /// virada para trás, como nos jogos de F1 — mostra quem vem atrás sem tirar o carro da tela.</summary>
        public static bool LookBackHeld;
        bool lookingBack;
        public Camera Cam { get; private set; }

        Vector3 smoothPos;
        Quaternion smoothRot;
        float headTilt, headBob, dip;
        bool switching;

        public void Initialize(CarController target, Camera cam, CameraView startView)
        {
            car = target;
            visuals = target.GetComponent<CarVisuals>();
            Cam = cam;
            Cam.nearClipPlane = 0.05f;
            Cam.farClipPlane = SettingsManager.RenderDistance(SettingsManager.Quality);
            SetView(startView);
        }

        public void NextView()
        {
            if (switching) return;
            StartCoroutine(SwitchRoutine((CameraView)(((int)View + 1) % 5)));
        }

        public void ForceThirdPerson()
        {
            if (View == CameraView.ThirdPerson || switching) return;
            StartCoroutine(SwitchRoutine(CameraView.ThirdPerson));
        }

        IEnumerator SwitchRoutine(CameraView next)
        {
            switching = true;
            float half = fadeDuration * 0.5f;
            yield return Fade(0f, 1f, half);
            SetView(next);
            yield return Fade(1f, 0f, half);
            switching = false;
        }

        IEnumerator Fade(float from, float to, float duration)
        {
            if (fadeImage == null) yield break;
            fadeImage.enabled = true;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            fadeImage.color = new Color(0f, 0f, 0f, to);
            if (to <= 0f) fadeImage.enabled = false;
        }

        int pitShot = -1;

        /// <summary>PARADA NO BOX (carro desta câmera): 3 tomadas baixas como na TV — frente-esquerda, frente-direita e
        /// traseira — trocando a cada terço da parada, para ver os mecânicos. Na liberação volta à câmera escolhida.</summary>
        bool PitShot(Transform t)
        {
            float st = 0f, dur = 1f;
            bool stopped = !RaceX.Core.ReplaySystem.IsPlaying && RaceX.Core.PitLaneSystem.StopInfo(car, out st, out dur, out _);
            if (!stopped)
            {
                if (pitShot >= 0) { pitShot = -1; SetView(View); }
                return false;
            }
            int shot = Mathf.Clamp((int)(st / Mathf.Max(0.5f, dur / 3f)), 0, 2);
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = car.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 c = car.transform.position;
            Vector3 pos, look;
            if (RaceX.Core.PitLaneSystem.PracticeSession)
            {
                // treino: carro dentro da garagem (tomadas curtas, cabem entre as paredes)
                switch (shot)
                {
                    case 0: pos = c + fwd * 3.4f - right * 2.6f + Vector3.up * 0.45f; look = c + fwd * 0.5f + Vector3.up * 0.3f; break;
                    case 1: pos = c + fwd * 2.0f + right * 3.0f + Vector3.up * 0.5f; look = c + fwd * 0.6f + Vector3.up * 0.3f; break;
                    default: pos = c - fwd * 4.4f + right * 1.2f + Vector3.up * 0.6f; look = c + Vector3.up * 0.4f; break;
                }
            }
            else
            {
                // corrida: carro paralelo na faixa interna. Câmeras FORA das marcas da equipe (todas a até ~4,4 m do
                // centro do carro): de frente pelas luzes, do lado da garagem mais alta, e de trás pelo lado da faixa rápida.
                switch (shot)
                {
                    case 0: pos = c + fwd * 7.0f - right * 0.8f + Vector3.up * 1.5f; look = c + fwd * 0.5f + Vector3.up * 0.5f; break;
                    case 1: pos = c - right * 5.2f + fwd * 0.6f + Vector3.up * 2.3f; look = c + Vector3.up * 0.3f; break;
                    default: pos = c - fwd * 5.8f + right * 1.8f + Vector3.up * 1.0f; look = c + Vector3.up * 0.5f; break;
                }
            }
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            Cam.fieldOfView = 55f;
            if (shot != pitShot)
            {
                pitShot = shot;
                // vista de fora: piloto visível, volante do cockpit escondido
                if (visuals != null && visuals.driverHead != null) visuals.driverHead.SetActive(true);
                if (visuals != null && visuals.steeringWheel != null) visuals.steeringWheel.gameObject.SetActive(false);
            }
            return true;
        }

        public void SetView(CameraView view)
        {
            View = view;
            if (persistSelection) SettingsManager.LastCamera = (int)view;
            if (visuals != null && visuals.steeringWheel != null)
                visuals.steeringWheel.gameObject.SetActive(view == CameraView.SteeringWheel || view == CameraView.FirstPerson);
            if (visuals != null && visuals.driverHead != null)
                visuals.driverHead.SetActive(view == CameraView.ThirdPerson || view == CameraView.TCam);
            Snap();
        }

        void Snap()
        {
            if (car == null) return;
            ComputeThirdPerson(out smoothPos, out smoothRot);
            headTilt = headBob = dip = 0f;
        }

        void ComputeThirdPerson(out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = car.transform.forward;
            bool inGarage = RaceX.Core.PitLaneSystem.IsInGarageView(car);
            float d = inGarage ? 3.8f : distance;
            float h = inGarage ? 1.85f : height;
            pos = car.transform.position - fwd * d + Vector3.up * h;
            Vector3 look = car.transform.position + Vector3.up * (inGarage ? 0.85f : 1.0f) + fwd * (inGarage ? 4.5f : 6f);
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        void LateUpdate()
        {
            if (car == null || Cam == null) return;
            float dt = Time.deltaTime;
            Transform t = Cam.transform;
            if (PitShot(t)) return;

            switch (View)
            {
                case CameraView.ThirdPerson:
                {
                    ComputeThirdPerson(out Vector3 desired, out Quaternion desiredRot);
                    smoothPos = Vector3.Lerp(smoothPos, desired, 1f - Mathf.Exp(-positionLerp * dt));
                    Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
                    Vector3 lookTarget = car.transform.position + Vector3.up * 1.0f + fwd * 4f;
                    Vector3 pos = ResolveCollision(lookTarget, smoothPos);
                    Quaternion targetRot = Quaternion.LookRotation(lookTarget + (desiredRot * Vector3.forward) * 3f - pos, Vector3.up);
                    smoothRot = Quaternion.Slerp(smoothRot, targetRot, 1f - Mathf.Exp(-rotationLerp * 2f * dt));
                    t.SetPositionAndRotation(pos, smoothRot);
                    float boost = Mathf.Clamp01((car.SpeedKmh - 150f) / 100f) * highSpeedFovBoost;
                    Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, thirdPersonFov + boost, 1f - Mathf.Exp(-3f * dt));
                    break;
                }
                case CameraView.FirstPerson:
                case CameraView.SteeringWheel:
                {
                    Vector3 acc = car.LocalAcceleration;
                    float targetTilt = Mathf.Clamp(-acc.x / 9.81f * maxHeadTilt, -maxHeadTilt, maxHeadTilt);
                    headTilt = Mathf.Lerp(headTilt, targetTilt, 1f - Mathf.Exp(-5f * dt));
                    float targetBob = Mathf.Clamp(-acc.y * 0.002f, -maxHeadBob, maxHeadBob);
                    headBob = Mathf.Lerp(headBob, targetBob, 1f - Mathf.Exp(-12f * dt));
                    bool wheel = View == CameraView.SteeringWheel;
                    Transform anchor = visuals != null ? (wheel ? visuals.wheelCamAnchor : visuals.eyeAnchor) : car.transform;
                    float pitch = wheel ? -19f : -3f;
                    t.SetPositionAndRotation(anchor.position + car.transform.up * headBob, car.transform.rotation * Quaternion.Euler(pitch, 0f, headTilt));
                    Cam.fieldOfView = wheel ? wheelFov : firstPersonFov;
                    break;
                }
                case CameraView.TCam:
                {
                    // T-CAM: acima e atrás do capacete (o piloto aparece na tela), como a câmera da TV.
                    Vector3 eye = visuals != null && visuals.eyeAnchor != null ? visuals.eyeAnchor.position : car.transform.position + car.transform.up;
                    t.SetPositionAndRotation(eye + car.transform.up * 0.55f - car.transform.forward * 0.45f, car.transform.rotation * Quaternion.Euler(7f, 0f, 0f));
                    Cam.fieldOfView = 72f;
                    break;
                }
                case CameraView.Front:
                {
                    bool hardBrake = car.brakeInput > 0.6f && car.ForwardSpeed > 8f && car.LocalAcceleration.z < -4f;
                    dip = Mathf.MoveTowards(dip, hardBrake ? 1f : 0f, dt / brakeDipReturn);
                    Transform anchor = visuals != null ? visuals.hoodAnchor : car.transform;
                    t.SetPositionAndRotation(anchor.position - car.transform.up * brakeDipDrop * dip,
                        car.transform.rotation * Quaternion.Euler(2f + brakeDipAngle * dip, 0f, 0f));
                    Cam.fieldOfView = frontFov;
                    break;
                }
            }
            bool back = (LookBackHeld || KeyBack()) && !switching;
            if (back) LookBack(t);
            if (back != lookingBack)
            {
                lookingBack = back;
                if (visuals != null && visuals.driverHead != null)
                    visuals.driverHead.SetActive(back || View == CameraView.ThirdPerson || View == CameraView.TCam);
                if (!back) Snap();
            }
        }

        bool KeyBack()
        {
            try { return UnityEngine.Input.GetKey(KeyCode.B); } catch (System.Exception) { return false; }
        }

        void LookBack(Transform t)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = car.transform.forward;
            bool far = View == CameraView.ThirdPerson;
            Vector3 pos = car.transform.position + fwd * (far ? 6.5f : 2.4f) + Vector3.up * (far ? 2.3f : 1.25f);
            Vector3 look = car.transform.position - fwd * 14f + Vector3.up * 0.8f;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            Cam.fieldOfView = 70f;
        }

        Vector3 ResolveCollision(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return to;
            var hits = Physics.SphereCastAll(from, 0.25f, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = dist;
            foreach (var h in hits)
            {
                if (h.rigidbody != null && h.rigidbody == car.Body) continue;
                if (h.distance > 0f && h.distance < best) best = h.distance;
            }
            return from + dir / dist * Mathf.Max(0.5f, best - 0.1f);
        }
    }
}
