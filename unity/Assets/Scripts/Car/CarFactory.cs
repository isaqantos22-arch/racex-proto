using System.Collections.Generic;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    public class CarVisuals : MonoBehaviour
    {
        public List<Renderer> paintRenderers = new List<Renderer>();
        public Transform eyeAnchor;
        public Transform wheelCamAnchor;
        public Transform hoodAnchor;
        public SteeringWheelView steeringWheel;
        public GameObject driverHead;

        public readonly List<KeyValuePair<Renderer, int>> paintSlots = new List<KeyValuePair<Renderer, int>>();

        public void HideProceduralWheel()
        {
            if (steeringWheel != null) steeringWheel.gameObject.SetActive(false);
        }

        public void SetColor(Color c)
        {
            foreach (var r in paintRenderers) if (r != null) MaterialLibrary.ApplyPaint(r, c);
            foreach (var s in paintSlots) if (s.Key != null) MaterialLibrary.ApplyPaint(s.Key, c, s.Value);
        }
    }

    public static class CarFactory
    {
        const float GroundClearance = 0.12f;

        public static CarController Create(CarSpec spec, int colorIndex, string name, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);
            root.layer = LayerMask.NameToLayer("Default");

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = spec.massKg;

            var visuals = root.AddComponent<CarVisuals>();
            var paint = MaterialLibrary.Get("CarPaint", Color.white, 0.88f, 0.55f);
            var black = MaterialLibrary.Get("CarBlack", new Color(0.05f, 0.05f, 0.06f), 0.4f);
            var glass = MaterialLibrary.Get("CarGlass", new Color(0.08f, 0.1f, 0.13f), 0.95f, 0.2f);
            var tire = MaterialLibrary.Get("Tire", new Color(0.07f, 0.07f, 0.07f), 0.15f);
            var rim = MaterialLibrary.Get("Rim", new Color(0.1f, 0.1f, 0.11f), 0.8f, 0.85f);
            var headlight = MaterialLibrary.Get("Headlight", Color.white, 0.9f, 0f, new Color(2f, 2f, 1.8f));
            var taillight = MaterialLibrary.Get("Taillight", new Color(0.6f, 0f, 0f), 0.9f, 0f, new Color(1.5f, 0f, 0f));
            var glassDark = MaterialLibrary.Get("CarGlassTint", new Color(0.25f, 0.32f, 0.4f, 0.3f), 0.92f, 0.1f);
            glassDark.SetFloat("_Surface", 1f);
            glassDark.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glassDark.renderQueue = 3000;
            if (glassDark.HasProperty("_SrcBlend")) glassDark.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (glassDark.HasProperty("_DstBlend")) glassDark.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (glassDark.HasProperty("_ZWrite")) glassDark.SetFloat("_ZWrite", 0f);
            glassDark.SetOverrideTag("RenderType", "Transparent");
            var exhaustTip = MaterialLibrary.GetPbr("ExhaustTip", new Color(0.12f, 0.12f, 0.13f), 0.8f, 0.6f);

            float wr = spec.wheelRadius;
            float bodyBottom = -wr + GroundClearance;
            Vector3 size = spec.bodySize;
            var bodyRoot = new GameObject("Body").transform;
            bodyRoot.SetParent(root.transform, false);

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y * 0.75f, size.z);
            col.center = new Vector3(0f, bodyBottom + size.y * 0.375f, 0f);

            var roleMaterials = new Dictionary<ModelMaterialRole, Material>
            {
                { ModelMaterialRole.Paint, paint }, { ModelMaterialRole.Black, black }, { ModelMaterialRole.Tire, tire },
                { ModelMaterialRole.Glass, glassDark }, { ModelMaterialRole.Metal, rim },
                { ModelMaterialRole.Headlight, headlight }, { ModelMaterialRole.Taillight, taillight }
            };
            // Carro importado: as rodas ficam onde o modelo tem as rodas (medido no FBX). Carro procedural: geometria da spec.
            Vector3[] anchors = ImportedCarModels.TryMeasureWheels(spec.model, size.z, -wr);
            if (anchors == null)
            {
                anchors = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    bool front = i < 2;
                    anchors[i] = new Vector3((i % 2 == 0 ? -1f : 1f) * spec.trackWidth * 0.5f, 0f, (front ? 1f : -1f) * spec.wheelbase * 0.5f);
                }
            }
            var imported = ImportedCarModels.BuildInto(bodyRoot, spec.model, size.z, -wr, roleMaterials, anchors);
            if (imported != null)
            {
                visuals.paintSlots.AddRange(imported.paintSlots);
            }
            else
            {
                switch (spec.model)
                {
                case CarModel.F1:
                    visuals.paintRenderers.Add(Part(bodyRoot, "Tub", new Vector3(0, bodyBottom + 0.28f, 0.3f), new Vector3(0.62f, 0.42f, size.z * 0.62f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "Nose", new Vector3(0, bodyBottom + 0.2f, size.z * 0.44f), new Vector3(0.34f, 0.2f, size.z * 0.2f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "SidepodL", new Vector3(-0.52f, bodyBottom + 0.24f, 0.1f), new Vector3(0.42f, 0.34f, size.z * 0.4f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "SidepodR", new Vector3(0.52f, bodyBottom + 0.24f, 0.1f), new Vector3(0.42f, 0.34f, size.z * 0.4f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "EngineCover", new Vector3(0, bodyBottom + 0.5f, -1.2f), new Vector3(0.4f, 0.5f, 1.8f), paint));
                    Part(bodyRoot, "FrontWing", new Vector3(0, bodyBottom + 0.06f, size.z * 0.48f), new Vector3(1.85f, 0.05f, 0.55f), black);
                    Part(bodyRoot, "FrontWingFlap", new Vector3(0, bodyBottom + 0.14f, size.z * 0.45f), new Vector3(1.7f, 0.04f, 0.3f), paint);
                    Part(bodyRoot, "RearWing", new Vector3(0, bodyBottom + 0.95f, -size.z * 0.47f), new Vector3(1.5f, 0.06f, 0.5f), paint);
                    Part(bodyRoot, "RearWingPylon", new Vector3(0, bodyBottom + 0.55f, -size.z * 0.46f), new Vector3(0.08f, 0.55f, 0.25f), black);
                    Part(bodyRoot, "EndplateL", new Vector3(-0.75f, bodyBottom + 0.85f, -size.z * 0.47f), new Vector3(0.04f, 0.45f, 0.55f), black);
                    Part(bodyRoot, "EndplateR", new Vector3(0.75f, bodyBottom + 0.85f, -size.z * 0.47f), new Vector3(0.04f, 0.45f, 0.55f), black);
                    Part(bodyRoot, "Halo", new Vector3(0, bodyBottom + 0.72f, 0.28f), new Vector3(0.55f, 0.05f, 0.7f), black);
                    Part(bodyRoot, "RollHoop", new Vector3(0, bodyBottom + 0.62f, -0.55f), new Vector3(0.3f, 0.35f, 0.25f), black);
                    Part(bodyRoot, "RainLight", new Vector3(0, bodyBottom + 0.25f, -size.z * 0.5f), new Vector3(0.12f, 0.1f, 0.04f), taillight);
                    Part(bodyRoot, "RoofCam", new Vector3(0, bodyBottom + 0.82f, -1.2f), new Vector3(0.13f, 0.06f, 0.13f), black);
                    Part(bodyRoot, "SideCamL", new Vector3(-0.62f, bodyBottom + 0.4f, 1.0f), new Vector3(0.05f, 0.05f, 0.05f), black);
                    Part(bodyRoot, "SideCamR", new Vector3(0.62f, bodyBottom + 0.4f, 1.0f), new Vector3(0.05f, 0.05f, 0.05f), black);
                    break;
                case CarModel.Falcon:
                    visuals.paintRenderers.Add(Part(bodyRoot, "SillBody", new Vector3(0, bodyBottom + 0.35f, -size.z * 0.285f), new Vector3(size.x, 0.7f, size.z * 0.425f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "CowlLow", new Vector3(0, bodyBottom + 0.275f, -size.z * 0.03f), new Vector3(size.x * 0.98f, 0.55f, size.z * 0.09f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "Cabin", new Vector3(0, bodyBottom + 0.924f, -0.35f), new Vector3(size.x * 0.86f, 0.468f, size.z * 0.55f), glassDark));
                    visuals.paintRenderers.Add(Part(bodyRoot, "HoodFront", new Vector3(0, bodyBottom + 0.21f, size.z * 0.275f), new Vector3(size.x * 0.96f, 0.42f, size.z * 0.45f), paint));
                    Part(bodyRoot, "Windshield", new Vector3(0, bodyBottom + 0.62f, 0.5f), new Vector3(size.x * 0.8f, 0.41f, 0.05f), glassDark, new Vector3(-30f, 0, 0));
                    Part(bodyRoot, "SideWindows", new Vector3(0, bodyBottom + 0.924f, -0.35f), new Vector3(size.x * 0.87f, 0.3f, size.z * 0.45f), glassDark);
                    AddRoadDetail(bodyRoot, spec, bodyBottom, paint, black, exhaustTip);
                    break;
                case CarModel.Titan:
                    visuals.paintRenderers.Add(Part(bodyRoot, "SillBody", new Vector3(0, bodyBottom + 0.3f, -size.z * 0.285f), new Vector3(size.x, 0.6f, size.z * 0.425f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "CowlLow", new Vector3(0, bodyBottom + 0.25f, -size.z * 0.03f), new Vector3(size.x * 0.98f, 0.5f, size.z * 0.09f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "Cabin", new Vector3(0, bodyBottom + 0.779f, -0.4f), new Vector3(size.x * 0.78f, 0.378f, size.z * 0.38f), glassDark));
                    visuals.paintRenderers.Add(Part(bodyRoot, "HoodFront", new Vector3(0, bodyBottom + 0.19f, size.z * 0.275f), new Vector3(size.x * 0.96f, 0.38f, size.z * 0.45f), paint));
                    Part(bodyRoot, "Windshield", new Vector3(0, bodyBottom + 0.55f, 0.45f), new Vector3(size.x * 0.74f, 0.38f, 0.05f), glassDark, new Vector3(-55f, 0, 0));
                    Part(bodyRoot, "SideWindows", new Vector3(0, bodyBottom + 0.779f, -0.4f), new Vector3(size.x * 0.79f, 0.24f, size.z * 0.3f), glassDark);
                    Part(bodyRoot, "WingL", new Vector3(-0.7f, bodyBottom + 0.85f, -size.z * 0.46f), new Vector3(0.05f, 0.3f, 0.2f), black);
                    Part(bodyRoot, "WingR", new Vector3(0.7f, bodyBottom + 0.85f, -size.z * 0.46f), new Vector3(0.05f, 0.3f, 0.2f), black);
                    visuals.paintRenderers.Add(Part(bodyRoot, "RearWing", new Vector3(0, bodyBottom + 1.0f, -size.z * 0.46f), new Vector3(size.x * 0.9f, 0.04f, 0.35f), paint));
                    AddRoadDetail(bodyRoot, spec, bodyBottom, paint, black, exhaustTip);
                    break;
                default:
                    visuals.paintRenderers.Add(Part(bodyRoot, "Monocoque", new Vector3(0, bodyBottom + 0.25f, 0), new Vector3(0.75f, 0.45f, size.z * 0.75f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "Nose", new Vector3(0, bodyBottom + 0.18f, size.z * 0.42f), new Vector3(0.35f, 0.22f, size.z * 0.25f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "PodL", new Vector3(-0.55f, bodyBottom + 0.22f, -0.3f), new Vector3(0.45f, 0.35f, 1.6f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "PodR", new Vector3(0.55f, bodyBottom + 0.22f, -0.3f), new Vector3(0.45f, 0.35f, 1.6f), paint));
                    visuals.paintRenderers.Add(Part(bodyRoot, "EngineCover", new Vector3(0, bodyBottom + 0.55f, -1.1f), new Vector3(0.5f, 0.45f, 1.5f), paint));
                    Part(bodyRoot, "FrontWing", new Vector3(0, bodyBottom + 0.05f, size.z * 0.52f), new Vector3(1.8f, 0.05f, 0.45f), black);
                    Part(bodyRoot, "RearWing", new Vector3(0, bodyBottom + 0.9f, -size.z * 0.48f), new Vector3(1.0f, 0.06f, 0.4f), black);
                    Part(bodyRoot, "RearWingEndL", new Vector3(-0.5f, bodyBottom + 0.7f, -size.z * 0.48f), new Vector3(0.04f, 0.5f, 0.45f), black);
                    Part(bodyRoot, "RearWingEndR", new Vector3(0.5f, bodyBottom + 0.7f, -size.z * 0.48f), new Vector3(0.04f, 0.5f, 0.45f), black);
                    Part(bodyRoot, "Halo", new Vector3(0, bodyBottom + 0.72f, 0.05f), new Vector3(0.5f, 0.05f, 0.6f), black);
                    Part(bodyRoot, "RoofCam", new Vector3(0, bodyBottom + 0.85f, -1.4f), new Vector3(0.14f, 0.07f, 0.14f), black);
                    break;
                }
            }

            if (imported == null && spec.model != CarModel.Storm)
            {
                float frontZ = size.z * 0.5f, lightY = bodyBottom + 0.45f;
                Part(bodyRoot, "HeadL", new Vector3(-size.x * 0.33f, lightY, frontZ), new Vector3(0.35f, 0.12f, 0.04f), headlight);
                Part(bodyRoot, "HeadR", new Vector3(size.x * 0.33f, lightY, frontZ), new Vector3(0.35f, 0.12f, 0.04f), headlight);
                Part(bodyRoot, "TailL", new Vector3(-size.x * 0.35f, lightY, -frontZ), new Vector3(0.35f, 0.1f, 0.04f), taillight);
                Part(bodyRoot, "TailR", new Vector3(size.x * 0.35f, lightY, -frontZ), new Vector3(0.35f, 0.1f, 0.04f), taillight);
            }
            else if (imported == null)
            {
                Part(bodyRoot, "RainLight", new Vector3(0, bodyBottom + 0.2f, -size.z * 0.5f), new Vector3(0.12f, 0.1f, 0.04f), taillight);
            }

            var controller = root.AddComponent<CarController>();
            string[] names = { "FL", "FR", "RL", "RR" };
            var wcRoot = new GameObject("WheelColliders").transform;
            wcRoot.SetParent(root.transform, false);
            var meshRoot = new GameObject("WheelMeshes").transform;
            meshRoot.SetParent(root.transform, false);
            float wheelWidth = 0.30f;
            float rearScale = 1f;

            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float x = anchors != null && anchors.Length == 4 ? anchors[i].x : (i % 2 == 0 ? -1f : 1f) * spec.trackWidth * 0.5f;
                float z = anchors != null && anchors.Length == 4 ? anchors[i].z : (front ? 1f : -1f) * spec.wheelbase * 0.5f;
                var wcGo = new GameObject(names[i]);
                wcGo.transform.SetParent(wcRoot, false);
                wcGo.transform.localPosition = new Vector3(x, spec.suspensionDistance * 0.5f, z);
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = wr;
                wc.suspensionDistance = spec.suspensionDistance;
                controller.wheels[i] = wc;

                var meshPivot = new GameObject(names[i] + "_Mesh").transform;
                meshPivot.SetParent(meshRoot, false);
                meshPivot.localPosition = new Vector3(x, 0f, z);
                float r = wr * (i >= 2 ? rearScale : 1f);
                var tireT = Part(meshPivot, "Tire", Vector3.zero, new Vector3(r * 2f, wheelWidth * 0.5f, r * 2f), tire, new Vector3(0, 0, 90), PrimitiveType.Cylinder).transform;
                Part(meshPivot, "Rim", new Vector3((i % 2 == 0 ? -1f : 1f) * wheelWidth * 0.26f, 0, 0), new Vector3(r * 1.3f, 0.01f, r * 1.3f), rim, new Vector3(0, 0, 90), PrimitiveType.Cylinder);
                tireT.name = "Tire";
                controller.wheelMeshes[i] = meshPivot;
            }

            bool closed = spec.model == CarModel.Falcon || spec.model == CarModel.Titan;
            Vector3 eyePos;
            if (imported != null)
            {
                var lb = imported.localBounds;
                eyePos = new Vector3(spec.driverEyeLocal.x, lb.min.y + lb.size.y * imported.eyeHeightFraction, lb.size.z * imported.eyeForwardFraction);
            }
            else
            {
                float eyeY = spec.driverEyeLocal.y;
                if (spec.model == CarModel.Falcon) eyeY = bodyBottom + 1.05f;
                else if (spec.model == CarModel.Titan) eyeY = bodyBottom + 0.88f;
                eyePos = new Vector3(spec.driverEyeLocal.x, eyeY, spec.driverEyeLocal.z);
            }

            var eye = new GameObject("EyeAnchor").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = eyePos;
            visuals.eyeAnchor = eye;
            visuals.driverHead = BuildDriver(spec, root.transform, eyePos, colorIndex, closed);

            var wheelCam = new GameObject("WheelCamAnchor").transform;
            wheelCam.SetParent(root.transform, false);
            wheelCam.localPosition = eyePos + new Vector3(0f, 0.01f, 0.12f);
            visuals.wheelCamAnchor = wheelCam;

            var hood = new GameObject("HoodAnchor").transform;
            hood.SetParent(root.transform, false);
            if (imported != null)
                hood.localPosition = closed
                    ? new Vector3(0f, imported.localBounds.min.y + imported.localBounds.size.y * 0.72f, size.z * 0.25f)
                    // Fórmulas: T-cam — acima/atrás da cabeça do piloto, halo à frente (referência de tela do jogo oficial).
                    : eyePos + new Vector3(0f, 0.42f, -0.55f);
            else
                hood.localPosition = new Vector3(0f, bodyBottom + size.y * 0.75f, size.z * 0.3f);
            visuals.hoodAnchor = hood;

            if (imported != null)
                Part(root.transform, "Dashboard", eyePos + new Vector3(0.2f, -0.25f, 0.4f), new Vector3(size.x * 0.7f, 0.12f, 0.25f), black);
            else
                Part(root.transform, "Dashboard", spec.driverEyeLocal + new Vector3(0.35f, closed ? -0.28f : -0.1f, 0.4f), new Vector3(size.x * 0.8f, 0.12f, 0.25f), black);

            {
                var swGo = new GameObject("SteeringWheel");
                swGo.transform.SetParent(root.transform, false);
                swGo.transform.localPosition = imported != null
                    ? (Vector3)(eyePos + new Vector3(0f, -0.12f, 0.30f))
                    : (Vector3)(spec.driverEyeLocal + new Vector3(0f, closed ? -0.18f : 0.02f, 0.34f));
                swGo.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                var sw = swGo.AddComponent<SteeringWheelView>();
                sw.car = controller;
                bool openWheel = spec.model == CarModel.F1 || spec.model == CarModel.F2008 || spec.model == CarModel.F1Studio;
                if (openWheel) swGo.transform.localScale = Vector3.one * 0.62f;
                sw.Build(spec.rectangularSteeringWheel, black, spec);
                visuals.steeringWheel = sw;
            }

            controller.spec = spec;
            controller.Setup(spec);
            visuals.SetColor(CarPalette.Get(colorIndex));
            if (spec.model == CarModel.Ferrari || spec.model == CarModel.Revuelto || spec.model == CarModel.Storm
                || spec.model == CarModel.Mustang || spec.model == CarModel.R8 || spec.model == CarModel.GtrNismo
                || spec.model == CarModel.McLarenP1 || spec.model == CarModel.Bugatti)
                visuals.HideProceduralWheel();
            return controller;
        }

        public static Renderer Part(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, Vector3? euler = null, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject go;
            try
            {
                go = GameObject.CreatePrimitive(type);
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            catch
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        static void AddRoadDetail(Transform bodyRoot, CarSpec spec, float bodyBottom, Material paint, Material black, Material exhaustTip)
        {
            float tw = spec.trackWidth * 0.5f;
            float wb = spec.wheelbase * 0.5f;
            float sx = spec.bodySize.x, sz = spec.bodySize.z;
            Part(bodyRoot, "FenderFL", new Vector3(-tw, bodyBottom + 0.5f, wb), new Vector3(0.34f, 0.1f, 0.9f), paint);
            Part(bodyRoot, "FenderFR", new Vector3(tw, bodyBottom + 0.5f, wb), new Vector3(0.34f, 0.1f, 0.9f), paint);
            Part(bodyRoot, "FenderRL", new Vector3(-tw, bodyBottom + 0.5f, -wb), new Vector3(0.34f, 0.1f, 0.9f), paint);
            Part(bodyRoot, "FenderRR", new Vector3(tw, bodyBottom + 0.5f, -wb), new Vector3(0.34f, 0.1f, 0.9f), paint);
            Part(bodyRoot, "SkirtL", new Vector3(-sx * 0.5f + 0.03f, bodyBottom + 0.15f, 0f), new Vector3(0.05f, 0.09f, sz * 0.72f), black);
            Part(bodyRoot, "SkirtR", new Vector3(sx * 0.5f - 0.03f, bodyBottom + 0.15f, 0f), new Vector3(0.05f, 0.09f, sz * 0.72f), black);
            Part(bodyRoot, "Diffuser", new Vector3(0f, bodyBottom + 0.06f, -sz * 0.5f - 0.05f), new Vector3(sx * 0.72f, 0.06f, 0.35f), black);
            Part(bodyRoot, "MirrorStemL", new Vector3(-sx * 0.5f - 0.07f, bodyBottom + 0.8f, 0.5f), new Vector3(0.24f, 0.04f, 0.045f), black);
            Part(bodyRoot, "MirrorStemR", new Vector3(sx * 0.5f + 0.07f, bodyBottom + 0.8f, 0.5f), new Vector3(0.24f, 0.04f, 0.045f), black);
            Part(bodyRoot, "MirrorL", new Vector3(-sx * 0.5f - 0.19f, bodyBottom + 0.8f, 0.5f), new Vector3(0.15f, 0.07f, 0.045f), black);
            Part(bodyRoot, "MirrorR", new Vector3(sx * 0.5f + 0.19f, bodyBottom + 0.8f, 0.5f), new Vector3(0.15f, 0.07f, 0.045f), black);
            Part(bodyRoot, "ExhaustL", new Vector3(-0.32f, bodyBottom + 0.28f, -sz * 0.5f - 0.12f), new Vector3(0.11f, 0.18f, 0.11f), exhaustTip, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
            Part(bodyRoot, "ExhaustR", new Vector3(0.32f, bodyBottom + 0.28f, -sz * 0.5f - 0.12f), new Vector3(0.11f, 0.18f, 0.11f), exhaustTip, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
        }

        /// <summary>Piloto procedural no cockpit REAL (eyePos): capacete com viseira, queixeira e faixa,
        /// colar HANS, ombros, braços até o volante e luvas; macacão na cor do carro. Peças sem sombra
        /// (custo mobile) e nenhuma peça filha de objeto escalado. Retorna a cabeça (some na câmera interna).</summary>
        static GameObject BuildDriver(CarSpec spec, Transform root, Vector3 eye, int colorIndex, bool closed)
        {
            if (spec == null) return null;
            try
            {
                Color team = CarPalette.Get(colorIndex);
                var suit = MaterialLibrary.Get("DriverSuit_" + colorIndex, Color.Lerp(team, Color.black, 0.2f), 0.35f);
                var suitTrim = MaterialLibrary.Get("DriverSuitTrim", new Color(0.92f, 0.92f, 0.94f), 0.35f);
                var helmetMat = MaterialLibrary.Get("HelmetShell", new Color(0.95f, 0.95f, 0.97f), 0.8f);
                var helmetStripe = MaterialLibrary.Get("HelmetStripe_" + colorIndex, team, 0.8f);
                var visor = MaterialLibrary.Get("HelmetVisorDark", new Color(0.02f, 0.02f, 0.03f), 0.95f, 0.6f);
                var glove = MaterialLibrary.Get("DriverGlove", new Color(0.12f, 0.12f, 0.14f), 0.3f);
                var hans = MaterialLibrary.Get("DriverHans", new Color(0.05f, 0.05f, 0.06f), 0.4f);

                var driver = new GameObject("Driver").transform;
                driver.SetParent(root, false);
                float lift = spec.model == CarModel.F1 ? 0.25f : spec.model == CarModel.F2008 ? 0.08f : spec.model == CarModel.F1Studio ? -0.06f : 0f;
                driver.localPosition = eye + new Vector3(0f, lift, 0f);
                var head = new GameObject("DriverHead").transform;
                head.SetParent(driver, false);
                head.localPosition = new Vector3(0f, 0.01f, -0.05f);

                NoShadow(Part(head, "HelmetShell", Vector3.zero, new Vector3(0.25f, 0.265f, 0.29f), helmetMat, null, PrimitiveType.Sphere));
                NoShadow(Part(head, "HelmetStripe", new Vector3(0f, 0.004f, -0.004f), new Vector3(0.075f, 0.27f, 0.292f), helmetStripe, null, PrimitiveType.Sphere));
                NoShadow(Part(head, "HelmetChin", new Vector3(0f, -0.085f, 0.045f), new Vector3(0.21f, 0.12f, 0.2f), helmetMat, null, PrimitiveType.Sphere));
                NoShadow(Part(head, "Visor", new Vector3(0f, 0.005f, 0.05f), new Vector3(0.226f, 0.075f, 0.2f), visor, null, PrimitiveType.Sphere));
                NoShadow(Part(head, "HelmetSpoiler", new Vector3(0f, 0.085f, -0.135f), new Vector3(0.15f, 0.02f, 0.05f), helmetStripe, new Vector3(-20f, 0f, 0f)));
                NoShadow(Part(driver, "Hans", new Vector3(0f, -0.15f, -0.08f), new Vector3(0.24f, 0.02f, 0.2f), hans, null, PrimitiveType.Cylinder));

                Vector3 shoulderC = new Vector3(0f, -0.26f, -0.1f);
                float sh = closed ? 0.17f : 0.1f;
                NoShadow(Limb(driver, "Shoulders", shoulderC + new Vector3(-sh, 0f, 0f), shoulderC + new Vector3(sh, 0f, 0f), closed ? 0.085f : 0.07f, suit));
                NoShadow(Part(driver, "Torso", new Vector3(0f, -0.5f, -0.12f), closed ? new Vector3(0.36f, 0.26f, 0.24f) : new Vector3(0.26f, 0.24f, 0.2f), suit, null, PrimitiveType.Capsule));
                NoShadow(Part(driver, "SuitStripe", shoulderC + new Vector3(0f, 0.02f, 0.06f), new Vector3(0.05f, 0.1f, 0.06f), suitTrim));

                Vector3 wheel = new Vector3(0f, -0.27f, 0.3f);
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    var shoulder = shoulderC + new Vector3(side * (closed ? 0.19f : 0.12f), 0f, 0.01f);
                    var hand = wheel + new Vector3(side * (closed ? 0.17f : 0.1f), 0f, -0.02f);
                    var elbow = Vector3.Lerp(shoulder, hand, 0.5f) + new Vector3(side * (closed ? 0.08f : 0.02f), -0.08f, 0f);
                    NoShadow(Limb(driver, "UpperArm" + i, shoulder, elbow, 0.05f, suit));
                    NoShadow(Limb(driver, "Forearm" + i, elbow, hand, 0.042f, suit));
                    NoShadow(Part(driver, "Glove" + i, hand, new Vector3(0.07f, 0.085f, 0.07f), glove, new Vector3(-20f, 0f, side * 15f), PrimitiveType.Sphere));
                }
                return head.gameObject;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CarFactory] piloto não criado: " + ex.Message);
                return null;
            }
        }

        static Renderer Limb(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
        {
            var d = b - a;
            float len = Mathf.Max(0.01f, d.magnitude);
            var r = Part(parent, name, (a + b) * 0.5f, new Vector3(radius * 2f, len * 0.5f + radius, radius * 2f), mat, null, PrimitiveType.Capsule);
            r.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            return r;
        }

        static Renderer NoShadow(Renderer r)
        {
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static Color paintFor(CarSpec spec)
        {
            switch (spec.model)
            {
                case CarModel.Ferrari: return new Color(0.75f, 0.05f, 0.05f);
                case CarModel.Bugatti: return new Color(0.08f, 0.1f, 0.45f);
                case CarModel.F1:
                case CarModel.F2008:
                case CarModel.F1Studio: return new Color(0.75f, 0.05f, 0.05f);
                case CarModel.Mustang: return new Color(0.02f, 0.15f, 0.5f);
                default: return new Color(0.1f, 0.3f, 0.65f);
            }
        }
    }
}
