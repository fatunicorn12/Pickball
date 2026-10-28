using System;
using Pickleball.Sandbox.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Runs the training stage: Smash-style blast zones, a respawn key, and a
    /// readout that measures every jump against the grid.
    ///
    /// <b>Blast zones.</b> Falling below <see cref="blastZoneY"/> or straying
    /// past <see cref="blastZoneRadius"/> puts the player back on the spawn
    /// with full health, so the void around the stage is safe to test over.
    ///
    /// <b>Readout.</b> Speed, height above the stage floor (y = 0), and the
    /// last and best jump: how high it rose above its take-off, how far it
    /// carried, and how long it was in the air. Everything is read off the
    /// CharacterController, so it measures whatever movement code is driving
    /// the player without knowing anything about it.
    ///
    /// Keyboard: Backspace respawns, Shift+Backspace also takes every ability
    /// away so the pickups can be tested from scratch.
    /// </summary>
    public class TrainingStage : MonoBehaviour
    {
        [SerializeField] private CharacterController player;
        [SerializeField] private Transform spawnPoint;

        [Header("Blast zones")]
        [SerializeField] private float blastZoneY = -25f;
        [Tooltip("Horizontal distance from the stage origin past which the player is respawned.")]
        [SerializeField] private float blastZoneRadius = 160f;

        [Header("Readout")]
        [SerializeField] private TMP_Text readout;
        [Tooltip("Seconds between readout refreshes while nothing new has landed.")]
        [SerializeField] private float refreshInterval = 0.1f;
        [Tooltip("Air time below this is a stumble off a step, not a jump, and is not recorded.")]
        [SerializeField] private float minAirTime = 0.2f;

        [Header("Combo readout")]
        [Tooltip("Sword to read the chain step from. Optional.")]
        [SerializeField] private MeleeCombat melee;
        [Tooltip("Dummies whose damage feeds the combo readout.")]
        [SerializeField] private Health[] dummies = Array.Empty<Health>();
        [SerializeField] private TMP_Text comboReadout;
        [Tooltip("Seconds after the last hit before the combo resets. Matches TrainingDummy.refillDelay.")]
        [SerializeField] private float comboTimeout = 2.5f;

        ComboTracker _combo;
        JumpSampler _jump;
        bool _comboDirty = true;

        bool _hasJump;
        float _lastRise, _lastCarry, _lastAirTime;
        float _bestRise, _bestCarry;

        float _nextRefresh;

        void Awake() => EnsureLogic();

        void EnsureLogic()
        {
            _combo ??= new ComboTracker(comboTimeout);
            _jump ??= new JumpSampler(minAirTime);
        }

        void OnEnable()
        {
            EnsureLogic();
            foreach (var d in dummies) if (d != null) d.Hurt += OnDummyHurt;
            if (melee != null) melee.SwingStarted += OnSwingStarted;
        }

        void OnDisable()
        {
            foreach (var d in dummies) if (d != null) d.Hurt -= OnDummyHurt;
            if (melee != null) melee.SwingStarted -= OnSwingStarted;
        }

        void OnDummyHurt(float amount, Vector3 at)
        {
            _combo.RegisterHit(amount, Time.time);
            _comboDirty = true;
        }

        void OnSwingStarted(ResolvedStep step)
        {
            _combo.RegisterSwing(step.Index, step.Step != null ? step.Step.DisplayName : string.Empty, Time.time);
            _comboDirty = true;
        }

        void Update()
        {
            if (player == null) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame)
                Respawn(keyboard.shiftKey.isPressed);

            Vector3 pos = player.transform.position;
            Vector3 offset = pos - transform.position;
            if (pos.y < blastZoneY || new Vector2(offset.x, offset.z).magnitude > blastZoneRadius)
            {
                Respawn(false);
                return;
            }

            if (_combo.Tick(Time.time)) _comboDirty = true;
            if (_comboDirty) RefreshCombo();

            if (readout != null && Time.unscaledTime >= _nextRefresh)
                Refresh();
        }

        // After every Update has run, so the motor's final position this frame is what is sampled.
        void LateUpdate()
        {
            if (player == null) return;
            if (!_jump.Sample(player.transform.position, player.isGrounded, Time.time, out var r)) return;
            _hasJump = true;
            _lastRise = r.Rise;
            _lastCarry = r.Carry;
            _lastAirTime = r.AirTime;
            _bestRise = Mathf.Max(_bestRise, r.Rise);
            _bestCarry = Mathf.Max(_bestCarry, r.Carry);
            Refresh();
        }

        /// <summary>Back to the spawn with full health. Optionally also revokes
        /// every ability, so pickups can be collected again from nothing.</summary>
        public void Respawn(bool resetAbilities)
        {
            if (player == null || spawnPoint == null) return;

            // A CharacterController overrides a teleport made while it is enabled.
            player.enabled = false;
            player.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            player.enabled = true;

            if (player.TryGetComponent(out Health health)) health.ResetToFull();

            if (resetAbilities && player.TryGetComponent(out PlayerAbilities abilities))
                foreach (AbilityKind kind in Enum.GetValues(typeof(AbilityKind)))
                    abilities.Revoke(kind);

            _jump.Reset();
            _combo.Clear();
            _comboDirty = true;
            Refresh();
        }

        void RefreshCombo()
        {
            _comboDirty = false;
            if (comboReadout == null) return;
            string last = _combo.Hits > 0 ? _combo.LastHit.ToString("0") : "-";
            string step = _combo.ChainStep == 0 ? "-"
                : _combo.StepName.Length > 0 ? $"{_combo.ChainStep}  {_combo.StepName}"
                : _combo.ChainStep.ToString();
            comboReadout.text = $"HITS  {_combo.Hits}\nTOTAL  {_combo.TotalDamage:0}\nLAST HIT  {last}\nCHAIN  {step}";
        }

        void Refresh()
        {
            if (readout == null || player == null) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;

            Vector3 v = player.velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            float height = player.transform.position.y - transform.position.y;

            string jump = _hasJump
                ? $"JUMP  {_lastRise:0.00} m up   {_lastCarry:0.00} m across   {_lastAirTime:0.00} s\n" +
                  $"BEST  {_bestRise:0.00} m up   {_bestCarry:0.00} m across"
                : "JUMP  -\nBEST  -";
            readout.text = $"SPEED  {speed:0.0} m/s\nHEIGHT  {height:0.00} m\n{jump}";
        }
    }
}
