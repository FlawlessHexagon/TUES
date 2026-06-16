# Development Phase 2 — The World
## Detailed Implementation Plan

*Phase 2 establishes the environment. It transitions the engine from an empty void into a persistent, procedurally generated, and atmospheric voxel universe.*

---

## Guiding Principles for Phase 2

1. **Universal `.tuesengine` Modularity.** The engine has no hardcoded generators. All terrain generation is executed via `.tuesengine` packages (zipped archives containing a manifest, a `.dll`, and assets). Even the built-in default world is a `.tuesengine`.
2. **Dynamic World Bounds.** The engine must not enforce a global vertical chunk limit. Every `.tuesengine` will declare its own `MinY` and `MaxY` limits, allowing infinitely deep or hyper-scaled worlds.
3. **Lazy Lighting.** Use highly optimized flood-fill algorithms running on a dedicated thread.
4. **Full Chunk Persistence.** Save entire 8KB chunk arrays to disk when modified.

---

## Step 2.0 — The `.tuesengine` Package Pipeline

**Goal**: Establish the structural foundation of the game by building the loader for Dimension Packages.

### What is built in this step

#### The Archive System
- Define the `.tuesengine` file structure:
  - `manifest.json`: Defines the engine ID, version, and entry DLL name.
  - `generator.dll`: The compiled C# logic.
  - `assets/blocks.json`: Custom block definitions specific to this dimension.
  - `assets/textures/`: PNG textures for the new blocks.

#### The `TuesEngineLoader`
- A system that scans `user://dimensions/` for `.tuesengine` files.
- Unzips them into memory/cache.
- Dynamically injects any custom blocks from `blocks.json` into the global `VoxelRegistry`.
- **Dynamic Atlas Rebuild**: Displays a loading screen, collects all PNGs from the core game and loaded modules, and stitches them into a fresh `Texture2DArray` in memory before the game starts.
- Uses a secure `AssemblyLoadContext` sandbox to execute `generator.dll`, preventing malicious file/network access.

#### Verification for this step
- The game can load an external `.tuesengine` package, register its custom blocks dynamically at runtime, and generate terrain using the custom `.dll`.

---

## Step 2.1 — The Built-In `.tuesengine` Packages

**Goal**: Implement the core engines, but build them exclusively as native `.tuesengine` packages.

### What is built in this step

#### 1. Default (`tues:default`)
- The standard pipeline. Packaged as `default.tuesengine`.

#### 2. Smooth (`tues:smooth`)
- A gentle, rolling hills generator. Packaged as `smooth.tuesengine`.

#### 3. Extreme (`tues:extreme`)
- Amplified generation featuring massive vertical mountains. Packaged as `extreme.tuesengine`.

#### 4. Flat (`tues:flat`)
- Classic Superflat. Packaged as `flat.tuesengine`.

#### Verification for this step
- Changing `GeneratorType` in `settings.json` reliably swaps the active `.tuesengine` package.

---

## Step 2.2 — Structures & Carvers

**Goal**: Add subterranean caves and surface decorations.

### What is built in this step

#### Cave Carvers (3D Noise)
- Use 3D volumetric noise. If the noise drops below a threshold, replace the terrain block with Air.

#### C# Structure Decorators
- Run a hardcoded C# structure script (like a `TreeGenerator`) that places logs and leaves via fast math operations.

---

## Step 2.3 — Voxel Lighting Engine

**Goal**: Implement a robust lighting engine.

### What is built in this step

#### Concurrency Model & Memory Layout
- A **Dedicated Lighting Worker Thread** will process light propagation entirely in the background.
- Lighting data (4 bits SkyLight, 4 bits BlockLight) is packed into a **Separate `byte[]` array** within the `Chunk`. This ensures the main `ushort[]` voxel array remains tightly packed and cache-friendly for meshing.

#### SkyLight & BlockLight Propagation
- Sunlight travels perfectly downwards. If it hits a transparent block, it bleeds horizontally with a -1 dropoff.
- Torches emit light that floods outward in all 6 directions.

---

## Step 2.4 — World Persistence

**Goal**: Save modifications to disk.

### What is built in this step

#### Region Files
- Implement a system that bundles 32x32 chunks into a single binary `.tregion` file.

#### Full Chunk Saving (Async Background Task)
- When a chunk modified by the player falls out of bounds, clone its `ushort[]` array and dispatch an **Async Background Task** to compress (via Zlib) and write it to the Region file, completely avoiding main thread stutters.

---

## Phase 2 — Completion Criteria
1. The `TuesEngineLoader` successfully parses `.tuesengine` archives, loading custom blocks and sandboxed DLLs.
2. The four core generator types function distinctly as individual packages.
3. Shadows cast dynamically based on terrain height and player block placement.
4. Player modifications persist perfectly across game restarts via full chunk region files.
