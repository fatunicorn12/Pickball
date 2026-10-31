using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Pipeline;
using Pickleball.Cutscene.Playback;
using UnityEditor;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The data-driven set list menu 4 places the sets from (the set swap point).</summary>
    public class SetListTests
    {
        [Test]
        public void ConfigureReplacesTheListAndNullBecomesEmpty()
        {
            var list = UnityEngine.ScriptableObject.CreateInstance<CutsceneSetList>();
            try
            {
                list.Configure(null);
                Assert.That(list.Sets, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(list);
            }
        }

        [Test, Category("Assets")]
        public void TheSetListPlacesEverySetPrefab()
        {
            var list = AssetDatabase.LoadAssetAtPath<CutsceneSetList>(PipelinePaths.SetList);
            Assert.That(list, Is.Not.Null, PipelinePaths.SetList + " is missing: run Build Opening Scene.");
            var ids = list.Sets.Where(p => p != null).Select(p => p.GetComponent<CutsceneSet>()?.SetId).ToArray();
            Assert.That(ids, Is.EqualTo(PipelinePaths.SetIds));
        }
    }
}
