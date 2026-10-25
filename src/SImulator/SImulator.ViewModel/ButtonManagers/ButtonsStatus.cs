using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SImulator.ViewModel.ButtonManagers;

/// <summary>
/// What the host sees about the buttons in the status line (QuizSet). Filled by the button manager on the UI thread.
/// </summary>
public sealed class ButtonsStatus : INotifyPropertyChanged
{
    private bool _isLinked;

    /// <summary>
    /// The buttons' master answers and is in the buttons' mode.
    /// </summary>
    public bool IsLinked { get => _isLinked; set => Set(ref _isLinked, value); }

    private string _linkText = "";

    /// <summary>
    /// "QuizSet · COM8 · linked" and the like.
    /// </summary>
    public string LinkText { get => _linkText; set => Set(ref _linkText, value); }

    private string _buttonsText = "";

    /// <summary>
    /// How many buttons are connected and offline.
    /// </summary>
    public string ButtonsText { get => _buttonsText; set => Set(ref _buttonsText, value); }

    private int _newCount;

    /// <summary>
    /// Connected buttons without a player: the host may take them into the game.
    /// </summary>
    public int NewCount { get => _newCount; set { if (Set(ref _newCount, value)) OnPropertyChanged(nameof(HasNew)); } }

    public bool HasNew => _newCount > 0;

    private string _newButtonsText = "";

    /// <summary>
    /// "New button 4 «Dmitry»".
    /// </summary>
    public string NewButtonsText { get => _newButtonsText; set => Set(ref _newButtonsText, value); }

    private string _stateText = "";

    /// <summary>
    /// What the buttons show now: open, answering, dark, paused.
    /// </summary>
    public string StateText { get => _stateText; set => Set(ref _stateText, value); }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
