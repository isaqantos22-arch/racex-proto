using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    public class SteeringWheelView : MonoBehaviour
    {
        public CarController car;
        public Transform wheelPivot;
        public float maxWheelAngle = 120f;
        public float smoothing = 10f;

        Transform leverPivot;
        float leverX;
        float current;

        public void Build(bool rectangular, Material mat, CarSpec spec = null)
        {
            BuildLever(mat, spec);
            wheelPivot = new GameObject("WheelPivot").transform;
            wheelPivot.SetParent(transform, false);
            var skin = MaterialLibrary.Get("HandPlaceholder", new Color(0.12f, 0.12f, 0.14f), 0.3f);
            var rimMat = MaterialLibrary.Get("WheelDark", new Color(0.055f, 0.055f, 0.075f), 0.45f);
            var markMat = MaterialLibrary.Get("WheelTopMark", new Color(0.75f, 0.08f, 0.08f), 0.5f);
            if (rectangular)
                CarFactory.Part(wheelPivot, "TopMark", new Vector3(0.085f, 0.045f, 0f), new Vector3(0.026f, 0.024f, 0.014f), markMat);
            else
                CarFactory.Part(wheelPivot, "TopMark", new Vector3(0f, 0.165f, 0f), new Vector3(0.026f, 0.036f, 0.026f), markMat);

            if (rectangular)
            {
                CarFactory.Part(wheelPivot, "Grip", Vector3.zero, new Vector3(0.28f, 0.14f, 0.04f), rimMat);
                CarFactory.Part(wheelPivot, "GripL", new Vector3(-0.15f, 0f, 0f), new Vector3(0.05f, 0.18f, 0.06f), rimMat);
                CarFactory.Part(wheelPivot, "GripR", new Vector3(0.15f, 0f, 0f), new Vector3(0.05f, 0.18f, 0.06f), rimMat);
                CarFactory.Part(wheelPivot, "Display", new Vector3(0f, 0.02f, -0.025f), new Vector3(0.1f, 0.05f, 0.01f),
                    MaterialLibrary.Get("WheelDisplay", Color.black, 0.9f, 0f, new Color(0f, 0.8f, 1f)));
                AddHand(wheelPivot, new Vector3(-0.16f, 0f, -0.02f), skin);
                AddHand(wheelPivot, new Vector3(0.16f, 0f, -0.02f), skin);
            }
            else
            {
                const int segments = 16;
                const float radius = 0.18f;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    var pos = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                    CarFactory.Part(wheelPivot, "Rim" + i, pos, new Vector3(0.075f, 0.03f, 0.03f), mat, new Vector3(0f, 0f, a * Mathf.Rad2Deg + 90f));
                }
                CarFactory.Part(wheelPivot, "Hub", Vector3.zero, new Vector3(0.07f, 0.02f, 0.07f), mat, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
                CarFactory.Part(wheelPivot, "SpokeL", new Vector3(-0.09f, 0f, 0f), new Vector3(0.18f, 0.025f, 0.015f), rimMat);
                CarFactory.Part(wheelPivot, "SpokeR", new Vector3(0.09f, 0f, 0f), new Vector3(0.18f, 0.025f, 0.015f), rimMat);
                CarFactory.Part(wheelPivot, "SpokeD", new Vector3(0f, -0.09f, 0f), new Vector3(0.025f, 0.18f, 0.015f), rimMat);
                AddHand(wheelPivot, new Vector3(-radius, 0.02f, -0.02f), skin);
                AddHand(wheelPivot, new Vector3(radius, 0.02f, -0.02f), skin);
            }
        }

        void BuildLever(Material mat, CarSpec spec)
        {
            bool paddles = spec != null && !spec.manualShifter;
            if (paddles)
            {
                CarFactory.Part(transform, "PaddleL", new Vector3(-0.17f, -0.03f, -0.06f), new Vector3(0.045f, 0.09f, 0.015f), mat);
                CarFactory.Part(transform, "PaddleR", new Vector3(0.17f, -0.03f, -0.06f), new Vector3(0.045f, 0.09f, 0.015f), mat);
                return;
            }
            Vector3 basePos = spec != null ? spec.driverEyeLocal : new Vector3(0f, 0.4f, 0f);
            leverPivot = new GameObject("GearLeverPivot").transform;
            leverPivot.SetParent(transform.parent, false);
            leverPivot.localPosition = transform.localPosition + new Vector3(0.34f, -0.34f, -0.06f);
            leverPivot.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            CarFactory.Part(leverPivot, "Gaiter", new Vector3(0f, 0.02f, 0f), new Vector3(0.075f, 0.06f, 0.075f), mat, null, PrimitiveType.Cylinder);
            CarFactory.Part(leverPivot, "Shaft", new Vector3(0f, 0.07f, 0f), new Vector3(0.026f, 0.12f, 0.026f), mat, null, PrimitiveType.Cylinder);
            CarFactory.Part(leverPivot, "Knob", new Vector3(0f, 0.14f, 0f), new Vector3(0.055f, 0.042f, 0.055f), MaterialLibrary.Get("Knob", new Color(0.85f, 0.7f, 0.1f), 0.6f), null, PrimitiveType.Sphere);
        }

        static void AddHand(Transform parent, Vector3 pos, Material skin)
        {
            var hand = CarFactory.Part(parent, "Hand", pos, new Vector3(0.075f, 0.09f, 0.05f), skin, null, PrimitiveType.Capsule).transform;
            for (int f = 0; f < 4; f++)
            {
                var finger = CarFactory.Part(hand, "Finger" + f, new Vector3(-0.018f + f * 0.013f, -0.028f, 0.012f), new Vector3(0.011f, 0.05f, 0.02f), skin, new Vector3(-38f, 0f, 0f), PrimitiveType.Capsule);
                CarFactory.Part(finger.transform, "FingerTip" + f, new Vector3(0f, -0.022f, 0.004f), new Vector3(0.009f, 0.034f, 0.017f), skin, new Vector3(-46f, 0f, 0f), PrimitiveType.Capsule);
            }
            CarFactory.Part(hand, "Thumb", new Vector3(0.045f, 0.004f, 0.014f), new Vector3(0.014f, 0.042f, 0.02f), skin, new Vector3(0f, -26f, -30f), PrimitiveType.Capsule);
            var forearm = CarFactory.Part(hand, "Forearm", new Vector3(-0.012f, -0.17f, -0.14f), new Vector3(0.075f, 0.30f, 0.085f), skin, new Vector3(-38f, 0f, 0f), PrimitiveType.Capsule).transform;
            CarFactory.Part(forearm, "UpperArm", new Vector3(0f, -0.22f, -0.16f), new Vector3(0.088f, 0.34f, 0.1f), skin, new Vector3(-16f, 0f, 0f), PrimitiveType.Capsule);
        }

        void LateUpdate()
        {
            if (car == null || wheelPivot == null) return;
            float target = -car.steerInput * maxWheelAngle;
            current = Mathf.LerpAngle(current, target, smoothing * Time.deltaTime);

            if (leverPivot != null)
            {
                // Portão do câmbio: R inclina para trás, marchas avançam; troca tem movimento rápido
                float gearX = car.Gear < 0 ? -42f : Mathf.Lerp(28f, -8f, Mathf.Clamp01((car.Gear - 1) / 5f));
                leverX = Mathf.MoveTowards(leverX, gearX, car.IsShifting ? 260f : 120f * Time.deltaTime);
                leverPivot.localRotation = Quaternion.Euler(leverX, 0f, 0f);
            }
            wheelPivot.localRotation = Quaternion.Euler(0f, 0f, current);
        }
    }
}
