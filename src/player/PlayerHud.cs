using Godot;
using System;

namespace TheUniversalEntertainmentSystem;

/// <summary>
/// The programmatic Zero-Scene HUD for the local player.
/// Contains crosshair, debug overlay, and a voxel selection hotbar.
/// Implements GC-friendly deferred string formatting for debug metrics.
/// </summary>
public partial class PlayerHud : CanvasLayer
{
    public ushort SelectedVoxelId { get; private set; }

    private Player _player = null!;
    private ChunkManager _chunkManager = null!;

    private Label _debugLabel = null!;
    private ColorRect[] _slots = new ColorRect[9];
    private ushort[] _slotVoxelIds = new ushort[9];

    private double _debugAccumulator = 0.0;
    private const double DebugUpdateRate = 0.25; // 4 updates per second

    public void Init(Player player, ChunkManager chunkManager)
    {
        _player = player;
        _chunkManager = chunkManager;
    }

    public override void _Ready()
    {
        // 1. Crosshair
        var crosshair = new ColorRect
        {
            CustomMinimumSize = new Vector2(4, 4),
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        // Fine-tune centering mathematically
        crosshair.Position -= new Vector2(2, 2);
        AddChild(crosshair);

        // 2. Debug Label
        _debugLabel = new Label
        {
            Text = "Loading...",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                OutlineSize = 4,
                OutlineColor = Colors.Black,
                FontColor = Colors.White
            }
        };
        _debugLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _debugLabel.Position = new Vector2(10, 10);
        AddChild(_debugLabel);

        // 3. Hotbar Container
        var hotbarMargin = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hotbarMargin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hotbarMargin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(hotbarMargin);

        var hotbar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hotbarMargin.AddChild(hotbar);

        // Populate slots
        for (int i = 0; i < 9; i++)
        {
            _slots[i] = new ColorRect
            {
                CustomMinimumSize = new Vector2(48, 48),
                Color = Colors.DarkGray,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            hotbar.AddChild(_slots[i]);
        }

        // Initialize Slot ID Mappings
        _slotVoxelIds[0] = VoxelRegistry.GetRuntimeId("tues:grass");
        _slotVoxelIds[1] = VoxelRegistry.GetRuntimeId("tues:dirt");
        _slotVoxelIds[2] = VoxelRegistry.GetRuntimeId("tues:stone");
        _slotVoxelIds[3] = VoxelRegistry.GetRuntimeId("tues:bedrock");
        // Slots 4-8 default to 0 (Air)

        SelectSlot(0); // Default to first slot
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        for (int i = 0; i < 9; i++)
        {
            if (Input.IsActionJustPressed($"hotbar_{i + 1}"))
            {
                SelectSlot(i);
                break;
            }
        }
    }

    private void SelectSlot(int index)
    {
        for (int i = 0; i < 9; i++)
        {
            if (i == index)
            {
                _slots[i].Color = Colors.White; // Highlight selected
                _slots[i].CustomMinimumSize = new Vector2(56, 56); // Pop out
            }
            else
            {
                // Basic color coding for starter blocks
                if (_slotVoxelIds[i] == VoxelRegistry.GetRuntimeId("tues:grass")) _slots[i].Color = Colors.WebGreen;
                else if (_slotVoxelIds[i] == VoxelRegistry.GetRuntimeId("tues:dirt")) _slots[i].Color = Colors.SaddleBrown;
                else if (_slotVoxelIds[i] == VoxelRegistry.GetRuntimeId("tues:stone")) _slots[i].Color = Colors.Gray;
                else if (_slotVoxelIds[i] == VoxelRegistry.GetRuntimeId("tues:bedrock")) _slots[i].Color = Colors.DarkSlateGray;
                else _slots[i].Color = new Color(0, 0, 0, 0.5f); // Empty slot

                _slots[i].CustomMinimumSize = new Vector2(48, 48); // Normal size
            }
        }
        
        SelectedVoxelId = _slotVoxelIds[index];
    }

    public override void _Process(double delta)
    {
        if (_player == null || _chunkManager == null) return;

        _debugAccumulator += delta;
        if (_debugAccumulator >= DebugUpdateRate) // Strictly 4 times per second to prevent GC stutter
        {
            _debugAccumulator = 0;

            int fps = (int)Engine.GetFramesPerSecond();
            Vector3 pos = _player.Position;
            Vector3I chunkPos = new Vector3I(
                Mathf.FloorToInt(pos.X / Chunk.SizeX),
                Mathf.FloorToInt(pos.Y / Chunk.SizeY),
                Mathf.FloorToInt(pos.Z / Chunk.SizeZ)
            );
            int activeChunks = _chunkManager.ActiveChunkCount;

            _debugLabel.Text = $"FPS: {fps}\nPos: {pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}\nChunk: {chunkPos.X}, {chunkPos.Y}, {chunkPos.Z}\nActive Chunks: {activeChunks}";
        }
    }
}
