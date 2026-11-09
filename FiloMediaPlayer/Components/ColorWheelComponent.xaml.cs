using Microsoft.Maui.Graphics;

namespace FiloMediaPlayer.Components;

public partial class ColorWheelComponent : ContentView
{
    private readonly ColorWheelDrawable _drawable = new();

    private double _hue;
    private double _saturation = 1;
    private double _brightness = 1;

    public string SelectedHex { get; private set; } = "#FF0000";

    public event EventHandler<string>? ColorChanged;

    public ColorWheelComponent()
    {
        InitializeComponent();

        WheelView.Drawable = _drawable;
        UpdateColor();
    }

    private void OnWheelInteraction(
        object sender,
        TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
            return;

        var position = e.Touches[0];

        double centerX = WheelView.Width / 2;
        double centerY = WheelView.Height / 2;

        double dx = position.X - centerX;
        double dy = position.Y - centerY;

        double distance = Math.Sqrt(dx * dx + dy * dy);
        double radius = Math.Min(
            WheelView.Width,
            WheelView.Height) / 2 - 25;

        // Respond only when interacting with the ring
        if (distance < radius - 25 ||
            distance > radius + 25)
        {
            return;
        }

        double angle = Math.Atan2(dx, -dy)
            * 180 / Math.PI;

        _hue = (angle + 360) % 360;

        UpdateColor();
    }

    private void OnSliderChanged(
        object sender,
        ValueChangedEventArgs e)
    {
        if (SaturationSlider == null ||
            BrightnessSlider == null)
        {
            return;
        }

        _saturation = SaturationSlider.Value / 100;
        _brightness = BrightnessSlider.Value / 100;

        SaturationLabel.Text =
            $"{SaturationSlider.Value:F0}%";

        BrightnessLabel.Text =
            $"{BrightnessSlider.Value:F0}%";

        UpdateColor();
    }

    private void UpdateColor()
    {
        Color color = Color.FromHsv(
            (float)(_hue / 360),
            (float)_saturation,
            (float)_brightness);

        int r = (int)Math.Round(color.Red * 255);
        int g = (int)Math.Round(color.Green * 255);
        int b = (int)Math.Round(color.Blue * 255);

        SelectedHex = $"#{r:X2}{g:X2}{b:X2}";

        _drawable.Hue = _hue;
        _drawable.PreviewColor = color;

        SelectedColorPreview.BackgroundColor = color;
        HexColorLabel.Text = SelectedHex;

        WheelView.Invalidate();

        ColorChanged?.Invoke(this, SelectedHex);
    }
}