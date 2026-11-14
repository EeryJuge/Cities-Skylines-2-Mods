// Mod settings, shown in the game's Options menu as "Road+ by Raggend".
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using TF2YolAraci.Core;

namespace TF2YolAraci
{
    [FileLocation(nameof(TF2YolAraci))]
    [SettingsUIGroupOrder(kSlopeGroup, kShapeGroup, kConnectGroup)]
    [SettingsUIShowGroupName(kSlopeGroup, kShapeGroup, kConnectGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kSlopeGroup = "Slope";
        public const string kShapeGroup = "Shape";
        public const string kConnectGroup = "Connect";

        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        [SettingsUISection(kSection, kSlopeGroup)]
        [SettingsUISlider(min = 1, max = 15, step = 1, unit = Unit.kPercentage)]
        public int HighwaySlopePercent { get; set; }

        [SettingsUISection(kSection, kSlopeGroup)]
        [SettingsUISlider(min = 1, max = 20, step = 1, unit = Unit.kPercentage)]
        public int RampSlopePercent { get; set; }

        [SettingsUISection(kSection, kSlopeGroup)]
        [SettingsUISlider(min = 1, max = 20, step = 1, unit = Unit.kPercentage)]
        public int OtherSlopePercent { get; set; }

        [SettingsUISection(kSection, kSlopeGroup)]
        [SettingsUISlider(min = 1, max = 15, step = 1, unit = Unit.kPercentage)]
        public int RailSlopePercent { get; set; }

        [SettingsUISection(kSection, kShapeGroup)]
        [SettingsUISlider(min = 0, max = 20, step = 1, unit = Unit.kInteger)]
        public int EarthworkMaxMeters { get; set; }

        [SettingsUISection(kSection, kShapeGroup)]
        [SettingsUISlider(min = 20, max = 200, step = 10, unit = Unit.kInteger)]
        public int MaxSegmentLengthMeters { get; set; }

        [SettingsUISection(kSection, kShapeGroup)]
        [SettingsUISlider(min = 30, max = 90, step = 5, unit = Unit.kInteger)]
        public int MaxTurnDegrees { get; set; }

        [SettingsUISection(kSection, kConnectGroup)]
        [SettingsUISlider(min = 15, max = 300, step = 5, unit = Unit.kInteger)]
        public int SpiralRadiusMeters { get; set; }

        [SettingsUISection(kSection, kConnectGroup)]
        [SettingsUISlider(min = 4, max = 20, step = 1, unit = Unit.kInteger)]
        public int SpiralClearanceMeters { get; set; }

        public override void SetDefaults()
        {
            HighwaySlopePercent = 5;
            RampSlopePercent = 10;
            OtherSlopePercent = 10;
            RailSlopePercent = 5;
            EarthworkMaxMeters = 6;
            MaxSegmentLengthMeters = 80;
            MaxTurnDegrees = 90;
            SpiralRadiusMeters = 60;
            SpiralClearanceMeters = 8;
        }

        public RoadSettings ToRoadSettings() => new RoadSettings
        {
            highwaySlopeLimit = HighwaySlopePercent / 100.0,
            rampSlopeLimit = RampSlopePercent / 100.0,
            otherSlopeLimit = OtherSlopePercent / 100.0,
            railSlopeLimit = RailSlopePercent / 100.0,
            earthworkMax = EarthworkMaxMeters,
            maxSegmentLength = MaxSegmentLengthMeters,
            maxTurnDegrees = MaxTurnDegrees,
        };
    }

    /// <summary>Settings labels shown in the game's Options menu.</summary>
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Road+ by Raggend" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kSlopeGroup), "Slope limits" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kShapeGroup), "Road shape" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kConnectGroup), "Connect tool" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.HighwaySlopePercent)), "Highway slope limit" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.HighwaySlopePercent)), "Maximum grade on highways (ramps excluded)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RampSlopePercent)), "Highway ramp slope limit" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RampSlopePercent)), "Maximum grade on single-lane one-way highways (ramps)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.OtherSlopePercent)), "Other roads slope limit" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.OtherSlopePercent)), "Maximum grade on all roads that are not highways." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RailSlopePercent)), "Rail slope limit" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RailSlopePercent)), "Maximum grade on train and subway tracks. Tram tracks use the 'other roads' limit." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SpiralRadiusMeters)), "Spiral radius (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SpiralRadiusMeters)), "Default radius of the Spiral shape in the Connect tool. Page Up / Page Down change it while connecting." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SpiralClearanceMeters)), "Spiral loop clearance (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SpiralClearanceMeters)), "Minimum height between two loops of a spiral, so the road fits under itself." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EarthworkMaxMeters)), "Sloped terrain limit (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EarthworkMaxMeters)), "If the height difference between the road and the terrain is below this, the terrain is sloped to fit the road (fill / cut). Above it, the game builds a retaining wall, bridge or tunnel. Values below the game's own limit for the road have no effect." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxSegmentLengthMeters)), "Max segment length (m)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxSegmentLengthMeters)), "Long roads are split into segments of this length." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxTurnDegrees)), "Max turn per segment (°)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxTurnDegrees)), "If the angle between the start direction and the target exceeds this, place an intermediate point first." },
            };
        }

        public void Unload() { }
    }
}
