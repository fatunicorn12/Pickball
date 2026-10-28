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
    /// specific is reported: blocked, hurt, busy, airborne, moved, released.
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
        public ChannelStopReason TryBegin(in ChannelSignals signals, float duration, int cost,
                                          ChannelManaTiming timing, Mana mana)
        {
            if (IsChanneling) return ChannelStopReason.None;

            var reason = Interruption(signals);
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

            var reason = Interruption(signals);
            if (reason != ChannelStopReason.None)
            {
                Stop(reason);
                return ChannelStep.Cancelled;
            }

            if (dt > 0f) Elapsed += dt;
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
