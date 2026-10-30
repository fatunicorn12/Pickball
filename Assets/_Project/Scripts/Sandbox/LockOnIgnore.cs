using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Marks part of a lock-on target as scenery. <see cref="LockOnTargeting"/>
    /// measures a target's aim point and size from the renderers under its
    /// <see cref="Health"/>; renderers under this marker are left out.
    ///
    /// Why it exists: Hypatia carries her giant astrolabe dial and a halo as
    /// children so they turn and hover with her. Measured with her, they put the
    /// aim point on the dial 3 m above her head and doubled her radius, so the
    /// reticle and camera framed the dial instead of her. No behaviour of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockOnIgnore : MonoBehaviour
    {
    }
}
