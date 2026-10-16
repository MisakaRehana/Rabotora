namespace Rabotora.Core.Enumerators.Logging;

/// <summary>
/// Severity of one log entry. The names are also what the formatter prints as the category, so keep
/// them in the order a backend would filter them.
/// </summary>
public enum LogLevel
{
	Debug = 0,
	Info = 1,
	Warning = 2,
	Error = 3,
	Fatal = 4
}
