using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class PlaylistResponse
    {
        public int PlaylistId { get; set; }
        public string Name { get; set; } = "";
        public int? ThumbnailId { get; set; }
    }
}
