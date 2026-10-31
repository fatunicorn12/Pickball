using Pickleball.Cutscene.Actors;
using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Applies the active <see cref="ActorStateClip"/> to the bound object: a
    /// <see cref="CutsceneRotor"/> in its children (Birdy) and/or a <see cref="PropellerProp"/> and
    /// its renderers (the loose propeller). Steps, never blends. The components are found once
    /// per binding; nothing allocates per frame. In edit mode the rotor is ticked with dt 0 so
    /// Timeline preview shows the detached rotor too. The last applied state stays after the
    /// timeline stops, so Birdy keeps his bare helmet through the platforming.
    /// </summary>
    public sealed class ActorStateMixerBehaviour : PlayableBehaviour
    {
        object _boundTo;
        CutsceneRotor _rotor;
        PropellerProp _prop;
        Renderer[] _propRenderers;
        EyeSwap _eyes;

        /// <inheritdoc />
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData == null) return;
            if (!ReferenceEquals(playerData, _boundTo)) Bind(playerData);

            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var b = ((ScriptPlayable<ActorStateBehaviour>)playable.GetInput(i)).GetBehaviour();
                Apply(b);
                return;
            }
        }

        void Bind(object playerData)
        {
            _boundTo = playerData;
            var go = playerData as GameObject;
            if (go == null && playerData is Component c) go = c.gameObject;
            _rotor = go != null ? go.GetComponentInChildren<CutsceneRotor>(true) : null;
            _prop = go != null ? go.GetComponentInChildren<PropellerProp>(true) : null;
            _propRenderers = _prop != null ? _prop.GetComponentsInChildren<Renderer>(true) : null;
            _eyes = go != null ? go.GetComponentInChildren<EyeSwap>(true) : null;
        }

        void Apply(ActorStateBehaviour b)
        {
            if (_rotor != null)
            {
                if (b.RotorAttached >= 0) _rotor.SetAttached(b.RotorAttached > 0);
                if (b.RotorSpin >= 0f) _rotor.SetSpin(b.RotorSpin);
                if (!Application.isPlaying) _rotor.Tick(0f);
            }
            if (_eyes != null && b.Eyes >= 0) _eyes.Show(b.Eyes);
            if (_prop != null)
            {
                if (b.PropSpin >= 0f)
                {
                    if (b.PropSpin <= 0f) _prop.SpinDown();
                    else _prop.SpinTo(b.PropSpin);
                }
                if (b.PropVisible >= 0 && _propRenderers != null)
                    for (int i = 0; i < _propRenderers.Length; i++)
                        if (_propRenderers[i] != null) _propRenderers[i].enabled = b.PropVisible > 0;
            }
        }
    }
}
