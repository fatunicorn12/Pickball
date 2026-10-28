using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins who the talk prompt is for: lock-on first, never while locked on to
    /// someone who cannot talk, otherwise the nearest one in range and in front,
    /// with hysteresis so the prompt does not flicker.
    /// </summary>
    public class TalkTargetSelectorTests
    {
        static readonly Vector3 Origin = new(0f, 1.3f, 0f);

        static TalkSelectionRules Rules(float range = 2.5f, float cone = 60f, float locked = 4.5f,
                                        float height = 1.5f, float sticky = 0f, float stickyCone = 0f)
            => new()
            {
                TalkRange = range, FacingConeDegrees = cone, LockedTalkRange = locked,
                MaxHeightDifference = height, StickyMargin = sticky, StickyConeDegrees = stickyCone,
            };

        static TalkCandidate At(float x, float z, bool canTalk = true, float y = 1.3f)
            => new(new Vector3(x, y, z), canTalk);

        static int Select(TalkCandidate[] candidates, TalkSelectionRules rules,
                          int locked = -1, bool lockedOnOther = false, int current = -1)
            => TalkTargetSelector.Select(Origin, Vector3.forward, candidates, locked, lockedOnOther, current, rules);

        [Test]
        public void Nearest_InRangeAndInFront_Wins()
        {
            var candidates = new[] { At(0f, 2f), At(0.3f, 1.2f), At(-0.5f, 2.2f) };
            Assert.That(Select(candidates, Rules()), Is.EqualTo(1));
        }

        [Test]
        public void Range_EdgeIsInclusive_AndJustOutsideIsExcluded()
        {
            Assert.That(Select(new[] { At(0f, 2.49f) }, Rules(range: 2.5f)), Is.EqualTo(0));
            Assert.That(Select(new[] { At(0f, 2.51f) }, Rules(range: 2.5f)), Is.EqualTo(-1));
        }

        [Test]
        public void Range_IsMeasuredFlat_HeightIsASeparateCut()
        {
            // 2 m away and 1.4 m higher: flat distance 2 is in range, height 1.4 is under 1.5.
            Assert.That(Select(new[] { At(0f, 2f, y: 2.7f) }, Rules(range: 2.5f, height: 1.5f)), Is.EqualTo(0));
            // On the balcony: 3 m up.
            Assert.That(Select(new[] { At(0f, 2f, y: 4.3f) }, Rules(range: 2.5f, height: 1.5f)), Is.EqualTo(-1));
        }

        [Test]
        public void Cone_InsideShows_OutsideHides_BehindHides()
        {
            var rules = Rules(cone: 45f);
            float r = 2f;
            Vector3 inside = Quaternion.Euler(0f, 44f, 0f) * Vector3.forward * r;
            Vector3 outside = Quaternion.Euler(0f, 46f, 0f) * Vector3.forward * r;

            Assert.That(Select(new[] { At(inside.x, inside.z) }, rules), Is.EqualTo(0));
            Assert.That(Select(new[] { At(outside.x, outside.z) }, rules), Is.EqualTo(-1));
            Assert.That(Select(new[] { At(0f, -1f) }, rules), Is.EqualTo(-1));
        }

        [Test]
        public void Cone_OfOneEightyIgnoresFacing()
        {
            Assert.That(Select(new[] { At(0f, -1f) }, Rules(cone: 180f)), Is.EqualTo(0));
        }

        [Test]
        public void CannotTalk_IsExcluded_EvenWhenNearest()
        {
            var candidates = new[] { At(0f, 0.8f, canTalk: false), At(0f, 2f) };
            Assert.That(Select(candidates, Rules()), Is.EqualTo(1));
        }

        [Test]
        public void LockOn_WinsOverANearerCandidate_EvenOutsideTheCone()
        {
            // The locked one is behind the player and further away than a free one in front.
            var candidates = new[] { At(0f, 1f), At(0f, -3f) };
            Assert.That(Select(candidates, Rules(), locked: 1), Is.EqualTo(1));
        }

        [Test]
        public void LockOn_OutsideLockedRange_FallsBackToWalkUp()
        {
            var candidates = new[] { At(0f, 1f), At(0f, 10f) };
            Assert.That(Select(candidates, Rules(locked: 4.5f), locked: 1), Is.EqualTo(0));
        }

        [Test]
        public void LockOn_OnSomeoneWhoCannotTalk_FallsBackToWalkUp()
        {
            var candidates = new[] { At(0f, 1f), At(0f, 2f, canTalk: false) };
            Assert.That(Select(candidates, Rules(), locked: 1), Is.EqualTo(0));
        }

        [Test]
        public void LockOn_OnANonTalker_TurnsTalkingOff()
        {
            // Locked on to Newton: the NPC right in front gets no prompt, so E stays Dash.
            var candidates = new[] { At(0f, 1f) };
            Assert.That(Select(candidates, Rules(), lockedOnOther: true), Is.EqualTo(-1));
        }

        [Test]
        public void Sticky_CurrentTargetKeepsThePromptPastTheRange()
        {
            var candidates = new[] { At(0f, 2.7f) };
            var rules = Rules(range: 2.5f, sticky: 0.3f);

            Assert.That(Select(candidates, rules), Is.EqualTo(-1), "a new target needs the plain range");
            Assert.That(Select(candidates, rules, current: 0), Is.EqualTo(0), "the current one keeps it");
        }

        [Test]
        public void Sticky_ARivalMustBeCloserByTheMargin()
        {
            var candidates = new[] { At(-0.3f, 2.0f), At(0.3f, 1.9f) };
            var rules = Rules(sticky: 0.3f);

            Assert.That(Select(candidates, rules, current: 0), Is.EqualTo(0), "0.1 m closer is not enough");
            Assert.That(Select(new[] { At(-0.3f, 2.0f), At(0.3f, 1.5f) }, rules, current: 0), Is.EqualTo(1));
        }

        [Test]
        public void Empty_OrNull_SelectsNobody()
        {
            Assert.That(Select(new TalkCandidate[0], Rules()), Is.EqualTo(-1));
            Assert.That(TalkTargetSelector.Select(Origin, Vector3.forward, null, -1, false, -1, Rules()), Is.EqualTo(-1));
        }
    }
}
