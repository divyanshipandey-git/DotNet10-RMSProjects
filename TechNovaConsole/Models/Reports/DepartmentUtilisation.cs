using System;

namespace TechNovaConsole.Models.Reports;

public sealed class DepartmentUtilisationPoint
{
    public string Department { get; init; } = string.Empty;
    public decimal Allocated { get; init; }
    public decimal Capacity { get; init; }
    public decimal UtilisationPercent { get; init; }
}

public sealed class DepartmentUtilisationPeriod
{
    public string Department { get; init; } = string.Empty;
    public decimal AllocatedEmployeeDays { get; init; }
    public decimal CapacityEmployeeDays { get; init; }
    public decimal UtilisationPercent { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}
