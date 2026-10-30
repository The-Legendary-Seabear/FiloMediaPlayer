using FiloMediaPlayer.DTOs;

namespace FiloMediaPlayer.Components;

public partial class ThumbnailPickerView : ContentView
{
    public static readonly BindableProperty ThumbnailsProperty =
    BindableProperty.Create(nameof(Thumbnails),typeof(List<ThumbnailResponse>),typeof(ThumbnailPickerView),defaultValue: null,propertyChanged: OnThumbnailsChanged);

    public static readonly BindableProperty SelectedThumbnailIdProperty =
    BindableProperty.Create(nameof(SelectedThumbnailId),typeof(int?), typeof(ThumbnailPickerView), defaultValue: null,defaultBindingMode: BindingMode.TwoWay, propertyChanged: OnSelectedThumbnailChanged);

    public List<ThumbnailResponse>? Thumbnails
    {
        get => (List<ThumbnailResponse>?)GetValue(ThumbnailsProperty);
        set => SetValue(ThumbnailsProperty, value);
    }

    public int? SelectedThumbnailId
    {
        get => (int?)GetValue(SelectedThumbnailIdProperty);
        set => SetValue(SelectedThumbnailIdProperty, value);
    }
    public ThumbnailPickerView()
	{
		InitializeComponent();
	}

    private static void OnThumbnailsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var picker = (ThumbnailPickerView)bindable;

        picker.ThumbnailCollection.ItemsSource = newValue as List<ThumbnailResponse> ?? new List<ThumbnailResponse>();
        picker.UpdateSelectedThumbnailDisplay();
    }

    private static void OnSelectedThumbnailChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var picker = (ThumbnailPickerView)bindable;

        picker.UpdateSelectedThumbnailDisplay();
    }

    private void OnThumbnailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = e.CurrentSelection.FirstOrDefault();

        if(selected is ThumbnailResponse thumbnail)
        {
            SelectedThumbnailId = thumbnail.ThumbnailId;
        }
    }

    private void UpdateSelectedThumbnailDisplay()
    {

        var thumbnail = Thumbnails?.FirstOrDefault(t => t.ThumbnailId == SelectedThumbnailId);

        var visibleThumbnail = ThumbnailCollection.ItemsSource?.OfType<ThumbnailResponse>().FirstOrDefault(t => t.ThumbnailId == SelectedThumbnailId);

        ThumbnailCollection.SelectedItem = visibleThumbnail;

        SelectedThumbnailLabel.Text = thumbnail != null ? $"Selected: {thumbnail.Name}" : "Selected: None";
    }

    private void OnRemoveThumbnailClicked(object sender, EventArgs e)
    {
        SelectedThumbnailId = null;
    }

    private void OnThumbnailSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = e.NewTextValue ?? string.Empty;
        var thumbnails = Thumbnails?.Where(t => t.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<ThumbnailResponse>();
        ThumbnailCollection.ItemsSource = thumbnails;
        UpdateSelectedThumbnailDisplay();
    }
}