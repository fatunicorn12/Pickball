using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Where a ground spell (Freeze Zone, an untargeted Thunderbolt) may land
    /// (<see cref="GroundProbe.TryGroundTarget"/>, QA F-2): on the floor under
    /// the aim, within the probe depth and the spell's range, or nowhere. The
    /// caster refuses a press, free, whenever this says no, so a sky aim never
    /// spends mana on a spell placed 26-39 m up.
    /// </summary>
    public class GroundTargetTests : MagicTestFixture
    {
        const float Range = 30f, Drop = 12f;

        // Far from the origin, clear of anything else in the edit scene.
        static readonly Vector3 Site = new(-500f, 0f, 500f);

        readonly RaycastHit[] _buffer = new RaycastHit[16];

        void NewFloor(float halfSize = 10f)
        {
            var floor = NewComponent<BoxCollider>();
            floor.size = new Vector3(halfSize * 2f, 1f, halfSize * 2f);
            floor.transform.position = Site - Vector3.up * 0.5f;   // top face at Site.y
            Physics.SyncTransforms();
        }

        bool Resolve(Vector3 aim, out Vector3 ground, float range = Range)
            => GroundProbe.TryGroundTarget(aim, Site, range, Drop, ~0, null, _buffer, out ground);

        [Test]
        public void AimAtTheSky_IsRefused()
        {
            NewFloor();
            Assert.That(Resolve(Site + new Vector3(3f, 26f, 8f), out _), Is.False, "26 m up: no floor within 12 m");
            Assert.That(Resolve(Site + new Vector3(1f, 39f, 2f), out _), Is.False);
        }

        [Test]
        public void AimOnTheFloor_LandsWhereAimed()
        {
            NewFloor();
            Assert.That(Resolve(Site + new Vector3(3f, 0f, 2f), out var ground), Is.True);
            Assert.That(Vector3.Distance(ground, Site + new Vector3(3f, 0f, 2f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void AimOnAWallOrChest_DropsToTheFloorBelow()
        {
            NewFloor();
            Assert.That(Resolve(Site + new Vector3(3f, 2.5f, 0f), out var ground), Is.True);
            Assert.That(ground.y, Is.EqualTo(Site.y).Within(1e-3f));
            Assert.That(ground.x, Is.EqualTo(Site.x + 3f).Within(1e-3f));
        }

        [Test]
        public void OverAPit_IsRefused()
        {
            NewFloor(halfSize: 5f);
            Assert.That(Resolve(Site + new Vector3(8f, 0f, 0f), out _), Is.False, "past the floor's edge");
        }

        [Test]
        public void GroundOutOfRange_IsRefused()
        {
            NewFloor();
            Assert.That(Resolve(Site + new Vector3(8f, 0f, 0f), out _, range: 5f), Is.False);
            Assert.That(Resolve(Site + new Vector3(4f, 0f, 3f), out _, range: 5f), Is.True, "5 m exactly is in range");
        }
    }
}
