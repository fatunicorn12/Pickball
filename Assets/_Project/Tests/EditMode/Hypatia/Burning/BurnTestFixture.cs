using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// A Hypatia fight reduced to what the burn reads: a config with every
    /// burn and scroll number set by the test (never inherited from the
    /// authored defaults), an arena centre at the origin, a player with a
    /// 100-point Health that banks all recoverable damage, and an
    /// <see cref="ArenaBurn"/> bound to a context whose boss clock the test
    /// controls through <see cref="BossScale"/>.
    /// </summary>
    public abstract class BurnTestFixture : BossTestFixture
    {
        protected static readonly float[] Radii = { 6f, 8.33f, 10.67f, 13f };
        protected const float Lead = 2f, FireDps = 8f, FireHeight = 1.2f;

        readonly List<Object> _assets = new();

        /// <summary>Her slow multiplier, read live by the context.</summary>
        protected float BossScale = 1f;

        protected HypatiaConfig Config;
        protected ArenaBurn Burn;
        protected Health PlayerHealth;
        protected Transform Player;
        protected HypatiaContext Context;

        [SetUp]
        public void BuildFight()
        {
            BossScale = 1f;
            Config = ScriptableObject.CreateInstance<HypatiaConfig>();
            Config.hideFlags = HideFlags.DontSave;
            _assets.Add(Config);

            SetConfigFloat("arenaRadius", 13f);
            SetConfigFloats("bandOuterRadii", Radii);
            SetConfigFloat("burnWarningLead", Lead);
            SetConfigFloat("minSafeRadius", 6f);
            SetConfigFloat("fireDamagePerSecond", FireDps);
            SetConfigFloat("fireHeight", FireHeight);
            SetConfigBool("scrollsEnabled", true);
            SetConfigFloat("scrollInterval", 4f);
            SetConfigInt("maxLiveScrolls", 2);
            SetConfigFloat("scrollDriftSpeed", 0.8f);
            SetConfigFloat("scrollLifetime", 14f);
            SetConfigFloat("scrollHeight", 0.6f);
            SetConfigFloat("scrollPickupRadius", 0.8f);
            SetConfigFloat("scrollHealthCost", 6f);

            Burn = NewComponent<ArenaBurn>();
            var centre = new GameObject("Test_ArenaCentre") { hideFlags = HideFlags.DontSave }.transform;
            centre.SetParent(Burn.transform, false); // destroyed with it

            PlayerHealth = NewComponent<Health>();
            SetTunable(PlayerHealth, "maxHealth", 100f);
            SetTunable(PlayerHealth, "regainShare", 1f);
            SetTunable(PlayerHealth, "hitGraceSeconds", 0f);
            PlayerHealth.ResetToFull(); // Awake does not run in EditMode
            Player = PlayerHealth.transform;
            Player.position = Vector3.zero; // in the hub

            Context = new HypatiaContext(Config, Burn.transform, centre, Player, PlayerHealth, null,
                                         () => BossScale);
            Burn.Bind(Context);
        }

        [TearDown]
        public void DestroyAssets()
        {
            foreach (var asset in _assets)
                if (asset != null) Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        /// <summary>Smoulder the next band and let it catch (at full speed,
        /// whatever <see cref="BossScale"/> is). Returns the band.</summary>
        protected int BurnNextBand()
        {
            int before = Burn.BurnedBands;
            Burn.IgniteNext();
            float scale = BossScale;
            BossScale = 1f;
            Burn.Tick(Lead + 0.01f);
            BossScale = scale;
            Assert.That(Burn.BurnedBands, Is.EqualTo(before + 1), "a band should have caught");
            for (int i = 0; i < Burn.BandCount; i++) // the innermost burned band is the new one
                if (Burn.GetBandState(i) == BandState.Burned) return i;
            return -1;
        }

        protected void SetConfigFloat(string field, float value)
        {
            var so = Serialized(field, out var p);
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected void SetConfigInt(string field, int value)
        {
            var so = Serialized(field, out var p);
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected void SetConfigBool(string field, bool value)
        {
            var so = Serialized(field, out var p);
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected void SetConfigFloats(string field, float[] values)
        {
            var so = Serialized(field, out var p);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).floatValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject Serialized(string field, out SerializedProperty property)
        {
            var so = new SerializedObject(Config);
            property = so.FindProperty(field);
            Assert.That(property, Is.Not.Null,
                        $"HypatiaConfig has no serialized field '{field}' — it was probably renamed. " +
                        "Update the test.");
            return so;
        }
    }
}
