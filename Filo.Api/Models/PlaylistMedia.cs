using Filo.Api.Migrations;

namespace Filo.Api.Models
{
    public class PlaylistMedia
    {
        public int PlaylistId { get; set; }

        public int MediaFileId { get; set; }

        public int Position { get; set; }

        public Playlist Playlist { get; set; } = null!;

        public MediaFile MediaFile { get; set; } = null!;
    }
}