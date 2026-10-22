using SImulator.ViewModel.Model;

namespace SImulator.ViewModel.Contracts;

/// <summary>
/// Controls the game process.
/// </summary>
public interface IGameActions : IDisposable
{
    void Init() { }

    void MoveNext(int delayMs = 100);

    void ShowThemes(string[] themeNames);

    void MoveBack();

    void MoveNextRound();

    void MoveBackRound();

    void AddPlayer();

    void RemovePlayerAt(int index);

    void OnRightAnswer(bool moveToAnswer);

    void IsRightAnswer();

    void IsWrongAnswer();

    void IsPassAnswer();

    void SelectPlayer(int playerIndex);

    void ConnectPlayer(PlayerInfo player) { }

    void PlayerPressed(int playerIndex, PlayerInfo player) { }

    /// <summary>
    /// Applies the player name edited by the host. May restore the previous name if the new one is rejected.
    /// </summary>
    void RenamePlayer(int playerIndex, PlayerInfo player) { }

    /// <summary>
    /// Handles media playback completion on the board.
    /// </summary>
    void OnMediaCompleted(string contentType, string contentValue) { }

    void SetPause(bool isPaused) { }
}
