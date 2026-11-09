using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.Models.Bookmarks
{
    public class UpdateBookmarkRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double StartSeconds { get; set; }
        public double? EndSeconds { get; set; }
        public string? Color { get; set; }
    }
}
