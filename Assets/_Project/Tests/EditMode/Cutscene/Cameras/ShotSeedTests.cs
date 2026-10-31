using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using Pickleball.Cutscene.Cameras.Editor;
using Pickleball.Cutscene.Timeline.Plan;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>The seeded shot table matches the shot list and the timing plan.</summary>
    public class ShotSeedTests
    {
        static readonly HashSet<string> TcuShots = new HashSet<string> { "S6_SH05", "S6_SH08", "S6_SH10", "S6_SH15" };

        [Test]
        public void OneSpecPerPlannedShotInOrderWithTheSameLens()
        {
            var seed = OpeningShotSeed.Build();
            var plan = new List<ShotPlan>();
            for (int s = 1; s <= OpeningTimingPlan.SceneCount; s++) plan.AddRange(OpeningTimingPlan.Scene(s));
            Assert.AreEqual(52, seed.Count);
            Assert.AreEqual(plan.Count, seed.Count);
            for (int i = 0; i < seed.Count; i++)
            {
                Assert.AreEqual(plan[i].Id, seed[i].shotId);
                Assert.AreEqual(plan[i].LensStart, seed[i].lensStart, 1e-4f, plan[i].Id);
                Assert.AreEqual(plan[i].LensEnd, seed[i].lensEnd, 1e-4f, plan[i].Id);
            }
        }

        [Test]
        public void EveryShotHasASubjectAndAPositionAndTimesInsideItsBeats()
        {
            var plan = new Dictionary<string, ShotPlan>();
            for (int s = 1; s <= OpeningTimingPlan.SceneCount; s++)
                foreach (var p in OpeningTimingPlan.Scene(s)) plan[p.Id] = p;
            foreach (var spec in OpeningShotSeed.Build())
            {
                Assert.Greater(spec.subjects.Length, 0, spec.shotId);
                if (spec.body != BodyKind.Gameplay && spec.body != BodyKind.Track)
                    Assert.Greater(spec.path.Length, 0, spec.shotId);
                int beats = plan[spec.shotId].Beats.Length;
                foreach (var k in spec.path) AssertBeat(k.time, beats, spec.shotId);
                foreach (var k in spec.aimKeys) AssertBeat(k.time, beats, spec.shotId);
                foreach (var b in spec.bursts) AssertBeat(b.at, beats, spec.shotId);
            }
        }

        [Test]
        public void TightCloseUpsAreAllowedOnlyInTheFourScene6Pauses()
        {
            foreach (var spec in OpeningShotSeed.Build())
                Assert.AreEqual(TcuShots.Contains(spec.shotId), spec.tcuAllowed.Length > 0, spec.shotId);
        }

        [Test]
        public void DepthOfFieldOnlyWhereTheShotListSaysAndTheLastShotIsPoseG()
        {
            var dof = new HashSet<string> { "S6_SH01", "S6_SH04", "S6_SH05", "S6_SH08", "S6_SH10", "S6_SH15" };
            var seed = OpeningShotSeed.Build();
            foreach (var spec in seed) Assert.AreEqual(dof.Contains(spec.shotId), spec.aperture > 0f, spec.shotId);
            var last = seed[seed.Count - 1];
            Assert.AreEqual("S6_SH16", last.shotId);
            // Lead decision (review round 2): S6 ends behind MC with the black sun in frame and blends
            // 2.0 s into the orbit camera, so the last shot is keyed and aimed at the sun, not pose G.
            Assert.AreNotEqual(BodyKind.Gameplay, last.body);
            Assert.AreEqual(AimMode.Composer, last.aim);
        }

        static void AssertBeat(TimeRef t, int beats, string id)
        {
            Assert.That(t.beat == TimeRef.ShotStart || t.beat == TimeRef.ShotEnd || (t.beat >= 0 && t.beat < beats),
                $"{id}: beat {t.beat} outside its {beats} beats");
        }
    }
}
