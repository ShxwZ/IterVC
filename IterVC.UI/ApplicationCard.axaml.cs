using Avalonia;
using Avalonia.Controls;
using IterVC.Core.Models;

namespace IterVC.UI;

/// <summary>
/// Canonical application-selection row, shared verbatim by Windows and Linux.
/// The card knows only presentation data, never OS-specific stream IDs/services.
/// </summary>
public sealed partial class ApplicationCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ApplicationCard, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<ApplicationCard, string>(nameof(Subtitle), "");
    public static readonly StyledProperty<string> IdentifierLabelProperty =
        AvaloniaProperty.Register<ApplicationCard, string>(nameof(IdentifierLabel), "PID");
    public static readonly StyledProperty<string> IdentifierProperty =
        AvaloniaProperty.Register<ApplicationCard, string>(nameof(Identifier), "");
    public static readonly StyledProperty<bool> IsIncludedProperty =
        AvaloniaProperty.Register<ApplicationCard, bool>(nameof(IsIncluded));
    public static readonly StyledProperty<float> LevelDbProperty =
        AvaloniaProperty.Register<ApplicationCard, float>(nameof(LevelDb), AudioLevelSnapshot.MinimumDb);
    public static readonly StyledProperty<float> PeakDbProperty =
        AvaloniaProperty.Register<ApplicationCard, float>(nameof(PeakDb), AudioLevelSnapshot.MinimumDb);
    public static readonly StyledProperty<bool> MeterActiveProperty =
        AvaloniaProperty.Register<ApplicationCard, bool>(nameof(MeterActive));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string IdentifierLabel { get => GetValue(IdentifierLabelProperty); set => SetValue(IdentifierLabelProperty, value); }
    public string Identifier { get => GetValue(IdentifierProperty); set => SetValue(IdentifierProperty, value); }
    public bool IsIncluded { get => GetValue(IsIncludedProperty); set => SetValue(IsIncludedProperty, value); }
    public float LevelDb { get => GetValue(LevelDbProperty); set => SetValue(LevelDbProperty, value); }
    public float PeakDb { get => GetValue(PeakDbProperty); set => SetValue(PeakDbProperty, value); }
    public bool MeterActive { get => GetValue(MeterActiveProperty); set => SetValue(MeterActiveProperty, value); }

    public ApplicationCard() => InitializeComponent();
}
