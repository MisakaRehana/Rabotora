using Rabotora.Core.Data.Graphics.Internal.LowLevel;
using Rabotora.Core.Services.Graphics;
using Rabotora.Core.Services.Graphics.Internal.LowLevel;

namespace Rabotora.Core.Abstractions.Graphics.Internal.LowLevel;

public interface IFrameRenderer : IDisposable
{
	/// <summary>
	/// Called once before the first frame, with the device and swap chain this renderer draws into.
	/// </summary>
	void Load(WebGpuGraphicsDevice graphics, WebGpuSwapChain swapChain);

	/// <summary>
	/// Records this frame's GPU work into <paramref name="frame"/>'s command encoder.
	/// </summary>
	void RenderFrame(WebGpuFrame frame, in FrameInfo info);

	/// <summary>
	/// Called when the client area changed size, after the swap chain was reconfigured.
	/// </summary>
	void Resize(int width, int height);
}