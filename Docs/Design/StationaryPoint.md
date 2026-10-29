# Stationary Point (proposed)

> **Status: proposed, not built.** Design spec written 2026-10-28. Nothing in the code does this
> yet. When it ships, its behaviour moves into `Docs/Systems/Spacetime.md` (and `Magic.md` for the
> spell side) and this page is deleted.

A 3-second stun that makes the derivative literal: for 3 s, the target's derivative is zero. It
sits **alongside** the 4D worldline beams and does not replace them.

## 1. Purpose and feel

In calculus, a moment where f′(t) = 0 is called a **stationary point**: the function is, for that
instant, not changing. This ability forces one on a target. For 3 seconds every rate of change the
target owns is multiplied by zero:

- It does not move or turn.
- Its attack clocks (wind-ups, charges, cooldowns, telegraphs) stop counting.
- Its animation holds its pose.

When the 3 s end, everything resumes from exactly where it stopped. Nothing is cancelled or reset;
it is paused.

**The pitch: spend three seconds of your time to take three of theirs.** It costs 3 s of the 4D
meter, which is half of a full 6 s meter.

**How it fits the 4D loop** ("in 3D you change things, in 4D you walk on the results"). The stun is
a change you make in the world, and 4D shows it: the stunned stretch of a worldline goes straight up
(§4).

**How it fits Newton.** His Fluxion Beam already hurts you by your derivative: its damage equals
your speed, and standing still is free. Stationary Point turns the same calculus on him and makes
*him* stand still. Placeholder barks for the story owner to replace, in `NewtonBarks`:
- "You differentiate *me*? Those are my fluxions, student."
- "A stationary point. How very Leibnizian."

## 2. Rules

| Rule | Value | Why |
|---|---|---|
| Duration | 3 s | |
| Cost | 3 s of 4D meter, paid on cast | The meter is what ties it to 4D. |
| Availability | Only while Spacetime is enabled and the meter holds at least 3 s. Castable in 3D or 4D. | |
| Targeting | The lock-on target; otherwise the stunnable target under the crosshair. No valid target: no cast, no cost. | It cannot miss, so the cost is never wasted. |
| Immunity | For 6 s after a stun **ends**, the same target cannot be stunned again. | The meter refills at 1 s per second in 3D, so without this you could re-stun the moment the last stun ends, a permanent lock. With it, a target is frozen at most 3 s in every 9 s (33%). |
| Overlap with slows | A stun is a slow with multiplier 0. The strongest slow wins, so the stun overrides a Freeze Zone, and the zone's 30% resumes when the stun ends. | This is the existing `ISlowable` contract, unchanged. |
| Damage taken | Normal, weak-point multiplier included. | |
| Released projectiles | Keep moving. Apples and volley orbs already in the air are not frozen. | Freezing only the target keeps the stun from being an "everything stops" button. |
| The player | Cannot be stunned by it (out of scope). | |

## 3. On Newton

What freezes: his movement and turning, all four attack timers (wells, beam, volley, barrage), his
wind-up and recoil, and his animator. His timing is driven by code, not by the animation, so his
owner sets the animator's speed in code rather than through `StatusReceiver.animatorToSlow`.

- **Fluxion Beam mid-fire (recommended):** it freezes with him. It stops tracking, stops dealing
  damage (damage per second is a rate of change too), and its remaining fire time resumes after the
  stun. A frozen beam is a window to step out of its line. The alternative is to cancel the beam.
  This is the boss owner's call.
- **Barrage:** spawning pauses. Apples already falling keep falling.
- **Phase 2:** still triggers on health. A stun does not delay it.

**Prerequisite.** Newton has to honour slows first. That work is already listed in `Magic.md`
under "What Newton needs" (the Freeze Zone): every timer multiplies `dt` by `SpeedMultiplier`.
Once that is in, the stun works on him with no further boss code, except the beam's damage pause
and the animator.

**Balance flag.** The Magic system caps a stagger at 1.5 s and never stacks staggers, so that
Newton cannot be stun-locked (`StatusReceiver.maxStaggerSeconds`). Stationary Point prevents locks
with the immunity window instead, so it keeps its full 3 s. Measure what the sword deals in 3 s at
the weak point. If it is too much, add a per-target cap (for example 2 s on Newton).

## 4. What it looks like in 4D

The 4D view is a graph of position against time: time runs upward at `timeAxis` = 0.5 m per
second. That makes the derivative visible:

- **A beam's lean is its object's speed.** An object moving horizontally at v m/s leaves a beam
  that rises 0.5 m for every v m it runs, so its angle above horizontal is atan(0.5 / v).
- **Walkable beams need movement.** The player can walk slopes up to 50° (`cc.slopeLimit`,
  `ChibiSandboxSetup.cs:1257`). A beam is walkable only where its object moved at least
  0.5 / tan 50° ≈ 0.42 m/s.
- **A stunned stretch has v = 0,** so the beam goes straight up for 3 s × 0.5 m/s = 1.5 m. That
  section is a thin vertical wall (beam width 1.1 m, thickness 0.2 m), not a platform.

What follows from that:

- **A stun cuts a 1.5 m wall into its target's ramp.** Stun a swinging platform and, in 4D, its
  ramp has a riser you have to jump. That might be a puzzle tool (break a ramp, raise a wall) or a
  nuisance. Open question 1.
- **Slows do the same, more gently.** If movers honour slows, a platform moving at 1 m/s inside a
  30% Freeze Zone moves at 0.3 m/s, and its ramp steepens to 59°, too steep to walk.
- **The post hangs straight below where the object froze,** because older positions are drawn
  lower. On a grounded target it is underground. On Newton (traced 1 m above his pivot,
  `SpacetimeSandboxSetup.cs:76`) most of it is inside his body. The stun reads best in 4D on things
  that are off the ground: moving platforms and the lobbed ball.
- **Recorders keep 4 s of history** (`historySeconds`). To see the whole post, flip within 1 s of
  the stun ending, or raise `historySeconds` on that recorder.
- **No recording changes are needed.** A still object still samples every 0.1 s
  (`sampleInterval`), so the post is drawn as a straight line.
- **The two corners are kinks.** The speed jumps from v to 0 and back, so the derivative is
  undefined at those two instants and the beam turns sharply there. `WorldlineMesh` already seeds
  vertical starts so it does not twist. Check in SpacetimeLab that a ramp-to-vertical-to-ramp turn
  does not pinch.

**Optional: highlight stationary sections** (for example in gold) so "f′ = 0" is readable at a
glance. Stationary stretches can be found from the existing samples (consecutive samples closer
than `sampleSpacing`), so no new recording is needed, only a vertex-colour pass in `WorldlineMesh`.

## 5. Input and delivery

There is no free button. Every gamepad face button, shoulder, trigger, stick click and d-pad
direction is bound, and keyboard T is the flip.

**Recommended: a spell in the existing selection,** cast with aim (RMB / LT) and fire (LMB / RT),
offered only while Spacetime is enabled. It reuses aim, fire, selection and cooldowns, and leaves
the flip on T untouched.

- **Delivery:** a new `SpellDelivery` value, `Instant = 4` (the enum is append-only). It applies to
  the target with no projectile and no telegraph. Not `Orb`, because a miss would waste 3 s of
  meter. Not `Strike`, because its telegraph delay makes a stun land late.
- **Cost:** the 4D meter, not mana. Magic must not reference Spacetime, so the caster pays through
  a small interface that `SpacetimeView` implements (the Sandbox's interface pattern, see
  `Docs/Architecture/Communication.md`).

**Rejected: hold T to stun.** A flip could then only fire on release, adding latency to every flip,
and the flip's timing decides the beams' shape.

## 6. Architecture sketch

| Piece | Owner | Job |
|---|---|---|
| `StationaryPointRule` (plain C#) | Spacetime | Meter check and cost, per-target immunity, accepted duration. EditMode-tested. |
| Applying the stun | — | `ISlowable.ApplySlow(rule, 0f, 3f)`. A zero multiplier is already allowed (the Freeze Zone's tooltip says "0 is a full freeze"), and the ledger's "strongest wins" gives the overlap rule for free. No new interface. |
| Meter spend | Spacetime | `SpacetimeView` gains a spend method behind the interface in §5. |
| Spell asset and `Instant` delivery | Magic | `SpellCaster`, `SpellDefinition` and the Setup Magic builders are held by the Bird NPC session. Check `locks.md` first. |
| Stun look | Magic (`StatusReceiverVisual`) | Today a zero slow would show frost. The stun needs its own look: the held pose plus a glyph (f′ = 0, or a stopped clock). |
| Newton opts in to slows | Boss owner | Already listed in `Magic.md`. |
| Lab movers honour slows | Spacetime | `SpacetimeDemoMover` advances its own `_clock` by `Time.deltaTime`. Multiplying that by a `StatusReceiver`'s `SpeedMultiplier` makes the 4D effect testable in SpacetimeLab. |

**Tuning** (duration 3 s, cost 3 s, immunity 6 s) lives on the spell's asset, like the Thunderbolt's
`staggerSeconds`. Not on `SpacetimeSettings.asset`, which has uncommitted local changes.

## 7. Tests (EditMode)

- `StationaryPointRule`: refuses below 3 s of meter; spends exactly 3 s; immunity runs 6 s from
  the stun's end, not its start; immunity is per target.
- `SlowLedger`: a 0 entry over a 0.3 entry reads 0, then 0.3 once the 0 expires.
- `WorldlineHistory.Project`: a still object sampled for 3 s projects to a vertical segment 1.5 m
  long with the default time axis.

## 8. Open questions

1. Is the 1.5 m wall in a stunned platform's beam a puzzle tool or a nuisance? Playtest in
   SpacetimeLab.
2. Fluxion Beam mid-fire: freeze it (recommended) or cancel it?
3. Is 3 s on Newton too strong once sword damage is measured? Fallback: a per-target cap.
4. Should apples be stunnable? A frozen apple would hang in the air for 3 s and leave a post in its
   fall. Apples move themselves and carry no `StatusReceiver`, so this is extra work.
5. Could Newton turn it on the player in phase 2? Out of scope for a first version.
