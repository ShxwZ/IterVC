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
    private readonly DispatcherTimer _appWatchTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly HashSet<uint> _trackedAppStreams = new();
    private readonly Dictionary<uint, DateTimeOffset> _pendingAppStreams = new();
    private readonly Dictionary<uint, DateTimeOffset> _appRetryAfter = new();
    private readonly LinuxSettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly LinuxOscChatboxService _osc;
    private ShortcutControlServer? _shortcuts;
    private readonly ComboBox _microphone = new() { MinWidth = 360 };
    private readonly ComboBox _speakers = new() { MinWidth = 360 };
    private readonly CheckBox _includeApplication = new();
    private readonly ListBox _applications = new() {
        MinWidth = 360, Height = 104, SelectionMode = SelectionMode.Multiple };
    private readonly Slider _applicationVolume = new() { Minimum = 0, Maximum = 1, Width = 250 };
    private readonly Slider _microphoneVolume = new() { Minimum = 0, Maximum = 1, Width = 250 };
    private readonly Slider _microphoneBoost = new() { Minimum = 0.5, Maximum = 3, Width = 250 };
    private readonly CheckBox _gate = new();
    private readonly Slider _gateThreshold = new() { Minimum = -80, Maximum = -15, Width = 250 };
    private readonly CheckBox _oscEnabled = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly TextBox _oscTemplate = new() { MinWidth = 340 };
    private readonly Button _previousTrack = new();
    private readonly Button _playPause = new();
    private readonly Button _nextTrack = new();
    private readonly CheckBox _monitor = new();
    private readonly Button _refresh = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 100, Height = 12 };
    private readonly TextBlock _levelText = new();
    private bool _running;
    private bool _starting;
    private bool _closing;
    private bool _loadingPreferences;
    private bool _refreshingApplicationStreams;
    private bool _microphoneMuted;
    private bool _applicationMuted;
    internal bool TrayConfigured { get; set; }

    internal void RestoreFromTray()
    {
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private static string L(string spanish, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? spanish : english;

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        _microphoneMuted = !_settings.MicrophoneEnabled;
        _osc = new LinuxOscChatboxService(message =>
            Dispatcher.UIThread.Post(() => _status.Text = message));
        Title = "IterVC — Linux (PipeWire)";
        Width = 600;
        MinWidth = 500;
        Height = 930;
        MinHeight = 580;
        _monitor.Content = L("Monitorizar micrófono (opcional)", "Monitor microphone (optional)");
        _monitor.IsChecked = _settings.MonitorMicrophone;
        _minimizeToTray.Content = L("Minimizar a la bandeja del sistema", "Minimize to system tray");
        _minimizeToTray.IsChecked = _settings.MinimizeToTrayWhenMinimized;
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
        _previousTrack.Content = L("Anterior", "Previous");
        _playPause.Content = L("Reproducir / pausar", "Play / pause");
        _nextTrack.Content = L("Siguiente", "Next");
        _includeApplication.Content = L("Mezclar audio de aplicaciones", "Mix application audio");
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
        root.Children.Add(_minimizeToTray);
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
        var mediaControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        mediaControls.Children.Add(_previousTrack);
        mediaControls.Children.Add(_playPause);
        mediaControls.Children.Add(_nextTrack);
        root.Children.Add(mediaControls);
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
                if (command == "exit")
                    Close();
                else if (command == "show-window")
                    RestoreFromTray();
                else if (command == "toggle-routing")
                {
                    if (_running) await StopAsync(); else await StartAsync();
                }
                else if (command == "start-routing" && !_running)
                    await StartAsync();
                else if (command == "stop-routing" && _running)
                    await StopAsync();
                else if (command == "toggle-monitor")
                    await ToggleMonitorAsync();
                else if (command == "toggle-microphone")
                    await ToggleMicrophoneMuteAsync();
                else if (command == "toggle-applications-mute")
                    await ToggleApplicationMuteAsync();
                else if (command == "media-play-pause")
                    await RunMediaCommandAsync("play-pause");
                else if (command == "media-next")
                    await RunMediaCommandAsync("next");
                else if (command == "media-previous")
                    await RunMediaCommandAsync("previous");
            }));
        }
        catch (Exception error) { _status.Text = error.Message; }

        _monitor.IsCheckedChanged += (_, _) => {
            _speakers.IsEnabled = !_running && _monitor.IsChecked == true;
            SavePreferences();
        };
        _minimizeToTray.IsCheckedChanged += (_, _) => SavePreferences();
        PropertyChanged += (_, change) => {
            if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized &&
                TrayConfigured && _minimizeToTray.IsChecked == true)
            {
                ShowInTaskbar = false;
                Hide();
            }
        };
        _microphone.SelectionChanged += (_, _) => SavePreferences();
        _speakers.SelectionChanged += (_, _) => SavePreferences();
        _includeApplication.IsCheckedChanged += (_, _) => {
            var enabled = !_running && _includeApplication.IsChecked == true;
            _applications.IsEnabled = enabled;
            _applicationVolume.IsEnabled = enabled;
            if (!_loadingPreferences && _includeApplication.IsChecked == true)
                _settings.IncludedProcessNames = SelectedApplicationNames();
            SavePreferences();
        };
        _applications.SelectionChanged += (_, _) => {
            // Explicit user edits replace the previous selection, including
            // clearing it. Automatic catalogue refreshes never enter here.
            // Otherwise a deselected application could still be captured by
            // its saved name even though it is no longer highlighted.
            if (!_loadingPreferences && _includeApplication.IsChecked == true)
                _settings.IncludedProcessNames = SelectedApplicationNames();
            SavePreferences();
        };
        _applicationVolume.PropertyChanged += async (_, args) => {
            if (args.Property.Name == nameof(Slider.Value))
            {
                SavePreferences();
                if (_running && !_applicationMuted && _includeApplication.IsChecked == true)
                {
                    try { await _audio.SetApplicationVolumeAsync(_applicationVolume.Value); }
                    catch (Exception error) { _status.Text = error.Message; }
                }
            }
        };
        _microphoneVolume.PropertyChanged += async (_, args) => {
            if (args.Property.Name == nameof(Slider.Value))
            {
                SavePreferences();
                await UpdateLiveMicrophoneVolumeAsync();
            }
        };
        _microphoneBoost.PropertyChanged += async (_, args) => {
            if (args.Property.Name == nameof(Slider.Value))
            {
                SavePreferences();
                await UpdateLiveMicrophoneVolumeAsync();
            }
        };
        _gateThreshold.PropertyChanged += (_, args) => {
            if (args.Property.Name == nameof(Slider.Value)) SavePreferences();
        };
        _gate.IsCheckedChanged += (_, _) => {
            _gateThreshold.IsEnabled = !_running && _gate.IsChecked == true;
            SavePreferences();
        };
        _oscEnabled.IsCheckedChanged += (_, _) => { SavePreferences(); UpdateOsc(); };
        _oscTemplate.TextChanged += (_, _) => { SavePreferences(); UpdateOsc(); };
        _previousTrack.Click += async (_, _) => await RunMediaCommandAsync("previous");
        _playPause.Click += async (_, _) => await RunMediaCommandAsync("play-pause");
        _nextTrack.Click += async (_, _) => await RunMediaCommandAsync("next");
        _refresh.Click += async (_, _) => await RefreshAsync();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopAsync();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        _audio.MessageReceived += text => Dispatcher.UIThread.Post(() =>
        {
            if (text.StartsWith("ITERVC_APP_LINKED:", StringComparison.Ordinal) &&
                uint.TryParse(text.AsSpan("ITERVC_APP_LINKED:".Length), out var addedId))
            {
                _pendingAppStreams.Remove(addedId);
                _appRetryAfter.Remove(addedId);
                if (_running)
                {
                    _trackedAppStreams.Add(addedId);
                    _status.Text = L("Aplicación conectada a la mezcla.",
                        "Application audio connected to the mix.");
                }
                return;
            }
            if (text.StartsWith("ITERVC_APP_FAILED:", StringComparison.Ordinal))
            {
                var fields = text.Split(':', 3);
                if (fields.Length >= 2 && uint.TryParse(fields[1], out var failedId))
                {
                    _pendingAppStreams.Remove(failedId);
                    _appRetryAfter[failedId] = DateTimeOffset.UtcNow.AddSeconds(15);
                    _status.Text = L("No se pudo conectar una aplicación. Se reintentará.",
                        "Could not connect an application. Will retry.");
                    return;
                }
            }
            _status.Text = text;
        });
        _audio.LevelChanged += rms => Dispatcher.UIThread.Post(() =>
        {
            var decibels = 20 * Math.Log10(Math.Max(0.00001, rms));
            _level.Value = Math.Clamp((decibels + 60) / 60 * 100, 0, 100);
            _levelText.Text = string.Format(CultureInfo.InvariantCulture,
                L("Nivel de micrófono: {0:0.0} dBFS", "Microphone level: {0:0.0} dBFS"), decibels);
        });
        _audio.SessionExited += () => Dispatcher.UIThread.Post(() => SetRunning(false));
        UpdateOsc();
        _appWatchTimer.Tick += async (_, _) =>
        {
            if (_running) await DiscoverNewApplicationStreamsAsync();
            else await RefreshIdleApplicationStreamsAsync();
        };
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            _appWatchTimer.Start();
        };
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
                _applications.SelectedItems?.Clear();
                foreach (var app in applications.Where(item =>
                    _settings.IncludedProcessNames.Contains(item.Name)))
                    _applications.SelectedItems?.Add(app);
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

    private async Task DiscoverNewApplicationStreamsAsync()
    {
        if (_refreshingApplicationStreams || !_running || _closing ||
            _includeApplication.IsChecked != true)
            return;

        var selectedNames = _settings.IncludedProcessNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedNames.Count == 0) return;

        _refreshingApplicationStreams = true;
        try
        {
            var available = ApplicationStreamCatalog.Parse(
                await _audio.QueryAsync("--list-apps"));
            foreach (var app in available)
            {
                if (!selectedNames.Contains(app.Name) || _trackedAppStreams.Contains(app.Id))
                    continue;
                var now = DateTimeOffset.UtcNow;
                if (_pendingAppStreams.TryGetValue(app.Id, out var sentAt) &&
                    now - sentAt < TimeSpan.FromSeconds(12))
                    continue;
                if (_appRetryAfter.TryGetValue(app.Id, out var retryAfter) &&
                    now < retryAfter)
                    continue;

                // Mark pending *before* writing: Rust may acknowledge on stderr
                // before our awaited stdin flush completes.
                _pendingAppStreams[app.Id] = now;
                try
                {
                    await _audio.AddApplicationStreamAsync(app.Id);
                }
                catch
                {
                    _pendingAppStreams.Remove(app.Id);
                    throw;
                }
                // Only the Rust ITERVC_APP_LINKED acknowledgement marks an app
                // as connected. Failed or timed-out links will be retried.
            }
        }
        catch (Exception error)
        {
            _status.Text = L("No se pudo actualizar el audio de aplicaciones: ",
                "Could not refresh application audio: ") + error.Message;
        }
        finally { _refreshingApplicationStreams = false; }
    }

    /// <summary>
    /// Keep the idle app picker up to date when applications start or stop.
    /// Reapply saved selections by *name*, since stream IDs are ephemeral.
    /// Do not rewrite preferences while refreshing the ListBox.
    /// </summary>
    private async Task RefreshIdleApplicationStreamsAsync()
    {
        if (_refreshingApplicationStreams || _running || _closing || !_refresh.IsEnabled)
            return;

        _refreshingApplicationStreams = true;
        try
        {
            var available = ApplicationStreamCatalog.Parse(
                await _audio.QueryAsync("--list-apps"));
            if (_running || _closing || !_refresh.IsEnabled) return;

            var previous = _applications.ItemsSource?.OfType<ApplicationStream>().ToArray()
                ?? Array.Empty<ApplicationStream>();
            if (previous.SequenceEqual(available)) return;

            _loadingPreferences = true;
            try
            {
                var savedNames = _settings.IncludedProcessNames
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _applications.ItemsSource = available;
                _applications.SelectedItems?.Clear();
                foreach (var app in available.Where(item => savedNames.Contains(item.Name)))
                    _applications.SelectedItems?.Add(app);
            }
            finally { _loadingPreferences = false; }
        }
        catch (Exception error)
        {
            // A stopped PipeWire service should not replace the user's status
            // message every four seconds. The manual Refresh button still
            // reports the error directly.
            System.Diagnostics.Trace.WriteLine("Idle application discovery: " + error);
        }
        finally { _refreshingApplicationStreams = false; }
    }

    private List<string> SelectedApplicationNames() =>
        _applications.SelectedItems?.OfType<ApplicationStream>()
            .Select(app => app.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

    private void SavePreferences()
    {
        if (_loadingPreferences) return;
        _settings.MicrophoneDeviceId = _microphone.SelectedItem as string;
        _settings.OutputDeviceId = _speakers.SelectedItem as string;
        _settings.MonitorMicrophone = _monitor.IsChecked == true;
        _settings.MinimizeToTrayWhenMinimized = _minimizeToTray.IsChecked == true;
        if (_includeApplication.IsChecked != true)
            _settings.IncludedProcessNames = new List<string>();
        // Only explicit app selection changes replace saved names. Unrelated
        // slider changes must preserve apps that are temporarily offline.
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

    private async Task RunMediaCommandAsync(string command)
    {
        try
        {
            if (!await LinuxOscChatboxService.ControlPlaybackAsync(command))
                _status.Text = L("No hay reproductor MPRIS o falta playerctl.",
                    "No active MPRIS player or playerctl is unavailable.");
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void UpdateOsc() => _osc.SetEnabled(_oscEnabled.IsChecked == true,
        () => _oscTemplate.Text ?? "{title} - {status}");

    private async Task StartAsync()
    {
        if (_running || _starting || _closing) return;
        _starting = true;
        _start.IsEnabled = false;
        try
        {
            var mic = _microphone.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(mic))
            {
                _status.Text = L("Selecciona primero un micrófono.", "Select a microphone first.");
                return;
            }
            var speaker = _monitor.IsChecked == true ? _speakers.SelectedItem as string : null;
            if (_monitor.IsChecked == true && speaker is null)
            {
                _status.Text = L("Selecciona unos altavoces para monitorizar.", "Choose speaker output for monitoring.");
                return;
            }
            var selectedApps = _includeApplication.IsChecked == true
                ? _applications.SelectedItems?.OfType<ApplicationStream>().ToArray()
                    ?? Array.Empty<ApplicationStream>()
                : Array.Empty<ApplicationStream>();
            // A previously selected app may not be playing yet. Prepare the app
            // capture sink now and auto-attach its stream when it reappears.
            if (_includeApplication.IsChecked == true && selectedApps.Length == 0 &&
                _settings.IncludedProcessNames.Count == 0)
            {
                _status.Text = L("Selecciona una aplicación para capturar su audio.",
                    "Choose an application to capture its audio.");
                return;
            }

            // A playback stream can disappear between the last GUI catalogue
            // refresh and this click. Resolve current IDs rather than passing
            // stale saved/ListBox IDs into the Rust process and failing startup.
            var liveApps = _includeApplication.IsChecked == true
                ? ApplicationStreamCatalog.SelectActive(
                    await _audio.QueryAsync("--list-apps"),
                    _settings.IncludedProcessNames.Concat(selectedApps.Select(app => app.Name)))
                : Array.Empty<ApplicationStream>();
            if (_closing) return;

            _audio.Start(mic, speaker, liveApps.Select(app => app.Id).ToArray(),
                _applicationMuted ? 0.0 : _applicationVolume.Value,
                _microphoneMuted ? 0.0 : Math.Clamp(_microphoneVolume.Value * _microphoneBoost.Value, 0, 3),
                _gate.IsChecked == true, _gateThreshold.Value,
                includeApplicationCapture: _includeApplication.IsChecked == true);
            _trackedAppStreams.Clear();
            _pendingAppStreams.Clear();
            _appRetryAfter.Clear();
            foreach (var app in liveApps)
                _trackedAppStreams.Add(app.Id);
            SetRunning(true);
            _status.Text = L("Enrutando al micrófono virtual «IterVC Rust Microphone».",
                "Routing to virtual microphone «IterVC Rust Microphone».");
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally
        {
            _starting = false;
            _start.IsEnabled = !_running && !_closing;
        }
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

    private async Task UpdateLiveMicrophoneVolumeAsync()
    {
        if (!_running || _microphoneMuted) return;
        try
        {
            await _audio.SetMicrophoneVolumeAsync(
                Math.Clamp(_microphoneVolume.Value * _microphoneBoost.Value, 0, 3));
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private async Task ToggleMicrophoneMuteAsync()
    {
        var nextMute = !_microphoneMuted;
        if (_running)
        {
            try
            {
                var gain = nextMute ? 0.0
                    : Math.Clamp(_microphoneVolume.Value * _microphoneBoost.Value, 0, 3);
                await _audio.SetMicrophoneVolumeAsync(gain);
            }
            catch (Exception error) { _status.Text = error.Message; return; }
        }
        _microphoneMuted = nextMute;
        _settings.MicrophoneEnabled = !_microphoneMuted;
        SavePreferences();
        _status.Text = _microphoneMuted
            ? L("Micrófono silenciado", "Microphone muted")
            : L("Micrófono activado", "Microphone unmuted");
    }

    private async Task ToggleApplicationMuteAsync()
    {
        if (_includeApplication.IsChecked != true) return;
        var nextMute = !_applicationMuted;
        if (_running)
        {
            try
            {
                await _audio.SetApplicationVolumeAsync(nextMute ? 0 : _applicationVolume.Value);
            }
            catch (Exception error) { _status.Text = error.Message; return; }
        }
        _applicationMuted = nextMute;
        _status.Text = _applicationMuted
            ? L("Audio de aplicaciones silenciado en la mezcla", "Application mix muted")
            : L("Audio de aplicaciones activado", "Application mix unmuted");
    }

    private void SetRunning(bool active)
    {
        _running = active;
        if (!active)
        {
            _trackedAppStreams.Clear();
            _pendingAppStreams.Clear();
            _appRetryAfter.Clear();
        }
        _start.IsEnabled = !active;
        _stop.IsEnabled = active;
        _microphone.IsEnabled = !active;
        _monitor.IsEnabled = !active;
        _speakers.IsEnabled = !active && _monitor.IsChecked == true;
        _includeApplication.IsEnabled = !active;
        _applications.IsEnabled = !active && _includeApplication.IsChecked == true;
        // Gains can be adjusted live; gate configuration and app selection
        // still require a restart so they remain locked during routing.
        _applicationVolume.IsEnabled = _includeApplication.IsChecked == true;
        _microphoneVolume.IsEnabled = true;
        _microphoneBoost.IsEnabled = true;
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
        else if (e.Key == Key.U)
        {
            await ToggleMicrophoneMuteAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.P)
        {
            await ToggleApplicationMuteAsync();
            e.Handled = true;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) { _appWatchTimer.Stop(); _osc.Dispose(); _shortcuts?.Dispose(); _audio.Dispose(); return; }
        e.Cancel = true;
        _closing = true;
        await StopAsync();
        Close();
    }
}
