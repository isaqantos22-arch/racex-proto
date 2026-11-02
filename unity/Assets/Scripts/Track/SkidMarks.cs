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
        static GameObject root;
        static string shaderName = "";

        /// <summary>Segmentos de marca ativos (telemetria do teste automático).</summary>
        public static int Count => segCount;
        public static int Total { get; private set; }
        public static string ShaderName => shaderName;

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

        static Material BuildMaterial()
        {
            // 1º: cópia do material de partícula do projeto (URP Particles/Unlit, transparente, usa a COR
            // DE VÉRTICE e está garantido no build por estar em Resources). Textura branca e sem
            // soft particles (senão a marca colada no asfalto "some" no depth fade).
            var src = Resources.Load<Material>("RaceX_Particle");
            if (src != null && src.shader != null)
            {
                var m = new Material(src) { name = "SkidMarks" };
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
                if (m.HasProperty("_SoftParticlesEnabled")) m.SetFloat("_SoftParticlesEnabled", 0f);
                if (m.HasProperty("_CameraFadingEnabled")) m.SetFloat("_CameraFadingEnabled", 0f);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.DisableKeyword("_SOFTPARTICLES_ON");
                m.DisableKeyword("_FADING_ON");
                return m;
            }
            // 2º: Sprites/Default (cor de vértice, alfa, Cull Off) e demais fallbacks.
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "SkidMarks" };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            return mat;
        }

        /// <summary>Malha única da CENA. O objeto some quando a cena troca (nova sessão da carreira):
        /// antes o "ready" estático continuava true com o renderer destruído e NENHUMA marca aparecia
        /// a partir da 2ª sessão. Agora detecta e reconstrói. Sem material válido, fica inativo.</summary>
        static bool EnsureSystem()
        {
            if (ready && root != null && mesh != null) return true;
            if (failed) return false;
            try
            {
                if (material == null) material = BuildMaterial();
                if (material == null) { failed = true; Debug.LogWarning("[SKID] nenhum shader disponível — marcas desligadas"); return false; }
                material.renderQueue = 2950;
                shaderName = material.shader != null ? material.shader.name : "?";

                if (mesh == null)
                {
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
                        // Face para CIMA (sentido horário visto de cima no Unity).
                        tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                        tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
                    }
                    mesh.vertices = verts;
                    mesh.colors = colors;
                    mesh.SetTriangles(tris, 0);
                }
                // Cena nova: zera as marcas da pista anterior.
                segCount = 0; head = 0; pending = 1;
                for (int i = 0; i < verts.Length; i++) { verts[i] = Vector3.zero; colors[i] = new Color(0f, 0f, 0f, 0f); }
                mesh.vertices = verts;
                mesh.colors = colors;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);

                root = new GameObject("SkidMarks");
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ready = true;
                Debug.Log("[SKID] sistema pronto shader=" + shaderName);
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
            if (!ready || root == null) return;
            Total++;
            ring[head] = new Segment { v0 = backL, v1 = backR, v2 = backR + dir * length, v3 = backL + dir * length, c = color };
            head = (head + 1) % MaxSegments;
            if (segCount < MaxSegments) segCount++;
            pending++;
        }

        /// <summary>Chamado pelos emissores: sobe a malha no máximo a 20 Hz.</summary>
        public static void Tick()
        {
            if (!ready || root == null || pending <= 0 || Time.time < nextCommit) return;
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
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f); // fixo: sem RecalculateBounds a 20 Hz
        }
    }

    /// <summary>Emissor por carro, POR RODA: só a roda que escorrega/trava marca (como na vida real —
    /// derrapagem de traseira marca as traseiras, travada de dianteira marca a dianteira). Freada forte
    /// marca as rodas que escorregam acima de ~0,2; grama/terra deixam rastro de terra. O segmento vai da
    /// posição anterior do pneu até a atual (antes era desenhado À FRENTE da roda).</summary>
    class SkidEmitter : MonoBehaviour
    {
        public CarController car;
        readonly Vector3[] lastPos = new Vector3[4];
        readonly bool[] hasLast = new bool[4];
        readonly float[] slideTime = new float[4];
        float tick;

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            try { Run(); }
            catch (System.Exception) { }
        }

        void Run()
        {
            if (car == null || car.spec == null) return;
            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = 1f / 20f; // 20 Hz

            bool fastEnough = car.SpeedKmh > 15f;
            // Pista encharcada não guarda borracha preta (a água lava) — só o rastro de terra fica.
            bool wetTrack = RaceX.Core.WeatherSystem.TextureWaterMm > RaceX.Core.WeatherSystem.TextureMm * 0.5f;
            var fwd = car.transform.forward; fwd.y = 0f; fwd.Normalize();
            float width = Mathf.Clamp(car.spec.bodySize.x * 0.17f, 0.24f, 0.36f);
            bool any = false;
            for (int i = 0; i < 4; i++)
            {
                var wc = car.wheels != null && i < car.wheels.Length ? car.wheels[i] : null;
                if (wc == null || !wc.GetGroundHit(out var wh)) { hasLast[i] = false; continue; }
                bool dirt = false;
                if (wh.collider != null && wh.collider.TryGetComponent<TrackSurface>(out var surf)) dirt = surf.IsOffTrack;
                // Borracha no asfalto SÓ quando o pneu passa do pico de aderência e PATINA de verdade:
                // roda girando em falso (arrancada) ou travada na freada = deslizamento longitudinal acima
                // de 0,55 (pico da curva de atrito em 0,4); derrapagem lateral acima de 0,32 (pico em 0,25).
                // Curva no limite e aceleração normal ficam abaixo do pico e NÃO marcam.
                float fSlip = Mathf.Abs(wh.forwardSlip), sSlip = Mathf.Abs(wh.sidewaysSlip);
                bool sliding = fSlip > 0.55f || sSlip > 0.32f;
                // O pneu precisa patinar por 0,25 s seguidos para começar a marcar (um tranco não risca a pista).
                slideTime[i] = sliding ? slideTime[i] + 1f / 20f : 0f;
                bool rubber = !dirt && !wetTrack && slideTime[i] >= 0.25f;
                float slip = Mathf.Max(Mathf.InverseLerp(0.55f, 1.2f, fSlip), Mathf.InverseLerp(0.32f, 0.8f, sSlip));
                bool mark = rubber || (dirt && fastEnough);
                if (!mark) { hasLast[i] = false; continue; }
                any = true;
                Vector3 pos = wh.point;
                pos.y += 0.025f;
                if (!hasLast[i]) { lastPos[i] = pos; hasLast[i] = true; continue; }
                Vector3 d = pos - lastPos[i]; d.y = 0f;
                float len = d.magnitude;
                if (len < 0.08f) continue;
                if (len > 3.5f) { lastPos[i] = pos; continue; } // teleporte/respawn: não liga os pontos
                Vector3 dir = d / len;
                var side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
                Color c = dirt ? SkidMarks.DirtColor : SkidMarks.SkidColor;
                // Intensidade pela força do escorregamento (marca leve = cinza translúcido).
                if (!dirt) c.a *= Mathf.Lerp(0.25f, 1f, slip);
                Vector3 back = lastPos[i];
                back.y = Mathf.Lerp(back.y, pos.y, 0.5f);
                SkidMarks.AddMark(back - side, back + side, dir, len, c);
                lastPos[i] = pos;
            }
            if (any) SkidMarks.Tick();
        }
    }
}
