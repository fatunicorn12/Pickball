using NUnit.Framework;
using UnityEngine;

namespace Pickleball.Tests.PickleballCore
{
    /// <summary>
    /// Line calls. <see cref="Court"/> decides in, out, kitchen and side from
    /// world position alone, and GameStateManager awards every rally from those
    /// answers, so a boundary that moves by a centimetre changes who wins.
    ///
    /// Uses the serialized defaults, a regulation 44 × 20 ft court
    /// (13.41 × 6.10 m, 2.13 m kitchen). EditMode never runs <c>Awake</c>, so no
    /// geometry is built; the queries do not need it.
    /// </summary>
    public class CourtTests
    {
        Court _court;

        [SetUp]
        public void MakeCourt()
        {
            _court = new GameObject("Test_Court") { hideFlags = HideFlags.DontSave }.AddComponent<Court>();
        }

        [TearDown]
        public void DestroyCourt()
        {
            if (_court != null) Object.DestroyImmediate(_court.gameObject);
        }

        // Half-width 3.05 m and half-length 6.705 m, plus the 2 cm line tolerance.
        [TestCase(3.069f, 0f, ExpectedResult = true, TestName = "Sideline_JustInside_IsIn")]
        [TestCase(-3.069f, 0f, ExpectedResult = true, TestName = "FarSideline_JustInside_IsIn")]
        [TestCase(3.08f, 0f, ExpectedResult = false, TestName = "Sideline_JustOutside_IsOut")]
        [TestCase(0f, 6.72f, ExpectedResult = true, TestName = "Baseline_JustInside_IsIn")]
        [TestCase(0f, -6.72f, ExpectedResult = true, TestName = "PlayerBaseline_JustInside_IsIn")]
        [TestCase(0f, 6.73f, ExpectedResult = false, TestName = "Baseline_JustOutside_IsOut")]
        [TestCase(3.069f, 6.72f, ExpectedResult = true, TestName = "Corner_IsIn")]
        public bool IsInBounds_CallsTheLinesWithATwoCentimetreTolerance(float x, float z)
            => _court.IsInBounds(new Vector3(x, 0f, z));

        [Test]
        public void IsInBounds_IgnoresHeight()
        {
            Assert.That(_court.IsInBounds(new Vector3(0f, 5f, 0f)), Is.True);
        }

        [Test]
        public void SideOf_TheNetPlaneBelongsToTheAI()
        {
            Assert.That(_court.SideOf(new Vector3(0f, 0f, -0.01f)), Is.EqualTo(CourtSide.Player));
            Assert.That(_court.SideOf(new Vector3(0f, 0f, 0f)), Is.EqualTo(CourtSide.AI));
            Assert.That(_court.SideOf(new Vector3(0f, 0f, 0.01f)), Is.EqualTo(CourtSide.AI));
        }

        [TestCase(0f, 2.13f, ExpectedResult = CourtZone.Kitchen, TestName = "KitchenLine_AISide_IsKitchen")]
        [TestCase(0f, -2.13f, ExpectedResult = CourtZone.Kitchen, TestName = "KitchenLine_PlayerSide_IsKitchen")]
        [TestCase(-1f, 0.5f, ExpectedResult = CourtZone.Kitchen, TestName = "NearTheNet_IsKitchen")]
        [TestCase(-1f, -4f, ExpectedResult = CourtZone.CourtLeft, TestName = "LeftOfCentre_IsCourtLeft")]
        [TestCase(1f, 4f, ExpectedResult = CourtZone.CourtRight, TestName = "RightOfCentre_IsCourtRight")]
        [TestCase(0f, -2.14f, ExpectedResult = CourtZone.CourtRight, TestName = "CentreLine_CountsAsRight")]
        [TestCase(3.2f, 1f, ExpectedResult = CourtZone.OutOfBounds, TestName = "WideOfTheKitchen_IsOut")]
        [TestCase(0f, 7f, ExpectedResult = CourtZone.OutOfBounds, TestName = "Long_IsOut")]
        public CourtZone ResolveZone_OutBeatsKitchenBeatsSide(float x, float z)
            => _court.ResolveZone(new Vector3(x, 0f, z));

        [Test]
        public void Baselines_SitAtHalfTheLengthEitherSideOfTheNet()
        {
            Assert.That(_court.PlayerBaselineZ, Is.EqualTo(-_court.HalfLength));
            Assert.That(_court.AiBaselineZ, Is.EqualTo(_court.HalfLength));
            Assert.That(_court.PlayerBaselineZ, Is.LessThan(0f), "The player's baseline is at −Z by convention.");
        }
    }
}
