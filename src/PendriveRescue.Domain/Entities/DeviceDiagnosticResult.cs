using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Entities;

/// <summary>
/// Result of a read-only pendrive diagnosis. The recommendation is intentionally action-oriented for the UI.
/// </summary>
public sealed class DeviceDiagnosticResult
{
    public DeviceProblemKind ProblemKind { get; set; } = DeviceProblemKind.Unknown;
    public string Title { get; set; } = "Unknown problem";
    public string Details { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public bool CanAttemptSafeRepair { get; set; }
    public bool ShouldUseDeepScan { get; set; }
    public bool IsLikelyPhysicalDamage { get; set; }
}
