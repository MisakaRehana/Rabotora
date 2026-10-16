namespace Rabotora.Core.Enumerators.Platform;

/// <summary>
/// A physical key, named after what is written on it rather than after any platform's key code, so a
/// backend translates its own codes into these values instead of the other way around.
/// </summary>
public enum Key
{
	/// <summary>A key this enumeration does not name. Backends report it instead of guessing.</summary>
	None = 0,

	// Letters
	A, B, C, D, E, F, G, H, I, J, K, L, M,
	N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

	// Digits of the top row
	Digit0, Digit1, Digit2, Digit3, Digit4,
	Digit5, Digit6, Digit7, Digit8, Digit9,

	// Function keys
	F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
	F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,

	// Numeric keypad digits
	NumPad0, NumPad1, NumPad2, NumPad3, NumPad4,
	NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,

	// Numeric keypad operators
	NumPadDecimal,
	NumPadDivide,
	NumPadMultiply,
	NumPadSubtract,
	NumPadAdd,

	// Punctuation, by US layout position
	Grave,        // ` ~
	Minus,        // - _
	Equal,        // = +
	LeftBracket,  // [ {
	RightBracket, // ] }
	Backslash,    // \ |
	Semicolon,    // ; :
	Apostrophe,   // ' "
	Comma,        // , <
	Period,       // . >
	Slash,        // / ?

	// Whitespace, editing and navigation
	Escape,
	Enter,
	Tab,
	Backspace,
	Space,
	Insert,
	Delete,
	Home,
	End,
	PageUp,
	PageDown,
	Left,
	Up,
	Right,
	Down,

	// Modifiers. Side-specific because games need to tell the two halves apart.
	LeftShift,
	RightShift,
	LeftControl,
	RightControl,
	LeftAlt,
	RightAlt,
	LeftSuper,
	RightSuper,

	// Locks and system keys
	CapsLock,
	NumLock,
	ScrollLock,
	PrintScreen,
	Pause,
	Menu // the context menu key
}
