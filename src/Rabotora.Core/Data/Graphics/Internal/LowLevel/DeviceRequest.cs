using Silk.NET.WebGPU;

namespace Rabotora.Core.Data.Graphics.Internal.LowLevel;

internal sealed class DeviceRequest
{
	public readonly ManualResetEventSlim Signal = new(false);
	public unsafe Device* Device;
	public RequestDeviceStatus Status;
	public string? Message;
}