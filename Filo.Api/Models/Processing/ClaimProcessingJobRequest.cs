using System.ComponentModel.DataAnnotations;

namespace Filo.Api.Models.Processing
{
    public class ClaimProcessingJobRequest
    {
        [Required]
        [StringLength(100)]
        public string WorkerId { get; set; } = string.Empty;
    }
}
