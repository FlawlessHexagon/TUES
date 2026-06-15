using System;
using System.Collections.Generic;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Return type for <see cref="ChunkMesher.BuildMesh"/>. Contains the visual mesh
/// and an optional collision shape built from solid voxel faces.
/// </summary>
public readonly struct MeshResult
{
	public readonly ArrayMesh Mesh;
	public readonly Vector3[]? CollisionFaces;

	public MeshResult(ArrayMesh mesh, Vector3[]? collisionFaces)
	{
		Mesh = mesh;
		CollisionFaces = collisionFaces;
	}
}

/// <summary>
/// Stateless meshing utility. Reads a <see cref="Chunk"/>'s voxel data and produces
/// a Godot <see cref="ArrayMesh"/> using naive face-culled meshing. Each cube-mode
/// voxel's six faces are checked against their neighbours — hidden faces between
/// adjacent solid opaque voxels are culled, visible faces are emitted as quads with
/// atlas-mapped UVs.
///
/// The mesher does not modify the chunk, does not access the scene tree, and does not
/// own the meshes it produces. The ChunkManager (Step 0.3) attaches the returned mesh
/// to the scene tree.
///
/// Opaque and transparent faces are separated into distinct surfaces on the ArrayMesh
/// for correct render ordering.
/// </summary>
public static class ChunkMesher
{
	// ── Atlas layout constants ──────────────────────────────────────────────
	//
	// The texture atlas is a 48×32 pixel image containing 6 tiles (3 columns × 2 rows)
	// of 16×16 pixels each. Five tiles are used by the starter voxel types:
	//
	//   Index 0 (0,0): Grass top    — solid green
	//   Index 1 (1,0): Grass side   — green top half, brown bottom half
	//   Index 2 (2,0): Dirt         — solid brown
	//   Index 3 (0,1): Stone        — solid grey
	//   Index 4 (1,1): Bedrock      — solid dark grey
	//   Index 5 (2,1): Unused pad   — black
	//
	// UV coordinates map each face quad to one tile. The tile's position in UV space
	// is computed from the texture index at mesh time.

	private const int AtlasTilePixels = 16;
	private const int AtlasCols = 3;
	private const int AtlasRows = 2;
	private const float UvTileWidth = 1.0f / AtlasCols;
	private const float UvTileHeight = 1.0f / AtlasRows;

	// Tiny inset to prevent sampling neighbouring tiles at boundaries due to
	// floating-point imprecision. At 48 pixels wide, 0.001 UV = 0.048 pixels —
	// well within a single texel.
	private const float UvEpsilon = 0.001f;

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

	private static StandardMaterial3D? _opaqueMaterial;
	private static StandardMaterial3D? _transparentMaterial;

	/// <summary>
	/// Initialises the mesher with the atlas texture. Must be called once before
	/// the first call to <see cref="BuildMesh"/>. Creates the opaque and transparent
	/// materials used for all chunk surfaces.
	/// </summary>
	/// <param name="atlasTexture">The texture atlas containing all voxel face tiles.</param>
	public static void Initialize(Texture2D atlasTexture)
	{
		ArgumentNullException.ThrowIfNull(atlasTexture);

		_opaqueMaterial = new StandardMaterial3D
		{
			AlbedoTexture = atlasTexture,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Roughness = 1.0f,
			SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
		};

		_transparentMaterial = new StandardMaterial3D
		{
			AlbedoTexture = atlasTexture,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Roughness = 1.0f,
			SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
	}

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
		if (_opaqueMaterial is null)
			throw new InvalidOperationException(
				"ChunkMesher.Initialize() must be called before BuildMesh().");

		var opaqueSt = new SurfaceTool();
		var transparentSt = new SurfaceTool();
		opaqueSt.Begin(Mesh.PrimitiveType.Triangles);
		transparentSt.Begin(Mesh.PrimitiveType.Triangles);

		bool hasOpaque = false;
		bool hasTransparent = false;

		int opaqueVertCount = 0;
		int transparentVertCount = 0;

		// Pre-allocate for a reasonable number of collision triangles.
		// Each visible solid face produces 2 triangles × 3 vertices = 6 entries.
		var collisionVerts = new List<Vector3>(4096);

		ushort[] voxels = chunk.Voxels;
		Vector3I worldPos = chunk.WorldPosition;

		// Iterate in Y-major order matching the chunk's memory layout
		// (Y outermost, Z middle, X innermost = contiguous memory access).
		for (int y = 0; y < Chunk.SizeY; y++)
		{
			for (int z = 0; z < Chunk.SizeZ; z++)
			{
				for (int x = 0; x < Chunk.SizeX; x++)
				{
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
							GD.PushWarning(
								$"ChunkMesher: Custom mesh mode not yet implemented " +
								$"(runtime ID {id} at ({x},{y},{z})).");
							continue;

						case VoxelMeshMode.Cube:
							EmitCubeFaces(
								x, y, z, id,
								chunk, worldPos, neighbourLookup,
								opaqueSt, transparentSt,
								collisionVerts,
								ref hasOpaque, ref hasTransparent,
								ref opaqueVertCount, ref transparentVertCount);
							break;
					}
				}
			}
		}

		if (!hasOpaque && !hasTransparent)
			return null;

		// ── Commit surfaces to ArrayMesh ────────────────────────────────────

		var arrayMesh = new ArrayMesh();

		if (hasOpaque)
		{
			opaqueSt.SetMaterial(_opaqueMaterial);
			opaqueSt.Commit(arrayMesh);
		}

		if (hasTransparent)
		{
			transparentSt.SetMaterial(_transparentMaterial);
			transparentSt.Commit(arrayMesh);
		}

		// ── Build collision shape from solid faces ──────────────────────────

		Vector3[]? collisionFaces = null;
		if (collisionVerts.Count > 0)
		{
			collisionFaces = collisionVerts.ToArray();
		}

		return new MeshResult(arrayMesh, collisionFaces);
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
		SurfaceTool opaqueSt, SurfaceTool transparentSt,
		List<Vector3> collisionVerts,
		ref bool hasOpaque, ref bool hasTransparent,
		ref int opaqueVertCount, ref int transparentVertCount)
	{
		bool isTransparent = VoxelRegistry.TransparentTable[id];
		SurfaceTool st = isTransparent ? transparentSt : opaqueSt;
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

			// Compute the UV rectangle for this tile
			float u0 = (texIndex % AtlasCols) * UvTileWidth + UvEpsilon;
			float v0 = (texIndex / AtlasCols) * UvTileHeight + UvEpsilon;
			float uSpan = UvTileWidth - 2.0f * UvEpsilon;
			float vSpan = UvTileHeight - 2.0f * UvEpsilon;

			Vector3 normal = FaceNormals[face];
			Vector3[] verts = FaceVertices[face];

			// Compute the 4 vertex positions and UV coordinates
			Vector3 p0 = origin + verts[0];
			Vector3 p1 = origin + verts[1];
			Vector3 p2 = origin + verts[2];
			Vector3 p3 = origin + verts[3];

			Vector2 uv0 = new(u0 + QuadUvs[0].X * uSpan, v0 + QuadUvs[0].Y * vSpan);
			Vector2 uv1 = new(u0 + QuadUvs[1].X * uSpan, v0 + QuadUvs[1].Y * vSpan);
			Vector2 uv2 = new(u0 + QuadUvs[2].X * uSpan, v0 + QuadUvs[2].Y * vSpan);
			Vector2 uv3 = new(u0 + QuadUvs[3].X * uSpan, v0 + QuadUvs[3].Y * vSpan);

			int baseIndex = isTransparent ? transparentVertCount : opaqueVertCount;

			st.SetNormal(normal);
			st.SetUV(uv0);
			st.AddVertex(p0);

			st.SetNormal(normal);
			st.SetUV(uv1);
			st.AddVertex(p1);

			st.SetNormal(normal);
			st.SetUV(uv2);
			st.AddVertex(p2);

			st.SetNormal(normal);
			st.SetUV(uv3);
			st.AddVertex(p3);

			// Triangle 1: v0 → v2 → v1 (Clockwise winding)
			st.AddIndex(baseIndex + 0);
			st.AddIndex(baseIndex + 2);
			st.AddIndex(baseIndex + 1);

			// Triangle 2: v0 → v3 → v2 (Clockwise winding)
			st.AddIndex(baseIndex + 0);
			st.AddIndex(baseIndex + 3);
			st.AddIndex(baseIndex + 2);

			if (isTransparent)
			{
				hasTransparent = true;
				transparentVertCount += 4;
			}
			else
			{
				hasOpaque = true;
				opaqueVertCount += 4;
			}

			// Collision geometry (solid voxels only)
			// Wait, the collision generation relies on `OccludesTable` instead of `IsSolid`. 
			// In TUES Phase 0, all standard blocks are solid anyway, so checking OccludesTable is safe.
			if (VoxelRegistry.OccludesTable[id])
			{
				collisionVerts.Add(p0);
				collisionVerts.Add(p2);
				collisionVerts.Add(p1);

				collisionVerts.Add(p0);
				collisionVerts.Add(p3);
				collisionVerts.Add(p2);
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
		if (Chunk.IsInBounds(nx, ny, nz))
			return chunk.GetVoxel(nx, ny, nz);

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

	// ── Atlas image generation ──────────────────────────────────────────────
	//
	// Generates a minimal placeholder atlas for the 5 starter voxel types.
	// Solid-coloured tiles with a two-tone tile for grass sides. This will be
	// replaced with real pixel art in a later phase.

	/// <summary>
	/// Creates a 48×32 pixel placeholder texture atlas image for the starter
	/// voxel types. Call this during test setup; the returned Image can be
	/// converted to an <see cref="ImageTexture"/> and passed to
	/// <see cref="Initialize"/>.
	/// </summary>
	public static Image CreateAtlasImage()
	{
		int width = AtlasCols * AtlasTilePixels;   // 48
		int height = AtlasRows * AtlasTilePixels;  // 32
		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

		Color grassGreen = new(0.298f, 0.686f, 0.314f);   // #4CAF50
		Color dirtBrown  = new(0.545f, 0.412f, 0.078f);   // #8B6914
		Color stoneGrey  = new(0.620f, 0.620f, 0.620f);   // #9E9E9E
		Color bedrockGrey = new(0.380f, 0.380f, 0.380f);  // #616161

		FillTile(image, 0, 0, grassGreen);                      // Index 0: grass top
		FillTileHalf(image, 1, 0, grassGreen, dirtBrown);       // Index 1: grass side
		FillTile(image, 2, 0, dirtBrown);                       // Index 2: dirt
		FillTile(image, 0, 1, stoneGrey);                       // Index 3: stone
		FillTile(image, 1, 1, bedrockGrey);                     // Index 4: bedrock
		FillTile(image, 2, 1, Colors.Black);                    // Index 5: unused pad

		return image;
	}

	private static void FillTile(Image image, int col, int row, Color color)
	{
		int x0 = col * AtlasTilePixels;
		int y0 = row * AtlasTilePixels;
		for (int py = 0; py < AtlasTilePixels; py++)
			for (int px = 0; px < AtlasTilePixels; px++)
				image.SetPixel(x0 + px, y0 + py, color);
	}

	private static void FillTileHalf(
		Image image, int col, int row, Color topColor, Color bottomColor)
	{
		int x0 = col * AtlasTilePixels;
		int y0 = row * AtlasTilePixels;
		int half = AtlasTilePixels / 2;

		for (int py = 0; py < half; py++)
			for (int px = 0; px < AtlasTilePixels; px++)
				image.SetPixel(x0 + px, y0 + py, topColor);

		for (int py = half; py < AtlasTilePixels; py++)
			for (int px = 0; px < AtlasTilePixels; px++)
				image.SetPixel(x0 + px, y0 + py, bottomColor);
	}
}
