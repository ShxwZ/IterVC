using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IterVC.Core.Models;

namespace IterVC.Desktop.Views.Controls;

public sealed class CompactAudioMeter : Control
{
    public static readonly StyledProperty<float> LevelDbProperty =
        AvaloniaProperty.Register<CompactAudioMeter, float>(nameof(LevelDb), AudioLevelSnapshot.MinimumDb);
    public static readonly StyledProperty<float> PeakDbProperty =
        AvaloniaProperty.Register<CompactAudioMeter, float>(nameof(PeakDb), AudioLevelSnapshot.MinimumDb);
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<CompactAudioMeter, bool>(nameof(IsActive));
    public static readonly StyledProperty<float?> ThresholdDbProperty =
        AvaloniaProperty.Register<CompactAudioMeter, float?>(nameof(ThresholdDb));
    public static readonly StyledProperty<bool> UseGateColorsProperty =
        AvaloniaProperty.Register<CompactAudioMeter, bool>(nameof(UseGateColors));
    public static readonly StyledProperty<bool> IsGateOpenProperty =
        AvaloniaProperty.Register<CompactAudioMeter, bool>(nameof(IsGateOpen));
    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(TrackBrush), Brushes.Transparent);
    public static readonly StyledProperty<IBrush> SafeLevelBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(SafeLevelBrush), Brushes.Green);
    public static readonly StyledProperty<IBrush> WarningLevelBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(WarningLevelBrush), Brushes.Orange);
    public static readonly StyledProperty<IBrush> CriticalLevelBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(CriticalLevelBrush), Brushes.Red);
    public static readonly StyledProperty<IBrush> PeakBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(PeakBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> ThresholdBrushProperty =
        AvaloniaProperty.Register<CompactAudioMeter, IBrush>(nameof(ThresholdBrush), Brushes.Orange);

    static CompactAudioMeter()
    {
        AffectsRender<CompactAudioMeter>(
            LevelDbProperty, PeakDbProperty, IsActiveProperty, ThresholdDbProperty,
            UseGateColorsProperty, IsGateOpenProperty, TrackBrushProperty,
            SafeLevelBrushProperty, WarningLevelBrushProperty, CriticalLevelBrushProperty,
            PeakBrushProperty, ThresholdBrushProperty);
    }

    public float LevelDb { get => GetValue(LevelDbProperty); set => SetValue(LevelDbProperty, value); }
    public float PeakDb { get => GetValue(PeakDbProperty); set => SetValue(PeakDbProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public float? ThresholdDb { get => GetValue(ThresholdDbProperty); set => SetValue(ThresholdDbProperty, value); }
    public bool UseGateColors { get => GetValue(UseGateColorsProperty); set => SetValue(UseGateColorsProperty, value); }
    public bool IsGateOpen { get => GetValue(IsGateOpenProperty); set => SetValue(IsGateOpenProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush SafeLevelBrush { get => GetValue(SafeLevelBrushProperty); set => SetValue(SafeLevelBrushProperty, value); }
    public IBrush WarningLevelBrush { get => GetValue(WarningLevelBrushProperty); set => SetValue(WarningLevelBrushProperty, value); }
    public IBrush CriticalLevelBrush { get => GetValue(CriticalLevelBrushProperty); set => SetValue(CriticalLevelBrushProperty, value); }
    public IBrush PeakBrush { get => GetValue(PeakBrushProperty); set => SetValue(PeakBrushProperty, value); }
    public IBrush ThresholdBrush { get => GetValue(ThresholdBrushProperty); set => SetValue(ThresholdBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(TrackBrush, bounds, 2);

        if (IsActive)
        {
            var levelWidth = bounds.Width * Normalize(LevelDb);
            if (UseGateColors)
            {
                var fill = IsGateOpen ? SafeLevelBrush : WarningLevelBrush;
                context.FillRectangle(fill,
                    new Rect(0, 0, levelWidth, bounds.Height), 2);
            }
            else
            {
                var fill = LevelDb switch
                {
                    >= -3f => CriticalLevelBrush,
                    >= -12f => WarningLevelBrush,
                    _ => SafeLevelBrush
                };
                context.FillRectangle(fill,
                    new Rect(0, 0, levelWidth, bounds.Height), 2);
            }

            var peakX = bounds.Width * Normalize(PeakDb);
            context.DrawLine(new Pen(PeakBrush, 1), new Point(peakX, 0), new Point(peakX, bounds.Height));
        }

        if (ThresholdDb is { } threshold)
        {
            var thresholdX = bounds.Width * Normalize(threshold);
            context.DrawLine(new Pen(ThresholdBrush, 1),
                new Point(thresholdX, 0), new Point(thresholdX, bounds.Height));
        }
    }

    private static double Normalize(float value) =>
        (Math.Clamp(value, AudioLevelSnapshot.MinimumDb, AudioLevelSnapshot.MaximumDb)
         - AudioLevelSnapshot.MinimumDb)
        / (AudioLevelSnapshot.MaximumDb - AudioLevelSnapshot.MinimumDb);

}
