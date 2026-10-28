using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Freeze Zone membership: the cylinder test itself (<see cref="ZoneMath"/>),
    /// and the <see cref="FreezeZone"/> applying it to real colliders — inside
    /// is slowed, outside and above are not, the caster never is, and a body
    /// overlapping the rim counts.
    /// </summary>
    public class FreezeZoneTests : MagicTestFixture
    {
        const float Radius = 3f, Height = 2.5f, Below = 0.5f;

        // ---------------------------------------------------------- ZoneMath

        [Test]
        public void Contains_CentreAndRim()
        {
            Assert.That(ZoneMath.Contains(Vector3.zero, Radius, Height, Below, Vector3.up), Is.True);
            Assert.That(ZoneMath.Contains(Vector3.zero, Radius, Height, Below, new Vector3(3f, 0f, 0f)), Is.True, "rim is inside");
            Assert.That(ZoneMath.Contains(Vector3.zero, Radius, Height, Below, new Vector3(2.2f, 1f, 2.1f)), Is.False);
            Assert.That(ZoneMath.Contains(Vector3.zero, Radius, Height, Below, new Vector3(3.01f, 0f, 0f)), Is.False);
        }

        [Test]
        public void Contains_Height()
        {
            var centre = new Vector3(5f, 2f, 5f);
            Assert.That(ZoneMath.Contains(centre, Radius, Height, Below, centre + Vector3.up * 2.5f), Is.True, "top cap");
            Assert.That(ZoneMath.Contains(centre, Radius, Height, Below, centre + Vector3.up * 2.6f), Is.False, "a ledge above");
            Assert.That(ZoneMath.Contains(centre, Radius, Height, Below, centre - Vector3.up * 0.4f), Is.True, "a little below");
            Assert.That(ZoneMath.Contains(centre, Radius, Height, Below, centre - Vector3.up * 0.6f), Is.False, "a floor below");
        }

        [Test]
        public void Contains_DegenerateZone_ContainsNothing()
        {
            Assert.That(ZoneMath.Contains(Vector3.zero, 0f, Height, Below, Vector3.zero), Is.False);
            Assert.That(ZoneMath.Contains(Vector3.zero, Radius, 0f, Below, Vector3.zero), Is.False);
        }

        [Test]
        public void AxisProbe_ClampsToTheCylinder()
        {
            Assert.That(ZoneMath.AxisProbe(Vector3.zero, Height, new Vector3(4f, 1f, 4f)), Is.EqualTo(new Vector3(0f, 1f, 0f)));
            Assert.That(ZoneMath.AxisProbe(Vector3.zero, Height, new Vector3(4f, 9f, 4f)).y, Is.EqualTo(Height));
            Assert.That(ZoneMath.AxisProbe(Vector3.zero, Height, new Vector3(4f, -3f, 4f)).y, Is.EqualTo(0f));
        }

        // ------------------------------------------------------- FreezeZone

        SpellDefinition NewZoneSpell()
        {
            var spell = NewAsset<SpellDefinition>();
            SetFloat(spell, "zoneRadius", Radius);
            SetFloat(spell, "zoneHeight", Height);
            SetFloat(spell, "zoneDuration", 4f);
            SetFloat(spell, "slowMultiplier", 0.3f);
            SetFloat(spell, "slowLinger", 0.35f);
            return spell;
        }

        StatusReceiver NewTarget(Vector3 feet, float radius = 0.4f)
        {
            var receiver = NewComponent<StatusReceiver>();
            var col = receiver.gameObject.AddComponent<CapsuleCollider>();
            col.radius = radius;
            col.height = 1.6f;
            col.center = new Vector3(0f, 0.8f, 0f);
            receiver.transform.position = feet;
            return receiver;
        }

        FreezeZone PlaceZone(Vector3 at, Transform owner = null)
        {
            var zone = NewComponent<FreezeZone>();
            Physics.SyncTransforms();
            zone.Place(at, NewZoneSpell(), owner);
            return zone;
        }

        // Zones far from the origin keep these tests clear of anything else in
        // the edit scene.
        static readonly Vector3 Site = new(500f, 0f, 500f);

        [Test]
        public void TargetInside_IsSlowed()
        {
            var target = NewTarget(Site + new Vector3(1f, 0f, 1f));
            var zone = PlaceZone(Site);
            Assert.That(target.IsSlowed, Is.True);
            Assert.That(target.SpeedMultiplier, Is.EqualTo(0.3f));
            Assert.That(zone.SlowedCount, Is.EqualTo(1));
        }

        [Test]
        public void TargetOutside_IsNotSlowed()
        {
            var target = NewTarget(Site + new Vector3(5f, 0f, 0f));
            PlaceZone(Site);
            Assert.That(target.IsSlowed, Is.False);
        }

        [Test]
        public void BodyOverlappingTheRim_Counts()
        {
            // Pivot 3.3 m out, but a 0.4 m-radius body reaches 2.9 m in.
            var target = NewTarget(Site + new Vector3(3.3f, 0f, 0f));
            PlaceZone(Site);
            Assert.That(target.IsSlowed, Is.True);
        }

        [Test]
        public void TargetOnALedgeAbove_IsNotSlowed()
        {
            var target = NewTarget(Site + new Vector3(0.5f, 3f, 0f));
            PlaceZone(Site);
            Assert.That(target.IsSlowed, Is.False);
        }

        [Test]
        public void Owner_IsNeverSlowed()
        {
            var target = NewTarget(Site + Vector3.right);
            PlaceZone(Site, owner: target.transform);
            Assert.That(target.IsSlowed, Is.False);
        }

        [Test]
        public void Tick_ReappliesWhileInside()
        {
            var target = NewTarget(Site + Vector3.right);
            var zone = PlaceZone(Site);
            zone.Tick(1f);
            Assert.That(zone.IsActive, Is.True);
            Assert.That(zone.SlowedCount, Is.EqualTo(1));
            Assert.That(zone.RemainingNormalised, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(target.IsSlowed, Is.True);
        }
    }
}
