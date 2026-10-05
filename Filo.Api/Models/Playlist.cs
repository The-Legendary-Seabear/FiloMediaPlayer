namespace Filo.Api.Models
{
    public class Playlist
    {
        public int PlaylistId { get; set; }

        public int UserId { get; set; }

        public int? ThumbnailId { get; set; }

        public string Name { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        public Thumbnail? Thumbnail { get; set; }

        public ICollection<PlaylistMedia> PlaylistMedia { get; set; }
            = new List<PlaylistMedia>();
    }
}