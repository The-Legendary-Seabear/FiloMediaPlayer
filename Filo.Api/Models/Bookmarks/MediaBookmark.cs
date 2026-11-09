namespace Filo.Api.Models.Bookmarks
{
    public class MediaBookmark
    {
        public int MediaBookmarkId { get; set; }

        public int MediaFileId { get; set; }
        public int OwnerUserId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public double StartSeconds { get; set; }
        public double? EndSeconds { get; set; }

        public string? Color { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public MediaFile MediaFile { get; set; } = null!;
        public ApplicationUser OwnerUser { get; set; } = null!;
    }
}
