using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Rabotora.Core.Enumerators.Logging;

namespace Rabotora.Core.Services.Logging;

/// <summary>
/// Renders one log entry as a single line.
/// </summary>
public static class LogFormatter
{
	/// <summary>
	/// How many distinct composite format strings are parsed and kept. Log format strings are normally
	/// literals, so this only bounds the damage when a caller passes dynamically built ones.
	/// </summary>
	private const int MaxCachedFormats = 256;

	/// <summary>
	/// Parsed composite format strings, keyed by the raw message. A <see langword="null"/> value records
	/// a message that cannot be parsed at all, so the failure is not rediscovered on every entry.
	/// </summary>
	private static readonly ConcurrentDictionary<string, CompositeFormat?> Formats = new();

	/// <summary>
	/// Prefixes one line: [wall clock] [elapsed since the clock epoch] [level] [message].
	/// </summary>
	/// <param name="level">Severity, also printed as the category.</param>
	/// <param name="elapsed">Time since the clock's epoch, printed to the millisecond; it wraps after a day of uptime, unlike the wall clock next to it.</param>
	/// <param name="message">Message text. This overload does not treat it as a format string.</param>
	public static string Format(LogLevel level, TimeSpan elapsed, string message)
	{
		return Build(level, elapsed, message, []);
	}

	/// <summary>
	/// Prefixes one line: [wall clock] [elapsed since the clock epoch] [level] [message].
	/// </summary>
	/// <param name="level">Severity, also printed as the category.</param>
	/// <param name="elapsed">Time since the clock's epoch, printed to the millisecond; it wraps after a day of uptime, unlike the wall clock next to it.</param>
	/// <param name="message">Composite format string.</param>
	/// <param name="args">Values for the placeholders in <paramref name="message"/>.</param>
	public static string Format(LogLevel level, TimeSpan elapsed, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string message, params object[] args)
	{
		return Build(level, elapsed, message, args);
	}

	private static string Build(LogLevel level, TimeSpan elapsed, string message, object[] args)
	{
		var now = DateTimeOffset.Now;
		var sb = new StringBuilder();
		// format: [2026.10.15 22:43:12.153] [00:00:00.000] [Info] [message]
		sb.Append('[').Append(now.ToString("yyyy.MM.dd HH:mm:ss.fff")).Append("] ");
		sb.Append('[').Append(elapsed.ToString(@"hh\:mm\:ss\.fff")).Append("] ");
		sb.Append('[').Append(DisplayName(level)).Append("] ");
		sb.Append(args.Length == 0 ? message : FormatMessage(message, args));
		return sb.ToString();
	}

	/// <summary>
	/// Applies the composite format string while treating a log message as data: a message that happens
	/// to contain braces must not become an exception on its way to the console, so a format that cannot
	/// be parsed or applied falls back to the raw text followed by the arguments.
	/// </summary>
	/// <remarks>
	/// The fallback costs formatting on the messages that trigger it: with a message like
	/// <c>"json: {...} is {0}"</c> the unescaped braces make the whole string unparseable, so the real
	/// placeholder is printed verbatim next to the arguments as well. A caller that needs both has to
	/// write the literal braces doubled, which is what a composite format string asks for anyway.
	/// </remarks>
	private static string FormatMessage(string message, object[] args)
	{
		if (!Formats.TryGetValue(message, out CompositeFormat? format))
		{
			format = ParseFormat(message);
			if (Formats.Count < MaxCachedFormats)
			{
				Formats.TryAdd(message, format);
			}
		}

		if (format is null)
		{
			return Raw(message, args);
		}

		try
		{
			return string.Format(CultureInfo.CurrentCulture, format, args);
		}
		catch (FormatException)
		{
			// A placeholder with no argument behind it, for instance.
			return Raw(message, args);
		}
	}

	private static CompositeFormat? ParseFormat(string message)
	{
		try
		{
			return CompositeFormat.Parse(message);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static string Raw(string message, object[] args)
	{
		var sb = new StringBuilder(message);
		foreach (object arg in args)
		{
			sb.Append(' ').Append(arg);
		}
		return sb.ToString();
	}

	private static string DisplayName(LogLevel level) => level switch
	{
		LogLevel.Debug => "Debug",
		LogLevel.Info => "Info",
		LogLevel.Warning => "Warning",
		LogLevel.Error => "Error",
		LogLevel.Fatal => "Fatal",
		_ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown log level.")
	};
}
