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

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.enableSignals)), "Place signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.enableSignals)), "Automatically signal the rail network. Turning this off removes every signal post." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalSubwayTracks)), "Signal subway track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalSubwayTracks)), "Also place signals on subway track. Most of it is underground and out of sight." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalTramTracks)), "Signal tram track" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalTramTracks)), "Also place signals on tram track. Trams run on sight, so this is for looks only." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.intermediateBlockSpacing)), "Block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.intermediateBlockSpacing)), "How long a stretch of plain line runs before an automatic signal divides it. Junctions and platform ends are always signalled regardless. Set to zero to signal junctions and platforms only." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.intermediateOnBidirectionalTrack)), "Automatic signals on single line" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.intermediateOnBidirectionalTrack)), "Divide bidirectional single track into blocks as well. Off by default, since single lines are normally worked as one section between passing places." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedCurveRadius)), "Medium speed curve radius" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedCurveRadius)), "Curves tighter than this radius are treated as medium speed, so the signal admitting a train onto them is a medium speed signal." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedLimit)), "Medium speed limit (km/h)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedLimit)), "Track posted at or below this speed counts as medium speed. This is what marks out yard and siding trackwork." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.mediumSpeedBlockLength)), "Medium speed block length" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.mediumSpeedBlockLength)), "Blocks shorter than this are taken as cramped geometry, which is what a junction throat looks like, and are signalled at medium speed." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.bottomHeadDrop)), "Medium speed head drop" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.bottomHeadDrop)), "How far below the top head the medium speed head is placed. Leave at zero when the head asset positions its own lamps on the mast; raise it to tell two stand-in assets apart." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.bottomHeadPrefabName)), "Medium speed head asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.bottomHeadPrefabName)), "Name of the object asset for the lower head, which carries the medium speed indications. It is placed at the same position as the top head, so its lamps should sit at the right height on the mast in the asset itself. Leave empty to pick one automatically." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalSetback)), "Set back from boundary" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalSetback)), "How far short of the block boundary the post stands." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.signalOffset)), "Offset from track centre" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.signalOffset)), "How far to the side of the track the post stands. Signals are placed on the driver's side, following the city's left or right hand running." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.homeSignalPrefabName)), "Home signal asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.homeSignalPrefabName)), "Name of the object asset used where the block ahead holds pointwork, a crossing or a platform. Leave empty to pick one automatically." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.automaticSignalPrefabName)), "Automatic signal asset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.automaticSignalPrefabName)), "Name of the object asset used where the block ahead is plain line. These carry an \"A\" plate on the prototype. Leave empty to pick one automatically." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.rebuildSignals)), "Rebuild signals" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.rebuildSignals)), "Recompute every signal position and block from the current track network." }
            };
        }

        public void Unload()
        {
        }
    }
}
