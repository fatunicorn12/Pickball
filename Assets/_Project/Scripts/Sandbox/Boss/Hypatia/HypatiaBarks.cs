using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Hypatia's voice. Elegant and sorrowful: a teacher of Alexandria who knows
    /// how the year 415 ends for her and lectures anyway. The lines are
    /// placeholders for the story owner to replace.
    ///
    /// Same shape as <see cref="NewtonBarks"/>, with the same <see cref="Spoke"/>
    /// signature, so the HUD's bark view can show either. (Its <c>source</c> field
    /// is typed <see cref="NewtonBarks"/> today; it needs a Hypatia source field
    /// or a shared interface before it can be wired to her.) Lines
    /// are picked per occasion and rate-limited, so the brain can call
    /// <see cref="Say"/> on every ring and volley without the HUD becoming a wall
    /// of text. Pacing uses real time (<see cref="Time.time"/>), not her slow
    /// clock: barks are presentation, and a frozen boss should still be heard.
    /// Nothing here knows about UI.
    /// </summary>
    public class HypatiaBarks : MonoBehaviour
    {
        /// <summary>What prompted a line. Intro, PhaseTwo and Death always play;
        /// the rest are rate-limited and chance-gated.</summary>
        public enum Occasion
        {
            /// <summary>The fight begins.</summary>
            Intro,
            /// <summary>A ring was forecast.</summary>
            Rings,
            /// <summary>The needle shows e = 1.</summary>
            Parabola,
            /// <summary>The needle shows e &lt; 1.</summary>
            Ellipse,
            /// <summary>The needle shows e &gt; 1.</summary>
            Hyperbola,
            /// <summary>The recalibration window opened.</summary>
            Recalibration,
            /// <summary>Alexandria's Burning began.</summary>
            PhaseTwo,
            /// <summary>She took a hit.</summary>
            Hurt,
            /// <summary>She died.</summary>
            Death,
            /// <summary>Nothing happened for a while.</summary>
            Unprompted,
        }

        [Tooltip("Minimum seconds (real time) between two lines, except Intro, Phase Two " +
                 "and Death, which always play. Higher = quieter.")]
        [Min(0f)]
        [SerializeField] private float minGap = 5f;

        [Tooltip("Chance (0-1) that a ring, conic, recalibration or hurt gets a line at all. " +
                 "Rings come every few seconds, so keep this well below 1.")]
        [Range(0f, 1f)]
        [SerializeField] private float attackChance = 0.4f;

        [Tooltip("Seconds each line stays on screen.")]
        [Min(0.1f)]
        [SerializeField] private float lineDuration = 3.6f;

        [SerializeField] private string[] intro =
        {
            "Sit, student. Alexandria taught the world to read the sky. Today it teaches you.",
            "I am Hypatia, daughter of Theon. The lecture is the heavens; the hall, as you see, is burning soon.",
            "The astrolabe turns whether we will it or not. So does the crowd outside my door.",
        };

        [SerializeField] private string[] rings =
        {
            "The spheres turn, and the gap turns with them. Follow the spiral.",
            "Ptolemy drew these circles. I only set them moving.",
            "Every orbit leaves a door. Find it before it closes.",
        };

        [SerializeField] private string[] parabola =
        {
            "Eccentricity one. Apollonius called it the parabola: the thrown stone's honest path.",
            "Exactly one. You know where it lands, if you know where you will be.",
        };

        [SerializeField] private string[] ellipse =
        {
            "Less than one: the ellipse. The wanderers never leave me.",
            "Apollonius taught me this curve. It always comes back, as I do to his books.",
        };

        [SerializeField] private string[] hyperbola =
        {
            "Greater than one. It passes once and never returns, like so much of Alexandria.",
            "Two asymptotes, one chance to step aside. Read them.",
        };

        [SerializeField] private string[] recalibration =
        {
            "Hold still, needle... let me find the pole again.",
            "The instrument drifts. Even I must stop to correct it.",
            "A moment. My father checked every table twice; so do I.",
        };

        [SerializeField] private string[] phaseTwo =
        {
            "Do you smell the smoke? The library burns, scroll by scroll.",
            "Four hundred and fifteen. I know how this year ends. Let us finish the lesson.",
        };

        [SerializeField] private string[] hurt =
        {
            "You strike like the mob in the street.",
            "A teacher bleeds like anyone. They will learn that soon enough.",
            "Careful. These hands drew the tables you steer by.",
        };

        [SerializeField] private string[] death =
        {
            "Keep the books... keep them, if you can...",
            "Remember the curves, student. Someone must remember.",
        };

        [SerializeField] private string[] unprompted =
        {
            "My father and I corrected Ptolemy's tables by lamplight. Those were good nights.",
            "They call my mathematics heresy. It is only the sky, written down.",
            "Measure twice. The stars are patient; people are not.",
        };

        /// <summary>Raised with the line and how long to show it, in seconds. The
        /// same signature as <see cref="NewtonBarks.Spoke"/>, so one view serves both.</summary>
        public event Action<string, float> Spoke;

        float _lastSpoke = -999f;
        string _lastLine;

        /// <summary>
        /// Maybe say a line for <paramref name="occasion"/>. Safe to call on every
        /// event: ordinary occasions are dropped inside <c>minGap</c> or by
        /// <c>attackChance</c>; Intro, PhaseTwo and Death always speak. The same
        /// line is never said twice in a row when the occasion has another.
        /// </summary>
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
                Occasion.Rings => rings,
                Occasion.Parabola => parabola,
                Occasion.Ellipse => ellipse,
                Occasion.Hyperbola => hyperbola,
                Occasion.Recalibration => recalibration,
                Occasion.PhaseTwo => phaseTwo,
                Occasion.Hurt => hurt,
                Occasion.Death => death,
                _ => unprompted,
            };
            if (lines == null || lines.Length == 0) return;

            int index = UnityEngine.Random.Range(0, lines.Length);
            if (lines.Length > 1 && lines[index] == _lastLine) index = (index + 1) % lines.Length;
            _lastLine = lines[index];

            _lastSpoke = Time.time;
            Spoke?.Invoke(lines[index], lineDuration);
        }
    }
}
