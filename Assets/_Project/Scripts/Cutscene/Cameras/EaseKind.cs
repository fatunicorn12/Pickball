namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The easing a camera move uses between two keys. The shot list names three ("ease-in",
    /// "ease-out", "ease-in-out"); linear is for moves the camera should not soften, such as the
    /// track that stays abeam a walker.
    /// </summary>
    public enum EaseKind
    {
        /// <summary>Constant speed.</summary>
        Linear = 0,
        /// <summary>Smoothstep: slow out, slow in. The shot list's "ease-in-out".</summary>
        InOut = 1,
        /// <summary>Quadratic start: slow out, full speed at the end.</summary>
        In = 2,
        /// <summary>Quadratic end: full speed out, settles at the end.</summary>
        Out = 3,
    }
}
