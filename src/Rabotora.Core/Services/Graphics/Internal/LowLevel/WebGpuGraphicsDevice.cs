using System.Runtime.InteropServices;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Abstractions.Platform;
using Rabotora.Core.Data;
using Rabotora.Core.Data.Graphics.Internal.LowLevel;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed unsafe class WebGpuGraphicsDevice : IDisposable
{
	private static readonly TimeSpan RequestTimeOut = TimeSpan.FromSeconds(30);
	private static readonly RequestAdapterCallback AdapterCallbackDelegate = OnAdapterRequested;
	private static readonly RequestDeviceCallback DeviceCallbackDelegate = OnDeviceRequested;
	private static readonly DeviceLostCallback DeviceLostCallbackDelegate = OnDeviceLost;

	private readonly ILogger? _logger;
	
	/// <summary>
	/// Like D3D9.<br />
	/// Represents the factory of the WebGPU API.
	/// </summary>
	public WebGPU Api { get; }
	/// <summary>
	/// No best equivalent; like IDirect3D9::GetAdapterCount().<br />
	/// Represents the physical graphics adapter device (like Intel UHD Graphics, NVIDIA GeForce RTX 3080, AMD Radeon RX 5500, etc.)
	/// </summary>
	public Adapter* Adapter { get; private set; }
	/// <summary>
	/// Like IDirect3DDevice9::GetDeviceCaps().<br />
	/// Represents the command queue of the logical graphics device (like a virtual graphics card that can be used to render graphics).
	/// </summary>
	public Queue* Queue { get; private set; }
	/// <summary>
	/// Like IDirect3DDevice9*.<br />
	/// Represents the logical graphics device (like a virtual graphics card that can be used to render graphics).
	/// </summary>
	public Device* Device { get; private set; }
	/// <summary>
	/// Like IDirect3DSwapChain9* + IDirect3DSurface9*.<br />
	/// Represents the surface of the WebGPU API.
	/// </summary>
	public Surface* Surface { get; private set; }
	
	/// <summary>
	/// Like IDirect3D9*.<br />
	/// Represents the instance of the WebGPU API.
	/// </summary>
	private Instance* _instance;
	/// <summary>
	/// Represents the callback function that is called when an uncaptured error occurs in the WebGPU API.
	/// </summary>
	private PfnErrorCallback _uncapturedErrorCallback;
	/// <summary>
	/// Represents the callback function that is called when a device is lost (e.g. due to a power loss, a display change, or the device driver being reloaded) in the WebGPU API.
	/// </summary>
	private PfnDeviceLostCallback _deviceLostCallback;
	/// <summary>
	/// Represents the delegate that is called when an uncaptured error occurs in the WebGPU API.
	/// </summary>
	private ErrorCallback _uncapturedErrorDelegate;
	private GCHandle _deviceLostUserData;
	private bool _isAlreadyDisposed;

	private WebGpuGraphicsDevice(WebGPU api, ILogger? logger)
	{
		Api = api;
		_logger = logger;
		_uncapturedErrorDelegate = null!;
		_isAlreadyDisposed = false;
	}

	public static WebGpuGraphicsDevice Create(INativeWindow window, ILogger? logger = null)
	{
		return Create(window, GetBackendsByPlatformDetect(), logger);
	}

	/// <summary>
	/// Creates the device on an explicitly chosen backend set instead of the platform default.
	/// </summary>
	/// <param name="backends">
	/// The backends wgpu may pick from, in wgpu's own preference order. Passed to wgpu through
	/// <c>InstanceExtras</c>; it is the only backend control this binding actually honors.
	/// </param>
	public static WebGpuGraphicsDevice Create(INativeWindow window, InstanceBackend backends, ILogger? logger = null)
	{
		var device = new WebGpuGraphicsDevice(WebGPU.GetApi(), logger);
		try
		{
			device.Initialize(window, backends);
			return device;
		}
		catch
		{
			device.Dispose();
			throw;
		}
	}
	
	private void Initialize(INativeWindow window, InstanceBackend backends)
	{
		_logger?.LogDebug("WebGPU instance: backends={0}", backends);

		var extras = new InstanceExtras()
		{
			Chain = new ChainedStruct()
			{
				SType = (SType)0x00030006
			},
			Backends = backends
		};
		
		var desInstance = new InstanceDescriptor()
		{
			NextInChain = &extras.Chain
		};
		_instance = Api.CreateInstance(in desInstance);
		if (_instance == null)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", "Creating a WebGPU instance failed.");
		}

		_logger?.LogDebug("WebGPU instance created with backends={0}", backends);

		Surface = WebGpuSurfaceFactory.Create(Api, _instance, window);
		if (Surface == null)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", "Creating a WebGPU surface for the window failed.");
		}

		Adapter = RequestAdapter(Surface);
		LogAdapterProperties(Adapter);

		Device = RequestDevice(Adapter);
		Queue = Api.DeviceGetQueue(Device); // borrowed - never released
		if (Queue == null)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", "Retrieving the default WebGPU queue failed.");
		}
		
		_uncapturedErrorDelegate = OnUncapturedError;
		_uncapturedErrorCallback = new PfnErrorCallback(_uncapturedErrorDelegate);
		Api.DeviceSetUncapturedErrorCallback(Device, _uncapturedErrorCallback, null);
		
		_logger?.Log("WebGPU device ready: queue=0x{0:X}", (long)Queue);
	}

	private Adapter* RequestAdapter(Surface* surface)
	{
		var request = new AdapterRequest();
		var handle = GCHandle.Alloc(request);
		var pfn = new PfnRequestAdapterCallback(AdapterCallbackDelegate);
		try
		{
			var options = new RequestAdapterOptions()
			{
				CompatibleSurface = surface,
				PowerPreference = PowerPreference.HighPerformance, // recommend for games
				BackendType = BackendType.Undefined, // WebGPU will auto-detect backend, and this will be ignored.
				ForceFallbackAdapter = false
			};

			Api.InstanceRequestAdapter(_instance, in options, pfn, (void*) GCHandle.ToIntPtr(handle));

			if (!request.Signal.Wait(RequestTimeOut))
			{
				throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", "Requesting a WebGPU adapter timed out.");
			}

			if (request.Status != RequestAdapterStatus.Success || request.Adapter == null)
			{
				throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", $"No WebGPU adapter is available ({request.Status}): {request.Message}");
			}

			return request.Adapter;
		}
		finally
		{
			pfn.Dispose();
			handle.Free();
			request.Signal.Dispose();
		}
	}
	
	private void LogAdapterProperties(Adapter* adapter)
	{
		if (_logger == null) return;

		var props = new AdapterProperties();
		Api.AdapterGetProperties(adapter, &props);
		
		string name = props.Name == null ? "<unnamed>" : Marshal.PtrToStringUTF8((nint) props.Name) ?? "<unnamed>";
		string driver = props.DriverDescription == null ? "<unknown>" : Marshal.PtrToStringUTF8((nint) props.DriverDescription) ?? "<unknown>";
		
		_logger.Log("WebGPU adapter: name={0}, backend={1}, type={2}, driver={3}", name, props.BackendType, props.AdapterType, driver);
	}

	private Device* RequestDevice(Adapter* adapter)
	{
		var request = new DeviceRequest();
		_deviceLostUserData = GCHandle.Alloc(this, GCHandleType.Weak);
		var requestHandle = GCHandle.Alloc(request);
		var requestPfn = new PfnRequestDeviceCallback(DeviceCallbackDelegate);
		_deviceLostCallback = new PfnDeviceLostCallback(DeviceLostCallbackDelegate);
		try
		{
			var descriptor = new DeviceDescriptor()
			{
				DefaultQueue = new QueueDescriptor(),
				DeviceLostCallback = _deviceLostCallback,
				DeviceLostUserdata = (void*)GCHandle.ToIntPtr(_deviceLostUserData)
			};

			Api.AdapterRequestDevice(adapter, in descriptor, requestPfn, (void*)GCHandle.ToIntPtr(requestHandle));

			if (!request.Signal.Wait(RequestTimeOut))
			{
				throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", "Requesting a WebGPU device timed out.");
			}

			if (request.Status != RequestDeviceStatus.Success || request.Device == null)
			{
				throw new RabotoraInternalException($"{nameof(WebGpuGraphicsDevice)}.cs", $"No WebGPU device is available ({request.Status}): {request.Message}");
			}

			return request.Device;
		}
		finally
		{
			requestPfn.Dispose();
			requestHandle.Free();
			request.Signal.Dispose();
		}
	}

	/// <summary>
	/// The backend set the engine asks for when the caller does not choose one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// On Windows this is Vulkan, not DX12, and that is a measured decision rather than a preference.
	/// With Silk.NET 2.23.0 the D3D12 backend gets as far as an adapter, a device and a configured swap chain,
	/// then dies inside <c>wgpuDeviceCreateRenderPipeline</c> with an access violation (<c>0xC0000005</c>, no managed frame beyond the P/Invoke).
	/// The same code, the same packages and the same managed struct layouts run clean on Vulkan, and a D3D12 run that never creates a pipeline (clear-only) also runs clean.
	/// (The crash is inside wgpu-native's DX12 backend rather than in this binding or in the marshaling)
	/// The DX11 fallback is not a way out either: requesting it alone fail-fasts with <c>0xC0000409</c> during instance/adapter setup.
	/// </para>
	/// <para>
	/// Revisit this when wgpu-native carries a DX12 fix: run the triangle renderer with <c>RABOTORA_D3D_ENABLE</c> and, if it exits 0, put DX12 back into this set.
	/// Do not "fix" the crash by pinning a single WGPU package to an older version; see the comment in R.cs.
	/// </para>
	/// </remarks>
	public static InstanceBackend GetBackendsByPlatformDetect()
	{
		if (OperatingSystem.IsWindows())
		{
#if RABOTORA_D3D_ENABLE
			return InstanceBackend.DX12;
#else
			return InstanceBackend.Vulkan;
#endif
		}

		if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
		{
			return InstanceBackend.Metal;
		}

		// Linux, Android, and other platforms
		return InstanceBackend.Vulkan | InstanceBackend.GL;
	}
	
	private static void OnAdapterRequested(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userData)
	{
		var request = (AdapterRequest)GCHandle.FromIntPtr((nint) userData).Target!;
		request.Status = status;
		request.Adapter = adapter;
		request.Message = message == null ? null : Marshal.PtrToStringUTF8((nint)message);
		request.Signal.Set();
	}
	
	private static void OnDeviceRequested(RequestDeviceStatus status, Device* device, byte* message, void* userData)
	{
		var request = (DeviceRequest) GCHandle.FromIntPtr((nint) userData).Target!;
		request.Status = status;
		request.Device = device;
		request.Message = message == null ? null : Marshal.PtrToStringUTF8((nint) message);
		request.Signal.Set();
	}
	
	private static void OnDeviceLost(DeviceLostReason reason, byte* message, void* userData)
	{
		var self = (WebGpuGraphicsDevice?) GCHandle.FromIntPtr((nint) userData).Target;
		if (self == null) return;

		string text = message == null ? "<no message>" : Marshal.PtrToStringUTF8((nint) message) ?? "<no message>";
		
		// Destroyed is the expected reason when the device is released during shutdown; the others are
		// the ones worth seeing. Logging all four keeps the shutdown case visible without pretending it
		// is a failure.
		self._logger?.LogError("WebGPU device lost ({0}): {1}", reason, text);
	}

	private void OnUncapturedError(ErrorType type, byte* message, void* userData)
	{
		string text = message == null ? "<no message>" : Marshal.PtrToStringUTF8((nint) message) ?? "<no message>";
		_logger?.LogError($"Uncaptured WebGPU error ({type}): {text}");
	}

	public void Dispose()
	{
		_logger?.LogDebug("Cleaning up WebGPU device");
		DisposeInternal();
#if DEBUG
		GC.SuppressFinalize(this);
#endif
	}

	private void DisposeInternal()
	{
		if (_isAlreadyDisposed) return;
		_isAlreadyDisposed = true;
		// PfnErrorCallback is a struct wrapping a function pointer; Dispose is only meaningful
		// when a wrapper was actually created. The field's default value has a null Handle.
		
		if (Queue != null)
		{
			Api.QueueRelease(Queue);
			Queue = null;
		}

		if (Device != null)
		{
			Api.DeviceRelease(Device);
			Device = null;
		}
		_deviceLostCallback.Dispose();
		_uncapturedErrorCallback.Dispose();

		if (_deviceLostUserData.IsAllocated)
		{
			_deviceLostUserData.Free(); // free 'this' pointer (used by device lost callback)
		}

		if (Adapter != null)
		{
			Api.AdapterRelease(Adapter);
			Adapter = null;
		}
		
		if (Surface != null)
		{
			Api.SurfaceRelease(Surface);
			Surface = null;
		}
		
		if (_instance != null)
		{
			Api.InstanceRelease(_instance);
			_instance = null;
		}
	}
	
#if DEBUG
	~WebGpuGraphicsDevice()
	{
		System.Diagnostics.Debug.Fail("WebGpuGraphicDevice was not disposed when reaching GC finalizing queue");
	}
#endif
}