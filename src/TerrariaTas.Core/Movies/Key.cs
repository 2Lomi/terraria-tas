namespace TerrariaTas.Core.Movies;

/// <summary>
/// Keyboard keys usable in movies. Names and values match XNA's <c>Keys</c> enum
/// (which are Windows virtual-key codes), so the game layer converts with a plain cast.
/// </summary>
public enum Key : byte
{
    None = 0,
    Back = 8, Tab = 9, Enter = 13, Pause = 19, CapsLock = 20, Escape = 27, Space = 32,
    PageUp = 33, PageDown = 34, End = 35, Home = 36,
    Left = 37, Up = 38, Right = 39, Down = 40,
    Insert = 45, Delete = 46,
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    NumPad0 = 96, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Multiply = 106, Add, Separator, Subtract, Decimal, Divide,
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    NumLock = 144, Scroll = 145,
    LeftShift = 160, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    OemSemicolon = 186, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion, OemTilde,
    OemOpenBrackets = 219, OemPipe, OemCloseBrackets, OemQuotes, Oem8,
    OemBackslash = 226,
}
