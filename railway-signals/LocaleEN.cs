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
                { m_Setting.GetOptionGroupLocaleID(Setting.kPlacementGroup), "Signal posts" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kGantryGroup), "Signal bridges" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.enableSignals)), "Place signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.enableSignals)), "Automatically signal the rail network. Turning this off removes every signal post." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalSubwayTracks)), "Signal subway track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalSubwayTracks)), "Also place signals on subway track. Most of it is underground and out of sight." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalTramTracks)), "Signal tram track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalTramTracks)), "Also place signals on tram track. Trams run on sight, so this is for looks only." },

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

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalHeadHeight)), "Head height above rail" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalHeadHeight)), "How high the normal speed head sits on a lineside post. A mast built as a stack grows its shaft to reach this, so one mast asset serves any height." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.headSpacing)), "Gap between heads" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.headSpacing)), "How far below the normal speed head the medium speed head hangs." },


                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalSetback)), "Set back from boundary" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalSetback)), "How far short of the block boundary the post stands." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalOffset)), "Offset from track centre" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalOffset)), "How far to the side of the track the post stands. Signals are placed on the driver's side, following the city's left or right hand running." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mastPrefabName)), "Mast asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mastPrefabName)), "Name of the object asset for the post a lineside signal stands on. Heads are modelled without a mast so the same ones can hang from a bridge, so this carries the post on its own. There is no vanilla stand-in; without one the heads are placed unsupported." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.homeHeadPrefabName)), "Lamp head asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.homeHeadPrefabName)), "The plain lamp head, with no \"A\" plate. Used for the upper head of every signal, and for the lower head of a home signal." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.automaticHeadPrefabName)), "Automatic lamp head asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.automaticHeadPrefabName)), "The lamp head carrying an \"A\" plate. Used for the lower head of an automatic signal, where the block ahead is plain line." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.minGantryTracks)), "Tracks needed for a bridge" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.minGantryTracks)), "How many parallel tracks have to carry signals abreast of each other before they are put on a signal bridge instead of their own lineside posts. Set to zero to always use lineside posts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.maxGantryTrackSpacing)), "Widest track spacing" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.maxGantryTrackSpacing)), "How far apart neighbouring tracks can be and still count as one group. The group grows one track at a time, so a wide formation is gathered as long as each step is within this." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.gantryAlignTolerance)), "Alignment tolerance" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.gantryAlignTolerance)), "How far apart along the track two signals can sit and still share a bridge. Signals that do share one are squared up onto its line." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.gantryMargin)), "Structure overhang" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.gantryMargin)), "How far the bridge extends beyond the outermost track it spans." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.gantryHeadHeight)), "Head height above rail" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.gantryHeadHeight)), "How high the heads hang when carried on a bridge. Match this to where the beam sits in your bridge asset." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.gantryPrefabName)), "Signal bridge asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.gantryPrefabName)), "Name of the object asset for the bridge. It has to be built as a stack, with a leg mesh, a beam mesh and a second leg mesh, so the beam can tile out to whatever width the tracks need. There is no vanilla stand-in, so until one is installed the grouped signals stay on lineside posts." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.rebuildSignals)), "Rebuild signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.rebuildSignals)), "Recompute every signal position and block from the current track network." }
            };
        }

        public void Unload()
        {
        }
    }
}
