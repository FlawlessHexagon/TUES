using System;
using Godot;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// Verification script for Step 0.0 — Voxel Definition.
/// Runs all verification cases in <see cref="_Ready"/> and prints results to the
/// Godot output console. Attach to a Node in a test scene to execute on launch.
/// </summary>
public partial class VoxelDefinitionTest : Node
{
	private int _passed;
	private int _failed;

	public override void _Ready()
	{
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("  Step 0.0 — Voxel Definition Verification");
		GD.Print("═══════════════════════════════════════════════════");
		GD.Print("");

		// Clean slate — ensure no prior state leaks into the tests.
		VoxelRegistry.Reset();

		Test1_RegisterMultipleTypesWithoutCollision();
		Test2_LookupByNamespacedId();
		Test3_LookupByRuntimeId();
		Test4_AirIsRuntimeIdZero();
		Test5_DuplicateRegistrationProducesError();
		Test6_PostFinalizationRegistrationProducesError();

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
	// Register 5 core types. No exceptions. Count is correct.
	private void Test1_RegisterMultipleTypesWithoutCollision()
	{
		VoxelRegistry.Reset();

		try
		{
			VoxelRegistration.RegisterCoreTypes();
			Assert(VoxelRegistry.Count == 5,
				"Register 5 types without collision",
				$"Expected 5 registered types, got {VoxelRegistry.Count}");
		}
		catch (Exception e)
		{
			Fail("Register 5 types without collision",
				$"Unexpected exception: {e.Message}");
		}
	}

	// ── Test 2 ─────────────────────────────────────────────────────────────
	// Look up tues:stone by namespaced ID. All fields must match.
	private void Test2_LookupByNamespacedId()
	{
		VoxelType? stone = VoxelRegistry.GetType("tues:stone");

		if (stone is null)
		{
			Fail("Lookup tues:stone by namespaced ID", "Returned null");
			return;
		}

		bool fieldsCorrect =
			stone.NamespacedId == "tues:stone" &&
			stone.DisplayName == "Stone" &&
			stone.IsSolid &&
			!stone.IsTransparent &&
			stone.MeshMode == VoxelMeshMode.Cube &&
			stone.CustomMeshPath is null;

		Assert(fieldsCorrect,
			"Lookup tues:stone by namespaced ID — all fields correct",
			$"Field mismatch: Id={stone.NamespacedId}, Name={stone.DisplayName}, " +
			$"Solid={stone.IsSolid}, Transparent={stone.IsTransparent}, " +
			$"Mode={stone.MeshMode}, MeshPath={stone.CustomMeshPath ?? "null"}");
	}

	// ── Test 3 ─────────────────────────────────────────────────────────────
	// Look up tues:stone by runtime ID. Must return the same reference.
	private void Test3_LookupByRuntimeId()
	{
		ushort stoneId = VoxelRegistry.GetRuntimeId("tues:stone");
		VoxelType? stoneByName = VoxelRegistry.GetType("tues:stone");
		VoxelType? stoneById = VoxelRegistry.GetType(stoneId);

		Assert(stoneByName is not null && ReferenceEquals(stoneByName, stoneById),
			"Lookup by runtime ID returns same VoxelType reference",
			"Name lookup and ID lookup returned different instances (or null)");
	}

	// ── Test 4 ─────────────────────────────────────────────────────────────
	// Runtime ID 0 must always resolve to tues:air.
	private void Test4_AirIsRuntimeIdZero()
	{
		VoxelType? air = VoxelRegistry.GetType(VoxelRegistry.AirId);

		Assert(air is not null && air.NamespacedId == "tues:air",
			"Runtime ID 0 resolves to tues:air",
			air is null
				? "ID 0 returned null"
				: $"ID 0 returned '{air.NamespacedId}'");
	}

	// ── Test 5 ─────────────────────────────────────────────────────────────
	// Registering a duplicate namespaced ID must throw ArgumentException.
	private void Test5_DuplicateRegistrationProducesError()
	{
		try
		{
			VoxelRegistry.Register(new VoxelType(
				namespacedId: "tues:stone",
				displayName:  "Duplicate Stone",
				isSolid:      true,
				isTransparent: false,
				meshMode:     VoxelMeshMode.Cube));

			Fail("Duplicate registration produces error",
				"No exception was thrown");
		}
		catch (ArgumentException)
		{
			Pass("Duplicate registration produces clear error (ArgumentException)");
		}
		catch (Exception e)
		{
			Fail("Duplicate registration produces error",
				$"Wrong exception type: {e.GetType().Name}: {e.Message}");
		}
	}

	// ── Test 6 ─────────────────────────────────────────────────────────────
	// After finalization, any registration attempt must throw.
	private void Test6_PostFinalizationRegistrationProducesError()
	{
		VoxelRegistry.Finalize();

		try
		{
			VoxelRegistry.Register(new VoxelType(
				namespacedId: "tues:new_type",
				displayName:  "New Type",
				isSolid:      true,
				isTransparent: false,
				meshMode:     VoxelMeshMode.Cube));

			Fail("Post-finalization registration produces error",
				"No exception was thrown");
		}
		catch (InvalidOperationException)
		{
			Pass("Post-finalization registration produces clear error (InvalidOperationException)");
		}
		catch (Exception e)
		{
			Fail("Post-finalization registration produces error",
				$"Wrong exception type: {e.GetType().Name}: {e.Message}");
		}
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
