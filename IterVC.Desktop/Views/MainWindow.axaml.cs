using Avalonia.Controls;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;

namespace IterVC.Desktop.Views;

/// <summary>
/// Code-behind mínimo: carga del XAML, captura de teclas del TextBox de atajos
/// y ciclo de vida del hotkey global (glue de plataforma, sin lógica de negocio).
/// </summary>
public sealed partial class MainWindow : Window
{
    private GlobalHotkeyService? _globalHotkey;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpenedRegisterGlobalHotkey;
        Closed += (_, _) => _globalHotkey?.Dispose();
    }

    private void OnOpenedRegisterGlobalHotkey(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle();
        if (handle is null || DataContext is not MainViewModel vm) return;

        _globalHotkey = new GlobalHotkeyService(this, handle.Handle);
        _globalHotkey.Pressed += () =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.ToggleRoutingCommand.Execute(null);
        };

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ToggleShortcutGesture))
                RegisterGlobalHotkey();
        };

        RegisterGlobalHotkey();
    }

    private void RegisterGlobalHotkey()
    {
        if (_globalHotkey is null || DataContext is not MainViewModel vm) return;

        var gesture = vm.ToggleShortcutGesture;
        if (gesture is null)
        {
            _globalHotkey.Unregister();
            return;
        }

        // Si Windows rechaza el registro, el KeyBinding de la ventana sigue
        // funcionando como fallback con la app enfocada; solo avisamos.
        if (!_globalHotkey.TryRegister(gesture))
            vm.StatusMessage = vm.Texts.ShortcutGlobalFailed;
    }

    private void ShortcutTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key;
        if (key is Avalonia.Input.Key.LeftCtrl or Avalonia.Input.Key.RightCtrl or
            Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift or
            Avalonia.Input.Key.LeftAlt or Avalonia.Input.Key.RightAlt or
            Avalonia.Input.Key.LWin or Avalonia.Input.Key.RWin or
            Avalonia.Input.Key.None)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var gesture = new Avalonia.Input.KeyGesture(key, modifiers);

        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.ToggleRoutingShortcut = gesture.ToString();
        }
    }
}
