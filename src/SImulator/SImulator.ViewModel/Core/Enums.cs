namespace SImulator.ViewModel.Core;

public enum GameMode
{
    Start,
    Moderator,
}

/// <summary>
/// Defines supported player key managers.
/// </summary>
public enum PlayerKeysModes
{
    None,
    External,
    Keyboard,
    Joystick,
    Com,
    WebNew,
    /// <summary>
    /// Quiz buttons of an ESP32 master linked over its USB serial port: the buttons show the game (QuizSet protocol).
    /// </summary>
    QuizSet,
}

/// <summary>
/// Defines players table display mode.
/// </summary>
public enum PlayersViewMode
{
    Hidden,
    Visible,
    Separate
}

/// <summary>
/// Defines a platform-independent key value.
/// </summary>
public enum GameKey { }
