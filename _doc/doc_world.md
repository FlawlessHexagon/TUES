# The Universal Entertainment System
## World Architecture Document (`doc_world.md`)

*This document outlines the architecture of the TUES voxel world, detailing the current highly-optimized implementation of the chunk system, and the Dimension Engine API used to generate terrain.*

---

## 1. Goal Vision

The vision for the TUES world architecture is to build a **massively scalable, lightning-fast, and infinitely moddable voxel universe**. 
Unlike legacy voxel engines that enforce strict global limits (e.g., exactly 256 blocks high, exactly 1 thread for generation), the TUES world is designed to be entirely fluid. 

**Key Principles of the Vision:**
- **Dynamic Scale**: The engine should not dictate world height. Dimension Engines will define their own vertical bounds (`MinY` to `MaxY`), allowing for hyper-scaled mountains or bottomless subterranean realms based on the player's hardware.
- **Zero-Stutter Streaming**: Moving through the world must be entirely seamless. The main thread should do nothing but present finished meshes to the player.
- **Total Modularity**: "The World" is not a hardcoded math function. It is a sandbox where community-created `.tuesengine` packages can fundamentally alter gravity, terrain, lighting, and block behaviors.

---

## 2. Chunk Architecture (Current Implementation)

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

## 3. Dimension Engines (`.tuesengine`)

The core game has zero hardcoded knowledge of how the terrain is generated. Instead, terrain is generated using Dimension Engines (`.tuesengine` files).

> **Note:** As a transitional measure during Step 2.0 implementation, `WorldGenerator.cs` currently contains fallback hardcoded generators (`Simplex`, `Perlin`, `Extreme`, `Superflat`) to support testing before the built-in `.tuesengine` packages are fully implemented in Step 2.1. These hardcoded shims will be removed once Step 2.1 is complete.

### 3.1 The Package Format

A `.tuesengine` file is simply a standard `.zip` archive. The engine will extract this archive into memory or a local cache directory to read its contents.

**Internal Directory Structure:**
At its absolute simplest, a Dimension Engine contains:

```text
my_custom_world.tuesengine/
│
├── manifest.json               # Required: Identifies the package, dependencies, and entry point
├── generator.dll               # Required: The compiled C# math logic
│
└── assets/                     # Optional: Assets specific to this dimension
    ├── blocks.json             # Definitions for new custom blocks
    └── textures/               # PNG files for the custom blocks
        └── alien_dirt.png
```

### 3.2 The `manifest.json`

The manifest tells the core game exactly what it is loading.

```json
{
    "id": "community:alien_world",
    "name": "The Alien World",
    "version": "1.0.0",
    "entry_dll": "generator.dll",
    "dependencies": [
        "community:some_required_mod"
    ]
}
```

> **Note:** The loader discovers the generator class by scanning the DLL for a type implementing `IDimensionGenerator` with a matching `[DimensionEngine("id")]` attribute, rather than requiring an explicit class name in the manifest.

**Design Decision: Manifest Dependencies**
*Status: Planned (Hard Dependencies)*
If an engine wants to use a block provided by a separate `.tuesmod` addon, the package must declare it in the `dependencies` array. The core loader will enforce **Hard Dependencies**—it will gracefully refuse to load the `.tuesengine` if any required addon is missing, preventing unpredictable generator crashes and broken terrain.

> **Note:** The current loader contains a stub check only. Real dependency validation requires a loaded-package registry, which will be built alongside Step 2.1 (built-in `.tuesengine` packages).

### 3.3 The C# API (`IDimensionGenerator`)

The `.dll` provided in the package must contain a class implementing the `IDimensionGenerator` interface. To ensure the engine is fully capable of scaling to complex terrain, the interface requires distinct systems:

**A. Initialization & Registry Injection**
*Status: Finalized (Dependency Injection)*
A custom DLL generates chunks by writing `ushort` integers to an array. But it needs to know the integer ID of `"tues:grass"` or its own custom `"alien:dirt"`. The core engine injects an `IRegistryAccess` interface during initialization so the DLL can cache these IDs without relying on global singletons, preserving the hermetic sandbox.

```csharp
    // Called once when the world is loaded. 
    // IRegistryAccess allows the DLL to query string IDs and get runtime ushort IDs.
    void Initialize(int seed, IRegistryAccess registry);
```

**B. The Generation Pipeline**
*Status: Finalized (Direct Array Modification)*
If a generator tries to place a tree on the very edge of a chunk, the leaves will bleed into the neighbor chunk. If the engine blindly requests the neighbor chunk, the neighbor will generate, which might place a tree bleeding into the next... causing an infinite lag loop. 

To solve this, chunk generation is strictly split into two passes. The Decorate pass receives an `IWorldAccess` interface to perform fast **Direct Array Modifications** on neighboring chunks, avoiding the massive overhead of event-driven updates.

```csharp
    // Pass 1: Base Terrain Geometry. 
    // MUST be strictly confined within the 16x16 chunk boundaries.
    void GenerateChunk(Chunk chunk);

    // Pass 2: Decoration (Trees, Ores, Structures). 
    // Called ONLY after all neighboring chunks have completed Pass 1.
    // Safe to write to neighboring chunks via IWorldAccess.
    void Decorate(Chunk chunk, IWorldAccess world);
```

**C. Dynamic World Bounds**
*Status: Planned (Phase 2)*
The engine will dictate how high or deep it goes.
```csharp
    int MinY { get; }
    int MaxY { get; }
```

> **Note:** The current implementation uses a fixed 8-chunk (128-block) vertical range. Dynamic bounds from the engine's `MinY`/`MaxY` will be integrated when the two-pass Generate/Decorate pipeline (§3.3.B) is implemented.

### 3.4 How it Works (The Simple Lifecycle)

1. The player drops `alien.tuesengine` into their `user://dimensions/` folder.
2. The player sets `"GeneratorType": "community:alien_world"` in `settings.json`.
3. The game boots, unzips the package, and reads `manifest.json`.
4. The game validates `dependencies`. If `community:some_required_mod` is missing, the engine gracefully aborts loading.
5. The game dynamically injects `alien_dirt.png` into the global atlas and registers the blocks.
6. The game loads `generator.dll` inside a secure `AssemblyLoadContext` sandbox.
7. The game calls `Initialize(seed, registry)` so the engine can query its block IDs.
8. The `ChunkManager` begins running `GenerateChunk` on all chunks, followed safely by `Decorate`!
