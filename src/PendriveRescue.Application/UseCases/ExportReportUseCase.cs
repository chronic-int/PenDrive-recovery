using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class ExportReportUseCase
{
    private readonly IReportService _reportService;

    public ExportReportUseCase(IReportService reportService)
    {
        _reportService = reportService;
    }

    public Task<bool> ExecuteAsync(ScanResult result, string filePath)
    {
        return _reportService.ExportReportAsync(result, filePath);
    }

    public Task<bool> ExecuteAsync(RecoveryJob job, string filePath)
    {
        return _reportService.ExportReportAsync(job, filePath);
    }
}
