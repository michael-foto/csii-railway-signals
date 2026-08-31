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
    [SettingsUIGroupOrder(kGeneralGroup, kBlockGroup, kSpeedGroup, kPlacementGroup, kGantryGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kBlockGroup, kSpeedGroup, kPlacementGroup, kGantryGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kGeneralGroup = "General";

        public const string kBlockGroup = "Blocks";

        public const string kSpeedGroup = "Speeds";

        public const string kPlacementGroup = "Placement";

        public const string kGantryGroup = "Gantries";

        public Setting(IMod mod)
            : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool enableSignals { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool signalSubwayTracks { get; set; }

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

        [SettingsUISlider(min = 0f, max = 30f, step = 0.5f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalSetback { get; set; }

        [SettingsUISlider(min = 0f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalOffset { get; set; }

        /// <summary>Height of the normal speed head above rail level on a lineside post, in metres.</summary>
        [SettingsUISlider(min = 1f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float signalHeadHeight { get; set; }

        /// <summary>Gap between the normal speed head and the medium speed head below it, in metres.</summary>
        [SettingsUISlider(min = 0.25f, max = 4f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kPlacementGroup)]
        public float headSpacing { get; set; }

        /// <summary>Fewest parallel tracks that get a signal bridge instead of lineside posts. Zero disables them.</summary>
        [SettingsUISlider(min = 0, max = 8, step = 1, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kGantryGroup)]
        public int minGantryTracks { get; set; }

        [SettingsUISlider(min = 4f, max = 30f, step = 0.5f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kGantryGroup)]
        public float maxGantryTrackSpacing { get; set; }

        [SettingsUISlider(min = 1f, max = 60f, step = 1f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kGantryGroup)]
        public float gantryAlignTolerance { get; set; }

        [SettingsUISlider(min = 0f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kGantryGroup)]
        public float gantryMargin { get; set; }

        [SettingsUISlider(min = 3f, max = 12f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kGantryGroup)]
        public float gantryHeadHeight { get; set; }

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
                return types;
            }
        }

        public override void SetDefaults()
        {
            enableSignals = true;
            signalSubwayTracks = true;
            intermediateBlockSpacing = 400;
            intermediateOnBidirectionalTrack = true;
            signalSetback = 6f;
            signalOffset = 3.5f;
            signalHeadHeight = 4f;
            headSpacing = 1.1f;
            mediumSpeedCurveRadius = 300;
            mediumSpeedLimit = 70;
            mediumSpeedBlockLength = 120;
            minGantryTracks = 3;
            maxGantryTrackSpacing = 12f;
            gantryAlignTolerance = 15f;
            gantryMargin = 2f;
            gantryHeadHeight = 5.5f;
        }

        public override void Apply()
        {
            base.Apply();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
        }
    }
}
