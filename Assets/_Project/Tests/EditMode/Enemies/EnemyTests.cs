using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Enemies;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Enemies
{
    /// <summary>
    /// The component's wiring, driven through <see cref="Enemy.Tick"/>. The target is
    /// re-placed relative to the enemy each frame, so these judge what it decides and
    /// deals rather than where the CharacterController ends up (no floor in EditMode).
    /// </summary>
    public class EnemyTests : BossTestFixture
    {
        const float Dt = 1f / 60f;

        EnemyConfig _config;
        Enemy _enemy;
        Health _player;
        float _now;

        [SetUp]
        public void BuildEnemy()
        {
            _config = ScriptableObject.CreateInstance<EnemyConfig>();
            _enemy = Spawn<Enemy>();
            _player = NewComponent<Health>();
            _player.SetMaxHealth(100f);
            _enemy.SetTarget(_player);
            _now = 100f;
        }

        [TearDown]
        public void DestroyConfig() => Object.DestroyImmediate(_config);

        T Spawn<T>() where T : Enemy
        {
            var enemy = NewComponent<T>();
            enemy.Configure(_config);
            // AddComponent does not run lifecycle messages in EditMode.
            typeof(Enemy).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)!
                         .Invoke(enemy, null);
            return enemy;
        }

        /// <summary>Run with the player held at a distance in front of the enemy.</summary>
        void RunFor(float seconds, float distance, Enemy enemy = null)
        {
            enemy ??= _enemy;
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                _now += Dt;
                _player.transform.position = enemy.transform.position + Vector3.forward * distance;
                enemy.Tick(_now, Dt);
            }
        }

        // ------------------------------------------------------------- the request

        [Test]
        public void BasicEnemy_HasTheRequestedNumbers()
        {
            Assert.AreEqual(300f, _config.MaxHealth);
            Assert.AreEqual(10f, _config.AttackDamage);
            Assert.AreEqual(0.5f, _config.AttackInterval);
        }

        [Test]
        public void Spawn_TakesItsHealthFromTheConfig()
        {
            Assert.AreEqual(300f, _enemy.Health.Max);
            Assert.AreEqual(300f, _enemy.Health.Current);
        }

        [Test]
        public void SetMaxHealth_RefillsAndClearsTheGreyPool()
        {
            var health = NewComponent<Health>();
            health.SetMaxHealth(100f);
            health.Damage(30f, Vector3.zero);
            Assert.Greater(health.Recoverable, 0f);

            health.SetMaxHealth(300f);

            Assert.AreEqual(300f, health.Max);
            Assert.AreEqual(300f, health.Current);
            Assert.AreEqual(0f, health.Recoverable);
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void SetMaxHealth_RefusesABadValue(float bad)
        {
            var health = NewComponent<Health>();
            health.SetMaxHealth(100f);
            health.Damage(30f, Vector3.zero);

            health.SetMaxHealth(bad);

            Assert.AreEqual(100f, health.Max);
            Assert.AreEqual(70f, health.Current);
        }

        [Test]
        public void SetMaxHealth_AfterDeath_CanDieAgain()
        {
            var health = NewComponent<Health>();
            health.SetMaxHealth(10f);
            int deaths = 0;
            health.Died += _ => deaths++;
            health.Damage(10f, Vector3.zero);

            health.SetMaxHealth(10f);
            health.Damage(10f, Vector3.zero);

            Assert.AreEqual(2, deaths);
        }

        [Test]
        public void OutOfRange_StandsIdleAndHurtsNobody()
        {
            RunFor(1f, _config.AggroRange + 5f);
            Assert.AreEqual(EnemyState.Idle, _enemy.State);
            Assert.AreEqual(100f, _player.Current);
        }

        [Test]
        public void InRange_RunsAtThePlayer()
        {
            RunFor(0.1f, _config.AggroRange - 1f);
            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            Assert.AreEqual(100f, _player.Current, "out of reach, so no damage yet");
        }

        [Test]
        public void InReach_Deals10EveryHalfSecond()
        {
            // First strike at 0.25 s, then one per 0.5 s: 0.25, 0.75, 1.25, 1.75.
            RunFor(2f, 1f);
            Assert.AreEqual(EnemyState.Attack, _enemy.State);
            Assert.AreEqual(60f, _player.Current, 1e-3f);
        }

        [Test]
        public void InReach_AgainstThePlayersHitGrace_LosesNoStrikes()
        {
            float clock() => _now;
            _player.TimeSource = clock;
            _player.HitGraceSeconds = 0.5f;
            RunFor(2f, 1f);
            Assert.AreEqual(60f, _player.Current, 1e-3f);
        }

        [Test]
        public void Struck_FiresOnEachStrike()
        {
            int struck = 0;
            _enemy.Struck += () => struck++;
            RunFor(2f, 1f);
            Assert.AreEqual(4, struck);
        }

        [Test]
        public void OverThePlayersHead_CannotStrike()
        {
            int frames = 60;
            for (int i = 0; i < frames; i++)
            {
                _now += Dt;
                _player.transform.position = _enemy.transform.position + Vector3.up * (_config.AttackHeight + 1f);
                _enemy.Tick(_now, Dt);
            }
            Assert.AreEqual(100f, _player.Current);
        }

        [Test]
        public void ThePlayerDead_StopsAttacking()
        {
            _player.SetMaxHealth(10f);
            RunFor(2f, 1f);
            Assert.IsTrue(_player.IsDead);
            Assert.AreEqual(EnemyState.Idle, _enemy.State);
        }

        // ------------------------------------------------------------- being hit

        [Test]
        public void Hit_StunsItAndStopsItsStrikes()
        {
            RunFor(0.1f, 1f);
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            RunFor(Dt, 1f);
            Assert.AreEqual(EnemyState.Stunned, _enemy.State);
            Assert.AreEqual(100f, _player.Current);
        }

        [Test]
        public void Stun_RunsOut_ThenItFightsAgain()
        {
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            RunFor(_config.Juggle.Hitstun + 0.1f, 1f);
            Assert.AreEqual(EnemyState.Attack, _enemy.State);
        }

        [Test]
        public void TooMuchDamageAtOnce_BreaksOutOfTheStun()
        {
            int breakOuts = 0;
            _enemy.BrokeOut += () => breakOuts++;
            _enemy.Health.TakeHit(new HitImpact(_config.Revenge.Limit, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            Assert.AreEqual(1, breakOuts);
            RunFor(Dt, 1f);
            Assert.AreEqual(EnemyState.Attack, _enemy.State, "armored, so it keeps fighting");
        }

        [Test]
        public void Launcher_LiftsItOffTheGround()
        {
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Launch));
            RunFor(0.05f, 1f);
            Assert.IsTrue(_enemy.Juggle.IsAirborne);
            Assert.AreEqual(EnemyState.Stunned, _enemy.State);
        }

        [Test]
        public void HurtingIt_CashesInThePlayersGreyPool()
        {
            RunFor(0.3f, 1f);   // one strike: 10 lost, 7 of it grey
            Assert.AreEqual(90f, _player.Current, 1e-3f);
            Assert.AreEqual(7f, _player.Recoverable, 1e-3f);

            _enemy.Health.TakeHit(new HitImpact(5f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            Assert.Greater(_player.Current, 90f);
            Assert.Less(_player.Recoverable, 7f);
        }

        // ------------------------------------------------------------------- death

        [Test]
        public void ThreeHundredDamage_KillsItOnce()
        {
            int defeats = 0;
            _enemy.Defeated += () => defeats++;

            _enemy.Health.TakeHit(new HitImpact(299f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            Assert.AreNotEqual(EnemyState.Dead, _enemy.State);
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Flinch));

            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            Assert.AreEqual(1, defeats);
            Assert.IsFalse(_enemy.GetComponent<CharacterController>().enabled, "a corpse must not block");
        }

        [Test]
        public void Dead_NeverStrikes()
        {
            _enemy.Health.TakeHit(new HitImpact(300f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            RunFor(2f, 1f);
            Assert.AreEqual(100f, _player.Current);
        }

        [Test]
        public void KilledInTheAir_DiesOnLanding()
        {
            _enemy.Health.TakeHit(new HitImpact(1f, Vector3.zero, Vector3.zero, HitReaction.Launch));
            RunFor(0.05f, 1f);
            _enemy.Health.TakeHit(new HitImpact(299f, Vector3.zero, Vector3.zero, HitReaction.Flinch));
            Assert.AreEqual(EnemyState.Stunned, _enemy.State, "the defeat waits for the ground");

            RunFor(3f, 1f);
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
        }

        // ------------------------------------------------------------- inheritance

        [Test]
        public void Subclass_CanReplaceTheAttack()
        {
            var thrower = Spawn<ThrowingEnemy>();
            thrower.SetTarget(_player);
            RunFor(2f, 1f, thrower);
            Assert.AreEqual(4, thrower.Throws);
            Assert.AreEqual(100f, _player.Current, "the override, not the base strike");
        }

        [Test]
        public void Subclass_CanReplaceTheRules()
        {
            var coward = Spawn<CowardEnemy>();
            coward.SetTarget(_player);
            RunFor(1f, 1f, coward);
            Assert.AreEqual(EnemyState.Idle, coward.State);
            Assert.AreEqual(100f, _player.Current);
        }

        sealed class ThrowingEnemy : Enemy
        {
            public int Throws;
            protected override void Strike() => Throws++;
        }

        sealed class CowardEnemy : Enemy
        {
            protected override EnemyState Think(in EnemySenses senses) => EnemyState.Idle;
        }

        // ---------------------------------------------------------- shipped assets

        [Test, Category("Assets")]
        public void ShippedConfig_HasTheRequestedNumbers()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyConfig>("Assets/_Project/ScriptableObjects/Enemies/BasicEnemy.asset");
            Assert.IsNotNull(asset, "run Tools > Pickleball > Enemies > Build Alien Enemy");
            Assert.AreEqual(300f, asset.MaxHealth);
            Assert.AreEqual(10f, asset.AttackDamage);
            Assert.AreEqual(0.5f, asset.AttackInterval);
        }

        [Test, Category("Assets")]
        public void ShippedPrefab_IsAWiredEnemy()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/AlienEnemy.prefab");
            Assert.IsNotNull(prefab, "run Tools > Pickleball > Enemies > Build Alien Enemy");
            Assert.IsNotNull(prefab.GetComponent<Enemy>());
            Assert.IsNotNull(prefab.GetComponent<Health>());
            Assert.IsNotNull(prefab.GetComponent<CharacterController>());
            Assert.IsNotNull(prefab.GetComponent<HurtFlash>());
            Assert.IsNotNull(prefab.GetComponent<EnemyWaddleView>());
            Assert.IsNotNull(new SerializedObject(prefab.GetComponent<Enemy>()).FindProperty("config").objectReferenceValue,
                             "no config assigned");
            Assert.IsNotEmpty(prefab.GetComponentsInChildren<Renderer>(true), "no visible body");
        }
    }
}
