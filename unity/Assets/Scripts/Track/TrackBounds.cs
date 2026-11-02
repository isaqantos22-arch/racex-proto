using RaceX.Car;
using UnityEngine;

namespace RaceX.Track
{
    /// <summary>RESPAWN NA LATERAL (pedido do usuário): o carro volta com DOIS PNEUS NA ZEBRA (centro do carro na borda do
    /// asfalto), não no meio da pista — e escolhe o lado e o ponto onde ninguém vai passar nos próximos segundos: para cada
    /// candidato (pontos atrás/à frente × lado esquerdo/direito) mede quem vem atrás, em quanto tempo chega (distância ÷
    /// velocidade) e em que lateral está. Candidato com carro chegando em menos de 5 s na mesma faixa (±3 m) é descartado.</summary>
    public static class RespawnPlanner
    {
        public static Pose Choose(TrackSpline sp, float half, Vector3 near, CarController me, System.Collections.Generic.IList<RaceX.Core.RaceEntry> entries, float[] offsets, out float chosenLat)
        {
            float L = sp.Length;
            float along0 = sp.ClosestIndex(near) * sp.spacing;
            float lat0 = half + 0.05f;
            Pose best = sp.PoseAtDistance(along0, lat0);
            chosenLat = lat0;
            float bestScore = float.NegativeInfinity;
            // Posição de cada carro no traçado calculada UMA vez (antes: busca na pista inteira por carro × candidato —
            // ~750 mil contas por respawn, uma travadinha no celular).
            int n = entries != null ? entries.Count : 0;
            var oIdx = new int[n];
            for (int i = 0; i < n; i++) oIdx[i] = entries[i] != null && entries[i].car != null ? sp.ClosestIndex(entries[i].car.transform.position) : -1;
            foreach (float off in offsets)
            {
                float a = Mathf.Repeat(along0 + off, L);
                for (int side = -1; side <= 1; side += 2)
                {
                    float lat = side * lat0;
                    var pose = sp.PoseAtDistance(a, lat);
                    float score = Safety(sp, a, lat, pose.position, me, entries, oIdx) - Mathf.Abs(off) * 0.01f;
                    if (score > bestScore) { bestScore = score; best = pose; chosenLat = lat; }
                }
            }
            return best;
        }

        static float Safety(TrackSpline sp, float along, float lat, Vector3 at, CarController me, System.Collections.Generic.IList<RaceX.Core.RaceEntry> entries, int[] oIdx)
        {
            float L = sp.Length;
            float score = 50f;
            if (entries == null) return score;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.car == null || e.car == me) continue;
                if (RaceX.Core.PitLaneSystem.IsInPitFlow(e.car)) continue;
                Vector3 p = e.car.transform.position;
                if ((p - at).sqrMagnitude < 9f * 9f) return -100f;               // ocupado
                int oi = oIdx[i];
                if (oi < 0) continue;
                float oAlong = oi * sp.spacing;
                float behind = Mathf.Repeat(along - oAlong, L);                 // quanto ele está ATRÁS do ponto
                if (behind > 300f) continue;
                float oLat = Vector3.Dot(p - sp.points[oi], sp.rights[oi]);
                float v = Mathf.Max(4f, e.car.ForwardSpeed);
                float t = behind / v;
                float latGap = Mathf.Abs(oLat - lat);
                float s = latGap < 3f && t < 5f ? (t - 5f) * 4f - (3f - latGap) * 3f : t * 0.6f + latGap * 1.5f;
                if (s < score) score = s;
            }
            return score;
        }
    }

    [RequireComponent(typeof(CarController))]
    public class TrackBounds : MonoBehaviour
    {
        public const float ResetDelay = 1.8f;

        public CheckpointManager checkpoints;
        float stuckTimer;
        CarController car;

        bool isHuman;
        void Awake() => car = GetComponent<CarController>();
        void Start() { isHuman = car.GetComponent<RaceX.Input.PlayerInputHandler>() != null; }

        // Lista de carros guardada por 1 s: antes varria a cena inteira A CADA QUADRO enquanto o carro estava preso.
        static CarController[] carsCache;
        static float carsCacheAt = -99f;

        bool TouchingCar()
        {
            if (carsCache == null || Time.unscaledTime - carsCacheAt > 1f)
            {
                carsCache = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
                carsCacheAt = Time.unscaledTime;
            }
            foreach (var o in carsCache)
                if (o != null && o != car && (o.transform.position - transform.position).sqrMagnitude < 5.5f * 5.5f) return true;
            return false;
        }

        void Update()
        {
            if (RaceX.Core.ReplaySystem.IsPlaying) return; // replay na tela: mundo congelado, nada reage às posições reproduzidas
            if (checkpoints == null || car.Body.isKinematic) return;
            if (car.wrecked) { stuckTimer = 0f; return; } // perda total: fica onde parou até ser retirado
            // NO PIT (caixa/fila/saindo guiado): NUNCA resetar — estacionar no box não é "estar preso".
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car)) { stuckTimer = 0f; return; }
            // REGRA DO JOGADOR: carro correndo na pista (saiu do pit) → respawn NUNCA mais é o box.
            if (!car.IsOffTrack && car.SpeedKmh > 15f) checkpoints.MarkOnTrack(car);
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f && car.SpeedKmh < 15f;
            bool stalledOffTrack = (car.IsOffTrack || car.GroundedWheels == 0) && car.SpeedKmh < 5f && !car.inputLocked;
            bool pinnedWithInput = !car.inputLocked && !car.pitControlled
                && (car.throttleInput > 0.25f || car.brakeInput > 0.25f) && car.SpeedKmh < 1.2f;
            // Enroscado com OUTRO carro (empurrando/sendo empurrado) não é "preso": o jogador era teleportado até
            // 150 m para trás no meio do toque e parecia ter "entrado" no carro do companheiro (mesma pintura).
            if (pinnedWithInput && TouchingCar()) pinnedWithInput = false;
            stuckTimer = (flipped || stalledOffTrack || pinnedWithInput) ? stuckTimer + Time.deltaTime : 0f;
            float delay = isHuman && pinnedWithInput && !flipped && !stalledOffTrack ? 3.5f : ResetDelay;

            if (stuckTimer >= delay || transform.position.y < -30f) ResetToCheckpoint();
        }

        public void ResetToCheckpoint()
        {
            stuckTimer = 0f;
            var p = checkpoints.Get(car);
            if (p == null) return;
            // REGRA DO JOGADOR: pit é respawn SÓ no início da sessão (e no retorno do menu).
            // Depois que o carro já saiu do pit e correu, sair da rota renasce NA PISTA,
            // perto de onde estava — NUNCA de volta ao box (nem antes do checkpoint 0).
            checkpoints.EnsureTrackRespawn(car);
            // Renasce no ponto da PISTA onde o carro estava (5 m antes), não no checkpoint (até 150 m atrás).
            var basePose = p.respawn;
            var spl = checkpoints.Builder != null && checkpoints.Builder.Waypoints != null ? checkpoints.Builder.Waypoints.Spline : null;
            if (spl != null && spl.Count > 4 && transform.position.y > -30f && !checkpoints.Builder.InPitZone(transform.position, 2f))
            {
                float here = spl.ClosestIndex(transform.position) * spl.spacing;
                var pp = spl.PoseAtDistance(Mathf.Repeat(here - 5f, spl.Length));
                basePose = new Pose(pp.position, pp.rotation);
            }
            Pose pose;
            var rmB = Object.FindFirstObjectByType<RaceX.Core.RaceManager>();
            if (spl != null && spl.Count > 4 && rmB != null && rmB.Track != null && rmB.Track.Definition != null && !checkpoints.Builder.InPitZone(basePose.position, 2f))
                pose = RespawnPlanner.Choose(spl, rmB.Track.Definition.roadWidth * 0.5f, basePose.position, car, rmB.entries, new[] { 0f, -10f, 10f, -20f, 20f, -30f, 30f, -45f }, out _);
            else pose = FreeRespawn(basePose);
            car.ResetTo(pose.position + Vector3.up * 0.3f, pose.rotation);
        }

        /// <summary>Ponto de respawn LIVRE: se já há carro a menos de 10 m do ponto do checkpoint (acidente
        /// duplo/em massa — todos nasciam no MESMO lugar, um em cima do outro, e voavam), recua 10 m por vez pela
        /// pista (fila, um atrás do outro); se atrás estiver cheio, tenta à frente.</summary>
        Pose FreeRespawn(Pose basePose)
        {
            var spline = checkpoints.Builder != null && checkpoints.Builder.Waypoints != null ? checkpoints.Builder.Waypoints.Spline : null;
            var others = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            bool Free(Vector3 at)
            {
                foreach (var o in others)
                    if (o != null && o != car && (o.transform.position - at).sqrMagnitude < 100f) return false;
                return true;
            }
            if (Free(basePose.position) || spline == null || spline.Count < 4) return basePose;
            float along = spline.ClosestIndex(basePose.position) * spline.spacing;
            float[] offs = { -10f, -20f, -30f, -40f, -50f, -60f, 10f, 20f, 30f };
            foreach (float off in offs)
            {
                var pp = spline.PoseAtDistance(Mathf.Repeat(along + off, spline.Length));
                if (Free(pp.position)) return new Pose(pp.position, pp.rotation);
            }
            return basePose;
        }
    }
}
