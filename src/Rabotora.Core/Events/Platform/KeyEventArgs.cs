using Rabotora.Core.Data.Platform;
using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

public readonly ref struct KeyEventArgs : IValueEventArgs<KeyEventArgs>
{
	public KeyEvent Key { get; init; }
}
