using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

public readonly ref struct MouseWheelEventArgs : IValueEventArgs<MouseWheelEventArgs>
{
	/// <summary>
	/// Wheel movement in notches, positive away from the user. Never zero: a message that only adds up
	/// to part of a notch is carried into the next one instead of being reported as no movement.
	/// </summary>
	public int Value { get; init; }
}
