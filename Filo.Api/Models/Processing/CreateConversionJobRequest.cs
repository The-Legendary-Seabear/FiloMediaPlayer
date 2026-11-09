namespace Filo.Api.Models.Processing
{
    public class CreateConversionJobRequest
    {
        public int MediaFileId { get; set; }
        public string TargetFormat { get; set; } = string.Empty;
        public string? VideoCodec { get; set; }
    }
}
