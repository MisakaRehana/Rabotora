namespace Rabotora.Core.Utility.Platform;

/// <summary>
/// Resolves the platform-specific implementations the core engine runs on.
/// </summary>
/// <remarks>
/// <para>
/// The core library references no platform assembly on purpose, so implementations are looked up by
/// <em>assembly-qualified type name</em> at runtime through the names below. Those names therefore have
/// to keep matching their declarations in <c>Rabotora.Interop.Win32</c>, whose files deliberately use
/// the <c>Rabotora.Core.*</c> namespaces even though they ship in another assembly: from the core's
/// point of view the implementations are plain types in its own namespace tree.
/// </para>
/// <para>
/// The trade-off is that the compiler cannot see these names, so every lookup site also carries a
/// <c>DynamicDependencyAttribute</c> naming the same type. Without it a trimmer is free to drop an
/// implementation that nothing appears to reference.
/// </para>
/// <para>
/// The platform assembly is loaded because the entry project references it; nothing here creates a
/// compile-time reference to it.
/// </para>
/// </remarks>
internal static class PlatformImplementation
{
	public const string Win32AssemblyName = "Rabotora.Interop.Win32";
	public const string Win32WindowTypeName = "Rabotora.Core.Services.Platform.Win32Window";
	public const string Win32ClockTypeName = "Rabotora.Core.Services.Timing.RWin32Clock";
	public const string Win32ConsoleLoggerTypeName = "Rabotora.Core.Services.Logging.Win32ConsoleLogger";

	private const string UnsupportedPlatformMessage =
		"This platform is not yet supported because there is no available implementation.\n" +
		"Check your entry project (usually Rabotora.Player.Desktop on desktop platforms) referenced the platform-specific interop package and try again.";

	/// <summary>
	/// Resolves an implementation that the caller cannot run without, throwing when this platform has
	/// none. Use <see cref="TryResolveWin32"/> for an optional implementation.
	/// </summary>
	/// <param name="typeName">Unqualified name of the implementation type, one of the constants above.</param>
	/// <exception cref="PlatformNotSupportedException">No implementation is available on this platform.</exception>
	public static Type ResolveRequiredWin32(string typeName)
	{
		return TryResolveWin32(typeName)
		       ?? throw new PlatformNotSupportedException($"{UnsupportedPlatformMessage}\nNo implementation was found for the type {typeName}, {Win32AssemblyName}.");
	}

	/// <summary>
	/// Resolves an optional implementation, returning <see langword="null"/> when this platform has none.
	/// </summary>
	/// <param name="typeName">Unqualified name of the implementation type, one of the constants above.</param>
	public static Type? TryResolveWin32(string typeName)
	{
		if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
		{
			return null;
		}

		// throwOnError: false so that a missing implementation reports as "this platform has none"
		// instead of surfacing a TypeLoadException from deep inside the loader.
		return Type.GetType($"{typeName}, {Win32AssemblyName}", throwOnError: false);
	}

	/// <summary>
	/// Creates the resolved implementation, passing <paramref name="arguments"/> to its constructor.
	/// </summary>
	public static T CreateInstance<T>(Type implementationType, params object?[]? arguments) where T : class
	{
		return (T) Activator.CreateInstance(implementationType, arguments)!;
	}

	/// <summary>
	/// Creates the resolved implementation for an abstraction that cannot be named as a type argument,
	/// which is any interface declaring a <c>static abstract</c> member: <see cref="Abstractions.Logging.ILogger"/>
	/// declares <c>IsAvailable</c>, so only a cast can bring the instance back to the abstraction.
	/// </summary>
	public static object CreateInstance(Type implementationType, params object?[]? arguments)
	{
		return Activator.CreateInstance(implementationType, arguments)!;
	}
}
