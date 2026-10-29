using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// Which floor bands are burning, and which burns next: the rules of
    /// Alexandria's Burning with no scene in sight, so they are EditMode-tested.
    ///
    /// The order is fixed: each <see cref="StartSmoulder"/> picks the OUTERMOST
    /// band still <see cref="BandState.Unburned"/> (a smouldering band is
    /// already spoken for and is skipped), so the fire closes in monotonically.
    /// It stops before the first band <see cref="BandMath.IsBurnable"/> refuses,
    /// which keeps the hub and everything inside the minimum safe radius safe
    /// for the whole fight. A smoulder counts down on whatever clock
    /// <see cref="Tick"/> is fed (her clock, from <see cref="ArenaBurn"/>) and
    /// then ignites. Nothing ever un-burns.
    ///
    /// [Serializable] with plain arrays so that, where Unity's script-reload
    /// serializer keeps a MonoBehaviour's private fields, the floor survives a
    /// reload in Play mode. The reload serializer also turns a null field into
    /// an empty instance, so every member tolerates an unbuilt schedule
    /// (<see cref="IsBuilt"/> false: no bands, nothing burns).
    /// </summary>
    [Serializable]
    public sealed class BurnSchedule
    {
        [SerializeField] private float[] radii;
        [SerializeField] private BandState[] states;
        [SerializeField] private float[] smoulderLeft;
        [SerializeField] private float minSafeRadius;

        /// <summary>Required by the serializer; gives an unbuilt schedule.</summary>
        public BurnSchedule() { }

        /// <param name="bandOuterRadii">Raw config radii; repaired by
        /// <see cref="BandMath.Sanitise"/> (see there for what is repaired).</param>
        /// <param name="arenaRadius">The plate's radius, m; the last edge.</param>
        /// <param name="minSafeRadius">No band whose inner edge is inside this burns, m.</param>
        public BurnSchedule(float[] bandOuterRadii, float arenaRadius, float minSafeRadius)
        {
            radii = BandMath.Sanitise(bandOuterRadii, arenaRadius);
            states = new BandState[radii.Length];
            smoulderLeft = new float[radii.Length];
            this.minSafeRadius = float.IsNaN(minSafeRadius) ? 0f : minSafeRadius;
        }

        /// <summary>False for a default-constructed (or reload-emptied) schedule.</summary>
        public bool IsBuilt => radii != null && radii.Length > 0
                               && states != null && states.Length == radii.Length
                               && smoulderLeft != null && smoulderLeft.Length == radii.Length;

        /// <summary>Number of bands, hub included.</summary>
        public int BandCount => IsBuilt ? radii.Length : 0;

        /// <summary>The plate's radius, m (the last edge); 0 when unbuilt.</summary>
        public float ArenaRadius => IsBuilt ? radii[radii.Length - 1] : 0f;

        /// <summary>The minimum safe radius this schedule was built with, m.</summary>
        public float MinSafeRadius => minSafeRadius;

        /// <summary>Inner radius of a band, m.</summary>
        public float InnerRadius(int band) => IsBuilt ? BandMath.InnerRadius(radii, band) : 0f;

        /// <summary>Outer radius of a band, m.</summary>
        public float OuterRadius(int band) => IsBuilt ? BandMath.OuterRadius(radii, band) : 0f;

        /// <summary>A band's state; Unburned for an index out of range.</summary>
        public BandState StateOf(int band)
            => IsBuilt && band >= 0 && band < states.Length ? states[band] : BandState.Unburned;

        /// <summary>Seconds (on the clock fed to <see cref="Tick"/>) until a
        /// smouldering band ignites; 0 for any other band.</summary>
        public float SmoulderRemaining(int band)
            => StateOf(band) == BandState.Smouldering ? Mathf.Max(0f, smoulderLeft[band]) : 0f;

        /// <summary>The band containing this horizontal distance, or -1 (see
        /// <see cref="BandMath.BandAt"/> for edges).</summary>
        public int BandAt(float radius) => IsBuilt ? BandMath.BandAt(radii, radius) : -1;

        /// <summary>True when that distance lies in a band that is alight.</summary>
        public bool IsBurnedAt(float radius) => StateOf(BandAt(radius)) == BandState.Burned;

        /// <summary>Bands fully alight.</summary>
        public int BurnedBands => Count(BandState.Burned);

        /// <summary>Bands in their warning.</summary>
        public int SmoulderingBands => Count(BandState.Smouldering);

        /// <summary>
        /// The band the next <see cref="StartSmoulder"/> would pick: the
        /// outermost Unburned band, skipping smouldering and burned ones; -1
        /// when that band may not burn (inside the minimum safe radius, or the
        /// hub) or none is left.
        /// </summary>
        public int NextToIgnite
        {
            get
            {
                if (!IsBuilt) return -1;
                for (int i = states.Length - 1; i >= 0; i--)
                {
                    if (states[i] != BandState.Unburned) continue;
                    // Inner edges only shrink inward, so if this one is refused
                    // every band inside it is too.
                    return BandMath.IsBurnable(radii, i, minSafeRadius) ? i : -1;
                }
                return -1;
            }
        }

        /// <summary>False once every band outside the minimum safe radius is
        /// smouldering or burned.</summary>
        public bool CanIgnite => NextToIgnite >= 0;

        /// <summary>
        /// Outer radius of the unburned floor, m: the inner edge of the
        /// innermost burned band, or the arena radius before any band burns. A
        /// smouldering band still counts as floor (it is safe until it catches);
        /// see <see cref="ProjectedSafeRadius"/> for where the floor is heading.
        /// </summary>
        public float SafeRadius => EdgeInsideOf(includeSmouldering: false);

        /// <summary>The safe radius once every pending smoulder has caught, m.</summary>
        public float ProjectedSafeRadius => EdgeInsideOf(includeSmouldering: true);

        /// <summary>
        /// Start the next band smouldering. Returns the band, or -1 when
        /// <see cref="CanIgnite"/> is false (nothing changes then). A lead of 0
        /// or less ignites on the next <see cref="Tick"/>, even a frozen one.
        /// </summary>
        /// <param name="lead">Seconds of warning, on the clock fed to Tick.</param>
        public int StartSmoulder(float lead)
        {
            int band = NextToIgnite;
            if (band < 0) return -1;
            states[band] = BandState.Smouldering;
            smoulderLeft[band] = float.IsNaN(lead) ? 0f : Mathf.Max(0f, lead);
            return band;
        }

        /// <summary>
        /// Count every smoulder down by <paramref name="dt"/> (0 holds them:
        /// that is how freezing her freezes the warning) and ignite those that
        /// run out, calling <paramref name="onIgnited"/> with each band.
        /// Allocation-free; pass a cached delegate.
        /// </summary>
        public void Tick(float dt, Action<int> onIgnited)
        {
            if (!IsBuilt) return;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            // Outermost first, so two bands that catch in one tick report in
            // the order they were scheduled.
            for (int i = states.Length - 1; i >= 0; i--)
            {
                if (states[i] != BandState.Smouldering) continue;
                smoulderLeft[i] -= dt;
                if (smoulderLeft[i] > 0f) continue;
                smoulderLeft[i] = 0f;
                states[i] = BandState.Burned;
                onIgnited?.Invoke(i);
            }
        }

        /// <summary>Call off every pending smoulder (back to Unburned) for the
        /// end of the fight. Burned bands stay burned. Returns how many.</summary>
        public int CancelSmoulders()
        {
            if (!IsBuilt) return 0;
            int cancelled = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] != BandState.Smouldering) continue;
                states[i] = BandState.Unburned;
                smoulderLeft[i] = 0f;
                cancelled++;
            }
            return cancelled;
        }

        /// <summary>
        /// A uniformly random point (by area) on the burned floor, as a
        /// horizontal offset from the centre. <paramref name="pick"/>,
        /// <paramref name="u"/> and <paramref name="v"/> are 0-1 draws: the
        /// first chooses a band weighted by its area, the others place the point
        /// in it. False when nothing is burned.
        /// </summary>
        public bool TryPointOnBurnedFloor(float pick, float u, float v, out Vector3 offset)
        {
            offset = Vector3.zero;
            float total = 0f;
            for (int i = 0; i < BandCount; i++)
                if (states[i] == BandState.Burned) total += AreaWeight(i);
            if (total <= 0f) return false;

            float target = Mathf.Clamp01(pick) * total;
            int chosen = -1;
            for (int i = 0; i < BandCount; i++)
            {
                if (states[i] != BandState.Burned) continue;
                chosen = i;
                target -= AreaWeight(i);
                if (target <= 0f) break;
            }
            offset = BandMath.PointInAnnulus(InnerRadius(chosen), OuterRadius(chosen), u, v);
            return true;
        }

        float AreaWeight(int band)
        {
            float a = InnerRadius(band), b = OuterRadius(band);
            return b * b - a * a;
        }

        float EdgeInsideOf(bool includeSmouldering)
        {
            if (!IsBuilt) return 0f;
            float edge = ArenaRadius;
            for (int i = 0; i < states.Length; i++)
            {
                bool lost = states[i] == BandState.Burned
                            || (includeSmouldering && states[i] == BandState.Smouldering);
                if (lost) return Mathf.Min(edge, InnerRadius(i));
            }
            return edge;
        }

        int Count(BandState state)
        {
            if (!IsBuilt) return 0;
            int n = 0;
            for (int i = 0; i < states.Length; i++)
                if (states[i] == state) n++;
            return n;
        }
    }
}
