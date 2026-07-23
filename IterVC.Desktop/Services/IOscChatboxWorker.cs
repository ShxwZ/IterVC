namespace IterVC.Desktop.Services;

public interface IOscChatboxWorker
{
    void Configure(bool enabled, string template);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
