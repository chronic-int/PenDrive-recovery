using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Evaluates synthetic evidence only. Windows collection remains separate so every rule can be tested without hardware.
/// </summary>
public sealed class DeviceDiagnosticEngine : IDeviceDiagnosticEngine
{
    // Lower values win. Identity and hardware-instability findings intentionally outrank filesystem symptoms.
    private static readonly IReadOnlyDictionary<DeviceDiagnosticCondition, int> Precedence =
        new Dictionary<DeviceDiagnosticCondition, int>
        {
            [DeviceDiagnosticCondition.DeviceIdentityChanged] = 1,
            [DeviceDiagnosticCondition.DeviceRemoved] = 2,
            [DeviceDiagnosticCondition.NoMedia] = 3,
            [DeviceDiagnosticCondition.IntermittentConnection] = 4,
            [DeviceDiagnosticCondition.SevereIoFailure] = 5,
            [DeviceDiagnosticCondition.LikelyPhysicalDamage] = 6,
            [DeviceDiagnosticCondition.ActiveMalwareThreat] = 7,
            [DeviceDiagnosticCondition.SuspiciousCapacity] = 8,
            [DeviceDiagnosticCondition.CapacityMismatch] = 8,
            [DeviceDiagnosticCondition.PartitionTableProblem] = 9,
            [DeviceDiagnosticCondition.NoPartitionTable] = 9,
            [DeviceDiagnosticCondition.UnallocatedDisk] = 9,
            [DeviceDiagnosticCondition.MissingPartition] = 9,
            [DeviceDiagnosticCondition.RawFileSystem] = 10,
            [DeviceDiagnosticCondition.CorruptedFileSystem] = 10,
            [DeviceDiagnosticCondition.UnsupportedFileSystem] = 10,
            [DeviceDiagnosticCondition.InaccessibleVolume] = 11,
            [DeviceDiagnosticCondition.OfflineDisk] = 11,
            [DeviceDiagnosticCondition.ReadOnlyDisk] = 12,
            [DeviceDiagnosticCondition.MissingDriveLetter] = 13,
            [DeviceDiagnosticCondition.MalwareSymptoms] = 14,
            [DeviceDiagnosticCondition.ReadErrorsDetected] = 14,
            [DeviceDiagnosticCondition.MountedAndReadable] = 15,
            [DeviceDiagnosticCondition.AnalysisIncomplete] = 16,
            [DeviceDiagnosticCondition.Unknown] = 17
        };

    public DeviceDiagnosticResult Evaluate(
        Guid analysisId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        DeviceDiagnosticEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var findings = EvaluateFindings(evidence);
        var coreEvidenceComplete = IsCoreEvidenceComplete(evidence);
        if (!coreEvidenceComplete || evidence.CollectionWarnings.Count > 0)
        {
            findings.Add(CreateIncompleteFinding(evidence));
        }

        if (findings.Count == 0)
        {
            findings.Add(CreateIncompleteFinding(evidence));
        }

        var orderedFindings = findings
            .DistinctBy(finding => finding.Code)
            .OrderBy(finding => GetPrecedence(finding.Condition))
            .ThenByDescending(finding => finding.Severity)
            .ToArray();
        var primary = orderedFindings[0];
        var likelyPhysicalDamage = orderedFindings.Any(finding =>
            finding.Condition is DeviceDiagnosticCondition.LikelyPhysicalDamage
                or DeviceDiagnosticCondition.SevereIoFailure);
        var recoveryRecommendedFirst = orderedFindings.Any(finding => finding.RecoveryRecommendedFirst);
        var actions = BuildActions(primary.Condition, orderedFindings, evidence, recoveryRecommendedFirst);
        ValidateRecommendationConsistency(primary.Condition, likelyPhysicalDamage, recoveryRecommendedFirst, actions);
        var limitations = BuildLimitations(evidence);
        var actionsToAvoid = orderedFindings
            .SelectMany(finding => finding.ActionsToAvoid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var safeRepairMayBeAppropriate = actions.Any(action =>
            action.Kind == DiagnosticActionKind.TrySafeRepair && action.Enabled);
        var destructiveRepairMayBeAppropriate = actions.Any(action =>
            action.Kind == DiagnosticActionKind.ConsiderDestructiveRepair && action.Enabled);

        return new DeviceDiagnosticResult
        {
            AnalysisId = analysisId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            TargetIdentity = evidence.DeviceIdentity,
            Evidence = evidence,
            PrimaryCondition = primary.Condition,
            Confidence = primary.Confidence,
            Severity = primary.Severity,
            Title = primary.Title,
            Summary = BuildSummary(primary, orderedFindings),
            Findings = orderedFindings,
            EvidenceSummary = BuildEvidenceSummary(evidence),
            RecommendedActions = actions,
            ActionsToAvoid = actionsToAvoid,
            RecoveryRecommendedFirst = recoveryRecommendedFirst,
            SafeRepairMayBeAppropriate = safeRepairMayBeAppropriate,
            DestructiveRepairMayBeAppropriate = destructiveRepairMayBeAppropriate,
            LikelyPhysicalDamage = likelyPhysicalDamage,
            AnalysisComplete = coreEvidenceComplete && evidence.CollectionWarnings.Count == 0,
            Limitations = limitations
        };
    }

    private static List<DiagnosticFinding> EvaluateFindings(DeviceDiagnosticEvidence evidence)
    {
        var findings = new List<DiagnosticFinding>();

        if (IsYes(evidence.IdentityChangedDuringAnalysis))
        {
            findings.Add(Finding(
                "USB-IDENTITY-001",
                DeviceDiagnosticCondition.DeviceIdentityChanged,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "USB identity changed during analysis",
                "The physical identity at the end of analysis did not match the device that was selected. No reliable diagnosis or repair recommendation can be made.",
                ["The final physical-device identity check did not match the initial validated identity."],
                ["Refresh the device list and select the USB again."],
                ["Do not scan or repair the device until its identity is stable."]));
        }

        if (IsNo(evidence.DevicePresent)
            || (IsYes(evidence.DeviceDisconnectedDuringAnalysis) && !IsYes(evidence.DeviceReappearedDuringAnalysis)))
        {
            findings.Add(Finding(
                "USB-CONNECTION-001",
                DeviceDiagnosticCondition.DeviceRemoved,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "USB was removed during analysis",
                "The selected physical USB was no longer present when Pendrive Rescue performed its final identity check.",
                ["The device could not be revalidated after evidence collection."],
                ["Reconnect the USB directly and refresh the device list."],
                ["Do not repair a disk until the same physical identity can be verified."]));
        }

        if (IsYes(evidence.IsNoMedia))
        {
            findings.Add(Finding(
                "USB-MEDIA-001",
                DeviceDiagnosticCondition.NoMedia,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "USB controller reports no media",
                "Windows can see a storage controller but no usable media capacity is available. This can indicate controller or flash-memory failure.",
                ["The disk reported a no-media state or no usable capacity."],
                ["Try one different USB port or computer.", "Seek professional recovery if the files are important."],
                ["Do not format or repair a no-media device."]));
        }

        if (IsYes(evidence.DeviceDisconnectedDuringAnalysis) && IsYes(evidence.DeviceReappearedDuringAnalysis))
        {
            findings.Add(Finding(
                "USB-CONNECTION-002",
                DeviceDiagnosticCondition.IntermittentConnection,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "USB connection is unstable",
                "The USB disappeared and returned during analysis. Filesystem conclusions are unreliable until the connection remains stable.",
                ["Arrival or removal evidence changed while analysis was running."],
                ["Reconnect without a hub.", "Try another USB port or computer."],
                ["Avoid repair and repeated deep reads until the connection is stable."],
                recoveryFirst: true));
        }

        var partialRead = evidence.ReadProbeBytesRequested > 0
            && evidence.ReadProbeBytesCompleted < evidence.ReadProbeBytesRequested;
        var severeIo = evidence.IoErrorCount >= 2;
        if (severeIo)
        {
            findings.Add(Finding(
                "USB-IO-002",
                DeviceDiagnosticCondition.SevereIoFailure,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "Severe read instability detected",
                "The bounded read probe encountered repeated or combined I/O failures and could not read the requested range reliably.",
                [$"Read {evidence.ReadProbeBytesCompleted:N0} of {evidence.ReadProbeBytesRequested:N0} requested bytes.",
                 $"Recorded {evidence.IoErrorCount} I/O error(s)."],
                ["Stop repeated scanning.", "Try one different USB port or computer.", "Seek professional recovery for important data."],
                ["Avoid CHKDSK, formatting, Safe Repair, destructive repair, and repeated Deep Scans."],
                recoveryFirst: true));
        }

        var strongPhysicalEvidence = evidence.IoErrorCount >= 2
            && (partialRead
                || IsYes(evidence.TimedOut)
                || IsYes(evidence.DeviceDisconnectedDuringAnalysis));
        if (strongPhysicalEvidence)
        {
            findings.Add(Finding(
                "USB-PHYSICAL-001",
                DeviceDiagnosticCondition.LikelyPhysicalDamage,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "Possible physical device failure",
                "Multiple low-level failures occurred together with a partial read, timeout, or disconnection. Physical failure is likely, but cannot be proven without specialist hardware testing.",
                ["Multiple independent low-level failure signals were collected."],
                ["Stop using the USB and seek professional recovery if the files are important."],
                ["Do not format, repair, or repeatedly scan the device."],
                recoveryFirst: true));
        }
        else if (evidence.IoErrorCount == 1 || evidence.ReadErrorCount == 1 || IsYes(evidence.TimedOut))
        {
            findings.Add(Finding(
                "USB-IO-001",
                DeviceDiagnosticCondition.ReadErrorsDetected,
                DiagnosticConfidence.Medium,
                DiagnosticSeverity.Caution,
                "A read problem was detected",
                "One read error or timeout occurred. This is not enough evidence to diagnose physical damage, but the USB should be handled cautiously.",
                ["The bounded read probe did not complete cleanly."],
                ["Try another USB port before relying on the device."],
                ["Do not assume one isolated failure proves physical damage."]));
        }

        if (IsYes(evidence.DefenderThreatRequiresAction))
        {
            findings.Add(Finding(
                "USB-MALWARE-002",
                DeviceDiagnosticCondition.ActiveMalwareThreat,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Critical,
                "Microsoft Defender reports an active threat",
                "Microsoft Defender evidence indicates that security action is still required.",
                ["Defender reported a threat requiring action."],
                ["Review Windows Security, scan the computer, then scan and clean the USB."],
                ["Do not open files normally until the threat is handled."],
                recoveryFirst: true));
        }

        if (IsNo(evidence.CapacityEvidenceIsConsistent))
        {
            findings.Add(Finding(
                "USB-CAPACITY-001",
                DeviceDiagnosticCondition.SuspiciousCapacity,
                DiagnosticConfidence.Medium,
                DiagnosticSeverity.Warning,
                "Suspicious capacity reporting",
                "The available capacity measurements are inconsistent. This does not prove counterfeit hardware, but the device should not be trusted for unique data.",
                [string.IsNullOrWhiteSpace(evidence.CapacityEvidenceReason)
                    ? "Capacity values differed beyond the diagnostic tolerance."
                    : evidence.CapacityEvidenceReason],
                ["Recover important files and use another device for unique data."],
                ["Do not run a destructive capacity test while data is still needed."],
                recoveryFirst: true));
        }

        if (IsYes(evidence.IsOffline))
        {
            findings.Add(Finding(
                "USB-DISK-001",
                DeviceDiagnosticCondition.OfflineDisk,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Warning,
                "Physical disk is offline",
                "Windows reports the disk as offline, so normal volume access is unavailable.",
                ["The Windows disk attribute reports Offline."],
                ["Inspect the device in Disk Management after recovering any needed data."],
                ["Do not initialize or format the disk before recovery."],
                recoveryFirst: true));
        }

        if (IsNo(evidence.HasPartitionTable))
        {
            findings.Add(Finding(
                "USB-PARTITION-001",
                DeviceDiagnosticCondition.NoPartitionTable,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Warning,
                "No recognized partition table",
                "Windows does not report a recognized partition table on the physical USB. The disk may be blank or its partition metadata may be missing.",
                ["Partition style was reported as RAW or absent."],
                ["Run Deep Scan before creating a new partition if files may be needed."],
                ["Do not initialize or format the disk before recovery."],
                recoveryFirst: true));
        }
        else if (evidence.PartitionCount == 0 || IsNo(evidence.HasAllocatedPartition))
        {
            var unallocated = IsYes(evidence.HasPartitionTable) && IsNo(evidence.HasAllocatedPartition);
            findings.Add(Finding(
                unallocated ? "USB-PARTITION-002" : "USB-PARTITION-003",
                unallocated ? DeviceDiagnosticCondition.UnallocatedDisk : DeviceDiagnosticCondition.MissingPartition,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Warning,
                unallocated ? "USB capacity is unallocated" : "No usable partition was found",
                unallocated
                    ? "The disk has partition metadata, but no allocated partition was reported."
                    : "The physical USB is present, but Windows did not report a usable partition.",
                ["No allocated partition is available for normal volume access."],
                ["Run Deep Scan and recover files before creating partitions."],
                ["Do not format or run destructive repair before recovery."],
                recoveryFirst: true));
        }

        if (IsYes(evidence.IsRawFileSystem))
        {
            findings.Add(Finding(
                "USB-RAW-001",
                DeviceDiagnosticCondition.RawFileSystem,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Warning,
                "Likely filesystem corruption",
                "Windows detects the physical USB and volume, but reports the filesystem as RAW. RAW alone is a filesystem symptom, not proof of physical damage.",
                ["The reported filesystem is RAW.", ProbeEvidence(evidence)],
                ["Run Deep Scan, recover important files to another disk, then consider Safe Repair."],
                ["Do not format, run CHKDSK, or use destructive repair before recovery."],
                recoveryFirst: true));
        }
        else if (IsNo(evidence.FileSystemRecognized) && !string.IsNullOrWhiteSpace(evidence.FileSystem))
        {
            findings.Add(Finding(
                "USB-FS-001",
                DeviceDiagnosticCondition.UnsupportedFileSystem,
                DiagnosticConfidence.Medium,
                DiagnosticSeverity.Caution,
                "Filesystem is not recognized by this analysis",
                "The reported filesystem is not in the set that Pendrive Rescue can assess reliably. It may be valid for another operating system.",
                [$"Windows reported filesystem: {evidence.FileSystem}."],
                ["Inspect the USB on a computer that supports this filesystem."],
                ["Do not format it merely because Windows cannot read it."]));
        }

        if (IsYes(evidence.HasMountedVolume)
            && IsYes(evidence.FileSystemRecognized)
            && IsNo(evidence.RootDirectoryReadable))
        {
            findings.Add(Finding(
                "USB-ACCESS-001",
                DeviceDiagnosticCondition.InaccessibleVolume,
                DiagnosticConfidence.Medium,
                DiagnosticSeverity.Warning,
                "Volume inaccessible - cause not yet confirmed",
                "A recognized filesystem and mount point exist, but the root directory could not be read. Access control, encryption, mount failure, corruption, or I/O instability may be responsible.",
                [$"Safe access category: {evidence.AccessFailureCategory}."],
                ["Run Deep Scan if important files are not accessible."],
                ["Do not assume corruption or run CHKDSK before recovery."],
                recoveryFirst: true));
        }

        if (IsYes(evidence.IsReadOnly))
        {
            findings.Add(Finding(
                "USB-READONLY-001",
                DeviceDiagnosticCondition.ReadOnlyDisk,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Caution,
                "USB is read-only",
                "Windows reports a read-only storage attribute. This may be a disk attribute, hardware behavior, or a controller response and does not prove the device is healthy.",
                [string.IsNullOrWhiteSpace(evidence.ReadOnlyEvidenceSource)
                    ? "A read-only state was reported."
                    : $"Read-only source: {evidence.ReadOnlyEvidenceSource}."],
                ["Recover accessible files before considering Safe Repair."],
                ["Avoid repeated write attempts and formatting before recovery."],
                recoveryFirst: true));
        }

        if (string.IsNullOrWhiteSpace(evidence.DriveLetter)
            && IsYes(evidence.HasAllocatedPartition)
            && !IsYes(evidence.IsRawFileSystem))
        {
            findings.Add(Finding(
                "USB-MOUNT-001",
                DeviceDiagnosticCondition.MissingDriveLetter,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Caution,
                "No drive letter is assigned",
                "The physical disk and an allocated partition are present, but Windows has not mounted the volume with a drive letter. This alone is not physical damage.",
                ["An allocated partition exists without a mounted drive letter."],
                ["Run Deep Scan if files are important, inspect Disk Management, then consider Safe Repair after recovery."],
                ["Do not use destructive repair as the first action."],
                recoveryFirst: true));
        }

        var malwareSymptoms = IsYes(evidence.SuspiciousAutorunDetected)
            || IsYes(evidence.SuspiciousShortcutPatternDetected)
            || evidence.SuspiciousLauncherCount > 0;
        if (malwareSymptoms && !IsYes(evidence.DefenderThreatRequiresAction))
        {
            findings.Add(Finding(
                "USB-MALWARE-001",
                DeviceDiagnosticCondition.MalwareSymptoms,
                DiagnosticConfidence.Medium,
                DiagnosticSeverity.Warning,
                "Malware-like USB launchers detected",
                "Root-level metadata resembles common shortcut or script-launcher infections. These symptoms are suspicious, but they are not a Defender-confirmed threat.",
                MalwareEvidence(evidence),
                ["Scan the computer and USB with Defender, recover personal files carefully, then run malware cleanup."],
                ["Do not open suspicious shortcuts, scripts, or executables."],
                recoveryFirst: true));
        }

        if (CanClassifyReadable(evidence))
        {
            findings.Add(Finding(
                "USB-HEALTH-001",
                DeviceDiagnosticCondition.MountedAndReadable,
                DiagnosticConfidence.High,
                DiagnosticSeverity.Information,
                "USB is mounted and readable",
                "The USB is mounted and readable, and no immediate structural problem was detected by this analysis. This does not guarantee that every file or storage sector is healthy.",
                ["Physical identity remained stable.", "The filesystem root and bounded read probe were readable."],
                ["Use Quick Scan to inspect files or run Defender if malware is suspected."],
                ["No repair is recommended for the current evidence."]));
        }

        return findings;
    }

    private static DiagnosticFinding CreateIncompleteFinding(DeviceDiagnosticEvidence evidence)
    {
        var missing = new List<string>();
        if (!IsYes(evidence.IdentityRevalidated) || !IsYes(evidence.FinalIdentityRevalidated))
        {
            missing.Add("A complete before-and-after identity validation was unavailable.");
        }
        if (IsUnknown(evidence.PartitionMetadataAvailable))
        {
            missing.Add("Partition metadata was unavailable.");
        }
        if (IsUnknown(evidence.VolumeMetadataAvailable))
        {
            missing.Add("Volume metadata was unavailable.");
        }
        if (!IsYes(evidence.ReadProbeAttempted))
        {
            missing.Add("The bounded physical read probe was not completed.");
        }
        missing.AddRange(evidence.CollectionWarnings);

        return Finding(
            "USB-ANALYSIS-001",
            DeviceDiagnosticCondition.AnalysisIncomplete,
            DiagnosticConfidence.Low,
            DiagnosticSeverity.Caution,
            "Analysis is incomplete",
            "Some diagnostic evidence could not be collected. Available findings are shown, but missing evidence prevents a complete conclusion.",
            [],
            ["Refresh the device and repeat Analyze if the USB remains stable."],
            ["Do not choose destructive repair from an incomplete diagnosis."],
            missingEvidence: missing);
    }

    private static IReadOnlyList<RecommendedDiagnosticAction> BuildActions(
        DeviceDiagnosticCondition primary,
        IReadOnlyCollection<DiagnosticFinding> findings,
        DeviceDiagnosticEvidence evidence,
        bool recoveryFirst)
    {
        var actions = new List<RecommendedDiagnosticAction>();
        var criticalDeviceState = primary is DeviceDiagnosticCondition.DeviceIdentityChanged
            or DeviceDiagnosticCondition.DeviceRemoved
            or DeviceDiagnosticCondition.NoMedia
            or DeviceDiagnosticCondition.IntermittentConnection
            or DeviceDiagnosticCondition.SevereIoFailure
            or DeviceDiagnosticCondition.LikelyPhysicalDamage;

        switch (primary)
        {
            case DeviceDiagnosticCondition.DeviceIdentityChanged:
                actions.Add(Action(DiagnosticActionKind.RefreshDevices, 1, "Refresh Devices", "Select the physical USB again before doing anything else."));
                actions.Add(Action(DiagnosticActionKind.ReconnectDevice, 2, "Reconnect the USB", "Reconnect it directly and wait for Windows to finish detecting it."));
                AddDisabledRiskyActions(actions, "The physical identity changed during analysis.", disableDeepScan: true);
                break;
            case DeviceDiagnosticCondition.DeviceRemoved:
                actions.Add(Action(DiagnosticActionKind.ReconnectDevice, 1, "Reconnect the USB", "Reconnect the same USB directly without a hub."));
                actions.Add(Action(DiagnosticActionKind.RefreshDevices, 2, "Refresh Devices", "Verify that Pendrive Rescue sees one stable physical disk."));
                actions.Add(Action(DiagnosticActionKind.TryDifferentUsbPort, 3, "Try another USB port", "A direct port can rule out a loose port or hub."));
                AddDisabledRiskyActions(actions, "The device is not currently present and verified.", disableDeepScan: true);
                break;
            case DeviceDiagnosticCondition.NoMedia:
                actions.Add(Action(DiagnosticActionKind.TryDifferentUsbPort, 1, "Try another USB port", "Confirm the no-media state once on a direct port."));
                actions.Add(Action(DiagnosticActionKind.TryDifferentComputer, 2, "Try another computer", "A second system can rule out a local driver issue."));
                actions.Add(Action(DiagnosticActionKind.SeekProfessionalRecovery, 3, "Seek professional recovery", "Software cannot recover data when the controller exposes no readable media."));
                AddDisabledRiskyActions(actions, "No usable media is available to scan or repair.", disableDeepScan: true);
                break;
            case DeviceDiagnosticCondition.IntermittentConnection:
            case DeviceDiagnosticCondition.SevereIoFailure:
            case DeviceDiagnosticCondition.LikelyPhysicalDamage:
                actions.Add(Action(DiagnosticActionKind.StopUsingDevice, 1, "Stop repeated reads", "Further scanning may stress an unstable device."));
                actions.Add(Action(DiagnosticActionKind.TryDifferentUsbPort, 2, "Try one different USB port", "Use a direct port and avoid a hub."));
                actions.Add(Action(DiagnosticActionKind.TryDifferentComputer, 3, "Try one different computer", "Confirm whether the failure follows the USB."));
                actions.Add(Action(DiagnosticActionKind.SeekProfessionalRecovery, 4, "Seek professional recovery", "Choose this when the files are important or irreplaceable."));
                AddDisabledRiskyActions(actions, "Strong instability evidence makes repeated scans and repair unsafe.", disableDeepScan: true);
                break;
            case DeviceDiagnosticCondition.ActiveMalwareThreat:
                actions.Add(Action(DiagnosticActionKind.RunDefenderUsbScan, 1, "Scan the USB with Defender", "Confirm and resolve the active threat before normal file access.", confirmation: true));
                actions.Add(Action(DiagnosticActionKind.CleanMalwareArtifacts, 2, "Clean malware artifacts", "Quarantine suspicious USB launchers after Defender review.", confirmation: true));
                actions.Add(Action(DiagnosticActionKind.EnableUsbProtection, 3, "Enable USB protection", "Enable the AutoRun blocker after cleanup."));
                actions.Add(DisabledAction(DiagnosticActionKind.TrySafeRepair, 90, "Safe Repair", "Repair does not treat malware."));
                break;
            case DeviceDiagnosticCondition.SuspiciousCapacity:
            case DeviceDiagnosticCondition.CapacityMismatch:
                actions.Add(Action(DiagnosticActionKind.RecoverFiles, 1, "Recover important files", "Save important files to another physical disk."));
                actions.Add(Action(DiagnosticActionKind.StopUsingDevice, 2, "Do not trust this USB for unique data", "Capacity evidence is inconsistent and needs separate verification."));
                actions.Add(DisabledAction(DiagnosticActionKind.ConsiderDestructiveRepair, 90, "Erase and Repair", "A destructive capacity test is outside Analyze and must wait until data is no longer needed."));
                break;
            case DeviceDiagnosticCondition.MalwareSymptoms:
                actions.Add(Action(DiagnosticActionKind.RunDefenderUsbScan, 1, "Scan the USB with Defender", "Confirm whether the suspicious launchers are an active threat.", confirmation: true));
                actions.Add(Action(DiagnosticActionKind.RecoverFiles, 2, "Recover personal files carefully", "Save personal files without launching shortcuts or scripts."));
                actions.Add(Action(DiagnosticActionKind.CleanMalwareArtifacts, 3, "Clean malware artifacts", "Quarantine suspicious root launchers after scanning.", confirmation: true));
                actions.Add(Action(DiagnosticActionKind.EnableUsbProtection, 4, "Enable USB protection", "Enable the AutoRun blocker after cleanup."));
                break;
            case DeviceDiagnosticCondition.MountedAndReadable:
                actions.Add(Action(DiagnosticActionKind.RunQuickScan, 1, "Run Quick Scan", "Inspect files through the mounted readable filesystem."));
                actions.Add(Action(DiagnosticActionKind.RunDefenderUsbScan, 2, "Scan with Defender", "Use this when the USB came from an untrusted computer.", confirmation: true));
                break;
            case DeviceDiagnosticCondition.AnalysisIncomplete:
            case DeviceDiagnosticCondition.Unknown:
                actions.Add(Action(DiagnosticActionKind.RefreshDevices, 1, "Refresh Devices", "Repeat identity and storage discovery while the USB remains connected."));
                actions.Add(Action(DiagnosticActionKind.InspectInDiskManagement, 2, "Inspect in Disk Management", "Review whether Windows reports a partition or volume without changing it."));
                AddDisabledRiskyActions(actions, "The diagnosis is incomplete.", disableDeepScan: false);
                break;
            default:
                actions.Add(Action(DiagnosticActionKind.RunDeepScan, 1, "Run Deep Scan", "Search the readable physical USB without relying on normal filesystem access."));
                actions.Add(Action(DiagnosticActionKind.RecoverFiles, 2, "Recover important files", "Save recovered files to another physical disk before repair."));
                actions.Add(Action(DiagnosticActionKind.InspectInDiskManagement, 3, "Inspect in Disk Management", "Review the current partition and volume state without initializing or formatting it."));
                var safeRepairAllowed = IsYes(evidence.IdentityRevalidated)
                    && IsYes(evidence.FinalIdentityRevalidated)
                    && !IsYes(evidence.IsSystemDisk)
                    && !IsYes(evidence.IsBootDisk)
                    && !criticalDeviceState;
                actions.Add(safeRepairAllowed
                    ? Action(DiagnosticActionKind.TrySafeRepair, 4, "Consider Safe Repair after recovery", "It may help mounting, drive-letter, read-only, or repairable filesystem metadata issues.", confirmation: true)
                    : DisabledAction(DiagnosticActionKind.TrySafeRepair, 4, "Safe Repair", "Identity or hardware safety evidence is insufficient."));
                actions.Add(DisabledAction(
                    DiagnosticActionKind.ConsiderDestructiveRepair,
                    90,
                    "Erase and Repair",
                    recoveryFirst
                        ? "Recover important files and try safer options first."
                        : "Analyze never starts destructive repair directly."));
                break;
        }

        var hasMalwareFinding = findings.Any(finding =>
            finding.Condition is DeviceDiagnosticCondition.MalwareSymptoms or DeviceDiagnosticCondition.ActiveMalwareThreat);
        if (hasMalwareFinding
            && !criticalDeviceState
            && actions.All(action => action.Kind != DiagnosticActionKind.RunDefenderUsbScan))
        {
            actions.Add(Action(DiagnosticActionKind.RunDefenderUsbScan, 20, "Scan the USB with Defender", "Security symptoms were also detected.", confirmation: true));
        }

        return actions
            .DistinctBy(action => action.Kind)
            .OrderBy(action => action.Priority)
            .ToArray();
    }

    private static void ValidateRecommendationConsistency(
        DeviceDiagnosticCondition primary,
        bool likelyPhysicalDamage,
        bool recoveryFirst,
        IReadOnlyCollection<RecommendedDiagnosticAction> actions)
    {
        var enabled = actions.Where(action => action.Enabled).ToArray();
        if ((likelyPhysicalDamage
             || primary is DeviceDiagnosticCondition.DeviceIdentityChanged
                 or DeviceDiagnosticCondition.DeviceRemoved
                 or DeviceDiagnosticCondition.NoMedia
                 or DeviceDiagnosticCondition.IntermittentConnection)
            && enabled.Any(action => action.Kind is DiagnosticActionKind.TrySafeRepair
                or DiagnosticActionKind.ConsiderDestructiveRepair
                or DiagnosticActionKind.RunDeepScan))
        {
            throw new InvalidOperationException("Unsafe scan or repair recommendation for unstable physical evidence.");
        }

        if (enabled.Any(action => action.Kind == DiagnosticActionKind.ConsiderDestructiveRepair))
        {
            throw new InvalidOperationException("Analyze must never enable destructive repair as a direct recommendation.");
        }

        if (recoveryFirst)
        {
            var recoveryPriority = enabled
                .Where(action => action.Kind is DiagnosticActionKind.RunDeepScan or DiagnosticActionKind.RecoverFiles)
                .Select(action => action.Priority)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            var repairPriority = enabled
                .Where(action => action.Kind == DiagnosticActionKind.TrySafeRepair)
                .Select(action => action.Priority)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            if (repairPriority < recoveryPriority)
            {
                throw new InvalidOperationException("Repair was recommended before recovery.");
            }
        }
    }

    private static IReadOnlyList<string> BuildEvidenceSummary(DeviceDiagnosticEvidence evidence)
    {
        var items = new List<string>
        {
            StateLine(evidence.IdentityRevalidated, "Initial physical identity was revalidated", "Initial identity did not validate", "Initial identity validation is unknown"),
            StateLine(evidence.FinalIdentityRevalidated, "Physical identity remained stable", "Final identity validation failed", "Final identity validation is unknown")
        };

        if (evidence.PhysicalDiskNumber.HasValue)
        {
            items.Add($"Confirmed: Disk {evidence.PhysicalDiskNumber.Value} detected over {Fallback(evidence.BusType, "an unknown bus")}.");
        }
        else
        {
            items.Add("Unknown: Windows did not provide a physical disk number.");
        }

        items.Add(evidence.PartitionCount.HasValue
            ? $"Confirmed: {evidence.PartitionCount.Value} partition(s) reported."
            : "Unknown: Partition count was unavailable.");

        if (!string.IsNullOrWhiteSpace(evidence.FileSystem))
        {
            items.Add(IsYes(evidence.IsRawFileSystem)
                ? "Warning: Windows reports the filesystem as RAW."
                : $"Confirmed: Windows reports {evidence.FileSystem}.");
        }
        else
        {
            items.Add("Unknown: Filesystem information was unavailable.");
        }

        items.Add(StateLine(
            evidence.RootDirectoryReadable,
            "The mounted root directory is readable",
            $"The mounted root directory is not readable ({evidence.AccessFailureCategory})",
            "Root-directory accessibility was not applicable or unavailable"));

        if (IsYes(evidence.ReadProbeAttempted))
        {
            if (IsYes(evidence.ReadProbeSucceeded))
            {
                items.Add($"Confirmed: First {FormatBytes(evidence.ReadProbeBytesCompleted)} read without I/O errors.");
            }
            else if (IsReadProbeUnavailable(evidence))
            {
                items.Add($"Unknown: Physical read evidence was unavailable ({evidence.ReadProbeFailureCategory}).");
            }
            else
            {
                items.Add($"Warning: Read probe completed {FormatBytes(evidence.ReadProbeBytesCompleted)} of {FormatBytes(evidence.ReadProbeBytesRequested)}.");
            }
        }
        else
        {
            items.Add("Unknown: The bounded physical read probe was not completed.");
        }

        if (IsYes(evidence.SuspiciousAutorunDetected)
            || IsYes(evidence.SuspiciousShortcutPatternDetected)
            || evidence.SuspiciousLauncherCount > 0)
        {
            items.Add("Warning: Malware-like root launchers or shortcut patterns were detected.");
        }
        else if (IsYes(evidence.SecurityEvidenceCollected))
        {
            items.Add("Confirmed: No configured malware-launcher pattern was found in the USB root metadata.");
        }
        else
        {
            items.Add("Unknown: Security metadata could not be inspected.");
        }

        items.Add(IsYes(evidence.DefenderThreatRequiresAction)
            ? "Warning: Defender reports a threat requiring action."
            : "Unknown: Analyze does not run Defender; use an explicit Defender scan for a threat verdict.");
        return items;
    }

    private static IReadOnlyList<string> BuildLimitations(DeviceDiagnosticEvidence evidence)
    {
        var limitations = new List<string>(evidence.CollectionWarnings);
        if (IsUnknown(evidence.PartitionMetadataAvailable))
        {
            limitations.Add("Windows partition metadata was unavailable.");
        }
        if (IsUnknown(evidence.VolumeMetadataAvailable))
        {
            limitations.Add("Windows volume metadata was unavailable.");
        }
        if (!IsYes(evidence.ReadProbeAttempted))
        {
            limitations.Add("The bounded 4 MB read probe was not completed.");
        }
        else if (IsReadProbeUnavailable(evidence))
        {
            limitations.Add($"The bounded 4 MB read probe could not be evaluated ({evidence.ReadProbeFailureCategory}).");
        }
        if (IsUnknown(evidence.SecurityEvidenceCollected))
        {
            limitations.Add("USB root security metadata was not available.");
        }
        if (!IsYes(evidence.DefenderThreatRequiresAction))
        {
            limitations.Add("Analyze does not run Microsoft Defender; a threat verdict requires an explicit Defender scan.");
        }
        limitations.Add("This quick read-only analysis is not a full surface test and cannot guarantee every storage sector is healthy.");
        return limitations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsCoreEvidenceComplete(DeviceDiagnosticEvidence evidence)
    {
        return IsYes(evidence.DevicePresent)
            && IsYes(evidence.IdentityRevalidated)
            && IsYes(evidence.FinalIdentityRevalidated)
            && !IsUnknown(evidence.PartitionMetadataAvailable)
            && !IsUnknown(evidence.VolumeMetadataAvailable)
            && IsYes(evidence.ReadProbeAttempted)
            && !IsReadProbeUnavailable(evidence);
    }

    private static bool CanClassifyReadable(DeviceDiagnosticEvidence evidence)
    {
        return IsYes(evidence.DevicePresent)
            && IsYes(evidence.IdentityRevalidated)
            && IsYes(evidence.FinalIdentityRevalidated)
            && IsYes(evidence.HasAllocatedPartition)
            && IsYes(evidence.HasMountedVolume)
            && IsYes(evidence.FileSystemRecognized)
            && IsYes(evidence.RootDirectoryReadable)
            && IsYes(evidence.ReadProbeSucceeded)
            && evidence.IoErrorCount == 0
            && !IsYes(evidence.DeviceDisconnectedDuringAnalysis)
            && !IsYes(evidence.IsSystemDisk)
            && !IsYes(evidence.IsBootDisk);
    }

    private static bool IsReadProbeUnavailable(DeviceDiagnosticEvidence evidence)
    {
        return evidence.ReadProbeFailureCategory is DiagnosticFailureCategory.AccessDenied
            or DiagnosticFailureCategory.EvidenceUnavailable;
    }

    private static DiagnosticFinding Finding(
        string code,
        DeviceDiagnosticCondition condition,
        DiagnosticConfidence confidence,
        DiagnosticSeverity severity,
        string title,
        string explanation,
        IReadOnlyList<string> evidence,
        IReadOnlyList<string> recommendations,
        IReadOnlyList<string> avoid,
        bool recoveryFirst = false,
        IReadOnlyList<string>? missingEvidence = null)
    {
        return new DiagnosticFinding
        {
            Code = code,
            Condition = condition,
            Confidence = confidence,
            Severity = severity,
            Title = title,
            Explanation = explanation,
            Evidence = evidence.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            MissingEvidence = missingEvidence ?? Array.Empty<string>(),
            RecommendedActions = recommendations,
            ActionsToAvoid = avoid,
            RecoveryRecommendedFirst = recoveryFirst,
            DestructiveRepairAllowedAsNextStep = false
        };
    }

    private static RecommendedDiagnosticAction Action(
        DiagnosticActionKind kind,
        int priority,
        string title,
        string reason,
        bool confirmation = false)
    {
        return new RecommendedDiagnosticAction
        {
            Kind = kind,
            Priority = priority,
            Title = title,
            Reason = reason,
            RequiresUserConfirmation = confirmation,
            Destructive = false
        };
    }

    private static RecommendedDiagnosticAction DisabledAction(
        DiagnosticActionKind kind,
        int priority,
        string title,
        string disabledReason)
    {
        return new RecommendedDiagnosticAction
        {
            Kind = kind,
            Priority = priority,
            Title = title,
            Enabled = false,
            Destructive = kind == DiagnosticActionKind.ConsiderDestructiveRepair,
            RequiresUserConfirmation = true,
            DisabledReason = disabledReason
        };
    }

    private static void AddDisabledRiskyActions(
        ICollection<RecommendedDiagnosticAction> actions,
        string reason,
        bool disableDeepScan)
    {
        if (disableDeepScan)
        {
            actions.Add(DisabledAction(DiagnosticActionKind.RunDeepScan, 80, "Deep Scan", reason));
        }
        actions.Add(DisabledAction(DiagnosticActionKind.TrySafeRepair, 90, "Safe Repair", reason));
        actions.Add(DisabledAction(DiagnosticActionKind.ConsiderDestructiveRepair, 91, "Erase and Repair", reason));
    }

    private static IReadOnlyList<string> MalwareEvidence(DeviceDiagnosticEvidence evidence)
    {
        var values = new List<string>();
        if (IsYes(evidence.SuspiciousAutorunDetected))
        {
            values.Add("A root autorun.inf file was detected.");
        }
        if (IsYes(evidence.SuspiciousShortcutPatternDetected))
        {
            values.Add("A shortcut-replacement pattern was detected in the USB root.");
        }
        if (evidence.SuspiciousLauncherCount > 0)
        {
            values.Add($"Detected {evidence.SuspiciousLauncherCount.Value} suspicious root launcher(s).");
        }
        return values;
    }

    private static string ProbeEvidence(DeviceDiagnosticEvidence evidence)
    {
        return IsYes(evidence.ReadProbeSucceeded)
            ? $"The bounded read probe completed {FormatBytes(evidence.ReadProbeBytesCompleted)} without an I/O error."
            : "The bounded physical read probe did not complete successfully.";
    }

    private static string BuildSummary(DiagnosticFinding primary, IReadOnlyCollection<DiagnosticFinding> findings)
    {
        var additionalCount = findings.Count(finding =>
            finding.Code != primary.Code && finding.Condition != DeviceDiagnosticCondition.AnalysisIncomplete);
        return additionalCount == 0
            ? primary.Explanation
            : $"{primary.Explanation} {additionalCount} additional finding(s) are listed below.";
    }

    private static int GetPrecedence(DeviceDiagnosticCondition condition)
    {
        return Precedence.TryGetValue(condition, out var value) ? value : int.MaxValue;
    }

    private static string StateLine(EvidenceState state, string yes, string no, string unknown)
    {
        return state switch
        {
            EvidenceState.Yes => $"Confirmed: {yes}.",
            EvidenceState.No => $"Warning: {no}.",
            _ => $"Unknown: {unknown}."
        };
    }

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static bool IsYes(EvidenceState state) => state == EvidenceState.Yes;
    private static bool IsNo(EvidenceState state) => state == EvidenceState.No;
    private static bool IsUnknown(EvidenceState state) => state == EvidenceState.Unknown;
}
