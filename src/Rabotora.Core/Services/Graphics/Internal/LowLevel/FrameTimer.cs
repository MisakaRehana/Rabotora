using Rabotora.Core.Abstractions.Timing;
using Rabotora.Core.Data.Graphics.Internal.LowLevel;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed class FrameTimer
{
	private const double MaxDeltaSeconds = 0.25;

	private readonly IRClock _clock;
	private double _lastSeconds;
	private double _totalSeconds;
	private ulong _frameIndex;

	public FrameTimer(IRClock clock)
	{
		_clock = clock;
		_lastSeconds = _clock.ElapsedSeconds;
	}

	public FrameInfo Advance(int width, int height)
	{
		double now = _clock.ElapsedSeconds;
		double delta = now - _lastSeconds;
		_lastSeconds = now;

		delta = delta switch
		{
			< 0d => 0d,
			> MaxDeltaSeconds => MaxDeltaSeconds,
			_ => delta
		};

		_totalSeconds += delta;
		_frameIndex++;

		return new FrameInfo(delta, _totalSeconds, _frameIndex, width, height);
	}
}