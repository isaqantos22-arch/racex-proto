using UnityEngine;

namespace RaceX.Car
{
    /// <summary>PNEU FURADO/ESTOURADO VISÍVEL. Referência: fotos de pneus de F1 que falharam (Hamilton, Silverstone 2020;
    /// Mercedes W11; Verstappen, Baku 2021; Renault de Palmer):
    /// 1) MURCHO — o pneu achata embaixo e o flanco estufa para os lados (o carro já arria pela roda física:
    ///    CarDamage.SetDeflation); aqui um "estufamento" de borracha no ponto de contato, que cresce com a pressão caindo;
    /// 2) DESFAZENDO — tiras da banda de rodagem soltas, presas à roda, girando com ela e batendo para fora;
    /// 3) DESTRUÍDO — o aro raspa no asfalto: faíscas saindo do ponto de contato.
    /// Larguras do pneu de 18" da Pirelli: dianteiro 305 mm, traseiro 405 mm (GT: 280/330 mm, estimativa).
    /// Criado só quando um pneu perde pressão — nenhum custo nos carros com pneus bons. Visual nunca quebra a corrida
    /// (try/catch e cadeia de shaders, como no resto do jogo).</summary>
    public class TyreDamageVisual : MonoBehaviour
    {
        CarController car;
        readonly float[] pressure = { 1f, 1f, 1f, 1f };
        readonly float[] shred = new float[4];
        readonly bool[] destroyed = new bool[4];
        readonly Transform[] bulge = new Transform[4];
        readonly Transform[][] strips = new Transform[4][];
        readonly ParticleSystem[] sparks = new ParticleSystem[4];
        bool broken;
        static Material rubberMat, sparkMat;
        static Mesh bulgeMesh;
        const int StripCount = 3;

        /// <summary>Estado de uma roda (chamado pelo modelo de pneus). Pressão 1 = normal; shred 0..1 = pneu se desfazendo;
        /// destroyed = rodando sobre o aro.</summary>
        public static void Set(CarController car, int wheel, float pressure01, float shred01, bool destroyedNow)
        {
            if (car == null || wheel < 0 || wheel > 3) return;
            var v = car.GetComponent<TyreDamageVisual>();
            if (v == null)
            {
                if (pressure01 > 0.95f && !destroyedNow) return;   // pneu bom: nada a criar
                v = car.gameObject.AddComponent<TyreDamageVisual>();
                v.car = car;
            }
            v.pressure[wheel] = pressure01;
            v.shred[wheel] = shred01;
            v.destroyed[wheel] = destroyedNow;
        }

        void LateUpdate()
        {
            if (broken || car == null || car.wheels == null) return;
            try
            {
                for (int i = 0; i < 4; i++) UpdateWheel(i);
            }
            catch (System.Exception ex)
            {
                broken = true;   // visual nunca pode travar a corrida
                Debug.LogWarning("[TyreVisual] desligado: " + ex.Message);
            }
        }

        void UpdateWheel(int i)
        {
            var wc = car.wheels[i];
            var pivot = car.wheelMeshes != null && i < car.wheelMeshes.Length ? car.wheelMeshes[i] : null;
            bool gone = car.damage != null && car.damage.Detached((int)DamageZone.WheelFL + i);
            float flat = gone ? 0f : 1f - pressure[i];
            bool showBulge = flat > 0.12f && wc != null && pivot != null;
            bool showStrips = !gone && shred[i] > 0.05f && pivot != null;
            bool showSparks = !gone && destroyed[i] && wc != null;

            if (!showBulge && bulge[i] == null && !showStrips && strips[i] == null && !showSparks && sparks[i] == null) return;

            bool front = i < 2;
            bool formula = car.spec != null && CarSpec.IsFormula(car.spec.model);
            float width = formula ? (front ? 0.305f : 0.405f) : (front ? 0.28f : 0.33f);
            float baseR = car.damage != null ? car.damage.BaseRadius(i) : (wc != null ? wc.radius : 0.34f);
            float rNow = wc != null ? wc.radius : baseR;
            Vector3 up = car.transform.up;
            Vector3 center = pivot != null ? pivot.position : car.transform.position;
            Vector3 ground = center - up * rNow;
            Quaternion steer = car.transform.rotation * Quaternion.Euler(0f, wc != null ? wc.steerAngle : 0f, 0f);

            // 1) estufamento no contato com o chão (não gira; segue o esterço)
            if (showBulge)
            {
                if (bulge[i] == null) bulge[i] = MakeBulge();
                if (bulge[i] != null)
                {
                    if (!bulge[i].gameObject.activeSelf) bulge[i].gameObject.SetActive(true);
                    float h = baseR * (0.10f + 0.24f * flat);
                    bulge[i].SetPositionAndRotation(ground + up * (h * 0.5f), steer);
                    bulge[i].localScale = new Vector3(width * (1.0f + 0.28f * flat), h, baseR * (0.55f + 0.5f * flat));
                }
            }
            else if (bulge[i] != null && bulge[i].gameObject.activeSelf) bulge[i].gameObject.SetActive(false);

            // 2) tiras da banda de rodagem (giram com a roda e batem para fora)
            if (showStrips)
            {
                if (strips[i] == null) strips[i] = MakeStrips();
                Vector3 axle = pivot.rotation * Vector3.right;
                float speed = Mathf.Abs(car.ForwardSpeed);
                float lift = Mathf.Lerp(8f, 55f, Mathf.Clamp01(speed / 40f)) * shred[i];
                for (int k = 0; k < StripCount; k++)
                {
                    var s = strips[i][k];
                    if (s == null) continue;
                    bool on = shred[i] > 0.05f + k * 0.25f;   // mais tiras quanto mais desfeito
                    if (s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
                    if (!on) continue;
                    float a = k * (Mathf.PI * 2f / StripCount);
                    Vector3 radial = pivot.rotation * new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    radial.Normalize();
                    Vector3 tangent = Vector3.Cross(axle, radial).normalized;
                    float flap = lift + 18f * shred[i] * Mathf.Sin(Time.time * 42f + k * 2.1f);
                    Quaternion hinge = Quaternion.LookRotation(tangent, radial) * Quaternion.Euler(-flap, 0f, 0f);
                    float len = 0.25f + 0.25f * shred[i];
                    Vector3 hingePos = center + radial * (rNow * 0.98f);
                    s.SetPositionAndRotation(hingePos + hinge * (Vector3.forward * len * 0.5f), hinge);
                    s.localScale = new Vector3(width * 0.7f, 0.02f, len);
                }
            }
            else if (strips[i] != null)
                foreach (var s in strips[i]) if (s != null && s.gameObject.activeSelf) s.gameObject.SetActive(false);

            // 3) faíscas do aro no asfalto
            if (showSparks)
            {
                if (sparks[i] == null) sparks[i] = MakeSparks();
                if (sparks[i] != null)
                {
                    var tr = sparks[i].transform;
                    Vector3 back = -car.transform.forward;
                    tr.SetPositionAndRotation(ground + up * 0.03f, Quaternion.LookRotation((back + up * 0.45f).normalized, up));
                    var em = sparks[i].emission;
                    float speed = Mathf.Abs(car.ForwardSpeed);
                    em.rateOverTime = speed > 3f ? Mathf.Lerp(60f, 260f, Mathf.Clamp01(speed / 50f)) : 0f;
                }
            }
            else if (sparks[i] != null)
            {
                var em = sparks[i].emission;
                em.rateOverTime = 0f;
            }
        }

        // ---------------- construção ----------------
        static Material RubberMat()
        {
            if (rubberMat != null) return rubberMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return null;
            rubberMat = new Material(sh) { name = "TyreDamageRubber" };
            var c = new Color(0.045f, 0.045f, 0.05f);
            if (rubberMat.HasProperty("_BaseColor")) rubberMat.SetColor("_BaseColor", c);
            if (rubberMat.HasProperty("_Color")) rubberMat.SetColor("_Color", c);
            if (rubberMat.HasProperty("_Smoothness")) rubberMat.SetFloat("_Smoothness", 0.28f);
            return rubberMat;
        }

        Transform MakeBulge()
        {
            var mat = RubberMat();
            if (mat == null) return null;
            if (bulgeMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulgeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            var go = new GameObject("TyreBulge");
            go.transform.SetParent(transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = bulgeMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Material carcassMat;

        /// <summary>Carcaça exposta da banda de rodagem solta (nas fotos aparece cinza, mais clara que o pneu).</summary>
        static Material CarcassMat()
        {
            if (carcassMat != null) return carcassMat;
            var baseMat = RubberMat();
            if (baseMat == null) return null;
            carcassMat = new Material(baseMat) { name = "TyreCarcass" };
            var c = new Color(0.22f, 0.21f, 0.2f);
            if (carcassMat.HasProperty("_BaseColor")) carcassMat.SetColor("_BaseColor", c);
            if (carcassMat.HasProperty("_Color")) carcassMat.SetColor("_Color", c);
            return carcassMat;
        }

        Transform[] MakeStrips()
        {
            var arr = new Transform[StripCount];
            var mat = CarcassMat();
            if (mat == null) return arr;
            for (int k = 0; k < StripCount; k++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "TyreStrip";
                go.transform.SetParent(transform, true);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                arr[k] = go.transform;
            }
            return arr;
        }

        ParticleSystem MakeSparks()
        {
            if (sparkMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh == null) return null;
                sparkMat = new Material(sh) { name = "RimSparks" };
                var c = new Color(1f, 0.72f, 0.25f, 1f);
                if (sparkMat.HasProperty("_BaseColor")) sparkMat.SetColor("_BaseColor", c);
                if (sparkMat.HasProperty("_Color")) sparkMat.SetColor("_Color", c);
            }
            var go = new GameObject("RimSparks");
            go.transform.SetParent(transform, true);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            // tamanho/traço para aparecer da câmera de trás (~8 m): com 2,5–5 cm as faíscas sumiam no teste de 30/10
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 320;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sparkMat;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.08f;
            r.lengthScale = 2.5f;
            ps.Play();
            return ps;
        }
    }
}
