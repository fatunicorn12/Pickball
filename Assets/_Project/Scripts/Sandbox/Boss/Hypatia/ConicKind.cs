namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// The three conic sections she fires, named by what eccentricity gives them:
    /// e &lt; 1 an ellipse, e = 1 a parabola, e &gt; 1 a hyperbola. The astrolabe
    /// needle shows the e before each one so the shape can be read in advance.
    /// </summary>
    public enum ConicKind
    {
        /// <summary>e = 1: a ballistic lob at the player's predicted position.</summary>
        Parabola,
        /// <summary>e &lt; 1: persistent orbiters with her at one focus.</summary>
        Ellipse,
        /// <summary>e &gt; 1: a fast flyby that stays between its two asymptotes.</summary>
        Hyperbola,
    }
}
