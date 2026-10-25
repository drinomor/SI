using SImulator.ViewModel.Model;

namespace SImulator.ViewModel.ButtonManagers;

/// <summary>
/// Defines what the players' buttons should show.
/// </summary>
public enum ButtonsPhase
{
    /// <summary>
    /// No pressing now: the buttons are dark and do not send presses.
    /// </summary>
    Idle,

    /// <summary>
    /// Players may press (except <see cref="ButtonsGameState.Out" />).
    /// </summary>
    Open,

    /// <summary>
    /// A player is answering (<see cref="ButtonsGameState.Answering" />).
    /// </summary>
    Locked,
}

/// <summary>
/// The host's latest verdict on an answer: the player's button shows it once (green / red).
/// </summary>
/// <param name="Player">The player whose answer was judged.</param>
/// <param name="IsRight">Right or wrong.</param>
/// <param name="Number">Changes with every verdict: a repeated state does not show it again.</param>
public sealed record ButtonsVerdict(PlayerInfo Player, bool IsRight, int Number);

/// <summary>
/// Game state as the players' buttons show it.
/// </summary>
/// <param name="Question">Changes with every question: the buttons start the question over.</param>
/// <param name="Phase">What the buttons do now.</param>
/// <param name="Answering">The answering player in <see cref="ButtonsPhase.Locked" />.</param>
/// <param name="Out">Players that may not press any more in this question.</param>
/// <param name="Verdict">The latest verdict in this question, if any.</param>
public sealed record ButtonsGameState(
    int Question,
    ButtonsPhase Phase,
    PlayerInfo? Answering,
    IReadOnlyList<PlayerInfo> Out,
    ButtonsVerdict? Verdict);

/// <summary>
/// Button managers whose buttons show the game themselves (QuizSet): the game tells them its state on every change.
/// </summary>
public interface IGameStateButtons
{
    /// <summary>
    /// Handles a new game state. Called on the UI thread; the same state may come again.
    /// </summary>
    void OnGameStateChanged(ButtonsGameState state);
}
