using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The Freeze Zone's overlap query includes trigger colliders, so that
    /// Hypatia's projectiles (a trigger sphere plus <see cref="ISlowable"/>, no
    /// solid collider) can be slowed. These tests pin down what that change may
    /// and may not do: a trigger-only slowable is slowed; a body with both a
    /// solid collider and a trigger is slowed exactly once per tick; triggers
    /// that are not slowable (Birdy's talk ring, a pickleball HitZone, a
    /// training-station volume) are left completely alone; and the zone never
    /// calls anything but <see cref="ISlowable.ApplySlow"/> on what it finds.
    ///
    /// Real physics queries, as in <see cref="FreezeZoneTests"/>: EditMode does
    /// not step the simulation, but overlap queries work once
    /// <see cref="Physics.SyncTransforms"/> has pushed the transforms.
    /// </summary>
    public class FreezeZoneTriggerTests : MagicTestFixture
    {
        const float Radius = 3f, Height = 2.5f;

        // Far from the origin and from FreezeZoneTests' site, so nothing else in
        // the edit scene is in range.
        static readonly Vector3 Site = new(-600f, 0f, 600f);

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

        FreezeZone PlaceZone(Vector3 at)
        {
            var zone = NewComponent<FreezeZone>();
            Physics.SyncTransforms();
            zone.Place(at, NewZoneSpell(), null);
            return zone;
        }

        static SphereCollider AddTriggerSphere(GameObject host, float radius)
        {
            var sphere = host.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.isTrigger = true;
            return sphere;
        }

        GameObject NewChild(Component parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = localPosition;
            return child; // destroyed with its parent in TearDown
        }

        // ------------------------------------------------- slowable triggers

        [Test]
        public void TriggerOnlySlowable_IsSlowed()
        {
            // Hypatia's projectiles look like this: an ISlowable and a small
            // trigger sphere, nothing solid.
            var projectile = NewComponent<StatusReceiver>();
            AddTriggerSphere(projectile.gameObject, 0.3f);
            projectile.transform.position = Site + new Vector3(1f, 1f, 0f);

            var zone = PlaceZone(Site);

            Assert.That(projectile.IsSlowed, Is.True);
            Assert.That(projectile.SpeedMultiplier, Is.EqualTo(0.3f));
            Assert.That(zone.SlowedCount, Is.EqualTo(1));
        }

        [Test]
        public void TriggerOnlySlowable_OutsideTheCylinder_IsNotSlowed()
        {
            // Inside the query's bounding sphere but beyond the disc's radius: the
            // cylinder test still decides, trigger or not.
            var projectile = NewComponent<StatusReceiver>();
            AddTriggerSphere(projectile.gameObject, 0.1f);
            // Query sphere: centre 1.25 m up, reach ~3.25 m, so this 3.1 m-away
            // trigger is returned; its nearest point is 3.1 m from the axis.
            projectile.transform.position = Site + new Vector3(3.2f, Height * 0.5f, 0f);

            var zone = PlaceZone(Site);

            Assert.That(projectile.IsSlowed, Is.False);
            Assert.That(zone.SlowedCount, Is.EqualTo(0));
        }

        [Test]
        public void SolidColliderPlusTrigger_IsSlowedOncePerTick()
        {
            var spy = NewComponent<FreezeZoneSpySlowable>();
            var body = spy.gameObject.AddComponent<CapsuleCollider>();
            body.radius = 0.4f;
            body.height = 1.6f;
            body.center = new Vector3(0f, 0.8f, 0f);
            AddTriggerSphere(NewChild(spy, "Sense", new Vector3(0f, 0.8f, 0f)), 1f);
            spy.transform.position = Site + Vector3.right;

            var zone = PlaceZone(Site);
            Assert.That(spy.SlowCalls, Is.EqualTo(1), "both colliders found, one slow applied");
            Assert.That(zone.SlowedCount, Is.EqualTo(1));

            zone.Tick(0.1f);
            Assert.That(spy.SlowCalls, Is.EqualTo(2), "once more per tick, not twice");
            Assert.That(zone.SlowedCount, Is.EqualTo(1));
        }

        [Test]
        public void OnlyApplySlow_IsEverCalledOnWhatTheQueryFinds()
        {
            // The spy is also damageable and staggerable; the zone must not reach
            // for either, through the trigger or the solid collider.
            var spy = NewComponent<FreezeZoneSpySlowable>();
            spy.gameObject.AddComponent<BoxCollider>();
            AddTriggerSphere(NewChild(spy, "Sense", Vector3.zero), 1.2f);
            spy.transform.position = Site + new Vector3(0f, 1f, 1f);

            var zone = PlaceZone(Site);
            zone.Tick(0.1f);

            Assert.That(spy.SlowCalls, Is.EqualTo(2));
            Assert.That(spy.LastSource, Is.SameAs(zone));
            Assert.That(spy.HitCalls, Is.EqualTo(0), "no damage");
            Assert.That(spy.StaggerCalls, Is.EqualTo(0), "no stagger");
        }

        // --------------------------------------------- non-slowable triggers

        [Test]
        public void NonSlowableTriggers_AreUnaffected()
        {
            // Stand-ins for the triggers that share the sandbox with a zone:
            // Birdy's talk ring (a trigger sphere on an NPC that can be hit), a
            // pickleball HitZone (box trigger), and a training-station volume
            // (box trigger under a damageable station root). None is ISlowable.
            var talkRing = NewComponent<FreezeZoneSpyBystander>();
            AddTriggerSphere(talkRing.gameObject, 2.5f);
            talkRing.transform.position = Site + new Vector3(0.5f, 0f, 0f);

            var hitZone = NewComponent<HitZone>();
            var box = hitZone.GetComponent<BoxCollider>();
            box.isTrigger = true; // HitZone.Awake does this; Awake does not run in EditMode
            box.size = new Vector3(2f, 2f, 2f);
            hitZone.transform.position = Site + new Vector3(-1f, 1f, 0f);

            var station = NewComponent<FreezeZoneSpyBystander>();
            station.transform.position = Site + new Vector3(0f, 0f, -1f);
            var volume = NewChild(station, "StationVolume", new Vector3(0f, 0.5f, 0f)).AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(1.5f, 1f, 1.5f);

            var plain = new GameObject("PlainTrigger") { hideFlags = HideFlags.DontSave };
            plain.transform.SetParent(station.transform, false); // cleaned up with the station
            plain.transform.position = Site + new Vector3(1f, 0.5f, 1f);
            AddTriggerSphere(plain, 0.5f);

            var zone = PlaceZone(Site);
            zone.Tick(0.1f);

            Assert.That(zone.SlowedCount, Is.EqualTo(0), "nothing in range is slowable");
            foreach (var bystander in new[] { talkRing, station })
            {
                Assert.That(bystander.HitCalls, Is.EqualTo(0), $"{bystander.name} was hit");
                Assert.That(bystander.StaggerCalls, Is.EqualTo(0), $"{bystander.name} was staggered");
            }
        }

        [Test]
        public void NonSlowableTrigger_BesideASlowable_DoesNotChangeTheCount()
        {
            var target = NewComponent<StatusReceiver>();
            var col = target.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.8f, 0f);
            target.transform.position = Site + Vector3.right;

            var bystander = NewComponent<FreezeZoneSpyBystander>();
            AddTriggerSphere(bystander.gameObject, 1f);
            bystander.transform.position = Site - Vector3.right;

            var zone = PlaceZone(Site);

            Assert.That(target.IsSlowed, Is.True);
            Assert.That(zone.SlowedCount, Is.EqualTo(1));
            Assert.That(bystander.HitCalls + bystander.StaggerCalls, Is.EqualTo(0));
        }
    }

    /// <summary>An ISlowable that also exposes every other way a spell could
    /// touch it, and counts each call.</summary>
    internal sealed class FreezeZoneSpySlowable : MonoBehaviour, ISlowable, IDamageable, IStaggerable
    {
        public int SlowCalls, HitCalls, StaggerCalls;
        public object LastSource;

        public void ApplySlow(object source, float speedMultiplier, float seconds)
        {
            SlowCalls++;
            LastSource = source;
        }

        public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                            DamageKind kind = DamageKind.Physical) => HitCalls++;

        public void Stagger(float seconds) => StaggerCalls++;
    }

    /// <summary>A damageable, staggerable bystander that is NOT slowable: what
    /// an NPC with a talk ring or a training station looks like to the zone.</summary>
    internal sealed class FreezeZoneSpyBystander : MonoBehaviour, IDamageable, IStaggerable
    {
        public int HitCalls, StaggerCalls;

        public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                            DamageKind kind = DamageKind.Physical) => HitCalls++;

        public void Stagger(float seconds) => StaggerCalls++;
    }
}
