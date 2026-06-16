using System.Runtime.Loader;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// A custom AssemblyLoadContext to isolate dimension generator DLLs.
/// </summary>
public class TuesEngineContext : AssemblyLoadContext
{
	public TuesEngineContext(string name) : base(name, isCollectible: true)
	{
	}

	protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName assemblyName)
	{
		// In Godot, assemblies like GodotSharp or the core game DLL are loaded into a custom Godot ALC.
		// If the dimension generator requires them, we must provide the already-loaded host assemblies.
		foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.GetName().Name == assemblyName.Name)
			{
				return asm;
			}
		}
		return null;
	}
}
