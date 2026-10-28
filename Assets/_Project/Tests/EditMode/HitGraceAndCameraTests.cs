using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>Hit grace (i-frames) on Health, and the camera occlusion / lag maths.</summary>
    public class HitGraceAndCameraTests : BossTestFixture
    {
        float _clock;

        Health NewHealth(float grace, float max = 1000f)
        {
            Health h = NewComponent<Health>();
            SetTunable(h, "maxHealth", max);
            SetTunable(h, "hitGraceSeconds", grace);
            SetTunable(h, "hitGraceMinDamage", 2f);
            _clock = 0f;
            h.TimeSource = () => _clock;
            h.ResetToFull();
            return h;
        }

        [Test]
        public void Discrete_hit_inside_window_is_refused_then_allowed_after()
        {
            Health h = NewHealth(0.5f);
            h.Damage(10f, Vector3.zero);
            _clock = 0.3f;
            h.Damage(10f, Vector3.zero);
            Assert.AreEqual(990f, h.Current, 1e-3f, "second hit inside grace must be refused");
            Assert.IsTrue(h.InGrace);
            _clock = 0.55f;
            h.Damage(10f, Vector3.zero);
            Assert.AreEqual(980f, h.Current, 1e-3f);
        }

        [Test]
        public void Grace_zero_changes_nothing()
        {
            Health h = NewHealth(0f);
            h.Damage(10f, Vector3.zero);
            h.Damage(10f, Vector3.zero);
            Assert.AreEqual(980f, h.Current, 1e-3f);
        }

        [Test]
        public void Explicit_continuous_flag_is_never_blocked_and_opens_no_window()
        {
            Health h = NewHealth(0.5f);
            h.Damage(10f, Vector3.zero, true, continuous: true);
            Assert.IsFalse(h.InGrace);
            h.Damage(10f, Vector3.zero);          // discrete, lands
            h.Damage(10f, Vector3.zero, false, continuous: true); // continuous inside window, lands
            Assert.AreEqual(970f, h.Current, 1e-3f);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Continuous_ticks_total_is_framerate_independent_inside_a_window(float dt)
        {
            Health h = NewHealth(0.5f);
            h.Damage(10f, Vector3.zero);          // opens a window
            const float dps = 30f;
            int steps = Mathf.RoundToInt(1f / dt);
            for (int i = 0; i < steps; i++)
            {
                _clock += dt;
                h.Damage(dps * dt, Vector3.zero, recoverable: false);
            }
            // 30 dps x 1 s, none refused (each tick < min discrete).
            Assert.AreEqual(1000f - 10f - dps * steps * dt, h.Current, 0.05f);
            Assert.IsFalse(h.LastHitDiscrete);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Hammering_discrete_hits_lands_one_per_window(float dt)
        {
            Health h = NewHealth(0.5f);
            int steps = Mathf.RoundToInt(2f / dt);
            for (int i = 0; i < steps; i++)
            {
                h.Damage(10f, Vector3.zero);
                _clock += dt;
            }
            float landed = (1000f - h.Current) / 10f;   // ideal: hits at 0, .5, 1, 1.5
            Assert.That(landed, Is.InRange(4f, 5f));
        }

        [Test]
        public void ResetToFull_clears_the_window()
        {
            Health h = NewHealth(0.5f);
            h.Damage(10f, Vector3.zero);
            h.ResetToFull();
            Assert.IsFalse(h.InGrace);
        }

        [Test]
        public void FreeLength_clamps_between_min_and_desired()
        {
            Assert.AreEqual(5f, CameraOcclusion.FreeLength(5f, -1f, 0.8f));
            Assert.AreEqual(2f, CameraOcclusion.FreeLength(5f, 2f, 0.8f));
            Assert.AreEqual(0.8f, CameraOcclusion.FreeLength(5f, 0.1f, 0.8f));
            Assert.AreEqual(0.5f, CameraOcclusion.FreeLength(0.5f, 0.1f, 0.8f), "never longer than wanted");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Clip_snaps_in_and_eases_out_at_the_same_speed_at_any_framerate(float dt)
        {
            float clip = CameraOcclusion.NextClip(0f, 5f, 2f, 5f, dt);
            Assert.AreEqual(3f, clip, 1e-4f, "blocked: pulls in at once");

            int steps = Mathf.RoundToInt(0.4f / dt);
            for (int i = 0; i < steps; i++) clip = CameraOcclusion.NextClip(clip, 5f, 5f, 5f, dt);
            Assert.AreEqual(1f, clip, 0.05f, "0.4 s at 5 m/s returns 2 m of the 3 m");
        }

        [Test]
        public void ClampLag_limits_vertical_trail_and_negative_disables()
        {
            Assert.AreEqual(4f, CameraOcclusion.ClampLag(3f, 5f, 1f));
            Assert.AreEqual(6f, CameraOcclusion.ClampLag(7f, 5f, 1f));
            Assert.AreEqual(5.5f, CameraOcclusion.ClampLag(5.5f, 5f, 1f));
            Assert.AreEqual(-9f, CameraOcclusion.ClampLag(-9f, 5f, -1f));
        }
    }
}
