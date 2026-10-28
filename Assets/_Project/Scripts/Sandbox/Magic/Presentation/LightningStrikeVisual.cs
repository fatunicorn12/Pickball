using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Draws a <see cref="LightningStrike"/>. During the telegraph a ring
    /// tightens onto the strike point (to the splash radius, so the edge you see
    /// is the edge that hits), a target disc fills under it, and a dark cloud
    /// gathers overhead with lightning flickering inside. On the strike a jagged
    /// white-blue bolt (re-jittered every few hundredths of a second, with a
    /// branch) cracks from the cloud to the ground, a light flashes, a shock
    /// ring spreads, sparks spray, and an arc snakes to each chained enemy.
    ///
    /// Presentation only, on a CHILD of the strike, like
    /// <see cref="MagicOrbVisual"/>: the strike destroys its own GameObject the
    /// moment it lands, so this unparents itself inside the
    /// <see cref="LightningStrike.Struck"/> handler and lives on for
    /// <see cref="lingerSeconds"/>.
    ///
    /// <b>Optional models</b> from the spell: <see cref="SpellDefinition.ModelPrefab"/>
    /// replaces the drawn bolt (stretched to the cloud height; author it about
    /// 1 m tall with its pivot at the bottom), and
    /// <see cref="SpellDefinition.SecondaryModelPrefab"/> replaces the cloud
    /// puffs (scaled by <see cref="SpellDefinition.ModelScale"/>).
    ///
    /// <b>Readable on any backdrop.</b> Additive light vanishes on a near-white
    /// floor (the training stage), so the parts that must read are layered: the
    /// bolt is an additive white core over an alpha-blended blue glow, and the
    /// telegraph ring and chain arcs are alpha-blended in saturated colours.
    ///
    /// Every buffer is allocated once, in OnEnable; nothing allocates per frame.
    /// </summary>
    public class LightningStrikeVisual : MonoBehaviour
    {
        const float TwoPi = Mathf.PI * 2f;
        const int RingSegments = 48;
        const int BoltSegments = 16;
        const int BranchSegments = 7;
        const int ChainSegments = 9;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The strike drawn. Found on a parent when empty. This visual must be on a " +
                 "child of the strike so it can detach and finish after the strike is gone.")]
        [SerializeField] private LightningStrike strike;

        [Header("Telegraph")]
        [Tooltip("World-space looped line: the ring that tightens onto the strike point.")]
        [SerializeField] private LineRenderer ring;
        [Tooltip("Flat disc under the ring that fills as the strike nears.")]
        [SerializeField] private Renderer marker;
        [Tooltip("Pivot of the cloud overhead; moved to the strike point every frame.")]
        [SerializeField] private Transform cloud;
        [Tooltip("The cloud's puffs (camera-facing quads), tinted here.")]
        [SerializeField] private Renderer[] cloudPuffs;
        [Tooltip("Light inside the cloud, flickered during the telegraph.")]
        [SerializeField] private Light cloudLight;

        [Header("Bolt")]
        [Tooltip("World-space line: the main bolt (additive core).")]
        [SerializeField] private LineRenderer bolt;
        [Tooltip("Optional world-space line drawn under the bolt along the same path: a " +
                 "wider, alpha-blended glow that keeps the bolt visible on bright floors.")]
        [SerializeField] private LineRenderer boltGlow;
        [Tooltip("World-space line: a thinner branch off the bolt.")]
        [SerializeField] private LineRenderer branch;
        [Tooltip("World-space lines, one per possible chain link.")]
        [SerializeField] private LineRenderer[] chains;
        [Tooltip("The strike flash.")]
        [SerializeField] private Light flash;
        [Tooltip("Flat ring that spreads over the splash radius.")]
        [SerializeField] private Renderer shock;
        [Tooltip("One-shot spark spray at the impact.")]
        [SerializeField] private ParticleSystem sparks;

        [Header("Colour")]
        [Tooltip("Bolt, arcs and flash tint.")]
        [SerializeField] private Color boltColour = new(0.75f, 0.88f, 1f, 1f);
        [Tooltip("Telegraph ring and disc tint; alpha is opacity. The ring blends normally, " +
                 "so a deep amber reads on light and dark floors alike.")]
        [SerializeField] private Color telegraphColour = new(1f, 0.68f, 0.08f, 0.95f);
        [Tooltip("The bolt's glow and the chain arcs (alpha-blended); alpha is opacity.")]
        [SerializeField] private Color glowColour = new(0.3f, 0.5f, 1f, 0.6f);
        [Tooltip("Cloud tint; alpha is opacity. Additive puffs would glow, so these blend normally.")]
        [SerializeField] private Color cloudColour = new(0.16f, 0.17f, 0.24f, 0.85f);
        [Tooltip("HDR brightness multiplier on the bolt and arcs (x). Bloom catches above ~1.")]
        [Min(0f)] [SerializeField] private float intensity = 4f;

        [Header("Shape")]
        [Tooltip("Height of the cloud above the strike point, in metres.")]
        [Min(1f)] [SerializeField] private float cloudHeight = 7f;
        [Tooltip("Ring radius at the cast as a multiple of the splash radius; it tightens to 1.")]
        [Min(1f)] [SerializeField] private float ringStartScale = 1.9f;
        [Tooltip("Sideways wander of the bolt, in metres, at its middle.")]
        [Min(0f)] [SerializeField] private float boltJitter = 0.55f;
        [Tooltip("Bolt width at the top, in metres.")]
        [Min(0.01f)] [SerializeField] private float boltWidth = 0.3f;
        [Tooltip("Sideways wander of a chain arc, in metres.")]
        [Min(0f)] [SerializeField] private float chainJitter = 0.3f;
        [Tooltip("Ring radius within the shock texture, as a fraction of the quad's half-size " +
                 "(the generated WellHalo ring sits at 0.55).")]
        [Min(0.05f)] [SerializeField] private float ringTextureFill = 0.55f;

        [Header("Timing")]
        [Tooltip("Seconds the bolt stays on screen.")]
        [Min(0.02f)] [SerializeField] private float boltSeconds = 0.22f;
        [Tooltip("Seconds between re-jitters of the bolt and arcs: the crackle.")]
        [Min(0.01f)] [SerializeField] private float flickerSeconds = 0.035f;
        [Tooltip("Seconds each chain arc stays on screen.")]
        [Min(0.02f)] [SerializeField] private float chainSeconds = 0.3f;
        [Tooltip("Seconds the shock ring spreads and fades.")]
        [Min(0.05f)] [SerializeField] private float shockSeconds = 0.4f;
        [Tooltip("Flash light intensity at the strike.")]
        [Min(0f)] [SerializeField] private float flashIntensity = 14f;
        [Tooltip("Seconds the detached visual lives after the strike.")]
        [Min(0.1f)] [SerializeField] private float lingerSeconds = 0.9f;

        MaterialPropertyBlock _block;
        Vector3[] _ringPoints, _boltPoints, _branchPoints, _chainPoints;
        Vector3[] _chainFrom, _chainTo;
        int _chainCount;
        bool _struck;
        float _fxAge, _sinceFlicker, _seed;
        Vector3 _point, _impact, _top;
        float _radius;
        GameObject _boltModel, _cloudModel;

        void OnEnable()
        {
            _block = new MaterialPropertyBlock();
            _ringPoints = new Vector3[RingSegments];
            _boltPoints = new Vector3[BoltSegments + 1];
            _branchPoints = new Vector3[BranchSegments + 1];
            _chainPoints = new Vector3[ChainSegments + 1];
            int chainSlots = chains != null ? chains.Length : 0;
            _chainFrom = new Vector3[chainSlots];
            _chainTo = new Vector3[chainSlots];
            _chainCount = 0;

            if (strike == null) strike = GetComponentInParent<LightningStrike>();
            if (strike == null)
            {
                Destroy(gameObject);
                return;
            }
            strike.Telegraphed += OnTelegraphed;
            strike.Chained += OnChained;
            strike.Struck += OnStruck;

            _struck = false;
            ShowTelegraph(true);
            ShowBolt(false);
        }

        void OnDisable() => Unsubscribe();

        void Unsubscribe()
        {
            if (strike == null) return;
            strike.Telegraphed -= OnTelegraphed;
            strike.Chained -= OnChained;
            strike.Struck -= OnStruck;
        }

        // ------------------------------------------------------------ events

        void OnTelegraphed(LightningStrike source)
        {
            var spell = source.Spell;
            _radius = Mathf.Max(spell != null ? spell.BlastRadius : 1f, 0.5f);
            _seed = Random.value * 100f;
            if (spell != null)
            {
                if (_cloudModel == null && spell.SecondaryModelPrefab != null && cloud != null)
                    _cloudModel = SpellModel.Spawn(spell.SecondaryModelPrefab, cloud, spell.ModelScale);
                if (_boltModel == null && spell.ModelPrefab != null)
                {
                    _boltModel = SpellModel.Spawn(spell.ModelPrefab, transform, 1f);
                    if (_boltModel != null) _boltModel.SetActive(false);
                }
            }
            if (_cloudModel != null && cloudPuffs != null)
                foreach (var puff in cloudPuffs) if (puff != null) puff.enabled = false;
            AnimateTelegraph(source.Point, 0f);
        }

        void OnChained(LightningStrike source, Vector3 from, Vector3 to)
        {
            if (_chainCount >= _chainFrom.Length) return;
            _chainFrom[_chainCount] = from;
            _chainTo[_chainCount] = to;
            _chainCount++;
        }

        void OnStruck(LightningStrike source, Vector3 point, int hits)
        {
            _point = point;
            _impact = source.ImpactPoint;
            _top = point + Vector3.up * cloudHeight;

            Unsubscribe();
            strike = null;
            transform.SetParent(null, true);
            transform.position = point;

            _struck = true;
            _fxAge = 0f;
            _sinceFlicker = flickerSeconds;   // jitter on the first frame
            if (ring != null) ring.enabled = false;
            if (marker != null) marker.enabled = false;
            ShowBolt(true);
            if (sparks != null)
            {
                sparks.transform.position = _impact;
                sparks.Play(true);
            }
            if (_boltModel != null)
            {
                _boltModel.SetActive(true);
                _boltModel.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                var spell = source.Spell;
                float scale = spell != null ? spell.ModelScale : 1f;
                _boltModel.transform.localScale = new Vector3(scale, cloudHeight, scale);
                if (bolt != null) bolt.enabled = false;
                if (boltGlow != null) boltGlow.enabled = false;
                if (branch != null) branch.enabled = false;
            }
            AnimateBolt(0f);
        }

        // --------------------------------------------------------- animation

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!_struck)
            {
                if (strike != null) AnimateTelegraph(strike.Point, strike.TelegraphProgress);
                return;
            }

            _fxAge += dt;
            AnimateBolt(dt);
            if (_fxAge >= lingerSeconds) Destroy(gameObject);
        }

        void AnimateTelegraph(Vector3 point, float t)
        {
            float ease = 1f - (1f - t) * (1f - t);
            float flicker = 0.75f + 0.25f * Mathf.Sin((Time.time + _seed) * 37f);

            if (ring != null)
            {
                float radius = _radius * Mathf.Lerp(ringStartScale, 1f, ease);
                DrawCircle(ring, point + Vector3.up * 0.06f, radius);
                ring.widthMultiplier = Mathf.Lerp(0.05f, 0.12f, ease);
                Tint(ring, telegraphColour, 1f, flicker);
            }
            if (marker != null)
            {
                marker.transform.position = point + Vector3.up * 0.04f;
                float size = _radius * 2f * Mathf.Max(0.05f, ease);
                marker.transform.localScale = new Vector3(size, size, 1f);
                Tint(marker, telegraphColour, 1f + ease, 0.35f * ease * flicker);
            }
            if (cloud != null)
            {
                cloud.position = point + Vector3.up * cloudHeight;
                cloud.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, ease);
                if (cloudPuffs != null && _cloudModel == null)
                    foreach (var puff in cloudPuffs)
                        if (puff != null) Tint(puff, cloudColour, 1f, Mathf.Clamp01(ease * 1.4f));
            }
            if (cloudLight != null)
            {
                // Sheet lightning inside the cloud: rare bright flickers, growing more frequent.
                float n = Mathf.PerlinNoise(Time.time * 18f, _seed);
                cloudLight.intensity = n > 1f - 0.45f * ease ? 4f * ease : 0.2f;
            }
        }

        void AnimateBolt(float dt)
        {
            _sinceFlicker += dt;
            bool rejitter = _sinceFlicker >= flickerSeconds;
            if (rejitter) _sinceFlicker = 0f;

            float boltT = Mathf.Clamp01(_fxAge / boltSeconds);
            bool boltLive = boltT < 1f;
            if (bolt != null && _boltModel == null)
            {
                bolt.enabled = boltLive;
                if (boltLive)
                {
                    if (rejitter)
                    {
                        Jagged(_boltPoints, _top, _point, boltJitter);
                        bolt.positionCount = _boltPoints.Length;
                        bolt.SetPositions(_boltPoints);
                    }
                    bolt.widthMultiplier = boltWidth * (1f - 0.6f * boltT);
                    Tint(bolt, boltColour, intensity * (1f + Random.value * 0.5f), 1f - boltT * boltT);
                }
            }
            if (boltGlow != null && _boltModel == null)
            {
                boltGlow.enabled = boltLive;
                if (boltLive)
                {
                    if (rejitter)
                    {
                        boltGlow.positionCount = _boltPoints.Length;
                        boltGlow.SetPositions(_boltPoints);
                    }
                    boltGlow.widthMultiplier = boltWidth * 3.2f * (1f - 0.5f * boltT);
                    Tint(boltGlow, glowColour, 1f, 1f - boltT);
                }
            }
            if (branch != null && _boltModel == null)
            {
                branch.enabled = boltLive;
                if (boltLive && rejitter)
                {
                    Vector3 start = _boltPoints[BoltSegments / 3];
                    Vector3 side = new(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                    Vector3 end = start + (Vector3.down * 0.45f + side.normalized * 0.35f) * cloudHeight * 0.45f;
                    Jagged(_branchPoints, start, end, boltJitter * 0.5f);
                    branch.positionCount = _branchPoints.Length;
                    branch.SetPositions(_branchPoints);
                }
                if (boltLive) Tint(branch, boltColour, intensity * 0.7f, 1f - boltT);
            }
            if (_boltModel != null)
            {
                _boltModel.SetActive(boltLive && (Random.value > 0.15f));   // crackle
            }

            if (chains != null)
            {
                float chainT = Mathf.Clamp01(_fxAge / chainSeconds);
                for (int i = 0; i < chains.Length; i++)
                {
                    var line = chains[i];
                    if (line == null) continue;
                    bool live = i < _chainCount && chainT < 1f;
                    line.enabled = live;
                    if (!live) continue;
                    if (rejitter)
                    {
                        Jagged(_chainPoints, _chainFrom[i], _chainTo[i], chainJitter);
                        line.positionCount = _chainPoints.Length;
                        line.SetPositions(_chainPoints);
                    }
                    Tint(line, glowColour, 1.3f, Mathf.Clamp01(1.6f * (1f - chainT)));
                }
            }

            float shockT = Mathf.Clamp01(_fxAge / shockSeconds);
            if (shock != null)
            {
                shock.enabled = shockT < 1f;
                if (shock.enabled)
                {
                    float rest = 1f - shockT;
                    float grow = 1f - rest * rest * rest;
                    float quad = _radius * 2f * grow / ringTextureFill;
                    shock.transform.position = _point + Vector3.up * 0.05f;
                    shock.transform.localScale = new Vector3(quad, quad, 1f);
                    Tint(shock, boltColour, intensity, rest * rest);
                }
            }
            if (flash != null)
            {
                float fade = 1f - Mathf.Clamp01(_fxAge / (boltSeconds * 1.6f));
                flash.enabled = fade > 0f;
                flash.transform.position = _impact + Vector3.up * 0.5f;
                flash.intensity = flashIntensity * fade * fade * (rejitter ? 1f : 0.85f);
            }
            if (cloud != null)
            {
                float cloudFade = 1f - Mathf.Clamp01(_fxAge / lingerSeconds);
                if (cloudPuffs != null && _cloudModel == null)
                    foreach (var puff in cloudPuffs)
                        if (puff != null) Tint(puff, cloudColour, 1f, cloudFade);
                if (_cloudModel != null) _cloudModel.SetActive(cloudFade > 0.1f);
            }
            if (cloudLight != null) cloudLight.intensity = boltLive ? 6f : 0f;
        }

        // ------------------------------------------------------------ helpers

        void ShowTelegraph(bool on)
        {
            if (ring != null) ring.enabled = on;
            if (marker != null) marker.enabled = on;
            if (cloud != null) cloud.gameObject.SetActive(on);
        }

        void ShowBolt(bool on)
        {
            if (bolt != null) bolt.enabled = on;
            if (boltGlow != null) boltGlow.enabled = on;
            if (branch != null) branch.enabled = on;
            if (chains != null) foreach (var c in chains) if (c != null) c.enabled = on && _chainCount > 0;
            if (flash != null) flash.enabled = on;
            if (shock != null) shock.enabled = on;
        }

        void DrawCircle(LineRenderer line, Vector3 centre, float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * TwoPi / RingSegments;
                _ringPoints[i] = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            line.loop = true;
            line.positionCount = RingSegments;
            line.SetPositions(_ringPoints);
        }

        /// <summary>A jagged path from <paramref name="from"/> to <paramref name="to"/>:
        /// straight ends, random sideways wander peaking mid-way.</summary>
        static void Jagged(Vector3[] points, Vector3 from, Vector3 to, float jitter)
        {
            int n = points.Length - 1;
            Vector3 along = to - from;
            Vector3 side = Vector3.Cross(along, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 other = Vector3.Cross(along, side).normalized;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float envelope = Mathf.Sin(t * Mathf.PI);
                Vector3 offset = (side * Random.Range(-1f, 1f) + other * Random.Range(-1f, 1f)) * (jitter * envelope);
                points[i] = from + along * t + offset;
            }
        }

        void Tint(Renderer target, Color colour, float brightness, float alpha)
        {
            _block.SetColor(BaseColorId, new Color(colour.r * brightness, colour.g * brightness,
                                                   colour.b * brightness, colour.a * Mathf.Clamp01(alpha)));
            target.SetPropertyBlock(_block);
        }
    }
}
