using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Lives on the player and decides who the talk prompt is for; a press of
    /// Interact (<see cref="InteractInput"/>: E, or RB on a pad) then starts that
    /// conversation through the <see cref="DialogueDirector"/>.
    ///
    /// <b>Targeting.</b> Each frame outside a conversation it copies the enabled
    /// Talkables from the <see cref="TalkableSet"/> (plus the lock-on target's
    /// Talkable, which may not be registered) into plain candidates and lets
    /// <see cref="TalkTargetSelector"/> choose: the lock-on target if it can
    /// talk and is close enough, nobody while locked on to something that cannot
    /// talk (Newton), otherwise the nearest one in front. No scene search and no
    /// physics query: the set holds a handful of entries. A candidate whose
    /// prompt would be off screen (<see cref="TalkTargetSelector.IsInView"/>,
    /// the same test the prompt view draws by) is not a target, so E never talks
    /// to someone whose prompt the player cannot see.
    ///
    /// <b>Only when free</b> (<see cref="TalkEligibility"/>, with
    /// <see cref="DialogueConfig.RequireFree"/>): no prompt while airborne (past a
    /// short grace), dashing, tethered or on the skateboard, swinging the sword
    /// or in 4D; and a press starts a talk only while really grounded, never in
    /// the grace, because time stops for the talk.
    ///
    /// <b>Interact is both Talk and Dash.</b> While it has a target, and for the
    /// whole conversation and its restart cooldown, it claims
    /// <see cref="InputBlock.InteractButton"/> on the <see cref="PlayerInputGate"/>,
    /// and the movement input drops the dash while that is claimed. It runs
    /// before the motor (<c>DefaultExecutionOrder(-50)</c>), so the claim is
    /// already current when the motor reads input: a press on the frame a prompt
    /// appears talks and never dashes, and a press on the frame a talk closes
    /// neither dashes nor reopens it.
    ///
    /// Presentation reads <see cref="CurrentTarget"/> and listens to
    /// <see cref="TargetChanged"/>; this component draws nothing.
    /// <see cref="Tick"/> is public so tests can drive it with explicit input.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Ranges, cone and eligibility rules. Set by Setup Dialogue.")]
        [SerializeField] private DialogueConfig config;
        [Tooltip("Runtime set the NPCs register in. Must be the same asset their Talkables use.")]
        [SerializeField] private TalkableSet talkables;
        [Tooltip("Starts and runs the conversation.")]
        [SerializeField] private DialogueDirector director;
        [Tooltip("The player's input gate, where the Interact claim lives. Empty: E talks AND " +
                 "dashes when a prompt is up.")]
        [SerializeField] private PlayerInputGate inputGate;
        [Tooltip("The player's Interact button (E / RB). Empty: nothing starts a talk.")]
        [SerializeField] private InteractInput interactInput;
        [Tooltip("The camera the prompt is drawn through. A target whose prompt would be off " +
                 "its screen is not offered. Empty: no on-screen test.")]
        [SerializeField] private Camera viewCamera;

        [Header("Eligibility (all optional)")]
        [Tooltip("Its locked target wins when it has a Talkable on it, a parent or a child; " +
                 "a locked target without one turns talking off.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("Read for grounded and busy (dash, tether, skate).")]
        [SerializeField] private ChibiLocomotion locomotion;
        [Tooltip("Read for a sword swing in progress.")]
        [SerializeField] private MeleeCombat melee;
        [Tooltip("Read for being in 4D.")]
        [SerializeField] private SpacetimeView spacetime;

        List<TalkCandidate> _candidates = new(8);
        List<Talkable> _candidateTalkables = new(8);
        InteractInput _listening;
        bool _talkPressed;
        float _airborneFor;
        Health _lastLockTarget;
        Talkable _lastLockTalkable;

        /// <summary>The character the prompt is for, or null.</summary>
        public Talkable CurrentTarget { get; private set; }

        /// <summary>Raised when <see cref="CurrentTarget"/> changes; the argument may be null.</summary>
        public event Action<Talkable> TargetChanged;

        void OnEnable()
        {
            _listening = interactInput;
            if (_listening != null) _listening.Pressed += OnInteractPressed;
        }

        void OnDisable()
        {
            if (_listening != null) _listening.Pressed -= OnInteractPressed;
            _listening = null;
            _talkPressed = false;
            SetTarget(null);
            if (inputGate != null) inputGate.Release(this);
        }

        // Latched here, consumed every Tick.
        void OnInteractPressed() => _talkPressed = true;

        void Update()
        {
            bool pressed = _talkPressed;
            _talkPressed = false;
            Tick(Time.unscaledDeltaTime, pressed);
        }

        /// <summary>
        /// One frame: pick the target, keep the Interact claim in step, and start
        /// a conversation when <paramref name="talkPressed"/>. A press with
        /// nobody to talk to, or while not free to start, is dropped, not
        /// remembered for later.
        /// </summary>
        public void Tick(float unscaledDeltaTime, bool talkPressed)
        {
            UpdateGrounding(unscaledDeltaTime);
            SetTarget(MaySearch() ? FindTarget() : null);

            if (talkPressed && CurrentTarget != null && MayStartNow()
                && director.TryStart(CurrentTarget, transform))
                SetTarget(null);

            UpdateClaim();
        }

        bool MaySearch()
        {
            if (config == null || director == null || !director.CanStartConversation) return false;
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Movement)) return false;
            return TalkEligibility.MayOffer(config.RequireFree, Grounded, _airborneFor,
                                            config.GroundedGraceSeconds, Busy, Attacking, In4D);
        }

        bool MayStartNow() => TalkEligibility.MayStart(config.RequireFree, Grounded, Busy, Attacking, In4D);

        bool Grounded => locomotion == null || locomotion.IsGrounded;
        bool Busy => locomotion != null && locomotion.IsBusy;
        bool Attacking => melee != null && melee.IsAttacking;
        bool In4D => spacetime != null && spacetime.In4D;

        void UpdateGrounding(float dt)
        {
            if (Grounded) _airborneFor = 0f;
            else _airborneFor += dt > 0f ? dt : 0f;
        }

        /// <summary>Held while there is a prompt, and for the whole conversation and
        /// its cooldown, so the press that opens or closes a talk never dashes.
        /// Re-asserted every frame, so a gate that dropped its claims (a reload)
        /// gets it back.</summary>
        void UpdateClaim()
        {
            if (inputGate == null) return;
            bool hold = CurrentTarget != null
                        || (director != null && (director.IsBusy || director.IsCoolingDown));
            var wanted = hold ? InputBlock.InteractButton : InputBlock.None;
            if (inputGate.ClaimOf(this) != wanted) inputGate.Claim(this, wanted);
        }

        Talkable FindTarget()
        {
            _candidates ??= new List<TalkCandidate>(8);
            _candidateTalkables ??= new List<Talkable>(8);
            _candidates.Clear();
            _candidateTalkables.Clear();

            bool lockedOnAnything = lockOn != null && lockOn.IsLocked;
            Talkable locked = lockedOnAnything ? LockedTalkable() : null;
            int lockedIndex = -1, currentIndex = -1;

            if (talkables != null)
            {
                for (int i = 0; i < talkables.Count; i++)
                {
                    var t = talkables[i];
                    if (t == null) continue;
                    if (t == locked) lockedIndex = _candidates.Count;
                    if (t == CurrentTarget) currentIndex = _candidates.Count;
                    Add(t);
                }
            }
            if (locked != null && lockedIndex < 0)
            {
                lockedIndex = _candidates.Count;
                if (locked == CurrentTarget) currentIndex = lockedIndex;
                Add(locked);
            }
            if (_candidates.Count == 0) return null;

            Vector3 origin = transform.position + Vector3.up * config.PlayerFocusHeight;
            int best = TalkTargetSelector.Select(origin, transform.forward, _candidates, lockedIndex,
                                                 lockedOnAnything && locked == null, currentIndex,
                                                 config.SelectionRules);
            return best >= 0 ? _candidateTalkables[best] : null;
        }

        void Add(Talkable t)
        {
            Vector3 focus = t.FocusPosition;
            _candidates.Add(new TalkCandidate(focus, t.CanTalk && PromptInView(focus)));
            _candidateTalkables.Add(t);
        }

        bool PromptInView(Vector3 focus)
        {
            if (viewCamera == null) return true;
            Vector3 anchor = focus + Vector3.up * config.PromptHeightOffset;
            return TalkTargetSelector.IsInView(viewCamera.WorldToViewportPoint(anchor));
        }

        /// <summary>The Talkable on, above or below the lock-on target. Looked
        /// up only when the target changes.</summary>
        Talkable LockedTalkable()
        {
            Health target = lockOn.Target;
            if (target != _lastLockTarget)
            {
                _lastLockTarget = target;
                _lastLockTalkable = null;
                if (target != null)
                {
                    _lastLockTalkable = target.GetComponentInParent<Talkable>();
                    if (_lastLockTalkable == null) _lastLockTalkable = target.GetComponentInChildren<Talkable>();
                }
            }
            return _lastLockTalkable;
        }

        void SetTarget(Talkable target)
        {
            // By reference: a destroyed target compares equal to null under
            // Unity's ==, and the prompt still needs to hear that it went.
            if (ReferenceEquals(target, CurrentTarget)) return;
            CurrentTarget = target;
            TargetChanged?.Invoke(target);
        }
    }
}
