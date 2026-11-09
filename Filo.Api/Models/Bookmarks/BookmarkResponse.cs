namespace Filo.Api.Models.Bookmarks
{
    public class BookmarkResponse
    {
        public int MediaBookmarkId { get; set; }

        public int MediaFileId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public double StartSeconds { get; set; }

        public double? EndSeconds { get; set; }

        public string? Color { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
