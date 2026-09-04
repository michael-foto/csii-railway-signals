#!/bin/bash
# Follow the mod's log. Pass any other log name to follow that instead, e.g. ./log.sh AssetPipeline
L="/home/michael/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II/Logs"
tail -n 40 -f "$L/${1:-RailwaySignals.Mod}.log"
