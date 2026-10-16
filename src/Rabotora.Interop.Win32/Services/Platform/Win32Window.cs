using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rabotora.Core.Abstractions.Logging;
using Rabotora.Core.Abstractions.Platform;
using Rabotora.Core.Data.Platform;
using Rabotora.Core.Enumerators.Platform;
using Rabotora.Core.Events.Platform;
using TerraFX.Interop.Windows;
using Win32 = TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.Windows.WM;

namespace Rabotora.Core.Services.Platform;

/// <summary>
/// Win32 (HWND) implementation of <see cref="INativeWindow"/>.
/// </summary>
/// <remarks>
/// This part holds the window itself: its state, its lifetime and the dispatch of its messages. The
/// decoding of raw message parameters lives in <c>Win32Window.Extension.cs</c>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class Win32Window : INativeWindow
{
	private const string WindowClassName = "RabotoraWindow";
	private const int EWindowClassAlreadyExists = 1410;

	/// <summary>
	/// The longest one <see cref="PumpEvents(double)"/> call may park the thread. A longer budget is
	/// capped instead of rejected, so that a wrong unit in a caller's arithmetic costs a late frame
	/// rather than a message loop that stops responding. The unbounded overload is not capped: waiting
	/// for the first message of an idle window is exactly its job.
	/// </summary>
	private const double MaxPumpBudgetSeconds = 1.0;

	public nint NativeHandle => _hwnd;
	public nint NativeInstance => _hinstance;

	public int Width { get; private set; }
	public int Height { get; private set; }
	public bool IsClosed { get; private set; }

	public string Title
	{
		get => _title;
		set
		{
			SetNativeTitle(value);

			// Only after the window accepted it, so that Title never reports a title the window does not have.
			_title = value;
		}
	}

	private readonly ILogger? _logger;
	private readonly HWND _hwnd;
	private readonly HINSTANCE _hinstance;
	private GCHandle _selfHandle;
	private string _title;

	private bool _windowDestroyed;
	private char? _pendingHighSurrogate;
	private int _pressedButtons;
	private bool _isMouseInside;
	private int _wheelDeltaRemainder;
	private bool _isDisposed;

	public event EventHandler<ResizedEventArgs>? Resized;
	public event EventHandler? Closed;
	public event EventHandler<KeyEventArgs>? KeyInput;
	public event EventHandler<CharInputEventArgs>? CharInput;
	public event EventHandler<MouseMoveEventArgs>? MouseMove;
	public event EventHandler? MouseEnter;
	public event EventHandler? MouseLeave;
	public event EventHandler<MouseButtonChangedEventArgs>? MouseButtonChanged;
	public event EventHandler<MouseWheelEventArgs>? MouseWheel;
	
	static Win32Window()
	{
		var instance = Win32.GetModuleHandleW(null);

		fixed (char* className = WindowClassName)
		{
			var windowClass = new WNDCLASSEXW()
			{
				cbSize = (uint) sizeof(WNDCLASSEXW),
				style = CS.CS_HREDRAW | CS.CS_VREDRAW | CS.CS_OWNDC,
				lpfnWndProc = &WndProcStatic,
				hInstance = instance,
				hCursor = Win32.LoadCursorW(HINSTANCE.NULL, IDC.IDC_ARROW),
				lpszClassName = className,
			};

			if (Win32.RegisterClassExW(&windowClass) == 0)
			{
				int error = Marshal.GetLastWin32Error();
				if (error != EWindowClassAlreadyExists)
				{
					throw new InvalidOperationException("Failed to register window class", new Win32Exception(error, "RegisterClassExW failed"));
				}
			}
		}
	}

	public Win32Window(string title, int initWidth, int initHeight, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(title);
		_logger = logger;
		_title = title;
		Width = initWidth;
		Height = initHeight;

		if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063)) // Windows 10 Creators Update, Version 1703 (Build 15063)
		{
			Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
		}

		_hinstance = Win32.GetModuleHandleW(null);

		try
		{
			var rect = new RECT()
			{
				left = 0,
				top = 0,
				right = initWidth,
				bottom = initHeight
			};
			if (Win32.AdjustWindowRectEx(&rect, WS.WS_OVERLAPPEDWINDOW, false, 0) == 0)
			{
				throw new InvalidOperationException("Failed to adjust the window rectangle", new Win32Exception(Marshal.GetLastWin32Error(), "AdjustWindowRectEx failed"));
			}

			_selfHandle = GCHandle.Alloc(this, GCHandleType.Normal); // fix 'this' pointer before creating window

			fixed (char* className = WindowClassName)
			fixed (char* titlePtr = _title)
			{
				_hwnd = Win32.CreateWindowExW(0, className, titlePtr, WS.WS_OVERLAPPEDWINDOW,
					Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
					rect.right - rect.left, rect.bottom - rect.top,
					HWND.NULL, HMENU.NULL,
					_hinstance, (void*)GCHandle.ToIntPtr(_selfHandle));
				if (_hwnd == HWND.NULL)
				{
					throw new InvalidOperationException("Failed to create window", new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed"));
				}
			}

			// Hidden from the start, and said so explicitly rather than relying on WS_OVERLAPPEDWINDOW
			// happening not to carry WS_VISIBLE: going on screen is the caller's decision and reaches
			// this instance through Show.
			Win32.ShowWindow(_hwnd, SW.SW_HIDE);
			_logger?.LogDebug("Win32 window created: hwnd=0x{0:X}, client area {1}x{2}", (long) _hwnd.Value, Width, Height);
		}
		catch
		{
			ReleaseNativeResources();
			throw;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SetNativeTitle(string title)
	{
		ArgumentNullException.ThrowIfNull(title);
		if (string.Equals(_title, title, StringComparison.Ordinal))
		{
			return;
		}

		if (!_windowDestroyed && _hwnd != HWND.NULL)
		{
			fixed (char* titlePtr = title)
			{
				if (Win32.SetWindowTextW(_hwnd, titlePtr) == 0)
				{
					throw new InvalidOperationException("Failed to set the window title", new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowTextW failed"));
				}
			}
		}
	}
	
	/// <summary>
	/// Puts the window on screen and paints it before returning.
	/// </summary>
	public void Show()
	{
		if (_windowDestroyed || _hwnd == HWND.NULL)
		{
			return;
		}

		Win32.ShowWindow(_hwnd, SW.SW_SHOW);

		// Paint now instead of waiting for the loop to reach the queued WM_PAINT, so the window never
		// shows an unpainted frame.
		Win32.UpdateWindow(_hwnd);
	}

	/// <summary>
	/// Takes the window off-screen, leaving it alive and still able to be shown again.
	/// </summary>
	public void Hide()
	{
		if (_windowDestroyed || _hwnd == HWND.NULL)
		{
			return;
		}

		Win32.ShowWindow(_hwnd, SW.SW_HIDE);
	}

	[UnmanagedCallersOnly]
	private static LRESULT WndProcStatic(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
	{
		if (msg == WM_NCCREATE)
		{
			// lParam is a CREATESTRUCTW whose lpCreateParams carries the GCHandle passed to CreateWindowExW
			var createStruct = (CREATESTRUCTW*) lParam.Value;
			nint selfHandle = (nint) createStruct->lpCreateParams;
			if (selfHandle != 0)
			{
				Win32.SetWindowLongPtrW(hwnd, GWLP.GWLP_USERDATA, selfHandle);
			}
		}

		nint ptr = Win32.GetWindowLongPtrW(hwnd, GWLP.GWLP_USERDATA);
		var self = ptr != 0 ? (Win32Window?) GCHandle.FromIntPtr(ptr).Target : null;
		
		return self?.WndProc(hwnd, msg, wParam, lParam) ?? Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
	}

	private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			return HandleMessage(hwnd, msg, wParam, lParam);
		}
		catch (Exception exception)
		{
			_logger?.LogError($"A window message handler threw while processing message 0x{msg:X4}.", exception);
			return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
		}
	}

	private LRESULT HandleMessage(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
	{
		switch (msg)
		{
			case WM_CLOSE:
			{
				Close();
				return 0;	
			}
			case WM_DESTROY:
			{
				_windowDestroyed = true;
				IsClosed = true;
				Closed?.Invoke(this, EventArgs.Empty);
				Win32.PostQuitMessage(0);
				return 0;
			}
			case WM_SIZE:
			{
				var (w, h) = SizeFrom(lParam);
				if (w > 0 && h > 0 && (w != Width || h != Height))
				{
					Width = w;
					Height = h;
					Resized?.Invoke(this, new ResizedEventArgs() { NewWidth = w, NewHeight = h });
				}
				return 0;
			}
			case WM_ERASEBKGND:
			{
				return 1; // To avoid flickering; WebGPU will redraw the window.
			}
			case WM_KEYDOWN:
			{
				// Repeats are dropped: a subscriber tracks the key state from the down and up events.
				if (!IsRepeat(lParam))
				{
					KeyInput?.Invoke(this, new KeyEventArgs() { Key = new KeyEvent(TranslateKey(wParam, lParam), true) });
				}
				return 0;
			}
			case WM_KEYUP:
			{
				KeyInput?.Invoke(this, new KeyEventArgs() { Key = new KeyEvent(TranslateKey(wParam, lParam), false) });
				return 0;
			}
			case WM_SYSKEYDOWN:
			{
				if (!IsRepeat(lParam))
				{
					KeyInput?.Invoke(this, new KeyEventArgs() { Key = new KeyEvent(TranslateKey(wParam, lParam), true) });
				}
				// Handed back to the default handling, which is what turns Alt+F4 and Alt+Space into a
				// WM_SYSCOMMAND. Returning 0 here is what used to make Alt+F4 do nothing at all.
				return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
			}
			case WM_SYSKEYUP:
			{
				KeyInput?.Invoke(this, new KeyEventArgs() { Key = new KeyEvent(TranslateKey(wParam, lParam), false) });
				return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
			}
			case WM_CHAR:
			{
				ReportCharInput((char)wParam.Value);
				return 0; // to block Win32 DefaultSound
			}
			case WM_MOUSEMOVE:
			{
				var (x, y) = PositionFrom(lParam);
				ReportMousePosition(hwnd, x, y);
				return 0;
			}
			case WM_MOUSELEAVE:
			{
				_isMouseInside = false;
				MouseLeave?.Invoke(this, EventArgs.Empty);
				return 0;
			}
			case WM_CAPTURECHANGED:
			{
				ReportButtonsReleased();
				return 0;
			}
			case WM_LBUTTONDOWN: ReportButton(hwnd, MouseButton.Left, true); return 0;
			case WM_LBUTTONUP: ReportButton(hwnd, MouseButton.Left, false); return 0;
			case WM_RBUTTONDOWN: ReportButton(hwnd, MouseButton.Right, true); return 0;
			case WM_RBUTTONUP: ReportButton(hwnd, MouseButton.Right, false); return 0;
			case WM_MBUTTONDOWN: ReportButton(hwnd, MouseButton.Middle, true); return 0;
			case WM_MBUTTONUP: ReportButton(hwnd, MouseButton.Middle, false); return 0;

			case WM_XBUTTONDOWN:
			case WM_XBUTTONUP:
			{
				ReportButton(hwnd, XButtonFrom(wParam), msg == WM_XBUTTONDOWN);
				return 1; // TRUE, so the system does not also handle the extra button
			}

			case WM_MOUSEWHEEL:
			{
				int notches = WheelNotchesFrom(wParam, ref _wheelDeltaRemainder);
				if (notches != 0)
				{
					MouseWheel?.Invoke(this, new MouseWheelEventArgs() { Value = notches });
				}
				return 0;
			}
		}
		
		return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
	}

	/// <summary>
	/// Reports one typed character, pairing a character outside the Basic Multilingual Plane from the two WM_CHAR messages Win32 delivers it in.
	/// </summary>
	private void ReportCharInput(char unit)
	{
		if (DecodeCodePoint(unit, ref _pendingHighSurrogate) is { } codePoint)
		{
			CharInput?.Invoke(this, new CharInputEventArgs() { Value = codePoint });
		}
	}

	/// <summary>
	/// Reports a cursor position, along with the entry into the client area that a position arriving after a leave implies.
	/// </summary>
	private void ReportMousePosition(HWND hwnd, int x, int y)
	{
		if (!_isMouseInside)
		{
			_isMouseInside = true;
			TrackMouseLeave(hwnd);
			MouseEnter?.Invoke(this, EventArgs.Empty);
		}

		MouseMove?.Invoke(this, new MouseMoveEventArgs() { X = x, Y = y });
	}

	/// <summary>
	/// Asks for a single <c>WM_MOUSELEAVE</c> once the cursor leaves the client area. Win32 sends that notification once per request, so it takes a fresh request on every entry.
	/// </summary>
	private static void TrackMouseLeave(HWND hwnd)
	{
		var tracking = new TRACKMOUSEEVENT()
		{
			cbSize = (uint) sizeof(TRACKMOUSEEVENT),
			dwFlags = TME.TME_LEAVE,
			hwndTrack = hwnd,
		};

		// A failure here only costs the leave notification, which has no fallback worth the complexity.
		Win32.TrackMouseEvent(&tracking);
	}

	/// <summary>
	/// Reports a mouse button going down or up, and holds mouse capture for as long as any button is down.
	/// </summary>
	private void ReportButton(HWND hwnd, MouseButton button, bool isPressed)
	{
		int flag = 1 << (int) button;

		if (isPressed)
		{
			_pressedButtons |= flag;

			if (Win32.GetCapture() != hwnd)
			{
				Win32.SetCapture(hwnd);
			}
		}
		else
		{
			_pressedButtons &= ~flag;
			if (_pressedButtons == 0)
			{
				Win32.ReleaseCapture();
			}
		}

		MouseButtonChanged?.Invoke(this, new MouseButtonChangedEventArgs() { MouseButton = button, IsPressed = isPressed });
	}

	/// <summary>
	/// Reports every button still marked as down as released, which is what losing capture calls for.
	/// </summary>
	private void ReportButtonsReleased()
	{
		if (_pressedButtons == 0)
		{
			return;
		}

		int pressed = _pressedButtons;
		_pressedButtons = 0;

		foreach (MouseButton button in Enum.GetValues<MouseButton>())
		{
			if ((pressed & (1 << (int) button)) != 0)
			{
				MouseButtonChanged?.Invoke(this, new MouseButtonChangedEventArgs() { MouseButton = button, IsPressed = false });
			}
		}
	}

	/// <summary>
	/// Dispatches messages, waiting until at least one arrives.
	/// </summary>
	public void PumpEvents() => PumpEvents(double.PositiveInfinity);

	/// <summary>
	/// Waits at most <paramref name="budgetSeconds"/> for a message, then dispatches every message that
	/// is already queued.
	/// </summary>
	/// <param name="budgetSeconds">
	/// Upper bound on how long this call spends waiting for the first message. Positive infinity waits
	/// until a message arrives; NaN and values at or below zero only take one non-blocking pass over a
	/// queue that may already be full.
	/// </param>
	/// <returns><see langword="true"/> when at least one message was dispatched.</returns>
	public bool PumpEvents(double budgetSeconds)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		
		if (_windowDestroyed || _hwnd == HWND.NULL)
		{
			return false;
		}

		double budget = NormalizeBudget(budgetSeconds);
		long startedAt = Stopwatch.GetTimestamp();

		while (true)
		{
			double remaining = budget - SecondsSince(startedAt);
			uint timeout = TimeoutFrom(remaining);

			uint wait = Win32.MsgWaitForMultipleObjectsEx(0, null, timeout, QS.QS_ALLINPUT, MWMO.MWMO_INPUTAVAILABLE);

			if (wait == WAIT.WAIT_TIMEOUT)
			{
				return false; // Budget spent with nothing to dispatch: the caller gets the frame.
			}

			if (wait != WAIT.WAIT_OBJECT_0)
			{
				throw new InvalidOperationException("Failed to wait for a window message", new Win32Exception(Marshal.GetLastWin32Error(), "MsgWaitForMultipleObjectsEx failed"));
			}

			if (DrainQueue())
			{
				return true;
			}

			if (IsClosed)
			{
				return false;
			}

			if (remaining <= 0d)
			{
				return false;
			}

			// The wait came back with nothing to dispatch: a message another thread sent with
			// SendMessage is delivered inside the wait itself and never reaches the queue, so this is
			// not the frame's turn yet. Covering the rest of the budget keeps the "wait at most the
			// budget" promise instead of handing the caller a frame it did not ask for; a caller with
			// no budget left stops here so that a stream of such messages cannot spin this loop.
		}
	}

	/// <summary>
	/// Dispatches every message already in this thread's queue.
	/// </summary>
	/// <returns><see langword="true"/> when at least one message was dispatched.</returns>
	/// <remarks>
	/// The queue is read with <c>HWND.NULL</c>, not with this window's handle: messages posted to the
	/// thread live in the same queue, and a wait that keeps noticing them while this method refuses to
	/// take them degrades into a poll that returns immediately every time.
	/// </remarks>
	private bool DrainQueue()
	{
		bool dispatched = false;
		MSG msg;
		while (Win32.PeekMessageW(&msg, HWND.NULL, 0, 0, PM.PM_REMOVE))
		{
			if (msg.message == WM_QUIT)
			{
				IsClosed = true;
				return dispatched;
			}

			Win32.TranslateMessage(&msg);
			Win32.DispatchMessageW(&msg);
			dispatched = true;
		}
		
		return dispatched;
	}

	/// <summary>
	/// Brings a caller's budget into the range a single wait accepts.
	/// </summary>
	/// <remarks>
	/// NaN and negative values (negative infinity included) mean "do not wait". They are normalized
	/// here rather than clamped, because <see cref="Math.Clamp(double, double, double)"/> maps NaN onto
	/// NaN, which is not a timeout any wait can use.
	/// </remarks>
	private static double NormalizeBudget(double budgetSeconds)
	{
		if (double.IsPositiveInfinity(budgetSeconds))
		{
			return double.PositiveInfinity;
		}

		if (double.IsNaN(budgetSeconds) || budgetSeconds <= 0d)
		{
			return 0d;
		}

		return Math.Min(budgetSeconds, MaxPumpBudgetSeconds);
	}

	/// <summary>
	/// Converts the time left of a budget into the millisecond timeout a wait takes.
	/// </summary>
	private static uint TimeoutFrom(double seconds)
	{
		if (double.IsPositiveInfinity(seconds))
		{
			return Win32.INFINITE;
		}

		return seconds <= 0d ? 0 : (uint) Math.Ceiling(seconds * 1000d);
	}

	private static double SecondsSince(long timestamp)
	{
		return (Stopwatch.GetTimestamp() - timestamp) / (double) Stopwatch.Frequency;
	}

	public void Close()
	{
		if (_windowDestroyed || _hwnd == HWND.NULL)
		{
			return;
		}

		// DestroyWindow sends WM_DESTROY before it returns, and that handler marks the window as gone.
		Win32.DestroyWindow(_hwnd);
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;

		ReleaseNativeResources();
	}

	/// <summary>
	/// Releases everything this instance owns on the native side.
	/// </summary>
	private void ReleaseNativeResources()
	{
		_logger?.LogDebug("Cleaning up Win32 native resources for Win32Window");
		if (!_windowDestroyed && _hwnd != HWND.NULL)
		{
			Win32.DestroyWindow(_hwnd);
		}

		if (_selfHandle.IsAllocated)
		{
			_selfHandle.Free();
		}
	}
}