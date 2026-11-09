
using FiloMediaPlayer.Models.Bookmarks;
using Microsoft.Maui.Graphics;

namespace FiloMediaPlayer.Components;

public class BookmarkTimelineDrawable : IDrawable
{
    public IReadOnlyList<BookmarkResponse> Bookmarks { get; set; }
        = Array.Empty<BookmarkResponse>();

    public double DurationSeconds { get; set; }
    public float TrackInset { get; set; } = 0f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (DurationSeconds <= 0 ||
            !double.IsFinite(DurationSeconds) ||
            dirtyRect.Width <= 24)
        {
            return;
        }

        float left = TrackInset;
        float right = dirtyRect.Width - TrackInset;
        float trackWidth = right - left;
        float centerY = dirtyRect.Height / 2f;

        float GetX(double seconds)
        {
            double fraction = Math.Clamp(
                seconds / DurationSeconds, 0, 1);

            return left + (float)(fraction * trackWidth);
        }

        // Draw section ranges first so point markers
        // remain visible on top.

        // Draw translucent section ranges first.
        foreach (var bookmark in Bookmarks)
        {
            if (!bookmark.EndSeconds.HasValue)
                continue;

            double start = bookmark.StartSeconds;
            double end = bookmark.EndSeconds.Value;

            if (!double.IsFinite(start) ||
                !double.IsFinite(end) ||
                end <= start)
            {
                continue;
            }

            float startX = GetX(start);
            float endX = GetX(end);

            // Preserve the bookmark's color but reduce opacity.
            canvas.FillColor = GetBookmarkColor(bookmark)
                .WithAlpha(0.30f);

            canvas.FillRoundedRectangle(
                startX,
                centerY - 2.5f,
                Math.Max(endX - startX, 2),
                5,
                2.5f);
        }


        // Draw point bookmarks.
        foreach (var bookmark in Bookmarks)
        {
            if (bookmark.EndSeconds.HasValue)
                continue;

            if (!double.IsFinite(bookmark.StartSeconds))
                continue;

            float x = GetX(bookmark.StartSeconds);

            canvas.FillColor = GetBookmarkColor(bookmark).WithAlpha(0.75f);

            canvas.FillCircle(x, centerY, 3.5f);
        }
    }

    private static Color GetBookmarkColor(
        BookmarkResponse bookmark)
    {
        if (string.IsNullOrWhiteSpace(bookmark.Color))
            return Color.FromArgb("#8B5CF6");

        try
        {
            return Color.FromArgb(bookmark.Color);
        }
        catch
        {
            return Color.FromArgb("#8B5CF6");
        }
    }
}
