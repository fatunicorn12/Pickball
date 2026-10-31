using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>The shot-list notation: frames, units, Pair.Side, paths, aims, focus and the Birdy cap.</summary>
    public class RigSolverTests
    {
        sealed class Markers : IMarkerSource
        {
            public readonly Dictionary<string, (Vector3 p, float yaw)> Map = new Dictionary<string, (Vector3, float)>();
            public bool TryGetMarker(string id, out Vector3 position, out float yaw)
            {
                if (id != null && Map.TryGetValue(id, out var m)) { position = m.p; yaw = m.yaw; return true; }
                position = Vector3.zero; yaw = 0f; return false;
            }
        }

        /// <summary>MC at the origin facing +Z, Birdy 0.65·H in front facing MC; everything scaled by k about the origin.</summary>
        static RigScene Scene(float k = 1f, float mcYaw = 0f)
        {
            float h = 1.7f * k, hb = 0.53f * k;
            var mcRot = Quaternion.Euler(0f, mcYaw, 0f);
            var s = new RigScene
            {
                MC = ActorSample.Standing(Vector3.zero, mcYaw, h, 0.57f * k, 0.9f * h),
                Birdy = ActorSample.Standing(mcRot * new Vector3(0f, 0f, 0.65f * 1.7f * k), mcYaw + 180f, 1.1f * k, hb, 1.36f * k),
                Markers = new Markers(),
            };
            ((Markers)s.Markers).Map["Sun"] = (new Vector3(180f, 130f, 410f) * k, 0f);
            return s;
        }

        static RigPoint H(RigBasis b, RigFrame f, float x, float y, float z) => new RigPoint { basis = b, frame = f, h = new Vector3(x, y, z) };

        [Test]
        public void OffsetsAreInTheActorsYawFrameAndScaleWithH()
        {
            var s = Scene(1f, 90f);   // MC faces +X: his right is -Z
            Vector3 p = RigSolver.Resolve(H(RigBasis.MCFeet, RigFrame.MC, 1f, 0.5f, 2f), s);
            Assert.AreEqual(2f * 1.7f, p.x, 1e-4f);
            Assert.AreEqual(0.5f * 1.7f, p.y, 1e-4f);
            Assert.AreEqual(-1.7f, p.z, 1e-4f);
        }

        [Test]
        public void ShotGeometryScalesWithTheModel()
        {
            var point = new RigPoint { basis = RigBasis.BirdyHead, frame = RigFrame.Birdy, yaw = -50f, hb = new Vector3(0f, 0f, 9f), level = RigBasis.BirdyEyes };
            foreach (float k in new[] { 0.7f, 1.4f })
            {
                var a = Scene(1f);
                var b = Scene(k);
                Vector3 da = RigSolver.Resolve(point, a) - a.Birdy.Head;
                Vector3 db = RigSolver.Resolve(point, b) - b.Birdy.Head;
                Assert.That(Vector3.Distance(da * k, db), Is.LessThan(1e-4f), $"scale {k}");
                // Same composition: Birdy's head box fills the same share of the frame.
                Assert.AreEqual(CameraMath.TcuFraction(a.HB, da.magnitude, 40f), CameraMath.TcuFraction(b.HB, db.magnitude, 40f), 1e-4f);
            }
        }

        [Test]
        public void LevelTakesTheReferenceHeight()
        {
            var s = Scene();
            var p = new RigPoint { basis = RigBasis.BirdyHead, frame = RigFrame.Birdy, hb = new Vector3(0f, 0.2f, 2f), level = RigBasis.MCEyes };
            Assert.AreEqual(s.MC.Eyes.y + 0.2f * s.HB, RigSolver.Resolve(p, s).y, 1e-4f);
        }

        [Test]
        public void PairSideRules()
        {
            var s = Scene();   // MC→Birdy runs north: the perpendiculars are east and west
            Assert.AreEqual(-1f, RigSolver.PairSide(s, SideRule.SunSide).x, 1e-4f, "the south-west sun is on the west side");
            Vector3 left = RigSolver.PairSide(s, SideRule.MCLeft);
            // From that side, looking back at the pair, MC must read left of Birdy.
            var rot = Quaternion.LookRotation(-left);
            float mcX = CameraMath.Viewport(left * 5f + Vector3.up, rot, 40f, 16f / 9f, s.MC.Feet).x;
            float bX = CameraMath.Viewport(left * 5f + Vector3.up, rot, 40f, 16f / 9f, s.Birdy.Feet).x;
            Assert.Less(mcX, bX);
            Assert.AreEqual(-RigSolver.PairSide(s, SideRule.MCRight).x, left.x, 1e-4f);
        }

        [Test]
        public void TowardFrameFacesTheMarker()
        {
            var s = Scene();
            var p = new RigPoint { basis = RigBasis.MCHead, frame = RigFrame.Toward, frameMarker = "Sun", h = new Vector3(0f, 0f, 1f) };
            Vector3 d = RigSolver.Resolve(p, s) - s.MC.Head;
            Assert.AreEqual(CameraMath.YawOf(new Vector3(180f, 0f, 410f)), CameraMath.YawOf(d), 1e-3f);
        }

        [Test]
        public void KeyedPathPushesFromAToBWithItsEase()
        {
            var s = Scene();
            var keys = new[]
            {
                new RigKey { time = TimeRef.Start(), point = H(RigBasis.MCMid, RigFrame.World, 0f, 2.6f, 0f) },
                new RigKey { time = TimeRef.End(), point = H(RigBasis.MCMid, RigFrame.World, 0f, 2.0f, 0f), ease = EaseKind.InOut },
            };
            float mid = s.MC.Mid.y;
            Assert.AreEqual(mid + 2.6f * 1.7f, RigSolver.Keyed(keys, null, 6.3f, 0f, s).y, 1e-4f);
            Assert.AreEqual(mid + 2.3f * 1.7f, RigSolver.Keyed(keys, null, 6.3f, 3.15f, s).y, 1e-4f);
            Assert.AreEqual(mid + 2.0f * 1.7f, RigSolver.Keyed(keys, null, 6.3f, 6.3f, s).y, 1e-4f);
        }

        [Test]
        public void CraneHoldsBeforeItsFirstKeyAndPassesThroughTheKnots()
        {
            var s = Scene();
            var beats = new[] { 0f, 3f, 4f };
            var keys = new[]
            {
                new RigKey { time = TimeRef.Beat(1), point = H(RigBasis.MCFeet, RigFrame.MC, 0f, 1f, 1f) },
                new RigKey { time = TimeRef.Beat(1, 1f), point = H(RigBasis.MCFeet, RigFrame.MC, 1f, 1f, 0f) },
                new RigKey { time = TimeRef.Beat(1, 2f), point = H(RigBasis.MCFeet, RigFrame.MC, 0f, 1f, -1f) },
            };
            Vector3 k0 = RigSolver.Resolve(keys[0].point, s), k1 = RigSolver.Resolve(keys[1].point, s), k2 = RigSolver.Resolve(keys[2].point, s);
            Assert.That(Vector3.Distance(k0, RigSolver.Crane(keys, beats, 6f, 1f, s, EaseKind.InOut)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(k1, RigSolver.Crane(keys, beats, 6f, 4f, s, EaseKind.Linear)), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(k2, RigSolver.Crane(keys, beats, 6f, 6f, s, EaseKind.InOut)), Is.LessThan(1e-4f));
        }

        [Test]
        public void AimHandsFromOneTargetToAnother()
        {
            var s = Scene();
            var keys = new[]
            {
                new AimKey { time = TimeRef.Start(), target = RigPoint.At(RigBasis.MCHead), screen = new Vector2(0.5f, 0.4f) },
                new AimKey { time = TimeRef.Start(2f), target = RigPoint.At(RigBasis.BirdyHead), screen = new Vector2(0.5f, 0.6f), ease = EaseKind.Linear },
            };
            RigSolver.Aim(keys, null, 4f, 1f, s, out var target, out var screen);
            Assert.That(Vector3.Distance((s.MC.Head + s.Birdy.Head) * 0.5f, target), Is.LessThan(1e-4f));
            Assert.AreEqual(0.5f, screen.y, 1e-4f);
            RigSolver.Aim(keys, null, 4f, 3f, s, out target, out screen);
            Assert.That(Vector3.Distance(s.Birdy.Head, target), Is.LessThan(1e-4f));
        }

        [Test]
        public void FocusPullEasesInDioptres()
        {
            var s = Scene();
            var cam = s.MC.Head + Vector3.back;
            var keys = new[]
            {
                new FocusKey { time = TimeRef.Start(), target = RigPoint.At(RigBasis.MCHead) },
                new FocusKey { time = TimeRef.Start(2f), target = RigPoint.AtMarker("Sun"), ease = EaseKind.Linear },
            };
            float near = RigSolver.Focus(keys, null, 3f, 0f, s, cam);
            float far = RigSolver.Focus(keys, null, 3f, 3f, s, cam);
            float mid = RigSolver.Focus(keys, null, 3f, 1f, s, cam);
            Assert.AreEqual(1f, near, 1e-4f);
            Assert.AreEqual(1f / Mathf.Lerp(1f / near, 1f / far, 0.5f), mid, 1e-3f);
        }

        [Test]
        public void BirdyCapBacksTheCameraOffToTheCap()
        {
            var s = Scene();
            Vector3 close = s.Birdy.Head + new Vector3(0.3f, 0f, -0.8f);
            Vector3 capped = RigSolver.CapBirdyFraction(close, s, 0.2f, 40f, s.Birdy.Head);
            float depth = Vector3.Distance(capped, s.Birdy.Head);
            Assert.LessOrEqual(CameraMath.TcuFraction(s.HB, depth, 40f), 0.2f + 1e-4f);
            Vector3 far = s.Birdy.Head + new Vector3(0f, 0f, -20f);
            Assert.AreEqual(far, RigSolver.CapBirdyFraction(far, s, 0.2f, 40f, s.Birdy.Head));
        }

        [Test]
        public void TopDownAxisFollowsTheBodyOrHisFacing()
        {
            var s = Scene(1f, 90f);
            Assert.That(Vector3.Distance(Vector3.right, RigSolver.BodyAxis(s)), Is.LessThan(1e-4f), "standing: his forward");
            s.MC.Head = s.MC.Feet + new Vector3(0f, 0.2f, 1.4f);   // lying, head north
            Assert.That(Vector3.Distance(Vector3.forward, RigSolver.BodyAxis(s)), Is.LessThan(1e-4f));
        }

    }
}
