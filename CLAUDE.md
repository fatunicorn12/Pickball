# Pickball — Mobile Pickleball POC (Unity 6 / URP)

Wii-Sports-Tennis-style arcade pickleball. Low-poly primitives, floaty exaggerated ball
physics, fixed camera behind the player. Targets Android/iOS. Proof of concept.

## Architecture

Modular components wired together in the scene via the Inspector. Systems never reference each
other directly — they communicate through `ScriptableObject` **event channel** assets
(`Scripts/Events/EventChannels.cs`). All tunables live on `BallConfig` / `AIConfig` / `GameConfig`
assets. UniTask only (no coroutines). The only per-frame loop is `BallController.FixedUpdate`
(manual gravity); everything else is event- or UniTask-driven.

**Coordinate convention:** X = court width, Z = court length (player baseline at −Z, AI at +Z,
net at Z=0), Y = up (surface top at Y=0).

**Ball bounce is manual** (in `BallController.OnCollisionEnter`), not PhysX restitution — PhysX
ignores restitution below `Physics.bounceThreshold` (~2 m/s), so slow floaty balls never bounced.
Tune via `BallConfig.bounciness` / `bounceHorizontalKeep` / `minBounceUpSpeed`.

**Hit detection is a bounds check** (`HitZone.Contains`), not a physics trigger, so a freshly
parked serve ball is always hittable.

## Layout

```
Assets/_Project/Scripts/
  Core/    PrimitiveFactory, Palette
  Events/  PickleballTypes (enums/structs), EventChannels
  Court/   Court (procedural build + zone queries; Editor-preview context menu)
  Ball/    BallController, BallConfig
  Player/  PlayerController, HitZone
  Input/   InputReader (new Input System, tap/swipe; no Update loop)
  AI/      AIController, AIConfig
  GameState/ GameStateManager, GameConfig
  UI/      ScoreView (TextMeshPro)
  Editor/  PickleballSceneBuilder → Tools ▸ Pickleball ▸ Create Assets
```

## Event channel wiring (which asset goes where)

| Channel asset | Raised by | Listened to by |
|---|---|---|
| `BounceEvent` (BallContact) | BallController | GameStateManager |
| `BallDeadEvent` (BallContact) | BallController | GameStateManager |
| `BallHitEvent` (CourtSide) | PlayerController, AIController | AIController, GameStateManager |
| `GameStateEvent` (GameState) | GameStateManager | PlayerController, ScoreView |
| `ScoreEvent` (Score) | GameStateManager | ScoreView |

The **same asset instance** must be dragged into every component that shares it.

## Setup (do this once)

1. Open in Unity 6 (6000.3.5f2); let packages resolve. **UniTask installs from a git URL**, so git
   must be on your PATH.
2. **Tools ▸ Pickleball ▸ Create Assets** — makes the 3 configs + 5 channel assets under
   `Assets/_Project/ScriptableObjects/` and registers the zone tags.
3. If TMP text is invisible: **Window ▸ TextMeshPro ▸ Import TMP Essential Resources** (one-time).

## Build the scene (manual, inspector-wired)

Create these GameObjects and assign references in the Inspector (`PlayerBaselineZ ≈ −6.7`,
`AiBaselineZ ≈ +6.7`):

1. **Court** — empty at (0,0,0); add `Court`. (Right-click the component ▸ *Rebuild Court (Editor
   Preview)* to see it without Play.) Assign nothing.
2. **Ball** — Sphere; add `BallController` (auto-adds Rigidbody). Keep its SphereCollider.
   Assign: Config=`BallConfig`, Court=Court, BounceChannel=`BounceEvent`, BallDeadChannel=`BallDeadEvent`.
3. **Player** — empty at (0,0,−6.1); add `PlayerController` + `InputReader`.
   - child **Body**: Capsule, *delete its CapsuleCollider*, pos (0,0.8,0), scale ~(0.7,0.8,0.7).
   - child **HitZone**: empty; add `HitZone`; set its BoxCollider size ~(6,3,6.2), local pos (0,1,2.5).
   - child **PlayerServeAnchor**: empty at world (0,0.95,−5.1) (must sit inside the HitZone box).
   - PlayerController refs: BallConfig, Ball, HitZone, Input(=this InputReader),
     BallHitChannel=`BallHitEvent`, GameStateChannel=`GameStateEvent`.
4. **AI** — empty at (0,0,6.1); add `AIController`.
   - child **Body**: Capsule, *delete its collider*.
   - child **HitZone**: `HitZone`, BoxCollider size ~(2.8,2.6,3.0), local pos (0,1,−0.3).
   - child **AIServeAnchor**: empty at world (0,0.95,5.1).
   - AIController refs: AIConfig, Court, Ball, HitZone, BallHitChannel=`BallHitEvent`.
5. **GameStateManager** — empty; add `GameStateManager`. Refs: Config=`GameConfig`, Ball, AI,
   PlayerServeAnchor, AIServeAnchor, GameStateChannel, ScoreChannel=`ScoreEvent`,
   BallHitChannel=`BallHitEvent`, BounceChannel=`BounceEvent`, BallDeadChannel=`BallDeadEvent`.
6. **UI** — Canvas (Screen Space – Overlay) + a *TextMeshPro – Text (UI)* anchored top-centre.
   Add `ScoreView` to the canvas; refs: ScoreText=the TMP text, ScoreChannel=`ScoreEvent`,
   GameStateChannel=`GameStateEvent`.
7. **Main Camera** — pos (0,6.5,−12.2), rotation (26,0,0), FOV 60, solid-colour sky background.

Press **Play**: tap to serve/swing, swipe for aim + power. First to 11, win by 2.

## Tuning notes

Ball feel is best dialled in live in Play mode on `BallConfig`. Defaults are conservative so shots
stay inside the 13.4 m court; raise `shotPowerRange`/`shotArcLift` for bigger arcs, raise
`bounciness` for a livelier bounce. AI difficulty is a single 0–1 float on `AIConfig`.
