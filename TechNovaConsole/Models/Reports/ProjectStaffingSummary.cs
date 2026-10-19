using System;
using System.Collections.Generic;

namespace TechNovaConsole.Models.Reports;

public sealed class ProjectStaffingSummary
{
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public TechNovaConsole.Models.ProjectStatus Status { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int ActiveAllocationCount { get; init; }
    public IReadOnlyList<AllocatedEmployeeInfo> AllocatedEmployees { get; init; } = Array.Empty<AllocatedEmployeeInfo>();
    public decimal AllocatedPercent { get; init; }
    public decimal? StaffingTargetPercent { get; init; }
    public bool IsUnderstaffed { get; init; }
}

public sealed class AllocatedEmployeeInfo
{
    public Guid EmployeeUniqueId { get; init; }
    public decimal AllocationPercent { get; init; }
}
