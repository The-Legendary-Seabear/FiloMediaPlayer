
namespace Filo.Api.DTOs;

public class LoudnessAnalysisResultRequest
{
    public string WorkerId { get; set; } = string.Empty;

    public double IntegratedLufs { get; set; }

    public double TruePeakDbfs { get; set; }

    public double LoudnessRangeLu { get; set; }

    public double LoudnessThresholdLufs { get; set; }

    public double NormalizationGainDb { get; set; }
}
