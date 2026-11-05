using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.LockOn
{
    /// <summary>
    /// The one answer to "where is the enemy": from what the sword can hit, tracked live. These
    /// deliberately never call Physics.SyncTransforms: autoSyncTransforms is off in this project,
    /// and an aim that needs a sync would stair-step at the physics rate.
    /// </summary>
    public class LockOnAimPointTests : LockOnTestFixture
    {
        readonly List<Collider> _colliders = new();
        readonly List<Renderer> _renderers = new();

        LockOnBody Measure(Health h, int mask = ~0)
        {
            Assert.IsTrue(LockOnAim.TryMeasureHittable(h, mask, LockOnAim.DefaultChestBias, _colliders, out var body), "nothing hittable");
            return body;
        }

        bool HasHittable(Health h, int mask = ~0)
            => LockOnAim.TryMeasureHittable(h, mask, LockOnAim.DefaultChestBias, _colliders, out _);

        GameObject Child(Health parent, PrimitiveType type, Vector3 local, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            return go;
        }

        LockOnTargeting NewLockOn() => Host("Player").AddComponent<LockOnTargeting>();

        // ------------------------------------------------------------- what counts

        [Test]
        public void Body_ComesFromTheCollider_NotTheRenderersAroundIt()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var banner = Child(h, PrimitiveType.Cube, new Vector3(0f, 9f, 0f), new Vector3(6f, 6f, 0.2f));
            Object.DestroyImmediate(banner.GetComponent<Collider>());

            var body = Measure(h);
            Assert.That(body.IsHittable, Is.True);
            Assert.That(body.Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(body.Radius, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void AimPoint_IsTheChest_InsideTheBody()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var body = Measure(h);
            Assert.That(body.AimPoint.y, Is.EqualTo(3.2f).Within(1e-4f), "a fifth of the 1 m half-height above the middle");
            Assert.That(body.Bounds.Contains(body.AimPoint), Is.True);
        }

        [Test]
        public void Triggers_DontCount()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var sense = Child(h, PrimitiveType.Sphere, new Vector3(0f, 10f, 0f), Vector3.one * 8f);
            sense.GetComponent<Collider>().isTrigger = true;
            Assert.That(Measure(h).Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void DisabledAndInactiveColliders_DontCount()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            Child(h, PrimitiveType.Cube, new Vector3(0f, 10f, 0f), Vector3.one).GetComponent<Collider>().enabled = false;
            Child(h, PrimitiveType.Cube, new Vector3(0f, 12f, 0f), Vector3.one).SetActive(false);
            Assert.That(Measure(h).Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void LayersTheBladeCantHit_DontCount()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            var shield = Child(h, PrimitiveType.Cube, new Vector3(0f, 10f, 0f), Vector3.one);
            shield.layer = 2;   // Ignore Raycast
            int blade = ~(1 << 2);
            Assert.That(Measure(h, blade).Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(capsule.gameObject.layer, Is.Not.EqualTo(2));
        }

        [Test]
        public void TheMask_ComesFromTheSword()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            capsule.gameObject.layer = 2;
            var scenery = Child(h, PrimitiveType.Cube, new Vector3(0f, 10f, 0f), Vector3.one);
            scenery.layer = 0;

            var lockOn = NewLockOn();
            var melee = lockOn.gameObject.AddComponent<MeleeCombat>();
            SetMask(melee, "hittable", 1 << 2);
            ForceLock(lockOn, h);

            Assert.That(lockOn.AimPoint.y, Is.EqualTo(3.2f).Within(1e-4f), "the only thing this blade can hit");
        }

        [Test]
        public void LockOnIgnore_DoesntCount()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var dial = Child(h, PrimitiveType.Cube, new Vector3(0f, 8f, 0f), new Vector3(4f, 4f, 0.2f));
            dial.AddComponent<LockOnIgnore>();
            // A part below the marker, as the astrolabe's dial parts are.
            var part = Child(h, PrimitiveType.Sphere, Vector3.zero, Vector3.one);
            part.transform.SetParent(dial.transform, true);
            Assert.That(Measure(h).Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void AColliderOwnedByANearerDamageable_DoesntCount()
        {
            // A Hypatia orbiter is its own damageable, and the sword would strike that, not her.
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var orbiter = Child(h, PrimitiveType.Sphere, new Vector3(0f, 12f, 0f), Vector3.one);
            orbiter.AddComponent<Health>();
            Assert.That(Measure(h).Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void SeveralColliders_MakeOneBody()
        {
            var h = NewFloater("Target", Vector3.zero, 1f, out _);
            Child(h, PrimitiveType.Sphere, new Vector3(0f, 3.5f, 0f), Vector3.one);
            var body = Measure(h);
            Assert.That(body.Bounds.min.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(body.Bounds.max.y, Is.EqualTo(4f).Within(1e-4f));
        }

        // ------------------------------------------------------------- shapes

        [Test]
        public void EveryColliderShape_MatchesWhatPhysicsReports()
        {
            var parent = Host("Parent", new Vector3(3f, 2f, -4f));
            parent.transform.rotation = Quaternion.Euler(20f, 30f, 40f);
            parent.transform.localScale = Vector3.one * 1.5f;
            foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere })
            {
                var go = GameObject.CreatePrimitive(type);
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = new Vector3(1f, 2f, 3f);
                var col = go.GetComponent<Collider>();
                Physics.SyncTransforms();
                Assert.IsTrue(LockOnAim.TryWorldBounds(col, out Bounds mine));
                Assert.That(Vector3.Distance(mine.center, col.bounds.center), Is.LessThan(1e-3f), type.ToString());
                if (type == PrimitiveType.Sphere) continue;   // ours is the box round it, by design
                Assert.That(Vector3.Distance(mine.extents, col.bounds.extents), Is.LessThan(1e-2f), type.ToString());
            }
        }

        [Test]
        public void AMeshCollider_UsesItsMeshBounds()
        {
            var go = Primitive(PrimitiveType.Cube, "Mesh", new Vector3(1f, 2f, 3f));
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            Assert.IsTrue(LockOnAim.TryWorldBounds(col, out Bounds b));
            Assert.That(b.center, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(b.extents.x, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void AMeshColliderWithNoMesh_HasNoShape()
        {
            var col = Host("Empty").AddComponent<MeshCollider>();
            Assert.IsFalse(LockOnAim.TryWorldBounds(col, out _));
        }

        [Test]
        public void ACapsuleOnAnotherAxis_IsLongOnThatAxis()
        {
            var col = Host("Capsule").AddComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 4f;
            col.direction = 2;
            Assert.IsTrue(LockOnAim.TryWorldBounds(col, out Bounds b));
            Assert.That(b.extents, Is.EqualTo(new Vector3(0.5f, 0.5f, 2f)));
        }

        [Test]
        public void AToppledBody_IsMeasuredOnItsSide()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var body = Measure(h);
            Assert.That(body.Bounds.extents.y, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(body.Radius, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(body.Bounds.center.y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void TheAlienEnemy_IsMeasuredFromItsCharacterController()
        {
            var h = NewTarget("Alien", new Vector3(2f, 0f, 2f));
            var controller = h.gameObject.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = new Vector3(0f, 1.1f, 0f);
            var body = Measure(h);
            Assert.That(Vector3.Distance(body.Bounds.center, new Vector3(2f, 1.1f, 2f)), Is.LessThan(1e-4f));
            Assert.That(body.Radius, Is.EqualTo(0.5f).Within(1e-4f));
        }

        // ------------------------------------------------------------ tracking it

        [Test]
        public void AimPoint_FollowsTheRoot_WithNoPhysicsSync()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            float before = lockOn.AimPoint.x;
            h.transform.position += Vector3.right * 2f;
            Assert.That(lockOn.AimPoint.x - before, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void AimPoint_FollowsABodyHoveringOnAPlantedRoot()
        {
            // The old offset was frozen at lock time against the root, and drifted off her.
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            capsule.transform.localPosition = Vector3.up * 5f;
            Assert.That(lockOn.AimPoint.y, Is.EqualTo(5.2f).Within(1e-4f));
        }

        [Test]
        public void AJuggledDummy_CountsItsOffsetOnce()
        {
            // TrainingDummy moves its collider's centre and reports the same move as BodyOffset.
            var h = NewTarget("Dummy", Vector3.zero);
            var col = h.gameObject.AddComponent<CapsuleCollider>();
            col.height = 1.6f;
            col.radius = 0.42f;
            col.center = new Vector3(0f, 0.8f, 0f);
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            float rest = lockOn.AimPoint.y;

            col.center += Vector3.up * 2f;
            h.BodyOffset = Vector3.up * 2f;

            Assert.That(lockOn.AimPoint.y - rest, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void TheAim_DoesntBreatheWithTheRenderers()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            Vector3 before = lockOn.AimPoint;
            var limb = Child(h, PrimitiveType.Cube, Vector3.up * 3f, Vector3.one);
            Object.DestroyImmediate(limb.GetComponent<Collider>());
            limb.transform.localScale = Vector3.one * 7f;
            Assert.That(lockOn.AimPoint, Is.EqualTo(before));
            Assert.That(capsule.enabled, Is.True);
        }

        [Test]
        public void TargetRadius_ReadsTheHittableBody()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out _);
            var lockOn = NewLockOn();
            Assert.That(lockOn.TargetRadius, Is.EqualTo(0f), "no lock");
            ForceLock(lockOn, h);
            Assert.That(lockOn.TargetRadius, Is.EqualTo(0.5f).Within(1e-4f));
        }

        // ------------------------------------------------------------ with no hurtbox

        [Test]
        public void WithTheHurtboxOff_ItFallsBackToTheRenderers()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            capsule.enabled = false;
            Assert.That(HasHittable(h), Is.False);

            var body = LockOnAim.MeasureFallback(h, LockOnAim.DefaultChestBias, _renderers, out bool measured);
            Assert.That(measured, Is.True);
            Assert.That(body.IsHittable, Is.False);
            Assert.That(body.AimPoint.y, Is.EqualTo(3.2f).Within(1e-3f));
        }

        [Test]
        public void TheFallback_SkipsIgnoredAndEffectRenderers()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            capsule.enabled = false;
            Child(h, PrimitiveType.Cube, Vector3.up * 9f, Vector3.one * 4f).AddComponent<LockOnIgnore>();
            var trail = new GameObject("Trail");
            trail.transform.SetParent(h.transform, false);
            trail.transform.localPosition = Vector3.up * 20f;
            trail.AddComponent<TrailRenderer>();

            var body = LockOnAim.MeasureFallback(h, LockOnAim.DefaultChestBias, _renderers, out _);
            Assert.That(body.Bounds.center.y, Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void WithNothingAtAll_ItAimsAtADefaultChest()
        {
            var h = NewTarget("Empty", new Vector3(1f, 2f, 3f));
            h.BodyOffset = Vector3.up;
            var body = LockOnAim.MeasureFallback(h, LockOnAim.DefaultChestBias, _renderers, out bool measured);
            Assert.That(measured, Is.False);
            Assert.That(Vector3.Distance(body.AimPoint, new Vector3(1f, 4.2f, 3f)), Is.LessThan(1e-3f));
            Assert.That(body.Radius, Is.EqualTo(LockOnAim.DefaultSize).Within(1e-4f));
        }

        [Test]
        public void AHeldLockOnTheFallback_IsFrozen_SoItDoesntBreathe()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            var mesh = capsule.GetComponent<MeshRenderer>();
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            capsule.enabled = false;
            Vector3 frozen = lockOn.AimPoint;

            mesh.transform.localScale = new Vector3(1f, 4f, 1f);            // the animation breathes
            Assert.That(Vector3.Distance(lockOn.AimPoint, frozen), Is.LessThan(1e-4f));

            h.transform.position += Vector3.forward * 2f;                    // but it still moves with her
            h.BodyOffset = Vector3.up;
            Assert.That(Vector3.Distance(lockOn.AimPoint, frozen + Vector3.forward * 2f + Vector3.up), Is.LessThan(1e-3f));
        }

        [Test]
        public void WhenTheHurtboxComesBack_TheAimReturnsToIt()
        {
            var h = NewFloater("Target", Vector3.zero, 3f, out var capsule);
            var lockOn = NewLockOn();
            ForceLock(lockOn, h);
            capsule.enabled = false;
            _ = lockOn.AimPoint;
            capsule.transform.localPosition = Vector3.up * 6f;

            capsule.enabled = true;
            Assert.That(lockOn.AimPoint.y, Is.EqualTo(6.2f).Within(1e-4f));
        }

        // ---------------------------------------------------- Hypatia as she is built

        [Test]
        public void Hypatia_AimsAtHerHurtbox_NotHerDialOrHalo()
        {
            var h = NewTarget("Hypatia", Vector3.zero);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(h.transform, false);
            bodyGo.transform.localPosition = Vector3.up * 1.2f;
            var capsule = bodyGo.AddComponent<CapsuleCollider>();
            capsule.height = 2.5f;
            capsule.radius = 0.34f;
            capsule.center = Vector3.up * 1.25f;
            var dial = Child(h, PrimitiveType.Cube, new Vector3(0f, 6.4f, -1.8f), new Vector3(4f, 4f, 0.2f));
            dial.AddComponent<LockOnIgnore>();
            var gown = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(gown.GetComponent<Collider>());
            gown.transform.SetParent(bodyGo.transform, false);
            gown.transform.localScale = new Vector3(3f, 2f, 3f);

            var body = Measure(h);
            Assert.That(body.Radius, Is.EqualTo(0.34f).Within(1e-3f), "not the wide gown");
            Assert.That(body.AimPoint.y, Is.EqualTo(1.2f + 1.25f + 0.25f).Within(1e-3f));
            Assert.That(body.Bounds.Contains(body.AimPoint), Is.True);
        }
    }
}
