using System.Collections;
using NUnit.Framework;
using RaceX.Car;
using RaceX.Core;
using RaceX.Track;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaceX.Tests
{
    public class CarDiagTests
    {
        [UnitySetUp]
        public IEnumerator NewScene()
        {
            RaceSmokeTests.DestroyMenuLeftovers();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Diag_" + System.Guid.NewGuid().ToString("N")));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FlatGround_TitanAccelerates()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.localScale = new Vector3(2000f, 1f, 2000f);
            g.transform.position = new Vector3(0f, -0.5f, 0f);
            var car = CarFactory.Create(CarSpec.CreateDefault(CarModel.Titan), 0, "Diag", new Vector3(0f, 0.6f, -900f), Quaternion.identity);
            yield return new WaitForSeconds(1f);
            car.throttleInput = 1f;
            for (int i = 1; i <= 8; i++)
            {
                yield return new WaitForSeconds(1f);
                Debug.Log($"RACEX_FLAT t={i}s v={car.SpeedKmh:F1}km/h gear={car.Gear} rpm={car.Rpm:F0} grounded={car.GroundedWheels} pos={car.transform.position} motorRL={car.wheels[2].motorTorque:F0} brakeRL={car.wheels[2].brakeTorque:F0} wrpm={car.wheels[2].rpm:F0}");
            }
            Assert.Greater(car.SpeedKmh, 60f);
        }

        [UnityTest]
        public IEnumerator Grid_SpawnStaysOnRoad()
        {
            var track = TrackBuilder.Build(TrackLibrary.Get(TrackId.ForestCircuit));
            var pose = track.GetGridPose(5);
            var car = CarFactory.Create(CarSpec.CreateDefault(CarModel.Titan), 0, "Diag", pose.position, pose.rotation);
            car.inputLocked = true;
            Debug.Log($"RACEX_GRID spawn pos={pose.position} yaw={pose.rotation.eulerAngles.y:F0}");
            foreach (var h in Physics.RaycastAll(pose.position + Vector3.up * 5f, Vector3.down, 20f))
                Debug.Log($"RACEX_GRID ray hit {h.collider.name} y={h.point.y:F3} normal={h.normal}");
            for (int i = 1; i <= 4; i++)
            {
                yield return new WaitForSeconds(1f);
                car.wheels[0].GetGroundHit(out var hit);
                Debug.Log($"RACEX_GRID t={i}s pos={car.transform.position} yaw={car.transform.eulerAngles.y:F0} v={car.SpeedKmh:F1} hit={(hit.collider ? hit.collider.name : "-")} offTrack={car.IsOffTrack}");
            }
            for (float d = -20f; d <= 30f; d += 2f)
            {
                var p = track.Spline.PoseAtDistance(track.Spline.Length + d);
                foreach (var c in Physics.OverlapBox(p.position + Vector3.up * 0.6f, new Vector3(6f, 0.5f, 1f), p.rotation, ~0, QueryTriggerInteraction.Ignore))
                    if (c.attachedRigidbody == null) Debug.Log($"RACEX_BLOCK d={d} {c.name} {c.GetType().Name} {c.bounds.center} size={c.bounds.size}");
            }
            car.inputLocked = false;
            car.throttleInput = 1f;
            for (int i = 1; i <= 4; i++)
            {
                yield return new WaitForSeconds(1f);
                Debug.Log($"RACEX_GRIDGO t={i}s pos={car.transform.position} v={car.SpeedKmh:F1} gear={car.Gear} motor={car.wheels[2].motorTorque:F0} brake={car.wheels[2].brakeTorque:F0}");
            }
            Assert.Greater(car.SpeedKmh, 30f);
        }
    }
}
