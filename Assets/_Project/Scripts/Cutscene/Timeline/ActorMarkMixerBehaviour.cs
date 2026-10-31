using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Puts the bound actor (or prop) on the pose of the clip under the playhead. A
    /// CharacterController would undo a teleport made while it is enabled, so, as
    /// <c>TrainingStage.Respawn</c> does, it is disabled around the write and re-enabled straight
    /// after. Outside every clip the actor is left alone. No allocation per frame.
    /// </summary>
    public sealed class ActorMarkMixerBehaviour : PlayableBehaviour
    {
        Transform _target;
        CharacterController _controller;
        object _boundTo;
        bool _snap;
        float _lift;
        RaycastHit[] _hits;

        /// <summary>Probe height above a mark and depth below it for the ground ray, metres.</summary>
        const float ProbeUp = 1.5f, ProbeDown = 3f;

        /// <summary>Ground snapping (set by <see cref="ActorMarkTrack"/>).</summary>
        public void ConfigureGround(bool snap, float lift)
        {
            _snap = snap;
            _lift = lift;
            _hits = snap ? new RaycastHit[16] : null;
        }

        /// <inheritdoc />
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (playerData == null) return;
            if (!ReferenceEquals(playerData, _boundTo)) Bind(playerData);
            if (_target == null) return;

            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<ActorMarkBehaviour>)playable.GetInput(i);
                double duration = input.GetDuration();
                float t = duration > 1e-6 ? (float)(input.GetTime() / duration) : 1f;
                if (!input.GetBehaviour().TryEvaluate(t, out Pose pose)) return;
                Apply(pose);
                return;
            }
        }

        void Bind(object playerData)
        {
            _boundTo = playerData;
            _target = playerData is Component c ? c.transform : null;
            _controller = _target != null ? _target.GetComponent<CharacterController>() : null;
        }

        void Apply(Pose pose)
        {
            if (_snap && _hits != null) pose.position.y = GroundY(pose.position);
            bool reenable = _controller != null && _controller.enabled;
            if (reenable) _controller.enabled = false;
            _target.SetPositionAndRotation(pose.position, pose.rotation);
            if (reenable) _controller.enabled = true;
        }

        /// <summary>The highest ground under <paramref name="p"/> (plus the lift), or p.y when nothing is hit.</summary>
        float GroundY(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * ProbeUp, Vector3.down, _hits, ProbeUp + ProbeDown, ~0,
                QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null || col.transform.IsChildOf(_target)) continue;
                if (col.GetComponentInParent<CutsceneActor>() != null) continue;
                if (_hits[i].point.y > best) best = _hits[i].point.y;
            }
            return float.IsNegativeInfinity(best) ? p.y : best + _lift;
        }
    }
}
