namespace Filo.Api.Models
{
    public class Thumbnail
    {
        public int ThumbnailId { get; set; }

        public int UserId { get; set; }

        public string S3ObjectKey { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;
    }
}