using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Data;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed unsafe class WebGpuSwapChain : IDisposable
{
	public int Width { get; private set; }
	public int Height { get; private set; }
	public TextureFormat Format { get; private set; }
	
	private readonly WebGpuGraphicsDevice _graphics;
	private readonly ILogger? _logger;
	private readonly TextureFormat _format;
	private readonly PresentMode _presentMode;
	private readonly CompositeAlphaMode _alphaMode;

	/// <summary>
	/// Whether the surface image handed out by the last successful <see cref="TryAcquireFrame"/> is still
	/// waiting for <c>SurfacePresent</c>. wgpu-native aborts the process if a second image is acquired
	/// while the previous one is outstanding, so this has to be tracked.
	/// </summary>
	private bool _frameOutstanding;
	private bool _configured;
	private bool _isDisposed;

	public WebGpuSwapChain(WebGpuGraphicsDevice graphics, int width, int height, ILogger? logger)
	{
		_isDisposed = false;
		_graphics = graphics;
		_logger = logger;

		// The surface's capabilities describe the surface, not the size it is configured to, so they are
		// queried once here instead of on every resize. SurfaceGetCapabilities allocates lists that have
		// to be handed back, which makes doing it per resize both wasteful and easy to leak.
		var capabilities = new SurfaceCapabilities();
		graphics.Api.SurfaceGetCapabilities(graphics.Surface, graphics.Adapter, ref capabilities);
		try
		{
			_format = graphics.Api.SurfaceGetPreferredFormat(graphics.Surface, graphics.Adapter);
			_presentMode = ChoosePresentMode(in capabilities);
			_alphaMode = ChooseAlphaMode(in capabilities);
		}
		finally
		{
			graphics.Api.SurfaceCapabilitiesFreeMembers(capabilities);
		}

		Configure(width, height);
	}

	public void Resize(int width, int height)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		// A minimized Win32 window reports 0x0 (Win32Window keeps the last real size in that case, but a
		// zero can still arrive from another platform), and a same-size resize is a no-op by definition.
		if (width <= 0 || height <= 0) return;
		if (width == Width && height == Height) return;

		Configure(width, height);
	}

	private void Configure(int width, int height)
	{
		var configuration = new SurfaceConfiguration()
		{
			Device = _graphics.Device,
			Format = _format,
			Usage = TextureUsage.RenderAttachment,
			Width = (uint) width,
			Height = (uint) height,
			PresentMode = _presentMode,
			AlphaMode = _alphaMode,
		};

		_graphics.Api.SurfaceConfigure(_graphics.Surface, in configuration);

		Width = width;
		Height = height;
		Format = _format;
		_configured = true;

		_logger?.LogDebug("Swap chain configured: {0}x{1}, format={2}, present={3}, alpha={4}", width, height, _format, _presentMode, _alphaMode);
	}

	private static PresentMode ChoosePresentMode(in SurfaceCapabilities capabilities)
	{
		// Fifo (vsync) is what the frame loop is designed around: it throttles the loop to the display
		// refresh rate without a Sleep. Any driver that lacks it is unusual enough that the first
		// available mode is the least surprising fallback.
		nuint count = capabilities.PresentModeCount;
		if (count == 0)
		{
			return PresentMode.Fifo;
		}

		for (nuint i = 0; i < count; i++)
		{
			if (capabilities.PresentModes[i] == PresentMode.Fifo)
			{
				return PresentMode.Fifo;
			}
		}
		return capabilities.PresentModes[0];
	}

	private static CompositeAlphaMode ChooseAlphaMode(in SurfaceCapabilities capabilities)
	{
		nuint count = capabilities.AlphaModeCount;
		if (count == 0)
		{
			return CompositeAlphaMode.Auto;
		}

		for (nuint i = 0; i < count; i++)
		{
			if (capabilities.AlphaModes[i] == CompositeAlphaMode.Auto)
			{
				return CompositeAlphaMode.Auto;
			}
		}
		return capabilities.AlphaModes[0];
	}

	public bool TryAcquireFrame(out WebGpuFrame frame)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		frame = null!;

		if (_frameOutstanding)
		{
			_logger?.LogWarning("A frame was disposed without being presented; presenting it as-is to release the surface image.");
			_graphics.Api.SurfacePresent(_graphics.Surface);
			_frameOutstanding = false;
			return false;
		}

		var api = _graphics.Api;
		var surfaceTexture = new SurfaceTexture();
		api.SurfaceGetCurrentTexture(_graphics.Surface, ref surfaceTexture);

		switch (surfaceTexture.Status)
		{
			case SurfaceGetCurrentTextureStatus.Success:
				break;

			case SurfaceGetCurrentTextureStatus.Outdated:
			case SurfaceGetCurrentTextureStatus.Lost:
				_logger?.LogDebug("Surface {0}; reconfiguring.", surfaceTexture.Status);
				Configure(Width, Height);
				return false;

			case SurfaceGetCurrentTextureStatus.Timeout:
				// Nothing to present yet. Don't spin, don't reconfigure — just skip.
				return false;

			default:
				// OutOfMemory / DeviceLost are unrecoverable here.
				throw new RabotoraInternalException($"{nameof(WebGpuSwapChain)}.cs", $"Acquiring a WebGPU surface texture failed with {surfaceTexture.Status}.");
		}

		frame = new WebGpuFrame(this, _graphics, surfaceTexture.Texture);
		_frameOutstanding = true;
		return true;
	}

	/// <summary>
	/// Told by <see cref="WebGpuFrame.SubmitAndPresent"/> that the outstanding image reached the screen.
	/// </summary>
	internal void NotifyFramePresented()
	{
		_frameOutstanding = false;
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;

		if (_configured)
		{
			_graphics.Api.SurfaceUnconfigure(_graphics.Surface);
			_configured = false;
		}

		// Unconfiguring already throws away an image that was never presented, so there is nothing left
		// for TryAcquireFrame to guard against.
		_frameOutstanding = false;
	}
}