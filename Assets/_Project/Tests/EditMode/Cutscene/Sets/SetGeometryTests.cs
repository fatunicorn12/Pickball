using NUnit.Framework;
using Pickleball.Cutscene.Editor.Sets;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Sets
{
    /// <summary>
    /// The sets' geometry against the movement kit and the greybox: the ledge is reachable with
    /// single jumps, the walk up the path and down the ridge stays under the controller's slope
    /// limit, markers stand on the ground, the island keeps clear of the plateau and the Delta
    /// islets, and the crest sees the gnomon, the rim and the black sun over the stoa.
    /// </summary>
    public class SetGeometryTests
    {
        private const string MovementConfigPath = "Assets/_Project/ScriptableObjects/Movement/MovementConfig.asset";

        // The chibi's CharacterController (Prefabs/Chibi.prefab): slope limit 50°.
        private const float SlopeLimit = 50f;

        private static float MovementValue(string field)
        {
            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MovementConfigPath);
            Assert.IsNotNull(config, $"{MovementConfigPath} not found.");
            SerializedProperty p = new SerializedObject(config).FindProperty(field);
            Assert.IsNotNull(p, $"MovementConfig has no {field}.");
            return p.floatValue;
        }

        [Test]
        public void EachLedgeStepRiseIsWithinASingleJump()
        {
            // A jump must clear the edge with margin, as the greybox's 1.25 m crates do.
            float single = MovementValue("singleJumpHeight");
            const float margin = 0.5f;
            float previous = 0f;
            foreach (Vector3 step in SetLayout.LedgeSteps)
            {
                Assert.LessOrEqual(step.y - previous, single - margin, $"Step at {step} rises {step.y - previous} m.");
                previous = step.y;
            }
            Assert.LessOrEqual(SetLayout.LedgeTop - previous, single - margin, "The last rise onto the ledge.");
            Assert.LessOrEqual(SetLayout.LedgeTop - previous, 2f, "Main-route steps are 2 m or less (TheAcademy.md §7).");
        }

        [Test]
        public void TheStepsTouchEachOtherAndTheLedge()
        {
            Vector3[] s = SetLayout.LedgeSteps;
            for (int i = 0; i + 1 < s.Length; i++)
            {
                float gap = Mathf.Abs(s[i + 1].x - s[i].x) - SetLayout.StepSize.x;
                Assert.LessOrEqual(gap, 0.01f, $"Gap between steps {i + 1} and {i + 2}.");
            }
            float toPier = Mathf.Abs(s[2].z - SetLayout.PierCentre.y) - (SetLayout.StepSize.y + SetLayout.PierSize.y) * 0.5f;
            Assert.LessOrEqual(toPier, 0.01f, "The top step abuts the pier.");
            Assert.LessOrEqual(Mathf.Abs(s[2].x - SetLayout.PierCentre.x), (SetLayout.PierSize.x - SetLayout.StepSize.x) * 0.5f + 1e-3f,
                               "The top step lies along the pier's face.");
        }

        [Test]
        public void TheWalkUpThePathAndDownTheRidgeStaysUnderTheSlopeLimit()
        {
            foreach (var route in new[] { SetLayout.PathPoints, SetLayout.DescentPoints })
            {
                foreach (Vector2 p in PropMeshes.SmoothPolyline(route, 2f, 0.25f))
                {
                    float slope = SouthIslandTerrain.SlopeDegrees(p.x, p.y);
                    Assert.Less(slope, SlopeLimit - 10f, $"Slope {slope:0.0}° at {p}.");
                }
            }
            Vector2 foot = SetLayout.DescentPoints[SetLayout.DescentPoints.Length - 1];
            Assert.AreEqual(GreyboxReference.ParapetTop, SouthIslandTerrain.Height(foot.x, foot.y), 0.3f,
                            "The ridge foot meets the parapet top within a controller step.");
        }

        [Test]
        public void WalkersAndMarksStandOnTheGround()
        {
            foreach (Vector2 xz in new[] { SetLayout.PathStartXZ, SetLayout.PathTrackAXZ, SetLayout.PathTrackBXZ, SetLayout.PathEndXZ, SetLayout.CrestViewXZ })
                Assert.Less(SouthIslandTerrain.SignedDistance(xz.x, xz.y), -1f, $"{xz} is off the island.");
            Assert.AreEqual(SetLayout.CrestHeight, SouthIslandTerrain.Height(SetLayout.CrestViewXZ.x, SetLayout.CrestViewXZ.y), 0.05f);
            Assert.AreEqual(0f, SouthIslandTerrain.Height(SetLayout.ShrineCentre.x, SetLayout.ShrineCentre.z), 1e-4f, "The shrine is flat.");
            Assert.AreEqual(0f, SouthIslandTerrain.Height(SetLayout.MCHandover.x, SetLayout.MCHandover.z), 1e-4f);
            foreach (Vector3 step in SetLayout.LedgeSteps)
                Assert.AreEqual(0f, SouthIslandTerrain.Height(step.x, step.z), 1e-4f, "Steps stand on flat meadow.");
        }

        [Test]
        public void TheIslandKeepsClearOfThePlateauAndTheDeltaIslets()
        {
            // Sample the outline's interior; nothing may lie over the plateau or an islet.
            for (float x = -20f; x <= 28f; x += 0.25f)
            for (float z = -106f; z <= -49f; z += 0.25f)
            {
                if (SouthIslandTerrain.SignedDistance(x, z) > 0f) continue;
                Assert.Less(z, GreyboxReference.ParapetSouthZ, $"({x}, {z}) is over the plateau or its parapet.");
                foreach (var (x0, x1, z0, z1) in GreyboxReference.DeltaIslets)
                    Assert.IsFalse(x > x0 && x < x1 && z > z0 && z < z1, $"({x}, {z}) is over a Delta islet.");
            }
        }

        [Test]
        public void TheCrestSeesTheGnomonTheRimAndTheBlackSun()
        {
            var occluders = GreyboxReference.Occluders();
            Vector3 eye = SetLayout.Ground(SetLayout.CrestViewXZ) + Vector3.up * SetVerification.EyeHeight;

            Vector3 toGnomon = SetLayout.GnomonAim - eye;
            Vector3 justShort = eye + toGnomon * ((toGnomon.z - 1.5f) / toGnomon.z);   // stop before its box
            Assert.IsNull(SightlineMath.FirstBlocker(eye, justShort, occluders), "Gnomon_Aim is hidden.");
            Assert.IsNull(SightlineMath.FirstBlocker(eye, SetLayout.RimGaze, occluders), "Rim_Gaze is hidden.");
            Assert.IsNull(SightlineMath.FirstBlocker(eye, SetLayout.BlackSun, occluders), "The black sun is hidden.");
            foreach (float x in new[] { -25f, -10f, 10f, 25f })
                Assert.IsNull(SightlineMath.FirstBlocker(eye, new Vector3(x, 0.95f, 59.5f), occluders),
                              $"The north rim's parapet at x {x} is hidden.");
        }

        [Test]
        public void TheShotListsOldRimGazeWasBehindTheBellTower()
        {
            // Documents why Rim_Gaze moved: from the crest, (30, 8, 64) sits behind Birdy's bell tower.
            var occluders = GreyboxReference.Occluders();
            Vector3 eye = SetLayout.Ground(SetLayout.CrestViewXZ) + Vector3.up * SetVerification.EyeHeight;
            Assert.AreEqual("BellTower", SightlineMath.FirstBlocker(eye, new Vector3(30f, 8f, 64f), occluders));
        }

        [Test]
        public void TheBlackSunMatchesTheShotListBearingAndElevation()
        {
            Vector3 eye = SetLayout.Ground(SetLayout.CrestViewXZ);
            Vector3 d = SetLayout.BlackSun - eye;
            float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            Assert.AreEqual(21.9f, bearing, 0.3f);
            Assert.AreEqual(13.5f, elevation, 0.5f);
        }
    }
}
