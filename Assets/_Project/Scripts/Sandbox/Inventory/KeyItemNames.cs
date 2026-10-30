namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The key item that stands for each <see cref="AbilityKind"/>: its stable
    /// id, menu name and blurb. One table, so the setup that seeds the assets
    /// and the tests that check them agree, and so the in-fiction names live
    /// in one place (the Tether ability is the <b>Pendulum</b>).
    /// </summary>
    public static class KeyItemNames
    {
        /// <summary>One row of the table.</summary>
        public readonly struct Entry
        {
            /// <summary>Stable id (lowercase, underscores).</summary>
            public readonly string Id;

            /// <summary>Menu name.</summary>
            public readonly string DisplayName;

            /// <summary>Menu blurb.</summary>
            public readonly string Description;

            /// <summary>Builds a row.</summary>
            public Entry(string id, string displayName, string description)
            {
                Id = id;
                DisplayName = displayName;
                Description = description;
            }
        }

        /// <summary>Every ability, in Key Items tab order.</summary>
        public static readonly AbilityKind[] Order =
        {
            AbilityKind.Dash,
            AbilityKind.DoubleJump,
            AbilityKind.Tether,
            AbilityKind.GravityAnchor,
            AbilityKind.Skateboard,
        };

        /// <summary>The row for <paramref name="kind"/>.</summary>
        public static Entry For(AbilityKind kind) => kind switch
        {
            AbilityKind.Dash => new Entry("dash", "Dash",
                "A quick burst along the ground or through the air. E / RB."),
            AbilityKind.DoubleJump => new Entry("double_jump", "Double Jump",
                "A feather-light second jump in mid-air. Space / A while airborne."),
            AbilityKind.Tether => new Entry("pendulum", "Pendulum",
                "Latch onto an anchor and swing from it like Foucault's pendulum. Q / LB."),
            AbilityKind.GravityAnchor => new Entry("gravity_anchor", "Gravity Anchor",
                "Switch your own gravity off and hang in the air; hold Jump to rise. G / R3."),
            AbilityKind.Skateboard => new Entry("skateboard", "Skateboard",
                "Ride the board for speed and tricks. R / D-pad right."),
            _ => new Entry(kind.ToString().ToLowerInvariant(), kind.ToString(), ""),
        };
    }
}
