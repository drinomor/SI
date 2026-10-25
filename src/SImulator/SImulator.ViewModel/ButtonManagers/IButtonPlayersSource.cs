using SImulator.ViewModel.Model;

namespace SImulator.ViewModel.ButtonManagers;

/// <summary>
/// Button managers whose buttons know their players (QuizSet master): the players can be taken from the buttons.
/// </summary>
public interface IButtonPlayersSource
{
    /// <summary>
    /// The link and the buttons, for the host's status line.
    /// </summary>
    ButtonsStatus Status { get; }

    /// <summary>
    /// Binds every connected button that has no player yet: to an unbound player (one with the button's name first),
    /// or to a new player. Only adds: players without a button stay. The host calls it between questions.
    /// </summary>
    void SyncPlayers();

    /// <summary>
    /// The player's button shows its identification effect (so the host sees whose button it is).
    /// </summary>
    void IdentifyPlayer(PlayerInfo player);
}
