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

| Aspect | Shown when | Lamps |
| --- | --- | --- |
| Stop | A train stands in the block ahead, or another movement has claimed part of it | Red |
| Caution | Block ahead clear, next signal at stop, or the block runs into buffers | Yellow |
| Reduce to medium | Road clear, but the next signal is a medium speed one | Flashing green, or yellow over green |
| Clear | This block and the next are clear at normal speed | Green |

Where a signal has several routes beyond it, the most restrictive of them decides the aspect,
since the route a train will take is not known in advance.

A train still on approach to a signal does not put that signal to danger with its own claim on
the block beyond it. A claim from any other movement does, which is what makes conflicting
routes through a junction interlock.

## Making the signal assets

The mod does not draw anything itself. It sets `Game.Objects.TrafficLight.m_State` on the post
and the base game lights the lamps, so an asset needs:

1. A **`TrafficLightObject`** component. This is what puts `Game.Objects.TrafficLight` into the
   instance archetype. Without it the mod will not use the asset at all.
2. An **`EmissiveProperties`** component with one light mapped to each of the purposes
   `TrafficLight_Red`, `TrafficLight_Yellow` and `TrafficLight_Green`.
3. For the flashing green medium indication, an **animation curve** assigned to the green light's
   `animationIndex`. Without one the lamp just holds steady green, and the aspect is
   indistinguishable from clear. Choosing "yellow over green" instead avoids needing the curve.

The model's **+Z axis faces the approaching train**, so the lamps should point along +Z.

Name the assets and put those names in the mod's settings under "Signal posts". Until then the
mod stands in a vanilla road traffic light so the signalling can be seen working.

## Building

Requires the in-game modding toolchain, a Unity mod project, and `protontricks`. The post
processor is a Windows tool, and it is run through protontricks so it gets the same Proton build
the game runs under; under plain wine it faults on shutdown after having written every output.

```
dotnet build -c Debug
```

The output deploys to the game's local `Mods` directory.
