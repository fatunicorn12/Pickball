using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Unity muscle names, their clip curve names, and the small pose vocabulary the cutscene
    /// clips are written in ("fist", "look", "arms spread"). Signs were measured on the chibi
    /// avatar (2026-10-29) by setting one muscle and reading the bone direction back:
    /// <list type="bullet">
    /// <item>Arm Down-Up: −0.5 hangs straight down, 0 is 55° below horizontal, 0.5 horizontal,
    /// 1 is 40° above (add Shoulder Down-Up for more).</item>
    /// <item>Arm Front-Back: negative swings the arm forward (−1 forward and across), positive back.</item>
    /// <item>Forearm / Lower Leg Stretch: 1 straight, 0 a right angle, −1 folded.</item>
    /// <item>Upper Leg Front-Back: ~0.6 straight down, 0 is 27° forward, −0.5 near horizontal.</item>
    /// <item>Upper Leg In-Out: positive spreads the leg out. Head Nod Down-Up: positive looks up.</item>
    /// </list>
    /// </summary>
    public static class HumanMuscles
    {
        private static Dictionary<string, int> _index;

        /// <summary>Index of a muscle name in <see cref="HumanTrait.MuscleName"/>, or −1.</summary>
        public static int Index(string muscle)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, int>();
                string[] names = HumanTrait.MuscleName;
                for (int i = 0; i < names.Length; i++) _index[names[i]] = i;
            }
            return muscle != null && _index.TryGetValue(muscle, out int idx) ? idx : -1;
        }

        /// <summary>
        /// The animation-clip attribute for a muscle. Body muscles use the muscle name; finger
        /// muscles are written "LeftHand.Index.1 Stretched" in clips but "Left Index 1 Stretched"
        /// by HumanTrait.
        /// </summary>
        public static string ClipAttribute(int muscleIndex)
        {
            string name = HumanTrait.MuscleName[muscleIndex];
            string[] digits = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string d in digits)
                {
                    string prefix = side + " " + d + " ";
                    if (!name.StartsWith(prefix)) continue;
                    string rest = name.Substring(prefix.Length); // "1 Stretched" or "Spread"
                    return side + "Hand." + d + "." + rest;
                }
            }
            return name;
        }

        /// <summary>"Left …" / "Right …" muscle name.</summary>
        public static string Side(bool right, string muscle) => (right ? "Right " : "Left ") + muscle;

        private static KeyValuePair<string, float> P(string n, float v) => new KeyValuePair<string, float>(n, v);

        /// <summary>A closed fist (thumb over the fingers). 0 = relaxed, 1 = clenched.</summary>
        public static List<KeyValuePair<string, float>> Fist(bool right, float closed = 1f)
        {
            var l = new List<KeyValuePair<string, float>>();
            foreach (string d in new[] { "Index", "Middle", "Ring", "Little" })
            {
                l.Add(P(Side(right, d + " 1 Stretched"), Mathf.Lerp(0.4f, -0.9f, closed)));
                l.Add(P(Side(right, d + " 2 Stretched"), Mathf.Lerp(0.4f, -1f, closed)));
                l.Add(P(Side(right, d + " 3 Stretched"), Mathf.Lerp(0.4f, -1f, closed)));
                l.Add(P(Side(right, d + " Spread"), 0f));
            }
            l.Add(P(Side(right, "Thumb 1 Stretched"), Mathf.Lerp(0.2f, -0.4f, closed)));
            l.Add(P(Side(right, "Thumb 2 Stretched"), Mathf.Lerp(0.2f, -0.6f, closed)));
            l.Add(P(Side(right, "Thumb 3 Stretched"), Mathf.Lerp(0.2f, -0.6f, closed)));
            return l;
        }

        /// <summary>An open hand, fingers straight and a little spread.</summary>
        public static List<KeyValuePair<string, float>> OpenHand(bool right, float spread = 0.3f)
        {
            var l = new List<KeyValuePair<string, float>>();
            foreach (string d in new[] { "Index", "Middle", "Ring", "Little" })
            {
                l.Add(P(Side(right, d + " 1 Stretched"), 0.9f));
                l.Add(P(Side(right, d + " 2 Stretched"), 0.9f));
                l.Add(P(Side(right, d + " 3 Stretched"), 0.9f));
                l.Add(P(Side(right, d + " Spread"), spread));
            }
            l.Add(P(Side(right, "Thumb 1 Stretched"), 0.6f));
            l.Add(P(Side(right, "Thumb 2 Stretched"), 0.6f));
            l.Add(P(Side(right, "Thumb 3 Stretched"), 0.6f));
            return l;
        }

        /// <summary>A pointing hand: index straight, the rest curled.</summary>
        public static List<KeyValuePair<string, float>> Point(bool right)
        {
            var l = Fist(right);
            l.Add(P(Side(right, "Index 1 Stretched"), 0.9f));
            l.Add(P(Side(right, "Index 2 Stretched"), 0.9f));
            l.Add(P(Side(right, "Index 3 Stretched"), 0.9f));
            return l;
        }

        /// <summary>
        /// Where the head looks, split over neck (40%) and head (60%) so it bends rather than
        /// pivots. nod: +up / −down; turn and tilt in muscle units (±1 = full range).
        /// </summary>
        public static List<KeyValuePair<string, float>> Look(float nod, float turn, float tilt = 0f)
        {
            return new List<KeyValuePair<string, float>>
            {
                P("Neck Nod Down-Up", nod * 0.4f), P("Head Nod Down-Up", nod * 0.6f),
                P("Neck Turn Left-Right", turn * 0.4f), P("Head Turn Left-Right", turn * 0.6f),
                P("Neck Tilt Left-Right", tilt * 0.4f), P("Head Tilt Left-Right", tilt * 0.6f),
            };
        }

        /// <summary>One arm: down-up, front-back, forearm stretch, twist (muscle units).</summary>
        public static List<KeyValuePair<string, float>> Arm(bool right, float downUp, float frontBack, float forearm,
            float twist = 0f, float shoulderUp = 0f)
        {
            return new List<KeyValuePair<string, float>>
            {
                P(Side(right, "Arm Down-Up"), downUp), P(Side(right, "Arm Front-Back"), frontBack),
                P(Side(right, "Forearm Stretch"), forearm), P(Side(right, "Arm Twist In-Out"), twist),
                P(Side(right, "Shoulder Down-Up"), shoulderUp), P(Side(right, "Shoulder Front-Back"), 0f),
            };
        }

        /// <summary>One leg: thigh front-back and in-out, knee stretch, foot up-down.</summary>
        public static List<KeyValuePair<string, float>> Leg(bool right, float thighFrontBack, float thighInOut,
            float knee, float foot = 0f)
        {
            return new List<KeyValuePair<string, float>>
            {
                P(Side(right, "Upper Leg Front-Back"), thighFrontBack), P(Side(right, "Upper Leg In-Out"), thighInOut),
                P(Side(right, "Lower Leg Stretch"), knee), P(Side(right, "Foot Up-Down"), foot),
                P(Side(right, "Upper Leg Twist In-Out"), 0f), P(Side(right, "Lower Leg Twist In-Out"), 0f),
            };
        }

        /// <summary>Spine, chest and upper chest bend split evenly (front-back +forward, left-right, twist).</summary>
        public static List<KeyValuePair<string, float>> Torso(float frontBack, float leftRight = 0f, float twist = 0f)
        {
            return new List<KeyValuePair<string, float>>
            {
                P("Spine Front-Back", frontBack), P("Chest Front-Back", frontBack), P("UpperChest Front-Back", frontBack),
                P("Spine Left-Right", leftRight), P("Chest Left-Right", leftRight), P("UpperChest Left-Right", leftRight),
                P("Spine Twist Left-Right", twist), P("Chest Twist Left-Right", twist), P("UpperChest Twist Left-Right", twist),
            };
        }
    }
}
