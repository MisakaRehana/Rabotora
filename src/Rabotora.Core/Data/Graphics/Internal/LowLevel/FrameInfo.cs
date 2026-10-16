namespace Rabotora.Core.Data.Graphics.Internal.LowLevel;

public readonly record struct FrameInfo(double DeltaSeconds, double TotalSeconds, ulong FrameIndex, int Width, int Height);