using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Movement;
using UnityEditor;

namespace Pickleball.Tests.Hypatia.Pulse
{
    /// <summary>
    /// The pulse solves its push against a COPY of the movement's ground drag
    /// (HypatiaConfig ▸ Pulse Assumed Ground Drag = MovementConfig ▸ Momentum
    /// Ground Drag): the boss must not reach into movement's config at runtime.
    /// This fails loudly the day someone tunes one and not the other, which would
    /// otherwise silently land pulsed players short of (or past) the target radius.
    /// </summary>
    public class PulseDragCouplingTests
    {
        static T LoadOnly<T>(string filter) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            Assert.That(guids.Length, Is.GreaterThan(0), $"No asset matches '{filter}'.");
            T found = null;
            foreach (string guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                Assert.That(found, Is.Null, $"More than one '{filter}' asset; this test needs updating to pick one.");
                found = asset;
            }
            Assert.That(found, Is.Not.Null, $"'{filter}' assets found but none loaded as {typeof(T).Name}.");
            return found;
        }

        [Test]
        public void PulseAssumedGroundDrag_MatchesTheMovementConfig()
        {
            var movement = LoadOnly<MovementConfig>("t:MovementConfig");
            var hypatia = AssetDatabase.LoadAssetAtPath<HypatiaConfig>(
                "Assets/_Project/ScriptableObjects/Hypatia/HypatiaConfig.asset");
            Assert.That(hypatia, Is.Not.Null, "HypatiaConfig.asset not found; run Setup Hypatia Fight.");
            Assert.That(hypatia.PulseAssumedGroundDrag, Is.EqualTo(movement.MomentumGroundDrag).Within(1e-4f),
                        "HypatiaConfig ▸ Pulse Assumed Ground Drag must equal MovementConfig ▸ Momentum Ground " +
                        "Drag (see Docs/Systems/HypatiaBoss.md, coupling). Change both together.");
        }
    }
}
