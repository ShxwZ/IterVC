using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IterVC.Core.Settings;

namespace IterVC.Linux;

/// <summary>
/// Linux proof-of-concept UI. Does not instantiate any Windows-only NAudio
/// services. All capture and monitoring go through the same Rust PCM stream.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly LinuxAudioBackend _audio = new();
    private readonly LinuxSettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly LinuxOscChatboxService _osc;
    private ShortcutControlServer? _shortcuts;
    private readonly ComboBox _microphone = new() { MinWidth = 360 };
    private readonly ComboBox _speakers = new() { MinWidth = 360 };
    private readonly CheckBox _includeApplication = new();
    private readonly ComboBox _applications = new() { MinWidth = 360 };
    private readonly Slider _applicationVolume = new() { Minimum = 0, Maximum = 1, Width = 250 };
    private readonly Slider _microphoneVolume = new() { Minimum = 0, Maximum = 1, Width = 250 };
    private readonly Slider _microphoneBoost = new() { Minimum = 0.5, Maximum = 3, Width = 250 };
    private readonly CheckBox _gate = new();
    private readonly Slider _gateThreshold = new() { Minimum = -80, Maximum = -15, Width = 250 };
    private readonly CheckBox _oscEnabled = new();
    private readonly TextBox _oscTemplate = new() { MinWidth = 340 };
    private readonly CheckBox _monitor = new();
    private readonly Button _refresh = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 100, Height = 12 };
    private readonly TextBlock _levelText = new();
    private bool _running;
    private bool _closing;
    private bool _loadingPreferences;

    private static string L(string spanish, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? spanish : english;

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        _osc = new LinuxOscChatboxService(message =>
            Dispatcher.UIThread.Post(() => _status.Text = message));
        Title = "IterVC — Linux (PipeWire)";
        Width = 600;
        MinWidth = 500;
        Height = 930;
        MinHeight = 580;
        _monitor.Content = L("Monitorizar micrófono (opcional)", "Monitor microphone (optional)");
        _monitor.IsChecked = _settings.MonitorMicrophone;
        _microphoneVolume.Value = Math.Clamp(_settings.MicrophoneVolume, 0, 1);
        _microphoneBoost.Value = Math.Clamp(_settings.MicrophoneBoost, 0.5, 3);
        _gate.Content = L("Activar puerta de ruido", "Enable noise gate");
        _gate.IsChecked = _settings.NoiseGateEnabled;
        _gateThreshold.Value = Math.Clamp(_settings.NoiseGateThresholdDb, -80, -15);
        _gateThreshold.IsEnabled = _gate.IsChecked == true;
        _oscEnabled.Content = L("Enviar música al chatbox OSC (VRChat)",
            "Send music to the OSC chatbox (VRChat)");
        _oscEnabled.IsChecked = _settings.EnableOscChatbox;
        _oscTemplate.Text = _settings.OscTemplate;
        _includeApplication.Content = L("Mezclar audio de una aplicación", "Mix an application's audio");
        _includeApplication.IsChecked = _settings.IncludedProcessNames.Count > 0;
        _applicationVolume.Value = Math.Clamp(_settings.AppsVolume, 0, 1);
        _applications.IsEnabled = _includeApplication.IsChecked == true;
        _applicationVolume.IsEnabled = _includeApplication.IsChecked == true;
        _refresh.Content = L("Actualizar dispositivos", "Refresh devices");
        _start.Content = L("Iniciar enrutamiento", "Start routing");
        _stop.Content = L("Detener", "Stop");
        _status.Text = L("Preparando PipeWire…", "Preparing PipeWire…");
        _levelText.Text = L("Nivel de micrófono: sin señal", "Microphone level: no signal");
        _status.TextWrapping = TextWrapping.Wrap;
        _stop.IsEnabled = false;
        _speakers.IsEnabled = false;

        var root = new StackPanel { Spacing = 13, Margin = new Thickness(22) };
        root.Children.Add(new TextBlock
        {
            Text = L("IterVC para Linux · Vista previa", "IterVC for Linux · Preview"),
            FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        root.Children.Add(new TextBlock {
            Text = L("Micrófono virtual sin VB-Cable, mediante PipeWire.",
                "Virtual microphone via PipeWire, without VB-Cable."),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock { Text = L("Micrófono físico", "Physical microphone") });
        root.Children.Add(_microphone);
        root.Children.Add(_refresh);
        root.Children.Add(new TextBlock { Text = L("Volumen del micrófono", "Microphone volume") });
        root.Children.Add(_microphoneVolume);
        root.Children.Add(new TextBlock { Text = L("Amplificación del micrófono", "Microphone boost") });
        root.Children.Add(_microphoneBoost);
        root.Children.Add(_gate);
        root.Children.Add(new TextBlock { Text = L("Umbral de la puerta (dBFS)", "Noise gate threshold (dBFS)") });
        root.Children.Add(_gateThreshold);
        root.Children.Add(_monitor);
        root.Children.Add(new TextBlock { Text = L("Salida para escuchar el micrófono", "Speaker output for monitoring") });
        root.Children.Add(_speakers);
        root.Children.Add(new TextBlock { Text = L("Audio por aplicación", "Application audio"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold });
        root.Children.Add(_includeApplication);
        root.Children.Add(_applications);
        root.Children.Add(new TextBlock { Text = L("Volumen en la mezcla virtual",
            "Volume in the virtual microphone mix") });
        root.Children.Add(_applicationVolume);
        root.Children.Add(new TextBlock { Text = L("Chatbox OSC", "OSC chatbox"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold });
        root.Children.Add(_oscEnabled);
        root.Children.Add(new TextBlock { Text = L("Plantilla del mensaje", "Message template") });
        root.Children.Add(_oscTemplate);
        root.Children.Add(new TextBlock { Text = L("Requiere playerctl para detectar música mediante MPRIS.",
            "Requires playerctl to detect music through MPRIS."),
            FontSize = 11, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = L(
            "La aplicación seleccionada seguirá escuchándose por sus altavoces originales.",
            "The selected application will continue playing through its original speakers."),
            FontSize = 11, TextWrapping = TextWrapping.Wrap });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        controls.Children.Add(_start);
        controls.Children.Add(_stop);
        root.Children.Add(controls);
        root.Children.Add(_levelText);
        root.Children.Add(_level);
        root.Children.Add(new TextBlock { Text = L("Estado", "Status"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        root.Children.Add(_status);
        root.Children.Add(new TextBlock {
            Text = L("Atajos con esta ventana activa: Ctrl+Shift+R iniciar/detener; Ctrl+Shift+M monitor.",
                "Shortcuts while this window is focused: Ctrl+Shift+R start/stop; Ctrl+Shift+M monitor."),
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock {
            Text = L("Atajos globales X11/Wayland: configura comandos de teclado en tu escritorio: "
                + "IterVC.Linux --toggle-routing y --toggle-monitor.",
                "Global X11/Wayland shortcuts: configure desktop keyboard commands "
                + "IterVC.Linux --toggle-routing and --toggle-monitor."),
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        Content = new ScrollViewer { Content = root };
        try
        {
            _shortcuts = new ShortcutControlServer(command => Dispatcher.UIThread.Post(async () =>
            {
                if (command == "toggle-routing")
                {
                    if (_running) await StopAsync(); else await StartAsync();
                }
                else if (command == "toggle-monitor")
                    await ToggleMonitorAsync();
            }));
        }
        catch (Exception error) { _status.Text = error.Message; }

        _monitor.Checked += (_, _) => { _speakers.IsEnabled = !_running; SavePreferences(); };
        _monitor.Unchecked += (_, _) => { _speakers.IsEnabled = false; SavePreferences(); };
        _microphone.SelectionChanged += (_, _) => SavePreferences();
        _speakers.SelectionChanged += (_, _) => SavePreferences();
        _includeApplication.Checked += (_, _) => {
            _applications.IsEnabled = !_running;
            _applicationVolume.IsEnabled = !_running;
            SavePreferences();
        };
        _includeApplication.Unchecked += (_, _) => {
            _applications.IsEnabled = false;
            _applicationVolume.IsEnabled = false;
            SavePreferences();
        };
        _applications.SelectionChanged += (_, _) => SavePreferences();
        _applicationVolume.PropertyChanged += (_, args) => {
            if (args.Property.Name == nameof(Slider.Value)) SavePreferences();
        };
        _microphoneVolume.PropertyChanged += (_, args) => {
            if (args.Property.Name == nameof(Slider.Value)) SavePreferences();
        };
        _microphoneBoost.PropertyChanged += (_, args) => {
            if (args.Property.Name == nameof(Slider.Value)) SavePreferences();
        };
        _gateThreshold.PropertyChanged += (_, args) => {
            if (args.Property.Name == nameof(Slider.Value)) SavePreferences();
        };
        _gate.Checked += (_, _) => { _gateThreshold.IsEnabled = !_running; SavePreferences(); };
        _gate.Unchecked += (_, _) => { _gateThreshold.IsEnabled = false; SavePreferences(); };
        _oscEnabled.Checked += (_, _) => { SavePreferences(); UpdateOsc(); };
        _oscEnabled.Unchecked += (_, _) => { SavePreferences(); UpdateOsc(); };
        _oscTemplate.TextChanged += (_, _) => { SavePreferences(); UpdateOsc(); };
        _refresh.Click += async (_, _) => await RefreshAsync();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        _audio.MessageReceived += text => Dispatcher.UIThread.Post(() => _status.Text = text);
        _audio.LevelChanged += rms => Dispatcher.UIThread.Post(() =>
        {
            var decibels = 20 * Math.Log10(Math.Max(0.00001, rms));
            _level.Value = Math.Clamp((decibels + 60) / 60 * 100, 0, 100);
            _levelText.Text = string.Format(CultureInfo.InvariantCulture,
                L("Nivel de micrófono: {0:0.0} dBFS", "Microphone level: {0:0.0} dBFS"), decibels);
        });
        _audio.SessionExited += () => Dispatcher.UIThread.Post(() => SetRunning(false));
        UpdateOsc();
        Opened += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _refresh.IsEnabled = false;
        try
        {
            var inputs = (await _audio.QueryAsync("--list-inputs"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !item.StartsWith("No ", StringComparison.Ordinal)).ToArray();
            var outputs = (await _audio.QueryAsync("--list-outputs"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !item.StartsWith("No ", StringComparison.Ordinal)).ToArray();
            var applications = ApplicationStreamCatalog.Parse(await _audio.QueryAsync("--list-apps"));
            _loadingPreferences = true;
            try
            {
                _microphone.ItemsSource = inputs;
                _microphone.SelectedItem = inputs.FirstOrDefault(item => item == _settings.MicrophoneDeviceId)
                    ?? inputs.FirstOrDefault();
                _speakers.ItemsSource = outputs;
                _speakers.SelectedItem = outputs.FirstOrDefault(item => item == _settings.OutputDeviceId)
                    ?? outputs.FirstOrDefault();
                _speakers.IsEnabled = !_running && _monitor.IsChecked == true;
                _applications.ItemsSource = applications;
                _applications.SelectedItem = applications.FirstOrDefault(item =>
                    _settings.IncludedProcessNames.Contains(item.Name)) ?? applications.FirstOrDefault();
                _applications.IsEnabled = !_running && _includeApplication.IsChecked == true;
            }
            finally { _loadingPreferences = false; }
            _status.Text = inputs.Length == 0
                ? L("No se detectaron micrófonos físicos.", "No physical microphones found.")
                : L("Selecciona el micrófono y pulsa Iniciar.", "Choose a microphone and press Start.");
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _refresh.IsEnabled = true; }
    }

    private void SavePreferences()
    {
        if (_loadingPreferences) return;
        _settings.MicrophoneDeviceId = _microphone.SelectedItem as string;
        _settings.OutputDeviceId = _speakers.SelectedItem as string;
        _settings.MonitorMicrophone = _monitor.IsChecked == true;
        _settings.IncludedProcessNames = _includeApplication.IsChecked == true &&
            _applications.SelectedItem is ApplicationStream selectedApp
            ? new List<string> { selectedApp.Name } : new List<string>();
        _settings.AppsVolume = (float)_applicationVolume.Value;
        _settings.MicrophoneVolume = (float)_microphoneVolume.Value;
        _settings.MicrophoneBoost = (float)_microphoneBoost.Value;
        _settings.NoiseGateEnabled = _gate.IsChecked == true;
        _settings.NoiseGateThresholdDb = (float)_gateThreshold.Value;
        _settings.EnableOscChatbox = _oscEnabled.IsChecked == true;
        _settings.OscTemplate = _oscTemplate.Text ?? "{title} - {status}";
        try { _settingsStore.Save(_settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _status.Text = L("No se pudo guardar la configuración: ",
                "Unable to save settings: ") + error.Message;
        }
    }

    private void UpdateOsc() => _osc.SetEnabled(_oscEnabled.IsChecked == true,
        () => _oscTemplate.Text ?? "{title} - {status}");

    private Task StartAsync()
    {
        if (_running) return Task.CompletedTask;
        var mic = _microphone.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(mic))
        {
            _status.Text = L("Selecciona primero un micrófono.", "Select a microphone first.");
            return Task.CompletedTask;
        }
        var speaker = _monitor.IsChecked == true ? _speakers.SelectedItem as string : null;
        if (_monitor.IsChecked == true && speaker is null)
        {
            _status.Text = L("Selecciona unos altavoces para monitorizar.", "Choose speaker output for monitoring.");
            return Task.CompletedTask;
        }
        var selectedApp = _includeApplication.IsChecked == true
            ? _applications.SelectedItem as ApplicationStream : null;
        if (_includeApplication.IsChecked == true && selectedApp is null)
        {
            _status.Text = L("Selecciona una aplicación que esté reproduciendo sonido.",
                "Select an application that is currently playing audio.");
            return Task.CompletedTask;
        }
        try
        {
            _audio.Start(mic, speaker, selectedApp?.Id, _applicationVolume.Value,
                Math.Clamp(_microphoneVolume.Value * _microphoneBoost.Value, 0, 3),
                _gate.IsChecked == true, _gateThreshold.Value);
            SetRunning(true);
            _status.Text = L("Enrutando al micrófono virtual «IterVC Rust Microphone».",
                "Routing to virtual microphone «IterVC Rust Microphone».");
        }
        catch (Exception error) { _status.Text = error.Message; }
        return Task.CompletedTask;
    }

    private async Task<bool> StopAsync()
    {
        try { await _audio.StopAsync(); }
        catch (Exception error) { _status.Text = error.Message; return false; }
        SetRunning(false);
        return true;
    }

    private async Task ToggleMonitorAsync()
    {
        var enable = _monitor.IsChecked != true;
        if (enable && _speakers.SelectedItem is not string)
        {
            _status.Text = L("Selecciona unos altavoces antes de monitorizar.",
                "Select speaker output before enabling monitoring.");
            return;
        }
        var restart = _running;
        if (restart && !await StopAsync()) return;
        _monitor.IsChecked = enable;
        if (restart) await StartAsync();
    }

    private void SetRunning(bool active)
    {
        _running = active;
        _start.IsEnabled = !active;
        _stop.IsEnabled = active;
        _microphone.IsEnabled = !active;
        _monitor.IsEnabled = !active;
        _speakers.IsEnabled = !active && _monitor.IsChecked == true;
        _includeApplication.IsEnabled = !active;
        _applications.IsEnabled = !active && _includeApplication.IsChecked == true;
        _applicationVolume.IsEnabled = !active && _includeApplication.IsChecked == true;
        _microphoneVolume.IsEnabled = !active;
        _microphoneBoost.IsEnabled = !active;
        _gate.IsEnabled = !active;
        _gateThreshold.IsEnabled = !active && _gate.IsChecked == true;
        if (!active) { _level.Value = 0; _levelText.Text = L("Micrófono detenido", "Microphone stopped"); }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift)) return;
        if (e.Key == Key.R)
        {
            if (_running) await StopAsync(); else await StartAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.M)
        {
            await ToggleMonitorAsync();
            e.Handled = true;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) { _osc.Dispose(); _shortcuts?.Dispose(); _audio.Dispose(); return; }
        e.Cancel = true;
        _closing = true;
        await StopAsync();
        Close();
    }
}
