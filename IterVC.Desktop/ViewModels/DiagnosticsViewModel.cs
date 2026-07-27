using CommunityToolkit.Mvvm.Input;
using IterVC.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.ViewModels;

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly DiagnosticsService _diagnostics;
    private readonly ILogger<DiagnosticsViewModel> _logger;

    internal DiagnosticsViewModel(DiagnosticsService diagnostics, ILogger<DiagnosticsViewModel> logger)
    {
        _diagnostics = diagnostics;
        _logger = logger;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try { _diagnostics.OpenLogFolder(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Could not open the diagnostic log folder"); }
    }
}
