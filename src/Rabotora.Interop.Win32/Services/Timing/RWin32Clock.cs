using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rabotora.Core.Abstractions.Timing;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using Win32Long = TerraFX.Interop.Windows.LARGE_INTEGER;

namespace Rabotora.Core.Services.Timing;

[SupportedOSPlatform("windows")]
public sealed unsafe class RWin32Clock : IRClock
{
	private const uint SecondsTo100Nanoseconds = 10_000_000;
	private const double MaxWaitSeconds = 1d;

	private readonly HANDLE _waitTimer;
	private bool _disposed;

	public long TicksPerSecond { get; }

	public RWin32Clock()
	{
		Win32Long freq;
		if (QueryPerformanceFrequency(&freq) == 0)
		{
			throw new Win32Exception("Failed to get performance frequency by QueryPerformanceFrequency");
		}
		if (freq.QuadPart <= 0)
		{
			throw new InvalidOperationException($"Invalid QPC frequency: {freq}");
		}
		
		TicksPerSecond = freq.QuadPart;

		// Least privilege: the clock only waits on the timer and arms it, so it asks for exactly that
		// rather than TIMER_ALL_ACCESS.
		var timer = CreateWaitableTimerExW(null, null, CREATE.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, SYNCHRONIZE | TIMER_MODIFY_STATE);
		if (timer == HANDLE.NULL)
		{
			timer = CreateWaitableTimerW(null, false, null);
		}
		if (timer == HANDLE.NULL)
		{
			throw new InvalidOperationException("Failed to create the waitable timer", new Win32Exception(Marshal.GetLastWin32Error(), "CreateWaitableTimerExW failed"));
		}

		_waitTimer = timer;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public long GetTicks()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		Win32Long counter;
		if (QueryPerformanceCounter(&counter) == 0)
		{
			throw new Win32Exception("Failed to get performance counter by QueryPerformanceCounter");
		}
		return counter.QuadPart;
	}

	public void WaitSeconds(double seconds)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (!double.IsFinite(seconds))
		{
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "The wait duration must be a finite number of seconds.");
		}

		if (seconds <= 0d)
		{
			Thread.Sleep(0); // yield the remainder of the slice instead of sleeping a whole millisecond
			return;
		}

		if (_waitTimer == HANDLE.NULL)
		{
			SleepInChunks(seconds);
			return;
		}

		// Long waits are a sequence of short ones rather than a single truncated one, so the method
		// honors the whole duration it was asked for whatever that duration is.
		double remaining = seconds;
		while (remaining > 0d)
		{
			double chunk = Math.Min(remaining, MaxWaitSeconds);
			var dueTime = new Win32Long { QuadPart = -(long) (chunk * SecondsTo100Nanoseconds) };
			if (SetWaitableTimer(_waitTimer, &dueTime, 0, null, null, false) == 0)
			{
				SleepInChunks(remaining);
				return;
			}

			uint waitResult = WaitForSingleObject(_waitTimer, INFINITE);
			if (waitResult != WAIT.WAIT_OBJECT_0)
			{
				throw new InvalidOperationException($"Failed to wait on the clock's timer (WaitForSingleObject returned 0x{waitResult:X8})", new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed"));
			}

			remaining -= chunk;
		}
	}

	/// <summary>
	/// Waits the whole requested duration on the coarse scheduler timer, in chunks small enough that
	/// the sleep cannot overflow.
	/// </summary>
	private static void SleepInChunks(double seconds)
	{
		double remaining = seconds;
		while (remaining > 0d)
		{
			double chunk = Math.Min(remaining, MaxWaitSeconds);
			Thread.Sleep(TimeSpan.FromSeconds(chunk));
			remaining -= chunk;
		}
	}

	/// <summary>
	/// Closes the waitable timer this clock owns. The counter itself needs no cleanup.
	/// </summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (_waitTimer != HANDLE.NULL)
		{
			CloseHandle(_waitTimer);
		}
	}
}
