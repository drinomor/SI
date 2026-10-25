using SICore;
using SIData;
using SImulator.ViewModel.Contracts;
using SImulator.ViewModel.Model;
using System.Diagnostics;
using Utils.Timers;

namespace SImulator.ViewModel.Services;

/// <summary>
/// Provides new version of game actions.
/// </summary>
internal sealed class NewGameActions : IGameActions, ITaskRunHandler<Model.Tasks>
{
    private readonly PersonActions _actions;
    private readonly PersonState _state;
    private readonly Func<string, bool, GameRole, string?, AuthenticationResult> _join;
    private readonly Func<Message, ValueTask> _onMessageReceivedAsync;
    private readonly Func<int, string, ValueTask<bool>> _renamePlayerAsync;
    private readonly TaskRunner<Model.Tasks> _taskRunner;

    public NewGameActions(
        PersonActions viewerActions,
        PersonState state,
        Func<string, bool, GameRole, string?, AuthenticationResult> join,
        Func<Message, ValueTask> onMessageReceivedAsync,
        Func<int, string, ValueTask<bool>> renamePlayerAsync)
    {
        _actions = viewerActions;
        _state = state;
        _join = join;
        _onMessageReceivedAsync = onMessageReceivedAsync;
        _renamePlayerAsync = renamePlayerAsync;
        _taskRunner = new TaskRunner<Model.Tasks>(this);
    }

    public void ExecuteTask(Model.Tasks taskId, int arg)
    {
        _taskRunner.ScheduleExecution(Model.Tasks.NoTask, 0, runTimer: false);

        switch (taskId)
        {
            case Model.Tasks.MoveNext:
                _actions.Move();
                break;
        }
    }

    public void Init() => _actions.GetInfo();

    public void OnRightAnswer(bool moveToAnswer)
    {
        throw new NotImplementedException();
    }

    public void MoveBack() => _actions.Move(MoveDirections.Back);

    public void MoveBackRound() => _actions.Move(MoveDirections.RoundBack);

    public void MoveNext(int delayMs = 100)
    {
        if (_state.Stage == GameStage.Before)
        {
            _actions.Start();
        }

        _taskRunner.ScheduleExecution(Model.Tasks.MoveNext, delayMs / 100);
    }

    public void ShowThemes(string[] themeNames)
    {

    }

    public void MoveNextRound() => _actions.Move(MoveDirections.RoundNext);

    public void AddPlayer() => _actions.AddTable();

    public void RemovePlayerAt(int index) => _actions.RemoveTable(index);

    public void IsRightAnswer() => _actions.IsRight(true);

    public void IsWrongAnswer() => _actions.IsRight(false);

    // Zero factor is treated by the engine as a pass: no penalty, the player cannot press again
    public void IsPassAnswer() => _actions.IsRight(false, 0.0);

    public void SelectPlayer(int playerIndex) => _actions.SelectPlayer(playerIndex);

    public void ConnectPlayer(PlayerInfo player) => _join(player.Name, false, GameRole.Player, null);

    public async void PlayerPressed(int playerIndex, PlayerInfo player)
    {
        // The engine identifies the sender by its own player name.
        // The host can rename players locally, so the engine name is taken by index
        var players = _state.Players;

        if (playerIndex < 0 || playerIndex >= players.Count)
        {
            return;
        }

        var engineName = players[playerIndex].Name;
        await _onMessageReceivedAsync(new Message(Messages.I, engineName));
    }

    public async void PlayerPassed(int playerIndex, PlayerInfo player)
    {
        // Like a press: the engine takes the player by its own name. It answers with Messages.Pass for the player
        // (GameController.OnPersonPass) and moves to the answer when nobody can press any more
        var players = _state.Players;

        if (playerIndex < 0 || playerIndex >= players.Count)
        {
            return;
        }

        await _onMessageReceivedAsync(new Message(Messages.Pass, players[playerIndex].Name));
    }

    public async void RenamePlayer(int playerIndex, PlayerInfo player)
    {
        try
        {
            if (await _renamePlayerAsync(playerIndex, player.Name))
            {
                return;
            }

            // The engine has rejected the name (empty or occupied): restore the engine one.
            // This raises the rename again, but with the current name it is a no-op
            var players = _state.Players;

            if (playerIndex < players.Count)
            {
                player.Name = players[playerIndex].Name;
            }
        }
        catch (Exception exc)
        {
            // async void: an unhandled exception here would crash the application
            Trace.TraceError($"Player rename error: {exc}");
        }
    }

    public async void OnMediaCompleted(string contentType, string contentValue)
    {
        try
        {
            // The engine waits for completions from at least half of the connected humans (host and players).
            // Players have no own boards, so SImulator reports the completion for everybody.
            // Repeated or unknown senders are ignored by the engine
            var text = string.Join(Message.ArgsSeparator, Messages.MediaCompleted, contentType, contentValue);

            // Snapshot: the list may be replaced by the engine thread while we are awaiting
            var senders = _state.Players.Select(player => player.Name).Prepend(_state.ShowMan.Name).ToArray();

            foreach (var sender in senders)
            {
                await _onMessageReceivedAsync(new Message(text, sender));
            }
        }
        catch (Exception exc)
        {
            // async void: an unhandled exception here would crash the application
            Trace.TraceError($"Media completion error: {exc}");
        }
    }

    public void SetPause(bool isPaused) => _actions.Pause(isPaused);

    public void Dispose()
    {

    }
}
