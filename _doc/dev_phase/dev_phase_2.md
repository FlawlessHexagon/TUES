# Development Phase 2 — The World
## Detailed Implementation Plan

*Phase 2 establishes the environment. It transitions the engine from an empty void into a persistent, procedurally generated, and atmospheric voxel universe.*

---

## Guiding Principles for Phase 2

1. **Universal `.tuesengine` Modularity.** The engine has no hardcoded generators. All terrain generation is executed via `.tuesengine` packages (zipped archives containing a manifest, a `.dll`, and assets). Even the built-in default world is a `.tuesengine`.
2. **Dynamic World Bounds.** The engine must not enforce a global vertical chunk limit. Every `.tuesengine` will declare its own `MinY` and `MaxY` limits, allowing infinitely deep or hyper-scaled worlds.
3. **Lazy Lighting.** Use highly optimized flood-fill algorithms running on a dedicated thread.
4. **Full Chunk Persistence.** Save entire chunk arrays to disk when modified, avoiding main-thread disk I/O.

---

## Step 2.0 — The `.tuesengine` Package Pipeline

**Goal**: Establish the structural foundation of the game by building the loader for Dimension Packages. The hardcoded `WorldGenerator` from Phase 1 will be gutted.

### What is built in this step

#### The `TuesEngineLoader`
- A system that scans `user://dimensions/` for `.tuesengine` files.
- Unzips them into memory/cache and parses their manifests.
- Dynamically injects any custom blocks from `blocks.json` into the global `VoxelRegistry`.
- **Dynamic Atlas Rebuild**: Displays a loading screen, collects all PNGs from the core game and loaded modules, and stitches them into a fresh `Texture2DArray` in memory before the game starts.
- Uses secure `AssemblyLoadContext` sandboxing to execute the `.dll` logic securely without malicious file/network access.

#### Dynamic Bounds Integration
*Status: Planned — not yet implemented. Blocked on the two-pass Generate/Decorate pipeline (Step 2.2).*
- `ChunkManager` will be refactored to remove the hardcoded 128-block height limit. Instead, it will query the active Dimension Engine for its `MinY` and `MaxY` bounds.

#### Verification for this step
- The game can load an external `.tuesengine` package, register its custom blocks dynamically at runtime, and generate terrain using the custom `.dll`.

---

## Step 2.1 — The Built-In `.tuesengine` Packages

**Goal**: Implement the core engines natively, shipping them as built-in `.tuesengine` packages.

### What is built in this step

#### 1. Default (`tues:default`)
- The standard pipeline terrain generator. Packaged as `default.tuesengine`.

#### 2. Smooth (`tues:smooth`)
- A gentle, rolling hills generator utilizing FastNoiseLite. Packaged as `smooth.tuesengine`.

#### 3. Extreme (`tues:extreme`)
- Amplified generation featuring massive vertical mountains. Packaged as `extreme.tuesengine`.

#### 4. Flat (`tues:flat`)
- Classic Superflat isolating chunk performance without noise overhead. Packaged as `flat.tuesengine`.

#### Verification for this step
- Changing `GeneratorType` in `settings.json` reliably swaps the active `.tuesengine` package seamlessly.

---

## Step 2.2 — Structures & Decorators

**Goal**: Add subterranean caves and surface decorations via secondary terrain passes.

### What is built in this step

#### Cave Carvers (3D Noise)
- Use 3D volumetric noise running after the base terrain pass. If the noise drops below a threshold, replace the terrain block with Air to carve natural caves.

#### C# Structure Decorators
- Run a hardcoded C# structure script (like a `TreeGenerator`) that places logs and leaves via fast direct-array math operations using the `IWorldAccess` interface. These run strictly after neighboring chunks have completed their base generation.

---

## Step 2.3 — Voxel Lighting Engine

**Goal**: Implement a robust, lazy lighting engine that bakes shadows directly into vertex colors.

### What is built in this step

#### Concurrency Model & Memory Layout
- A **Dedicated Lighting Worker Thread** will process light propagation entirely in the background.
- Lighting data (4 bits SkyLight, 4 bits BlockLight) is packed into a **Separate `byte[]` array** within the `Chunk`. This ensures the main `ushort[]` voxel array remains tightly packed and cache-friendly for meshing.

#### SkyLight & BlockLight Propagation
- Sunlight travels perfectly downwards. If it hits a transparent block, it bleeds horizontally with a -1 dropoff.
- Torches emit light that floods outward in all 6 directions.
- These light values will be passed to the `ChunkMesher` to calculate vertex shadows dynamically.

---

## Step 2.4 — World Persistence

**Goal**: Save player modifications to disk without stuttering the main thread.

### What is built in this step

#### Region Files
- Implement a system that bundles 32x32 chunks into a single binary `.tregion` file on disk. Untouched chunks are mathematically regenerated on-the-fly and never occupy disk space.

#### Full Chunk Saving (Async Background Task)
- When a chunk modified by the player falls out of bounds, it is not discarded.
- Instead, clone its `ushort[]` array and dispatch an **Async Background Task** to compress (via Zlib) and write it to the `.tregion` file, completely avoiding main thread stutters.

---

## Phase 2 — Completion Criteria
1. The `TuesEngineLoader` successfully parses `.tuesengine` archives, loading custom blocks and sandboxed DLLs.
2. The four core generator types function distinctly as individual packages.
3. Shadows cast dynamically based on terrain height and player block placement.
4. Player modifications persist perfectly across game restarts via full chunk region files.
