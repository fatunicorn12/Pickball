# Pickleball core: decisions and current state

Snapshot as of `a930952`. Code: `Scripts/{Ball,Court,Player,AI,Input,GameState,Events,Core}`,
plus `UI/ScoreView.cs`. Setup is described in `CLAUDE.md`. There is no `Docs/Systems` doc for
this game. None of this code has changed since the initial scaffold except the move into the
`Pickleball.Runtime` assembly (commit `d489f33`).

## Current state: the game isn't playable from the repo

- **`GameScene.unity` is a stub.** It has no camera, light, AI, `GameStateManager`, `HitZone` or
  UI. "Court" is a bare transform, and the ball's and player's references are all unassigned.
  `GameStateManager`, `AIController`, `HitZone`, `Court` and `ScoreView` appear in **no** scene or
  prefab. The scene is still build index 0 in `EditorBuildSettings.asset`.
- **The five event-channel assets don't load** (verified in the Editor: they come back null). See
  [Communication.md](Communication.md#pickleball-event-channels-current-state).
- `CLAUDE.md`'s "Build the scene (manual, inspector-wired)" steps are the only way to get a
  playable scene. That conflicts with `CodingStandards.md` §4, which says setup scripts are the
  source of truth for wiring. `PickleballSceneBuilder` deliberately creates assets only, never the
  scene.

## Rules as implemented

**Side-out scoring.** Only the server scores. If the receiver wins the rally, the serve changes
sides and no point is awarded. There is no second-server rule, no even/odd serve position and no
diagonal serve target. After `GameOver`, a new game starts automatically with `firstServer`.

**Who loses the rally** is worked out from bounce-channel bookkeeping in `GameStateManager`:
- A net contact → the last hitter loses.
- Out of bounds with no bounce since the last hit → the hitter loses.
- Out of bounds after a bounce → the side where the first bounce landed loses.
- A second bounce → the side it died on loses.

*Not enforced:* the two-bounce rule, kitchen/volley faults, and checking that the first bounce
landed on the opponent's side. The default case blames the side of the second bounce. The
**Kitchen** zone is resolved by `Court.ResolveZone` but never read.

**Win condition.** `HasWon` is first to `pointsToWin` (11) and ahead by `winBy` (2), both from
`GameConfig`.

## Physics decisions

**The bounce is manual** (`BallController.OnCollisionEnter`), not PhysX restitution. PhysX ignores
restitution below `bounceThreshold` (2 m/s), so slow floaty balls never bounced. Ball colliders
use a physics material with restitution 0 and `Maximum` combine. A bouncy material on any surface
would therefore add a PhysX bounce on top of the manual one.

**Custom gravity.** Gravity is lighter near the apex, below `|v.y| < threshold`, which gives the
floaty arc. The same model is copied into `BallController.PredictLanding`, which the AI uses. Keep
the two in step, or better, extract one static flight model both call.

**Net detection is by contact normal, not collider identity.** Any contact whose normal isn't
mostly up counts as a net fault. That is why the player's and AI's `Body` capsule colliders must
be deleted (CLAUDE.md setup step 3). The net's top face points up, so a net-cord touch currently
registers as a Kitchen *floor* bounce.

**Zones are geometric.** `Court` answers `IsInBounds`, `SideOf` and `ResolveZone` from world
coordinates and its serialized dimensions. The zone **tags** that `Create Assets` registers are
vestigial: nothing reads them. Zone queries ignore the court's transform, so moving or rotating
the Court object breaks them. `Court.Build` is skipped when the object already has children; that
supports the editor preview, but any child suppresses the whole build. The ground collider is kept
on purpose so out-of-bounds landings still register.

**Hit detection is a bounds check** (`HitZone.Contains` against the BoxCollider's world bounds),
so a freshly parked serve ball is always hittable. It uses an AABB, so a rotated zone accepts
points outside the oriented box. It is empty while the collider is disabled.

## AI decisions

- **The miss is rolled once per incoming shot** and played out as a deliberate whiff.
- **The AI moves in X and Z**, contrary to the "Fixed Z" class comment. The `ballOnOurSide` guard
  stops it volleying across the net.
- **Difficulty is a single 0–1 float** that lerps miss chance, accuracy offset and reaction delay
  toward hard-coded endpoints (0.02 / 0.1 / 0.05 s) in `AIConfig`. Those endpoints are magic
  numbers, against CodingStandards §2.
- *Known issues:*
  - The aim error is re-rolled every loop iteration, so the noise averages out.
  - The AI ignores incoming balls while walking back to its home position.

## Input

`InputReader` builds `<Pointer>` bindings in code; there is no `.inputactions` asset. Gestures are
classified on release, with screen-relative thresholds: a tap under the travel threshold, else a
swipe giving aim (x) and power (speed). Down-swipes and UI taps also swing.

## The per-frame model

`BallController.FixedUpdate` is the only `FixedUpdate` in the project. The AI also polls every
frame, through `UniTask.Yield` loops in `AIController`. The "only per-frame loop" line in
`CLAUDE.md` is true of the pickleball core's MonoBehaviour callbacks, not of the project. The
Sandbox runs its simulation in `Update`/`LateUpdate`.

## Tuning notes

`BallConfig.asset` predates `bounceHorizontalKeep` and `minBounceUpSpeed`, so the code defaults
apply for those fields. Its `bounciness` is 0.7, while the code default is 0.75. At full aim,
shots drift about 4 m sideways during roughly 1.7 s of flight against a 3.05 m half-width, so any
|aim| above about 0.76 lands wide. That was calculated by hand; a test should confirm it.
