using Rabotora.Core.Utility.Events;

namespace Rabotora.Core.Events.Platform;

/// <summary>
/// One typed character.
/// </summary>
public readonly ref struct CharInputEventArgs : IValueEventArgs<CharInputEventArgs>
{
	/// <summary>
	/// The Unicode scalar value the platform produced, so a character outside the Basic Multilingual
	/// Plane arrives as one value rather than as two surrogates the subscriber has to pair itself.
	/// An unpaired surrogate is reported as U+FFFD.
	/// </summary>
	public int Value { get; init; }
}
