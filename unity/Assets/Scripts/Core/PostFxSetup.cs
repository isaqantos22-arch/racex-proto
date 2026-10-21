using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RaceX.Core
{
    public static class PostFxSetup
    {
        static bool ensured;

        public static void Ensure()
        {
            if (ensured) return;
            ensured = true;

            var go = new GameObject("PostFxVolume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "RaceX_PostFx";

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.25f;

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.22f;

            var adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.saturation.value = 8f;

            volume.profile = profile;
        }
    }
}
