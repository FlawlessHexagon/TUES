using System;

namespace TheUniversalEntertainmentSystem.API;

/// <summary>
/// Identifies a class as a dimension engine.
/// The loader uses this attribute to find the entry point for the dimension generator
/// in a loaded .tuesengine package.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DimensionEngineAttribute : Attribute
{
	public string Id { get; }

	public DimensionEngineAttribute(string id)
	{
		Id = id;
	}
}
