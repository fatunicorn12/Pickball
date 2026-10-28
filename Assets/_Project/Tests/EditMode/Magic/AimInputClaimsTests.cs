using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Movement;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// What aiming takes from the player's other input and gives back:
    /// <see cref="AimInputClaims"/> claims the sword and lock-on inputs and caps
    /// the drive while aiming, and releases only its own claim. The caster raises
    /// AimStarted / AimEnded from Update, which EditMode never runs, so the tests
    /// call the public handlers the events are wired to.
    /// </summary>
    public class AimInputClaimsTests : BossTestFixture
    {
        const float AimMoveScale = 0.4f;

        SpellDefinition _spell;
        AimInputClaims _claims;
        PlayerInputGate _gate;
        PlayerMovementInput _movement;

        [SetUp]
        public void CreateRig()
        {
            _spell = ScriptableObject.CreateInstance<SpellDefinition>();
            var spellSo = new SerializedObject(_spell);
            spellSo.FindProperty("aimMoveScale").floatValue = AimMoveScale;
            spellSo.ApplyModifiedPropertiesWithoutUndo();

            _claims = NewComponent<AimInputClaims>();
            GameObject host = _claims.gameObject;
            var caster = host.AddComponent<SpellCaster>();
            _gate = host.AddComponent<PlayerInputGate>();
            _movement = host.AddComponent<PlayerMovementInput>();

            SetReference(caster, "spell", _spell);
            SetReference(_claims, "caster", caster);
            SetReference(_claims, "inputGate", _gate);
            SetReference(_claims, "movementInput", _movement);
        }

        [TearDown]
        public void DestroySpell() => Object.DestroyImmediate(_spell);

        [Test]
        public void AimStart_BlocksSwordAndLockOn_LeavesMovementAndSpellFree()
        {
            _claims.ClaimForAim();

            Assert.That(_gate.IsBlocked(InputBlock.Sword), Is.True, "fire must not also swing the sword");
            Assert.That(_gate.IsBlocked(InputBlock.LockOn), Is.True, "RT must not also toggle lock-on");
            Assert.That(_gate.IsBlocked(InputBlock.Movement), Is.False, "aiming walks");
            Assert.That(_gate.IsBlocked(InputBlock.Spell), Is.False, "aiming must not block itself");
            Assert.That(_claims.IsClaiming, Is.True);
        }

        [Test]
        public void AimStart_CapsDriveToTheSpellsAimMoveScale()
        {
            _claims.ClaimForAim();

            Assert.That(_movement.DriveLimit, Is.EqualTo(AimMoveScale).Within(1e-6f));
        }

        [Test]
        public void AimEnd_ReleasesTheClaimAndRestoresFullDrive()
        {
            _claims.ClaimForAim();
            _claims.ReleaseAim();

            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(_movement.DriveLimit, Is.EqualTo(1f));
            Assert.That(_claims.IsClaiming, Is.False);
        }

        [Test]
        public void AimEnd_KeepsAConversationsClaim()
        {
            var dialogue = new object();
            _claims.ClaimForAim();
            _gate.Claim(dialogue, InputBlock.All);

            _claims.ReleaseAim();

            Assert.That(_gate.IsBlocked(InputBlock.All), Is.True);
            Assert.That(_gate.ClaimOf(dialogue), Is.EqualTo(InputBlock.All));
        }

        [Test]
        public void ReleaseWithoutAClaim_LeavesSomeoneElsesDriveLimitAlone()
        {
            _movement.DriveLimit = 0.25f;

            _claims.ReleaseAim();

            Assert.That(_movement.DriveLimit, Is.EqualTo(0.25f));
        }

        [Test]
        public void ConversationClaim_BlocksTheSpell()
        {
            // A conversation claims All; SpellInput reads Spell, so aim and fire
            // read as released mid-conversation.
            _gate.Claim(new object(), InputBlock.All);

            Assert.That(_gate.IsBlocked(InputBlock.Spell), Is.True);
        }

        [Test]
        public void Unwired_ClaimAndReleaseDoNothingAndDoNotThrow()
        {
            var bare = NewComponent<AimInputClaims>();

            Assert.DoesNotThrow(() =>
            {
                bare.ClaimForAim();
                bare.ReleaseAim();
            });
        }

        static void SetReference(Component target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized '{fieldName}'");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
