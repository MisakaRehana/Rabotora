using System.Diagnostics.CodeAnalysis;
using Rabotora.Core.Utility.Platform;

namespace Rabotora.Core.Abstractions.Timing;

public interface IRClock : IDisposable
{
	long GetTicks();
	long TicksPerSecond { get; }
	double ElapsedSeconds => GetTicks() / (double)TicksPerSecond;

	/// <summary>
	/// Waits at least <paramref name="seconds"/> before returning.
	/// </summary>
	/// <param name="seconds">
	/// How long to wait. Must be finite; values at or below zero yield the rest of the current
	/// scheduler slice instead of waiting.
	/// </param>
	void WaitSeconds(double seconds) => Thread.Sleep(TimeSpan.FromSeconds(seconds));

	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, PlatformImplementation.Win32ClockTypeName, PlatformImplementation.Win32AssemblyName)]
	public static IRClock PlatformCreate()
	{
		// The name has to keep matching RWin32Clock's declaration in Rabotora.Interop.Win32; see
		// PlatformImplementation for why the lookup is a string in the first place.
		Type type = PlatformImplementation.ResolveRequiredWin32(PlatformImplementation.Win32ClockTypeName);
		return PlatformImplementation.CreateInstance<IRClock>(type);
	}
}
