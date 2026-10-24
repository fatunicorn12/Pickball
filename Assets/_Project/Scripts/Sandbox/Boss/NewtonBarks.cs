using System;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Newton's mouth. Arrogant, imperious, and unable to go a full fight without
    /// mentioning that calculus was his idea.
    ///
    /// Lines are picked per occasion and rate-limited, so the boss can call
    /// <see cref="Say"/> on every attack without the HUD turning into a wall of
    /// text. A <see cref="BarkView"/> on the HUD listens to <see cref="Spoke"/>;
    /// nothing here knows about UI.
    /// </summary>
    public class NewtonBarks : MonoBehaviour
    {
        public enum Occasion { Intro, Well, Beam, Volley, PhaseTwo, Barrage, Hurt, Death, Unprompted }

        [Tooltip("Minimum seconds between two lines, except Intro/PhaseTwo/Death, " +
                 "which always play.")]
        [SerializeField] private float minGap = 5f;

        [Tooltip("Chance an ordinary attack gets a line at all.")]
        [Range(0f, 1f)]
        [SerializeField] private float attackChance = 0.45f;

        [SerializeField] private float lineDuration = 3.2f;

        [SerializeField] private string[] intro =
        {
            "You stand before the man who explained the heavens. Kneel, or fall. Either obeys my law.",
            "I invented calculus. Leibniz merely invented notation and a grudge.",
        };

        [SerializeField] private string[] well =
        {
            "Every body attracts every other body. You are no exception.",
            "Feel that? That is me, inversely squared.",
            "Down is wherever I say it is.",
        };

        [SerializeField] private string[] beam =
        {
            "The fluxion of your position, student. Move and pay for it.",
            "Stand still. Or do you not understand rates of change?",
            "Leibniz would call this a 'derivative'. Plagiarist.",
        };

        [SerializeField] private string[] volley =
        {
            "Closer, and it hurts the square of closer.",
            "One over r squared. Memorise it on the way down.",
        };

        [SerializeField] private string[] phaseTwo =
        {
            "Enough theory. Let me show you the apple.",
        };

        [SerializeField] private string[] barrage =
        {
            "Apples! Hundreds of them! I have been saving these.",
            "It was one apple. It inspired me. These are the rest of the orchard.",
        };

        [SerializeField] private string[] hurt =
        {
            "You dare? I was president of the Royal Society!",
            "A lucky blow. Statistically insignificant.",
            "Even Leibniz never struck me.",
        };

        [SerializeField] private string[] death =
        {
            "If I have seen further... it is because... you stood on me...",
        };

        [SerializeField] private string[] unprompted =
        {
            "Did I mention I invented calculus? I did. Not him. Me.",
            "I also did optics. And alchemy. You're welcome.",
        };

        /// <summary>Raised with the line and how long to show it.</summary>
        public event Action<string, float> Spoke;

        float _lastSpoke = -999f;
        int _lastIndex = -1;

        public void Say(Occasion occasion)
        {
            bool always = occasion is Occasion.Intro or Occasion.PhaseTwo or Occasion.Death;
            if (!always)
            {
                if (Time.time - _lastSpoke < minGap) return;
                if (occasion != Occasion.Unprompted && UnityEngine.Random.value > attackChance) return;
            }

            string[] lines = occasion switch
            {
                Occasion.Intro => intro,
                Occasion.Well => well,
                Occasion.Beam => beam,
                Occasion.Volley => volley,
                Occasion.PhaseTwo => phaseTwo,
                Occasion.Barrage => barrage,
                Occasion.Hurt => hurt,
                Occasion.Death => death,
                _ => unprompted,
            };
            if (lines == null || lines.Length == 0) return;

            // Avoid saying the same line twice in a row when there is a choice.
            int index = UnityEngine.Random.Range(0, lines.Length);
            if (lines.Length > 1 && index == _lastIndex) index = (index + 1) % lines.Length;
            _lastIndex = index;

            _lastSpoke = Time.time;
            Spoke?.Invoke(lines[index], lineDuration);
        }
    }
}
