using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace IterVC.Linux;

/// <summary>
/// Linux proof-of-concept UI. Does not instantiate any Windows-only NAudio
/// services. All capture and monitoring go through the same Rust PCM stream.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly LinuxAudioBackend _audio = new();
    private readonly ComboBox _microphone = new() { MinWidth = 360 };
    private readonly ComboBox _speakers = new() { MinWidth = 360 };
    private readonly CheckBox _monitor = new();
    private readonly Button _refresh = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 100, Height = 12 };
    private readonly TextBlock _levelText = new();
    private bool _running;
    private bool _closing;

    private static string L(string spanish, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? spanish : english;

    public MainWindow()
    {
        Title = "IterVC — Linux (PipeWire)";
        Width = 600;
        MinWidth = 500;
        Height = 520;
        MinHeight = 460;
        _monitor.Content = L("Monitorizar micrófono (opcional)", "Monitor microphone (optional)");
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
        root.Children.Add(_monitor);
        root.Children.Add(new TextBlock { Text = L("Salida para escuchar el micrófono", "Speaker output for monitoring") });
        root.Children.Add(_speakers);

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
            Text = L("Vista previa: aún no incluye mezcla por aplicación ni atajos globales de Wayland.",
                "Preview: application mixing and Wayland global shortcuts are not implemented yet."),
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        Content = new ScrollViewer { Content = root };

        _monitor.IsCheckedChanged += (_, _) => _speakers.IsEnabled = !_running && _monitor.IsChecked == true;
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
            _microphone.ItemsSource = inputs;
            _microphone.SelectedIndex = inputs.Length > 0 ? 0 : -1;
            _speakers.ItemsSource = outputs;
            _speakers.SelectedIndex = outputs.Length > 0 ? 0 : -1;
            _status.Text = inputs.Length == 0
                ? L("No se detectaron micrófonos físicos.", "No physical microphones found.")
                : L("Selecciona el micrófono y pulsa Iniciar.", "Choose a microphone and press Start.");
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _refresh.IsEnabled = true; }
    }

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
        try
        {
            _audio.Start(mic, speaker);
            SetRunning(true);
            _status.Text = L("Enrutando al micrófono virtual «IterVC Rust Microphone».",
                "Routing to virtual microphone «IterVC Rust Microphone».");
        }
        catch (Exception error) { _status.Text = error.Message; }
        return Task.CompletedTask;
    }

    private async Task StopAsync()
    {
        try { await _audio.StopAsync(); }
        catch (Exception error) { _status.Text = error.Message; }
        SetRunning(false);
    }

    private void SetRunning(bool active)
    {
        _running = active;
        _start.IsEnabled = !active;
        _stop.IsEnabled = active;
        _microphone.IsEnabled = !active;
        _monitor.IsEnabled = !active;
        _speakers.IsEnabled = !active && _monitor.IsChecked == true;
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
        else if (e.Key == Key.M && !_running)
        {
            _monitor.IsChecked = _monitor.IsChecked != true;
            e.Handled = true;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) { _audio.Dispose(); return; }
        e.Cancel = true;
        _closing = true;
        await StopAsync();
        Close();
    }
}
