using RaceX.Core;
using UnityEngine;

namespace RaceX.Car
{
    /// <summary>Faixa colorida do composto no flanco do pneu, como a marcação da Pirelli na F1:
    /// macio vermelho, médio amarelo, duro branco (racer.com, esquema desde 2019); intermediário verde e
    /// chuva azul (esquema de 2012 — Sidepodcast; o atual não foi confirmado em fonte). Anel no lado de FORA
    /// de cada roda, filho do pivô da roda (gira e esterça junto). Troca de cor sozinho quando a equipe troca o
    /// composto no pit. Só nos monopostos. Anel também na face INTERNA (voltada para o carro), como o pneu real tem nos
    /// dois flancos: da câmera do cockpit só se vê o lado de dentro das rodas dianteiras.
    /// Desenho medido no vídeo de referência (pit stop do F1 2012, pneu em close aos 26,33 s): anel entre 81% e
    /// 91% do raio do pneu, amarelo RGB 243/230/81 fosco, sem as letras "P ZERO"/"PIRELLI" (pedido do usuário).</summary>
    public class TyreCompoundMarker : MonoBehaviour
    {
        static readonly Color[] Colors =
        {
            new Color(0.95f, 0.08f, 0.08f),     // macio
            new Color(0.953f, 0.902f, 0.318f),  // médio: amarelo medido no vídeo (mediana dos pixels amarelos)
            new Color(0.95f, 0.95f, 0.95f),     // duro
            new Color(0.15f, 0.8f, 0.25f),      // intermediário
            new Color(0.1f, 0.45f, 1f),         // chuva
        };
        const float RingIn = 0.81f, RingOut = 0.91f;
        static Material[] mats;
        static Mesh ring;

        CarController car;
        readonly MeshRenderer[] bands = new MeshRenderer[8];   // 0..3 face externa, 4..7 face interna
        int shown = -1;
        float timer;

        /// <param name="radii">Raio visual de cada pneu.</param>
        /// <param name="faceOffsets">Distância do eixo até a face externa de cada pneu (medida na malha do pneu).</param>
        /// <param name="bandFace">Fração da face externa em que o flanco fica a 81% (x) e a 91% (y) do raio — medida
        /// no Blender por modelo; o anel acompanha essa inclinação (cônico) para não flutuar nem afundar.</param>
        public static void Attach(CarController car, float[] radii, float[] faceOffsets, Vector2 bandFace)
        {
            if (car == null || car.wheelMeshes == null) return;
            try
            {
                var m = car.gameObject.AddComponent<TyreCompoundMarker>();
                m.car = car;
                for (int i = 0; i < 4; i++)
                {
                    var pivot = car.wheelMeshes[i];
                    if (pivot == null) continue;
                    float outSide = i % 2 == 0 ? -1f : 1f; // FL/RL à esquerda
                    float hw = faceOffsets != null && i < faceOffsets.Length && faceOffsets[i] > 0.02f ? faceOffsets[i] : 0.15f;
                    float depth = Mathf.Max(0.0005f, (bandFace.x - bandFace.y) * hw);
                    for (int f = 0; f < 2; f++)
                    {
                        // f = 0: face externa; f = 1: face interna (espelhada no centro do pneu, que é simétrico)
                        float side = f == 0 ? outSide : -outSide;
                        var go = new GameObject(f == 0 ? "CompoundBand" : "CompoundBandInner");
                        go.transform.SetParent(pivot, false);
                        go.transform.localPosition = new Vector3(side * (bandFace.x * hw + 0.003f), 0f, 0f);
                        // x = profundidade do cone (borda externa do anel entra em direção ao centro do pneu), y/z = raio
                        go.transform.localScale = new Vector3(side * depth, radii[i], radii[i]);
                        go.AddComponent<MeshFilter>().sharedMesh = Ring();
                        var r = go.AddComponent<MeshRenderer>();
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        m.bands[i + f * 4] = r;
                    }
                }
                m.Refresh();
            }
            catch (System.Exception ex) { Debug.LogWarning("[TyreMarker] ignorado: " + ex.Message); }
        }

        /// <summary>Anel cônico: borda interna (81% do raio) em x = 0 e borda externa (91%) em x = -1 (a escala X
        /// do objeto vira a profundidade real). Dupla face, 64 segmentos.</summary>
        static Mesh Ring()
        {
            if (ring != null) return ring;
            const int seg = 64;
            var v = new Vector3[(seg + 1) * 4];
            var uv = new Vector2[(seg + 1) * 4];
            var t = new int[seg * 12];
            int o = (seg + 1) * 2;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                var inner = dir * RingIn; var outer = dir * RingOut + new Vector3(-1f, 0f, 0f);
                v[i * 2] = inner; v[i * 2 + 1] = outer;
                v[o + i * 2] = inner; v[o + i * 2 + 1] = outer;
                float u = i * 12f / seg;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
                uv[o + i * 2] = new Vector2(u, 0f); uv[o + i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = i * 2 + 1, b0 = (i + 1) * 2, b1 = b0 + 1;
                int k = i * 12;
                t[k] = a0; t[k + 1] = b0; t[k + 2] = a1; t[k + 3] = a1; t[k + 4] = b0; t[k + 5] = b1;
                t[k + 6] = o + a0; t[k + 7] = o + a1; t[k + 8] = o + b0; t[k + 9] = o + a1; t[k + 10] = o + b1; t[k + 11] = o + b0;
            }
            ring = new Mesh { name = "CompoundBand", vertices = v, uv = uv, triangles = t };
            ring.RecalculateNormals();
            ring.RecalculateBounds();
            return ring;
        }

        static Material MatFor(int compound)
        {
            if (mats == null) mats = new Material[Colors.Length];
            int c = TyreModel.Clamp(compound);
            // Fosco (tinta sobre borracha) com um pouco de emissão para continuar legível na sombra. Cor lisa: a
            // sujeira é estado passageiro (TyreDirt), não característica do pneu.
            if (mats[c] == null) mats[c] = MaterialLibrary.Get("CompoundBand_" + c, Colors[c], 0.22f, 0f, Colors[c] * 0.18f);
            return mats[c];
        }

        void Refresh()
        {
            int c = PitLaneSystem.PitEnabled ? PitLaneSystem.CompoundOf(car) : TyreModel.Medium;
            // Na parada, a partir de quando a equipe MONTA a roda nova, a faixa já é a do composto novo (antes ficava a
            // cor do pneu velho até a liberação — "dava para ver que era amarelo, mesmo tendo pedido vermelho").
            if (PitLaneSystem.PitEnabled && PitLaneSystem.StopInfo(car, out float stT, out float stD, out _) && stT >= 0.6f * Mathf.Min(stD, 3f))
                c = PitLaneSystem.FittingCompound(car);
            if (c == shown) return;
            shown = c;
            var mat = MatFor(c);
            foreach (var b in bands) if (b != null) b.sharedMaterial = mat;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = PitLaneSystem.PitEnabled && PitLaneSystem.StopInfo(car, out _, out _, out _) ? 0.05f : 0.5f;
            Refresh();
        }
    }
}
