using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;

namespace IterVC.Linux;

/// <summary>
/// Linux's desktop shell follows the original IterVC.Desktop/MainWindow layout:
/// left application cards, right tabbed configuration, compact status pills,
/// shared semantic dark palette and an audio/status footer.
/// The controls are the SAME live controls used by the PipeWire backend.
/// No Windows-only view models or WASAPI services are instantiated here.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly StackPanel _appCards = new() { Spacing = 6 };
    private readonly Button _routingPill = new();
    private readonly Button _microphonePill = new();
    private readonly Button _applicationsPill = new();
    private readonly TextBlock _appListHelp = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _languageSelector = new() { Width = 104 };
    private readonly Slider _gateAttack = new() { Minimum = 1, Maximum = 1000 };
    private readonly Slider _gateRelease = new() { Minimum = 1, Maximum = 2000 };
    private readonly TextBlock _dependencies = new() { TextWrapping = TextWrapping.Wrap };
    private readonly IterVC.UI.CompactAudioMeter _micInputMeter = new();
    private readonly IterVC.UI.CompactAudioMeter _micOutputMeter = new();
    private readonly IterVC.UI.CompactAudioMeter _gateOutputMeter = new();
    private readonly Button _calibrateGate = new();
    private double _lastMicInputDb = -80.0;
    private bool _gateCalibrating;

    private static IBrush C(string name) => LinuxTheme.Brush(name);

    private static TextBlock Label(string text, double size = 12, string color = "TextSecondaryBrush",
        bool bold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = C(color),
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
    };

    private static Button ActionButton(string text, bool primary = false) => new()
    {
        Content = text,
        Height = 34,
        Padding = new Thickness(16, 0),
        CornerRadius = new CornerRadius(6),
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = primary ? C("TextStrongBrush") : C("TextSecondaryBrush"),
        Background = C(primary ? "AccentBrush" : "SurfaceRaisedBrush"),
        BorderBrush = primary ? C("AccentBrush") : C("BorderSubtleBrush"),
        BorderThickness = new Thickness(1)
    };

    private static Button StatusPill(Button button) {
        button.Height = 30;
        button.Padding = new Thickness(13, 0);
        button.CornerRadius = new CornerRadius(15);
        button.FontSize = 11;
        button.FontWeight = FontWeight.SemiBold;
        button.BorderThickness = new Thickness(1);
        return button;
    }

    private static StackPanel Vertical(params Control[] elements)
    {
        var stack = new StackPanel { Spacing = 11 };
        foreach (var element in elements) stack.Children.Add(element);
        return stack;
    }

    private static Control SliderField(string title, Slider slider, string? help = null)
    {
        slider.HorizontalAlignment = HorizontalAlignment.Stretch;
        slider.Width = double.NaN;
        slider.MinWidth = 160;
        var panel = Vertical(Label(title, 11, "TextSecondaryBrush", true), slider);
        panel.Spacing = 4;
        if (!string.IsNullOrEmpty(help))
            panel.Children.Add(Label(help, 10, "TextSubtleBrush"));
        return panel;
    }

    private static Control ScrollableTab(Control content) => new ScrollViewer
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static void StyleMicMeter(IterVC.UI.CompactAudioMeter meter)
    {
        meter.Height = 7;
        meter.TrackBrush = C("SurfaceEmphasisBrush");
        meter.SafeLevelBrush = C("SuccessBrush");
        meter.WarningLevelBrush = C("WarningBrush");
        meter.CriticalLevelBrush = C("DangerBrush");
        meter.PeakBrush = C("TextStrongBrush");
        meter.ThresholdBrush = C("WarningBrush");
    }

    private async Task CalibrateGateAsync()
    {
        if (!_running || _gateCalibrating)
        {
            if (!_running)
                _status.Text = L("Inicia primero el micrófono para calibrar el ruido ambiente.",
                    "Start microphone routing before calibrating ambient noise.");
            return;
        }
        _gateCalibrating = true;
        _calibrateGate.IsEnabled = false;
        _status.Text = L("Midiendo el ruido ambiente del micrófono durante dos segundos…",
            "Measuring microphone ambient noise for two seconds…");
        try
        {
            var maximum = -80.0;
            // The sampled level is measured upstream of the gate and before
            // the selected applications are added to the virtual microphone.
            for (var sample = 0; sample < 40 && _running && !_closing; sample++)
            {
                maximum = Math.Max(maximum, _lastMicInputDb);
                await Task.Delay(50);
            }
            if (_running && !_closing)
            {
                _gateThreshold.Value = Math.Clamp(maximum + 6.0, -75.0, -15.0);
                SavePreferences();
                _status.Text = L("Calibración aplicada al micrófono mientras está en marcha.",
                    "Calibration applied to the microphone while routing.");
            }
        }
        finally
        {
            _gateCalibrating = false;
            _calibrateGate.IsEnabled = true;
        }
    }

    private Control BuildDesktopLayout()
    {
        Width = 1025;
        Height = 615;
        MinWidth = 900;
        MinHeight = 550;
        Background = C("AppBackgroundBrush");

        foreach (var audioMeter in new[] { _micInputMeter, _micOutputMeter, _gateOutputMeter })
            StyleMicMeter(audioMeter);
        _calibrateGate.Content = L("Calibrar ruido ambiente", "Calibrate ambient noise");
        StyleAction(_calibrateGate);
        _calibrateGate.Click += async (_, _) => await CalibrateGateAsync();

        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(Label("IterVC", 21, "TextStrongBrush", true));
        status.Children.Add(StatusPill(_routingPill));
        status.Children.Add(StatusPill(_microphonePill));
        status.Children.Add(StatusPill(_applicationsPill));

        _routingPill.Click += async (_, _) => { if (_running) await StopAsync(); else await StartAsync(); };
        _microphonePill.Click += async (_, _) => await ToggleMicrophoneMuteAsync();
        _applicationsPill.Click += async (_, _) => await ToggleApplicationMuteAsync();

        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center };
        _refresh.Content = L("Actualizar apps", "Refresh apps");
        StyleAction(_refresh);
        headerActions.Children.Add(_refresh);

        _languageSelector.ItemsSource = new[] { "Español", "English" };
        _languageSelector.SelectedItem = _settings.Language == "English" ? "English" : "Español";
        _languageSelector.SelectionChanged += (_, _) =>
        {
            if (_languageSelector.SelectedItem is not string selection || selection == _settings.Language)
                return;
            _settings.Language = selection;
            SavePreferences();
            _status.Text = L("Idioma guardado. Reinicia IterVC para aplicar todos los textos.",
                "Language saved. Restart IterVC to apply all text.");
        };
        headerActions.Children.Add(_languageSelector);

        _includeApplication.Content = L("Mezclar audio de aplicaciones", "Mix application audio");
        _applications.IsVisible = false; // Backing selection for the PipeWire controller.
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = C("TextMutedBrush");
        _status.FontSize = 11;
        var appsBottom = Vertical(_applications, _status);
        appsBottom.Spacing = 5;
        var appsCard = new IterVC.UI.DesktopApplicationsPanel
        {
            Title = L("Aplicaciones", "Applications"),
            Help = L("Selecciona los programas que se mezclarán en el micrófono virtual.",
                "Choose which applications should be mixed into the virtual microphone."),
            Actions = _includeApplication,
            ApplicationItems = _appCards,
            Status = appsBottom
        };

        var settings = new IterVC.UI.DesktopSettingsTabs {
            DevicesTitle = L("Dispositivos", "Devices"),
            MixTitle = L("Mezcla", "Mix"),
            GateTitle = L("Puerta de ruido", "Noise gate"),
            OscTitle = "OSC",
            PreferencesTitle = L("Ajustes", "Settings")
        };
        settings.Devices = ScrollableTab(
            Vertical(
                Label(L("Micrófono físico", "Physical microphone"), 11, "TextSecondaryBrush", true),
                _microphone,
                Label(L("Nivel de entrada del micrófono", "Microphone input level"),
                    10, "TextSubtleBrush"),
                _micInputMeter,
                Label(L("Salida del micrófono a PipeWire", "Microphone output to PipeWire"),
                    10, "TextSubtleBrush"),
                _micOutputMeter,
                Label(L("Salida para monitorización", "Monitoring speaker output"), 11, "TextSecondaryBrush", true),
                _speakers,
                _monitor,
                Label(L("PipeWire crea «IterVC Rust Microphone» automáticamente; no requiere VB-Cable.",
                    "PipeWire creates «IterVC Rust Microphone» automatically; VB-Cable is not required."),
                    10, "TextSubtleBrush")
            ));
        settings.Mix = ScrollableTab(
            Vertical(
                SliderField(L("Volumen de las aplicaciones", "Application mix volume"),
                    _applicationVolume),
                SliderField(L("Volumen del micrófono", "Microphone volume"),
                    _microphoneVolume),
                SliderField(L("Amplificación del micrófono", "Microphone boost"),
                    _microphoneBoost,
                    L("Se aplica sin reiniciar el micrófono virtual.", "Applied without restarting the virtual microphone."))
            ));
        settings.Gate = ScrollableTab(
            Vertical(
                Label(L("Supresión RNNoise · disponible en Windows; todavía no en Linux.",
                    "RNNoise suppression · available on Windows, not yet on Linux."),
                    10, "TextSubtleBrush"),
                _gate,
                Label(L("Nivel de salida de la puerta", "Noise gate output level"),
                    11, "TextSecondaryBrush", true),
                _gateOutputMeter,
                SliderField(L("Umbral (dBFS)", "Threshold (dBFS)"), _gateThreshold),
                _calibrateGate,
                SliderField(L("Ataque (ms)", "Attack (ms)"), _gateAttack),
                SliderField(L("Liberación (ms)", "Release (ms)"), _gateRelease),
                Label(L("Los ajustes de la puerta se aplican al instante, también mientras está activo.",
                    "Gate settings apply live, including while routing."), 10, "TextSubtleBrush")
            ));
        settings.Osc = ScrollableTab(
            Vertical(
                _oscEnabled,
                Label(L("Plantilla del mensaje", "Message template"), 11, "TextSecondaryBrush", true),
                _oscTemplate,
                Label(L("Controles multimedia (MPRIS / playerctl)", "Media controls (MPRIS / playerctl)"),
                    11, "TextSecondaryBrush", true),
                MediaRow(),
                Label(L("Envía música al chatbox de VRChat por OSC.",
                    "Sends music to the VRChat chatbox via OSC."), 10, "TextSubtleBrush")
            ));
        settings.Preferences = ScrollableTab(
            Vertical(
                _minimizeToTray,
                Label(L("Comportamiento al cerrar", "Close behavior"), 12, "TextStrongBrush", true),
                CloseSettingsControls(),
                Label(L("Atajos de teclado", "Keyboard shortcuts"), 12, "TextStrongBrush", true),
                Label(L("Con la ventana activa: Ctrl+Shift+R para iniciar/detener, Ctrl+Shift+M para escuchar, Ctrl+Shift+U para el micrófono y Ctrl+Shift+P para las aplicaciones.",
                    "With the window focused: Ctrl+Shift+R routing, Ctrl+Shift+M monitoring, Ctrl+Shift+U mic mute and Ctrl+Shift+P app mute."),
                    10, "TextSecondaryBrush"),
                Label(L("Atajos globales X11/Wayland: asigna comandos IterVC.Linux --toggle-routing y --toggle-monitor en los ajustes del escritorio.",
                    "Global X11/Wayland shortcuts: bind IterVC.Linux --toggle-routing and --toggle-monitor in desktop settings."),
                    10, "TextSubtleBrush"),
                Label(L("Diagnóstico de dependencias", "Dependency diagnostics"), 12, "TextStrongBrush", true),
                DiagnosticControls(),
                _dependencies
            ));
        var settingsCard = settings; // The shared tabs already render the original settings card.

        var footer = new Grid {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 13, 0, 0)
        };
        var meter = Vertical(_levelText, _level);
        meter.Spacing = 4;
        _level.Foreground = C("SuccessBrush");
        _level.Height = 9;
        _level.Minimum = 0;
        _level.Maximum = 100;
        meter.Margin = new Thickness(0, 0, 30, 0);
        footer.Children.Add(meter);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _start.Content = L("Iniciar enrutamiento", "Start routing");
        _stop.Content = L("Detener", "Stop");
        StyleAction(_start, primary: true);
        StyleAction(_stop);
        actions.Children.Add(_start);
        actions.Children.Add(_stop);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        var shell = new IterVC.UI.DesktopShell
        {
            HeaderLeft = status,
            HeaderRight = headerActions,
            Applications = appsCard,
            Settings = settingsCard,
            Footer = footer
        };

        RenderApplicationCards(_applications.ItemsSource?.OfType<ApplicationStream>().ToArray()
            ?? Array.Empty<ApplicationStream>());
        UpdateDesktopStatus();
        return shell;
    }

    private static void StyleAction(Button button, bool primary = false)
    {
        button.Height = 34;
        button.Padding = new Thickness(14, 0);
        button.CornerRadius = new CornerRadius(6);
        button.FontWeight = FontWeight.SemiBold;
        button.FontSize = 12;
        button.Foreground = C(primary ? "TextStrongBrush" : "TextSecondaryBrush");
        button.Background = C(primary ? "AccentBrush" : "SurfaceRaisedBrush");
        button.BorderBrush = C(primary ? "AccentBrush" : "BorderSubtleBrush");
        button.BorderThickness = new Thickness(1);
    }

    private Control MediaRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in new[] { _previousTrack, _playPause, _nextTrack })
        {
            StyleAction(button);
            row.Children.Add(button);
        }
        return row;
    }

    private Control DiagnosticControls()
    {
        var button = ActionButton(L("Comprobar PipeWire", "Check PipeWire"));
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var message = await _audio.QueryAsync("--check");
                _dependencies.Text = message.Trim();
                _dependencies.Foreground = C("SuccessBrush");
            }
            catch (Exception error)
            {
                _dependencies.Text = error.Message;
                _dependencies.Foreground = C("WarningTextBrush");
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private void RenderApplicationCards(ApplicationStream[] active)
    {
        _appCards.Children.Clear();
        var enabled = _includeApplication.IsChecked == true;
        var current = active.Select(app => app.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var app in active)
        {
            var card = new IterVC.UI.ApplicationCard
            {
                Title = app.Name,
                Subtitle = L("Reproducción PipeWire", "PipeWire playback"),
                IdentifierLabel = "Stream",
                Identifier = app.Id.ToString(CultureInfo.InvariantCulture),
                IsIncluded = enabled && _settings.IncludedProcessNames.Contains(app.Name,
                    StringComparer.OrdinalIgnoreCase),
                // Rust currently exposes only the final mix RMS. Deliberately
                // keep individual app meters inactive, never synthesize levels.
                MeterActive = false,
                IsEnabled = enabled && !_running
            };
            card.PointerPressed += (_, e) =>
            {
                if (_loadingPreferences || _running || !enabled) return;
                var selected = !card.IsIncluded;
                _loadingPreferences = true;
                try
                {
                    if (selected)
                    {
                        if (!_settings.IncludedProcessNames.Contains(app.Name, StringComparer.OrdinalIgnoreCase))
                            _settings.IncludedProcessNames.Add(app.Name);
                        if (_applications.SelectedItems?.Contains(app) == false)
                            _applications.SelectedItems.Add(app);
                    }
                    else
                    {
                        _settings.IncludedProcessNames.RemoveAll(name =>
                            name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
                        _applications.SelectedItems?.Remove(app);
                    }
                    card.IsIncluded = selected;
                }
                finally { _loadingPreferences = false; }
                SavePreferences();
                e.Handled = true;
            };
            _appCards.Children.Add(card);
        }

        // The same app-card component also represents saved, offline apps.
        // Clicking a disconnected card removes that choice explicitly.
        foreach (var name in _settings.IncludedProcessNames.Where(n => !current.Contains(n)).ToArray())
        {
            var offline = new IterVC.UI.ApplicationCard
            {
                Title = name,
                Subtitle = L("Sin conexión · pulsa para quitar", "Offline · click to remove"),
                IdentifierLabel = "Stream",
                Identifier = "—",
                IsIncluded = true,
                MeterActive = false,
                IsEnabled = enabled && !_running
            };
            offline.PointerPressed += (_, e) =>
            {
                if (_running || _loadingPreferences || !enabled) return;
                _settings.IncludedProcessNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase));
                SavePreferences();
                RenderApplicationCards(active);
                e.Handled = true;
            };
            _appCards.Children.Add(offline);
        }

        if (_appCards.Children.Count == 0)
            _appCards.Children.Add(Label(L("No hay aplicaciones reproduciendo audio. Se actualizará automáticamente.",
                "No applications are playing. The list updates automatically."),
                11, "TextMutedBrush"));
    }

    private void UpdateDesktopStatus()
    {
        _routingPill.Content = _running ? L("● Enrutando", "● Routing")
            : L("○ Inactivo", "○ Inactive");
        _microphonePill.Content = _microphoneMuted ? L("○ Micro silenciado", "○ Mic muted")
            : L("● Micrófono activo", "● Microphone active");
        _applicationsPill.Content = _applicationMuted ? L("○ Apps silenciadas", "○ Apps muted")
            : L("● Audio de apps", "● App audio");
        _applicationsPill.IsEnabled = _includeApplication.IsChecked == true;
        foreach (var button in new[] { _routingPill, _microphonePill, _applicationsPill })
        {
            var active = ReferenceEquals(button, _routingPill) ? _running :
                ReferenceEquals(button, _microphonePill) ? !_microphoneMuted : !_applicationMuted;
            button.Background = C(active ? "SuccessSurfaceBrush" : "SurfaceInteractiveBrush");
            button.BorderBrush = C(active ? "SuccessBorderBrush" : "BorderStrongBrush");
            button.Foreground = C(active ? "SuccessBrush" : "TextMutedBrush");
        }
    }
}
