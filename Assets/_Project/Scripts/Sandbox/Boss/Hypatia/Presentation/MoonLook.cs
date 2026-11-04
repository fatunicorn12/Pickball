using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Makes an orbiter look like a moon of hers: she is its sun, so the side facing
    /// her is lit and it passes through real phases as it circles, always keeping
    /// the same face toward her. Once batted it leaves that order: it spins and
    /// flares, which also marks it as no longer a threat.
    ///
    /// Presentation only, and on the prefab rather than a scene view so it lives and
    /// dies with its orbiter. Reads <see cref="ConicOrbiter"/>; never writes to it.
    /// She sits at the orbit's focus, which is where the light comes from. No
    /// per-frame allocation.
    /// </summary>
    public class MoonLook : MonoBehaviour
    {
        static readonly int SunPositionId = Shader.PropertyToID("_SunPosition");
        static readonly int BoostId = Shader.PropertyToID("_Boost");

        [Tooltip("The orbiter this moon belongs to, on the prefab root.")]
        [SerializeField] private ConicOrbiter orbiter;
        [Tooltip("The visible sphere: turned to face her and lit by the moon shader.")]
        [SerializeField] private Renderer body;
        [Tooltip("How far above her flight height the light sits, m. Higher lights the moons' tops, so they read as round from the chase camera.")]
        [SerializeField, Min(0f)] private float sunHeight = 2f;
        [Tooltip("Spin once batted, °/s.")]
        [SerializeField, Min(0f)] private float battedSpin = 720f;
        [Tooltip("Extra brightness once batted (0 = none). The flare says it is now on your side.")]
        [SerializeField, Min(0f)] private float battedBoost = 1.2f;

        MaterialPropertyBlock _block;
        float _spinAngle;

        void OnEnable() => _block ??= new MaterialPropertyBlock();

        void LateUpdate()
        {
            if (orbiter == null || body == null) return;
            Vector3 sun = orbiter.Plane.Focus + Vector3.up * sunHeight;
            Transform t = body.transform;

            Vector3 toSun = sun - t.position;
            if (toSun.sqrMagnitude > 1e-4f)
            {
                Quaternion facing = Quaternion.LookRotation(toSun);
                if (orbiter.IsBatted)
                {
                    _spinAngle = Mathf.Repeat(_spinAngle + battedSpin * Time.deltaTime, 360f);
                    facing *= Quaternion.Euler(_spinAngle, _spinAngle * 0.6f, 0f);
                }
                t.rotation = facing;
            }

            _block.Clear();
            _block.SetVector(SunPositionId, sun);
            _block.SetFloat(BoostId, orbiter.IsBatted ? battedBoost : 0f);
            body.SetPropertyBlock(_block);
        }
    }
}
