using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Net;
using Game.Settings;
using Game.UI;
using RailwaySignals.Systems;
using Unity.Entities;

namespace RailwaySignals
{
    /// <summary>
    /// How a three position head shows "reduce to medium speed", which has no lamp of its own.
    /// </summary>
    public enum MediumIndication
    {
        /// <summary>Flash the green lamp. Needs an animation curve on that lamp in the asset.</summary>
        FlashingGreen,
        /// <summary>Light yellow and green together, as a two headed signal would.</summary>
        YellowOverGreen
    }

    [FileLocation(nameof(RailwaySignals))]
    [SettingsUIGroupOrder(kGeneralGroup, kBlockGroup, kSpeedGroup, kPlacementGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kBlockGroup, kSpeedGroup, kPlacementGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kGeneralGroup = "General";

        public const string kBlockGroup = "Blocks";

        public const string kSpeedGroup = "Speeds";

        public const string kPlacementGroup = "Placement";

        public Setting(IMod mod)
            : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool enableSignals { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool signalSubwayTracks { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool signalTramTracks { get; set; }

        /// <summary>Target length of an automatic block on plain line, in metres. Zero places none.</summary>
        [SettingsUISlider(min = 0, max = 2000, step = 50, unit = Unit.kLength)]
        [SettingsUISection(kSection, kBlockGroup)]
        public int intermediateBlockSpacing { get; set; }

        [SettingsUISection(kSection, kBlockGroup)]
        public bool intermediateOnBidirectionalTrack { get; set; }

        /// <summary>Curves tighter than this radius, in metres, are taken at medium speed.</summary>
        [SettingsUISlider(min = 50, max = 1500, step = 25, unit = Unit.kLength)]
        [SettingsUISection(kSection, kSpeedGroup)]
        public int mediumSpeedCurveRadius { get; set; }

        /// <summary>Track posted at or below this speed, in km/h, is medium speed.</summary>
        [SettingsUISlider(min = 10, max = 160, step = 5, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kSpeedGroup)]
        public int mediumSpeedLimit { get; set; }

        /// <summary>Blocks no longer than this, in metres, are cramped enough to be medium speed.</summary>
        [SettingsUISlider(min = 0, max = 500, step = 10, unit = Unit.kLength)]
        [SettingsUISection(kSection, kSpeedGroup)]
        public int mediumSpeedBlockLength { get; set; }

        [SettingsUISection(kSection, kSpeedGroup)]
        public MediumIndication mediumIndication { get; set; }

        [SettingsUISlider(min = 0f, max = 30f, step = 0.5f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalSetback { get; set; }

        [SettingsUISlider(min = 0f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalOffset { get; set; }

        /// <summary>Asset for home signals, which are interlocked. Empty picks one automatically.</summary>
        [SettingsUITextInput]
        [SettingsUISection(kSection, kPlacementGroup)]
        public string homeSignalPrefabName { get; set; }

        /// <summary>Asset for automatic signals, which carry an "A" plate. Empty picks one automatically.</summary>
        [SettingsUITextInput]
        [SettingsUISection(kSection, kPlacementGroup)]
        public string automaticSignalPrefabName { get; set; }

        [SettingsUIButton]
        [SettingsUISection(kSection, kPlacementGroup)]
        public bool rebuildSignals
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
            }
        }

        public TrackTypes signalledTrackTypes
        {
            get
            {
                TrackTypes types = TrackTypes.Train;
                if (signalSubwayTracks)
                {
                    types |= TrackTypes.Subway;
                }
                if (signalTramTracks)
                {
                    types |= TrackTypes.Tram;
                }
                return types;
            }
        }

        public override void SetDefaults()
        {
            enableSignals = true;
            signalSubwayTracks = false;
            signalTramTracks = false;
            intermediateBlockSpacing = 400;
            intermediateOnBidirectionalTrack = false;
            signalSetback = 6f;
            signalOffset = 3.5f;
            mediumSpeedCurveRadius = 300;
            mediumSpeedLimit = 70;
            mediumSpeedBlockLength = 120;
            mediumIndication = MediumIndication.FlashingGreen;
            homeSignalPrefabName = string.Empty;
            automaticSignalPrefabName = string.Empty;
        }

        public override void Apply()
        {
            base.Apply();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
        }
    }
}
