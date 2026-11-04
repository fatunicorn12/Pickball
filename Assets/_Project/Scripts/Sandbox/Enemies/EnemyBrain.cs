using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// The basic enemy's rules as pure functions: when it notices you, when it can
    /// reach you, and which state that puts it in. Plain C# so they are tested without
    /// a scene (<c>EnemyBrainTests</c>); <see cref="Enemy"/> only feeds them facts.
    /// </summary>
    public static class EnemyBrain
    {
        /// <summary>
        /// Whether the target is noticed. Two ranges rather than one: an enemy notices
        /// at <paramref name="aggroRange"/> and only gives up beyond
        /// <paramref name="loseRange"/>, so a target standing on the line does not flip
        /// it between running and stopping every frame.
        /// </summary>
        /// <param name="wasAware">Whether it had noticed the target last frame.</param>
        /// <param name="distance">Metres to the target.</param>
        /// <param name="aggroRange">Metres at which it notices.</param>
        /// <param name="loseRange">Metres beyond which it gives up. A value below
        /// <paramref name="aggroRange"/> counts as equal to it.</param>
        public static bool IsAware(bool wasAware, float distance, float aggroRange, float loseRange)
            => distance <= (wasAware ? Mathf.Max(aggroRange, loseRange) : aggroRange);

        /// <summary>
        /// Whether a strike from <paramref name="from"/> can land on
        /// <paramref name="to"/>. Height is judged apart from the horizontal reach, so
        /// jumping over an enemy really does dodge it.
        /// </summary>
        /// <param name="from">The enemy's feet.</param>
        /// <param name="to">The target's feet.</param>
        /// <param name="reach">Horizontal metres.</param>
        /// <param name="height">Metres of height difference, either way.</param>
        public static bool IsInReach(Vector3 from, Vector3 to, float reach, float height)
        {
            Vector3 d = to - from;
            float flatSq = d.x * d.x + d.z * d.z;
            return flatSq <= reach * reach && Mathf.Abs(d.y) <= height;
        }

        /// <summary>The state these facts call for. Order is priority: dead beats
        /// stunned, stunned beats everything it would otherwise want to do.</summary>
        public static EnemyState Decide(in EnemySenses senses)
        {
            if (senses.Defeated) return EnemyState.Dead;
            if (!senses.CanAct) return EnemyState.Stunned;
            if (!senses.HasTarget || !senses.Aware) return EnemyState.Idle;
            return senses.InReach ? EnemyState.Attack : EnemyState.Chase;
        }

        /// <summary>
        /// Horizontal speed that runs toward the target and stops at
        /// <paramref name="stopDistance"/> without overshooting at a low frame rate.
        /// </summary>
        /// <param name="flatDistance">Horizontal metres to the target.</param>
        /// <param name="stopDistance">Metres from the target to stop at.</param>
        /// <param name="speed">Top speed, m/s.</param>
        /// <param name="dt">Frame time, s.</param>
        public static float ApproachSpeed(float flatDistance, float stopDistance, float speed, float dt)
        {
            float gap = flatDistance - stopDistance;
            if (gap <= 0f || speed <= 0f) return 0f;
            return dt > 0f ? Mathf.Min(speed, gap / dt) : speed;
        }
    }
}
