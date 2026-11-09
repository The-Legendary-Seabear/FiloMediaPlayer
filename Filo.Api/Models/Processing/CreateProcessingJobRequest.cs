namespace Filo.Api.Models.Processing
{
    public class CreateProcessingJobRequest
    {
        public int MediaFileId { get; set; }
        public string JobType { get; set; } = string.Empty;
        public string TargetFormat { get; set; } = string.Empty;
    }
}
