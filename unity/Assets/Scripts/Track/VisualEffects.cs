using RaceX.Car;
using RaceX.Core;
using UnityEngine;

namespace RaceX.Track
{
    public static class VisualEffects
    {
        public static void Attach(CarController car)
        {
            if (car == null || car.spec == null) return;
            var fx = car.gameObject.AddComponent<VisualFx>();
            fx.car = car;
            fx.smoke = Make(car, "Fx_TireSmoke", new Vector3(0f, 0.28f, -car.spec.wheelbase * 0.5f));
            fx.dust = Make(car, "Fx_Dust", new Vector3(0f, 0.2f, -car.spec.wheelbase * 0.6f));
        }

        static Material particleMaterial;

        static Material ParticleMaterial
        {
            get
            {
                if (particleMaterial != null) return particleMaterial;
                var source = Resources.Load<Material>("RaceX_Particle");
                if (source == null) return particleMaterial = MaterialLibrary.Get("FxParticle", Color.white, 0f);
                particleMaterial = new Material(source) { name = "RaceX_ParticleSoft" };
                var tex = SoftDot(64);
                if (particleMaterial.HasProperty("_BaseMap")) particleMaterial.SetTexture("_BaseMap", tex);
                if (particleMaterial.HasProperty("_MainTex")) particleMaterial.SetTexture("_MainTex", tex);
                return particleMaterial;
            }
        }

        static Texture2D SoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
            tex.Apply();
            return tex;
        }

        static ParticleSystem Make(CarController car, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(car.transform, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = ParticleMaterial;
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.6f;
            main.startSpeed = 0.6f;
            main.startSize = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.8f));
            var emission = ps.emission;
            emission.enabled = false;
            return ps;
        }
    }

    class VisualFx : MonoBehaviour
    {
        public CarController car;
        public ParticleSystem smoke;
        public ParticleSystem dust;

        static readonly Color SmokeColor = new Color(0.78f, 0.78f, 0.78f, 0.55f);
        static readonly Color DustColor = new Color(0.45f, 0.36f, 0.25f, 0.85f);

        static void EmitAt(ParticleSystem ps, Vector3 vel, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = ps.transform.position,
                velocity = vel,
                startLifetime = life,
                startColor = color,
                startSize = 0.5f,
            };
            ps.Emit(p, 1);
        }

        void LateUpdate()
        {
            if (car == null || car.spec == null) return;
            if (smoke != null && car.MaxWheelSlip > 0.4f && car.GroundedWheels > 0)
            {
                var vel = -car.transform.forward * 0.4f + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.1f, 0.4f), Random.Range(-0.3f, 0.3f));
                EmitAt(smoke, vel, 0.6f, SmokeColor);
            }
            if (dust != null && car.IsOffTrack && car.SpeedKmh > 20f)
            {
                var vel = -car.transform.forward * 0.5f + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0f, 0.3f), Random.Range(-0.2f, 0.2f));
                EmitAt(dust, vel, 1f, DustColor);
            }
        }
    }
}
