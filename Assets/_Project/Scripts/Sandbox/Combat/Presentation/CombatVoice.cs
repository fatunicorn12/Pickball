using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// The player's voice in a fight: a grunt as each swing's blade goes live, rising
    /// through the chain to a finisher yell, and a hurt sound when he is hit.
    ///
    /// Listens to <see cref="MeleeCombat"/> and <see cref="Health"/>; neither knows this
    /// exists, so the component can be removed without touching gameplay. A swing that
    /// is interrupted before its blade goes live stays silent, and a miss still grunts.
    ///
    /// He has one voice: a new sound fades out whatever is playing instead of overlapping
    /// it, which is why this uses its own pair of AudioSources and not PlayOneShot.
    /// </summary>
    public class CombatVoice : MonoBehaviour
    {
        [Tooltip("The swings to voice. Found on this object if left empty.")]
        [SerializeField] private MeleeCombat combat;

        [Tooltip("The clips and how they play. Created by Tools ▸ Pickleball ▸ Setup Combat Voice.")]
        [SerializeField] private CombatVoiceBank bank;

        [Tooltip("Whose hurt sounds to play. Found on this object if left empty; with none " +
                 "at all, only the attack grunts play.")]
        [SerializeField] private Health health;

        const int TierCount = 4;

        // All rebuilt in OnEnable, so a script reload in Play mode starts clean.
        GameObject _audioRoot;
        AudioSource _playing, _fading;
        float _fadeStartVolume;
        float _fadeLeft;
        ShuffleBag[] _tierBags;
        ShuffleBag _hurtBag;
        float _nextHurtTime;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (health == null) health = GetComponent<Health>();
        }

        void OnEnable()
        {
            if (bank == null)
            {
                Debug.LogWarning($"[CombatVoice] {name} has no CombatVoiceBank, so it stays " +
                                 "silent. Run Tools ▸ Pickleball ▸ Setup Combat Voice.", this);
                return;
            }

            _audioRoot = new GameObject($"{name}_CombatVoice");
            _audioRoot.transform.SetParent(transform, false);
            _playing = NewSource();
            _fading = NewSource();
            _fadeLeft = 0f;
            _nextHurtTime = 0f;

            var random = new System.Random();
            _tierBags = new ShuffleBag[TierCount];
            for (int i = 0; i < TierCount; i++)
                _tierBags[i] = new ShuffleBag(bank.GroupFor((GruntTier)i).Clips.Length, random);
            _hurtBag = new ShuffleBag(bank.Hurt.Clips.Length, random);

            if (combat != null) combat.BladeLiveChanged += OnBladeLive;
            if (health != null) health.Hurt += OnHurt;
        }

        void OnDisable()
        {
            if (combat != null) combat.BladeLiveChanged -= OnBladeLive;
            if (health != null) health.Hurt -= OnHurt;
            if (_audioRoot != null) Destroy(_audioRoot);
            _audioRoot = null;
            _playing = _fading = null;
        }

        AudioSource NewSource()
        {
            var source = _audioRoot.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        void Update()
        {
            if (_fadeLeft <= 0f) return;
            // Unscaled, so a slow-motion moment can't stretch the fade into an overlap.
            _fadeLeft -= Time.unscaledDeltaTime;
            if (_fadeLeft > 0f)
            {
                _fading.volume = _fadeStartVolume * (_fadeLeft / bank.HandoffFadeSeconds);
                return;
            }
            _fading.Stop();
        }

        void OnBladeLive(bool live)
        {
            if (!live) return;
            var step = combat.Current;
            if (!step.IsValid) return;
            var tier = GruntLadder.TierFor(step, combat.PreviewChain(step.Track));
            Say(bank.GroupFor(tier), _tierBags[(int)tier]);
        }

        void OnHurt(float amount, Vector3 at)
        {
            if (!health.LastHitDiscrete || Time.time < _nextHurtTime) return;
            _nextHurtTime = Time.time + bank.HurtMinIntervalSeconds;
            Say(bank.Hurt, _hurtBag);
        }

        void Say(CombatVoiceGroup group, ShuffleBag bag)
        {
            int index = bag.Next();
            if (index < 0) return;
            var clip = group.Clips[index];
            if (clip == null) return;

            // The sound that is playing becomes the one that fades out.
            (_playing, _fading) = (_fading, _playing);
            _fadeStartVolume = _fading.volume;
            _fadeLeft = _fading.isPlaying ? bank.HandoffFadeSeconds : 0f;

            float spread = bank.PitchVariation;
            _playing.Stop();
            _playing.clip = clip;
            _playing.volume = group.Volume;
            _playing.pitch = Random.Range(1f - spread, 1f + spread);
            _playing.Play();
        }
    }
}
