using System;
using UnityEngine;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Optical coefficients are per reference planet radius, not per Unity unit.
    /// Defaults approximate clear terrestrial air for a 6,371 km reference radius.
    /// </summary>
    [Serializable]
    public sealed class AtmosphereSettings
    {
        public Vector3 Rayleigh = new Vector3(37f, 86f, 211f);
        public Vector3 Mie = new Vector3(25.5f, 25.5f, 25.5f);
        [Range(0f, 0.95f)] public float MieG = 0.76f;
        [Min(0.1f)] public float DensityFalloff = 12.5f;
        [Min(0.1f)] public float MieDensityFalloff = 80f;
        [Tooltip("Relative solar irradiance, in the same display radiance convention as terrain lighting. This is not an SI solar luminance.")]
        [Min(0f)] public float SunIntensity = 2f;
        [Range(4, 48)] public int ViewSamples = 16;
        [Range(2, 16)] public int LightSamples = 6;
    }
}
