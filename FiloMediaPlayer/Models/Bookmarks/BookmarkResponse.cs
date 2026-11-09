using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.Models.Bookmarks
{
    public class BookmarkResponse
    {
        public int MediaBookmarkId { get; set; }

        public int MediaFileId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public double StartSeconds { get; set; }

        public double? EndSeconds { get; set; }

        public string? Color { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public string DisplayTime =>
    EndSeconds.HasValue
        ? $"{TimeSpan.FromSeconds(StartSeconds):hh\\:mm\\:ss} - {TimeSpan.FromSeconds(EndSeconds.Value):hh\\:mm\\:ss}"
        : $"{TimeSpan.FromSeconds(StartSeconds):hh\\:mm\\:ss}";
    }
}
