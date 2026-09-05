#!/bin/bash
# Follow the mod's log. Pass any other log name to follow that instead, e.g. ./log.sh AssetPipeline
: "${CSII_USERDATAPATH:?not set. See Building in the README}"
tail -n 40 -f "$CSII_USERDATAPATH/Logs/${1:-RailwaySignals.Mod}.log"
