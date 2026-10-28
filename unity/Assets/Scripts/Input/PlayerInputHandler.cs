using System;
using RaceX.Car;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaceX.Input
{
    public class PlayerInputHandler : MonoBehaviour
    {
        public const float TouchSteerRampTime = 0.25f;

        public CarController car;
        public CarCamera carCamera;
        public TouchControlsUI touch;
        public string actionMap = "Player";

        public event Action PausePressed;

        float touchSteer;

#if ENABLE_INPUT_SYSTEM
        InputActionAsset asset;
        InputAction steer, throttle, brake, handbrake, changeCamera, pause;

        void Awake()
        {
            var source = Resources.Load<InputActionAsset>("InputActions");
            if (source == null)
            {
                Debug.LogError("RaceX: Assets/Resources/InputActions.inputactions não encontrado.");
                return;
            }
            asset = Instantiate(source);
        }

        void OnEnable() => BindMap();

        public void SetActionMap(string mapName, InputDevice[] devices = null)
        {
            actionMap = mapName;
            if (asset == null) return;
            if (devices != null) asset.devices = devices;
            BindMap();
        }

        void BindMap()
        {
            if (asset == null) return;
            asset.Disable();
            var map = asset.FindActionMap(actionMap, true);
            steer = map.FindAction("Steer");
            throttle = map.FindAction("Throttle");
            brake = map.FindAction("Brake");
            handbrake = map.FindAction("Handbrake");
            changeCamera = map.FindAction("ChangeCamera");
            pause = map.FindAction("Pause");
            map.Enable();
        }

        void OnDisable()
        {
            if (asset != null) asset.Disable();
        }

        void OnDestroy()
        {
            if (asset != null) Destroy(asset);
        }
#endif

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.P) && car != null)
                RaceX.Core.PitLaneSystem.RequestPit(car);
            if (car == null) return;
            float s = 0f, th = 0f, br = 0f;
            bool hb = false, cam = false, pz = false;

#if ENABLE_INPUT_SYSTEM
            if (steer != null)
            {
                s = steer.ReadValue<float>();
                th = throttle.ReadValue<float>();
                br = brake.ReadValue<float>();
                hb = handbrake.IsPressed();
                cam = changeCamera.WasPressedThisFrame();
                pz = pause.WasPressedThisFrame();
            }
#endif

            if (touch != null && touch.isActiveAndEnabled)
            {
                float speedFactor = Mathf.Clamp01(car.SpeedKmh / 200f);
                float target = touch.Steer * Mathf.Lerp(1f, 0.55f, speedFactor);
                touchSteer = Mathf.MoveTowards(touchSteer, target, Time.deltaTime / TouchSteerRampTime);
                if (Mathf.Abs(s) < 0.01f) s = touchSteer;
                if (th < 0.01f && br < 0.01f)
                {
                    th = touch.Throttle;
                    br = touch.Brake;
                }
                hb |= touch.Handbrake;
            }

            car.steerInput = Mathf.Clamp(s, -1f, 1f);
            car.throttleInput = Mathf.Clamp01(th);
            car.brakeInput = Mathf.Clamp01(br);
            // Piloto automático do pit (entrada/box/saída): o freio de mão do jogador NÃO interfere —
            // segurar F. MÃO travava as rodas traseiras e o carro não saía do box.
            car.handbrake = hb && !car.pitControlled;

            if (cam && carCamera != null) carCamera.NextView();
            if (pz) PausePressed?.Invoke();
        }
    }
}
