using System.Runtime.Loader;

namespace TheUniversalEntertainmentSystem;
using TheUniversalEntertainmentSystem.API;

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
		if (assemblyName.Name == "TUES.API")
		{
			return typeof(IDimensionGenerator).Assembly;
		}

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
