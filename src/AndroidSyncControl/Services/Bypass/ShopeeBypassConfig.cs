using System;

namespace AndroidSyncControl.Services.Bypass
{
    /// <summary>
    /// Configuration options for the Shopee Bypass Pipeline.
    /// </summary>
    public class ShopeeBypassConfig
    {
        public int  AirplaneDropDelayMs       { get; set; } = 2000;
        public int  IpPollIntervalMs          { get; set; } = 1500;
        public int  IpPollTimeoutMs           { get; set; } = 25000;
        public bool EnableDeepRootHookTriggers { get; set; } = true;

        // ── [FIX-8] GEO & Locale Spoofing ───────────────────────────────────
        /// <summary>
        /// Inject a randomised Vietnamese city coordinate via ADB Mock Location
        /// and reset device locale + timezone to vi_VN / Asia/Ho_Chi_Minh.
        /// Requires 'Allow Mock Locations' enabled in Developer Options (non-root)
        /// OR root for setprop-level override.
        /// </summary>
        public bool EnableGeoSpoofing       { get; set; } = true;
        public bool EnableLocaleReset        { get; set; } = true;

        /// <summary>
        /// Pool of real Vietnamese city centroids (lat, lon).
        /// Pipeline picks one at random — zero hardcoding (KR-06).
        /// Add / remove entries without touching pipeline logic.
        /// </summary>
        public static readonly (double Lat, double Lon, string City)[] VietnamCityPool =
        {
            ( 10.7769, 106.7009, "Ho Chi Minh City" ),  // TP.HCM trung tâm
            ( 10.8231, 106.6297, "Binh Tan, HCM" ),      // Q. Bình Tân
            ( 10.7384, 106.6585, "District 7, HCM" ),    // Q. 7
            ( 10.7907, 106.7186, "Thu Duc, HCM" ),       // TP. Thủ Đức
            ( 21.0285, 105.8542, "Hanoi Center" ),        // Hà Nội trung tâm
            ( 20.9997, 105.8412, "Ha Dong, Hanoi" ),      // Hà Đông
            ( 16.0544, 108.2022, "Da Nang Center" ),      // Đà Nẵng
            ( 10.9565, 106.8388, "Bien Hoa, Dong Nai" ),  // Biên Hòa
            ( 10.3599, 107.0843, "Vung Tau" ),            // Vũng Tàu
            ( 10.0453, 105.7469, "Can Tho Center" ),      // Cần Thơ
        };
    }
}
