using System.Collections.Generic;
using UnityEngine;

namespace RaceX.Track
{
    public class WaypointSystem : MonoBehaviour
    {
        public TrackSpline Spline { get; private set; }
        public readonly List<Vector3> waypoints = new List<Vector3>();
        public float Spacing { get; private set; }
        public float Length => Spline.Length;

        public void Initialize(TrackSpline spline, float spacing)
        {
            Spline = spline;
            int step = Mathf.Max(1, Mathf.RoundToInt(spacing / spline.spacing));
            Spacing = step * spline.spacing;
            waypoints.Clear();
            for (int i = 0; i < spline.Count; i += step) waypoints.Add(spline.points[i]);
        }

        public float DistanceAlong(Vector3 position, ref int hint)
        {
            hint = Spline.ClosestIndex(position, hint);
            int next = Spline.Wrap(hint + 1);
            Vector3 seg = Spline.points[next] - Spline.points[hint];
            float t = seg.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(position - Spline.points[hint], seg) / seg.sqrMagnitude) : 0f;
            return (hint + t) * Spline.spacing;
        }

        public Vector3 DirectionAt(int splineIndex) => Spline.tangents[Spline.Wrap(splineIndex)];

        public Vector3 PointAhead(Vector3 position, float lookAhead, ref int hint, float lateral = 0f)
        {
            float d = DistanceAlong(position, ref hint) + lookAhead;
            return Spline.PoseAtDistance(d, lateral).position;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            foreach (var w in waypoints) Gizmos.DrawSphere(w + Vector3.up, 0.5f);
        }
    }
}
