using Rabotora.Core.Abstractions.Platform;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

internal static unsafe class WebGpuSurfaceFactory
{
	public static Surface* Create(WebGPU api, Instance* instance, INativeWindow window)
	{
		if (OperatingSystem.IsWindows())
		{
			var chain = new SurfaceDescriptorFromWindowsHWND()
			{
				Chain = new ChainedStruct()
				{
					Next = null,
					SType = SType.SurfaceDescriptorFromWindowsHwnd,
				},
				Hinstance = (void*) window.NativeInstance,
				Hwnd = (void*) window.NativeHandle
			};
			
			return Create(api, instance, (ChainedStruct*)&chain);
		}
		
		// TODO: Implement other platforms
		// X11: SurfaceDescriptorFromXlibWindow(Display, Window)
		// Wayland: SurfaceDescriptorFromWaylandSurface(wl_display, wl_surface)
		// macOS: SurfaceDescriptorFromMetalLayer(CAMetalLayer*) - note that INativeWindow would have to hand out an NSView/CAMetalLayer, not the NSWindow.
		// Android: SurfaceDescriptorFromAndroidNativeWindow()
		// iOS: SurfaceDescriptorFromMetalLayer(CAMetalLayer*)
		throw new PlatformNotSupportedException("This platform has no WebGPU surface implementation yet. To implement platform-specified implementation, add its chained struct here.");
	}

	private static Surface* Create(WebGPU api, Instance* instance, ChainedStruct* chain)
	{
		var descriptor = new SurfaceDescriptor()
		{
			NextInChain = chain
		};
		return api.InstanceCreateSurface(instance, in descriptor);
	}
}