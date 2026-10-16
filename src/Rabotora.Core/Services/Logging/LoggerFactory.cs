using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Abstractions.Timing;
using Rabotora.Core.Utility.Platform;

namespace Rabotora.Core.Services.Logging;

public static class LoggerFactory
{
	/// <summary>
	/// Creates a specific logger backend. Throws when the backend reports itself unavailable, because
	/// the caller explicitly asked for that backend and silently substituting another would hide the
	/// mistake. For "whatever this platform has", use <see cref="CreateByPlatformDetect"/>.
	/// </summary>
	public static T Create<T>(IRClock clock) where T : ILogger<T>
	{
		if (!T.IsAvailable) throw new PlatformNotSupportedException("The logger is not available on this platform. Make sure you have the correct package installed.");
		return (T)Activator.CreateInstance(typeof(T), clock)!;
	}

	public static ILogger Create(Type loggerType, IRClock clock)
	{
		if (!loggerType.IsAssignableTo(typeof(ILogger))) throw new ArgumentException("The logger type must implement ILogger.", nameof(loggerType));
		bool isAvailable = loggerType.GetProperty("IsAvailable", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as bool? ?? false;
		if (!isAvailable) throw new PlatformNotSupportedException("The logger is not available on this platform. Make sure you have the correct package installed.");
		return (ILogger)Activator.CreateInstance(loggerType, clock)!;
	}

	/// <summary>
	/// Creates the logger backend the current platform provides, or <see langword="null"/> when this
	/// platform has none. Callers fall back to <see cref="NullLogger"/> in that case: logging is
	/// diagnostic, so its absence must not stop the engine from starting.
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, PlatformImplementation.Win32ConsoleLoggerTypeName, PlatformImplementation.Win32AssemblyName)]
	public static ILogger? CreateByPlatformDetect(IRClock clock)
	{
		// The name has to keep matching Win32ConsoleLogger's declaration in Rabotora.Interop.Win32;
		// see PlatformImplementation for why the lookup is a string in the first place.
		Type? type = PlatformImplementation.TryResolveWin32(PlatformImplementation.Win32ConsoleLoggerTypeName);
		return type == null ? null : (ILogger) PlatformImplementation.CreateInstance(type, clock);
	}
}
