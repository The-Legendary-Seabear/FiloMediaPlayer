namespace Filo.Api.DTOs
{
    public class FailProcessingJobRequest
    {
        public string WorkerId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
