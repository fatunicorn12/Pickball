using System;
using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Gameplay world-space indicators the cutscene hides for its whole length and puts back
    /// exactly as they were: the Player's heal-channel ring and pentagram rune, the dash
    /// protractor arc, the sword's blade trail, the tether rope, the double-jump puff, the
    /// skateboard and any light they carry. The systems that own them are never edited or told.
    /// The setup menu fills the lists (every renderer and light under the Player that is not part
    /// of its character model).
    /// <para>
    /// <b>How it hides without fighting the owners:</b> renderers get
    /// <see cref="Renderer.forceRenderingOff"/>, lights a culling mask of 0. Neither touches the
    /// <c>enabled</c> flag the owners toggle (the dash arc times itself out, the blade trail sets
    /// it every frame, the heal ring shows and hides on spell events), so an owner that changes
    /// its own state during a cutscene keeps that change, and <see cref="Restore"/> puts back
    /// exactly the flag and mask that were there before. No GameObject is deactivated, so no owner
    /// sees an OnDisable/OnEnable. Serializable plain class owned by <see cref="CutscenePlayer"/>;
    /// the state arrays are allocated on the first hide, never per frame.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GameplayVisuals
    {
        [Tooltip("Gameplay-only renderers hidden while a cutscene plays (set by the scene builder).")]
        [SerializeField] private Renderer[] renderers = new Renderer[0];

        [Tooltip("Gameplay-only lights switched off while a cutscene plays (set by the scene builder).")]
        [SerializeField] private Light[] lights = new Light[0];

        [Tooltip("Gameplay behaviours disabled while a cutscene plays and re-enabled exactly as they were: " +
                 "the Player's foot IK (ChibiFootIK), whose foot planting held a boot behind MC at hip height " +
                 "while the timeline walked him (review round 2). Re-enabled early in a hand-back scene's " +
                 "eased last second (CutscenePlayer), so gameplay's grounded pose is already on screen at the cut. " +
                 "Set by the scene builder.")]
        [SerializeField] private Behaviour[] behaviours = new Behaviour[0];

        bool[] _rendererWas;
        int[] _lightWas;
        bool[] _behaviourWas;
        bool _hidden;

        /// <summary>True between <see cref="Hide"/> and <see cref="Restore"/>.</summary>
        public bool IsHidden => _hidden;

        /// <summary>The renderers in the list (read-only view for tools and tests).</summary>
        public Renderer[] Renderers => renderers;

        /// <summary>The lights in the list.</summary>
        public Light[] Lights => lights;

        /// <summary>The behaviours disabled during cutscenes.</summary>
        public Behaviour[] Behaviours => behaviours;

        /// <summary>Sets the behaviours disabled during cutscenes (setup menu and tests).</summary>
        public void ConfigureBehaviours(Behaviour[] disabled)
        {
            behaviours = disabled ?? new Behaviour[0];
            _behaviourWas = null;
        }

        /// <summary>Sets the lists (setup menu and tests). Nulls become empty lists.</summary>
        public void Configure(Renderer[] hiddenRenderers, Light[] hiddenLights)
        {
            renderers = hiddenRenderers ?? new Renderer[0];
            lights = hiddenLights ?? new Light[0];
            _rendererWas = null;
            _lightWas = null;
            _hidden = false;
        }

        /// <summary>Records each item's forced-off flag or culling mask and hides it. Idempotent.</summary>
        public void Hide()
        {
            if (_hidden) return;
            renderers ??= new Renderer[0];
            lights ??= new Light[0];
            if (_rendererWas == null || _rendererWas.Length != renderers.Length) _rendererWas = new bool[renderers.Length];
            if (_lightWas == null || _lightWas.Length != lights.Length) _lightWas = new int[lights.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                _rendererWas[i] = r.forceRenderingOff;
                r.forceRenderingOff = true;
            }
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                _lightWas[i] = l.cullingMask;
                l.cullingMask = 0;
            }
            behaviours ??= new Behaviour[0];
            if (_behaviourWas == null || _behaviourWas.Length != behaviours.Length) _behaviourWas = new bool[behaviours.Length];
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                _behaviourWas[i] = b.enabled;
                b.enabled = false;
            }
            _hidden = true;
        }

        /// <summary>Writes back the states <see cref="Hide"/> recorded. Does nothing when not hidden.</summary>
        public void Restore()
        {
            if (!_hidden) return;
            _hidden = false;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = _rendererWas[i];
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].cullingMask = _lightWas[i];
            RestoreBehaviours();
        }

        /// <summary>
        /// Puts the disabled behaviours back as <see cref="Hide"/> found them, ahead of the rest (a
        /// hand-back scene's last second). Idempotent; <see cref="Restore"/> calls it too.
        /// </summary>
        public void RestoreBehaviours()
        {
            if (behaviours == null || _behaviourWas == null) return;
            for (int i = 0; i < behaviours.Length && i < _behaviourWas.Length; i++)
                if (behaviours[i] != null) behaviours[i].enabled = _behaviourWas[i];
            _behaviourWas = null;
        }
    }
}
