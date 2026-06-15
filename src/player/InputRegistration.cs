using Godot;

namespace TheUniversalEntertainmentSystem;

public static class InputRegistration
{
	public static void RegisterCoreActions()
	{
		RegisterAction("move_forward", Key.W);
		RegisterAction("move_backward", Key.S);
		RegisterAction("move_left", Key.A);
		RegisterAction("move_right", Key.D);
		RegisterAction("jump", Key.Space);
		RegisterAction("sprint", Key.Shift);

		RegisterMouseAction("interact_break", MouseButton.Left);
		RegisterMouseAction("interact_place", MouseButton.Right);
	}

	private static void RegisterAction(string actionName, Key physicalKeycode)
	{
		if (!InputMap.HasAction(actionName))
		{
			InputMap.AddAction(actionName);
		}
		var keyEvent = new InputEventKey { PhysicalKeycode = physicalKeycode };
		InputMap.ActionAddEvent(actionName, keyEvent);
	}

	private static void RegisterMouseAction(string actionName, MouseButton button)
	{
		if (!InputMap.HasAction(actionName))
		{
			InputMap.AddAction(actionName);
		}
		var mouseEvent = new InputEventMouseButton { ButtonIndex = button };
		InputMap.ActionAddEvent(actionName, mouseEvent);
	}
}
