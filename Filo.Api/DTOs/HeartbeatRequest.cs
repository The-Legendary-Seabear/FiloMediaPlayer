using System.ComponentModel.DataAnnotations;

namespace Filo.Api.DTOs
{
    public class HeartbeatRequest
    {
        [Required]
        [StringLength(100)]
        public string WorkerId { get; set; } = string.Empty;
    }
}
