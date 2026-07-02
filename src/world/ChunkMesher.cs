using System;
using System.Collections.Generic;
using Godot;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

/// <summary>
/// Return type for <see cref="ChunkMesher.BuildMesh"/>. Contains the visual mesh
/// and an optional collision shape built from solid voxel faces.
/// </summary>
	public class MeshResult
	{
		public Vector3[]? OpaqueVerts;
		public Vector3[]? OpaqueNormals;
		public Vector2[]? OpaqueUVs;
		public Vector2[]? OpaqueUV2s;
		public int[]? OpaqueIndices;

		public Vector3[]? TransVerts;
		public Vector3[]? TransNormals;
		public Vector2[]? TransUVs;
		public Vector2[]? TransUV2s;
		public int[]? TransIndices;

		public Vector3[]? CollisionFaces;
	}

/// <summary>
/// Stateless meshing utility. Reads a <see cref="Chunk"/>'s voxel data and produces
/// a Godot <see cref="ArrayMesh"/> using naive face-culled meshing. Each cube-mode
/// voxel's six faces are checked against their neighbours — hidden faces between
/// adjacent solid opaque voxels are culled, visible faces are emitted as quads with
/// atlas-mapped UVs.
/// </summary>
public static class ChunkMesher
{
	// ── Face geometry tables ────────────────────────────────────────────────
	//
	// For a unit cube at origin (0,0,0)→(1,1,1), each face is defined by:
	//   - A direction vector (which neighbour to check for culling)
	//   - An outward-pointing normal
	//   - Four vertex positions forming a quad
	//
	// Vertex winding is clockwise when viewed from outside the cube (Godot's
	// front-face convention). The cross product (v1-v0) × (v2-v0) yields the
	// outward normal for each face, confirming correct winding.
	//
	// Face indices: 0=+X, 1=−X, 2=+Y, 3=−Y, 4=+Z, 5=−Z

	private static readonly Vector3I[] FaceDirections =
	{
		new( 1,  0,  0), // 0: +X
		new(-1,  0,  0), // 1: −X
		new( 0,  1,  0), // 2: +Y
		new( 0, -1,  0), // 3: −Y
		new( 0,  0,  1), // 4: +Z
		new( 0,  0, -1), // 5: −Z
	};

	private static readonly Vector3[] FaceNormals =
	{
		new( 1,  0,  0),
		new(-1,  0,  0),
		new( 0,  1,  0),
		new( 0, -1,  0),
		new( 0,  0,  1),
		new( 0,  0, -1),
	};

	// Four vertices per face, ordered for clockwise winding (Godot front-face).
	// Offset from voxel origin — add (x, y, z) to get world-local position.
	private static readonly Vector3[][] FaceVertices =
	{
		// 0: +X face — visible from the right
		new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
		// 1: −X face — visible from the left
		new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
		// 2: +Y face — visible from above
		new[] { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
		// 3: −Y face — visible from below
		new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
		// 4: +Z face — visible from the front (towards camera in Godot's default view)
		new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
		// 5: −Z face — visible from the back
		new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
	};

	// UV offsets within a single tile for each quad vertex.
	// (0,0) = tile top-left, (1,1) = tile bottom-right.
	// For side faces (±X, ±Z): v0/v3 are at the bottom (Y=0), v1/v2 at the top (Y=1).
	// This maps the top of the tile to the top of the face and the bottom of the
	// tile to the bottom — so the grass-side tile (green top, brown bottom) renders
	// correctly with green at the top.
	private static readonly Vector2[] QuadUvs =
	{
		new(0, 1), // v0: bottom-left of tile
		new(0, 0), // v1: top-left
		new(1, 0), // v2: top-right
		new(1, 1), // v3: bottom-right
	};

	// ── Material cache ──────────────────────────────────────────────────────

	public static ShaderMaterial? OpaqueMaterial { get; private set; }
	public static ShaderMaterial? TransparentMaterial { get; private set; }

	/// <summary>
	/// Initialises the mesher with the atlas texture array. Must be called once before
	/// the first call to <see cref="BuildMesh"/>. Creates the opaque and transparent
	/// materials used for all chunk surfaces.
	/// </summary>
	/// <param name="atlasTexture">The texture atlas containing all voxel face slices.</param>
	public static void Initialize(Texture2DArray atlasTexture)
	{
		ArgumentNullException.ThrowIfNull(atlasTexture);

		string opaqueShaderCode = @"
shader_type spatial;
render_mode depth_draw_opaque, cull_back, diffuse_burley, specular_disabled;
uniform sampler2DArray albedo_texture : source_color, filter_nearest;

void fragment() {
    vec4 albedo_tex = texture(albedo_texture, vec3(UV, UV2.x));
    ALBEDO = albedo_tex.rgb;
}
";

		string transparentShaderCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_disabled;
uniform sampler2DArray albedo_texture : source_color, filter_nearest;

void fragment() {
    vec4 albedo_tex = texture(albedo_texture, vec3(UV, UV2.x));
    ALBEDO = albedo_tex.rgb;
    ALPHA = albedo_tex.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
";

		var opaqueShader = new Shader { Code = opaqueShaderCode };
		OpaqueMaterial = new ShaderMaterial { Shader = opaqueShader };
		OpaqueMaterial.SetShaderParameter("albedo_texture", atlasTexture);

		var transparentShader = new Shader { Code = transparentShaderCode };
		TransparentMaterial = new ShaderMaterial { Shader = transparentShader };
		TransparentMaterial.SetShaderParameter("albedo_texture", atlasTexture);
	}

	// ── Thread-local Meshing Context ────────────────────────────────────────

	private class MeshingContext
	{
		public readonly List<Vector3> OpaqueVerts = new(8192);
		public readonly List<Vector3> OpaqueNormals = new(8192);
		public readonly List<Vector2> OpaqueUVs = new(8192);
		public readonly List<Vector2> OpaqueUV2s = new(8192);
		public readonly List<int> OpaqueIndices = new(12288);

		public readonly List<Vector3> TransVerts = new(4096);
		public readonly List<Vector3> TransNormals = new(4096);
		public readonly List<Vector2> TransUVs = new(4096);
		public readonly List<Vector2> TransUV2s = new(4096);
		public readonly List<int> TransIndices = new(6144);

		public readonly List<Vector3> CollisionVerts = new(8192);

		public void Clear()
		{
			OpaqueVerts.Clear();
			OpaqueNormals.Clear();
			OpaqueUVs.Clear();
			OpaqueUV2s.Clear();
			OpaqueIndices.Clear();

			TransVerts.Clear();
			TransNormals.Clear();
			TransUVs.Clear();
			TransUV2s.Clear();
			TransIndices.Clear();

			CollisionVerts.Clear();
		}
	}

	[ThreadStatic] private static MeshingContext? _ctx;

	// ── Public API ──────────────────────────────────────────────────────────

	/// <summary>
	/// Reads the chunk's voxel data and produces a renderable <see cref="ArrayMesh"/>
	/// with face-culled geometry and atlas-mapped UVs.
	/// </summary>
	/// <param name="chunk">The chunk to mesh. Not modified.</param>
	/// <param name="neighbourLookup">
	/// Optional delegate resolving voxel IDs at world-space coordinates for
	/// cross-chunk neighbour checks. If null, out-of-bounds neighbours default to
	/// Air (all border faces render — correct for isolated testing).
	/// </param>
	/// <returns>
	/// A <see cref="MeshResult"/> containing the visual mesh and collision shape,
	/// or <c>null</c> if the chunk is entirely Air (no geometry produced).
	/// </returns>
	public static MeshResult? BuildMesh(
		Chunk chunk,
		Func<int, int, int, ushort>? neighbourLookup = null)
	{
		if (OpaqueMaterial is null)
			throw new InvalidOperationException(
				"ChunkMesher.Initialize() must be called before BuildMesh().");

		ushort[] voxels = chunk.Voxels;

		// Fast-paths: If the entire chunk is 100% Air, skip entirely.
		// If 100% Solid Opaque, we can skip the inner core during meshing.
		bool isAir = true;
		bool isSolidOpaque = true;

		for (int i = 0; i < Chunk.Volume; i++)
		{
			ushort id = voxels[i];
			if (id != VoxelRegistry.AirId)
			{
				isAir = false;
			}
			if (id == VoxelRegistry.AirId || id >= VoxelRegistry.Count || VoxelRegistry.TransparentTable[id] || !VoxelRegistry.OccludesTable[id])
			{
				isSolidOpaque = false;
			}
		}

		if (isAir) return null;

		_ctx ??= new MeshingContext();
		_ctx.Clear();

		bool hasOpaque = false;
		bool hasTransparent = false;

		int opaqueVertCount = 0;
		int transparentVertCount = 0;

		Vector3I worldPos = chunk.WorldPosition;

		// Iterate in Y-major order matching the chunk's memory layout
		// (Y outermost, Z middle, X innermost = contiguous memory access).
		for (int y = 0; y < Chunk.SizeY; y++)
		{
			for (int z = 0; z < Chunk.SizeZ; z++)
			{
				for (int x = 0; x < Chunk.SizeX; x++)
				{
					// Fast-path: Skip fully enclosed inner core of a solid opaque chunk
					if (isSolidOpaque && x > 0 && x < Chunk.SizeX - 1 && y > 0 && y < Chunk.SizeY - 1 && z > 0 && z < Chunk.SizeZ - 1)
						continue;

					ushort id = voxels[Chunk.FlatIndex(x, y, z)];

					if (id == VoxelRegistry.AirId || id >= VoxelRegistry.Count)
						continue;

					VoxelMeshMode meshMode = VoxelRegistry.MeshModeTable[id];
					switch (meshMode)
					{
						case VoxelMeshMode.None:
							continue;

						case VoxelMeshMode.Custom:
							// Placeholder: custom mesh stamping is deferred. The code path
							// exists but no custom-mesh types are in the starter set.
							Logger.Warning(
								$"ChunkMesher: Custom mesh mode not yet implemented " +
								$"(runtime ID {id} at ({x},{y},{z})).");
							continue;

						case VoxelMeshMode.Cube:
							EmitCubeFaces(
								x, y, z, id,
								chunk, worldPos, neighbourLookup,
								_ctx,
								ref hasOpaque, ref hasTransparent,
								ref opaqueVertCount, ref transparentVertCount);
							break;
					}
				}
			}
		}

		if (!hasOpaque && !hasTransparent)
			return null;

		// ── Package primitive arrays ────────────────────────────────────────

		var result = new MeshResult();

		if (hasOpaque)
		{
			result.OpaqueVerts = _ctx.OpaqueVerts.ToArray();
			result.OpaqueNormals = _ctx.OpaqueNormals.ToArray();
			result.OpaqueUVs = _ctx.OpaqueUVs.ToArray();
			result.OpaqueUV2s = _ctx.OpaqueUV2s.ToArray();
			result.OpaqueIndices = _ctx.OpaqueIndices.ToArray();
		}

		if (hasTransparent)
		{
			result.TransVerts = _ctx.TransVerts.ToArray();
			result.TransNormals = _ctx.TransNormals.ToArray();
			result.TransUVs = _ctx.TransUVs.ToArray();
			result.TransUV2s = _ctx.TransUV2s.ToArray();
			result.TransIndices = _ctx.TransIndices.ToArray();
		}

		if (_ctx.CollisionVerts.Count > 0)
		{
			result.CollisionFaces = _ctx.CollisionVerts.ToArray();
		}

		return result;
	}

	// ── Core meshing logic ──────────────────────────────────────────────────

	/// <summary>
	/// Emits the visible faces for a single cube-mode voxel. Each of the 6 faces
	/// is checked against its neighbour for culling. Visible faces are added to
	/// the appropriate SurfaceTool (opaque or transparent) and to the collision
	/// vertex list (solid faces only).
	/// </summary>
	private static void EmitCubeFaces(
		int x, int y, int z,
		ushort id,
		Chunk chunk, Vector3I worldPos,
		Func<int, int, int, ushort>? neighbourLookup,
		MeshingContext ctx,
		ref bool hasOpaque, ref bool hasTransparent,
		ref int opaqueVertCount, ref int transparentVertCount)
	{
		bool isTransparent = VoxelRegistry.TransparentTable[id];
		Vector3 origin = new(x, y, z);

		for (int face = 0; face < 6; face++)
		{
			Vector3I dir = FaceDirections[face];
			int nx = x + dir.X;
			int ny = y + dir.Y;
			int nz = z + dir.Z;

			// Resolve the neighbour's voxel ID
			ushort neighbourId = ResolveNeighbour(
				nx, ny, nz, chunk, worldPos, neighbourLookup);

			// Face culling
			if (ShouldCullFace(id, neighbourId, isTransparent))
				continue;

			// ── Emit quad geometry ──────────────────────────────────────

			int texIndex = GetTextureIndex(id, face);
			Vector2 uv2Coord = new Vector2(texIndex, 0);

			Vector3 normal = FaceNormals[face];
			Vector3[] verts = FaceVertices[face];

			// Compute the 4 vertex positions
			Vector3 p0 = origin + verts[0];
			Vector3 p1 = origin + verts[1];
			Vector3 p2 = origin + verts[2];
			Vector3 p3 = origin + verts[3];

			int baseIndex = isTransparent ? transparentVertCount : opaqueVertCount;

			if (isTransparent)
			{
				ctx.TransNormals.Add(normal); ctx.TransUVs.Add(QuadUvs[0]); ctx.TransUV2s.Add(uv2Coord); ctx.TransVerts.Add(p0);
				ctx.TransNormals.Add(normal); ctx.TransUVs.Add(QuadUvs[1]); ctx.TransUV2s.Add(uv2Coord); ctx.TransVerts.Add(p1);
				ctx.TransNormals.Add(normal); ctx.TransUVs.Add(QuadUvs[2]); ctx.TransUV2s.Add(uv2Coord); ctx.TransVerts.Add(p2);
				ctx.TransNormals.Add(normal); ctx.TransUVs.Add(QuadUvs[3]); ctx.TransUV2s.Add(uv2Coord); ctx.TransVerts.Add(p3);

				// Triangle 1: v0 → v2 → v1 (Clockwise winding)
				ctx.TransIndices.Add(baseIndex + 0); ctx.TransIndices.Add(baseIndex + 2); ctx.TransIndices.Add(baseIndex + 1);
				// Triangle 2: v0 → v3 → v2 (Clockwise winding)
				ctx.TransIndices.Add(baseIndex + 0); ctx.TransIndices.Add(baseIndex + 3); ctx.TransIndices.Add(baseIndex + 2);

				hasTransparent = true;
				transparentVertCount += 4;
			}
			else
			{
				ctx.OpaqueNormals.Add(normal); ctx.OpaqueUVs.Add(QuadUvs[0]); ctx.OpaqueUV2s.Add(uv2Coord); ctx.OpaqueVerts.Add(p0);
				ctx.OpaqueNormals.Add(normal); ctx.OpaqueUVs.Add(QuadUvs[1]); ctx.OpaqueUV2s.Add(uv2Coord); ctx.OpaqueVerts.Add(p1);
				ctx.OpaqueNormals.Add(normal); ctx.OpaqueUVs.Add(QuadUvs[2]); ctx.OpaqueUV2s.Add(uv2Coord); ctx.OpaqueVerts.Add(p2);
				ctx.OpaqueNormals.Add(normal); ctx.OpaqueUVs.Add(QuadUvs[3]); ctx.OpaqueUV2s.Add(uv2Coord); ctx.OpaqueVerts.Add(p3);

				// Triangle 1: v0 → v2 → v1 (Clockwise winding)
				ctx.OpaqueIndices.Add(baseIndex + 0); ctx.OpaqueIndices.Add(baseIndex + 2); ctx.OpaqueIndices.Add(baseIndex + 1);
				// Triangle 2: v0 → v3 → v2 (Clockwise winding)
				ctx.OpaqueIndices.Add(baseIndex + 0); ctx.OpaqueIndices.Add(baseIndex + 3); ctx.OpaqueIndices.Add(baseIndex + 2);

				hasOpaque = true;
				opaqueVertCount += 4;
			}

			// Collision geometry (solid voxels only)
			if (VoxelRegistry.OccludesTable[id])
			{
				ctx.CollisionVerts.Add(p0);
				ctx.CollisionVerts.Add(p2);
				ctx.CollisionVerts.Add(p1);

				ctx.CollisionVerts.Add(p0);
				ctx.CollisionVerts.Add(p3);
				ctx.CollisionVerts.Add(p2);
			}
		}
	}

	// ── Helpers ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Resolves the voxel ID at the given local neighbour coordinate.
	/// In-bounds: reads from the chunk. Out-of-bounds: delegates to the
	/// neighbour lookup (or returns Air if no lookup is provided).
	/// </summary>
	private static ushort ResolveNeighbour(
		int nx, int ny, int nz,
		Chunk chunk, Vector3I worldPos,
		Func<int, int, int, ushort>? neighbourLookup)
	{
		// Inlined fast bounds check and flat array access.
		if ((uint)nx < Chunk.SizeX && (uint)ny < Chunk.SizeY && (uint)nz < Chunk.SizeZ)
		{
			return chunk.Voxels[nx + Chunk.SizeX * (nz + Chunk.SizeZ * ny)];
		}

		if (neighbourLookup is not null)
			return neighbourLookup(worldPos.X + nx, worldPos.Y + ny, worldPos.Z + nz);

		return VoxelRegistry.AirId;
	}

	/// <summary>
	/// Determines whether the face between the current voxel and its neighbour
	/// should be culled (not rendered).
	/// </summary>
	private static bool ShouldCullFace(
		ushort currentId, ushort neighbourId, bool currentIsTransparent)
	{
		// Always render faces adjacent to Air or invalid IDs
		if (neighbourId == VoxelRegistry.AirId || neighbourId >= VoxelRegistry.Count)
			return false;

		if (currentIsTransparent)
		{
			// Transparent voxels cull only against same-type neighbours.
			return currentId == neighbourId;
		}

		// Opaque voxels cull against neighbours that physically occlude faces.
		return VoxelRegistry.OccludesTable[neighbourId];
	}

	/// <summary>
	/// Returns the texture atlas index for the given face of a voxel type.
	/// +Y → top, −Y → bottom, all others → side.
	/// </summary>
	private static int GetTextureIndex(ushort id, int face)
	{
		return face switch
		{
			2 => VoxelRegistry.TextureTopTable[id],    // +Y (top)
			3 => VoxelRegistry.TextureBottomTable[id], // −Y (bottom)
			_ => VoxelRegistry.TextureSideTable[id],   // ±X, ±Z (sides)
		};
	}
}
