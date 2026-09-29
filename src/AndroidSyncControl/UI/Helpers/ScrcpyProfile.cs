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
                // Software fallback: Google H.264 software encoder (safe 720p, 3M, 30fps, zero buffer)
                return $"-s {serial} --power-on --stay-awake --no-audio --video-buffer=0 --tunnel-host=127.0.0.1 --max-size 720 --video-bit-rate=3M --max-fps=30 --video-codec=h264 --video-encoder=OMX.google.h264.encoder --keyboard=sdk --shortcut-mod=lctrl,rctrl --window-title \"{screenTitle}\"";
            }

            // Hardware encoder: ultra-low latency 60fps (max-size 1024, 6M bitrate, zero buffer, instant power-on)
            // 1024 dimension is optimal: 2.5x faster encoding on Exynos/Snapdragon SoCs than 1280/1440 while retaining sharp display
            return $"-s {serial} --power-on --stay-awake --no-audio --video-buffer=0 --tunnel-host=127.0.0.1 --max-size 1024 --video-bit-rate=6M --max-fps=60 --keyboard=sdk --shortcut-mod=lctrl,rctrl --window-title \"{screenTitle}\"";
        }
    }
}
