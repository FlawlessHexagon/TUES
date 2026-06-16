using System.Text.Json.Serialization;

namespace TheUniversalEntertainmentSystem;

public class BlockDefinitionDto
{
	[JsonPropertyName("namespacedId")]
	public string NamespacedId { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("isSolid")]
	public bool IsSolid { get; set; }

	[JsonPropertyName("occludesNeighbours")]
	public bool OccludesNeighbours { get; set; }

	[JsonPropertyName("isTransparent")]
	public bool IsTransparent { get; set; }

	[JsonPropertyName("meshMode")]
	public string MeshMode { get; set; } = "Cube";

	[JsonPropertyName("customMeshPath")]
	public string? CustomMeshPath { get; set; }

	[JsonPropertyName("textureTopIndex")]
	public int TextureTopIndex { get; set; }

	[JsonPropertyName("textureBottomIndex")]
	public int TextureBottomIndex { get; set; }

	[JsonPropertyName("textureSideIndex")]
	public int TextureSideIndex { get; set; }
}
