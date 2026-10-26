using RaceX.Core;
using RaceX.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaceX.Input
{
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        int pointers;
        public bool IsPressed => pointers > 0;
        Image image;
        Color idle;

        void Awake()
        {
            image = GetComponent<Image>();
            if (image != null) idle = image.color;
        }

        public void OnPointerDown(PointerEventData e)
        {
            pointers++;
            if (image != null) image.color = idle * 1.6f;
        }

        public void OnPointerUp(PointerEventData e)
        {
            pointers = Mathf.Max(0, pointers - 1);
            if (pointers == 0 && image != null) image.color = idle;
        }

        void OnDisable() => pointers = 0;
    }

    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        public float radius = 120f;
        /// <summary>Fração do raio (0-1) que é ignorada perto do centro, para o toque inicial não virar o carro sozinho.</summary>
        public float deadZone = 0.18f;
        public float Horizontal { get; private set; }
        RectTransform rt;
        bool dragging;

        void Awake() => rt = (RectTransform)transform;

        void OnEnable()
        {
            Horizontal = 0f;
            dragging = false;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        void OnDisable()
        {
            Horizontal = 0f;
            dragging = false;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData e)
        {
            dragging = true;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragging) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local)) return;
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            if (knob != null) knob.anchoredPosition = clamped;
            float raw = clamped.x / radius;
            float abs = Mathf.Abs(raw);
            Horizontal = abs < deadZone ? 0f : Mathf.Sign(raw) * (abs - deadZone) / (1f - deadZone);
        }

        public void OnPointerUp(PointerEventData e)
        {
            dragging = false;
            Horizontal = 0f;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }
    }

    public class TouchControlsUI : MonoBehaviour
    {
        public const float TiltDeadZone = 3f;
        public const float TiltFullAngle = 30f;

        public ControlMode mode = ControlMode.Buttons;
        public float tiltSensitivity = 1f;

        TouchButton left, right, brake, handbrake, accel;
        VirtualJoystick joystick;
        RectTransform rootRt;
        bool allowTilt;
        static readonly System.Collections.Generic.List<TouchControlsUI> active = new System.Collections.Generic.List<TouchControlsUI>();

        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public bool Handbrake { get; private set; }
        public bool UsesAutoThrottle => AutoThrottleEnabled;

        public const string PrefAutoThrottle = "RaceX.AutoThrottle";

        public static bool AutoThrottleEnabled
        {
            get => PlayerPrefs.GetInt(PrefAutoThrottle, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefAutoThrottle, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static TouchControlsUI Create(Transform canvas, Rect viewport, ControlMode mode, bool allowTilt)
        {
            var root = UIFactory.Stretch("TouchControls", canvas);
            UIFactory.ApplySafeArea(root, viewport);
            var ui = root.gameObject.AddComponent<TouchControlsUI>();
            ui.rootRt = root;
            ui.allowTilt = allowTilt;
            ui.mode = mode == ControlMode.Tilt && !allowTilt ? ControlMode.Buttons : mode;
            ui.tiltSensitivity = SettingsManager.TiltSensitivity;
            ui.Build(root);
            active.Add(ui);
            return ui;
        }

        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() { active.Remove(this); }

        /// <summary>Troca o tipo de controle ao vivo (chamado pelo menu de pausa), sem recriar a cena.</summary>
        public void SetMode(ControlMode newMode)
        {
            newMode = newMode == ControlMode.Tilt && !allowTilt ? ControlMode.Buttons : newMode;
            if (newMode == mode) return;
            mode = newMode;
            for (int i = rootRt.childCount - 1; i >= 0; i--) Destroy(rootRt.GetChild(i).gameObject);
            left = right = brake = handbrake = accel = null;
            joystick = null;
            Build(rootRt);
        }

        /// <summary>Aplica o modo de controle atual (SettingsManager.ControlMode) a todos os controles de toque ativos na corrida.</summary>
        public static void ApplyModeToAll(ControlMode mode)
        {
            for (int i = 0; i < active.Count; i++) active[i].SetMode(mode);
        }

        void Build(RectTransform root)
        {
            var bl = new Vector2(0f, 0f);
            var br = new Vector2(1f, 0f);
            Vector2 big = new Vector2(220f, 220f), small = new Vector2(170f, 170f);

            if (mode == ControlMode.Buttons)
            {
                left = MakeButton(root, "SteerLeft", "<", bl, bl, new Vector2(40f, 40f), big);
                right = MakeButton(root, "SteerRight", ">", br, br, new Vector2(-40f, 40f), big);
            }
            else if (mode == ControlMode.Joystick)
            {
                var area = UIFactory.Panel("Joystick", root, bl, bl, new Vector2(60f, 60f), new Vector2(300f, 300f), new Color(1f, 1f, 1f, 0.12f));
                area.raycastTarget = true; // o VirtualJoystick (handlers de pointer) mora nesta área
                var knob = UIFactory.Panel("Knob", area.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f), new Color(1f, 1f, 1f, 0.45f));
                knob.raycastTarget = false;
                joystick = area.gameObject.AddComponent<VirtualJoystick>();
                joystick.knob = (RectTransform)knob.transform;
            }

            // PEDADOS À DIREITA: ACEL no canto, FREIO AO LADO dele.
            // F. MÃO fica na parte de BAIXO, no lugar onde antigamente era o freio (troca de lugares).
            accel = MakeButton(root, "Throttle", "ACEL", br, br, new Vector2(-40f, 40f), big);
            brake = MakeButton(root, "Brake", "FREIO", br, br, new Vector2(-270f, 40f), small);
            Vector2 hbPos = mode == ControlMode.Buttons ? new Vector2(280f, 40f)
                          : mode == ControlMode.Joystick ? new Vector2(380f, 40f)
                          : new Vector2(40f, 40f);
            handbrake = MakeButton(root, "Handbrake", "F. MÃO", bl, bl, hbPos, small);

            // Aceleração automática mudou de lugar: agora é opção em CONFIGURAÇÕES (não há toggle em corrida).

#if ENABLE_INPUT_SYSTEM
            if (mode == ControlMode.Tilt && Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
#endif
        }

        static TouchButton MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var img = UIFactory.Panel(name, parent, anchor, pivot, pos, size, new Color(0.1f, 0.12f, 0.16f, 0.45f));
            img.raycastTarget = true; // botão de toque PRECISA capturar o pointer (Panel vem false p/ decoração)
            UIFactory.Label("Label", img.transform, label, 36, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return img.gameObject.AddComponent<TouchButton>();
        }

        void Update()
        {
            float steer = 0f;
            switch (mode)
            {
                case ControlMode.Buttons:
                    steer = (right != null && right.IsPressed ? 1f : 0f) - (left != null && left.IsPressed ? 1f : 0f);
                    break;
                case ControlMode.Joystick:
                    steer = joystick != null ? joystick.Horizontal : 0f;
                    break;
                case ControlMode.Tilt:
                    steer = ReadTilt();
                    break;
            }
            Steer = steer;
            Brake = brake != null && brake.IsPressed ? 1f : 0f;
            Handbrake = handbrake != null && handbrake.IsPressed;
            Throttle = AutoThrottleEnabled
                ? (Brake > 0f ? 0f : 1f)
                : (accel != null && accel.IsPressed ? 1f : 0f);
        }

        float ReadTilt()
        {
#if ENABLE_INPUT_SYSTEM
            var acc = Accelerometer.current;
            if (acc == null || !acc.enabled) return 0f;
            Vector3 g = acc.acceleration.ReadValue();
            float angle = Screen.orientation == ScreenOrientation.LandscapeRight
                ? Mathf.Atan2(-g.y, g.x) * Mathf.Rad2Deg
                : Mathf.Atan2(g.y, -g.x) * Mathf.Rad2Deg;
            float abs = Mathf.Abs(angle);
            if (abs < TiltDeadZone) return 0f;
            float v = (abs - TiltDeadZone) / (TiltFullAngle - TiltDeadZone) * tiltSensitivity;
            return Mathf.Clamp(Mathf.Sign(angle) * v, -1f, 1f);
#else
            return 0f;
#endif
        }
    }
}
