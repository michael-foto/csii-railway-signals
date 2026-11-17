using System.Collections.Generic;
using Colossal;

namespace RailwaySignals
{
    public class LocaleEN : IDictionarySource
    {
        private readonly RailwaySignalsSetting m_Setting;

        public LocaleEN(RailwaySignalsSetting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Railway Signals" },
                { m_Setting.GetOptionTabLocaleID(RailwaySignalsSetting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(RailwaySignalsSetting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(RailwaySignalsSetting.kBlockGroup), "Blocks" },
                { m_Setting.GetOptionGroupLocaleID(RailwaySignalsSetting.kSpeedGroup), "Medium speed signals" },
                { m_Setting.GetOptionGroupLocaleID(RailwaySignalsSetting.kAdvancedGroup), "Advanced offsets" },
                { m_Setting.GetOptionGroupLocaleID(RailwaySignalsSetting.kGantryGroup), "Gantries" },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.enableSignals)), "Place signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.enableSignals)), "Automatically signal the rail network. Turning this off removes every signal post." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.signalSubwayTracks)), "Signal subway track" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.signalSubwayTracks)), "Also place signals on subway track. Most of it is underground and out of sight." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.holdTrainsAtSignals)), "Trains obey stop signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.holdTrainsAtSignals)), "Hold a train at a signal showing stop, instead of only displaying the aspect. Only a block occupied by another train holds a train: a signal at a buffer stop stays at danger for good, so enforcing that one would make every terminal platform, siding and depot road unreachable. Turning this off releases any train being held within about half a second." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.holdReleaseSeconds)), "Release a held train after" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.holdReleaseSeconds)), "How long one signal may hold a train before letting it go anyway, in seconds. Absolute block on track laid without signalling in mind can bring two trains to a stand waiting on each other, and the game's own answer to a deadlocked train is to delete it, so a held train always has to be released eventually." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.intermediateBlockSpacing)), "Block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.intermediateBlockSpacing)), "How long a stretch of plain line runs before an automatic signal divides it. Junctions and platform ends are always signalled regardless. Set to zero to signal junctions and platforms only." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.intermediateOnBidirectionalTrack)), "Automatic signals on single line" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.intermediateOnBidirectionalTrack)), "Divide bidirectional single track into blocks as well. Train track in this game is always bidirectional, so turning this off leaves automatic signals nowhere to go and only junctions and platforms get signalled." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.mediumSpeedCurveRadius)), "Medium speed curve radius" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.mediumSpeedCurveRadius)), "Curves tighter than this radius are treated as medium speed, so the signal admitting a train onto them is a medium speed signal." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.mediumSpeedLimit)), "Medium speed limit (km/h)" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.mediumSpeedLimit)), "Track posted at or below this speed counts as medium speed. This is what marks out yard and siding trackwork." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.mediumSpeedBlockLength)), "Medium speed block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.mediumSpeedBlockLength)), "Blocks shorter than this are taken as cramped geometry, which is what a junction throat looks like, and are signalled at medium speed." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.minBlockLength)), "Shortest block" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.minBlockLength)), "The shortest block length allowed. Blocks shorter than this are merged into their neighbor. Signals at buffer stops are always kept. Set to zero to keep every signal." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.minGantryTracks)), "Tracks needed for a gantry" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.minGantryTracks)), "How many parallel tracks a gantry has to stand over before the signals abreast of each other there are put on one instead of their own lineside posts. Every track of a network counts, so a double track counts as two and a quad as four whether or not each one is signalled here. Set to zero to always use lineside posts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.maxGantryWidth)), "Widest gantry" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.maxGantryWidth)), "The widest a gantry may be, measured across its outermost tracks. A gantry spans whole networks, so it takes in another one only if all of that network's tracks fit within this. The structure itself is this plus its overhang at either end." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.maxGantryTrackGap)), "Widest track gap" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.maxGantryTrackGap)), "How far a gantry may reach from the tracks it already stands over to the next network across. Tracks further off than this get their own gantry or lineside posts instead, so a gantry is never thrown over open ground. A network is always spanned whole, so this decides where one gantry ends and the next begins, never where one is cut through." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.gantryAlignTolerance)), "Alignment tolerance" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.gantryAlignTolerance)), "How far apart along the track two signals can sit and still share a gantry. Signals that do share one are squared up onto its line." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.minGantryTrackSeparation)), "Closest signals on a gantry" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.minGantryTrackSeparation)), "Minimum distance that two signals have to be apart to be allowed on the same gantry. If this number is too small, signal heads can overlap under certain track geometry." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustSetback)), "Setback" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustSetback)), "Moves every signal along its track, away from or towards the block boundary it protects. Zero leaves it at the built-in 3 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustLateral)), "Lineside offset from track" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustLateral)), "Moves a lineside post further from or closer to the track centre. Zero leaves it at the built-in 2 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustHeight)), "Height above ground" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustHeight)), "Raises or lowers every part of every signal, posts and gantries alike. Zero leaves the built-in drop of 0.15 m below the lane centreline, which is where the models sit level with the railhead." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustHeadSpacing)), "Gap between heads" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustHeadSpacing)), "Widens or narrows the gap between the normal speed head and the medium speed head below it. Zero leaves it at the built-in 1.15 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustGantryMargin)), "Gantry overhang" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustGantryMargin)), "Extends or shortens a gantry beyond the edge of the networks it spans. Zero stands its legs on the edge itself, alongside the wiring masts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustGantryLateral)), "Gantry offset from track" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustGantryLateral)), "Moves a gantry-carried signal further from or closer to its own track centre, where it clears the overhead wiring. Zero leaves it at the built-in 1.5 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadHeight)), "Gantry head height" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadHeight)), "Raises or lowers a gantry-carried head above rail level. Zero leaves it at the built-in 2.25 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadSide)), "Gantry head across track" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadSide)), "Moves a gantry-carried head sideways relative to the cage holding it. Zero leaves it at the built-in 0.65 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadForward)), "Gantry head along track" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.adjustGantryHeadForward)), "Moves a gantry-carried head along the track relative to the cage holding it. Zero leaves it at the built-in 1.05 m." },
                { m_Setting.GetOptionLabelLocaleID(nameof(RailwaySignalsSetting.rebuildSignals)), "Rebuild signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(RailwaySignalsSetting.rebuildSignals)), "Recompute every signal position and block from the current track network." }
            };
        }

        public void Unload()
        {
        }
    }
}
