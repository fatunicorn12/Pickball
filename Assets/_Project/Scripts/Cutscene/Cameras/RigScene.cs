using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Everything a shot's points can refer to at one instant: MC, Birdy, the propeller and the
    /// markers. Filled in place each evaluation (no allocation) by <see cref="CutsceneCameraContext"/>.
    /// The sizes fall back to the shot list's nominal H = 1.6 m and HB = 0.55 m when an actor is missing.
    /// </summary>
    public sealed class RigScene
    {
        /// <summary>Nominal MC height when MC is missing, metres.</summary>
        public const float NominalH = 1.6f;

        /// <summary>Nominal Birdy head box when Birdy is missing, metres.</summary>
        public const float NominalHB = 0.55f;

        /// <summary>MC.</summary>
        public ActorSample MC;

        /// <summary>Birdy.</summary>
        public ActorSample Birdy;

        /// <summary>Whether the propeller is present.</summary>
        public bool HasProp;

        /// <summary>The propeller's root.</summary>
        public Vector3 Prop;

        /// <summary>The set markers.</summary>
        public IMarkerSource Markers;

        /// <summary>H: MC's measured height.</summary>
        public float H => MC.Valid && MC.Height > 0.01f ? MC.Height : NominalH;

        /// <summary>HB: Birdy's measured head box.</summary>
        public float HB => Birdy.Valid && Birdy.HeadBox > 0.01f ? Birdy.HeadBox : NominalHB;
    }
}
