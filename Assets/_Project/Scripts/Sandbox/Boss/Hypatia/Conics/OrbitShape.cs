namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>The size and shape <see cref="OrbitFit"/> chose for one orbiter.</summary>
    public readonly struct OrbitShape
    {
        public OrbitShape(float semiMajor, float eccentricity)
        {
            SemiMajor = semiMajor;
            Eccentricity = eccentricity;
        }

        /// <summary>a, m.</summary>
        public float SemiMajor { get; }

        /// <summary>e, in [0, 1).</summary>
        public float Eccentricity { get; }

        /// <summary>a(1−e), m.</summary>
        public float Periapsis => SemiMajor * (1f - Eccentricity);

        /// <summary>a(1+e), m.</summary>
        public float Apoapsis => SemiMajor * (1f + Eccentricity);
    }
}
