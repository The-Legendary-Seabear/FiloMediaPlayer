using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class ThumbnailResponse
    {
        public int ThumbnailId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }
}
