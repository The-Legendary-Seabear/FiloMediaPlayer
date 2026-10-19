using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class UpdatePlaylistRequest
    {
        public string Name { get; set; } = string.Empty;
        public int? ThumbnailId { get; set; }
    }
}
