using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Shared scaffolding for the boss EditMode suites.
    ///
    /// The systems under test are pure arithmetic, but they live on
    /// MonoBehaviours, and a MonoBehaviour outside Play mode is an awkward
    /// subject: Unity never runs <c>Awake</c>, <c>Start</c> or <c>Update</c> for
    /// it, and its tunables are private serialized fields with no public setters.
    /// Rather than widen the production API to suit the tests — which would put
    /// pressure on the design from the wrong direction — the fixture works with
    /// the component as authored:
    ///
    /// <list type="bullet">
    /// <item>Objects are spawned <see cref="HideFlags.DontSave"/> so a suite run
    /// never dirties whatever scene the author happens to have open. Note that the
    /// stronger <c>HideAndDontSave</c> would be wrong here: it folds in
    /// <c>NotEditable</c>, which is exactly the flag that stops the Inspector — and
    /// therefore <see cref="SerializedObject"/> — writing to the object.</item>
    /// <item>Tunables are written through <see cref="SerializedObject"/>, which is
    /// the same path the Inspector uses. The cost is that the tests know field
    /// names; <see cref="SetTunable"/> fails loudly with the name it could not
    /// find, so a rename surfaces as a clear message rather than a wrong
    /// number.</item>
    /// <item>Anything that would normally be established by a lifecycle callback is
    /// re-established explicitly by each suite, so a test never depends on whether
    /// the editor happened to tick the object.</item>
    /// </list>
    /// </summary>
    public abstract class BossTestFixture
    {
        readonly List<GameObject> _spawned = new();

        /// <summary>Spawn a throwaway host object carrying one component.</summary>
        protected T NewComponent<T>() where T : Component
        {
            var host = new GameObject($"Test_{typeof(T).Name}")
            {
                hideFlags = HideFlags.DontSave
            };
            _spawned.Add(host);
            return host.AddComponent<T>();
        }

        [TearDown]
        public void DestroySpawnedObjects()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null)
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);

            _spawned.Clear();
        }

        /// <summary>Write a private <c>[SerializeField]</c> float, as the Inspector
        /// would. Tests set every value they depend on rather than inheriting the
        /// authored defaults, so tuning the game can never silently rewrite what a
        /// test claims to prove.</summary>
        protected static void SetTunable(Component target, string fieldName, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null,
                        $"{target.GetType().Name} has no serialized field '{fieldName}' — " +
                        "it was probably renamed. Update the test's field name.");
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Run one frame of a component's private <c>Update</c>.
        ///
        /// EditMode gives us no game loop, and some state a formula depends on is
        /// only established by ticking — <see cref="Pickleball.Sandbox.GravityWell.Intensity"/>
        /// is the case that forces this. Calling the real method keeps the test
        /// honest about how that state actually comes to exist; the alternative,
        /// poking a backing field, would let the test pass even if the production
        /// ramp were broken. Callers must first put the component in a
        /// configuration where a tick is side-effect free.</summary>
        protected static void Tick(MonoBehaviour behaviour)
        {
            MethodInfo update = behaviour.GetType()
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(update, Is.Not.Null,
                        $"{behaviour.GetType().Name}.Update() not found — the test harness " +
                        "drives it manually because EditMode has no game loop.");

            update.Invoke(behaviour, null);
        }
    }
}
