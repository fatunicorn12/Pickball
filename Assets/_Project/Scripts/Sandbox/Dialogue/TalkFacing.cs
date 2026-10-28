using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Turns the player to face the NPC while a talk settles, the way Link turns
    /// to whoever he speaks to.
    ///
    /// <b>Why this way.</b> Movement already takes facing requests from outside
    /// through <see cref="IFacingSource"/> on <see cref="MovementMotor.FacingSource"/>
    /// (lock-on and attack square-up use it), so the dialogue needs no change to
    /// movement code. For the settle it stands in front of whatever source was
    /// there, asking urgently (a fast turn even while he is still sliding to a
    /// stop), and puts that source back afterwards. Before, the lock-on did this
    /// by accident; now that a talk drops the lock, nothing else would.
    ///
    /// <b>Hand-back.</b> <see cref="End"/> restores the previous source only if
    /// this one is still installed, so a source that replaced it meanwhile (the
    /// player's glue component re-enabling) is never overwritten. The motor does
    /// not tick while time is stopped, so the turn only happens during the
    /// settle; ending it when the pause begins changes nothing visible.
    /// </summary>
    public sealed class TalkFacing : IFacingSource
    {
        MovementMotor _motor;
        IFacingSource _previous;
        Vector3 _point;

        /// <summary>True while this is installed on a motor.</summary>
        public bool IsActive => _motor != null && ReferenceEquals(_motor.FacingSource, this);

        /// <summary>Turns <paramref name="partner"/> (the player's root, which holds
        /// his <see cref="MovementMotor"/>) toward <paramref name="point"/> until
        /// <see cref="End"/>. Returns false, changing nothing, without a motor.</summary>
        public bool Begin(Transform partner, Vector3 point)
        {
            End();
            if (partner == null || !partner.TryGetComponent(out MovementMotor motor)) return false;
            _motor = motor;
            _previous = motor.FacingSource;
            _point = point;
            motor.FacingSource = this;
            return true;
        }

        /// <summary>Hands facing back to the source that was there before. Safe to
        /// call more than once, or without a <see cref="Begin"/>.</summary>
        public void End()
        {
            if (_motor != null && ReferenceEquals(_motor.FacingSource, this)) _motor.FacingSource = _previous;
            _motor = null;
            _previous = null;
        }

        bool IFacingSource.TryGetFacingTarget(out Vector3 point, out bool urgent)
        {
            point = _point;
            urgent = true;
            return true;
        }
    }
}
