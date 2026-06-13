using System;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Verification script for Step 0.1 — Chunk Definition.
/// Exercises all acceptance criteria and prints results to the Godot output console.
/// </summary>
public partial class ChunkDefinitionTest : Node
{
	private int _passed;
	private int _failed;

	public override void _Ready()
	{
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("  Step 0.1 — Chunk Definition Verification");
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("");

		// Ensure registry has core types for integration checks.
		VoxelRegistry.Reset();
		VoxelRegistration.RegisterCoreTypes();

		Test1_FreshChunkIsAllAir();
		Test2_SetGetRoundTrip();
		Test3_GetVoxelNegativeOob();
		Test4_GetVoxelPositiveOob();
		Test5_SetVoxelSetsDirty();
		Test6_FreshChunkDefaultState();
		Test7_IndexFormulaConsistency();
		Test8_MemoryFootprint();
		Test9_WorldPositionConversion();

		GD.Print("");
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print($"  Results: {_passed} passed, {_failed} failed");
		GD.Print("═══════════════════════════════════════════════════");

		if (_failed > 0)
			GD.PrintErr($"  ⚠ {_failed} verification(s) FAILED.");
		else
			GD.Print("  All verifications passed.");
	}

	// ── Test 1 ─────────────────────────────────────────────────────────────
	// Fresh chunk has all voxels set to 0 (Air).
	private void Test1_FreshChunkIsAllAir()
	{
		var chunk = new Chunk(Vector3I.Zero);
		bool allAir = true;

		for (int i = 0; i < Chunk.Volume; i++)
		{
			if (chunk.Voxels[i] != 0)
			{
				allAir = false;
				break;
			}
		}

		Assert(allAir,
			"Fresh chunk is all Air (all zeros)",
			"Found non-zero voxel in freshly created chunk");
	}

	// ── Test 2 ─────────────────────────────────────────────────────────────
	// SetVoxel then GetVoxel returns the same value.
	private void Test2_SetGetRoundTrip()
	{
		var chunk = new Chunk(Vector3I.Zero);
		ushort stoneId = VoxelRegistry.GetRuntimeId("tues:stone");

		chunk.SetVoxel(5, 10, 3, stoneId);
		ushort result = chunk.GetVoxel(5, 10, 3);

		Assert(result == stoneId,
			$"SetVoxel/GetVoxel round trip (stone ID = {stoneId})",
			$"Expected {stoneId}, got {result}");
	}

	// ── Test 3 ─────────────────────────────────────────────────────────────
	// GetVoxel with negative coordinate returns Air without throwing.
	private void Test3_GetVoxelNegativeOob()
	{
		var chunk = new Chunk(Vector3I.Zero);

		try
		{
			ushort result = chunk.GetVoxel(-1, 0, 0);
			Assert(result == 0,
				"GetVoxel(-1, 0, 0) returns Air without throwing",
				$"Expected 0 (Air), got {result}");
		}
		catch (Exception e)
		{
			Fail("GetVoxel(-1, 0, 0) returns Air without throwing",
				$"Threw {e.GetType().Name}: {e.Message}");
		}
	}

	// ── Test 4 ─────────────────────────────────────────────────────────────
	// GetVoxel with coordinate == Size returns Air without throwing.
	private void Test4_GetVoxelPositiveOob()
	{
		var chunk = new Chunk(Vector3I.Zero);

		try
		{
			ushort result = chunk.GetVoxel(16, 0, 0);
			Assert(result == 0,
				"GetVoxel(16, 0, 0) returns Air without throwing",
				$"Expected 0 (Air), got {result}");
		}
		catch (Exception e)
		{
			Fail("GetVoxel(16, 0, 0) returns Air without throwing",
				$"Threw {e.GetType().Name}: {e.Message}");
		}
	}

	// ── Test 5 ─────────────────────────────────────────────────────────────
	// SetVoxel on a valid coordinate sets the dirty flag.
	private void Test5_SetVoxelSetsDirty()
	{
		var chunk = new Chunk(Vector3I.Zero);

		Assert(!chunk.IsDirty,
			"Fresh chunk IsDirty is false",
			$"Expected false, got {chunk.IsDirty}");

		chunk.SetVoxel(0, 0, 0, 1);

		Assert(chunk.IsDirty,
			"SetVoxel sets IsDirty to true",
			$"Expected true, got {chunk.IsDirty}");
	}

	// ── Test 6 ─────────────────────────────────────────────────────────────
	// Fresh chunk has State == Unloaded and IsDirty == false.
	private void Test6_FreshChunkDefaultState()
	{
		var chunk = new Chunk(new Vector3I(3, -1, 7));

		bool stateCorrect = chunk.State == ChunkState.Unloaded;
		bool dirtyCorrect = !chunk.IsDirty;

		Assert(stateCorrect && dirtyCorrect,
			"Fresh chunk: State == Unloaded, IsDirty == false",
			$"State={chunk.State}, IsDirty={chunk.IsDirty}");
	}

	// ── Test 7 ─────────────────────────────────────────────────────────────
	// Index formula consistency: write then read at every corner and several
	// interior positions. Each must return the written value, and no two
	// distinct positions may alias to the same index.
	private void Test7_IndexFormulaConsistency()
	{
		var chunk = new Chunk(Vector3I.Zero);

		// Test positions: all 8 corners + centre + arbitrary interior point
		(int x, int y, int z, ushort val)[] cases =
		{
			(0,  0,  0,  1),
			(15, 0,  0,  2),
			(0,  15, 0,  3),
			(0,  0,  15, 4),
			(15, 15, 0,  5),
			(15, 0,  15, 6),
			(0,  15, 15, 7),
			(15, 15, 15, 8),
			(8,  8,  8,  9),
			(3,  11, 7,  10),
		};

		// Write all values.
		foreach (var (x, y, z, val) in cases)
			chunk.SetVoxel(x, y, z, val);

		// Read back and verify.
		bool allCorrect = true;
		string failDetail = "";

		foreach (var (x, y, z, val) in cases)
		{
			ushort result = chunk.GetVoxel(x, y, z);
			if (result != val)
			{
				allCorrect = false;
				failDetail = $"At ({x},{y},{z}): expected {val}, got {result}";
				break;
			}
		}

		Assert(allCorrect,
			"Index formula consistency — all positions round-trip correctly",
			failDetail);
	}

	// ── Test 8 ─────────────────────────────────────────────────────────────
	// Memory footprint: voxel array is exactly 8,192 bytes.
	private void Test8_MemoryFootprint()
	{
		int expected = sizeof(ushort) * Chunk.Volume; // 2 * 4096 = 8192
		Assert(expected == 8192,
			$"Voxel array memory: sizeof(ushort) × {Chunk.Volume} = {expected} bytes",
			$"Expected 8192, got {expected}");
	}

	// ── Test 9 ─────────────────────────────────────────────────────────────
	// WorldPosition is correctly computed from chunk position × size.
	private void Test9_WorldPositionConversion()
	{
		var chunk = new Chunk(new Vector3I(2, -3, 5));
		var expected = new Vector3I(
			2 * Chunk.SizeX,
			-3 * Chunk.SizeY,
			5 * Chunk.SizeZ);

		Assert(chunk.WorldPosition == expected,
			$"WorldPosition: chunk (2,-3,5) → world ({expected})",
			$"Expected {expected}, got {chunk.WorldPosition}");
	}

	// ── Assertion helpers ──────────────────────────────────────────────────

	private void Assert(bool condition, string testName, string failureDetail)
	{
		if (condition)
			Pass(testName);
		else
			Fail(testName, failureDetail);
	}

	private void Pass(string testName)
	{
		_passed++;
		GD.Print($"  [PASS] {testName}");
	}

	private void Fail(string testName, string detail)
	{
		_failed++;
		GD.PrintErr($"  [FAIL] {testName}: {detail}");
	}
}
