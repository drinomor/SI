using SImulator.ViewModel.Core;
using SImulator.ViewModel.Model;

namespace SImulator.ViewModel.ButtonManagers;

/// <summary>
/// Listens to button managers events and provides player info to button managers.
/// </summary>
public interface IButtonManagerListener
{
    /// <summary>
    /// Button blocking time in milliseconds.
    /// </summary>
    int ButtonBlockTime { get; }

    /// <summary>
    /// Gets game players.
    /// </summary>
    IEnumerable<PlayerInfo> GamePlayers { get; }

    bool OnKeyPressed(GameKey key);

    bool OnPlayerPressed(PlayerInfo player);

    void OnPlayerPressed(string playerName) { }

    void OnPlayerPassed(string playerName) { }

    /// <summary>
    /// The player pressed PASS: declines to answer the current question.
    /// </summary>
    void OnPlayerPassed(PlayerInfo player) { }
    
    void OnPlayerAdded(string? id, string playerName) { }

    /// <summary>
    /// Adds a player for a button (through the game engine: it may appear later, see
    /// <see cref="IButtonManager.OnPlayersChanged" />).
    /// </summary>
    void AddPlayerForButton() { }

    bool TryConnectPlayer(string playerName, string connectionId) => false;

    bool TryDisconnectPlayer(string playerName) => false;

    void OnPlayerAnswered(string playerName, string answer, bool isPreliminary) { }

    void OnPlayerStake(string playerName, int stake) { }
}
