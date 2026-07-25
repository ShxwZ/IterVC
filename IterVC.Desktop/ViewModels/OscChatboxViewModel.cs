using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.ViewModels;

public sealed partial class OscChatboxViewModel : ViewModelBase
{
    private readonly IOscChatboxWorker _worker;
    private readonly ISettingsService _settings;
    private readonly ILogger<OscChatboxViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _persistenceLock = new();
    private Task _persistenceTask = Task.CompletedTask;
    private bool _hydrating;
    private int _stopped;

    public OscChatboxViewModel(IOscChatboxWorker worker, ISettingsService settings,
        ILogger<OscChatboxViewModel> logger)
    {
        _worker = worker;
        _settings = settings;
        _logger = logger;
    }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _template = "{title} - [{time}]";

    public async Task HydrateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _hydrating = true;
        try
        {
            Template = settings.OscTemplate;
            IsEnabled = settings.EnableOscChatbox;
            _worker.Configure(IsEnabled, Template);
            await _worker.StartAsync(cancellationToken);
        }
        finally { _hydrating = false; }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_hydrating) return;
        _worker.Configure(value, Template);
        QueuePersistence(settings => settings.EnableOscChatbox = value);
    }

    partial void OnTemplateChanged(string value)
    {
        if (_hydrating) return;
        _worker.Configure(IsEnabled, value);
        QueuePersistence(settings => settings.OscTemplate = value);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _lifetimeCancellation.Cancel();
        Task persistence;
        lock (_persistenceLock) persistence = _persistenceTask;
        try { await persistence; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish OSC settings persistence"); }
        try { await _worker.StopAsync(); }
        finally { _lifetimeCancellation.Dispose(); }
    }

    private void QueuePersistence(Action<AppSettings> mutation)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        lock (_persistenceLock)
            _persistenceTask = PersistAfterAsync(_persistenceTask, mutation);
    }

    private async Task PersistAfterAsync(Task previous, Action<AppSettings> mutation)
    {
        try { await previous; }
        catch { }
        try { await _settings.UpdateAsync(mutation, _lifetimeCancellation.Token); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not persist OSC chatbox settings"); }
    }
}
