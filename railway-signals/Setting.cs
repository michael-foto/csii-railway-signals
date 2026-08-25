using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Net;
using Game.Settings;
using Game.UI;
using RailwaySignals.Systems;
using Unity.Entities;

namespace RailwaySignals
{
    [FileLocation(nameof(RailwaySignals))]
    [SettingsUIGroupOrder(kGeneralGroup, kBlockGroup, kPlacementGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kBlockGroup, kPlacementGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kGeneralGroup = "General";

        public const string kBlockGroup = "Blocks";

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

        [SettingsUISlider(min = 0f, max = 30f, step = 0.5f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalSetback { get; set; }

        [SettingsUISlider(min = 0f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalOffset { get; set; }

        /// <summary>Name of the object prefab to use for signal posts. Empty picks one automatically.</summary>
        [SettingsUITextInput]
        [SettingsUISection(kSection, kPlacementGroup)]
        public string signalPrefabName { get; set; }

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
            signalPrefabName = string.Empty;
        }

        public override void Apply()
        {
            base.Apply();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
        }
    }
}
