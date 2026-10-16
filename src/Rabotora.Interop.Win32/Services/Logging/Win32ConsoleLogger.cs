using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Abstractions.Timing;
using Rabotora.Core.Enumerators.Logging;
using static TerraFX.Interop.Windows.Windows;

namespace Rabotora.Core.Services.Logging;

/// <summary>
/// Writes log entries to the console, colorized per level when the output really is a console.
/// </summary>
/// <remarks>
/// <para>
/// Entries are formatted by the calling thread, which is what fixes their timestamps and their order,
/// and then handed to a single consumer that owns the console. That split is deliberate: the console
/// has process-wide state (the current color) and only one writer is allowed to touch it, while a
/// caller must never wait for the console to become ready.
/// </para>
/// <para>
/// The queue is unbounded, matching the "logging never blocks the caller" rule above; a console that
/// stops draining therefore costs memory rather than stalling the engine.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class Win32ConsoleLogger : ILogger<Win32ConsoleLogger>
{
	private const uint StdOutputHandle = unchecked((uint)-11);
	private const uint StdErrorHandle = unchecked((uint)-12);

	/// <summary>How long <see cref="Dispose"/> waits for the queued entries to reach the console.</summary>
	private const int DrainTimeoutSeconds = 5;

	/// <summary>One formatted entry, waiting for the consumer.</summary>
	private readonly record struct Entry(LogLevel Level, string Text);

	public static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(6, 1);

	private static readonly LogLevel[] ErrorLevels = [LogLevel.Warning, LogLevel.Error, LogLevel.Fatal];

	private readonly IRClock _clock;
	private readonly long _startTicks;
	private readonly Channel<Entry> _queue = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
	private readonly Task _consumer;
	private readonly Lazy<bool> _isColorAvailable;
	private int _disposed;

	private TimeSpan Elapsed => TimeSpan.FromSeconds((double) (_clock.GetTicks() - _startTicks) / _clock.TicksPerSecond);

	public Win32ConsoleLogger(IRClock clock)
	{
		ArgumentNullException.ThrowIfNull(clock);
		_clock = clock;
		_startTicks = clock.GetTicks();
		_isColorAvailable = new Lazy<bool>(CanColorize);

		// Started before the banner is queued, so the consumer is already reading when the first entry
		// arrives. It only touches fields that are initialized by this point.
		_consumer = Task.Run(ConsumeAsync);

		LogInitialHeader();
	}

	private unsafe static bool CanColorize()
	{
		uint mode;
		return GetConsoleMode(GetStdHandle(StdOutputHandle), &mode) != 0 && // stdout must be a real console
		       GetConsoleMode(GetStdHandle(StdErrorHandle), &mode) != 0 && // stderr must be a real console
		       !Console.IsOutputRedirected &&
		       !Console.IsErrorRedirected;
	}

	private void LogInitialHeader()
	{
		string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
#if DEBUG
		Log($"Rabotora {version} [Debug]");
#else
		Log($"Rabotora {version}");
#endif
		Log($"Framework Version: .NET {Environment.Version:3}");
		Log($"Runtime version: {Assembly.GetAssembly(typeof(LoggerFactory))!.GetName().Version:3}");
#if !DEBUG
		Log($"Built on {Rabotora.Core.BuildInfo.Timestamp}");
#endif
	}
	
	public void LogDebug(string message)
	{
#if DEBUG
		LogInternal(LogLevel.Debug, message);
#endif
	}
	
	public void LogError(string message)
	{
		LogInternal(LogLevel.Error, message);
	}

	public void LogFatal(string message)
	{
		LogInternal(LogLevel.Fatal, message);
	}

	public void LogWarning(string message)
	{
		LogInternal(LogLevel.Warning, message);
	}

	public void Log(string message)
	{
		LogInternal(LogLevel.Info, message);
	}
	
	public void LogDebug(string message, params object[] args)
	{
#if DEBUG
		LogInternal(LogLevel.Debug, message, args);
#endif
	}

	public void LogError(string message, params object[] args)
	{
		LogInternal(LogLevel.Error, message, args);
	}

	public void LogFatal(string message, params object[] args)
	{
		LogInternal(LogLevel.Fatal, message, args);
	}

	public void LogWarning(string message, params object[] args)
	{
		LogInternal(LogLevel.Warning, message, args);
	}

	public void Log(string message, params object[] args)
	{
		LogInternal(LogLevel.Info, message, args);
	}

	public void LogError(string message, Exception exception)
	{
		// Passed as plain text on purpose: an exception message is data and may well contain braces.
		LogInternal(LogLevel.Error, $"{message}\n{exception}");
	}

	public void LogFatal(string message, Exception exception)
	{
		LogInternal(LogLevel.Fatal, $"{message}\n{exception}");
	}

	/// <summary>
	/// Stops accepting new entries and waits for the ones already queued to reach the console.
	/// </summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Completing the writer is what lets the consumer finish the queue instead of abandoning it;
		// the timeout only covers a console that stopped accepting output altogether.
		_queue.Writer.TryComplete();
		try
		{
			_consumer.Wait(TimeSpan.FromSeconds(DrainTimeoutSeconds));
		}
		catch (AggregateException)
		{
			// The consumer handles every write failure itself, so a fault here would mean the queue
			// broke. Nothing is left to do about it while shutting down.
		}
	}

	private void LogInternal(LogLevel level, string message, params object[] args)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		// Formatted here rather than by the consumer so the timestamp and the elapsed time belong to the
		// moment the entry was written, and so the queue preserves that order.
		string text = args.Length == 0
			? LogFormatter.Format(level, Elapsed, message)
			: LogFormatter.Format(level, Elapsed, message, args);

		// A "Write" that loses the race against Dispose is dropped; TryWrite never blocks the caller.
		_queue.Writer.TryWrite(new Entry(level, text));
	}

	/// <summary>
	/// The single owner of the console: it drains the queue in order and is the only thread that ever
	/// touches <see cref="Console.ForegroundColor"/>.
	/// </summary>
	private async Task ConsumeAsync()
	{
		await foreach (Entry entry in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			WriteEntry(entry);
		}
	}

	private void WriteEntry(Entry entry)
	{
		var writer = Array.IndexOf(ErrorLevels, entry.Level) >= 0 ? Console.Error : Console.Out;
		var previousColor = default(ConsoleColor);
		bool colorChanged = false;

		try
		{
			if (_isColorAvailable.Value)
			{
				// Reading the color belongs inside the try: it fails the same way writing does once the
				// console is gone, and losing it must not cost the entry.
				previousColor = Console.ForegroundColor;
				Console.ForegroundColor = ColorOf(entry.Level);
				colorChanged = true;
			}

			writer.WriteLine(entry.Text);
		}
		catch (Exception exception)
		{
			try
			{
				Console.Error.WriteLine($"[Rabotora] a {entry.Level} log entry could not be written: {exception.Message}");
				Console.Error.WriteLine(entry.Text);
			}
			catch (Exception)
			{
				// The console itself is unavailable; there is nowhere left to report to.
			}
		}
		finally
		{
			if (colorChanged)
			{
				try
				{
					Console.ForegroundColor = previousColor;
				}
				catch (IOException)
				{
					// Lost the console between writing and restoring; nothing sensible left to do.
				}
			}
		}
	}

	private static ConsoleColor ColorOf(LogLevel level) => level switch
	{
		LogLevel.Debug => ConsoleColor.DarkGray,
		LogLevel.Info => ConsoleColor.Gray,
		LogLevel.Warning => ConsoleColor.Yellow,
		LogLevel.Error => ConsoleColor.Red,
		LogLevel.Fatal => ConsoleColor.DarkRed,
		_ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown log level.")
	};
}
