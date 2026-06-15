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
		RegisterAction("move_down", Key.Ctrl);

		RegisterMouseAction("interact_break", MouseButton.Left);
		RegisterMouseAction("interact_place", MouseButton.Right);

		RegisterAction("hotbar_1", Key.Key1);
		RegisterAction("hotbar_2", Key.Key2);
		RegisterAction("hotbar_3", Key.Key3);
		RegisterAction("hotbar_4", Key.Key4);
		RegisterAction("hotbar_5", Key.Key5);
		RegisterAction("hotbar_6", Key.Key6);
		RegisterAction("hotbar_7", Key.Key7);
		RegisterAction("hotbar_8", Key.Key8);
		RegisterAction("hotbar_9", Key.Key9);
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
