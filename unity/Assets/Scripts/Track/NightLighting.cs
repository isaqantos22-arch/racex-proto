using UnityEngine;

namespace RaceX.Track
{
    /// <summary>
    /// ILUMINAÇÃO NOTURNA (v99, pedido do usuário: "pistas à noite não têm iluminação; deve ter postes"). Pista com sol fraco
    /// (sunIntensity ≤ 0,6: Metrópole Noturna, Jardim da Baía, Corniche da Noite, Faixa de Neon) ganha postes a cada
    /// PoleStep m, alternando os lados, com cabeça EMISSIVA (brilha sem custo) e, a cada LightEvery postes, uma luz real
    /// (spot) — poucas luzes reais porque no celular cada luz adicional pesa (URP: 4 por objeto). Tudo sem colisor e em try/catch:
    /// o visual nunca pode quebrar o carregamento (regra do projeto).
    /// </summary>
    public static class NightLighting
    {
        public const float NightSunMax = 0.6f;
        const float PoleStep = 45f;
        const int LightEvery = 3;
        const float PoleHeight = 10f;

        public static bool IsNight(TrackDefinition def) => def != null && def.sunIntensity <= NightSunMax;

        public static void Build(TrackBuilder track, float wallOffset)
        {
            if (track == null || track.Spline == null || !IsNight(track.Definition)) return;
            try
            {
                var spline = track.Spline;
                var pole = Core.MaterialLibrary.Get("NightPole", new Color(0.28f, 0.29f, 0.32f), 0.5f, 0.6f);
                var warm = new Color(1f, 0.93f, 0.78f);
                var head = Core.MaterialLibrary.Get("NightLampHead", warm, 0.3f, 0f, warm * 3.5f);
                var root = new GameObject("NightLighting").transform;
                root.SetParent(track.transform, false);
                int n = 0, lights = 0;
                for (float d = 20f; d < spline.Length - 10f; d += PoleStep, n++)
                {
                    var p = spline.PoseAtDistance(d);
                    float side = n % 2 == 0 ? 1f : -1f;
                    Vector3 right = p.rotation * Vector3.right;
                    Vector3 basePos = p.position + right * side * (wallOffset + 1.8f);
                    // Poste + braço virado para a pista + cabeça emissiva.
                    track.Static(PrimitiveType.Cylinder, "Pole" + n, basePos + Vector3.up * PoleHeight * 0.5f, Quaternion.identity,
                        new Vector3(0.14f, PoleHeight * 0.5f, 0.14f), pole, false).transform.SetParent(root, true);
                    Vector3 armEnd = basePos + Vector3.up * (PoleHeight - 0.2f) - right * side * 1.6f;
                    track.Static(PrimitiveType.Cube, "Arm" + n, (basePos + Vector3.up * (PoleHeight - 0.2f) + armEnd) * 0.5f, p.rotation,
                        new Vector3(1.7f, 0.12f, 0.12f), pole, false).transform.SetParent(root, true);
                    track.Static(PrimitiveType.Cube, "Lamp" + n, armEnd - Vector3.up * 0.15f, p.rotation,
                        new Vector3(0.7f, 0.18f, 1.4f), head, false).transform.SetParent(root, true);
                    if (n % LightEvery == 0)
                    {
                        var lg = new GameObject("PoleLight" + n);
                        lg.transform.SetParent(root, false);
                        lg.transform.position = armEnd - Vector3.up * 0.3f;
                        lg.transform.rotation = Quaternion.LookRotation(Vector3.down + right * -side * 0.35f, Vector3.forward);
                        var l = lg.AddComponent<Light>();
                        l.type = LightType.Spot;
                        l.color = warm;
                        l.intensity = 2.6f;
                        l.range = 48f;
                        l.spotAngle = 110f;
                        l.shadows = LightShadows.None;
                        lights++;
                    }
                }
                Debug.Log("[NightLighting] postes=" + n + " luzes reais=" + lights);
            }
            catch (System.Exception ex) { Debug.LogWarning("[NightLighting] ignorado: " + ex.Message); }
        }
    }
}
