using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SImulator.ViewModel;

/// <summary>
/// Defines a single SIGame engine time setting (in seconds) for editing.
/// </summary>
public sealed class EngineTimeSettingViewModel : INotifyPropertyChanged
{
    private readonly Func<int> _getValue;
    private readonly Action<int> _setValue;

    /// <summary>
    /// Setting name.
    /// </summary>
    public string Name { get; }

    public int Minimum { get; }

    public int Maximum { get; }

    /// <summary>
    /// Setting value. Out of range values are clamped.
    /// </summary>
    public int Value
    {
        get => _getValue();
        set
        {
            var newValue = Math.Clamp(value, Minimum, Maximum);

            if (newValue != _getValue())
            {
                _setValue(newValue);
                OnPropertyChanged();
            }
        }
    }

    /// <param name="getValue">Reads the value from the model. The model object could be replaced (e.g. on reset), so it is read every time.</param>
    /// <param name="setValue">Writes the value to the model.</param>
    public EngineTimeSettingViewModel(string name, int minimum, int maximum, Func<int> getValue, Action<int> setValue)
    {
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
        _getValue = getValue;
        _setValue = setValue;
    }

    /// <summary>
    /// Notifies the view that the model value has been changed outside.
    /// </summary>
    public void Refresh() => OnPropertyChanged(nameof(Value));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
