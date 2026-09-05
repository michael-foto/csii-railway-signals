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
    [SettingsUIGroupOrder(kGeneralGroup, kBlockGroup, kSpeedGroup, kGantryGroup, kAdvancedGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kBlockGroup, kSpeedGroup, kGantryGroup, kAdvancedGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kGeneralGroup = "General";

        public const string kBlockGroup = "Blocks";

        public const string kSpeedGroup = "Speeds";

        public const string kAdvancedGroup = "Advanced";

        public const string kGantryGroup = "Gantries";

        public Setting(IMod mod)
            : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool enableSignals { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool signalSubwayTracks { get; set; }

        /// <summary>
        /// Whether a stop aspect actually holds a train, rather than only being displayed. Off by
        /// default: it changes how the railway runs and it can deadlock on track a real railway's
        /// signalling would never have been built over.
        /// </summary>
        [SettingsUISection(kSection, kGeneralGroup)]
        public bool holdTrainsAtSignals { get; set; }

        /// <summary>How long a train may be held at one signal before it is let go, in seconds.</summary>
        [SettingsUISlider(min = 5, max = 300, step = 5, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kGeneralGroup)]
        public int holdReleaseSeconds { get; set; }

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

        /// <summary>Closest two signals on one bridge may sit across the track, in metres.</summary>
        [SettingsUISlider(min = 0f, max = 8f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kGantryGroup)]
        public float minGantryTrackSeparation { get; set; }

        /// <summary>Offsets the fixed setback of a signal from its block boundary, in metres.</summary>
        [SettingsUISlider(min = -10f, max = 10f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustSetback { get; set; }

        /// <summary>Offsets how far a lineside post stands from the track centre, in metres.</summary>
        [SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustLateral { get; set; }

        /// <summary>Raises or lowers every part of every signal, in metres.</summary>
        [SettingsUISlider(min = -2f, max = 2f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustHeight { get; set; }

        /// <summary>Offsets the gap between the two heads of a signal, in metres.</summary>
        [SettingsUISlider(min = -1f, max = 1f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustHeadSpacing { get; set; }

        /// <summary>Offsets how far a bridge extends beyond the tracks it spans, in metres.</summary>
        [SettingsUISlider(min = -6f, max = 6f, step = 0.25f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustGantryMargin { get; set; }

        /// <summary>Offsets how far off its track centre a bridge-carried signal sits, in metres.</summary>
        [SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustGantryLateral { get; set; }

        /// <summary>Offsets the height of a bridge-carried head above rail level, in metres.</summary>
        [SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustGantryHeadHeight { get; set; }

        /// <summary>Offsets a bridge-carried head from its cage across the track, in metres.</summary>
        [SettingsUISlider(min = -2f, max = 2f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustGantryHeadSide { get; set; }

        /// <summary>Offsets a bridge-carried head from its cage along the track, in metres.</summary>
        [SettingsUISlider(min = -2f, max = 2f, step = 0.05f, unit = Unit.kLength, scalarMultiplier = 1f)]
        [SettingsUISection(kSection, kAdvancedGroup)]
        [SettingsUIAdvanced]
        public float adjustGantryHeadForward { get; set; }

        [SettingsUIButton]
        [SettingsUISection(kSection, kGeneralGroup)]
        public bool rebuildSignals
        {
            set
            {
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
            }
        }

        public override void SetDefaults()
        {
            enableSignals = true;
            signalSubwayTracks = true;
            holdTrainsAtSignals = false;
            holdReleaseSeconds = 30;
            intermediateBlockSpacing = 400;
            intermediateOnBidirectionalTrack = true;
            mediumSpeedCurveRadius = 300;
            mediumSpeedLimit = 70;
            mediumSpeedBlockLength = 120;
            minGantryTracks = 3;
            maxGantryTrackSpacing = 12f;
            gantryAlignTolerance = 15f;
            minGantryTrackSeparation = 2.5f;
            adjustSetback = 0f;
            adjustLateral = 0f;
            adjustHeight = 0f;
            adjustHeadSpacing = 0f;
            adjustGantryMargin = 0f;
            adjustGantryLateral = 0f;
            adjustGantryHeadHeight = 0f;
            adjustGantryHeadSide = 0f;
            adjustGantryHeadForward = 0f;
        }

        public override void Apply()
        {
            base.Apply();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalPrefabSystem>()?.Invalidate();
            World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SignalNetworkSystem>()?.Invalidate();
        }
    }
}
