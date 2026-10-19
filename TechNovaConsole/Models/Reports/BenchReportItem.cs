using System;

namespace TechNovaConsole.Models.Reports;

public sealed class BenchReportItem
{
    public Guid EmployeeUniqueId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public decimal AllocatedPercent { get; init; }
    public decimal AvailablePercent { get; init; }
    public string Note { get; init; } = string.Empty;
}
