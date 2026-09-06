# VR Railway Signals

This mod automatically places three-position colour light signals along your railway networks. These signals
automatically display aspects based on [Victorian Three-Position Signalling practice](https://vicsig.net/index.php?page=infrastructure&section=signalling#3pst)
The aspects are computed in real-time by the game's engine based on the actual routing and positioning of trains
on the network.

## Props/Assets

This mod comes with two main types of signal assets:
- Line-side posts, which feature two stacked colour lights in either vertical or staggered arrangement
- Overhead steel gantries with individual catwalk-cages, holding multiple signals in parallel.

### Bundling them with the mod

To update or replace the assets, copy each exported asset's files into this project's `Assets/` folder. An asset is a set of files sharing a name: `.Prefab`, `.Geometry`, `.Surface`, `.Texture`. The build copies the folder into the deployed mod directory if 

## Placement

Signals are automatically placed at the following locations with some configuration:
- Home signals are placed at junctions and the departure ends of station platforms
- Permanant stop signals are placed at every buffer block.
- Automatic signals are placed at fixed spacing along plain line track

Signals are placed following the city's left or right hand running, and face the approaching train.  Where enough parallel tracks run abreast of each other, the signals over them move onto an overhead gantry structure. Every track of a network counts towards this, so a double track counts as two and a quad as four whether or not each one is signalled there, and a gantry spans whole networks. The tracks needed, the widest a gantry may be and how far one may reach between networks are tunable to your liking with reasonable defaults, with the option to disable the feature if preferred.

## Signal Aspects

While Victorian Railways practice is largely followed, for more variety in-game, some liberties are taken.

Typical practice would have all signals with no route explicitly booked showing a stop aspect.  This would make almost all signals in-game display stop at almost all times so signals default to clear if the track is not occupied.

Medium speed applies where the track a train is booked over turns sharply, is posted slow, or is short enough that the geometry is what limits speed, typically around diverging routes and yard pointwork.  If a signal is protecting a turnout off a main line, it will only display medium speed if a train is booked/routed into that turnout, defaulting to normal speed.

## Configuration

### Trains obey signals

On by default. Trains in the game follow a CBTC-like safeworking method, this contradicts the fixed-block safeworking this mod emulates, meaning the base game's train AI will occasionally pass a signal at danger. This setting modifies this aspect of the AI and forces trains to wait at danger aspects, with a configurable timeout to avoid softlocking the network in the case of conflicts.

This setting adds realism to the simulation, but can negatively impact the efficiency of your railways so turn it off if it causes issues in your map.


## Issues & Contribution

For any bugs, issues, feature requests or contributions, see the [Github Repo](https://github.com/michael-foto/csii-railway-signals/tree/main)
This mod is still in beta and early feedback, performance testing and bug hunting is greatly appreciated.

## Building

Build toolchain configured for Linux, but will work on windows with minimal changes.

Requires the in-game modding toolchain and a Unity mod project, both located through environment
variables. The toolchain's Windows installer sets these itself; on Linux put them somewhere that
reaches the build, such as `/etc/environment`.

| Variable | Points at |
| --- | --- |
| `CSII_TOOLPATH` | `Cities2_Data/Content/Game/.ModdingToolchain` |
| `CSII_MANAGEDPATH` | `Cities2_Data/Managed` |
| `CSII_USERDATAPATH` | The game's user data folder |
| `CSII_LOCALMODSPATH` | `Mods` inside that folder |
| `CSII_MODPOSTPROCESSORPATH` | The `ModPostProcessor` directory |
| `CSII_MODPUBLISHERPATH` | The `ModPublisher` directory |
| `CSII_MSCORLIBPATH` | A .NET Framework 4 `mscorlib.dll` |
| `CSII_UNITYMODPROJECTPATH` | The Unity project supplying Burst |
| `CSII_ENTITIESVERSION` | The `com.unity.entities` version in that project |

The toolchain's `Mod.props` reads each of these with `EnvironmentVariableTarget.User`, which goes
to the Windows registry and so comes back empty everywhere else. The project re-reads the same
variables from the process environment, where MSBuild exposes each as a property of the same name.

```
dotnet build -c Debug
```

The output deploys to `$CSII_LOCALMODSPATH/railway-signals`.

### Linux

Also requires `protontricks`. The post processor is a Windows tool, and it is run through
protontricks so it gets the same Proton build the game runs under; under plain wine it faults on
shutdown after having written every output.

## Tests

`RailwaySignals.Tests`, alongside this project at the repository root, tests the core signalling rules `Signalling/SignalRules.cs`: what a signal shows, the speed it admits a train at, which lanes a booking claims, and whether a stop aspect will hold a train.

```
dotnet test
```

From the repository root, and it needs none of the environment above. The tests build and run on their own, without the game installed and without touching the mod project.

The test project source links `SignalRules.cs` and `SignalAspects.cs` and runs via reflection rather than referencing the mod, which requires the toolchain to run. This keeps the tests only testing the core logic rather than the ECS system behaviour.

| Class | Covers |
| --- | --- |
| `AspectTests` | The aspect a signal shows, routed and unrouted, including the stop blocks and a junction with an occupied branch |
| `SpeedTests` | Normal against medium, over a booked road and with none set |
| `RouteLockingTests` | Which signals a booking puts to danger, and which it must not |
| `HoldingTests` | When a stop aspect holds a train, and when it lets go |
| `LampTests` | The lamp each aspect lights, on each of the two heads |
