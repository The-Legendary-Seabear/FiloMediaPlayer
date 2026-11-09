using System.ComponentModel.DataAnnotations;

namespace Filo.Api.Models.Bookmarks
{
    public class CreateBookmarkRequest
    {
        [Required]
        public int MediaFileId { get; set; }

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }

        [Required]
        public double StartSeconds { get; set; }

        public double? EndSeconds { get; set; }

        [StringLength(20)]
        public string? Color { get; set; }
    }
}
