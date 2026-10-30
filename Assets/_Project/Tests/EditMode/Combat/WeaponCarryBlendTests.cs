using NUnit.Framework;
using Pickleball.Sandbox.Combat.Presentation;

namespace Pickleball.Tests
{
    /// <summary>
    /// The rules that fade a weapon's carry pose:
    ///  - in on equipping a weapon with a carry, out on one without, over its blend time;
    ///  - never moved by a swing (the swing blends from and back to the carry);
    ///  - out of the way, fast, during a move that uses the arm, and back after;
    ///  - the same fade at any frame rate, and no pop after a script reload.
    /// </summary>
    public class WeaponCarryBlendTests
    {
        const float Blend = 0.3f;
        const float Suppress = 0.1f;
        const float Dt = 1f / 60f;

        static void Run(WeaponCarryBlend b, float seconds, bool swinging = false, bool suppressed = false, float dt = Dt)
        {
            for (float t = 0f; t < seconds - 1e-5f; t += dt) b.Tick(dt, swinging, suppressed, Suppress);
        }

        static WeaponCarryBlend Carrying()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 1f, Blend);
            Run(b, Blend);
            Assert.AreEqual(1f, b.Weight, 1e-4f, "should be fully in after its blend");
            return b;
        }

        [Test]
        public void Equip_WithCarry_FadesInOverTheBlendTime()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 1f, Blend);
            Run(b, Blend * 0.5f);
            Assert.AreEqual(0.5f, b.Weight, 0.02f, "half way through the blend");
            Run(b, Blend * 0.5f);
            Assert.AreEqual(1f, b.Weight, 1e-4f);
            Assert.IsFalse(b.IsHidden);
        }

        [Test]
        public void Equip_TheWeaponsWeight_IsWhereItSettles()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 0.6f, Blend);
            Run(b, 1f);
            Assert.AreEqual(0.6f, b.Weight, 1e-4f);
        }

        [Test]
        public void Equip_WithoutCarry_FadesOutOverItsBlendTime()
        {
            var b = Carrying();
            b.SetWeapon(false, 0f, 0.2f);
            Assert.IsFalse(b.HasCarry);
            Run(b, 0.1f);
            Assert.AreEqual(0.5f, b.Weight, 0.02f, "the unequip fade uses the new weapon's blend time");
            Run(b, 0.1f);
            Assert.AreEqual(0f, b.Weight, 1e-4f);
            Assert.IsTrue(b.IsHidden);
        }

        [Test]
        public void Equip_ZeroWeight_CountsAsNoCarry()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 0f, Blend);
            Assert.IsFalse(b.HasCarry);
            Run(b, 1f);
            Assert.AreEqual(0f, b.Weight);
        }

        [Test]
        public void NothingEquipped_StaysHidden()
        {
            var b = new WeaponCarryBlend();
            Run(b, 1f, swinging: true);
            Assert.AreEqual(0f, b.Weight);
            Assert.IsTrue(b.IsHidden);
        }

        [Test]
        public void Swing_DoesNotMoveIt()
        {
            var b = Carrying();
            Run(b, 1.5f, swinging: true);
            Assert.AreEqual(1f, b.Weight, 1e-4f, "held under the whole swing");
        }

        [Test]
        public void Swing_StartedMidFadeIn_KeepsFadingIn()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 1f, Blend);
            Run(b, 0.1f);
            Run(b, Blend, swinging: true);
            Assert.AreEqual(1f, b.Weight, 1e-4f, "the swing must end on the carry, not on the clips' arm");
        }

        [Test]
        public void AfterTheChainEnds_ItIsAlreadyThere()
        {
            var b = Carrying();
            Run(b, 0.9f, swinging: true);
            b.Tick(Dt, false, false, Suppress);
            Assert.AreEqual(1f, b.Weight, 1e-4f, "the Attack layer's fade reveals the carry, not a fade from 0");
        }

        [Test]
        public void SuppressingMove_FadesOutFast_AndBackOverTheBlend()
        {
            var b = Carrying();
            Run(b, Suppress * 0.5f, suppressed: true);
            Assert.AreEqual(0.5f, b.Weight, 0.02f, "out over the suppress time");
            Run(b, Suppress * 0.5f, suppressed: true);
            Assert.AreEqual(0f, b.Weight, 1e-4f);
            Assert.AreEqual(0f, b.Target);

            Run(b, Blend * 0.5f);
            Assert.AreEqual(0.5f, b.Weight, 0.02f, "back over the weapon's blend");
            Run(b, Blend * 0.5f);
            Assert.AreEqual(1f, b.Weight, 1e-4f);
        }

        [Test]
        public void SuppressingMove_DuringASwing_IsIgnoredUntilTheSwingEnds()
        {
            var b = Carrying();
            Run(b, 0.5f, swinging: true, suppressed: true);
            Assert.AreEqual(1f, b.Weight, 1e-4f);
            Run(b, Suppress, suppressed: true);
            Assert.AreEqual(0f, b.Weight, 1e-4f);
        }

        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void Fade_IsFrameRateIndependent(float fps)
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 1f, Blend);
            float dt = 1f / fps;
            // 1/6 s is a whole number of frames at all three rates.
            int frames = UnityEngine.Mathf.RoundToInt(fps / 6f);
            for (int i = 0; i < frames; i++) b.Tick(dt, false, false, Suppress);
            Assert.AreEqual((1f / 6f) / Blend, b.Weight, 1e-4f, "the same weight after the same time");
        }

        [Test]
        public void LongFrame_NeverOvershoots()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 0.8f, Blend);
            b.Tick(5f, false, false, Suppress);
            Assert.AreEqual(0.8f, b.Weight, 1e-6f);
            b.Tick(-1f, false, true, Suppress);
            Assert.AreEqual(0.8f, b.Weight, 1e-6f, "a negative dt moves nothing");
        }

        [Test]
        public void ZeroBlendTime_Switches()
        {
            var b = new WeaponCarryBlend();
            b.SetWeapon(true, 1f, 0f);
            b.Tick(Dt, false, false, Suppress);
            Assert.AreEqual(1f, b.Weight);
        }

        [Test]
        public void Reload_SyncedToTheLayer_DoesNotPop()
        {
            // A script reload rebuilds the blend; the component seeds it from the layer.
            var b = new WeaponCarryBlend();
            b.SyncTo(1f);
            b.SetWeapon(true, 1f, Blend);
            b.Tick(Dt, false, false, Suppress);
            Assert.AreEqual(1f, b.Weight, 1e-4f, "carries on at full weight, no fade from 0");

            var mid = new WeaponCarryBlend();
            mid.SyncTo(0.4f);
            mid.SetWeapon(true, 1f, Blend);
            mid.Tick(Dt, false, false, Suppress);
            Assert.AreEqual(0.4f + Dt / Blend, mid.Weight, 1e-4f, "carries on from where the layer was");
        }

        [Test]
        public void Reset_DropsEverything()
        {
            var b = Carrying();
            b.Reset();
            Assert.AreEqual(0f, b.Weight);
            Assert.IsFalse(b.HasCarry);
            Run(b, 1f);
            Assert.AreEqual(0f, b.Weight, "nothing comes back until a weapon is set again");
        }
    }
}
