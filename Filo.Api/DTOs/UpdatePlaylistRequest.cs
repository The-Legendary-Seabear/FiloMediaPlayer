namespace Filo.Api.DTOs
{
    public class UpdatePlaylistRequest
    {
        public string Name { get; set; } = string.Empty;
        public int? ThumbnailId { get; set; }
    }
}
