namespace Pickleball.Sandbox.Enemies
{
    /// <summary>What an <see cref="Enemy"/> is doing this frame. Presentation
    /// (animation, audio) reads it through <see cref="Enemy.StateChanged"/>.</summary>
    public enum EnemyState
    {
        /// <summary>No target in range, or the target is dead. Stands still.</summary>
        Idle = 0,
        /// <summary>Target in range but out of reach: running at it.</summary>
        Chase = 1,
        /// <summary>Target in reach: striking on the attack interval.</summary>
        Attack = 2,
        /// <summary>In hitstun, juggled, getting up or staggered. Cannot act.</summary>
        Stunned = 3,
        /// <summary>Defeated. Stops simulating and despawns after the corpse time.</summary>
        Dead = 4,
    }
}
