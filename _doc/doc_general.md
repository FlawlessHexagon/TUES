# The Universal Entertainment System (TUES)
## General Design Document

*This is the foundational reference for the entire project. Every implementing agent,
every design decision, and every feature proposal starts here.*

---

## 1. Identity

**The Universal Entertainment System** (TUES) is a voxel-based open world platform. Its
name and spirit are drawn from the Nintendo Entertainment System — a single, universal
device that served as a gateway to an enormous range of experiences. TUES is that idea
rebuilt for the modern era: a common voxel foundation upon which countless gameplay
experiences can be built, shared, and played together.

TUES sits between Minecraft and Roblox, taking the best of both:
- From Minecraft: a **strong shared identity**. Every player, on every platform, begins
  from the same recognisable voxel reality. This common ground is what creates community.
- From Roblox: **deep, unified customizability**. Players and creators can manipulate
  terrain generation, define new elements, and reprogram gameplay — through a single
  scripting system, not three incompatible ecosystems.

The balance: enough structure to feel like *one game*, enough freedom to become *any game*.

---

## 2. Technical Foundation

These decisions are locked. They are not subject to change without a formal review.

| Layer           | Choice                              | Rationale                                                    |
|-----------------|-------------------------------------|--------------------------------------------------------------|
| **Engine**      | Godot 4.6                           | Open-source, cross-platform, C# support, active development  |
| **Language**    | C# (.NET 8, .NET 9 for Android)     | Strong typing, mature ecosystem, Godot-native integration     |
| **Physics**     | Jolt Physics                        | High performance, deterministic, Godot 4.6 native support    |
| **Renderer**    | Forward Plus (desktop)              | Modern PBR pipeline, supports advanced lighting               |
| **GPU Backend** | D3D12 (Windows), Vulkan (default)   | Low-overhead modern APIs                                      |

### Platform Targets

**Current development targets desktop only.** Mobile, web, and console are future
ambitions that will not influence architectural decisions at this stage. When the desktop
foundation is solid, platform expansion becomes its own workstream.

- **Active**: Windows, macOS, Linux
- **Future**: Android, iOS, Web (browser-playable), Console

---

## 3. The Fundamental Unit: The Voxel

The atomic building block of TUES is the **voxel** — a volumetric unit occupying one cell
of the 3D world grid.

**A voxel is not just a cube.** A voxel is a **3D model** that occupies a single grid cell.
While the default visual form is cubic (and the majority of terrain will be cubic), a
voxel's shape is defined by its type. Different voxel types can have different geometries:
slopes, slabs, stairs, cylinders, organic forms — any mesh that fits within a single grid
cell. The engine must not assume all voxels are axis-aligned cubes. The cubic form is
the *common case*, not the *only case*.

At the data level, a voxel is stored as a compact identifier (its **type reference** —
see Section 5.2 for the registry system). It has no per-instance object overhead. The
world is a vast three-dimensional grid of these identifiers, grouped into **chunks** for
efficient processing.

**Future ambitions** (noted, not designed):
- Sub-voxel manipulation — altering the world at scales smaller than one grid cell.
- World complexity rating — a measurable metric indicating how "heavy" a world is, so
  players joining a multiplayer session can gauge performance cost before loading in.

---

## 4. Element Classification

Everything that exists in the TUES game world falls into exactly one of three categories.
This taxonomy drives how elements are stored, rendered, updated, and networked.

*(Note: things that exist in the game but not "in the world" — UI, menus, game rules,
settings — are outside this taxonomy. They are meta-layers around the world, not world
elements. A separate classification for those may be developed later.)*

---

### 4.1 — Environment

The **Environment** is everything the player experiences but cannot directly touch or
manipulate. It is the systemic backdrop of the world — the stage upon which all other
elements exist.

Environment encompasses:
- **Sky**: the skybox, sun, moon, stars, celestial bodies
- **Weather**: rain, snow, fog, storms, wind
- **Time**: the day/night cycle, season progression
- **Lighting**: ambient light levels, directional sun/moon light, global illumination rules
- **Atmosphere**: fog density, ambient particles, volumetric effects
- **Ambient sound**: background audio tied to biome, weather, or time of day

**Key property**: Environment is **global and continuous**. It is not made of discrete
objects with positions. It is a set of systems that produce sensory output everywhere,
all the time. A player does not "interact with the weather" — they experience it.

Environment state is lightweight to store and synchronise (a handful of parameters:
time of day, current weather type, wind direction, etc.) and is typically authoritative
from a single source (the host/server).

---

### 4.2 — Fixtures (Static Elements)

A **Fixture** is any discrete element that exists at a fixed position in the world and
does not move. Fixtures are the substance of the world — the things you see, touch,
build with, and destroy.

Fixtures include:
- **Terrain voxels**: grass, stone, dirt, sand, water (visual), ores, bedrock
- **Placed voxels**: anything a player has built or modified
- **Structures**: trees, boulders, ruins, generated buildings
- **Functional objects**: crafting stations, furnaces, chests, doors, levers
- **Decorative objects**: torches, flowers, signs, banners

**Key property**: Fixtures have a **fixed position** on the voxel grid. They do not move
through space. A Fixture *may* have visual animations (a torch flame flickers, leaves
sway in the wind, a piston extends) — but animation is a cosmetic property, not a
dynamic one. The element itself remains at its grid position.

**Key property**: Fixtures are **interactable**. Unlike Environment, a player can break,
place, activate, or modify a Fixture. This is the fundamental distinction between
Environment and Fixture: Environment is observed; Fixtures are acted upon.

Fixtures live on the voxel grid and are stored as part of chunk data. They are the
densest data category by far — millions of Fixtures exist in a typical loaded world.

---

### 4.3 — Entities (Dynamic Elements)

An **Entity** is any element that possesses one or more **dynamic properties** — properties
that change as a function of gameplay. The defining characteristic of an Entity is not
merely visual change (animation), but *physical mutability*.

Dynamic properties include:
- **Position**: the Entity moves through space (players, creatures, projectiles)
- **Size**: the Entity grows, shrinks, or scales (expanding explosion, growing crop)
- **Velocity**: the Entity has speed and direction
- **State**: the Entity transitions between behavioural states (idle → attacking → fleeing)
- **Health / Lifetime**: the Entity has a finite existence that can be depleted

Entities include:
- **Players**: the human-controlled characters
- **Creatures / Mobs**: AI-driven beings that move, react, and interact
- **Projectiles**: arrows, thrown objects, launched blocks
- **Particles with gameplay effect**: explosions, area-of-effect clouds
- **Falling blocks**: a voxel dislodged from the grid, now subject to gravity
- **Dropped items**: objects lying on the ground awaiting pickup

**Key property**: Entities exist in **continuous floating-point space**, not locked to the
integer voxel grid. A player stands at position (12.347, 65.0, -8.912), not at grid
cell (12, 65, -8). This continuous positioning is what enables smooth movement, physics
simulation, and precise collision detection.

**Key property**: Entities are **ticked**. Every game frame (or physics frame), each
Entity's dynamic properties are updated. Fixtures are not ticked — they are inert until
acted upon. This distinction has massive performance implications: the engine must update
every active Entity every frame, so Entity count directly impacts frame rate.

Entities are stored separately from chunk data, typically in per-chunk or per-region
entity lists, and are serialised independently.

---

### 4.4 — Classification Summary

```
┌──────────────────────────────────────────────────────────────────┐
│                        TUES GAME WORLD                           │
├──────────────┬──────────────────────┬────────────────────────────┤
│ ENVIRONMENT  │      FIXTURES        │         ENTITIES           │
│              │   (Static Elements)  │    (Dynamic Elements)      │
├──────────────┼──────────────────────┼────────────────────────────┤
│ Sky          │ Terrain voxels       │ Players                    │
│ Weather      │ Placed voxels        │ Creatures / Mobs           │
│ Time         │ Structures (trees…)  │ Projectiles                │
│ Lighting     │ Functional objects   │ Falling blocks             │
│ Atmosphere   │ Decorative objects   │ Dropped items              │
│ Ambient sound│                      │                            │
├──────────────┼──────────────────────┼────────────────────────────┤
│ Observed     │ Interacted with      │ Ticked every frame         │
│ Global state │ Grid-positioned      │ Continuous-space position  │
│ Lightweight  │ Dense (millions)     │ Sparse (hundreds/thousands)│
│ Not per-obj  │ Stored in chunks     │ Stored separately          │
└──────────────┴──────────────────────┴────────────────────────────┘
```

**The boundary cases**:
- A block that begins falling (e.g., sand with no support) **transitions from Fixture to
  Entity**. It leaves the voxel grid and enters continuous space. When it lands, it
  transitions back to Fixture.
- A door that opens/closes is a **Fixture with state**, not an Entity. Its position does
  not change; only its visual/collision state toggles.
- A growing sapling that expands into a tree is a **Fixture state transition**, not an
  Entity — unless the growth is continuous and physics-interactive, in which case it
  temporarily becomes an Entity during the growth animation.

---

## 5. Core Architectural Concepts

### 5.1 — Chunks

The world is divided into fixed-size **chunks** — three-dimensional groups of voxels.
Chunks are the fundamental unit of loading, unloading, mesh generation, storage, and
network synchronisation. Only chunks near the player are active.

**Async world streaming**: The world is effectively infinite, but memory is not. As the
player moves, new chunks are loaded ahead and old chunks are unloaded behind — like a
treadmill rolling beneath the player's feet. This loading happens **in background worker
threads**, never on the main rendering thread, so the game never stutters or freezes when
new terrain appears. The main thread only receives fully-prepared chunk data, builds the
visual mesh from it, and displays it.

Detailed chunk architecture (size, format, storage) is specified in `doc_world.md`.

---

### 5.2 — The Voxel Registry (Namespaced Identification)

Every voxel type in the game needs a canonical identity. The naive approach — assigning
sequential integer IDs (stone = 1, dirt = 2, grass = 3…) — breaks catastrophically when
the game updates or when modules (plugins) are added:

- Game version 1.0 defines voxels 1–50.
- Version 1.1 inserts a new voxel at position 23 — every ID after 23 shifts.
- Module A adds voxels 51–60. Module B also tries to use 51–60. Collision.
- A player removes Module A. Now every ID that referenced Module A's voxels is orphaned.

**The solution: namespaced string identifiers.**

Every voxel type is identified by a **namespace:name** string pair:
- `tues:stone` — core game stone. Always means stone, forever.
- `tues:grass` — core game grass.
- `forestcraft:maple_log` — a voxel added by a module called "forestcraft".

The namespace is unique per module (enforced by the module system). The name is unique
within its namespace. Together, they form a globally unique, collision-proof identity
that never shifts when content is added, removed, or reordered.

**But strings are large — how do we store millions of them in chunk data?**

We don't. At world load time, a **mapping table** is built that assigns each namespaced
identifier a compact integer (the **runtime ID**). Chunk data stores these small integers,
not strings. The mapping table is saved in the world file and rebuilt whenever the set of
loaded modules changes. This gives us:
- **Stable identity**: `tues:stone` is always `tues:stone` regardless of which modules are
  loaded or what integer it maps to in any given world.
- **Compact storage**: chunk arrays store small integers, not strings.
- **Module safety**: adding or removing a module remaps integers but never collides.

Runtime ID `0` is always reserved for **Air** (empty space).

---

### 5.3 — World Generation

#### Seed-Based Deterministic Generation

Given a **seed** (an integer), the world generator produces identical terrain every time.
The same seed always produces the same world. This is the baseline generation model.

**Storage optimisation**: Chunks that have never been modified by a player do not need to
be saved to disk — they can be regenerated from the seed on demand. Only chunks that a
player has altered (placed or broken a voxel) are persisted. This dramatically reduces
world file sizes.

**Caveat**: Players may want to **pre-generate** chunks (e.g., for performance, for
sharing pre-built worlds, or for server preparation). The system must support explicitly
saving unmodified chunks when requested, not just as a "dirty" flag. Pre-generation is
an opt-in action, not the default.

#### Dimension Engines (Future Concept)

Seed-based generation with fixed noise functions is a starting point, not the end state.
The long-term vision is a system called **Dimension Engines** — self-contained world
generation configurations that players can:
- Generate from scratch using provided tools
- Modify from existing templates
- Share with other players to experience different world types

A Dimension Engine is essentially a portable world generation ruleset — like Minecraft's
custom world generation datapacks, but formalised as a specific, well-defined category
within the TUES module system rather than an ad-hoc mod. This provides a clear, universal
interface for world generation customisation rather than requiring players to install
arbitrary code.

*Dimension Engines are noted here for architectural awareness. They are not a current
implementation target.*

---

### 5.4 — Multiplayer & Networking (Future Concept)

Playing together should not require external infrastructure. The game will embed P2P
connection primitives directly, with optional dedicated server support for larger sessions.

Key concerns to be addressed when this becomes active:
- **Data synchronisation**: how chunk modifications, Entity states, and Environment
  changes are communicated between host and clients.
- **Packet management**: voxel worlds generate large volumes of data (chunk loads, block
  changes, entity updates). Packet sizes must be carefully managed — naive approaches
  (sending entire chunks on every change) will saturate bandwidth instantly. Delta
  compression, change batching, and priority queuing are expected solutions.
- **Server ↔ client authority model**: who is the source of truth for world state?
  What is validated server-side vs. trusted client-side?
- **Scalability**: these games can grow to massive player counts. The networking
  architecture must degrade gracefully under load, not catastrophically.

*Networking architecture is noted here for awareness. Detailed design will live in a
dedicated `doc_network.md` when this workstream begins.*

---

## 6. Design Pillars

### Pillar 1 — The Voxel World (Shared Identity)
The default TUES world is a specific, opinionated experience. Every player starts from
the same recognisable reality. This common ground is sacred — it is what makes TUES
feel like *one game* rather than a platform of disconnected experiences.

### Pillar 2 — Extreme Optimisation (Universal Access)
TUES must run well on a wide range of hardware. Optimisation is an architectural
constraint from day one, not a polish pass at the end.

### Pillar 3 — Unified Customisation (The Module System)
One system for extending the game. Not mods vs. plugins vs. datapacks. One interface,
one format, one ecosystem. Modules occupy specific, well-defined categories — world
generation (Dimension Engines), voxel types, Entity behaviours, game rules — rather
than being arbitrary code injections. This structure makes modules predictable, safe,
and interoperable.

### Pillar 4 — Embedded Multiplayer
Playing together should not require a server administrator. Connection primitives are
built into the game.

### Pillar 5 — Cross-Platform by Design
Desktop first. Mobile and web are future targets that will not compromise the desktop
foundation.

---

## 7. Development Phases

Previous attempts at this project failed due to rushing through development without
a sufficiently granular plan. These phases are deliberately broken into small, verifiable
steps. **No phase begins until the previous phase is complete and verified.**

### Phase 0 — The Atomic Layer

Build the absolute smallest units of the engine. No gameplay, no world, no player.
Just data structures and systems.

| Step  | Name                        | Scope                                                    |
|-------|-----------------------------|----------------------------------------------------------|
| 0.0   | **Voxel Definition**        | Define what a voxel IS as a data type. The type registry. The model/mesh association per type. Not limited to cubes — the system must support arbitrary per-type geometry from the start. |
| 0.1   | **Chunk Definition**        | Define the chunk as a fixed-size 3D array of voxel IDs. Flat array storage. Index math. Neighbour access. |
| 0.2   | **Chunk Meshing**           | Build a visible mesh from a chunk's voxel data. Face culling against solid neighbours. Support for non-cubic voxel geometry. |
| 0.3   | **Chunk Manager**           | Load/unload chunks around a point. Async generation in worker threads. Spatial indexing (which chunks are active). |
| 0.4   | **World Generator (Basic)** | Seed-based noise terrain. Fill chunks with voxel data. Deterministic output. |

### Phase 1 — The Player

Put a human into the world.

| Step  | Name                        | Scope                                                    |
|-------|-----------------------------|----------------------------------------------------------|
| 1.0   | **Player Controller**       | CharacterBody3D. Movement (walk, sprint, jump). Gravity. Collision with chunk meshes. |
| 1.1   | **Camera & Input**          | First-person camera. Mouse look. Input capture.          |
| 1.2   | **Voxel Interaction**       | Raycast to identify targeted voxel face. Break (set to Air). Place (set adjacent cell). Rebuild affected chunk mesh. |
| 1.3   | **Basic HUD**               | Crosshair. Hotbar showing held voxel type. Debug info (FPS, chunk count, position). |

### Phase 2 — The World

Make the world worth exploring.

| Step  | Name                        | Scope                                                    |
|-------|-----------------------------|----------------------------------------------------------|
| 2.0   | **Terrain Variety**         | Multiple terrain types via noise layering. Height variation. Basic biome differentiation. |
| 2.1   | **Structure Generation**    | Trees, rock formations, caves. Placed during chunk generation. |
| 2.2   | **Lighting System**         | Sky light propagation. Block light sources. Light level stored per voxel. Dynamic updates on block change. |
| 2.3   | **World Persistence**       | Save/load modified chunks. Region file format. World metadata file. |

### Phase 3+ — (To Be Planned)

Subsequent phases (Entity system, Module system, Multiplayer, Platform expansion) will
be planned in detail when Phase 2 is approaching completion. Planning too far ahead
with insufficient information leads to architectural assumptions that break on contact
with reality.

---

## 8. Document Map

This document (`doc_general.md`) is the root reference. Detailed technical specifications
live in separate documents:

| Document                 | Scope                                              |
|--------------------------|----------------------------------------------------|
| `doc_general.md`         | This file. Vision, taxonomy, architecture overview. |
| `doc_world.md`           | World storage, region files, chunk format, I/O.     |
| *(future)* `doc_fixtures.md`  | Voxel registry, Fixture types, state system.   |
| *(future)* `doc_entities.md`  | Entity system, ticking, physics, AI.           |
| *(future)* `doc_modules.md`   | Module API, scripting, Dimension Engines.      |
| *(future)* `doc_network.md`   | Multiplayer architecture, P2P, sync protocol.  |

---

*This document is the single source of truth for what TUES is. It will grow alongside
the game, but its core definitions — the element taxonomy, the technical foundation,
and the design pillars — are foundational and should not be altered without deliberation.*
