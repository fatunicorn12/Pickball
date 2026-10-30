using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Pulse
{
    /// <summary>
    /// The knockback pulse's numbers: who it reaches (horizontally, any height),
    /// the push speed that slides a grounded player to the target radius against
    /// the movement's linear momentum drag, and a safe direction when the player
    /// stands on top of her.
    /// </summary>
    public class PulseMathTests
    {
        const float Drag = 11f;
        const float Target = 10.5f;
        const float Reach = 10f;

        [Test]
        public void PushSpeed_SlidesExactlyToTheTargetRadius()
        {
            foreach (float d in new[] { 0f, 1f, 3f, 6.5f, 9.9f })
            {
                float v = PulseMath.PushSpeed(d, Target, Drag, 0f);
                Assert.That(v, Is.EqualTo(Mathf.Sqrt(2f * Drag * (Target - d))).Within(1e-4f));
                Assert.That(d + PulseMath.SlideDistance(v, Drag), Is.EqualTo(Target).Within(1e-3f), $"from {d} m");
            }
        }

        [Test]
        public void PushSpeed_AtThreeMetres_IsAbout12_8()
        {
            // The design case: standing 3 m out, pushed to 10.5 m.
            Assert.That(PulseMath.PushSpeed(3f, Target, Drag, 0f), Is.EqualTo(12.845f).Within(0.01f));
        }

        [Test]
        public void PushSpeed_AddsTheExtra_AndNeverGoesNegativePastTheTarget()
        {
            Assert.That(PulseMath.PushSpeed(3f, Target, Drag, 2f),
                        Is.EqualTo(PulseMath.PushSpeed(3f, Target, Drag, 0f) + 2f).Within(1e-4f));
            Assert.That(PulseMath.PushSpeed(12f, Target, Drag, 0f), Is.EqualTo(0f));
            Assert.That(PulseMath.PushSpeed(12f, Target, Drag, 1.5f), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void NothingHappens_AtOrBeyondReach()
        {
            Vector3 her = Vector3.zero;
            Assert.IsFalse(PulseMath.TryComputePush(her, new Vector3(12f, 0f, 0f), Vector3.forward, Vector3.forward,
                                                    Reach, Target, Drag, 0f, out Vector3 push));
            Assert.That(push, Is.EqualTo(Vector3.zero));
            Assert.IsFalse(PulseMath.TryComputePush(her, new Vector3(0f, 0f, Reach), Vector3.forward, Vector3.forward,
                                                    Reach, Target, Drag, 0f, out _), "exactly at reach is outside");
        }

        [Test]
        public void ReachIsHorizontal_HeightNeverMatters()
        {
            // 3 m out but 9 m up (a full combo): still caught, pushed flat.
            Assert.IsTrue(PulseMath.TryComputePush(Vector3.zero, new Vector3(3f, 9f, 0f), Vector3.forward,
                                                   Vector3.forward, Reach, Target, Drag, 0f, out Vector3 push));
            Assert.That(push.y, Is.EqualTo(0f));
            Assert.That(push.x, Is.EqualTo(PulseMath.PushSpeed(3f, Target, Drag, 0f)).Within(1e-4f));
            Assert.That(PulseMath.HorizontalDistance(Vector3.zero, new Vector3(3f, 9f, 4f)), Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void Direction_IsStraightAwayFromHer()
        {
            Vector3 dir = PulseMath.PushDirection(new Vector3(1f, 2f, 1f), new Vector3(4f, 0f, 5f),
                                                  Vector3.forward, Vector3.forward);
            Assert.That((dir - new Vector3(0.6f, 0f, 0.8f)).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Direction_OnTopOfHer_FallsBackToThePlayersBack_ThenHerForward()
        {
            Vector3 her = new(2f, 0f, 2f);
            Vector3 back = PulseMath.PushDirection(her, her + Vector3.up * 3f, new Vector3(0f, 0.5f, 1f), Vector3.right);
            Assert.That((back - Vector3.back).magnitude, Is.LessThan(1e-5f), "the player's back");

            Vector3 forward = PulseMath.PushDirection(her, her, Vector3.up, Vector3.right);
            Assert.That((forward - Vector3.right).magnitude, Is.LessThan(1e-5f), "her forward when theirs is vertical");

            Vector3 last = PulseMath.PushDirection(her, her, Vector3.zero, Vector3.zero);
            Assert.That(last, Is.EqualTo(Vector3.forward), "never zero");

            Assert.IsTrue(PulseMath.TryComputePush(her, her, Vector3.forward, Vector3.right, Reach, Target, Drag, 0f,
                                                   out Vector3 push));
            Assert.That(push.magnitude, Is.EqualTo(PulseMath.PushSpeed(0f, Target, Drag, 0f)).Within(1e-4f));
            Assert.That(float.IsNaN(push.x) || float.IsNaN(push.z), Is.False);
        }
    }
}
