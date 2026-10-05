namespace Filo.Api.Models
{
    public class MediaFile
    {
        public int MediaFileId { get; set; }

        public int UserId { get; set; }

        public int? ThumbnailId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;

        public string S3ObjectKey { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        public double? DurationSeconds { get; set; }

        public DateTime UploadedAt { get; set; }

        public ApplicationUser User { get; set; } = null!;

        public Thumbnail? Thumbnail { get; set; }
    }
}