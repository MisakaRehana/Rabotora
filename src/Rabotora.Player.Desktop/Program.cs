using Rabotora.Core;
using Rabotora.Core.Abstractions.Graphics.Internal.LowLevel;
using Rabotora.Core.Services.Graphics.Internal.LowLevel;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Rabotora.Player.Desktop;

public static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		using var app = R.CreateByPlatformDetect("Rabotora Sample Player", SelectBackends());

		// RABOTORA_RENDERER=none drops the GPU path entirely, which is the control case when a crash has
		// to be attributed to the window/message loop rather than to WebGPU.
		app.Renderer = SelectRenderer();

		app.Run();
		return 0;
	}

	/// <summary>
	/// Reads the backend from <c>RABOTORA_BACKEND</c> (<c>auto</c> by default).
	/// </summary>
	/// <remarks>
	/// A process-wide switch rather than an API, because it exists to answer "which backend can this
	/// machine actually run" without a rebuild. Anything unrecognized, including an empty value, means
	/// <c>auto</c>: a typo must not silently change what the engine runs on.
	/// </remarks>
	private static InstanceBackend SelectBackends() =>
		Environment.GetEnvironmentVariable("RABOTORA_BACKEND")?.Trim().ToLowerInvariant() switch
		{
			"vulkan" => InstanceBackend.Vulkan,
			"dx12" => InstanceBackend.DX12,
			"dx11" => InstanceBackend.DX11,
			"gl" => InstanceBackend.GL,
			_ => WebGpuGraphicsDevice.GetBackendsByPlatformDetect()
		};

	/// <summary>
	/// Reads the renderer from <c>RABOTORA_RENDERER</c> (<c>triangle</c> by default).
	/// </summary>
	private static IFrameRenderer? SelectRenderer() =>
		Environment.GetEnvironmentVariable("RABOTORA_RENDERER")?.Trim().ToLowerInvariant() switch
		{
			"none" => null,
			"clear" => new ClearColorRenderer(),
			_ => new TriangleRenderer()
		};
}
