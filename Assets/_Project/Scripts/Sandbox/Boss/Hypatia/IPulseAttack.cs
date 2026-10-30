namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>The knockback pulse, as the brain drives it: the brain decides
    /// WHEN (its pulse timer), the attack owns the charge, the hit and the push.</summary>
    public interface IPulseAttack : IHypatiaAttack
    {
        /// <summary>Start charging; the pulse fires after Pulse Charge Time on her
        /// clock. False (nothing happens) when already charging or unbound.</summary>
        bool BeginPulse();

        /// <summary>True from <see cref="BeginPulse"/> until it fires or is cancelled.</summary>
        bool IsCharging { get; }

        /// <summary>Drop a charging pulse without firing (she toppled, or the
        /// recalibration window opened). No-op when not charging.</summary>
        void CancelPulse();
    }
}
