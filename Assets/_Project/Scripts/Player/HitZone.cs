using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// A swing-validity volume in front of a paddle. Rather than rely on physics trigger
    /// enter/exit events (which are unreliable when the ball is teleported in for a serve),
    /// callers simply ask <see cref="Contains"/> whether the ball is inside right now.
    /// The BoxCollider is kept as a trigger so it never physically blocks the ball.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class HitZone : MonoBehaviour
    {
        private BoxCollider _box;

        private BoxCollider Box
        {
            get
            {
                if (_box == null) _box = GetComponent<BoxCollider>();
                return _box;
            }
        }

        private void Awake() => Box.isTrigger = true;

        /// <summary>True if the world point lies within the zone's (axis-aligned) bounds.</summary>
        public bool Contains(Vector3 worldPoint) => Box.bounds.Contains(worldPoint);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Box.center, Box.size);
        }
#endif
    }
}
