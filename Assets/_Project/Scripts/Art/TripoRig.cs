namespace Pickleball.Art
{
    /// <summary>
    /// How a Tripo model's skeleton is imported. Static props have none; rigged
    /// characters become Unity Humanoids so any humanoid clip in the project
    /// (Mixamo, Quaternius, Newton's) retargets onto them.
    /// </summary>
    public enum TripoRig
    {
        /// <summary>A static mesh: no Animator, no avatar.</summary>
        None,

        /// <summary>A Humanoid avatar built from the model's own skeleton
        /// (Tripo's Mixamo rig preset auto-maps; no bone map needed).</summary>
        Humanoid,
    }
}
