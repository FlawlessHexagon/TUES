# Dimension Engines (`.tuesengine`)
## Basic Outline & Architecture Spec

*This document outlines the simplest foundational structure of a Dimension Engine package. This is a living document and will be expanded as the module system grows.*

---

## 1. The Package Format

A `.tuesengine` file is simply a standard `.zip` archive. The engine will extract this archive into memory or a local cache directory to read its contents.

### Internal Directory Structure
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

---

## 2. The `manifest.json`

The manifest tells the core game exactly what it is loading.

```json
{
    "id": "community:alien_world",
    "name": "The Alien World",
    "version": "1.0.0",
    "entry_dll": "generator.dll",
    "entry_class": "AlienWorld.DimensionGenerator",
    "dependencies": [
        "community:some_required_mod"
    ]
}
```

### Design Decision: Manifest Dependencies
*Status: Finalized (Hard Dependencies)*
If an engine wants to use a block provided by a separate `.tuesmod` addon, the package must declare it in the `dependencies` array. The core loader enforces **Hard Dependencies**—it will gracefully refuse to load the `.tuesengine` if any required addon is missing, preventing unpredictable generator crashes and broken terrain.

---

## 3. The C# API (`IDimensionGenerator`)

The core game has zero hardcoded knowledge of how the terrain is generated. The `.dll` provided in the package must contain a class implementing the `IDimensionGenerator` interface.

To ensure the engine is fully capable of scaling to complex terrain, the interface requires three distinct systems:

### A. Initialization & Registry Injection
*Status: Finalized (Dependency Injection)*

A custom DLL generates chunks by writing `ushort` integers to an array. But it needs to know the integer ID of `"tues:grass"` or its own custom `"alien:dirt"`. The core engine injects an `IRegistryAccess` interface during initialization so the DLL can cache these IDs without relying on global singletons, preserving the hermetic sandbox.

```csharp
    // Called once when the world is loaded. 
    // IRegistryAccess allows the DLL to query string IDs and get runtime ushort IDs.
    void Initialize(int seed, IRegistryAccess registry);
```

### B. The 2-Pass Generation Pipeline
*Status: Finalized (Direct Array Modification)*

If a generator tries to place a tree on the very edge of a chunk, the leaves will bleed into the neighbor chunk. If the engine blindly requests the neighbor chunk, the neighbor will generate, which might place a tree bleeding into the next... causing an infinite lag loop. 

To solve this, chunk generation is strictly split into two passes. The Decorate pass receives an `IWorldAccess` interface to perform fast **Direct Array Modifications** on neighboring chunks, avoiding the massive overhead of event-driven updates.

```csharp
    // Pass 1: Base Terrain Geometry. 
    // MUST be strictly confined within the 16x16 chunk boundaries.
    void GenerateTerrain(Chunk chunk);

    // Pass 2: Decoration (Trees, Ores, Structures). 
    // Called ONLY after all neighboring chunks have completed Pass 1.
    // Safe to write to neighboring chunks via IWorldAccess.
    void Decorate(Chunk chunk, IWorldAccess world);
```

### C. Dynamic World Bounds
The engine dictates how high or deep it goes.
```csharp
    int MinY { get; }
    int MaxY { get; }
```

---

## 4. How it Works (The Simple Lifecycle)

1. The player drops `alien.tuesengine` into their `user://dimensions/` folder.
2. The player sets `"GeneratorType": "community:alien_world"` in `settings.json`.
3. The game boots, unzips the package, and reads `manifest.json`.
4. The game validates `dependencies`. If `community:some_required_mod` is missing, the engine gracefully aborts loading.
5. The game dynamically injects `alien_dirt.png` into the global atlas and registers the blocks.
6. The game loads `generator.dll` inside a secure `AssemblyLoadContext` sandbox.
7. The game calls `Initialize(seed, registry)` so the engine can query its block IDs.
8. The `ChunkManager` begins running `GenerateTerrain` on all chunks, followed safely by `Decorate`!
