using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class MediaResponse
    {
        public int MediaFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string S3ObjectKey { get; set; } = string.Empty;

        public int? ThumbnailId { get; set; }
        public string? ThumbnailUrl { get; set; }
    }
}
