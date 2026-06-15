# Development Phase 1 — The Player
## Detailed Implementation Plan (Recursively Re-evaluated & Microscopically Polished)

*Phase 1 introduces the human element into the world. Following a strict recursive evaluation against the project's core pillars—Scalability, Customizability, and Hyper-Optimization—this plan has been aggressively streamlined to ensure fast input-to-output transitions, minimal network packet overhead, and extreme physics simplicity. Microscopically, we focus on mitigating C# garbage collection spikes and floating-point edge cases.*

---

## Guiding Principles for Phase 1 (Re-evaluated)

1. **Absolute Input Immediacy.** Input latency is unacceptable. Visual feedback (Camera rotation, block selection outlines) must be processed continuously in the visual frame (`_Process` or `_UnhandledInput`). Physics movement and state mutation must remain locked to `_PhysicsProcess`. The player must never feel "lag" between mouse movement and visual update.
2. **Deterministic & Minimalist Physics.** Do not use complex collision shapes (like `CapsuleShape3D` or `SphereShape3D`). Voxel worlds are cubic; player collision must be an Axis-Aligned Bounding Box (AABB, via `BoxShape3D`). Complex colliders create performance bottlenecks and complex edge-case networking (getting snagged on voxel corners). Player movement must be a pure function of `[Position, Velocity, Inputs]` to guarantee tiny player data packets in Phase 3.
3. **Delta-Event World Modification.** To completely avoid chunk-size network bottlenecks later, the `ChunkManager` must not simply update arrays silently. Every block break/place must be architected as a **Voxel Delta Event** (a tiny struct containing `Vector3I Coordinate` and `ushort NewVoxelID`). This guarantees we send 6-byte updates over the network, not 8-kilobyte chunk resends.
4. **Zero-Scene Philosophy.** The Player hierarchy must be constructed entirely in C# (`Player.cs`). Do not rely on `.tscn` files. This maintains the data-driven "Atomic Layer" philosophy, prevents merge conflicts, and ensures absolute control over initialization.

---

## Step 1.0 — Player Controller (Optimized for Networking)

**Goal**: Construct a minimally complex player entity capable of physics-based movement that is easy to sync and perfectly suited for voxel interaction.

### What is built in this step

#### The `Player` Node
A C# class inheriting from `CharacterBody3D`. It must be initialized purely via code.

- **CollisionShape3D (AABB strictly)**: Use a `BoxShape3D` (e.g., 0.6w x 1.8h x 0.6d). By using a strict AABB, we avoid the heavy math required for capsule-to-mesh collisions and drastically simplify server-side trajectory validation.
- **Movement Logic**: 
  - Keep movement math explicitly simple: Walk, Sprint, Jump, Gravity. 
  - Do not implement complex state machines (e.g., wall-running, vaulting) at this layer. The network must only need to sync a velocity vector and a few state flags.
  - Process movement exclusively in `_PhysicsProcess(double delta)`.

#### ChunkManager Injection
The Player receives a `ChunkManager` reference at initialization via Dependency Injection to avoid global singletons, keeping the module entirely hermetic.

#### Verification for this step
- Spawn a `Player` node.
- Confirm AABB perfectly collides with chunk mesh floors without "jittering" or getting stuck on the invisible seams between chunks.
- No complex physics anomalies when sliding against walls.

---

## Step 1.1 — Camera & Input (Optimized for Zero-Latency)

**Goal**: Implement zero-latency mouse look and movement binding.

### What is built in this step

#### Programmatic `InputMap` Registration
Register actions in C# (`move_forward`, `jump`, `interact_break`, `interact_place`). No `project.godot` pollution. Ensure physical key mapping is used (`PhysicalKeycode`) so controls adapt natively to AZERTY or QWERTY layouts without user reconfiguration.

#### The Zero-Latency Camera
- Instantiate a `Camera3D` child node.
- **Crucial Latency Fix**: Mouse-look logic MUST be processed in `_UnhandledInput(InputEvent @event)`. Applying rotation here ensures that the camera rotates *before* the rendering pass of the current frame, completely eliminating the 1-frame input lag that occurs if rotation is deferred to `_PhysicsProcess` or regular `_Process`.
- Clamp the camera pitch to prevent gimbal lock (-89 to 89 degrees).

#### Movement Binding
Map the directional inputs to the `Player`'s velocity vector relative to the camera's yaw, ensuring forward input moves exactly where the camera points horizontally.

#### Verification for this step
- Mouse look feels absolutely immediate, regardless of physics tick rate.
- WASD maps correctly to the visually immediate camera yaw.

---

## Step 1.2 — Voxel Interaction (DDA, Epsilon-Safety, & Delta Events)

**Goal**: Allow block breaking/placing with mathematical precision, outputting tiny data deltas to future-proof the network.

### What is built in this step

#### The DDA Traversal Algorithm (Epsilon-Safe)
Implement a pure mathematical 3D Digital Differential Analyzer (DDA).
- **Why?**: Physics raycasts against mesh colliders are computationally heavy and prone to floating-point rounding errors on block edges. DDA uses pure integer math to step through the grid, yielding absolute precision with near-zero performance cost.
- **Microscopic Detail**: Ray origins exactly on integer bounds (e.g., `Z = 1.00000000`) can cause DDA logic to step backwards or miscalculate the starting cell. The implementation must explicitly handle float-epsilon edge cases when starting rays on axis-aligned planes.
- **Output**: The integer coordinates of the exact voxel hit and its normal.

#### Voxel Delta Architecture (Network Preparation)
- When interacting, do NOT just call a private array setter.
- Implement `ChunkManager.ApplyVoxelDelta(Vector3I globalPos, ushort newVoxelId)`.
- This method updates the local chunk, flags it for remeshing, AND emits a C# event/signal: `public event Action<Vector3I, ushort> OnVoxelChanged;`.
- **Microscopic Detail - Thread Safety**: Ensure `OnVoxelChanged` is invoked safely. If chunk modifications originate from a background worker (e.g., world generation completing a structure), the event must be dispatched to the main thread via `CallDeferred` before notifying UI or Rendering systems.
- **Why?**: By establishing this event now, Phase 3 networking simply subscribes to this event to broadcast a tiny packet to clients. This completely circumvents the chunk-packet size bottleneck.

#### Verification for this step
- Breaking/placing works with mathematical perfection via DDA (no missing blocks at steep angles).
- A debug listener attached to `OnVoxelChanged` correctly prints the tiny delta changes, proving the system is network-ready and thread-safe.

---

## Step 1.3 — Basic HUD (Zero-Allocation UI)

**Goal**: Provide on-screen feedback without introducing rendering bottlenecks or C# garbage collection spikes.

### What is built in this step

#### Dynamic HUD Construction
Built entirely via C# using Godot's `Control` nodes on a `CanvasLayer` to ensure UI scaling is perfectly decoupled from 3D camera transforms.

#### Debug Information & Zero-Allocation Strategy
- Display FPS, Player Position, and Active Chunks.
- **Microscopic Detail - Garbage Collection Prevention**: Do NOT update text labels inside `_Process` every frame. String interpolation (`$"FPS: {fps}"`) allocates a new string object every frame, triggering constant C# Garbage Collection (GC) sweeps which cause stuttering. Instead, throttle the debug label update using a `Timer` set to `0.25s` (4 times a second).
- Allow 1-9 key presses to cycle the active block type.

#### Verification for this step
- UI elements update efficiently without causing frame drops or GC spikes (confirmable via Godot's built-in profiler).

---

## Phase 1 — Completion Criteria

1. Player navigation functions smoothly with strict AABB collisions (no getting stuck on chunk boundaries).
2. Mouse-look visual feedback is absolute and immediate (processed independently of physics ticks).
3. Voxel modifications (DDA) trigger `ApplyVoxelDelta`, successfully emitting a tiny data event to prove network scalability.
4. Breaking/placing accurately remeshes only the affected chunks (and neighbours, if on a seam).
5. All implementations remain in C# with zero `.tscn` or `project.godot` dependencies.
6. HUD debug UI is throttled to prevent string-allocation stuttering.

**When these criteria are met, Phase 2 begins.**

---

## File Structure (Recommended)

Organize the Phase 1 implementation cleanly within the source tree.

```text
res://src/
├── ... (Phase 0 files)
├── player/
│   ├── Player.cs                 ← CharacterBody3D, node assembly, movement logic
│   ├── InputRegistration.cs      ← Programmatic InputMap configuration
│   └── PlayerHud.cs              ← CanvasLayer, Crosshair, Debug info
└── physics/
    └── DdaRaycast.cs             ← Pure static function for voxel grid traversal
```
