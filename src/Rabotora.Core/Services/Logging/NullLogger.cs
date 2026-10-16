using Rabotora.Core.Abstractions.Logging;

namespace Rabotora.Core.Services.Logging;

/// <summary>
/// A logger that discards everything.
/// </summary>
public sealed class NullLogger : ILogger<NullLogger>
{
	public static bool IsAvailable => true;

	public void LogDebug(string message) { }

	public void LogError(string message) { }

	public void LogFatal(string message) { }

	public void LogWarning(string message) { }

	public void Log(string message) { }

	public void LogDebug(string message, params object[] args) { }

	public void LogError(string message, params object[] args) { }

	public void LogFatal(string message, params object[] args) { }

	public void LogWarning(string message, params object[] args) { }

	public void Log(string message, params object[] args) { }

	public void LogError(string message, Exception exception) { }

	public void LogFatal(string message, Exception exception) { }

	public void Dispose() { }
}
