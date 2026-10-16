using Rabotora.Core.Enumerators.Platform;
using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

public readonly ref struct MouseButtonChangedEventArgs : IValueEventArgs<MouseButtonChangedEventArgs>
{
	public MouseButton MouseButton { get; init; }
	public bool IsPressed { get; init; }
}
