using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

public readonly ref struct ResizedEventArgs : IValueEventArgs<ResizedEventArgs>
{
	public int NewWidth { get; init; }
	public int NewHeight { get; init; }
}
