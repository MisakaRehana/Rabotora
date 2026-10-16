using System.Diagnostics.CodeAnalysis;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Events.Platform;
using Rabotora.Core.Utility.Platform;

namespace Rabotora.Core.Abstractions.Platform;

public interface INativeWindow : IDisposable
{
	nint NativeHandle { get; }  // Windows: [Win32] HWND, Linux: [X11] Window [Wayland] wl_surface, macOS: [Cocoa] NSWindow*, Android: [Vulkan] ANativeWindow*, iOS: [Metal] CAMetalLayer*
	nint NativeInstance { get; } // Windows: [Win32] HINSTANCE, Linux: [X11] Display [Wayland] wl_display, macOS: [Cocoa] 0, Android: [Vulkan] ANativeActivity*, iOS: [Metal] 0

	int Width { get; }  // drawable/client area, so a renderer can size its swap chain from it directly
	int Height { get; }
	string Title { get; set; }

	bool IsClosed { get; }
	
	event EventHandler<ResizedEventArgs>? Resized;
	event EventHandler? Closed;
	event EventHandler<KeyEventArgs>? KeyInput;
	event EventHandler<CharInputEventArgs>? CharInput;
	event EventHandler<MouseMoveEventArgs>? MouseMove;
	event EventHandler? MouseEnter;
	event EventHandler? MouseLeave;
	event EventHandler<MouseButtonChangedEventArgs>? MouseButtonChanged;
	event EventHandler<MouseWheelEventArgs>? MouseWheel;
	
	/// <summary>
	/// Puts the window on screen.
	/// </summary>
	/// <remarks>
	/// Implementations create the window hidden: it exists, and can be sized and titled, before anything
	/// appears. Presenting it is the caller's decision, which for the engine is <c>R.Run</c>. This is a
	/// command and not a state: a window that is shown does not remember being asked, and a caller that
	/// needs to know whether the window is on screen keeps that for itself. Showing a window that is
	/// already gone has no effect.
	/// </remarks>
	void Show();

	/// <summary>
	/// Takes the window off screen, leaving it alive and still able to be shown again.
	/// </summary>
	void Hide();

	/// <summary>
	/// Waits at most <paramref name="budgetSeconds"/> for a message, then dispatches every message that
	/// is already queued.
	/// </summary>
	/// <param name="budgetSeconds">
	/// How long this call may spend waiting for the first message. Positive infinity waits until a
	/// message arrives; NaN and values at or below zero dispatch whatever is already queued without
	/// waiting.
	/// </param>
	/// <returns><see langword="true"/> when at least one message was dispatched.</returns>
	/// <remarks>
	/// <para>
	/// The budget is what lets a caller interleave work with the messages: the caller sleeps in the
	/// kernel until input arrives or its own turn comes, so an idle window still costs no CPU.
	/// </para>
	/// <para>
	/// <see langword="false"/> means nothing was dispatched, which is not the same as "the budget
	/// elapsed". A message another thread sends with <c>SendMessage</c> is delivered inside the wait
	/// itself and never reaches the queue, so a wait can come back early; callers that need to know how
	/// much time passed read their own clock. A caller that must not miss the frame's turn passes a
	/// budget and renders whatever the pump dispatched.
	/// </para>
	/// </remarks>
	bool PumpEvents(double budgetSeconds);

	/// <summary>
	/// Dispatches messages, waiting until at least one arrives.
	/// </summary>
	void PumpEvents() => PumpEvents(double.PositiveInfinity);
	void Close();

	/// <summary>
	/// Creates the window implementation the current platform provides.
	/// </summary>
	/// <param name="title">Initial window title.</param>
	/// <param name="initWidth">Initial <em>drawable</em> width, frame excluded.</param>
	/// <param name="initHeight">Initial <em>drawable</em> height, frame excluded.</param>
	/// <param name="logger">Optional logger the window reports through. May be <see langword="null"/>.</param>
	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, PlatformImplementation.Win32WindowTypeName, PlatformImplementation.Win32AssemblyName)]
	static INativeWindow PlatformCreate(string title, int initWidth, int initHeight, ILogger? logger)
	{
		// The name has to keep matching Win32Window's declaration in Rabotora.Interop.Win32; see
		// PlatformImplementation for why the lookup is a string in the first place.
		Type type = PlatformImplementation.ResolveRequiredWin32(PlatformImplementation.Win32WindowTypeName);
		return PlatformImplementation.CreateInstance<INativeWindow>(type, title, initWidth, initHeight, logger);
	}
}
