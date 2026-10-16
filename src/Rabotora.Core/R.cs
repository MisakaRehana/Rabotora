using System.Runtime.ExceptionServices;
using Rabotora.Core.Abstractions.Graphics.Internal.LowLevel;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Abstractions.Platform;
using Rabotora.Core.Abstractions.Timing;
using Rabotora.Core.Data;
using Rabotora.Core.Services.Graphics.Internal.LowLevel;
using Rabotora.Core.Services.Logging;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Rabotora.Core;

/// <summary>
/// Represents the core engine instance of the application managed by Rabotora.
/// </summary>
public sealed class R : IDisposable
{
	// WebGPU is pinned to Silk.NET 2.23.0, with all three WGPU packages on that one version.
	// Two things to know before touching it:
	//   1. The backend is chosen explicitly, never left to the native default.
	//      On Windows that is Vulkan (see WebGpuGraphicsDevice.GetBackendsByPlatformDetect): with 2.23.0 the D3D12 backend dies in wgpuDeviceCreateRenderPipeline with 0xC0000005,
	//      and DX11 fail-fasts during instance setup, both reproduced three times on 2026-10-17.
	//      Vulkan runs the triangle for 120 frames and exits 0.
	//   2. Never upgrade or downgrade a single WGPU package on its own: the managed structs are marshaled straight into wgpu_native, so a version skew is silent at compile time and fatal at runtime.
	// See comments in WebGpuGraphicsDevice.cs for how to re-test DX12 after a wgpu-native update.
	public IRClock Clock { get; }
	public ILogger Logger { get; }
	public INativeWindow Window { get; }
	public IFrameRenderer? Renderer { get; set; }

	private WebGpuGraphicsDevice? _graphics;
	private WebGpuSwapChain? _swapChain;
	private FrameTimer? _frameTimer;
	private readonly InstanceBackend _backends;

	private bool _isRunning;
	private bool _disposed;

	private R(IRClock clock, ILogger logger, INativeWindow window, InstanceBackend backends)
	{
		Clock = clock;
		Logger = logger;
		Window = window;
		_backends = backends;
	}

	public static R CreateByPlatformDetect(string title, int initWidth = 1280, int initHeight = 720)
	{
		return CreateByPlatformDetect(title, WebGpuGraphicsDevice.GetBackendsByPlatformDetect(), initWidth, initHeight);
	}

	/// <summary>
	/// Creates the engine on an explicitly chosen WebGPU backend set instead of the platform default.
	/// </summary>
	/// <param name="backends">
	/// Backends wgpu may choose from, in wgpu's preference order. The only backend control this binding
	/// honors: <c>RequestAdapterOptions.BackendType</c> is ignored by the native side.
	/// </param>
	public static R CreateByPlatformDetect(string title, InstanceBackend backends, int initWidth = 1280, int initHeight = 720)
	{
		var clock = IRClock.PlatformCreate();
		ILogger? logger = null;
		try
		{
			logger = LoggerFactory.CreateByPlatformDetect(clock) ?? new NullLogger();
			var window = INativeWindow.PlatformCreate(title, initWidth, initHeight, logger);
			return new R(clock, logger, window, backends);
		}
		catch
		{
			logger?.Dispose();
			clock.Dispose();
			throw;
		}
	}

	public void Run()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_isRunning)
		{
			throw new InvalidOperationException("The engine loop is already running.");
		}

		_isRunning = true;

		try
		{
			if (Renderer != null)
			{
				_graphics = WebGpuGraphicsDevice.Create(Window, _backends, Logger);
				_swapChain = new WebGpuSwapChain(_graphics, Window.Width, Window.Height, Logger);
				Renderer.Load(_graphics, _swapChain);
				_frameTimer = new FrameTimer(Clock);
			}

			Window.Show(); // then show window to avoid "flickering".

			Logger.Log("Engine entering the main loop.");

			var swapChain = _swapChain;
			var frameTimer = _frameTimer;
			var renderer = Renderer;

			while (!Window.IsClosed)
			{
				Window.PumpEvents(swapChain != null ? 0d : double.PositiveInfinity);
				if (Window.IsClosed) break;

				if (swapChain == null || frameTimer == null || renderer == null)
				{
					continue; // No renderer: nothing to draw, PumpEvents already waited for the next message.
				}

				if (swapChain.Width != Window.Width || swapChain.Height != Window.Height)
				{
					swapChain.Resize(Window.Width, Window.Height);
					renderer.Resize(Window.Width, Window.Height);
				}

				if (!swapChain.TryAcquireFrame(out WebGpuFrame frame))
				{
					Window.PumpEvents(0.001);
					continue;
				}

				using (frame)
				{
					var info = frameTimer.Advance(Window.Width, Window.Height);
					renderer.RenderFrame(frame, in info);
					frame.SubmitAndPresent();
				}
			}
		}
		finally
		{
			_isRunning = false;
		}
	}

	public void Dispose()
	{
		const string errMessage = "One or more resources failed to release while disposing the engine.";
		if (_disposed)
		{
			return;
		}
		_disposed = true;

		// // TODO: refactor this staircase shit.
		// try
		// {
		// 	Renderer?.Dispose();
		// 	Renderer = null;
		// }
		// finally
		// {
		// 	try
		// 	{
		// 		_swapChain?.Dispose();
		// 		_swapChain = null;
		// 	}
		// 	finally
		// 	{
		// 		try
		// 		{
		// 			_graphics?.Dispose();
		// 			_graphics = null;
		// 		}
		// 		finally
		// 		{
		// 			try
		// 			{
		// 				Window.Dispose();
		// 			}
		// 			finally
		// 			{
		// 				try
		// 				{
		// 					Logger.Dispose();
		// 				}
		// 				finally
		// 				{
		// 					Clock.Dispose();
		// 				}
		// 			}
		// 		}
		// 	}
		// }
		var errors = new List<Exception>(capacity: 6);

		Step(() => { Renderer?.Dispose(); Renderer = null; });
		Step(() => { _swapChain?.Dispose(); _swapChain = null; });
		Step(() => { _graphics?.Dispose(); _graphics = null; });
		Step(() => Window.Dispose());
		Logger.LogDebug("Finished engine quit clean-up. Rabotora will shut down.");
		Step(() => Logger.Dispose());
		Step(() => Clock.Dispose());

		if (errors.Count == 1)
		{
			ExceptionDispatchInfo.Capture(errors[0]).Throw();
		}
		else if (errors.Count > 0)
		{
			throw new RabotoraInternalException($"{nameof(R)}.cs", errMessage, new AggregateException(errMessage, errors));
		}
		return;

		void Step(Action release)
		{
			try
			{
				release();
			}
			catch (Exception ex)
			{
				errors.Add(ex);
			}
		}
	}
}