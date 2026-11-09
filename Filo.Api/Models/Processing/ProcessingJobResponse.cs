namespace Filo.Api.Models.Processing
{
    public class ProcessingJobResponse
    {
        public int ProcessingJobId { get; set; }
        public int? MediaFileId { get; set; }
        public int? OutputMediaFileId { get; set; }
        public string JobType { get; set; } = string.Empty;
        public ProcessingJobStatus Status { get; set; }
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
