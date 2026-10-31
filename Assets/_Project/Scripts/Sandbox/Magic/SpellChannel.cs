using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The rules of a held, stand-still channel (Heal): a fixed time with fire
    /// held, cancelled by anything that is not standing still, and paid for at
    /// the start or at the end.
    ///
    /// Plain C#, so every rule is an EditMode test: the timer, each cancel
    /// reason and the mana timing. <see cref="SpellCaster"/> feeds it one
    /// <see cref="ChannelSignals"/> per frame and does what it reports — heals,
    /// starts the cooldown, raises the events.
    ///
    /// <b>Order of reasons.</b> When several apply on one frame, the most
    /// specific is reported: blocked, hurt, busy, airborne, moved, displaced,
    /// released.
    ///
    /// <b>Standing still means it.</b> Besides movement input, the body being
    /// carried more than <see cref="MaxDrift"/> from where it began (a gravity
    /// well, a push, anything) cancels as <see cref="ChannelStopReason.Displaced"/>.
    /// Damage interrupts only by <see cref="HitInterrupts"/>: a real hit does, a
    /// sub-threshold tick does not.
    ///
    /// <b>Settling.</b> A heal pressed mid-run used to cancel on the player's own
    /// deceleration: the stick was still held for a frame or two, and the body
    /// slid past the drift limit while braking. For the first
    /// <see cref="SettleSeconds"/> of a channel, movement input and drift are
    /// ignored (the caster brakes the body to a stop and roots it), and the
    /// drift origin is re-taken where the body came to rest. Movement input
    /// does not refuse the start either when there is a settle window: the
    /// body is about to be stopped anyway.
    /// "You were hit" says more than "you let go" when both happened because
    /// the hit knocked the stick.
    ///
    /// <b>Frame-rate independent.</b> It completes on the first frame whose
    /// accumulated time reaches the duration (to within a microsecond, so
    /// 120 frames of 1/60 s complete 2 s exactly), and a cancel on the
    /// completing frame wins: moving on the last frame still cancels.
    /// </summary>
    public sealed class SpellChannel
    {
        // Float sums of 1/60 drift by ~1e-6 over two seconds.
        const float Epsilon = 1e-5f;

        /// <summary>True between a successful <see cref="TryBegin"/> and the
        /// completion or cancel.</summary>
        public bool IsChanneling { get; private set; }

        /// <summary>Seconds channelled so far.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Seconds the running (or last) channel needs.</summary>
        public float Duration { get; private set; }

        /// <summary>0 at the start, 1 on completion. Holds its last value after a
        /// cancel so a HUD can show where it broke.</summary>
        public float Progress => Duration > 0f ? Mathf.Clamp01(Elapsed / Duration) : (IsChanneling ? 0f : 1f);

        /// <summary>Mana points the running channel costs.</summary>
        public int Cost { get; private set; }

        /// <summary>When the running channel pays.</summary>
        public ChannelManaTiming Timing { get; private set; }

        /// <summary>True once the running channel's cost has been taken.</summary>
        public bool Paid { get; private set; }

        /// <summary>Why the last channel was refused or cancelled;
        /// <see cref="ChannelStopReason.None"/> after a completion.</summary>
        public ChannelStopReason LastStop { get; private set; }

        /// <summary>Where the body stood when the running (or last) channel began.</summary>
        public Vector3 Origin { get; private set; }

        /// <summary>Metres across the ground the body may be moved from
        /// <see cref="Origin"/> before the channel cancels as
        /// <see cref="ChannelStopReason.Displaced"/>; 0 or less = no limit.</summary>
        public float MaxDrift { get; private set; }

        /// <summary>Seconds at the start of the running channel during which movement
        /// input and drift do not cancel it; 0 = none.</summary>
        public float SettleSeconds { get; private set; }

        /// <summary>True while the running channel is still in its settle window.</summary>
        public bool IsSettling => IsChanneling && Elapsed + Epsilon < SettleSeconds;

        /// <summary>
        /// The one damage rule for a channel: a hit of at least
        /// <paramref name="minDamage"/> interrupts it; anything smaller (a gravity
        /// well's core tick, a burn, a beam's chip) does not. A non-positive
        /// <paramref name="minDamage"/> means any damage above zero interrupts.
        /// </summary>
        public static bool HitInterrupts(float damage, float minDamage)
            => damage > 0f && damage >= minDamage;

        /// <summary>True when <paramref name="now"/> is more than
        /// <paramref name="maxDrift"/> metres across the ground (height ignored)
        /// from <paramref name="origin"/>. A non-positive limit never drifts.</summary>
        public static bool Drifted(Vector3 origin, Vector3 now, float maxDrift)
        {
            if (maxDrift <= 0f) return false;
            float dx = now.x - origin.x, dz = now.z - origin.z;
            return dx * dx + dz * dz > maxDrift * maxDrift;
        }

        /// <summary>
        /// The first rule <paramref name="signals"/> break, in reporting order, or
        /// <see cref="ChannelStopReason.None"/>. Shared by starting and ticking,
        /// so a channel can never start in a state that would cancel it.
        /// </summary>
        public static ChannelStopReason Interruption(in ChannelSignals signals)
        {
            if (signals.Blocked) return ChannelStopReason.Blocked;
            if (signals.Hurt) return ChannelStopReason.Hurt;
            if (signals.Busy) return ChannelStopReason.Busy;
            if (signals.Airborne) return ChannelStopReason.Airborne;
            if (signals.Moving) return ChannelStopReason.Moved;
            if (!signals.Held) return ChannelStopReason.Released;
            return ChannelStopReason.None;
        }

        /// <summary>
        /// Starts a channel of <paramref name="duration"/> seconds if nothing
        /// forbids it. Returns <see cref="ChannelStopReason.None"/> on success,
        /// otherwise the reason it was refused (and changes nothing else).
        ///
        /// Mana: <see cref="ChannelManaTiming.OnStart"/> spends here (a failed
        /// spend refuses); <see cref="ChannelManaTiming.OnCompletion"/> only
        /// checks the cost is affordable now. A cost of zero or less is free.
        /// A null <paramref name="mana"/> can afford only a free channel.
        /// Starting while already channelling is refused as
        /// <see cref="ChannelStopReason.None"/> with no effect: the running one
        /// carries on.
        /// </summary>
        /// <param name="maxDrift">Metres the body may be moved from where it stands
        /// now (<see cref="ChannelSignals.Position"/>) before the channel cancels as
        /// <see cref="ChannelStopReason.Displaced"/>; 0 = no limit.</param>
        /// <param name="settleSeconds">Seconds at the start during which movement
        /// input and drift are ignored; while positive, movement input does not
        /// refuse the start. 0 = none.</param>
        public ChannelStopReason TryBegin(in ChannelSignals signals, float duration, int cost,
                                          ChannelManaTiming timing, Mana mana, float maxDrift = 0f,
                                          float settleSeconds = 0f)
        {
            if (IsChanneling) return ChannelStopReason.None;

            var judged = signals;
            if (settleSeconds > 0f) judged.Moving = false;
            var reason = Interruption(judged);
            if (reason == ChannelStopReason.None && signals.HealthUnavailable) reason = ChannelStopReason.Unavailable;
            if (reason == ChannelStopReason.None && signals.HealthFull) reason = ChannelStopReason.FullHealth;

            bool paid = false;
            if (reason == ChannelStopReason.None && cost > 0)
            {
                if (mana == null) reason = ChannelStopReason.NoMana;
                else if (timing == ChannelManaTiming.OnStart)
                {
                    if (mana.TrySpend(cost)) paid = true;
                    else reason = ChannelStopReason.NoMana;
                }
                else if (!mana.CanAfford(cost)) reason = ChannelStopReason.NoMana;
            }

            if (reason != ChannelStopReason.None)
            {
                LastStop = reason;
                return reason;
            }

            IsChanneling = true;
            Elapsed = 0f;
            Duration = Mathf.Max(0f, duration);
            Cost = cost;
            Timing = timing;
            Paid = paid || cost <= 0;
            Origin = signals.Position;
            MaxDrift = maxDrift;
            SettleSeconds = Mathf.Max(0f, settleSeconds);
            LastStop = ChannelStopReason.None;
            return ChannelStopReason.None;
        }

        /// <summary>
        /// Advances the channel by <paramref name="dt"/> seconds. Any broken rule
        /// cancels it first (no time is added on that frame). Reaching the
        /// duration completes it, paying an <see cref="ChannelManaTiming.OnCompletion"/>
        /// cost now; if that spend fails the channel is cancelled as
        /// <see cref="ChannelStopReason.NoMana"/> instead.
        /// </summary>
        public ChannelStep Tick(float dt, in ChannelSignals signals, Mana mana)
        {
            if (!IsChanneling) return ChannelStep.Idle;

            // Settling: the stick and the braking slide are the player stopping,
            // not the player leaving.
            bool settling = IsSettling;
            var judged = signals;
            if (settling) judged.Moving = false;

            // Displacement ranks after movement input ("Moved" says more when the
            // player walked) and before letting go.
            var reason = Interruption(judged);
            if (!settling && (reason == ChannelStopReason.None || reason == ChannelStopReason.Released)
                && Drifted(Origin, signals.Position, MaxDrift))
                reason = ChannelStopReason.Displaced;
            if (reason != ChannelStopReason.None)
            {
                Stop(reason);
                return ChannelStep.Cancelled;
            }

            if (dt > 0f) Elapsed += dt;
            // Drift is measured from where he came to rest, not where he pressed.
            if (settling && !IsSettling) Origin = signals.Position;
            if (Elapsed + Epsilon < Duration) return ChannelStep.Channeling;

            if (!Paid)
            {
                if (mana == null || !mana.TrySpend(Cost))
                {
                    Stop(ChannelStopReason.NoMana);
                    return ChannelStep.Cancelled;
                }
                Paid = true;
            }

            Elapsed = Duration;
            IsChanneling = false;
            LastStop = ChannelStopReason.None;
            return ChannelStep.Completed;
        }

        /// <summary>Cancels a running channel for <paramref name="reason"/> (switching
        /// spell, losing aim, being disabled). Returns false if none was running.
        /// Mana already paid (<see cref="ChannelManaTiming.OnStart"/>) is not refunded.</summary>
        public bool Cancel(ChannelStopReason reason)
        {
            if (!IsChanneling) return false;
            Stop(reason == ChannelStopReason.None ? ChannelStopReason.Released : reason);
            return true;
        }

        void Stop(ChannelStopReason reason)
        {
            IsChanneling = false;
            LastStop = reason;
        }
    }
}
