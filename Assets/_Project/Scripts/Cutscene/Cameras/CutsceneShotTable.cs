using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The opening's shot list as camera data: one <see cref="ShotRigSpec"/> per shot, the noise
    /// profiles and the framing rules the validator applies. Every number the shot list gives a
    /// camera lives here, not in code, so a director can tune a shot in Play mode and keep it. The
    /// cameras setup menu seeds it from the shot list once and afterwards only adds missing shots,
    /// so tuning survives a rebuild.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Shot Table", fileName = "OpeningShotTable")]
    public sealed class CutsceneShotTable : ScriptableObject
    {
        [Header("Shots")]
        [Tooltip("Every shot, by id. Order is the shot list's.")]
        [SerializeField] private List<ShotRigSpec> shots = new List<ShotRigSpec>();

        [Header("Noise profiles")]
        [Tooltip("Gentle handheld drift for the shot list's \"handheld (amplitude n)\" shots.")]
        [SerializeField] private NoiseSettings handheld;

        [Tooltip("6D shake for impact bursts (S1_SH04's \"Isekai!\").")]
        [SerializeField] private NoiseSettings shake6D;

        [Header("Framing rules")]
        [Tooltip("Aspect ratio compositions are made and checked for (16:9). Wider phones only gain width.")]
        [Min(0.5f)]
        [SerializeField] private float aspect = 16f / 9f;

        [Tooltip("Safe frame: the central share of the frame a subject must stay in (0.9 = 90% × 90%).")]
        [Range(0.5f, 1f)]
        [SerializeField] private float safeFrame = 0.9f;

        [Tooltip("Birdy frac at or above which a framing is a tight close-up and needs an allowed window " +
                 "(the bottom of the banned band).")]
        [Range(0.1f, 0.6f)]
        [SerializeField] private float tcuFlagFraction = 0.30f;

        /// <summary>Number of shots.</summary>
        public int Count => shots.Count;

        /// <summary>The shot at an index, for iteration without allocating.</summary>
        public ShotRigSpec Get(int index) => shots[index];

        /// <summary>Gentle handheld profile.</summary>
        public NoiseSettings Handheld => handheld;

        /// <summary>Impact shake profile.</summary>
        public NoiseSettings Shake6D => shake6D;

        /// <summary>Composition aspect ratio.</summary>
        public float Aspect => aspect;

        /// <summary>Safe-frame share (0.9).</summary>
        public float SafeFrame => safeFrame;

        /// <summary>The banned band's lower edge (0.30).</summary>
        public float TcuFlagFraction => tcuFlagFraction;

        /// <summary>Finds a shot by id. Linear over 52 entries, no allocation.</summary>
        public bool TryGet(string shotId, out ShotRigSpec spec)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] != null && shots[i].shotId == shotId)
                {
                    spec = shots[i];
                    return true;
                }
            }
            spec = null;
            return false;
        }

        /// <summary>The noise profile for a kind, or null.</summary>
        public NoiseSettings Noise(NoiseKind kind) =>
            kind == NoiseKind.Handheld ? handheld : kind == NoiseKind.Shake6D ? shake6D : null;

        /// <summary>The seed version the shots came from (see <see cref="Merge"/>).</summary>
        public int SeedVersion => seedVersion;

        [Header("Seed")]
        [Tooltip("Version of the code seed these shots came from. When the seed's version changes the " +
                 "setup menu reseeds every shot (tuning is lost, with a warning); otherwise it only adds " +
                 "missing shots.")]
        [SerializeField] private int seedVersion;

        /// <summary>
        /// Setup-menu entry point. With the same <paramref name="version"/> as last time it adds the
        /// seeded shots the table lacks and keeps every tuned shot as it is; with a new version it
        /// replaces them all. Sets the noise profiles. Returns how many shots were (re)seeded.
        /// </summary>
        public int Merge(IReadOnlyList<ShotRigSpec> seeds, int version, NoiseSettings handheldProfile, NoiseSettings shakeProfile)
        {
            handheld = handheldProfile;
            shake6D = shakeProfile;
            if (version != seedVersion)
            {
                seedVersion = version;
                shots = new List<ShotRigSpec>(seeds);
                return seeds.Count;
            }
            int added = 0;
            var merged = new List<ShotRigSpec>(seeds.Count);
            foreach (var seed in seeds)
            {
                if (TryGet(seed.shotId, out var existing)) merged.Add(existing);
                else
                {
                    merged.Add(seed);
                    added++;
                }
            }
            // Shots the seed no longer lists are kept at the end so nothing tuned is lost.
            foreach (var s in shots)
                if (s != null && !merged.Contains(s)) merged.Add(s);
            shots = merged;
            return added;
        }
    }
}
