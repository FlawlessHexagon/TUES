# Development Phase 0 — The Atomic Layer
## Detailed Implementation Plan

*Phase 0 builds the absolute smallest units of the voxel engine. There is no gameplay,
no player, no world to explore. Only data structures and the systems that operate on
them. Everything in every subsequent phase is built on top of what is created here.
Getting this wrong means rebuilding the entire project.*

---

## Guiding Principles for Phase 0

1. **No rendering shortcuts.** Every visual result must come from a correctly built
   mesh pipeline, not from placeholder Godot nodes or debug visualisations that get
   "replaced later." There is no later — what is built here is the real system.

2. **Non-cubic voxels from day one.** The system must support arbitrary per-type 3D
   models occupying a single grid cell. Cubes are the default geometry, not the only
   geometry. An implementing agent that hardcodes cube assumptions will produce code
   that must be rewritten in Phase 1.

3. **Flat data, zero allocations per voxel.** A voxel is a small integer in a flat
   array. No objects, no class instances, no dictionaries per voxel. The chunk data
   structure must be cache-friendly and allocation-free at the per-voxel level.

4. **Async from the start.** Chunk generation must be threaded from its first
   implementation. Do not build a synchronous system and "add threading later." The
   threading model shapes the entire data handoff architecture.

---

## Step 0.0 — Voxel Definition

**Goal**: Define what a voxel *is* as a data type and build the registry that maps
voxel identifiers to their full definitions.

### What is built in this step

#### The `VoxelType` Definition

A voxel type is a pure data record — it describes everything the engine needs to know
about a category of voxel. It is not a voxel instance; it is the *definition* that all
voxels of this type share.

A `VoxelType` contains (at minimum):
- **Namespaced ID** (`string`): the canonical identity, e.g. `tues:stone`. Immutable.
  See `doc_general.md` Section 5.2 for the namespacing rationale.
- **Display name** (`string`): human-readable label, e.g. "Stone".
- **Is Solid** (`bool`): does this voxel block movement and occlude faces of neighbours?
- **Is Transparent** (`bool`): does this voxel allow light/visibility through it?
- **Mesh Mode** (`enum`): how this voxel type generates its visual geometry.
  - `Cube` — the standard six-faced box. The vast majority of voxels use this.
  - `Custom` — a custom 3D mesh is used. The mesh is referenced, not embedded.
  - `None` — produces no geometry (Air, or invisible logic blocks).
- **Mesh Reference** (optional): for `Custom` mode, a path or reference to the mesh
  resource that represents this voxel type's shape.
- **Texture/Material info**: which texture region (for cube faces) or material (for
  custom meshes) this voxel uses. The exact structure depends on whether a texture
  atlas or individual materials are used — the implementing agent should evaluate both
  approaches and choose based on draw call impact.

#### The `VoxelRegistry`

A singleton (or static service) that holds the complete set of registered `VoxelType`
definitions. It is populated once at startup and is **read-only during gameplay**.

Responsibilities:
- Register voxel types from the core game definition.
- (Future) Register voxel types from loaded modules.
- Build the **runtime ID mapping table**: assign each namespaced ID a compact `ushort`
  (0–65535) for use in chunk data arrays.
- Provide lookup in both directions: `namespaced ID → runtime ID` and
  `runtime ID → VoxelType`.
- Runtime ID `0` is permanently reserved for Air.

#### Verification for this step
- A unit test or debug script can register several voxel types, retrieve them by
  namespaced ID and by runtime ID, and confirm round-trip consistency.
- No visual output is expected from this step. It is pure data.

---

## Step 0.1 — Chunk Definition

**Goal**: Define the chunk as a fixed-size container of voxel data with efficient
access patterns.

### What is built in this step

#### The `Chunk` Data Structure

A chunk is a fixed-size 3D region of voxels. It stores:
- **Voxel data array**: a flat `ushort[]` of length `SIZE_X × SIZE_Y × SIZE_Z`. Flat
  arrays are cache-friendly and vastly more performant than jagged or multidimensional
  arrays.
  - Recommended starting size: **16 × 16 × 16** = 4,096 entries.
  - Index formula: `index = x + SIZE_X * (z + SIZE_Z * y)` (or equivalent; the
    implementing agent should choose the axis ordering that best matches access
    patterns during meshing, which typically iterates layer-by-layer).
- **Chunk position** (`Vector3I`): the chunk's position in chunk-grid coordinates
  (not world coordinates). World position = chunk position × chunk size.
- **Dirty flag** (`bool`): has this chunk been modified since generation? Only dirty
  chunks are persisted to disk.
- **State enum**: tracks the chunk's lifecycle: `Unloaded → Generating → Generated →
  Meshed → Active`. This prevents race conditions where code tries to mesh a chunk
  whose data hasn't finished generating.

#### Access Methods

- `GetVoxel(int x, int y, int z) → ushort`: returns the runtime ID at local coordinates.
  Must handle out-of-bounds gracefully (return Air / 0) — this is critical at chunk
  borders during meshing.
- `SetVoxel(int x, int y, int z, ushort id)`: sets a voxel and marks the chunk dirty.

#### What this step does NOT include
- No mesh generation (that is Step 0.2).
- No scene tree integration (the chunk is not a Node yet).
- No world-space awareness (the chunk doesn't know about its neighbours yet).

#### Verification for this step
- Create a chunk, fill it with known data, read it back, confirm correctness.
- Confirm out-of-bounds access returns Air without crashing.
- Measure memory footprint: a 16³ chunk of `ushort` should be exactly 8,192 bytes.

---

## Step 0.2 — Chunk Meshing

**Goal**: Convert a chunk's voxel data into a renderable 3D mesh that can be displayed
by Godot's rendering pipeline.

This is the most complex step in Phase 0. It bridges pure data (arrays of integers)
and visual output (GPU geometry). It must be correct from the start because every
visual artefact in the game traces back to the meshing system.

### What is built in this step

#### The Meshing Pipeline

Input: a `Chunk` (voxel data array) + neighbour data (the edge voxels of the 6
adjacent chunks, for correct face culling at borders).

Output: a Godot `ArrayMesh` (or `SurfaceTool`-built mesh) ready to be assigned to a
`MeshInstance3D`.

The pipeline operates in two modes, determined by the voxel type's `MeshMode`:

**For `Cube`-mode voxels (the common path)**:
- Iterate every voxel in the chunk.
- For each solid voxel, check its 6 neighbours (±X, ±Y, ±Z).
- If a neighbour is not solid (or is transparent), generate the face between them.
- If a neighbour IS solid, skip that face (face culling).
- For each generated face, emit 4 vertices (a quad) + 2 triangles + UV coordinates
  pointing to the correct region of the texture atlas.

**For `Custom`-mode voxels**:
- The voxel type references a pre-built mesh.
- That mesh is stamped into the chunk mesh at the voxel's local position (translated).
- Custom meshes do not participate in face culling — they always render fully.
  (This is a simplification. Optimisation of custom mesh occlusion is a later concern.)

**For `None`-mode voxels (Air)**:
- Skip entirely. No geometry.

#### Neighbour Data at Chunk Borders

When checking whether to cull a face at the chunk boundary (e.g., the +X face of
voxel at local x=15), the mesher needs the voxel at x=0 of the neighbouring chunk.
This data must be provided to the meshing function — the chunk itself does not know
about its neighbours.

The implementing agent must design how this neighbour data is passed in. Options
include:
- Passing 6 neighbouring `Chunk` references.
- Passing a flat array of just the edge slices (16×16 per face = 256 voxels per
  neighbour, 1,536 total).
- A callback/delegate that resolves `GetVoxel(worldX, worldY, worldZ)`.

The choice affects threading safety and memory. The implementing agent should evaluate
and document the tradeoff.

#### Transparent Voxels

Transparent voxels (water, glass, leaves) must be collected into a **separate mesh
surface** from opaque voxels. This is because transparent geometry must be rendered
in a different pass (back-to-front sorted) to display correctly. Mixing opaque and
transparent faces in a single surface produces visual glitches.

At minimum, the mesher outputs **two surfaces per chunk**: one opaque, one transparent.

#### What this step produces in Godot

A `MeshInstance3D` node positioned at the chunk's world-space origin, holding the
generated `ArrayMesh`. Optionally, a `StaticBody3D` with a collision shape derived
from the solid voxel faces, for physics interaction.

#### Verification for this step
- Create a small test: generate a chunk with known voxel data (e.g., a flat grass
  layer), mesh it, add it to a scene, and visually confirm it renders correctly.
- Place two different voxel types adjacent and confirm face culling is correct (no
  internal faces visible).
- Place a transparent voxel next to a solid voxel and confirm both render.
- Confirm the mesh has the expected vertex count (no duplicate or missing faces).

---

## Step 0.3 — Chunk Manager

**Goal**: Manage the lifecycle of chunks — loading, generating, meshing, and unloading
them based on proximity to a reference point (which will be the player in Phase 1,
but is just a `Vector3` position for now).

### What is built in this step

#### The `ChunkManager` Node

A Godot `Node3D` that maintains the set of all active chunks. It is the single
owner of all chunk data and chunk scene nodes.

Responsibilities:
- **Determine which chunks should be active** based on a reference position and a
  configurable render distance (in chunk units).
- **Load chunks that enter the active set**: trigger generation, then meshing, then
  add to the scene tree.
- **Unload chunks that leave the active set**: remove from the scene tree, free mesh
  resources, discard data (or queue for saving if dirty — but saving is Phase 2).
- **Maintain a spatial index** of active chunks: `Dictionary<Vector3I, Chunk>` mapping
  chunk coordinates to their data/state.

#### Async Generation Pipeline

Chunk generation (filling the voxel data array) is CPU-intensive. It must not run on
the main thread. The pipeline is:

```
1. Main thread identifies a chunk that needs loading.
2. Main thread dispatches a generation task to a worker thread.
   - The task receives: chunk position, world seed, reference to VoxelRegistry.
   - The task produces: a filled voxel data array (ushort[]).
   - The task makes ZERO Godot API calls (no Node access, no RenderingServer).
3. Worker thread completes. Result is placed in a thread-safe queue.
4. Main thread polls the queue (once per frame), picks up completed chunks.
5. Main thread builds the mesh (Step 0.2) and adds MeshInstance3D to scene tree.
```

Step 5 (mesh building) happens on the main thread because Godot's rendering API is
not thread-safe. The implementing agent should verify this constraint against the
Godot 4.6 documentation — some `RenderingServer` methods may be callable from threads,
but `SurfaceTool` and scene tree manipulation are not.

#### Load/Unload Hysteresis

To prevent chunks at the edge of render distance from thrashing (loading and unloading
every frame as the player oscillates near a boundary), the unload distance should be
slightly larger than the load distance (e.g., load at 8 chunks, unload at 10). This
buffer zone prevents rapid cycling.

#### Load Prioritisation

Chunks closer to the reference position should generate first. A naive approach loads
chunks in arbitrary order, which can result in distant chunks appearing before nearby
ones — visually jarring. The generation queue should be sorted by distance to the
reference point, re-sorted when the reference moves significantly.

#### Verification for this step
- Set a reference position at the origin. Confirm chunks generate around it.
- Move the reference position. Confirm new chunks load ahead and old chunks unload
  behind.
- Confirm no main-thread stutter during chunk loading (profile frame times).
- Confirm chunk count stays bounded (no memory leak from chunks never unloading).
- Stress test: move the reference rapidly. Confirm no crashes, no dangling references.

---

## Step 0.4 — World Generator (Basic)

**Goal**: Given a chunk position and a world seed, produce a filled voxel data array
representing natural-looking terrain.

### What is built in this step

#### The `WorldGenerator` (Stateless Function)

The generator is a pure function: `Generate(Vector3I chunkPos, int seed) → ushort[]`.

It has no side effects, no mutable state, and no Godot API dependencies. This purity
is what makes it safe to run on worker threads and what guarantees determinism — same
inputs always produce the same output.

#### Noise-Based Terrain

The generator uses Godot's `FastNoiseLite` (or an equivalent noise library accessible
from C#) to compute a height map:

- For each (x, z) column within the chunk's world-space footprint, compute a noise
  value and map it to a terrain height.
- Fill voxels below the surface with appropriate types:
  - Top layer: `tues:grass` (or equivalent surface type)
  - Next 3–4 layers: `tues:dirt`
  - Below that: `tues:stone`
  - Y = 0: `tues:bedrock`
  - Above terrain height: `tues:air` (runtime ID 0)

**Important**: `FastNoiseLite` is a Godot `Resource`. The implementing agent must verify
whether `FastNoiseLite` instances are safe to use from worker threads. If not, the noise
computation must use a thread-safe alternative or pre-create one instance per worker
thread. This is a concrete threading concern that must be resolved during implementation,
not deferred.

#### Noise Configuration

The generator should use **Fractal FBm** (Fractal Brownian Motion) noise for
natural-looking terrain. Configurable parameters:
- Seed (passed in)
- Frequency (controls terrain scale — lower = wider hills)
- Octaves (controls terrain detail — more = rougher surface)
- Fractal gain and lacunarity

These should be exposed as tuneable constants or a configuration object, not buried
in the generation code. Future phases (and Dimension Engines) will need to swap them.

#### Starter Voxel Types

Phase 0 needs a minimal set of voxel types registered in the `VoxelRegistry` to
produce visible terrain:

| Namespaced ID      | Display Name | Solid | Transparent | Mesh Mode |
|--------------------|-------------|-------|-------------|-----------|
| `tues:air`         | Air         | No    | Yes         | None      |
| `tues:grass`       | Grass       | Yes   | No          | Cube      |
| `tues:dirt`        | Dirt        | Yes   | No          | Cube      |
| `tues:stone`       | Stone       | Yes   | No          | Cube      |
| `tues:bedrock`     | Bedrock     | Yes   | No          | Cube      |

More types can be added freely — the registry is designed for it — but these five are
sufficient to produce a visible, recognisable landscape.

#### Verification for this step
- Generate multiple chunks at different positions with the same seed. Confirm terrain
  is continuous across chunk boundaries (no seams, no height discontinuities).
- Generate the same chunk twice with the same seed. Confirm identical output
  (determinism).
- Generate with a different seed. Confirm different terrain.
- Visually confirm the terrain looks natural — rolling hills, not flat planes or
  random noise.

---

## Phase 0 — Completion Criteria

Phase 0 is complete when all of the following are true:

1. The game launches and displays procedurally generated voxel terrain.
2. Terrain extends in all horizontal directions as the reference point moves.
3. Chunks load and unload without freezing the main thread.
4. Chunk boundaries are seamless — no visible seams or face culling errors at borders.
5. The voxel registry correctly resolves namespaced IDs to runtime IDs and back.
6. Memory usage stays bounded as the reference point moves (chunks are actually freed).
7. Frame rate is stable (60 FPS target on a mid-range desktop at 8-chunk render distance).

There is no player in Phase 0. There is no input. The reference point can be moved
programmatically or via a debug control. The result is a flyover view of an infinite,
streaming voxel landscape.

**When these criteria are met, Phase 1 begins.**

---

## File Structure (Recommended)

The implementing agent should organise source files under `res://src/`. The exact
structure is a suggestion — the agent may adjust if there is a strong reason, but
should document why.

```
res://src/
├── voxel/
│   ├── VoxelType.cs          ← The voxel type data definition
│   └── VoxelRegistry.cs      ← Singleton registry, namespace→ID mapping
├── world/
│   ├── Chunk.cs              ← Chunk data container, voxel access methods
│   ├── ChunkMesher.cs        ← Converts chunk data → ArrayMesh
│   ├── ChunkManager.cs       ← Node3D, manages chunk lifecycle, async pipeline
│   └── WorldGenerator.cs     ← Pure function: chunk position + seed → voxel data
└── (Phase 1+)
    └── player/
        └── ...
```

---

*This document is the implementation brief for Phase 0. It defines what must be built,
in what order, and what success looks like. It does not prescribe exact code — that is
the implementing agent's domain. It prescribes outcomes, constraints, and architecture.*
