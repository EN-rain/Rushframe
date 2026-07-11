using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rushframe.Domain;

public sealed class CampaignTask : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
