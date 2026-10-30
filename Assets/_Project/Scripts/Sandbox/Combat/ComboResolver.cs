using System.Collections.Generic;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Decides what the next press of Attack does. Pure logic: no scene, no
    /// Animator, no time — given the same context, moveset and unlocked set it
    /// always gives the same answer, which is what makes it EditMode-testable
    /// and the combo predictable to play.
    ///
    /// A chain on either track is built as:
    ///
    ///   [opener?]  base hits…  extenders by chainOrder…  [finisher]
    ///
    /// <list type="number">
    /// <item>Base hits come from the <see cref="SwordMoveset"/>.</item>
    /// <item>Every unlocked <see cref="ComboRole.Extender"/> on the track is
    /// appended after them, ordered by <see cref="SwordAbilityDefinition.ChainOrder"/>
    /// (ties by asset name), never by unlock order — so the chain plays the
    /// same however the player earned it.</item>
    /// <item>The cap is the highest-priority unlocked
    /// <see cref="ComboRole.Finisher"/> whose condition holds, else the
    /// moveset's default finisher, else nothing.</item>
    /// <item>On the first press only, the highest-priority qualifying
    /// <see cref="ComboRole.Opener"/> replaces the first hit and the chain
    /// carries on from the second.</item>
    /// </list>
    ///
    /// Every learned ability is first passed through the moveset
    /// (<see cref="SwordMoveset.StepFor"/>): the weapon decides which swing the
    /// ability plays with it, or that it plays none. An ability the weapon declines
    /// is skipped entirely — it neither lengthens the chain nor wins a finisher or
    /// opener slot — so a heavy weapon is never handed another blade's fast swing.
    /// Placement (role, track, order, priority, condition) is always the ability's.
    ///
    /// Once the chain is spent the resolver returns an invalid step: the ground
    /// chain restarts after the swing ends, the air chain only after landing.
    ///
    /// The instance owns scratch lists so a press allocates nothing; create one
    /// per <c>MeleeCombat</c> and do not share it between threads.
    /// </summary>
    public sealed class ComboResolver
    {
        readonly List<ResolvedStep> _sequence = new();
        readonly List<Placed> _extenders = new();

        /// <summary>A learned ability with the swing the moveset gave it.</summary>
        readonly struct Placed
        {
            public readonly SwordAbilityDefinition Ability;
            public readonly ComboStepDefinition Step;
            public Placed(SwordAbilityDefinition ability, ComboStepDefinition step)
            {
                Ability = ability;
                Step = step;
            }
        }

        /// <summary>The step the next press plays, or an invalid step if the
        /// chain has nothing more to give in this context.</summary>
        public ResolvedStep Resolve(in ComboContext context, SwordMoveset moveset,
                                    IReadOnlyList<SwordAbilityDefinition> unlocked)
        {
            if (context.HitsInChain < 0) return default;
            var sequence = BuildSequence(context, moveset, unlocked);
            return context.HitsInChain < sequence.Count ? sequence[context.HitsInChain] : default;
        }

        /// <summary>
        /// The whole chain as it would play from the current context — what a
        /// HUD combo preview or a debug readout shows. Openers are applied only
        /// when <see cref="ComboContext.HitsInChain"/> is 0, because whether one
        /// qualifies is decided at the moment the chain starts.
        ///
        /// The returned list is owned by the resolver and reused by the next call.
        /// </summary>
        public IReadOnlyList<ResolvedStep> BuildSequence(in ComboContext context, SwordMoveset moveset,
                                                         IReadOnlyList<SwordAbilityDefinition> unlocked)
        {
            _sequence.Clear();
            if (moveset == null) return _sequence;
            ComboTrack track = context.Track;

            var chain = moveset.ChainFor(track);
            for (int i = 0; i < chain.Count; i++)
                if (chain[i] != null)
                    _sequence.Add(new ResolvedStep(chain[i], ComboRole.Regular, track, _sequence.Count, false));

            CollectExtenders(track, moveset, unlocked);
            for (int i = 0; i < _extenders.Count; i++)
                _sequence.Add(new ResolvedStep(_extenders[i].Step, ComboRole.Extender, track,
                                               _sequence.Count, false, _extenders[i].Ability));

            var finisher = BestQualifying(ComboRole.Finisher, context, moveset, unlocked);
            if (finisher.Ability != null)
                _sequence.Add(new ResolvedStep(finisher.Step, ComboRole.Finisher, track,
                                               _sequence.Count, false, finisher.Ability));
            else if (moveset.DefaultFinisherFor(track) is { } fallback)
                _sequence.Add(new ResolvedStep(fallback, ComboRole.Finisher, track, _sequence.Count, false));

            if (context.HitsInChain == 0 && _sequence.Count > 0)
            {
                var opener = BestQualifying(ComboRole.Opener, context, moveset, unlocked);
                if (opener.Ability != null)
                    _sequence[0] = new ResolvedStep(opener.Step, ComboRole.Opener, track, 0, false, opener.Ability);
            }

            if (_sequence.Count > 0) _sequence[^1] = _sequence[^1].AsLast();
            return _sequence;
        }

        /// <summary>Unlocked extenders on <paramref name="track"/> that the moveset
        /// gives a swing, sorted by chain order then name. Insertion sort: the list
        /// is a handful long and this keeps the press allocation-free and the order
        /// stable.</summary>
        void CollectExtenders(ComboTrack track, SwordMoveset moveset, IReadOnlyList<SwordAbilityDefinition> unlocked)
        {
            _extenders.Clear();
            if (unlocked == null) return;
            for (int i = 0; i < unlocked.Count; i++)
            {
                var a = unlocked[i];
                if (a == null || a.Role != ComboRole.Extender || a.Track != track) continue;
                var step = moveset.StepFor(a);
                if (step == null) continue;

                int at = _extenders.Count;
                while (at > 0 && ComesBefore(a, _extenders[at - 1].Ability)) at--;
                _extenders.Insert(at, new Placed(a, step));
            }
        }

        static bool ComesBefore(SwordAbilityDefinition a, SwordAbilityDefinition b)
        {
            if (a.ChainOrder != b.ChainOrder) return a.ChainOrder < b.ChainOrder;
            return string.CompareOrdinal(a.name, b.name) < 0;
        }

        /// <summary>The highest-priority unlocked ability of <paramref name="role"/>
        /// on the context's track whose condition holds and that the moveset gives a
        /// swing, with that swing. Ties go to the one unlocked first, which only
        /// matters if two are authored identically. Default (no ability) if none.</summary>
        static Placed BestQualifying(ComboRole role, in ComboContext context, SwordMoveset moveset,
                                     IReadOnlyList<SwordAbilityDefinition> unlocked)
        {
            if (unlocked == null) return default;
            Placed best = default;
            for (int i = 0; i < unlocked.Count; i++)
            {
                var a = unlocked[i];
                if (a == null || a.Role != role || a.Track != context.Track) continue;
                if (!a.Condition.IsMetBy(context)) continue;
                if (best.Ability != null && a.Priority <= best.Ability.Priority) continue;
                var step = moveset.StepFor(a);
                if (step != null) best = new Placed(a, step);
            }
            return best;
        }
    }
}
