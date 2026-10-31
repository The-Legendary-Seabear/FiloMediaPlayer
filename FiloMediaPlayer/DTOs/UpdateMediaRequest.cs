using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class UpdateMediaRequest
    {
        public string Title { get; set; } = string.Empty;
        public int? ThumbnailId { get; set; }
    }
}
