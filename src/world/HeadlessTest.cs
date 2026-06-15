using Godot;
using System.Threading.Tasks;
using TheUniversalEntertainmentSystem;

public partial class HeadlessTest : SceneTree
{
	public HeadlessTest()
	{
		GD.Print("Starting Headless Test");
		VoxelRegistry.Reset();
		VoxelRegistration.RegisterCoreTypes();
		VoxelRegistry.FreezeRegistry();
		WorldGenerator.Initialize(1337);

		Chunk chunk = new Chunk(new Vector3I(0, 4, 0)); // WorldY = 64
		WorldGenerator.GenerateChunk(chunk);
		
		int solidCount = 0;
		for (int i = 0; i < Chunk.Volume; i++) {
			if (chunk.Voxels[i] != 0) solidCount++;
		}
		GD.Print($"Chunk (0,4,0) solid voxels: {solidCount}");
		
		Quit();
	}
}
