
namespace Filo.Api.DTOs;

public class CreateMediaGraftingJobRequest
{
    public string Title { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public List<int> MediaFileIds { get; set; } = new();
}
