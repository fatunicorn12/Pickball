namespace Pickleball.Art
{
    /// <summary>
    /// The collider a Tripo prefab gets. Only solid props get one: projectiles and
    /// effect meshes are moved and hit-tested by their own code, and a PhysX
    /// collider there would give the physics engine a second, conflicting vote.
    /// </summary>
    public enum TripoColliderShape
    {
        /// <summary>No collider.</summary>
        None,

        /// <summary>One BoxCollider on the prefab root, fitted to the mesh bounds.</summary>
        Box,
    }
}
