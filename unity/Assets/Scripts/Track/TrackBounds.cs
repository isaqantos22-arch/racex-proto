using RaceX.Car;
using UnityEngine;

namespace RaceX.Track
{
    [RequireComponent(typeof(CarController))]
    public class TrackBounds : MonoBehaviour
    {
        public const float ResetDelay = 1.8f;

        public CheckpointManager checkpoints;
        float stuckTimer;
        CarController car;

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            if (checkpoints == null || car.Body.isKinematic) return;
            // NO PIT (caixa/fila/saindo guiado): NUNCA resetar — estacionar no box não é "estar preso".
            if (RaceX.Core.PitLaneSystem.IsInPitFlow(car)) { stuckTimer = 0f; return; }
            // REGRA DO JOGADOR: carro correndo na pista (saiu do pit) → respawn NUNCA mais é o box.
            if (!car.IsOffTrack && car.SpeedKmh > 15f) checkpoints.MarkOnTrack(car);
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f && car.SpeedKmh < 15f;
            bool stalledOffTrack = (car.IsOffTrack || car.GroundedWheels == 0) && car.SpeedKmh < 5f && !car.inputLocked;
            bool pinnedWithInput = !car.inputLocked && !car.pitControlled
                && (car.throttleInput > 0.25f || car.brakeInput > 0.25f) && car.SpeedKmh < 1.2f;
            stuckTimer = (flipped || stalledOffTrack || pinnedWithInput) ? stuckTimer + Time.deltaTime : 0f;

            if (stuckTimer >= ResetDelay || transform.position.y < -30f) ResetToCheckpoint();
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
            car.ResetTo(p.respawn.position + Vector3.up * 0.3f, p.respawn.rotation);
        }
    }
}
