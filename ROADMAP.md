# Roadmap — The Guardians of Mathematics

You start as a student who can barely dodge triangles. You end approaching the undefinable with
every tool mathematics ever built — and you defeat the void not with force, but with the most
elegant idea in calculus: that getting infinitely close is enough.

Each guardian is a boss fight. Each one drops an ability, and every ability is a tool you need
against the final boss.

**Status key:** ✅ done · 🟡 in progress · ⬜ not started

| # | Guardian | Era | Unlocks | Status |
|---|---|---|---|---|
| 1 | Pythagoras — The Angle Sage | Ancient Greece | Angle Dash | 🟡 dash exists (fixed 45°), no fight |
| 2 | Hypatia — The Astrolabe | Alexandria | Orbital Tether | 🟡 tether exists, no fight |
| 3 | Al-Khwarizmi — The Algebraist | Islamic Golden Age | Equation Swap | ⬜ |
| 4 | Fibonacci — The Spiral | Medieval Italy | Spiral Jump | ⬜ |
| 5 | Newton — The Gravity Sovereign | 17th c. England | Gravity Anchor | 🟡 fight playable, needs a play-test tuning pass |
| 6 | Euler — The Network | 18th c. Switzerland | Network Step | ⬜ |
| 7 | Galois — The Duelist | 19th c. France | Symmetry Break | ⬜ |
| 8 | Riemann — The Infinite Surveyor | 19th c. Germany | Limit Approach | ⬜ |
| ∞ | The Singularity (final boss) | — | — | ⬜ |

Cross-cutting systems:

- [x] Rolling HP bar (Bloodborne-style regain pool) — `Health`, `HealthBarView`
- [x] Ability unlock system — `PlayerAbilities`, `AbilityPickup`
- [ ] Boss dialogue / barks system (Newton has a first version — `NewtonBarks`)
- [ ] Item drops with flavour text (Bean, Book of Restoration, Galois's notes)
- [ ] Boss animation clips (Newton is fully procedural right now — `Animation/Newton/` is empty)

---

## 1. Pythagoras — The Angle Sage ⬜

Ancient Greece. Early game. Tutorial boss in spirit.

**Personality:** Stern but fair. Cult leader energy. Deeply uncomfortable when you mention
irrational numbers.

- [ ] **Pythagorean Volley** — three projectiles forming a right triangle. The safe spot is always
  the hypotenuse's midpoint.
- [ ] **Sacred Ratio** — geometric patterns projected across the arena that slowly rotate. Gaps
  only appear at specific angles.
- [ ] **Irrational Rage** (phase 2) — when weakened he panics and fires √2-spaced shots, slightly
  off his usual clean patterns.
- [ ] **Drop:** Bean item (restores HP). *"He didn't want you to have this."*
- [ ] **Unlocks: Angle Dash** — dash at precise 45° increments. *(A fixed 45° dash already
  exists in `ChibiLocomotion`; needs 8-way increments and to be gated behind this fight.)*

## 2. Hypatia — The Astrolabe ⬜

Alexandria. Early–mid game.

**Personality:** Elegant, sorrowful. Aware of her historical fate. The fight has an undercurrent
of tragedy.

- [ ] **Celestial Rings** — concentric circular waves from her position; the gaps rotate slowly.
- [ ] **Conic Sections** — parabolic, elliptical and hyperbolic projectiles, each curving
  differently.
- [ ] **Alexandria's Burning** (phase 2) — fire fills the arena from the edges inward, shrinking
  the safe space while her attacks continue.
- [ ] **Unlocks: Orbital Tether** — latch onto projectiles and swing around them. *(The tether
  exists in `TetherSystem`; it latches to fixed anchors, not projectiles yet.)*

## 3. Al-Khwarizmi — The Algebraist ⬜

Islamic Golden Age. Mid game.

**Personality:** Methodical, almost bureaucratic. Every attack is announced before it happens —
he is testing whether you can handle known information correctly.

- [ ] **Variable Storm** — X- and Y-labelled projectiles. Two of the same type in a row deals
  double damage; X then Y cancels them out safely.
- [ ] **Balance Equation** — splits the arena in two; the sides must be navigated symmetrically or
  a penalty triggers.
- [ ] **Algorithm** (phase 2) — a precise repeating attack sequence, identical every cycle.
  Punishes players who have not memorised it.
- [ ] **Drop:** *The Book of Restoration and Balancing* (key item).
- [ ] **Unlocks: Equation Swap** — swap position with your mirrored point in the arena.

## 4. Fibonacci — The Spiral ⬜

Medieval Italy. Mid game.

**Personality:** Whimsical, almost childlike. Obsessed with nature. His arena is overgrown.

- [ ] **Golden Spiral** — a massive spiral projectile expanding outward in Fibonacci proportions.
- [ ] **Rabbit Swarm** — small enemies spawned in Fibonacci quantities (1, 1, 2, 3, 5, 8…) that
  must be dealt with before they overwhelm you.
- [ ] **Nature's Pattern** (phase 2) — the arena rearranges into a sunflower spiral, moving every
  safe zone.
- [ ] **Unlocks: Spiral Jump** — a spiralling leap along a curved arc, reaching places a straight
  jump cannot.

## 5. Newton — The Gravity Sovereign 🟡

17th-century England. Mid–late game.

**Personality:** Arrogant, imperious. Has a thing about Leibniz. Will mention calculus was his
idea unprompted.

- [x] **Gravity Wells** — points dropped on the arena that pull your movement *and bend your
  dodges* (the dash curves toward wells). `GravityWell`
- [x] **Inverse Square** — a radial volley whose damage rises with 1/r² the closer you are to him,
  while his weak point *also* rewards being close (damage to him scales with 1/r² too). Forces the
  risk/reward call. `InverseSquareVolley`, `NewtonBoss.ScaleByRange`
- [x] **Apple Barrage** (phase 2, humorous) — the sky fills with apples. Absurd volume, very fast.
  `AppleBarrage`, `Apple`
- [x] **Fluxion Beam** — damage equal to the rate of change of your position: running costs,
  standing still is free. Permanent (non-recoverable) damage. `FluxionBeam`
- [x] **Personality barks** — arrogant one-liners, Leibniz grudges. `NewtonBarks`, `BarkView`
- [x] **Unlocks: Gravity Anchor** — dropped as a pickup when he dies. Press **G** to halt your own
  gravity (hover); hold **Jump** while anchored to invert it and rise.
- [ ] Play-test tuning pass (numbers are first-pass — see "Tuning" in `NewtonBoss`)
- [ ] Real animation clips (all tells are procedural today)

## 6. Euler — The Network ⬜

18th-century Switzerland. Late game.

**Personality:** Warm, generous, almost paternal. Went blind and kept doing mathematics. The fight
is about operating without full information.

- [ ] **Seven Bridges** — the arena is split into sections joined by toggleable bridges; cross
  without using any bridge twice (Königsberg).
- [ ] **Graph Storm** — projectiles along invisible network paths that only become visible once a
  projectile is already moving.
- [ ] **Blind Euler** (phase 2) — the arena goes partly dark. His attacks become more instinctual
  and harder to predict.
- [ ] **Unlocks: Network Step** — teleport between connected nodes in the environment.

## 7. Galois — The Duelist ⬜

19th-century France. Late game. Emotionally the heaviest fight.

**Personality:** Young, furious, romantic, doomed. He knows he is going to die. His pre-fight
dialogue is him writing his mathematical will.

- [ ] **Dawn timer** — a sun rises slowly across the arena; when it is fully up the fight ends
  regardless of outcome.
- [ ] **Group Theory Burst** — symmetric attack clusters where the symmetry is the safe zone.
- [ ] **Political Fire** — chaotic, emotional, hard-to-read attacks in phase 1.
- [ ] **Final Theorem** (phase 2, low timer) — desperately powerful as dawn approaches. Attacks
  speed up, the music changes.
- [ ] **Win condition:** survive until dawn; he acknowledges you as worthy before he leaves.
- [ ] **Drop:** his mathematical notes. *"Written the night before. The margins are full."*
- [ ] **Unlocks: Symmetry Break** — shatter symmetric patterns in the environment to open paths.

## 8. Riemann — The Infinite Surveyor ⬜

19th-century Germany. Late game. Direct predecessor to the final boss.

**Personality:** Quiet, almost meditative. His attacks feel like he is showing you something
rather than fighting you.

- [ ] **Integral Sweep** — area-under-curve waves; the safe zones require reading the curve.
- [ ] **Zeta Function** — projectiles at positions matching the Riemann zeta zeros. Feels almost
  solved but has one unpredictable element.
- [ ] **Hypothesis** (phase 2) — one attack per cycle is completely unpredictable. You can only
  react.
- [ ] **Post-fight:** he warns you directly about what is ahead. He has seen it and could not
  define it.
- [ ] **Unlocks: Limit Approach** — approach a point with precision without touching it.

---

## ∞. The Singularity — final boss ⬜

Working titles: ∅ (The Void), The Indeterminate, 0/0, The Divergent.

**Framing:** Every guardian was afraid of this. Pythagoras's cult was partly born from terror of
irrational numbers — √2 broke their worldview. The Singularity is what lives beyond the
expressible; what Cantor glimpsed when he stared into infinity. It is not evil. It is undefined,
and undefined things collapse everything around them.

- [ ] **Phase 1 — The Irrational:** patterns that seem almost regular but never repeat (π, √2).
  Each traversal ability from earlier guardians handles a different irrational attack type.
- [ ] **Phase 2 — The Infinite:** the arena expands; attacks come from directions that did not
  exist before. The boss splits into copies, each as strong as the original (Cantor). You *will*
  get hit — the rolling HP bar is the intended survival path. Weapons from earlier bosses collapse
  the copies.
- [ ] **Phase 3 — The Indeterminate (0/0):** the rules stop being consistent. A projectile that was
  lethal now heals, and vice versa.
- [ ] **Defeat mechanic — The Limit:** no final blow. The closer you get *without touching it*, the
  more damage the limit deals. Navigate phase 3's chaos and get infinitely close, repeatedly.
