using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How long a chain takes when every press is buffered (mashed), worked out from the
    /// step assets alone. Pure functions over the same rules <see cref="SwingTimeline"/>
    /// plays by, so a moveset's pace can be compared and pinned in EditMode tests
    /// without a scene: the ruler's "slower, harder" brief is a ratio against the
    /// compass, and this is where that ratio is measured.
    ///
    /// A mashed press chains as soon as the chain window opens: at comboOpen, but never
    /// before activeEnd or the step's burst (<see cref="SwingTimeline.IsChainWindowOpen"/>),
    /// and no later than the swing's end at comboClose. The last step of a chain plays
    /// to comboClose. Hitstop is added once per hit that connects, which is how
    /// <c>MeleeCombat</c> freezes the clock.
    /// </summary>
    public static class ComboTiming
    {
        /// <summary>Fraction of the swing at which a buffered press starts the next
        /// step.</summary>
        public static float ChainFraction(ComboStepDefinition step)
        {
            var w = step.Windows;
            float open = Mathf.Max(w.ComboOpen, w.ActiveEnd);
            if (step.Burst.Enabled) open = Mathf.Max(open, step.Burst.TriggerAt);
            return Mathf.Min(open, EndFraction(step));
        }

        /// <summary>Fraction of the swing at which it is over (comboClose, or the
        /// clip's end).</summary>
        public static float EndFraction(ComboStepDefinition step) => Mathf.Min(1f, step.Windows.ComboClose);

        /// <summary>Seconds from the step starting to its blade going live: the
        /// wind-up the player sees before anything can be hit.</summary>
        public static float WindupSeconds(ComboStepDefinition step) => step.Duration * step.Windows.ActiveStart;

        /// <summary>Seconds of hitstop one connecting step adds: the blade's, plus the
        /// burst's if it has one (the ring strikes on a later frame, so its freeze is
        /// separate).</summary>
        public static float HitstopSeconds(ComboStepDefinition step)
            => step.Hit.Hitstop + (step.Burst.Enabled ? step.Burst.Hit.Hitstop : 0f);

        /// <summary>Damage one step deals to a single target its blade reaches, plus its
        /// ring's when <paramref name="withBurst"/>.</summary>
        public static float DamageOnOneTarget(ComboStepDefinition step, bool withBurst = true)
            => step.Hit.Damage + (withBurst && step.Burst.Enabled ? step.Burst.Hit.Damage : 0f);

        /// <summary>Seconds a mashed chain of <paramref name="steps"/> lasts from the
        /// first press to the last swing's end, plus every connecting hit's hitstop
        /// when <paramref name="withHitstop"/>.</summary>
        public static float ChainSeconds(IReadOnlyList<ResolvedStep> steps, bool withHitstop)
        {
            float t = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i].Step;
                if (step == null) continue;
                bool last = i == steps.Count - 1;
                t += step.Duration * (last ? EndFraction(step) : ChainFraction(step));
                if (withHitstop) t += HitstopSeconds(step);
            }
            return t;
        }

        /// <summary>Total damage of the chain on one target that every blade (and, when
        /// <paramref name="withBursts"/>, every ring) reaches.</summary>
        public static float ChainDamage(IReadOnlyList<ResolvedStep> steps, bool withBursts = true)
        {
            float d = 0f;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i].Step != null) d += DamageOnOneTarget(steps[i].Step, withBursts);
            return d;
        }
    }
}
