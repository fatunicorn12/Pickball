namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>Where the cameras workstream's assets, scene objects and reports live.</summary>
    public static class CameraAssetPaths
    {
        /// <summary>Asset folder.</summary>
        public const string Dir = "Assets/_Project/ScriptableObjects/Cutscene/Cameras";

        /// <summary>The shot table.</summary>
        public const string ShotTable = Dir + "/OpeningShotTable.asset";

        /// <summary>Handheld noise profile (a copy of Cinemachine's Handheld_normal_mild).</summary>
        public const string NoiseHandheld = Dir + "/Noise_Handheld.asset";

        /// <summary>Impact shake profile (a copy of Cinemachine's 6D Shake).</summary>
        public const string NoiseShake = Dir + "/Noise_Shake6D.asset";

        /// <summary>Cinemachine's preset the handheld profile is copied from.</summary>
        public const string PresetHandheld = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

        /// <summary>Cinemachine's preset the shake profile is copied from.</summary>
        public const string PresetShake = "Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset";

        /// <summary>The depth-of-field volume profile for an f-number and vertical FOV.</summary>
        public static string DofProfile(float aperture, float fov) =>
            Dir + "/DoF_f" + aperture.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
            "_" + fov.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "deg.asset";

        /// <summary>The scene root every camera object lives under.</summary>
        public const string RootName = "CutsceneCameras";

        /// <summary>The timeline group the camera track lives in.</summary>
        public const string GroupName = "Cameras";

        /// <summary>The Cinemachine track's name.</summary>
        public const string TrackName = "Cinemachine";

        /// <summary>Prefix of the director's exposed-reference names for the shots' cameras.</summary>
        public const string RefPrefix = "occam:";

        /// <summary>The freeze-frame camera shown between timelines.</summary>
        public const string HoldName = "Hold (between timelines)";

        /// <summary>The shared look-at object the composers point at (the rigs override the point).</summary>
        public const string AimTargetName = "AimTarget";

        /// <summary>The scene-4 camera track spline.</summary>
        public const string TrackPathName = "Cam_Track_S4 Spline";

        /// <summary>The proxy the scene-4 dolly stays abeam of (MC.Mid).</summary>
        public const string TrackTargetName = "Track Target (MC.Mid)";

        /// <summary>The framing report.</summary>
        public const string FramingReport = "Logs/CutsceneFramingReport.txt";

        /// <summary>The validate menu.</summary>
        public const string ValidateMenu = CutsceneMenus.Root + "Validate Framing";
    }
}
