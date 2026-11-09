namespace FiloMediaPlayer.Pages;

public partial class ColorPickerPage : ContentPage
{
    public string? SelectedColor { get; private set; }

    public ColorPickerPage()
    {
        InitializeComponent();
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        SelectedColor = null;
        await Navigation.PopModalAsync();
    }

    private async void OnApplyClicked(object sender, EventArgs e)
    {
        SelectedColor = ColorWheel.SelectedHex;
        await Navigation.PopModalAsync();
    }
}