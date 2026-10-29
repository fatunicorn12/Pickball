using System;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One Thunderbolt: a short, readable telegraph on the ground, then lightning
    /// on that spot. Heavy damage to the enemy it lands on (and a stagger, if
    /// that enemy accepts one), a small splash with falloff around it, and an
    /// optional chain that arcs to the nearest bystanders.
    ///
    /// Simulation only, the same shape as <see cref="MagicOrb"/>: it ticks in
    /// Update, deals damage, raises <see cref="Telegraphed"/>,
    /// <see cref="Chained"/> and <see cref="Struck"/>, and destroys its own
    /// GameObject the moment it has struck. <c>LightningStrikeVisual</c>, on a
    /// child, draws the ring, the cloud and the bolt, and detaches itself inside
    /// the Struck handler to finish the flash.
    ///
    /// Design decisions:
    ///  - <b>Cast on an enemy, it follows that enemy</b> (when
    ///    <see cref="SpellDefinition.StrikeTracksTarget"/>), so the telegraph is
    ///    a promise that it will land, not an invitation to sidestep. Cast on
    ///    the ground, it lands where aimed and hits whoever is standing there:
    ///    the damageable nearest the point, within half the splash radius,
    ///    takes the direct hit.
    ///  - <b>Each damageable at most once per strike,</b> direct, splash and
    ///    chain together, tracked in fixed arrays (no allocation per strike).
    ///  - <b>Never hurts its owner,</b> like the orb.
    ///  - <b>The chain jumps from link to link,</b> nearest first, so it reads
    ///    as one arc travelling through a crowd.
    ///  - <b>No direct victim, no chain.</b> The chain starts from the struck
    ///    enemy; a bolt on empty ground is a miss (splash only).
    /// </summary>
    public class LightningStrike : MonoBehaviour
    {
        const int OverlapCapacity = 32;
        const int MaxChain = 4;

        /// <summary>Raised once by <see cref="Begin"/>: the telegraph is showing.</summary>
        public event Action<LightningStrike> Telegraphed;

        /// <summary>Raised once per chain link, before <see cref="Struck"/>: the
        /// arc's two ends, in order. Damage has been dealt.</summary>
        public event Action<LightningStrike, Vector3, Vector3> Chained;

        /// <summary>Raised once when the bolt lands: the ground point and how many
        /// damageables were hit (direct, splash and chain). Damage has been dealt.
        /// The GameObject is destroyed right after this returns.</summary>
        public event Action<LightningStrike, Vector3, int> Struck;

        SpellDefinition _spell;
        Transform _owner;
        Transform _target;
        Collider _targetCollider;
        Vector3 _point;
        Vector3 _impact;
        float _elapsed;
        bool _active;

        // Runtime-built, never readonly: rebuilt lazily so a reload is harmless.
        Collider[] _overlap;
        IDamageable[] _struck;
        int _struckCount;
        Vector3[] _chainPositions;
        IDamageable[] _chainTargets;
        bool[] _chainExcluded;
        int[] _chainPicks;

        /// <summary>The spell cast (null before <see cref="Begin"/>).</summary>
        public SpellDefinition Spell => _spell;

        /// <summary>Who called it; never damaged by it.</summary>
        public Transform Owner => _owner;

        /// <summary>The enemy it is following; null when aimed at the ground or lost.</summary>
        public Transform Target => _target;

        /// <summary>Where it will land (or landed), on the ground. Moves with the
        /// target while tracking.</summary>
        public Vector3 Point => _point;

        /// <summary>Where the bolt connects: the struck enemy's centre, else a metre
        /// above <see cref="Point"/>. Valid from the strike on.</summary>
        public Vector3 ImpactPoint => _impact;

        /// <summary>0 at the cast, 1 as the bolt lands.</summary>
        public float TelegraphProgress => _spell != null ? StrikeMath.TelegraphProgress(_elapsed, _spell.StrikeDelay) : 0f;

        /// <summary>True between <see cref="Begin"/> and the strike.</summary>
        public bool IsTelegraphing => _active;

        /// <summary>
        /// Starts the telegraph. Call once, right after instantiating the prefab.
        /// </summary>
        /// <param name="point">Ground point under the crosshair.</param>
        /// <param name="target">Collider transform under the crosshair, or null.</param>
        /// <param name="spell">Tuning. Null is a wiring error: the strike removes itself.</param>
        /// <param name="owner">The caster. Nothing under it is ever hit.</param>
        public void Begin(Vector3 point, Transform target, SpellDefinition spell, Transform owner)
        {
            if (spell == null)
            {
                Debug.LogWarning($"[LightningStrike] {name} begun without a SpellDefinition; removing it.", this);
                Destroy(gameObject);
                return;
            }

            EnsureBuffers();
            _spell = spell;
            _owner = owner;
            _target = target;
            _targetCollider = null;
            if (target != null) target.TryGetComponent(out _targetCollider);
            _point = point;
            _impact = point + Vector3.up;
            _elapsed = 0f;
            _active = true;
            TrackTarget();
            transform.position = _point;
            Telegraphed?.Invoke(this);

            // A zero delay strikes on the cast frame.
            if (_spell.StrikeDelay <= 0f) Strike();
        }

        /// <summary>Advances the telegraph by <paramref name="dt"/> seconds and strikes
        /// when it is due. Update calls it with Time.deltaTime; public so a test or
        /// harness can drive time.</summary>
        public void Tick(float dt)
        {
            if (!_active || _spell == null) return;
            if (dt > 0f) _elapsed += dt;
            TrackTarget();
            transform.position = _point;
            if (_elapsed >= _spell.StrikeDelay) Strike();
        }

        void Update()
        {
            if (!_active || _spell == null)
            {
                // Never begun, or a script reload dropped the state.
                Destroy(gameObject);
                return;
            }
            Tick(Time.deltaTime);
        }

        void EnsureBuffers()
        {
            if (_overlap == null) _overlap = new Collider[OverlapCapacity];
            if (_struck == null) _struck = new IDamageable[OverlapCapacity * 2 + 1];
            if (_chainPositions == null) _chainPositions = new Vector3[OverlapCapacity];
            if (_chainTargets == null) _chainTargets = new IDamageable[OverlapCapacity];
            if (_chainExcluded == null) _chainExcluded = new bool[OverlapCapacity];
            if (_chainPicks == null) _chainPicks = new int[MaxChain];
        }

        void TrackTarget()
        {
            if (!_spell.StrikeTracksTarget || !TargetAlive()) return;
            _point = GroundProbe.FeetOf(_target);
        }

        bool TargetAlive() => _target != null && _target.gameObject.activeInHierarchy;

        // ------------------------------------------------------------- strike

        void Strike()
        {
            if (!_active) return;
            _active = false;
            EnsureBuffers();
            _struckCount = 0;
            int hits = 0;

            // ---- direct
            Collider directCollider = null;
            IDamageable direct = null;
            if (_spell.StrikeTracksTarget && TargetAlive())
            {
                directCollider = _targetCollider != null ? _targetCollider : _target.GetComponentInChildren<Collider>();
                direct = TargetOf(directCollider);
            }
            if (direct == null)
            {
                directCollider = NearestDamageable(_point, _spell.BlastRadius * 0.5f);
                direct = TargetOf(directCollider);
            }

            _impact = _point + Vector3.up;
            if (direct != null)
            {
                _impact = directCollider.bounds.center;
                Remember(direct);
                Vector3 away = _owner != null ? _impact - _owner.position : Vector3.zero;
                direct.TakeHit(_spell.DirectDamage, _impact, Push(away, _spell.Knockback), false, DamageKind.Spell);
                hits++;
                if (_spell.StaggerSeconds > 0f)
                {
                    var staggerable = directCollider.GetComponentInParent<IStaggerable>();
                    if (staggerable != null && !(staggerable is Object o && o == null))
                        staggerable.Stagger(_spell.StaggerSeconds);
                }
            }

            // ---- splash
            float radius = _spell.BlastRadius;
            if (radius > 0f)
            {
                float max = _spell.BlastDamageMax;
                int count = Physics.OverlapSphereNonAlloc(_point, radius, _overlap, _spell.HitMask,
                                                          QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var col = _overlap[i];
                    var victim = TargetOf(col);
                    if (victim == null || HasStruck(victim)) continue;
                    Vector3 at = HitPoint.Nearest(col, _point);
                    float damage = OrbMath.BlastDamageAt(Vector3.Distance(_point, at), radius, max,
                                                         _spell.BlastDamageMin);
                    if (damage <= 0f) continue;
                    Remember(victim);
                    float share = max > 0f ? Mathf.Clamp01(damage / max) : 1f;
                    victim.TakeHit(damage, at, Push(col.bounds.center - _point, _spell.Knockback * share), false, DamageKind.Spell);
                    hits++;
                }
            }

            // ---- chain: only from a struck victim. A bolt on empty ground is a
            // miss and never arcs to a bystander.
            if (direct != null) hits += Chain();

            Struck?.Invoke(this, _point, hits);
            // DestroyImmediate only outside Play mode (EditMode tests drive Tick).
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>Arcs to up to ChainCount unstruck damageables near the impact,
        /// each link starting where the last one ended.</summary>
        int Chain()
        {
            int wanted = Mathf.Min(_spell.ChainCount, MaxChain);
            if (wanted <= 0 || _spell.ChainRadius <= 0f || _spell.ChainDamage <= 0f) return 0;

            int count = Physics.OverlapSphereNonAlloc(_impact, _spell.ChainRadius, _overlap, _spell.HitMask,
                                                      QueryTriggerInteraction.Ignore);
            int candidates = 0;
            for (int i = 0; i < count && candidates < _chainPositions.Length; i++)
            {
                var col = _overlap[i];
                var victim = TargetOf(col);
                if (victim == null || HasStruck(victim) || IsCandidate(victim, candidates)) continue;
                _chainTargets[candidates] = victim;
                _chainPositions[candidates] = col.bounds.center;
                _chainExcluded[candidates] = false;
                candidates++;
            }

            int hits = 0;
            Vector3 from = _impact;
            for (int link = 0; link < wanted; link++)
            {
                if (StrikeMath.PickChain(from, _chainPositions, _chainExcluded, candidates,
                                         _spell.ChainRadius, 1, _chainPicks) == 0) break;
                int pick = _chainPicks[0];
                _chainExcluded[pick] = true;
                var victim = _chainTargets[pick];
                Vector3 to = _chainPositions[pick];
                Remember(victim);
                victim.TakeHit(_spell.ChainDamage, to, Vector3.zero, false, DamageKind.Spell);
                hits++;
                Chained?.Invoke(this, from, to);
                from = to;
            }

            for (int i = 0; i < candidates; i++) _chainTargets[i] = null;
            return hits;
        }

        Collider NearestDamageable(Vector3 point, float radius)
        {
            if (radius <= 0f) return null;
            int count = Physics.OverlapSphereNonAlloc(point, radius, _overlap, _spell.HitMask,
                                                      QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (TargetOf(col) == null) continue;
                float sq = (HitPoint.Nearest(col, point) - point).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = col;
            }
            return best;
        }

        IDamageable TargetOf(Collider col)
        {
            if (col == null || IsOwners(col.transform)) return null;
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null) return null;
            if (target is Object unityObject && unityObject == null) return null;
            if (target is Component component && IsOwners(component.transform)) return null;
            return target;
        }

        bool IsOwners(Transform t) => _owner != null && t.IsChildOf(_owner);

        bool HasStruck(IDamageable target)
        {
            for (int i = 0; i < _struckCount; i++)
                if (ReferenceEquals(_struck[i], target)) return true;
            return false;
        }

        bool IsCandidate(IDamageable target, int candidates)
        {
            for (int i = 0; i < candidates; i++)
                if (ReferenceEquals(_chainTargets[i], target)) return true;
            return false;
        }

        void Remember(IDamageable target)
        {
            if (_struckCount < _struck.Length) _struck[_struckCount++] = target;
        }

        static Vector3 Push(Vector3 direction, float speed)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized * speed : Vector3.zero;
        }
    }
}
