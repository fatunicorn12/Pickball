# Inventory and weapon switching

Owner: the inventory agent (`feat/inventory`). Code: `Scripts/Sandbox/Inventory/`,
`Scripts/UI/Inventory*.cs`, `Scripts/Editor/Inventory*.cs`, `MeleeCombat.Equip`. Data:
`ScriptableObjects/Inventory/`. Tests: `Tests/EditMode/Inventory/`.

## 1. Purpose and player-facing feel

The player carries three kinds of thing, each on its own tab of one menu:

- **Weapons**: the swords. One is in the hand. The compass and the ruler sword ship; both are owned
  from the start in the sandbox, with the compass equipped.
- **Magic**: the spells on the spell selector (Arcane Orb, Fireball, Thunderbolt, Freeze Zone, Heal).
  Choosing one here selects it, exactly as its number key does.
- **Key Items**: the abilities found in the world: Dash, Double Jump, the **Pendulum** (the tether),
  Gravity Anchor and Skateboard. Each shows up the moment its ability is unlocked, whoever unlocked it.

**The menu** opens over a dimmed, paused game: three tabs across the top, the tab's slot grid on the
left, and the highlighted item's icon, name, status line ("Equipped", "Use to select · 25 mana") and
description on the right. The equipped weapon and the selected spell carry a gold dot. The look is
the spell selector's: the same slot wells and frames, Oswald with the HUD outline, dark rounded plates.

**Switching weapons** is either a menu pick or the quick-cycle button outside the menu. A switch never
cuts a swing short: asked for mid-swing (or during a hit's hitstop), it lands the moment the swing
ends. Switching starts the combo afresh. A small readout in the bottom-right corner names the weapon in
hand; it pops and brightens when a new weapon arrives, then settles to 65 % opacity.

### Controls

Every existing binding was audited first (`AddBinding` and polled keys / buttons across `Scripts/`).
On a pad, all four face buttons, both shoulders, both triggers, both stick presses and all four d-pad
directions were taken; only **Start** and **Select** were free.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Open / close the menu | **I** | **Start** |
| Next weapon (outside the menu) | **X** | **Select** (View / Back) |
| *In the menu:* move the highlight | WASD / arrows, or hover the mouse | D-pad / left stick (held repeats) |
| *In the menu:* previous / next tab | Q / E, or click a tab | LB / RB |
| *In the menu:* use the highlighted item | Enter / Space, or click it | A (South) |
| *In the menu:* close | Escape, or I again | B (East), or Start again |

The in-menu keys reuse gameplay keys on purpose. The menu claims every `InputBlock` flag while it is
open, so no gameplay reader acts on them. Quick-cycle is ignored while `InputBlock.Sword` is blocked:
while aiming a spell, in a conversation, and with the menu open.

**These bindings are new and need the user's confirmation.**

## 2. Architecture

```
InventoryInput ──(latched presses)──▶ InventoryMenu ──▶ Inventory ──▶ InventoryModel (plain C#)
       │                                  │  pause, gate, camera     │  ▲ mirrors
       └──(X / Select)──────────────────▶ Inventory.Update           │  ├─ PlayerAbilities (key items)
                                                                     │  └─ SpellSelector (magic)
                                                     EquippedChanged ▼
                                                              WeaponEquipper ──▶ HandGrip, BladeBase/Tip,
                                                                     │            WeaponClearance, MeleeCombat.Equip
                                                       WeaponEquipped ▼
                                             InventoryEquippedView, InventoryMenuView (presentation)
```

### Components (on the player root)

| Component | Job | Kind |
|---|---|---|
| `InventoryInput` | Builds the menu, cycle, tab, confirm, back and navigate actions; latches presses from `performed`, clears them in `LateUpdate`. | input |
| `Inventory` | Wraps `InventoryModel`; seeds starting items; mirrors key items from `PlayerAbilities` and magic from `SpellSelector`; quick-cycle. | gameplay |
| `WeaponEquipper` | Applies the equipped weapon to the hand and the sword, never mid-swing. | gameplay |
| `InventoryMenu` | Menu state (open, tab, slot), pause, input claim, camera hand-over, using an item. | gameplay (UI state) |
| `InventoryMenuView` | Draws the menu; mouse hover and click. On `InventoryHUD/InventoryMenu`. | presentation |
| `InventoryEquippedView` | The equipped-weapon readout. On `InventoryHUD/EquippedWeapon`. | presentation |
| `ItemPickup` | Optional world pickup that adds any item (a found ruler, a scroll). Not placed anywhere yet. | gameplay |

### Pure logic (EditMode-tested)

| Type | Rule |
|---|---|
| `InventoryModel` | Ordered slots per tab; capacity per tab (`Full` refuses); one copy per item (same asset or same id); equipped weapon always owned or null; first weapon added is equipped; removing the equipped weapon equips the one that slides into its slot, else the new last one; `CycleWeapon` wraps. |
| `PendingEquip` | A switch while busy waits; latest request wins; requesting the weapon in hand cancels. |
| `KeyItemNames` | `AbilityKind` → id, name, blurb. Tether is the Pendulum (`pendulum`). |
| `InventoryGrid` | Grid navigation: left/right stop at row ends, up/down by a row, down into a short last row lands on its last slot. |
| `DirectionRepeat` | Step on push, again after `RepeatDelay`, then every `RepeatInterval`; stronger stick axis wins. |

### Events

| Raiser | Event | Listeners |
|---|---|---|
| `InventoryModel` | `Changed(category)`, `EquippedChanged(weapon)` | `Inventory` (forwards both) |
| `Inventory` | `Changed(category)`, `EquippedChanged(weapon)` (the request) | `WeaponEquipper` (`EquippedChanged`), `InventoryMenuView` |
| `WeaponEquipper` | `WeaponEquipped(weapon)` (the swap that happened) | `InventoryEquippedView`, `InventoryMenuView` |
| `InventoryMenu` | `Opened`, `Closed`, `HighlightChanged`, `ItemUsed(item, changed)` | `InventoryMenuView` |
| `PlayerAbilities` | `Unlocked(kind)` | `Inventory` (adds the key item) |
| `SpellSelector` | `SelectionChanged` | `Inventory` (re-syncs the Magic tab) |

### One authority per tab

- **Weapons** are owned by `Inventory`.
- **Key items** mirror `PlayerAbilities`, which stays the source of truth. Adding a key item grants its
  ability; removing one revokes it. `PlayerAbilities.Revoke` raises no event, so the tab is re-checked
  whenever the menu opens (`Inventory.Resync`).
- **Magic** mirrors `SpellSelector`'s slots in slot order. Using a magic item calls
  `SpellSelector.Select`.
- A spell or ability with no asset in the catalog still shows, under a runtime item named after the
  spell or from `KeyItemNames`.

### The weapon swap (`WeaponEquipper`)

In this order, on the first frame the sword is not busy (`MeleeCombat.IsAttacking` or `IsInHitstop`):

1. The weapon's `HeldPrefab` is instantiated once, under the inactive `StowedWeapons` child of the
   player, colliders and rigidbodies stripped. Later swaps reuse it.
2. `HandGrip.Release(StowedWeapons)` stows the old item; `HandGrip.Hold(new)` seats the new one by its
   grip anchor.
3. The hand's own `WeaponSocket/BladeBase` and `BladeTip` are moved onto the held item's `BladeBase` /
   `BladeTip` markers. They are moved, never replaced, because `MeleeCombat`, `BladeTrail`,
   `WeaponClearance` and `SwordEffects` hold references to them. They stay children of the socket, so
   they follow the hand.
4. `WeaponClearance.SetProfile(weapon.ClearanceProfile)` (an empty profile keeps the current one).
5. `MeleeCombat.Equip(weapon.Moveset, weapon.BladeRadius)`. Equip ends a running chain if one is
   somehow running (`ChainEnded` is raised), resets `ComboProgress` (ground and air), cancels the chain
   grace, and drops queued and buffered presses. A null moveset keeps the current one. Hits and bursts
   in flight are untouched; the Hypatia orbiter batting path (`TryResolveTarget` → `IDamageable`) is
   unchanged.
6. `WeaponEquipped` is raised.

**The compass already in `Chibi.prefab`.** The prefab holds the compass as `WeaponSocket/WeaponVisual`
(a `HeldItem`). It is the equipper's `fallbackItem`. A weapon without a held prefab (Held_Compass and
Held_Ruler before they land) shows the fallback, and the blade markers go back to the pose the weapon
setup fitted to it (captured in `Awake`). Once Weapon_Compass points at Held_Compass, Setup Inventory
hides `WeaponVisual`, and the equipper stows it like any other item.

### Pause

The menu stops time with `Time.timeScale = 0`, through dialogue's `TimeScalePause`. That class records
and restores the previous value and refuses to hold when time is already stopped. It is the same
mechanism the dialogue director uses, so everything in the project already copes with it:

- **Freezes** (scaled time): movement, the swing timeline and hitstop countdown (`MeleeCombat` uses
  `Time.deltaTime`), animators, `SpringCloth` (`LateUpdate` with `Time.deltaTime`), `UniTask.Delay`
  with the default delay type, boss and spell timers.
- **Keeps running** (unscaled): the Input System, the menu and its view, the HUD fades, and
  `UniTask.Yield` loops. Those loops still poll, but with `deltaTime` 0 they advance nothing.
- **The orbit camera** reads mouse look and the re-capture click without time. The menu disables it
  while open, as `DialogueCamera` does, then frees the cursor. On close it re-enables the camera and
  re-captures the cursor if the camera still believes it owns it.
- **Refusals:** the menu will not open while something blocks `InputBlock.Movement` (a conversation),
  or while time is already stopped. A conversation can't start while it is open, because
  `DialogueDirector.TryStart` refuses at time scale 0.
- **Audio** is not paused (`pauseAudio: false`).
- **Teardown:** `OnDisable` closes the menu, so a script reload in Play mode never leaves time stopped
  or input claimed.

### Data assets (`ScriptableObjects/Inventory/`)

| Asset | What | Policy |
|---|---|---|
| `InventoryConfig` | Slots per tab (8 weapons, 10 magic, 10 key items), grid columns (5), menu repeat delay / interval and stick threshold. | Seeded once. |
| `Weapons/Weapon_Compass` | `Held_Compass` prefab, `Moveset_Compass`, radius **0.18**, `ChibiWeaponClearance`. | Seeded once; blank references filled by path every run. |
| `Weapons/Weapon_Ruler` | `Held_Ruler` prefab, `Moveset_Ruler`, radius **0.25**, `RulerWeaponClearance`. | Same. |
| `KeyItems/Key_<AbilityKind>` | One per ability, named from `KeyItemNames`. | Seeded once. |
| `Magic/Magic_<SpellAsset>` | One per `SpellDefinition` in `ScriptableObjects/Magic/`; icon copied from the spell while blank. | Seeded once. |

Why the radii: 0.18 is what `MeleeCombat` has always used for the compass. The ruler blade is 20 cm
wide and 6–8 cm thick, so half its width plus a little forgiveness gives 0.25. That is wider than the
compass's, so the heavy blade lands what it visibly sweeps through.

A reference whose asset does not exist yet is left null with a warning. Re-running Setup Inventory
after it lands fills it in. With no held prefab, the weapon shows the fallback compass. With no
moveset, it swings the current moveset at its own radius.

### Setup (`Tools ▸ Pickleball ▸ Setup Inventory`)

`InventorySetup.AttachToPlayer(GameObject)` does the whole job, and it is also the entry point for
other chains:

1. Seed the data assets.
2. Add and wire `InventoryInput`, `Inventory`, `WeaponEquipper` and `InventoryMenu` (plus
   `PlayerInputGate` if missing) and the `StowedWeapons` child. It finds `HandGrip`, `BladeBase` /
   `BladeTip`, `WeaponClearance` and `WeaponVisual` under the player, and the `SandboxOrbitCamera` by
   type.
3. Rebuild the `InventoryHUD` canvas (`InventoryHudBuilder`). It is its own overlay canvas at sort
   order 40, above FightHUD.

Starting items, the starting weapon and `InventoryConfig` are filled only when blank. The catalogs and
the component references are rewritten every run. The setup marks the scene dirty and never saves. The
components are scene overrides, not part of `Chibi.prefab`.

It runs at the end of Setup Chibi Sandbox and Build Training Stage. Run it by hand after **Setup
Weapon**, which rebuilds the socket and markers it points at, and after Held_Compass, Held_Ruler,
Moveset_Ruler or RulerWeaponClearance land. See `Docs/Architecture/EditorSetup.md` for the run order.

## 3. How to tune

| What | Where |
|---|---|
| Slots per tab, grid columns | `InventoryConfig` → Capacity, Columns. Re-run Setup Inventory after raising capacity past the largest tab: the grid is built with that many slots. |
| Menu scroll feel | `InventoryConfig` → Repeat Delay (0.35 s), Repeat Interval (0.12 s), Stick Threshold (0.55). |
| Hit forgiveness of a weapon | `Weapon_*` → Blade Radius (m). |
| A weapon's combo | `Weapon_*` → Moveset. Tune the steps themselves on the moveset's step assets (SwordAbilities.md). |
| Menu colours, highlight scale, fade | `InventoryMenuView` on `InventoryHUD/InventoryMenu`. These are rebuilt by the setup, so change the defaults in code. |
| Readout opacity and hold | `InventoryEquippedView` (Idle Alpha 0.65, Hold 1.6 s, Pop 0.2). |
| Bindings | `InventoryInput` → Menu Bindings, Cycle Bindings (Input System paths). The in-menu keys are in code. |
| Names and blurbs | The item assets (Identity). Key item seeds are in `KeyItemNames`, weapon and spell seeds in `InventorySetup`. |

## 4. How to extend

**A new weapon** (three steps):
1. Build its held prefab with a `HeldItem` (grip anchor plus `BladeBase` / `BladeTip` markers;
   Hands.md §4) and its moveset (SwordAbilities.md §4).
2. Create a `WeaponDefinition` (Create ▸ Pickleball ▸ Inventory ▸ Weapon) under
   `ScriptableObjects/Inventory/Weapons/`: identity, held prefab, moveset, blade radius, clearance
   profile. To seed it from code, add a `WeaponSeed` row to `InventorySetup.Weapons`.
3. Give it to the player: add it to `Inventory.startingItems`, or drop an `ItemPickup` with it in the
   world.

**A new key item:** add the `AbilityKind` (PlayerAbilities.cs) and a row in `KeyItemNames.For` and
`Order`, then re-run Setup Inventory. A new ability shows even without that row, under its enum name.

**A new spell:** add it with Setup Magic. Re-running Setup Inventory gives it a `Magic_*` entry; until
then it shows under its spell name.

**A new tab:** add a value to `InventoryCategory`, a capacity field to `InventoryCapacity`, a tab name
to `InventoryMenuView.TabNames` and `InventoryHudBuilder`, and a use rule in `InventoryMenu.UseSlot`.

## 5. Known limitations and open questions

- **Bindings need confirming** (I / Start, X / Select).
- **A picked-up magic item can't be cast.** `SpellSelector`'s slots are serialized data with no add
  API, so an `ItemPickup` with a `MagicItemDefinition` lands in the tab, but using it does nothing
  until the selector carries the spell. The next `SelectionChanged` re-sync drops it from the tab.
  Needs an additive `SpellSelector.Add(SpellDefinition)` hook (Magic owner).
- **`PlayerAbilities.Revoke` raises nothing.** A revoked ability (Shift+Backspace in the training stage)
  stays in the Key Items tab until the menu next opens. An additive `Revoked` event on
  `PlayerAbilities` would make it immediate.
- **Stowed weapons are not drawn.** There is no sheath or back slot yet.
- **No save.** Ownership resets each run. The ids on every item exist so a save can refer to them later.
- **Quick-cycle goes forward only.** There is no previous-weapon button; with two weapons it makes no
  difference.
- **The menu doesn't pause audio.** That is deliberate, as in dialogue, but it can be switched on in
  `InventoryMenu.Open` (`pauseAudio`).
- **Gamepad Select on some pads** is labelled View or Back. It is `<Gamepad>/select` either way.

## For other docs (to fold in after merge; not this branch's files)

- **`Docs/Architecture/Communication.md`, cross-system events:** add rows for
  `Inventory.EquippedChanged` → `WeaponEquipper`; `WeaponEquipper.WeaponEquipped` →
  `InventoryEquippedView`, `InventoryMenuView`; `PlayerAbilities.Unlocked` → also `Inventory`;
  `SpellSelector.SelectionChanged` → also `Inventory`. Add `TimeScalePause` to "A ScriptableObject
  as a runtime channel"'s neighbour notes: it now has two holders (dialogue, the inventory menu), and
  each refuses to hold while the other has time stopped.
- **`Docs/Architecture/CombatMovementBoundary.md`, binding table:** I / Start (menu), X / Select
  (next weapon); in-menu reuse of WASD, Q/E, Space, Enter, Escape, the d-pad, LB/RB, A/B under an
  `InputBlock.All` claim.
- **`Docs/Systems/SwordAbilities.md` §2 (MeleeCombat):** `MeleeCombat.Equip(SwordMoveset, float)`
  and `BladeRadius` exist; the moveset and radius are no longer fixed for a session.
- **`DialogueDirector` / `TimeScalePause` comments** still say dialogue is the only writer of
  `Time.timeScale`; the inventory menu now writes it too, through the same class.
