using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Actors;
using UnityEditor;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>
    /// Every verb in the shot list's §6 tables is either mapped to a clip in the character's
    /// action library or on the documented missing list, and the verb spelling rule holds.
    /// </summary>
    public class ActionLibraryCoverageTests
    {
        [Test]
        public void VerbCell_StripsParentheticalsAndSplitsOnSlash()
        {
            CollectionAssert.AreEqual(new[] { "idle stand", "idle look" },
                CutsceneVerbs.FromCell("idle stand / idle look (breathing, cloak flutter)"));
            CollectionAssert.AreEqual(new[] { "crash tumble and bounce, scramble up" },
                CutsceneVerbs.FromCell("crash tumble and bounce, scramble up"));
            CollectionAssert.AreEqual(new[] { "point at mc" }, CutsceneVerbs.FromCell("point at MC"));
            CollectionAssert.AreEqual(new[] { "reach out" },
                CutsceneVerbs.FromCell("reach out (arm extended, with \"poke air\" additive and a hold)"));
        }

        [Test]
        public void ShotListParser_ReadsOnlyTheNamedSixTable()
        {
            const string md = "## 5. Summary\n| Action | Shots |\n| x | y |\n## 6. Animation needs\n### MC\n| Action | Shots |\n" +
                              "|---|---|\n| sit up (slow) | S1 |\n| turn | S1 |\n### Birdy\n| Action | Shots |\n|---|---|\n| hover idle (rotor loop) | x |\n## 7. Next\n";
            CollectionAssert.AreEqual(new[] { "sit up", "turn" }, CutsceneVerbs.FromShotList(md, "MC"));
            CollectionAssert.AreEqual(new[] { "hover idle" }, CutsceneVerbs.FromShotList(md, "Birdy"));
        }

        [Test, Category("Assets")]
        public void McLibrary_CoversEveryShotListVerb()
        {
            Check("MC", CutsceneActorPaths.McLibrary, CutsceneSpeakers.MC, CutsceneClipCatalog.McMissing);
        }

        [Test, Category("Assets")]
        public void BirdyLibrary_CoversEveryShotListVerb()
        {
            Check("Birdy", CutsceneActorPaths.BirdyLibrary, CutsceneSpeakers.Birdy, CutsceneClipCatalog.BirdyMissing);
        }

        private static void Check(string character, string path, string id, string[] missing)
        {
            List<string> verbs = CutsceneVerbs.FromRepo(character);
            Assert.Greater(verbs.Count, 10, $"no {character} verbs read from {CutsceneActorPaths.ShotList}");
            var lib = AssetDatabase.LoadAssetAtPath<CutsceneActionLibrary>(path);
            Assert.NotNull(lib, $"{path} missing: run 1 Build Actors");
            Assert.AreEqual(id, lib.ActorId);
            Assert.IsTrue(lib.TryGet(lib.IdleVerb, out _), "the idle fallback verb has no clip");

            var uncovered = verbs.Where(v => !lib.TryGet(v, out _) && !missing.Contains(v)).ToList();
            Assert.IsEmpty(uncovered, $"{character} verbs neither mapped nor on the missing list: {string.Join(", ", uncovered)}");

            for (int i = 0; i < lib.Count; i++)
            {
                var e = lib.Get(i);
                Assert.NotNull(e.clip, $"'{e.verb}' has no clip");
                Assert.IsTrue(e.clip.isHumanMotion, $"'{e.verb}' ({e.clip.name}) is not a Humanoid clip");
                Assert.AreEqual(e.verb, e.verb.ToLowerInvariant(), "verbs are lower case");
            }
        }
    }
}
