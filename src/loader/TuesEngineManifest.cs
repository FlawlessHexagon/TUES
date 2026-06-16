using System;
using System.Text.Json.Serialization;

namespace TheUniversalEntertainmentSystem;

public class TuesEngineManifest
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("entryDll")]
	public string EntryDll { get; set; } = string.Empty;

	[JsonPropertyName("dependencies")]
	public string[] Dependencies { get; set; } = Array.Empty<string>();
}
