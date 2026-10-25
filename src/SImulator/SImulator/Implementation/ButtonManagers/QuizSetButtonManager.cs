using SImulator.Properties;
using SImulator.ViewModel;
using SImulator.ViewModel.ButtonManagers;
using SImulator.ViewModel.Model;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using Utils;

namespace SImulator.Implementation.ButtonManagers;

/// <summary>
/// Provides the buttons of an ESP32 quiz master linked over its USB serial port (QuizSet protocol).
/// </summary>
/// <remarks>
/// The buttons show the game themselves: the game state goes to the master as an "S ..." line on every change and
/// again every second (the master treats a silence of 3 s as a lost link). The master sends every press back
/// ("PRESS id" / "PASS id") in the order the presses came; who was first is decided here, as for other buttons.
/// Players are bound to buttons by the button number (<see cref="PlayerIdPrefix" /> in <see cref="PlayerInfo.Id" />):
/// the buttons connected at the game start, and later ones when the host takes the players from the buttons.
/// Names go both ways: the master's name (if set) is taken on binding and when changed there, a name changed here goes
/// to the master.
/// Protocol: components/quizset/include/quizset.h of the quiz firmware.
/// </remarks>
internal sealed class QuizSetButtonManager : ButtonManagerBase, IGameStateButtons, IButtonPlayersSource
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan RepeatPeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Player identifier prefix of a player bound to a button: "quizset:3" is button 3.
    /// </summary>
    internal const string PlayerIdPrefix = "quizset:";

    private readonly SerialPort _port;
    private readonly Timer _repeatTimer;

    /// <summary>
    /// Guards writing to the port (UI thread and the repeat timer) and <see cref="_stateLine" />.
    /// </summary>
    private readonly object _sendLock = new();

    /// <summary>
    /// The latest state line, repeated every <see cref="RepeatPeriod" />.
    /// </summary>
    private string _stateLine = "S round=0 phase=idle";

    /// <summary>
    /// Bytes of the line being received. DataReceived handler only (it never runs twice at once).
    /// </summary>
    private readonly List<byte> _received = new();

    // The fields below are used on the UI thread only (game calls and HandleLine)

    /// <summary>
    /// Problems already shown to the host: a message box once, not every second.
    /// </summary>
    private readonly HashSet<string> _reported = new();

    private sealed record ButtonInfo(bool IsOn, string Name);

    /// <summary>
    /// The master's registered buttons by number, from "PLAYER ..." lines.
    /// </summary>
    private readonly Dictionary<int, ButtonInfo> _buttons = new();

    /// <summary>
    /// Buttons taken into the game (connected at the latest sync): each one gets a player.
    /// </summary>
    private readonly SortedSet<int> _accepted = new();

    /// <summary>
    /// "PLAYERS?" was sent for a sync: the answer's end takes the connected buttons.
    /// </summary>
    private bool _syncListPending;

    /// <summary>
    /// A sync may add players until every accepted button has one.
    /// </summary>
    private bool _syncAdding;

    /// <summary>
    /// Players count when a player was requested: one request at a time (the engine adds players asynchronously).
    /// </summary>
    private int _playersCountAtAdd = -1;

    private ButtonsGameState? _lastState;

    /// <summary>
    /// Bound players whose renames go to the master.
    /// </summary>
    private readonly HashSet<PlayerInfo> _watched = new();

    private QuizSetButtonManager(SerialPort port, IButtonManagerListener buttonManagerListener) : base(buttonManagerListener)
    {
        _port = port;
        _port.DataReceived += Port_DataReceived;
        _repeatTimer = new Timer(_ => Repeat(), null, RepeatPeriod, RepeatPeriod);
    }

    /// <summary>
    /// Opens the port for the whole game and takes the connected buttons as players. Returns null (the error is shown)
    /// if the port cannot be opened.
    /// </summary>
    internal static QuizSetButtonManager? TryCreate(string portName, IButtonManagerListener buttonManagerListener)
    {
        var port = OpenPort(portName);

        if (port == null)
        {
            return null;
        }

        var manager = new QuizSetButtonManager(port, buttonManagerListener);
        manager.Send($"HELLO quizset {ProtocolVersion}");
        manager.Repeat(); // dark buttons at once: in the master's free phase every press would be a false start here
        manager.SyncPlayers();
        return manager;
    }

    /// <summary>
    /// Settings, before the game: how many buttons are connected now. Null if the master does not answer (shown).
    /// </summary>
    internal static async Task<int?> CountConnectedButtonsAsync(string portName)
    {
        using var port = OpenPort(portName);

        if (port == null)
        {
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                port.ReadTimeout = (int)QueryTimeout.TotalMilliseconds;
                port.NewLine = "\n";
                port.Encoding = Encoding.UTF8;
                port.Write("PLAYERS?\n");
                var count = 0;

                while (true)
                {
                    var line = port.ReadLine().TrimEnd('\r');

                    if (line == "PLAYERS END")
                    {
                        return count;
                    }

                    if (TryParsePlayer(line, out _, out var button) && button is { IsOn: true })
                    {
                        count++;
                    }
                }
            });
        }
        catch (Exception exc) when (exc is TimeoutException or InvalidOperationException or IOException)
        {
            ShowError(string.Format(Resources.QuizSetNoAnswer, portName));
            return null;
        }
    }

    private static SerialPort? OpenPort(string portName)
    {
        // Without DTR the master treats the port as closed and drops what it writes
        var port = new SerialPort(portName, 115200) { DtrEnable = true, WriteTimeout = 500 };

        try
        {
            port.Open();
            return port;
        }
        catch (Exception exc)
        {
            port.Dispose();
            ShowError(string.Format(Resources.QuizSetPortError, portName, exc.Message));
            return null;
        }
    }

    // The port stays open for the whole game: the buttons follow the game state, not Start / Stop
    public override bool Start() => true;

    public override void Stop() { }

    public void OnGameStateChanged(ButtonsGameState state)
    {
        _lastState = state;
        var line = BuildStateLine(state);

        lock (_sendLock)
        {
            if (line == _stateLine)
            {
                return;
            }

            _stateLine = line;
        }

        Send(line);
    }

    /// <summary>
    /// The players have changed (added, removed, or all of them created anew by the engine): bind the accepted buttons
    /// again.
    /// </summary>
    public override void OnPlayersChanged()
    {
        var players = Listener.GamePlayers.ToHashSet();

        foreach (var gone in _watched.Where(player => !players.Contains(player)).ToList())
        {
            gone.PropertyChanged -= Player_PropertyChanged;
            _watched.Remove(gone);
        }

        BindAcceptedButtons();
    }

    public void SyncPlayers()
    {
        _syncListPending = true;
        Send("PLAYERS?"); // a fresh list: the answer's end continues in HandleLine
    }

    public void IdentifyPlayer(PlayerInfo player)
    {
        if (DeviceId(player) is int id)
        {
            Send($"IDENT {id}");
        }
        else
        {
            ShowError(string.Format(Resources.QuizSetPlayerWithoutButton, player.Name));
        }
    }

    private string BuildStateLine(ButtonsGameState state)
    {
        var line = new StringBuilder($"S round={state.Question}");
        var answering = state.Answering != null ? DeviceId(state.Answering) : null;

        switch (state.Phase)
        {
            case ButtonsPhase.Open:
                line.Append(" phase=open");
                break;

            case ButtonsPhase.Locked when answering.HasValue:
                line.Append($" phase=locked locked={answering}");
                break;

            default: // also a player without a button answering
                line.Append(" phase=idle");
                break;
        }

        var outIds = state.Out.Select(DeviceId).Where(id => id.HasValue).ToList();

        if (outIds.Count > 0)
        {
            line.Append(" out=").Append(string.Join(",", outIds));
        }

        // PASS: declining the question, while players may press
        line.Append(state.Phase == ButtonsPhase.Open ? " pass=1" : " pass=0");

        // The master plays a verdict once per change of its number, whatever the phase
        if (state.Verdict != null && DeviceId(state.Verdict.Player) is int verdictId)
        {
            line.Append($" verdict={verdictId}:{(state.Verdict.IsRight ? "correct" : "wrong")}:{state.Verdict.Number}");
        }

        return line.ToString();
    }

    private static int? DeviceId(PlayerInfo player) =>
        player.Id != null && player.Id.StartsWith(PlayerIdPrefix) && int.TryParse(player.Id[PlayerIdPrefix.Length..], out var id)
            ? id
            : null;

    private static bool IsBound(PlayerInfo player) => DeviceId(player).HasValue;

    private PlayerInfo? FindPlayer(int deviceId) =>
        Listener.GamePlayers.FirstOrDefault(player => player.Id == PlayerIdPrefix + deviceId);

    /// <summary>
    /// Gives every accepted button without a player one: an unbound player with the button's name, any unbound
    /// player, or (during a sync) a new one.
    /// </summary>
    private void BindAcceptedButtons()
    {
        var players = Listener.GamePlayers.ToList();
        var changed = false;

        foreach (var id in _accepted)
        {
            if (FindPlayer(id) != null)
            {
                continue;
            }

            var name = _buttons.TryGetValue(id, out var button) ? button.Name : "";

            var player = players.FirstOrDefault(p => !IsBound(p) && name.Length > 0 && p.Name == name)
                ?? players.FirstOrDefault(p => !IsBound(p));

            if (player == null)
            {
                if (_syncAdding && players.Count != _playersCountAtAdd)
                {
                    _playersCountAtAdd = players.Count;
                    Listener.AddPlayerForButton(); // OnPlayersChanged continues when the player is there
                }

                break;
            }

            player.Id = PlayerIdPrefix + id;
            changed = true;

            if (_watched.Add(player))
            {
                player.PropertyChanged += Player_PropertyChanged;
            }

            // The master's name, when it has one; otherwise the name given here stays
            if (name.Length > 0 && player.Name != name)
            {
                player.Name = name;
            }
        }

        if (_accepted.All(id => FindPlayer(id) != null))
        {
            _syncAdding = false;
        }

        if (changed && _lastState != null)
        {
            OnGameStateChanged(_lastState); // the buttons of the new players
        }
    }

    /// <summary>
    /// A bound player renamed here (by the host, or by the game engine restoring a refused name): tell the master.
    /// </summary>
    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerInfo.Name) || sender is not PlayerInfo player || DeviceId(player) is not int id)
        {
            return;
        }

        // Not back: a name that came from the master is already the master's
        if (_buttons.TryGetValue(id, out var button) && button.Name == player.Name)
        {
            return;
        }

        Send($"NAME {id} {player.Name}");
    }

    /// <summary>
    /// "PLAYER id on|off [name]" or "PLAYER id removed"; removed buttons come as null.
    /// </summary>
    private static bool TryParsePlayer(string line, out int id, out ButtonInfo? button)
    {
        button = null;
        var parts = line.Split(' ', 4);

        if (parts.Length < 3 || parts[0] != "PLAYER" || !int.TryParse(parts[1], out id))
        {
            id = 0;
            return false;
        }

        if (parts[2] != "removed")
        {
            button = new ButtonInfo(parts[2] == "on", parts.Length == 4 ? parts[3] : "");
        }

        return true;
    }

    private void OnButtonChanged(int id, ButtonInfo? button)
    {
        if (button == null)
        {
            _buttons.Remove(id); // the player stays, with the scores; no presses come for it any more
            return;
        }

        _buttons[id] = button;

        // A name set on the master (its web page) comes here; an empty one does not clear the name here
        if (button.Name.Length > 0 && FindPlayer(id) is PlayerInfo player && player.Name != button.Name)
        {
            player.Name = button.Name;
        }
    }

    private void OnPlayersListEnd()
    {
        if (!_syncListPending)
        {
            return;
        }

        _syncListPending = false;

        // Only connected buttons become players; those already taken stay taken
        foreach (var (id, button) in _buttons)
        {
            if (button.IsOn)
            {
                _accepted.Add(id);
            }
        }

        _syncAdding = true;
        _playersCountAtAdd = -1;
        BindAcceptedButtons();
    }

    private void Repeat()
    {
        string line;

        lock (_sendLock)
        {
            line = _stateLine;
        }

        Send(line);
    }

    private void Send(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");

        lock (_sendLock)
        {
            try
            {
                _port.Write(bytes, 0, bytes.Length);
            }
            catch (Exception exc) when (exc is InvalidOperationException or IOException or TimeoutException)
            {
                // The master was unplugged or reset: its port comes back by itself, the next repetition gets through
                Trace.TraceWarning("QuizSet: {0}: {1}", line, exc.Message);
            }
        }
    }

    private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        byte[] chunk;

        try
        {
            chunk = new byte[_port.BytesToRead];
            var count = _port.Read(chunk, 0, chunk.Length);
            Array.Resize(ref chunk, count);
        }
        catch (Exception exc) when (exc is InvalidOperationException or IOException or TimeoutException)
        {
            return;
        }

        // Bytes, not characters: a UTF-8 letter of a name may be split between two reads
        foreach (var b in chunk)
        {
            if (b != (byte)'\n')
            {
                _received.Add(b);
                continue;
            }

            var line = Encoding.UTF8.GetString(_received.ToArray()).TrimEnd('\r');
            _received.Clear();

            if (line.Length > 0)
            {
                UI.Execute(() => HandleLine(line), exc => ShowError(exc.Message));
            }
        }
    }

    /// <summary>
    /// Handles a line from the master. UI thread: the game is touched only there.
    /// </summary>
    private void HandleLine(string line)
    {
        if (TryParsePlayer(line, out var buttonId, out var button))
        {
            OnButtonChanged(buttonId, button);
            return;
        }

        var parts = line.Split(' ');

        switch (parts[0])
        {
            case "PRESS" when parts.Length == 2 && int.TryParse(parts[1], out var id):
                {
                    var player = FindPlayer(id);

                    if (player != null)
                    {
                        Listener.OnPlayerPressed(player);
                    }

                    break;
                }

            case "PASS" when parts.Length == 2 && int.TryParse(parts[1], out var id):
                {
                    var player = FindPlayer(id);

                    if (player != null)
                    {
                        Listener.OnPlayerPassed(player);
                    }

                    break;
                }

            case "PLAYERS" when line == "PLAYERS END":
                OnPlayersListEnd();
                break;

            case "HELLO":
                // HELLO quizset <version> <firmware> active=<0|1>
                if (parts.Length < 3 || parts[2] != ProtocolVersion.ToString())
                {
                    ReportOnce(string.Format(Resources.QuizSetProtocolMismatch, line, ProtocolVersion));
                }
                else if (parts.Contains("active=0"))
                {
                    ReportOnce(Resources.QuizSetNotActive);
                }

                break;

            case "ERR" when line.StartsWith("ERR S master not in QuizSet mode"):
                ReportOnce(Resources.QuizSetNotActive);
                break;

            case "ERR" when parts.Length > 3 && parts[1] == "NAME" && line.Contains("too long"):
                ShowError(Resources.QuizSetNameTooLong);
                break;

            case "ERR":
                Trace.TraceWarning("QuizSet: {0}", line);
                break;

            default:
                break; // OK replies
        }
    }

    private void ReportOnce(string message)
    {
        if (_reported.Add(message))
        {
            ShowError(message);
        }
    }

    public override ValueTask DisposeAsync()
    {
        _repeatTimer.Dispose();
        _port.DataReceived -= Port_DataReceived;

        foreach (var player in _watched)
        {
            player.PropertyChanged -= Player_PropertyChanged;
        }

        _watched.Clear();
        Send("S round=0 phase=idle"); // the buttons go dark at once, not after the master's 3 s timeout
        _port.Dispose();
        return new ValueTask();
    }

    private static void ShowError(string error) => UI.Execute(
        () => System.Windows.MessageBox.Show(
            error,
            MainViewModel.ProductName,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning),
        exc => Trace.TraceError(exc.Message));
}
