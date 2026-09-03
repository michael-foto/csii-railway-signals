using System.Collections.Generic;
using Colossal;

namespace RailwaySignals
{
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Railway Signals" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kBlockGroup), "Blocks" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kSpeedGroup), "Medium speed" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAdvancedGroup), "Advanced offsets" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kGantryGroup), "Signal bridges" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.enableSignals)), "Place signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.enableSignals)), "Automatically signal the rail network. Turning this off removes every signal post." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalSubwayTracks)), "Signal subway track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalSubwayTracks)), "Also place signals on subway track. Most of it is underground and out of sight." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.intermediateBlockSpacing)), "Block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.intermediateBlockSpacing)), "How long a stretch of plain line runs before an automatic signal divides it. Junctions and platform ends are always signalled regardless. Set to zero to signal junctions and platforms only." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.intermediateOnBidirectionalTrack)), "Automatic signals on single line" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.intermediateOnBidirectionalTrack)), "Divide bidirectional single track into blocks as well. Train track in this game is always bidirectional, so turning this off leaves automatic signals nowhere to go and only junctions and platforms get signalled." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedCurveRadius)), "Medium speed curve radius" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedCurveRadius)), "Curves tighter than this radius are treated as medium speed, so the signal admitting a train onto them is a medium speed signal." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedLimit)), "Medium speed limit (km/h)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedLimit)), "Track posted at or below this speed counts as medium speed. This is what marks out yard and siding trackwork." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedBlockLength)), "Medium speed block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedBlockLength)), "Blocks shorter than this are taken as cramped geometry, which is what a junction throat looks like, and are signalled at medium speed." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.minGantryTracks)), "Tracks needed for a bridge" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.minGantryTracks)), "How many parallel tracks have to carry signals abreast of each other before they are put on a signal bridge instead of their own lineside posts. Set to zero to always use lineside posts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.maxGantryTrackSpacing)), "Widest track spacing" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.maxGantryTrackSpacing)), "How far apart neighbouring tracks can be and still count as one group. The group grows one track at a time, so a wide formation is gathered as long as each step is within this." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.gantryAlignTolerance)), "Alignment tolerance" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.gantryAlignTolerance)), "How far apart along the track two signals can sit and still share a bridge. Signals that do share one are squared up onto its line." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.minGantryTrackSeparation)), "Closest signals on a bridge" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.minGantryTrackSeparation)), "How far apart across the track two signals have to be to both get a place on the same bridge. The approaches to a junction run nearly parallel a few metres apart, so without this each diverging route claims its own slot and the heads land on top of each other." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustSetback)), "Setback" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustSetback)), "Moves every signal along its track, away from or towards the block boundary it protects. Zero leaves it at the built-in 3 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustLateral)), "Lineside offset from track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustLateral)), "Moves a lineside post further from or closer to the track centre. Zero leaves it at the built-in 2 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustHeight)), "Height above ground" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustHeight)), "Raises or lowers every part of every signal, posts and bridges alike. Zero leaves the built-in drop of 0.15 m below the lane centreline, which is where the models sit level with the railhead." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustHeadSpacing)), "Gap between heads" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustHeadSpacing)), "Widens or narrows the gap between the normal speed head and the medium speed head below it. Zero leaves it at the built-in 1.15 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustGantryMargin)), "Bridge overhang" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustGantryMargin)), "Extends or shortens a bridge beyond the outermost track it spans. Zero leaves it at the built-in 7 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustGantryLateral)), "Bridge offset from track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustGantryLateral)), "Moves a bridge-carried signal further from or closer to its own track centre, where it clears the overhead wiring. Zero leaves it at the built-in 1.5 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustGantryHeadHeight)), "Bridge head height" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustGantryHeadHeight)), "Raises or lowers a bridge-carried head above rail level. Zero leaves it at the built-in 2.25 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustGantryHeadSide)), "Bridge head across track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustGantryHeadSide)), "Moves a bridge-carried head sideways relative to the cage holding it. Zero leaves it at the built-in 0.65 m." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.adjustGantryHeadForward)), "Bridge head along track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.adjustGantryHeadForward)), "Moves a bridge-carried head along the track relative to the cage holding it. Zero leaves it at the built-in 1.05 m." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.rebuildSignals)), "Rebuild signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.rebuildSignals)), "Recompute every signal position and block from the current track network." }
            };
        }

        public void Unload()
        {
        }
    }
}
