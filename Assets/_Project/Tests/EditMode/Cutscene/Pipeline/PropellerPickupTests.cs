using NUnit.Framework;
using Pickleball.Cutscene.Hooks;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The pickup fires once, and only for the player.</summary>
    public class PropellerPickupTests
    {
        GameObject _player, _child, _other, _pickupHost;

        [SetUp]
        public void Make()
        {
            _player = new GameObject("Player") { hideFlags = HideFlags.DontSave };
            _child = new GameObject("Body") { hideFlags = HideFlags.DontSave };
            _child.transform.SetParent(_player.transform);
            _other = new GameObject("Birdy") { hideFlags = HideFlags.DontSave };
            _pickupHost = new GameObject("Pickup") { hideFlags = HideFlags.DontSave };
            _pickupHost.AddComponent<BoxCollider>().isTrigger = true;
        }

        [TearDown]
        public void Clean()
        {
            Object.DestroyImmediate(_pickupHost);
            Object.DestroyImmediate(_other);
            Object.DestroyImmediate(_player);
        }

        [Test]
        public void FiresOnceForThePlayerOnly()
        {
            var pickup = _pickupHost.AddComponent<PropellerPickup>();
            pickup.Configure(_player.transform);
            int fired = 0;
            pickup.PickedUp += () => fired++;

            Assert.That(pickup.TryPickUp(_other.transform), Is.False);
            Assert.That(fired, Is.EqualTo(0));
            Assert.That(pickup.TryPickUp(_child.transform), Is.True, "a collider on a child of the player counts");
            Assert.That(pickup.TryPickUp(_player.transform), Is.False);
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(pickup.HasFired, Is.True);
        }
    }
}
