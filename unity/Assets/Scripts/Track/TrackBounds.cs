using RaceX.Car;
using UnityEngine;

namespace RaceX.Track
{
    [RequireComponent(typeof(CarController))]
    public class TrackBounds : MonoBehaviour
    {
        public const float ResetDelay = 3f;

        public CheckpointManager checkpoints;
        float stuckTimer;
        CarController car;

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            if (checkpoints == null || car.Body.isKinematic) return;
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f && car.SpeedKmh < 15f;
            bool stalledOffTrack = (car.IsOffTrack || car.GroundedWheels == 0) && car.SpeedKmh < 5f && !car.inputLocked;
            stuckTimer = flipped || stalledOffTrack ? stuckTimer + Time.deltaTime : 0f;

            if (stuckTimer >= ResetDelay || transform.position.y < -30f) ResetToCheckpoint();
        }

        public void ResetToCheckpoint()
        {
            stuckTimer = 0f;
            var p = checkpoints.Get(car);
            if (p == null) return;
            car.ResetTo(p.respawn.position + Vector3.up * 0.3f, p.respawn.rotation);
        }
    }
}
