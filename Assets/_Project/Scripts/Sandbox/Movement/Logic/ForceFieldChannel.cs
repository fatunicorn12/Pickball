using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// The force-field velocity channel (gravity wells, wind, conveyors), moved
    /// out of ChibiLocomotion unchanged in behaviour so it can be tested.
    ///
    /// Two numbers live here. <see cref="Velocity"/> is what fields are asking
    /// the character to drift at; <see cref="Applied"/> is the share of the last
    /// tick's MEASURED motion that fields actually delivered. The Fluxion Beam
    /// prices damage off the second — see <see cref="Attribute"/> for the
    /// identity it depends on.
    /// </summary>
    public sealed class ForceFieldChannel
    {
        /// <summary>Banked field velocity, m/s — the request, before collision.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Field share of the last tick's achieved velocity, m/s.</summary>
        public Vector3 Applied { get; private set; }

        /// <summary>
        /// One tick: relax the banked field velocity toward the terminal speed this
        /// frame's acceleration implies.
        ///
        /// <b>The damping has to be proportional.</b> It used to be
        /// <c>MoveTowards(v, 0, fieldDrag · dt)</c>, which subtracts a flat rate and
        /// clamps at zero — so ANY acceleration below that rate produced exactly
        /// nothing, and every well in the Newton fight spawned outside its own
        /// effective radius. A proportional law has no threshold at any distance:
        /// half the pull is half the drift, out to the rim.
        ///
        /// Solved rather than stepped. dv/dt = a − k·v has the closed form
        /// v(t) = a/k + (v₀ − a/k)·e^(−k·t), so the settled speed is exactly a/k at
        /// ANY frame rate; stepping it the obvious way drifts about 4% between 30
        /// and 144 fps.
        /// </summary>
        /// <param name="acceleration">This frame's summed field acceleration, m/s².</param>
        /// <param name="damping">Relaxation rate, 1/s. 0 = undamped (tuning escape hatch).</param>
        /// <param name="maxSpeed">Backstop on the banked speed, m/s.</param>
        public void Integrate(Vector3 acceleration, float dt, float damping, float maxSpeed)
        {
            Vector3 v = Velocity;
            if (damping > 0f)
            {
                Vector3 terminal = acceleration / damping;   // where a steady pull settles
                v = terminal + (v - terminal) * Mathf.Exp(-damping * dt);
            }
            else
            {
                // No terminal speed without damping; maxSpeed alone bounds it.
                v += acceleration * dt;
            }
            Velocity = Vector3.ClampMagnitude(v, maxSpeed);
        }

        /// <summary>
        /// Cut loose from every field, banked velocity and all. Called every frame
        /// the tether or the dash owns movement: field velocity used to keep
        /// accumulating unread while they ran and then switch on at full
        /// magnitude the instant movement was handed back. Draining it makes the
        /// rule one sentence — while the rope or the dash has you the field has
        /// nothing, and it builds again from zero afterwards.
        /// </summary>
        public void Clear()
        {
            Velocity = Vector3.zero;
            Applied = Vector3.zero;
        }

        /// <summary>
        /// Split what the controller ACTUALLY moved into the field's share and the
        /// character's own, and bank the field's as <see cref="Applied"/>.
        ///
        /// Move can deliver less than it was asked for — a wall, a step, a floor
        /// under a downward pull — without saying which part it refused, so the
        /// shortfall is attributed in proportion to what each side asked for. In
        /// the case the Fluxion Beam depends on it is exact: requesting no motion
        /// of his own, the field is credited with precisely the motion that
        /// occurred, so <c>achieved − Applied</c> is exactly zero and being
        /// dragged into a wall or held in a well core costs nothing.
        /// </summary>
        /// <param name="requested">Total velocity handed to Move (own + field), m/s.</param>
        /// <param name="field">The field part of <paramref name="requested"/>, m/s.</param>
        /// <param name="grounded">Whether the ground hold-down was part of the request.</param>
        /// <param name="achieved">CharacterController.velocity after the Move.</param>
        public void Attribute(Vector3 requested, Vector3 field, bool grounded, Vector3 achieved)
        {
            // The grounded hold-down welds the capsule to the floor, which absorbs
            // all of it every frame. It must come out of BOTH the share and the
            // shortfall, or the field carries a constant phantom of it in y.
            Vector3 own = requested - field;
            if (grounded && own.y < 0f) own.y = 0f;

            float fieldAsked = field.magnitude;
            float ownAsked = own.magnitude;
            float asked = fieldAsked + ownAsked;
            if (asked < 1e-4f)
            {
                Applied = Vector3.zero;
                return;
            }

            Vector3 shortfall = own + field - achieved;
            Applied = field - shortfall * (fieldAsked / asked);
        }
    }
}
