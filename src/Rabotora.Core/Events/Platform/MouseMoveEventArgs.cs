using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

public readonly ref struct MouseMoveEventArgs : IValueEventArgs<MouseMoveEventArgs>
{
	public int X { get; init; }
	public int Y { get; init; }
}
