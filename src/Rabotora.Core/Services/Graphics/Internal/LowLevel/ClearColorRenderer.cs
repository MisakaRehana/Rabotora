using Rabotora.Core.Abstractions.Graphics.Internal.LowLevel;
using Rabotora.Core.Data.Graphics.Internal.LowLevel;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed unsafe class ClearColorRenderer : IFrameRenderer
{
	public Color Color { get; set; }

	private WebGpuGraphicsDevice? _graphics;
	private WebGpuSwapChain? _swapChain;
	private bool _isDisposed;

	public ClearColorRenderer()
	{
		Color = new Color(0, 0, 0, 1); // Color.Black
	}
	
	public ClearColorRenderer(double r, double g, double b, double a)
	{
		Color = new Color(r, g, b, a);
	}

	public void Load(WebGpuGraphicsDevice graphics, WebGpuSwapChain swapChain)
	{
		_graphics = graphics;
		_swapChain = swapChain;
	}

	public void RenderFrame(WebGpuFrame frame, in FrameInfo info)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var api = frame.Api;

		var colorAttachment = new RenderPassColorAttachment()
		{
			View = frame.View,
			// WGPU_DEPTH_SLICE_UNDEFINED: the swap chain view is 2D, so it has no depth slices. See R.cs.
			DepthSlice = uint.MaxValue,
			ResolveTarget = null,
			LoadOp = LoadOp.Clear,
			StoreOp = StoreOp.Store,
			ClearValue = Color
		};

		var desPass = new RenderPassDescriptor()
		{
			ColorAttachmentCount = 1,
			ColorAttachments = &colorAttachment,
			DepthStencilAttachment = null
		};

		var pass = api.CommandEncoderBeginRenderPass(frame.Encoder, in desPass);
		if (pass == null) return;

		api.RenderPassEncoderEnd(pass);
		api.RenderPassEncoderRelease(pass);
	}

	public void Resize(int width, int height)
	{
		// Nothing size-dependent to rebuild for a plain color clear renderer.
	}

	public void Dispose()
	{
		if (_isDisposed) return;
		_isDisposed = true;
		_graphics = null;
		_swapChain = null;
	}
}