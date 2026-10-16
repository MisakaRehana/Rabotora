using Silk.NET.WebGPU;

namespace Rabotora.Core.Data.Graphics.Internal.LowLevel;

internal sealed class AdapterRequest
{
	public readonly ManualResetEventSlim Signal = new(false);
	public unsafe Adapter* Adapter;
	public RequestAdapterStatus Status;
	public string? Message;
}