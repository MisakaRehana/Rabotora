using Rabotora.Core.Data;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed unsafe class WebGpuFrame : IDisposable
{
	public WebGPU Api => _graphics.Api;
	public TextureView* View { get; private set; }
	public CommandEncoder* Encoder { get; private set; }

	private readonly WebGpuSwapChain _swapChain;
	private readonly WebGpuGraphicsDevice _graphics;
	private CommandBuffer* _cmdBuffer;
	private bool _submitted;
	private bool _isDisposed;

	internal WebGpuFrame(WebGpuSwapChain swapChain, WebGpuGraphicsDevice graphics, Texture* surfaceTexture)
	{
		_swapChain = swapChain;
		_graphics = graphics;

		var api = graphics.Api;
		try
		{
			var desView = new TextureViewDescriptor()
			{
				Format = TextureFormat.Undefined, // unchanged: keep the texture's own format
				Dimension = TextureViewDimension.Dimension2D,
				BaseMipLevel = 0,
				MipLevelCount = 1,
				BaseArrayLayer = 0,
				ArrayLayerCount = 1,
				Aspect = TextureAspect.All,
			};
			View = api.TextureCreateView(surfaceTexture, in desView);
			if (View == null)
			{
				throw new RabotoraInternalException($"{nameof(WebGpuFrame)}.cs", "Creating a view of the surface texture failed.");
			}

			var desEncoder = new CommandEncoderDescriptor();
			Encoder = api.DeviceCreateCommandEncoder(graphics.Device, in desEncoder);
			if (Encoder == null)
			{
				throw new RabotoraInternalException($"{nameof(WebGpuFrame)}.cs", "Creating a command encoder failed.");
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public void SubmitAndPresent()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_submitted)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuFrame)}.cs", "This frame has already been submitted.");
		}

		var api = _graphics.Api;
		_cmdBuffer = api.CommandEncoderFinish(Encoder, null);
		if (_cmdBuffer == null)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuFrame)}.cs", "Finishing the command encoder failed.");
		}

		var cmds = _cmdBuffer;
		api.QueueSubmit(_graphics.Queue, 1, ref cmds);
		api.SurfacePresent(_graphics.Surface);
		
		_submitted = true;
		_swapChain.NotifyFramePresented();
	}
	
	/// <summary>
	/// Releases the objects this frame created. There is deliberately no finalizer: frames are created
	/// once per displayed frame, and a finalizer would promote every one of them to an older generation
	/// before their views and encoders could be released. The frame loop disposes them with <c>using</c>.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed) return;
		_isDisposed = true;
		var api = _graphics.Api;

		if (_cmdBuffer != null)
		{
			api.CommandBufferRelease(_cmdBuffer);
			_cmdBuffer = null;
		}

		if (Encoder != null)
		{
			api.CommandEncoderRelease(Encoder);
			Encoder = null;
		}

		if (View != null)
		{
			api.TextureViewRelease(View);
			View = null;
		}
		
	}
}