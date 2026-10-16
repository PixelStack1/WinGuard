using System.Windows.Input;

namespace WinGuard.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)
    {
        try { _execute(p); }
        catch (Exception ex) { Services.AppLog.Error("Command failed", ex); System.Windows.MessageBox.Show(ex.Message, Localization.Loc.T("UnexpectedError")); }
    }
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;
    public AsyncRelayCommand(Func<object?, Task> execute, Func<bool>? canExecute = null)
    { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? p) => !_running && (_canExecute?.Invoke() ?? true);
    public async void Execute(object? p)
    {
        _running = true; CommandManager.InvalidateRequerySuggested();
        try { await _execute(p); }
        catch (Exception ex) { Services.AppLog.Error("Async command failed", ex); System.Windows.MessageBox.Show(ex.Message, Localization.Loc.T("UnexpectedError")); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
