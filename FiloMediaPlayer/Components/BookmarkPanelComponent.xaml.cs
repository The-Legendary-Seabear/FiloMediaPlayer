using FiloMediaPlayer.Models.Bookmarks;

namespace FiloMediaPlayer.Components;

public partial class BookmarkPanelComponent : ContentView
{
    private List<BookmarkResponse> _bookmarks = new();

    public event EventHandler<double>? SeekRequested;
    public event EventHandler? AddRequested;
    public event EventHandler<BookmarkResponse>? OptionsRequested;

    public BookmarkPanelComponent()
    {
        InitializeComponent();
        BookmarkSortPicker.SelectedIndex = 0;
    }

    public void SetBookmarks(IEnumerable<BookmarkResponse> bookmarks)
    {
        _bookmarks = bookmarks.ToList();
        RefreshBookmarks();
    }

    private void RefreshBookmarks()
    {
        IEnumerable<BookmarkResponse> results = _bookmarks;

        string search = BookmarkSearchEntry.Text?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            results = results.Where(b =>
                b.Title.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }

        results = BookmarkSortPicker.SelectedIndex switch
        {
            1 => results.OrderByDescending(b => b.StartSeconds),
            2 => results.OrderBy(b => b.Title),
            3 => results.OrderByDescending(b => b.Title),
            _ => results.OrderBy(b => b.StartSeconds)
        };

        BookmarkList.ItemsSource = results.ToList();

        ToggleBookmarksButton.Text =
            $"Bookmarks ({_bookmarks.Count}) " +
            (BookmarkContent.IsVisible ? "▲" : "▼");
    }

    private void OnToggleBookmarksClicked(object sender, EventArgs e)
    {
        BookmarkContent.IsVisible = !BookmarkContent.IsVisible;
        RefreshBookmarks();
    }

    private void OnBookmarkSearchChanged(
        object sender, TextChangedEventArgs e)
    {
        RefreshBookmarks();
    }

    private void OnBookmarkSortChanged(object sender, EventArgs e)
    {
        RefreshBookmarks();
    }

    private void OnBookmarkSelected(
        object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault()
            is not BookmarkResponse bookmark)
        {
            return;
        }

        SeekRequested?.Invoke(this, bookmark.StartSeconds);

        BookmarkList.SelectedItem = null;
    }

    private void OnAddBookmarkClicked(object sender, EventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBookmarkOptionsClicked(object sender, EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter is BookmarkResponse bookmark)
        {
            OptionsRequested?.Invoke(this, bookmark);
        }
    }

    public void Collapse()
    {
        BookmarkContent.IsVisible = false;
    }
}