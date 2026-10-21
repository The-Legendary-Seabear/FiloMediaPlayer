namespace Filo.Api.DTOs
{
    public class PlaybackMediaResponse
    {
        public int MediaFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PlaybackUrl { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
