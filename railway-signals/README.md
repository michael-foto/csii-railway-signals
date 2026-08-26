# Railway Signals

Automatic three-position colour light signalling for the Cities: Skylines II rail network.
Signals are placed from the track topology, and each one displays the aspect its block
warrants. The mod only reads simulation state; it never holds a train up that the base game
would have let through.

## What gets placed where

| Placement | Reason |
| --- | --- |
| Every approach to a junction | Protects the pointwork, crossover or diamond crossing beyond |
| Departure end of each station platform | Starting signal |
| Along plain line at a set spacing | Automatic block signals, off for bidirectional single track by default |

Signals stand on the driver's side, following the city's left or right hand running, set back
from the boundary they govern and facing the approaching train.

Where enough tracks run abreast and their signals face the same way, those signals come off their
lineside posts and go onto a **signal bridge** instead: one structure spanning the group, with a
head over each track. The group is gathered one track at a time, so a wide formation is picked up
as long as each step across is within the track spacing setting, and the signals are squared onto
the line of the structure so every head reads as one row. How many tracks it takes is a setting,
three by default, which is roughly where a bridge stops being a bracket.

Each signal is also classified two ways:

- **Home or automatic.** A signal whose block contains pointwork, a crossing or a platform has
  to be interlocked and is a home signal. A signal whose block is plain line all the way to the
  next signal is an automatic, which carries an "A" plate on the prototype. Home and automatic
  signals take separate assets.
- **Normal or medium speed.** Medium where the road ahead curves more sharply than a set radius,
  is posted below a set speed, or is short enough that the geometry is what limits speed. That
  last one is what junction throats and yard trackwork look like.

## Aspects

These are speed signals, not route signals. The top head carries the normal speed indications and
the bottom head the medium speed ones.

| Aspect | Shown when | Top | Bottom |
| --- | --- | --- | --- |
| Stop | A train stands in the block ahead, or another movement has claimed part of it | Red | Red |
| Caution | Block ahead clear at normal speed, next signal at stop | Yellow | Red |
| Clear | This block and the next are clear at normal speed | Green | Red |
| Reduce to medium | Road clear, but the next signal is a medium speed one | Yellow | Green |
| Medium caution | Block ahead clear at medium speed, next signal at stop | Red | Yellow |
| Medium clear | Block ahead clear at medium speed, next signal off stop | Red | Green |

A block that runs into buffers reads as caution.

Where a signal has several routes beyond it, the most restrictive of them decides the aspect,
since the route a train will take is not known in advance. A signal's own speed is likewise fixed
by the geometry of its block rather than by which route is set through it.

A signal that can never show a medium indication, meaning it is a normal speed signal with no
medium speed signal ahead of it, is placed single headed.

A train still on approach to a signal does not put that signal to danger with its own claim on
the block beyond it. A claim from any other movement does, which is what makes conflicting
routes through a junction interlock.

## Making the signal assets

The mod does not draw anything itself. It sets `Game.Objects.TrafficLight.m_State` and the base
game lights the lamps. That component drives one three-lamp head, so **each head is its own
object**, and a signal is assembled from separate parts. Heads are modelled *without* a mast, which
is what lets the same head serve on a lineside post and hung from a bridge.

### What to model

| # | Asset | Meshes | Contents |
| --- | --- | --- | --- |
| 1 | Mast | 1, or 3 as a stack | The post alone. No lamps |
| 2 | Home signal head | 1 | Housing and three lamps |
| 3 | Automatic signal head | 1 | The same with an "A" plate |
| 4 | Medium speed head | 1 | The lower head. Usually the same casting as 2 |
| 5 | Signal bridge | 3 | Leg, beam section, leg |

Seven meshes, or nine if you make the mast a stack. Fewer if you reuse: the medium speed head is
normally identical to the home signal head, so you can point both settings at one asset and model
six. The two bridge legs can be one mesh used twice.

### The heads (2, 3, 4)

Each needs:

1. A **`TrafficLightObject`** component. This is what puts `Game.Objects.TrafficLight` into the
   instance archetype. Without it the mod will not use the asset at all.
2. An **`EmissiveProperties`** component with one light per lamp, assigned the purposes
   `TrafficLight_Red`, `TrafficLight_Yellow` and `TrafficLight_Green`. The purposes are what the
   mod addresses, so the lamps can sit in any order on the model.

Put the **pivot at the centre of the head**, not at the ground, since the mod places heads at a
height above rail. The **+Z axis faces the approaching train**.

### The mast (1)

Plain geometry, no lamps and no `TrafficLightObject`. Pivot at rail level, growing upwards.

Model it either at a fixed height matching the "head height above rail" setting, or as a **stack**
so the setting drives it: three meshes each carrying a **`StackProperties`** component with
`m_Direction = Up`, ordered `First` (base), `Middle` (a shaft section) and `Last` (cap). The mod
then stretches the shaft to reach whatever height is set.

### The signal bridge (5)

One object however many tracks it spans, because the game can tile a mesh along an axis. Three
meshes, each with a **`StackProperties`** component with `m_Direction = Right`:

| Mesh | `m_Order` |
| --- | --- |
| Leg | `First` |
| Beam section | `Middle` |
| Leg | `Last` |

A mesh with `StackProperties` gives the prefab `StackData` and its instances `Game.Objects.Stack`,
and the mod sets the span. The beam is repeated between the legs to fill it, so one asset covers
two tracks or ten. Set `m_ForbidScaling` on the beam if you would rather it tiled at its natural
width than stretched to fit exactly.

Pivot at rail level with the X axis across the tracks. Match the "head height above rail" setting
in the bridge group to where your beam sits, since that is what the heads hang at.

### Creating them

Assets are authored as FBX plus textures and imported by the game's own Editor, not through Unity.
The Unity project the build refers to is only there to supply Burst.

1. Model each mesh and export an `.fbx`.
2. Author textures named after the mesh with the suffixes the importer expects:
   `_BaseColor`, `_Normal`, `_MaskMap`, `_ControlMask`, and for the heads the emissive maps.
3. Drop a `settings.json` beside them to control LOD generation.
4. Open the game, go to the **Editor**, and use the **Asset Import** tool on that folder.
5. Add the components above to the imported prefab and save it.

The toolchain ships worked examples of exactly this layout under
`Cities2_Data/Content/Game/.ModdingToolchain/ExampleAssets`. `ExampleEmissiveUfoSign` is the one to
copy for the heads, since it shows how several separately controlled lights are mapped on one mesh.

### Bundling them with the mod

Copy each exported asset's files into this project's `Assets/` folder. An asset is a set of files
sharing a name: `.Prefab`, `.Geometry`, `.Surface`, `.Texture`. The build copies the folder into
the deployed mod directory.

Nothing has to load them. The game's User asset database covers the whole user data folder
recursively, and prefab loading registers every prefab asset it finds there, so anything sitting in
the deployed mod folder is already available. Put the asset names into the mod's settings and they
are used.

Until assets are installed the mod stands in a vanilla road traffic light for the heads, which
brings its own pole, so no mast is placed. There is no stand-in for a bridge: grouping is skipped
entirely and every signal stays on a lineside post.

## Building

Requires the in-game modding toolchain, a Unity mod project, and `protontricks`. The post
processor is a Windows tool, and it is run through protontricks so it gets the same Proton build
the game runs under; under plain wine it faults on shutdown after having written every output.

```
dotnet build -c Debug
```

The output deploys to the game's local `Mods` directory.
