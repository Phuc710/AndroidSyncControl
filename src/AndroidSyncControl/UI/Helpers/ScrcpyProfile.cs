using System;
using System.Collections.Concurrent;

namespace AndroidSyncControl.UI.Helpers
{
    public enum EncoderPreference
    {
        AutoHardware,       // Default hardware encoder (Vendor native: Qualcomm, MediaTek, Exynos, Tensor, etc.)
        SoftwareFallback    // Software encoder (OMX.google.h264.encoder) with safe flags
    }

    public static class ScrcpyProfile
    {
        // Cache per device serial to remember if hardware encoder failed
        private static readonly ConcurrentDictionary<string, EncoderPreference> DevicePreferences =
            new ConcurrentDictionary<string, EncoderPreference>(StringComparer.OrdinalIgnoreCase);

        public static EncoderPreference GetPreference(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return EncoderPreference.AutoHardware;
            return DevicePreferences.TryGetValue(serial, out var pref) ? pref : EncoderPreference.AutoHardware;
        }

        public static void MarkHardwareEncoderFailed(string serial)
        {
            if (!string.IsNullOrEmpty(serial))
            {
                DevicePreferences[serial] = EncoderPreference.SoftwareFallback;
            }
        }

        public static void ResetPreference(string serial)
        {
            if (!string.IsNullOrEmpty(serial))
            {
                DevicePreferences.TryRemove(serial, out _);
            }
        }

        public static string BuildArguments(string serial, string screenTitle, string deviceModel = "")
        {
            var pref = GetPreference(serial);
            bool useSoftware = (pref == EncoderPreference.SoftwareFallback);

            if (useSoftware)
            {
                // Software fallback: Google H.264 software encoder (safe 720p, 4M, 30fps)
                return $"-s {serial} --stay-awake --no-audio --max-size 720 --video-bit-rate=4M --max-fps=30 --video-codec=h264 --video-encoder=OMX.google.h264.encoder --window-title \"{screenTitle}\"";
            }

            // Hardware encoder: native high clarity up to 1280 max dimension, 8M bitrate, 60 fps
            return $"-s {serial} --stay-awake --no-audio --max-size 1280 --video-bit-rate=8M --max-fps=60 --window-title \"{screenTitle}\"";
        }
    }
}
