using Rabotora.Core.Enumerators.Platform;
using TerraFX.Interop.Windows;
using Win32 = TerraFX.Interop.Windows.Windows;

namespace Rabotora.Core.Services.Platform;

public sealed partial class Win32Window
{
	/// <summary>Bit 24 of a key message's lParam: the key is one of the "extended" keys, which is how the right-hand Ctrl and Alt are told from the left-hand ones.</summary>
	private const long ExtendedKeyFlag = 1L << 24;

	/// <summary>Bit 30 of a key message's lParam: the key is being held down and this is a repeat.</summary>
	private const long RepeatFlag = 1L << 30;

	/// <summary>U+FFFD, reported for a surrogate that arrived without its partner.</summary>
	private const int ReplacementCharacter = 0xFFFD;

	/// <summary>How many wheel-delta units (<c>WHEEL_DELTA</c>) one wheel notch is worth.</summary>
	private const int WheelDeltaPerNotch = 120;

	/// <summary>
	/// Tells whether a key message is the auto-repeat of a key that is already down.
	/// </summary>
	private static bool IsRepeat(LPARAM lParam)
	{
		return (lParam.Value & RepeatFlag) != 0;
	}

	/// <summary>
	/// Maps a Win32 virtual key onto a <see cref="Key"/>.
	/// </summary>
	private static Key TranslateKey(WPARAM wParam, LPARAM lParam)
	{
		uint virtualKey = (uint) wParam.Value;

		// Ranges first. The blocks of Key are declared in the same order as their Win32 counterparts
		// precisely so that this arithmetic is all the mapping they need.
		if (virtualKey is >= 'A' and <= 'Z') return Key.A + (int) (virtualKey - 'A');
		if (virtualKey is >= '0' and <= '9') return Key.Digit0 + (int) (virtualKey - '0');
		if (virtualKey is >= VK.VK_F1 and <= VK.VK_F24) return Key.F1 + (int) (virtualKey - VK.VK_F1);
		if (virtualKey is >= VK.VK_NUMPAD0 and <= VK.VK_NUMPAD9) return Key.NumPad0 + (int) (virtualKey - VK.VK_NUMPAD0);

		bool extended = (lParam.Value & ExtendedKeyFlag) != 0;
		return virtualKey switch
		{
			VK.VK_SHIFT => WhichShift(lParam),
			VK.VK_CONTROL => extended ? Key.RightControl : Key.LeftControl,
			VK.VK_MENU => extended ? Key.RightAlt : Key.LeftAlt,
			VK.VK_LWIN => Key.LeftSuper,
			VK.VK_RWIN => Key.RightSuper,

			VK.VK_ESCAPE => Key.Escape,
			VK.VK_RETURN => Key.Enter,
			VK.VK_TAB => Key.Tab,
			VK.VK_BACK => Key.Backspace,
			VK.VK_SPACE => Key.Space,
			VK.VK_INSERT => Key.Insert,
			VK.VK_DELETE => Key.Delete,
			VK.VK_HOME => Key.Home,
			VK.VK_END => Key.End,
			VK.VK_PRIOR => Key.PageUp,
			VK.VK_NEXT => Key.PageDown,
			VK.VK_LEFT => Key.Left,
			VK.VK_UP => Key.Up,
			VK.VK_RIGHT => Key.Right,
			VK.VK_DOWN => Key.Down,

			VK.VK_CAPITAL => Key.CapsLock,
			VK.VK_NUMLOCK => Key.NumLock,
			VK.VK_SCROLL => Key.ScrollLock,
			VK.VK_SNAPSHOT => Key.PrintScreen,
			VK.VK_PAUSE => Key.Pause,
			VK.VK_APPS => Key.Menu,

			VK.VK_DECIMAL => Key.NumPadDecimal,
			VK.VK_DIVIDE => Key.NumPadDivide,
			VK.VK_MULTIPLY => Key.NumPadMultiply,
			VK.VK_SUBTRACT => Key.NumPadSubtract,
			VK.VK_ADD => Key.NumPadAdd,

			VK.VK_OEM_3 => Key.Grave,
			VK.VK_OEM_MINUS => Key.Minus,
			VK.VK_OEM_PLUS => Key.Equal,
			VK.VK_OEM_4 => Key.LeftBracket,
			VK.VK_OEM_6 => Key.RightBracket,
			VK.VK_OEM_5 => Key.Backslash,
			VK.VK_OEM_1 => Key.Semicolon,
			VK.VK_OEM_7 => Key.Apostrophe,
			VK.VK_OEM_COMMA => Key.Comma,
			VK.VK_OEM_PERIOD => Key.Period,
			VK.VK_OEM_2 => Key.Slash,

			_ => Key.None
		};
	}

	/// <summary>
	/// Tells which Shift key was pressed. Shift is the one modifier Win32 does not mark as extended, so
	/// the scan code has to be resolved explicitly to get the left/right distinction.
	/// </summary>
	private static Key WhichShift(LPARAM lParam)
	{
		uint scanCode = (uint) ((lParam.Value >> 16) & 0xFF);
		return Win32.MapVirtualKeyW(scanCode, Win32.MAPVK_VSC_TO_VK_EX) == VK.VK_RSHIFT ? Key.RightShift : Key.LeftShift;
	}

	/// <summary>
	/// Unpacks the <em>client</em> size a <c>WM_SIZE</c> carries. Both halves are unsigned there: a
	/// minimized window reports zeroes rather than a negative size.
	/// </summary>
	private static (int Width, int Height) SizeFrom(LPARAM lParam)
	{
		return ((int) (lParam.Value & 0xFFFF), (int) ((lParam.Value >> 16) & 0xFFFF));
	}

	/// <summary>
	/// Unpacks the cursor position a mouse message carries, in client coordinates. Signed, because the
	/// position goes negative to the left of and above the client area while a button is held.
	/// </summary>
	private static (int X, int Y) PositionFrom(LPARAM lParam)
	{
		return ((short) (lParam.Value & 0xFFFF), (short) ((lParam.Value >> 16) & 0xFFFF));
	}

	/// <summary>
	/// Unpacks which of the two extra mouse buttons an X-button message is about.
	/// </summary>
	private static MouseButton XButtonFrom(WPARAM wParam)
	{
		int xButton = (int) ((wParam.Value >> 16) & 0xFFFF); // XBUTTON1 = 1, XBUTTON2 = 2
		return xButton == 1 ? MouseButton.X1 : MouseButton.X2;
	}

	/// <summary>
	/// Unpacks the wheel movement a <c>WM_MOUSEWHEEL</c> carries, as whole notches.
	/// </summary>
	private static int WheelNotchesFrom(WPARAM wParam, ref int remainder)
	{
		remainder += (short) ((wParam.Value >> 16) & 0xFFFF);

		int notches = remainder / WheelDeltaPerNotch;
		remainder %= WheelDeltaPerNotch;
		return notches;
	}

	/// <summary>
	/// Turns one UTF-16 code unit of a <c>WM_CHAR</c> into a code point. Win32 delivers code units, so a
	/// character outside the Basic Multilingual Plane arrives as two messages and the leading surrogate
	/// has to be remembered across them.
	/// </summary>
	private static int? DecodeCodePoint(char unit, ref char? pendingHighSurrogate)
	{
		if (char.IsHighSurrogate(unit))
		{
			pendingHighSurrogate = unit;
			return null;
		}

		if (pendingHighSurrogate is { } high)
		{
			pendingHighSurrogate = null;
			// A leading surrogate followed by anything else is dropped rather than reported as half a character.
			return char.IsLowSurrogate(unit) ? char.ConvertToUtf32(high, unit) : unit;
		}

		return char.IsLowSurrogate(unit) ? ReplacementCharacter : unit;
	}
}
