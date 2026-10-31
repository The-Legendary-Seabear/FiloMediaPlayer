namespace Filo.Api.DTOs
{
    public class UpdateMediaRequest
    {
        public string Title { get; set; } = string.Empty;
        public int? ThumbnailId { get; set; }
    }
}
