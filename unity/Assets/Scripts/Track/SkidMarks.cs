using RaceX.Car;
using UnityEngine;

namespace RaceX.Track
{
    /// <summary>Marcas de pneu persistentes da CENA: buffer em anel de 4000 segmentos compartilhado
    /// por TODOS os carros (a mais antiga é sobrescrita quando enche), uma malha dinâmica única e
    /// atualização no máximo a 20 Hz — sem GameObject por marca (mobile). Freio forte/travada/
    /// derrapagem deixam marca PRETA (alfa ~0.75); grama/terra deixam marca de terra marrom-escura
    /// (qualquer velocidade &gt; 15 km/h e quando o carro roda). Sempre decorativa: sem shader válido
    /// o sistema fica inativo e nunca quebra o carregamento da corrida.</summary>
    public static class SkidMarks
    {
        const int MaxSegments = 4000;
        const float UpdateInterval = 1f / 20f; // atualização máxima de 20 Hz

        public static readonly Color SkidColor = new Color(0.04f, 0.04f, 0.04f, 0.75f);
        public static readonly Color DirtColor = new Color(0.30f, 0.24f, 0.11f, 0.8f);

        struct Segment { public Vector3 v0, v1, v2, v3; public Color c; }

        static Segment[] ring;
        static int segCount, head;
        static Mesh mesh;
        static Material material;
        static Vector3[] verts;
        static Color[] colors;
        static int[] tris;
        static int pending;
        static float nextCommit;
        static bool ready, failed;

        /// <summary>Liga um emissor ao carro (jogador E IA). Nunca lança exceção: é decoração.</summary>
        public static void Attach(CarController car)
        {
            if (car == null || car.spec == null) return;
            try
            {
                if (!EnsureSystem()) return;
                var emitter = car.gameObject.AddComponent<SkidEmitter>();
                emitter.car = car;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SkidMarks] emissor ignorado: " + ex.Message);
            }
        }

        /// <summary>Malha única da cena com FALLBACK de shader. Sem shader válido, fica inativo.</summary>
        static bool EnsureSystem()
        {
            if (ready) return true;
            if (failed) return false;
            try
            {
                // Sprites/Default PRIMEIRO: usa a COR DE VÉRTICE (preto/terra por segmento), tem alfa e
                // Cull Off. O URP/Unlit ignorava a cor de vértice (marca branca/invisível) — bug v78.
                var shader = Shader.Find("Sprites/Default")
                          ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Unlit/Color");
                if (shader == null) { failed = true; return false; }
                material = new Material(shader) { name = "SkidMarks" };
                // Transparência (se o shader tiver): a marca precisa de alfa para não virar bloco.
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                material.renderQueue = 2950;

                mesh = new Mesh { name = "SkidMarks" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.MarkDynamic();
                ring = new Segment[MaxSegments];
                int vCount = MaxSegments * 4;
                verts = new Vector3[vCount];
                colors = new Color[vCount];
                tris = new int[MaxSegments * 6];
                for (int i = 0; i < MaxSegments; i++)
                {
                    int b = i * 4, t = i * 6;
                    // Face para CIMA (sentido horário visto de cima no Unity) — antes a face apontava para
                    // baixo e era descartada pelo culling dos shaders URP.
                    tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                    tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
                }
                mesh.SetTriangles(tris, 0);
                mesh.vertices = verts;
                mesh.colors = colors;
                mesh.RecalculateBounds();

                var go = new GameObject("SkidMarks");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ready = true;
            }
            catch (System.Exception ex)
            {
                failed = true;
                Debug.LogWarning("[SkidMarks] sistema ignorado: " + ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>Segmento novo: borda traseira (backL/backR), direção e comprimento. Sobrescreve o
        /// segmento mais antigo do anel (buffer fixo, nunca aloca).</summary>
        public static void AddMark(Vector3 backL, Vector3 backR, Vector3 dir, float length, Color color)
        {
            if (!ready) return;
            ring[head] = new Segment { v0 = backL, v1 = backR, v2 = backR + dir * length, v3 = backL + dir * length, c = color };
            head = (head + 1) % MaxSegments;
            if (segCount < MaxSegments) segCount++;
            pending++;
        }

        /// <summary>Chamado pelos emissores: sobe a malha no máximo a 20 Hz.</summary>
        public static void Tick()
        {
            if (!ready || pending <= 0 || Time.time < nextCommit) return;
            nextCommit = Time.time + UpdateInterval;
            pending = 0;
            for (int i = 0; i < segCount; i++)
            {
                int b = i * 4;
                var s = ring[i];
                verts[b] = s.v0; verts[b + 1] = s.v1; verts[b + 2] = s.v2; verts[b + 3] = s.v3;
                colors[b] = s.c; colors[b + 1] = s.c; colors[b + 2] = s.c; colors[b + 3] = s.c;
            }
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int i = segCount; i < MaxSegments; i++)
            {
                int b = i * 4;
                verts[b] = Vector3.zero; verts[b + 1] = Vector3.zero; verts[b + 2] = Vector3.zero; verts[b + 3] = Vector3.zero;
                colors[b] = clear; colors[b + 1] = clear; colors[b + 2] = clear; colors[b + 3] = clear;
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }
    }

    /// <summary>Emissor por carro: decide quando deixar marca (freio/travada/derrapagem = preta;
    /// grama/terra = cor de terra) e desenha o quad acompanhando o chão (offset pequeno anti-z-fighting).</summary>
    class SkidEmitter : MonoBehaviour
    {
        public CarController car;
        readonly Vector3[] lastPos = new Vector3[4];
        readonly bool[] hasLast = new bool[4];
        const float QuadWidth = 0.22f;
        float tick;

        void Update()
        {
            try { Run(); }
            catch (System.Exception) { }
        }

        void Run()
        {
            if (car == null || car.spec == null) return;
            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = 1f / 20f; // 20 Hz

            bool sliding = car.MaxWheelSlip > 0.45f;                        // travada/derrapagem
            bool braking = car.brakeInput > 0.55f && car.ForwardSpeed > 3f; // freio forte em movimento
            bool onDirt = car.IsOffTrack && car.SpeedKmh > 15f;             // grama/terra acima de 15 km/h
            if (!sliding && !braking && !onDirt)
            {
                for (int w = 0; w < 4; w++) hasLast[w] = false;
                return;
            }
            Color color = onDirt ? SkidMarks.DirtColor : SkidMarks.SkidColor;
            var fwd = car.transform.forward; fwd.y = 0f; fwd.Normalize();
            float track = car.spec.trackWidth * 0.5f;
            float wb = car.spec.wheelbase * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 pos;
                var wc = car.wheels != null && i < car.wheels.Length ? car.wheels[i] : null;
                // Ponto de CONTATO real do pneu com o chão (WheelHit). O raycast antigo batia no próprio
                // BoxCollider do carro e a marca nascia DENTRO da carroceria (invisível).
                if (wc != null && wc.GetGroundHit(out var wh))
                    pos = wh.point;
                else if (wc != null)
                {
                    // Roda no ar: sem marca nesta roda.
                    hasLast[i] = false;
                    continue;
                }
                else
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * track;
                    float z = i < 2 ? wb : -wb;
                    pos = car.transform.position + car.transform.rotation * new Vector3(x, 0f, z);
                    pos.y -= car.spec.wheelRadius;
                }
                pos.y += 0.025f;
                Vector3 d = pos - lastPos[i]; d.y = 0f;
                Vector3 dir = d.sqrMagnitude > 0.001f ? d.normalized : fwd;
                var side = new Vector3(-dir.z, 0f, dir.x) * (QuadWidth * 0.5f);
                if (hasLast[i])
                {
                    float len = d.magnitude;
                    if (len < 0.08f) continue;
                    if (len > 1.6f) len = 1.6f;
                    SkidMarks.AddMark(pos - side, pos + side, dir, len, color);
                }
                else
                {
                    SkidMarks.AddMark(pos - side, pos + side, dir, 0.3f, color);
                }
                lastPos[i] = pos;
                hasLast[i] = true;
            }
            SkidMarks.Tick();
        }
    }
}
