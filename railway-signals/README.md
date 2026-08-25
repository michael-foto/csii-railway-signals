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
object**, and a two headed signal is two objects sharing a position. Three assets are needed:

| Asset | What it is |
| --- | --- |
| Home signal | Mast, both head housings, and the top head's three lamps |
| Automatic signal | The same with an "A" plate |
| Medium speed head | The lower head's three lamps alone, no mast |

Each of the three needs:

1. A **`TrafficLightObject`** component. This is what puts `Game.Objects.TrafficLight` into the
   instance archetype. Without it the mod will not use the asset at all.
2. An **`EmissiveProperties`** component with one light mapped to each of the purposes
   `TrafficLight_Red`, `TrafficLight_Yellow` and `TrafficLight_Green`.

The two heads are placed at the same position and rotation, so the medium speed head asset should
carry its lamps at their real height on the mast. If you would rather offset it, the "medium speed
head drop" setting lowers it.

The model's **+Z axis faces the approaching train**, so the lamps should point along +Z.

Name the assets and put those names in the mod's settings under "Signal posts". Until then the
mod stands in a vanilla road traffic light for all three, which will stack both heads at one
spot; raise the head drop setting to tell them apart while testing.

## Building

Requires the in-game modding toolchain, a Unity mod project, and `protontricks`. The post
processor is a Windows tool, and it is run through protontricks so it gets the same Proton build
the game runs under; under plain wine it faults on shutdown after having written every output.

```
dotnet build -c Debug
```

The output deploys to the game's local `Mods` directory.
