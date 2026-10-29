namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// What the brain needs from every attack component on her root. The brain
    /// owns WHEN (its timers); the attack owns HOW (its own telegraphs, projectiles
    /// and contacts). Serialized on the brain as a MonoBehaviour and cast, as
    /// MeleeCombat does with its movement source.
    /// </summary>
    public interface IHypatiaAttack
    {
        /// <summary>Called by the brain in its OnEnable (so again after a script
        /// reload). Replace any cached references with the context's.</summary>
        void Bind(HypatiaContext context);

        /// <summary>Stop now: cancel pending telegraphs, despawn everything live,
        /// stop dealing damage. Called on her death or the player's. Idempotent.</summary>
        void StopAll();
    }
}
