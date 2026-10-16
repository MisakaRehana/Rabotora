using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Rabotora.Core.Abstractions.Logging;

public interface ILogger : IDisposable
{
	static abstract bool IsAvailable { get; }
	
	public void LogDebug(string message);
	public void LogError(string message);
	public void LogFatal(string message);
	public void LogWarning(string message);
	public void Log(string message);
	public void LogDebug([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args);
	public void LogError([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args);
	public void LogFatal([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args);
	public void LogWarning([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args);
	public void Log([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args);
	
	public void LogError(string message, Exception exception);
	public void LogFatal(string message, Exception exception);
}

public interface ILogger<out T> : ILogger where T : ILogger<T>;
