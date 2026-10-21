using UnityEngine;

namespace RaceX.Track
{
    public enum SurfaceType { Asphalt, Kerb, Grass, SandDirt, Sidewalk }

    public class TrackSurface : MonoBehaviour
    {
        public SurfaceType type = SurfaceType.Asphalt;

        public float FrictionMultiplier => GetFriction(type);
        public bool IsOffTrack => type == SurfaceType.Grass || type == SurfaceType.SandDirt;

        public static float GetFriction(SurfaceType t)
        {
            switch (t)
            {
                case SurfaceType.Kerb: return 0.9f;
                case SurfaceType.Grass: return 0.55f;
                case SurfaceType.SandDirt: return 0.45f;
                case SurfaceType.Sidewalk: return 0.85f;
                default: return 1f;
            }
        }
    }
}
