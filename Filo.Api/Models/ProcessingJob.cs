namespace Filo.Api.Models
{
    public class ProcessingJob
    {
        public int ProcessingJobId { get; set; }

        // Ownership
        public int OwnerUserId { get; set; }
        public ApplicationUser OwnerUser { get; set; } = null!;

        // Source and output media
        public int? MediaFileId { get; set; }
        public MediaFile? MediaFile { get; set; }

        public int? OutputMediaFileId { get; set; }
        public MediaFile? OutputMediaFile { get; set; }

        // Job information
        public string JobType { get; set; } = string.Empty;
        public ProcessingJobStatus Status { get; set; }
            = ProcessingJobStatus.Queued;

        public int Progress { get; set; } = 0;

        // Processing configuration
        public string ParametersJson { get; set; } = "{}";

        // Results and errors
        public string? ErrorMessage { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Worker tracking
        public string? WorkerId { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public int AttemptCount { get; set; } = 0;
    }

    public enum ProcessingJobStatus
    {
        Queued,
        Processing,
        Completed,
        Failed,
        Canceled
    }
}
