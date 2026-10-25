using SImulator.Properties;
using SImulator.ViewModel;
using SImulator.ViewModel.ButtonManagers;
using SImulator.ViewModel.Model;
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
/// Protocol: components/quizset/include/quizset.h of the quiz firmware.
/// Button number id is player number id (in the players' order) until players are bound to the buttons.
/// </remarks>
internal sealed class QuizSetButtonManager : ButtonManagerBase, IGameStateButtons
{
    private const int ProtocolVersion = 1;
    private const int MaxDeviceId = 32;
    private static readonly TimeSpan RepeatPeriod = TimeSpan.FromSeconds(1);

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

    /// <summary>
    /// Problems already shown to the host (UI thread only): a message box once, not every second.
    /// </summary>
    private readonly HashSet<string> _reported = new();

    private QuizSetButtonManager(SerialPort port, IButtonManagerListener buttonManagerListener) : base(buttonManagerListener)
    {
        _port = port;
        _port.DataReceived += Port_DataReceived;
        _repeatTimer = new Timer(_ => Repeat(), null, RepeatPeriod, RepeatPeriod);
    }

    /// <summary>
    /// Opens the port for the whole game. Returns null (the error is shown) if it cannot be opened.
    /// </summary>
    internal static QuizSetButtonManager? TryCreate(string portName, IButtonManagerListener buttonManagerListener)
    {
        // Without DTR the master treats the port as closed and drops what it writes
        var port = new SerialPort(portName, 115200) { DtrEnable = true, WriteTimeout = 500 };

        try
        {
            port.Open();
        }
        catch (Exception exc)
        {
            port.Dispose();
            ShowError(string.Format(Resources.QuizSetPortError, portName, exc.Message));
            return null;
        }

        var manager = new QuizSetButtonManager(port, buttonManagerListener);
        manager.Send($"HELLO quizset {ProtocolVersion}");
        manager.Repeat(); // dark buttons at once: in the master's free phase every press would be a false start here
        return manager;
    }

    // The port stays open for the whole game: the buttons follow the game state, not Start / Stop
    public override bool Start() => true;

    public override void Stop() { }

    public void OnGameStateChanged(ButtonsGameState state)
    {
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

    private string BuildStateLine(ButtonsGameState state)
    {
        var players = Listener.GamePlayers.ToList();
        var line = new StringBuilder($"S round={state.Question}");

        var answering = state.Answering != null ? DeviceId(state.Answering, players) : null;

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

        var outIds = state.Out.Select(player => DeviceId(player, players)).Where(id => id.HasValue).ToList();

        if (outIds.Count > 0)
        {
            line.Append(" out=").Append(string.Join(",", outIds));
        }

        // PASS: declining the question, while players may press
        line.Append(state.Phase == ButtonsPhase.Open ? " pass=1" : " pass=0");

        // The master plays a verdict once per change of its number, whatever the phase
        if (state.Verdict != null && DeviceId(state.Verdict.Player, players) is int verdictId)
        {
            line.Append($" verdict={verdictId}:{(state.Verdict.IsRight ? "correct" : "wrong")}:{state.Verdict.Number}");
        }

        return line.ToString();
    }

    private static int? DeviceId(PlayerInfo player, IList<PlayerInfo> players)
    {
        if (player.Id != null && player.Id.StartsWith(PlayerIdPrefix) && int.TryParse(player.Id[PlayerIdPrefix.Length..], out var id))
        {
            return id;
        }

        var index = players.IndexOf(player);
        return index >= 0 && index < MaxDeviceId ? index + 1 : null;
    }

    private PlayerInfo? FindPlayer(int deviceId)
    {
        var players = Listener.GamePlayers.ToList();
        var bound = players.FirstOrDefault(player => player.Id == PlayerIdPrefix + deviceId);

        if (bound != null)
        {
            return bound;
        }

        // By order, unless that player is bound to another button
        var index = deviceId - 1;

        return index >= 0 && index < players.Count && players[index].Id?.StartsWith(PlayerIdPrefix) != true
            ? players[index]
            : null;
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

            case "ERR":
                Trace.TraceWarning("QuizSet: {0}", line);
                break;

            default:
                break; // PLAYER lines and replies: not used yet
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
