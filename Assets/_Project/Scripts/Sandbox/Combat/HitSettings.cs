using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// What one connecting hit does to its target, and how hard it freezes the
    /// action. Shared by the blade sweep and the area burst so both speak the
    /// same units.
    /// </summary>
    [Serializable]
    public struct HitSettings
    {
        [Tooltip("Health removed per hit, in HP. The base chain runs 10/12/14/20.")]
        [Min(0f)] [SerializeField] private float damage;

        [Tooltip("Push given to the target, in m/s, along the attacker's facing " +
                 "(or outward from the centre, for a burst). Keep mid-chain hits " +
                 "at or under ~1.5 m/s or a long chain shoves the target out of " +
                 "blade reach before the finisher.")]
        [Min(0f)] [SerializeField] private float knockback;

        [Tooltip("Upward speed given to the target, in m/s. Above zero the hit is " +
                 "a launcher: the receiver is told to pop into the air so following " +
                 "hits can juggle it. A boss is free to ignore it.")]
        [Min(0f)] [SerializeField] private float launchSpeed;

        [Tooltip("Seconds the swing freezes on contact. Bigger reads as heavier; " +
                 "past ~0.12 s it starts to feel like lag.")]
        [Range(0f, 0.3f)] [SerializeField] private float hitstop;

        public float Damage => damage;
        public float Knockback => knockback;
        public float LaunchSpeed => launchSpeed;
        public float Hitstop => hitstop;
        public bool IsLauncher => launchSpeed > 0f;

        public HitSettings(float damage, float knockback, float launchSpeed = 0f, float hitstop = 0.05f)
        {
            this.damage = Mathf.Max(0f, damage);
            this.knockback = Mathf.Max(0f, knockback);
            this.launchSpeed = Mathf.Max(0f, launchSpeed);
            this.hitstop = Mathf.Clamp(hitstop, 0f, 0.3f);
        }

        /// <summary>World-space push for a target, given the direction the hit
        /// travels in. Horizontal by design; height comes only from the launch.</summary>
        public Vector3 PushAlong(Vector3 direction)
        {
            direction.y = 0f;
            Vector3 push = direction.sqrMagnitude > 1e-6f ? direction.normalized * knockback : Vector3.zero;
            return push + Vector3.up * launchSpeed;
        }
    }
}
