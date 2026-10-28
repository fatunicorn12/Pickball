using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The one seam through which other systems can quiet parts of the
    /// player's input, without the input readers knowing who asked or why.
    /// Movement, combat and lock-on know this gate; they never learn that
    /// dialogue (or any later claimant) exists.
    ///
    /// <b>Claims are owner-scoped.</b> An owner (pass <c>this</c>) holds one
    /// claim: a set of <see cref="InputBlock"/> flags. <see cref="Claim"/>
    /// replaces that owner's flags; <see cref="Release"/> drops them. A flag is
    /// blocked while any owner's claim includes it, so two systems cannot stomp
    /// each other: releasing one owner never clears another's claim, and
    /// releasing twice, or an owner that never claimed, does nothing.
    ///
    /// <b>Readers check only their own flag</b> through <see cref="IsBlocked"/>,
    /// which reads a cached union, so a per-frame check costs one AND. The
    /// claim list is touched only when a claim changes; nothing allocates per
    /// frame.
    ///
    /// <b>Reload safety.</b> Claims are not serialized. <c>OnDisable</c>
    /// (which a script reload in Play mode runs first) drops them all, and the
    /// claimants release or re-assert theirs from their own lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputGate : MonoBehaviour
    {
        struct Entry
        {
            public object Owner;
            public InputBlock Flags;
        }

        List<Entry> _claims;
        InputBlock _blocked;

        /// <summary>Every flag some owner currently claims.</summary>
        public InputBlock Blocked => _blocked;

        /// <summary>Raised whenever <see cref="Blocked"/> changes value.</summary>
        public event Action<PlayerInputGate> Changed;

        /// <summary>True if any of <paramref name="flags"/> is claimed by anyone.</summary>
        public bool IsBlocked(InputBlock flags) => (_blocked & flags) != 0;

        /// <summary>The flags <paramref name="owner"/> holds; <see cref="InputBlock.None"/> if none.</summary>
        public InputBlock ClaimOf(object owner)
        {
            int i = IndexOf(owner);
            return i >= 0 ? _claims[i].Flags : InputBlock.None;
        }

        /// <summary>
        /// Sets <paramref name="owner"/>'s claim to exactly <paramref name="flags"/>,
        /// replacing whatever it held. <see cref="InputBlock.None"/> is the same as
        /// <see cref="Release"/>.
        /// </summary>
        public void Claim(object owner, InputBlock flags)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (flags == InputBlock.None)
            {
                Release(owner);
                return;
            }
            _claims ??= new List<Entry>(4);
            int i = IndexOf(owner);
            if (i >= 0)
            {
                if (_claims[i].Flags == flags) return;
                _claims[i] = new Entry { Owner = owner, Flags = flags };
            }
            else _claims.Add(new Entry { Owner = owner, Flags = flags });
            Recompute();
        }

        /// <summary>Drops <paramref name="owner"/>'s claim. Idempotent; other owners keep theirs.</summary>
        public void Release(object owner)
        {
            int i = IndexOf(owner);
            if (i < 0) return;
            _claims.RemoveAt(i);
            Recompute();
        }

        /// <summary>
        /// For readers that latch a press from an input callback and act on it
        /// in Update: returns whether the latched press should act now, and
        /// always clears the latch. So a press made while <paramref name="flag"/>
        /// is blocked is dropped, never saved to fire the moment the claim ends.
        /// A null gate passes every press through.
        /// </summary>
        public static bool TakeLatched(ref bool latched, PlayerInputGate gate, InputBlock flag)
        {
            bool act = latched && (gate == null || !gate.IsBlocked(flag));
            latched = false;
            return act;
        }

        /// <summary>Drops every claim. For teardown; claimants re-assert from their own lifecycle.</summary>
        public void ReleaseAll()
        {
            if (_claims == null || _claims.Count == 0) return;
            _claims.Clear();
            Recompute();
        }

        void OnDisable() => ReleaseAll();

        int IndexOf(object owner)
        {
            if (owner == null || _claims == null) return -1;
            for (int i = 0; i < _claims.Count; i++)
                if (ReferenceEquals(_claims[i].Owner, owner)) return i;
            return -1;
        }

        void Recompute()
        {
            var union = InputBlock.None;
            if (_claims != null)
                for (int i = 0; i < _claims.Count; i++) union |= _claims[i].Flags;
            if (union == _blocked) return;
            _blocked = union;
            Changed?.Invoke(this);
        }
    }
}
