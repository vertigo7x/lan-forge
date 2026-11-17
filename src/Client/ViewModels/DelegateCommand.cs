using System.Windows.Input;

namespace LanForge.Client.ViewModels;

/// <summary>ICommand mínimo sin dependencias de MVVM.</summary>
public sealed class DelegateCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _busy;

    public DelegateCommand(Func<Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_busy;

    public async void Execute(object? parameter)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
