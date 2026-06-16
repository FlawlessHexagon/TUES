# The Universal Entertainment System
## World Architecture Document (`doc_world.md`)

*This document outlines the architecture of the TUES voxel world, detailing our ultimate vision, the current highly-optimized implementation, and our immediate future plans.*

---

## 1. Goal Vision

The vision for the TUES world architecture is to build a **massively scalable, lightning-fast, and infinitely moddable voxel universe**. 
Unlike legacy voxel engines that enforce strict global limits (e.g., exactly 256 blocks high, exactly 1 thread for generation), the TUES world is designed to be entirely fluid. 

**Key Principles of the Vision:**
- **Dynamic Scale**: The engine should not dictate world height. Dimension Engines will define their own vertical bounds (`MinY` to `MaxY`), allowing for hyper-scaled mountains or bottomless subterranean realms based on the player's hardware.
- **Zero-Stutter Streaming**: Moving through the world must be entirely seamless. The main thread should do nothing but present finished meshes to the player.
- **Total Modularity**: "The World" is not a hardcoded math function. It is a sandbox where community-created `.tuesengine` packages can fundamentally alter gravity, terrain, lighting, and block behaviors.

---

## 2. Current Implementation (What is Done Now)

Phase 0 and Phase 1 successfully established the atomic layer of the world. The current architecture handles memory, multi-threading, and mesh generation flawlessly.

### 2.1 The Chunk (`Chunk.cs`)
The atomic unit of the world is a 16x16x16 `Chunk`.
- **Memory Layout**: Stores 4,096 voxels in a flat `ushort[]` array. It uses **Y-major ordering** (X is contiguous, Z is the middle axis, Y is the outermost) to guarantee optimal CPU cache-hits during meshing loops.
- **Zero Allocations**: Chunks request memory from the C# `ArrayPool<ushort>.Shared`. When a chunk unloads, the memory is returned to the pool, resulting in zero garbage collection overhead during rapid traversal.
- **Thread-Safe State Machine**: Uses volatile reads/writes (`Interlocked.CompareExchange`) on a `ChunkState` enum to orchestrate handoffs between the Generation Thread, Meshing Thread, and Main Thread.

### 2.2 The Chunk Manager (`ChunkManager.cs`)
The master orchestrator of the world treadmill. 
- **Cylindrical Streaming**: Calculates 2D Euclidean distance to load chunks in a perfect cylinder around the player, rather than a jarring square.
- **Multi-Threaded Pipelines**: Spawns independent Task workers for chunk generation and mesh building based on the system's CPU core count.
- **Seamless Mesh Swapping**: When a block is broken, the chunk is pushed back to the background meshing thread. The *old* mesh remains visible until the *new* mesh is fully attached on the main thread, completely eliminating visual flickering.
- **Collision Streaming**: Extracts collision faces from the mesher and dynamically builds Godot `StaticBody3D` nodes close to the player, while silently unloading distant collision bodies to save physics overhead.

### 2.3 The Chunk Mesher (`ChunkMesher.cs`)
The geometry generator.
- Evaluates every voxel against its 6 neighbors (fetching neighbors from adjacent chunks if necessary).
- Skips hidden faces (Face Culling).
- Outputs raw `Vector3` vertices and UV coordinates mapping to the global `Texture2DArray` atlas.

---

## 3. Future Plans (Phase 2 & Beyond)

Phase 2 will transition this sterile mathematical grid into a living, atmospheric, and persistent universe.

### 3.1 Step 2.0: The `.tuesengine` Package Pipeline
The current hardcoded `WorldGenerator` will be completely gutted. 
- A new `TuesEngineLoader` will load external `.tuesengine` archives, parsing their manifests and injecting custom blocks into the global registry.
- It will load the packaged `.dll` files using secure `AssemblyLoadContext` sandboxing.
- `ChunkManager` will be refactored to remove the hardcoded 128-block height limit. Instead, it will query the active Dimension Engine for its `MinY` and `MaxY` bounds.

### 3.2 Step 2.1: The Built-In `.tuesengine` Packages
The core game will ship with native `.tuesengine` implementations for Default, Smooth, Extreme, and Flat world types.

### 3.3 Step 2.2: Structures & Decorators
Following base terrain generation, the active engine will run secondary passes to carve out 3D noise caves and place hardcoded C# structures (like trees, boulders, and lakes).

### 3.4 Step 2.3: Lazy Lighting Engine
A new background worker will be introduced: the **Dedicated Lighting Thread**.
- It will calculate a 4-bit SkyLight value (dropping by -1 horizontally) and a 4-bit BlockLight value (flood-filling from torches in 6 directions).
- These light values will be passed to the `ChunkMesher` to bake shadows directly into the vertex colors.

### 3.5 Step 2.4: World Persistence
When a chunk modified by the player falls out of bounds, it is not discarded.
- Instead, the chunk's `ushort[]` array is cloned and an **Async Background Task** is dispatched.
- This background task compresses the data via Zlib and writes it to a `.tregion` binary file on disk, completely avoiding main thread stutters.
- Untouched chunks are simply discarded and mathematically regenerated next time, saving massive amounts of disk space.
