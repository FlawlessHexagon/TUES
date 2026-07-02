# TUES Multiplayer & Networking Documentation
## General Design Document

*This document defines the strict boundaries and protocols between the Host (Server) and Clients in a multiplayer environment, ensuring that the "Game Engine" customizability of TUES translates seamlessly into networked play.*

---

## 1. The Trust Model

In a networked environment where players can break blocks, move freely, and interact with the physical world, establishing a clear line of authority is paramount to prevent cheating and desynchronization.

TUES operates on a **Server-Authoritative with Client Prediction** model.

### 1.1 Server Authority
The Server is the absolute dictator of the world state. The server calculates true physics, verifies all interactions, manages the authoritative voxel grid, and governs global entity logic. A client cannot simply inform the server, "I destroyed this block"; the client must send an *action request* ("I am attempting to destroy the block at X,Y,Z"). The server independently verifies if this action is legal (e.g., the player is within range, has the correct tool, and the block exists).

### 1.2 Client Prediction
To maintain the snappy, zero-latency feel essential to modern gaming, clients do not wait for the server's permission before updating their screen. When a player breaks a block or moves forward, the client *predicts* the success of the action and renders it immediately locally. 
- If the server agrees, it silently applies the change and broadcasts it to other players.
- If the server **disagrees** (e.g., due to lag, the player was actually out of range), the server issues a forceful correction to the client, snapping their position back or restoring the broken block.

---

## 2. Chunk Data Synchronisation

Because a single TUES chunk contains 4,096 voxels, transmitting raw chunk data to every connected player every time they move is unfeasible. To solve this, TUES leverages its highly customizable engine nature by offering configurable networking protocols. The Server Host decides which protocol to use based on their specific needs.

### 2.1 Protocol A: Seed-Sharing & Delta Sync (Default)
This is the default protocol, designed for maximum efficiency and massively reduced bandwidth.
1. Upon connecting, the Server transmits the **World Seed** and the active **Dimension Engine** configurations to the Client.
2. As the Client moves, they generate the base, untouched terrain locally using their own CPU.
3. The Server monitors the chunks the Client is loading and *only* sends the **Deltas** (the specific voxels that have been modified by players or events since the world was created).

### 2.2 Protocol B: Raw Streaming (Alternative)
For servers hosting heavily customized worlds where the generation algorithm is either private (to prevent players from reverse-engineering the map) or too computationally heavy for weak clients, the Server can enforce Raw Streaming.
1. The Client does no local terrain generation.
2. The Server generates all chunks internally and compresses the raw voxel arrays, streaming them directly to the Client.
This uses significantly more bandwidth but centralizes all processing power and logic on the server.

---

## 3. Entity & Physics Simulation Policy

In a massive open world, thousands of entities (mobs, dropped items, falling blocks) may exist simultaneously. Simulating all of them globally would instantly crash any server. As with all things in TUES, the solution is configurable by the Host.

### 3.1 Active Simulation Distance
By default, the server only "ticks" (updates physics and AI) for entities that are within the `SimulationDistance` of an active player. Entities outside this radius are frozen in memory or serialized to disk. This ensures the server only spends CPU cycles on events that a player can actually observe.

### 3.2 Global & Hybrid Simulation
Server Hosts with sufficient hardware can opt to override this behavior. A server may define **Global Simulation** (ticking everything everywhere) for specific mechanics like crop growth or automated machinery, while leaving complex AI entities on Active Simulation Distance. The engine provides the architectural mechanisms; the Server Host turns the dials.
