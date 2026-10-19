using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;
using TechNovaConsole.Models.Reports;
using TechNovaConsole.Utilities;

namespace TechNovaConsole.Services;

/// <summary>
/// Reporting service that produces reusable DTOs for bench, project staffing and department utilisation reports.
/// Calculations are independent of presentation and rely on repository data. All percentage values are 0..100 scale.
/// </summary>
public sealed class ReportService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAllocationRepository _allocationRepo;

    public ReportService(IEmployeeRepository employeeRepo, IProjectRepository projectRepo, IAllocationRepository allocationRepo)
    {
        _employeeRepo = employeeRepo ?? throw new ArgumentNullException(nameof(employeeRepo));
        _projectRepo = projectRepo ?? throw new ArgumentNullException(nameof(projectRepo));
        _allocationRepo = allocationRepo ?? throw new ArgumentNullException(nameof(allocationRepo));
    }

    #region Bench report

    /// <summary>
    /// Bench report: list active employees whose allocation on the given date is below thresholdPercent.
    /// - thresholdPercent is on 0..100 scale.
    /// - Released or historical allocations (IsActive = false) are excluded from the calculation; only active allocations
    ///   are counted toward an employee's occupied capacity on the report date.
    /// </summary>
    public async Task<Result<IReadOnlyList<BenchReportItem>>> GetBenchReportAsync(DateOnly date, decimal thresholdPercent = 20m, CancellationToken cancellationToken = default)
    {
        if (thresholdPercent < 0m || thresholdPercent > 100m) return Result<IReadOnlyList<BenchReportItem>>.Failure("thresholdPercent must be between 0 and 100.");

        var employees = await _employeeRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var allocations = await _allocationRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<BenchReportItem>();

        foreach (var e in employees.Where(x => x.IsActive))
        {
            // sum of active allocations overlapping date
            var activeAllocations = allocations.Where(a => a.IsActive && a.EmployeeId == e.UniqueId).ToList();
            decimal allocated = 0m;
            foreach (var a in activeAllocations)
            {
                DateOnly aEnd = a.EndDate ?? a.StartDate;
                if (a.StartDate <= date && date <= aEnd) allocated += a.AllocationPercentage;
            }

            // cap at employee capacity (100 * employment factor)
            decimal capacityFactor = EmploymentTypeCapacityFactor(e.EmploymentType);
            decimal employeeCapacity = 100m * capacityFactor;
            if (allocated > employeeCapacity) allocated = employeeCapacity;

            decimal available = Math.Max(0m, employeeCapacity - allocated);

            if (allocated < thresholdPercent)
            {
                items.Add(new BenchReportItem
                {
                    EmployeeUniqueId = e.UniqueId,
                    FullName = e.FullName,
                    Department = e.Department,
                    AllocatedPercent = Math.Round(allocated, 2),
                    AvailablePercent = Math.Round(available, 2),
                    Note = "Active allocations only (released allocations excluded)."
                });
            }
        }

        return Result<IReadOnlyList<BenchReportItem>>.Success(items.AsReadOnly());
    }

    private static decimal EmploymentTypeCapacityFactor(EmploymentType t)
    {
        // Assumption: FullTime and Contract count as 1.0 FTE, Intern and Consultant are weighted lower (0.5).
        // This affects denominators in utilisation calculations. Adjust as organizational policy.
        return t switch
        {
            EmploymentType.FullTime => 1.0m,
            EmploymentType.Contract => 1.0m,
            EmploymentType.Intern => 0.5m,
            EmploymentType.Consultant => 0.5m,
            _ => 1.0m
        };
    }

    #endregion

    #region Project staffing summary

    /// <summary>
    /// Returns a project staffing summary for the specified date (point-in-time). If staffingTargetPercent is provided
    /// it is used to classify projects as understaffed when totalAllocatedPercent &lt; staffingTargetPercent.
    /// Note: staffingTargetPercent is an explicit input and not inferred from project data.
    /// The returned AllocatedPercent is the sum of active allocation percentages for the project on the date (not capped).
    /// </summary>
    public async Task<Result<IReadOnlyList<ProjectStaffingSummary>>> GetProjectStaffingSummaryAsync(DateOnly date, decimal? staffingTargetPercent = null, CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var allocations = await _allocationRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var employees = await _employeeRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var summaries = new List<ProjectStaffingSummary>();

        foreach (var p in projects)
        {
            // active allocations overlapping date
            var active = allocations.Where(a => a.IsActive && a.ProjectId == p.Id && a.StartDate <= date && (a.EndDate ?? a.StartDate) >= date).ToList();
            decimal totalAllocatedPercent = active.Sum(a => a.AllocationPercentage);
            var allocatedEmployees = active.Select(a => new AllocatedEmployeeInfo { EmployeeUniqueId = a.EmployeeId, AllocationPercent = a.AllocationPercentage }).ToList();

            bool understaffed = false;
            if (staffingTargetPercent.HasValue) understaffed = totalAllocatedPercent < staffingTargetPercent.Value;

            summaries.Add(new ProjectStaffingSummary
            {
                ProjectId = p.Id,
                ProjectName = p.Name,
                Status = p.Status,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                ActiveAllocationCount = active.Count,
                AllocatedEmployees = allocatedEmployees,
                AllocatedPercent = Math.Round(totalAllocatedPercent, 2),
                StaffingTargetPercent = staffingTargetPercent,
                IsUnderstaffed = understaffed
            });
        }

        return Result<IReadOnlyList<ProjectStaffingSummary>>.Success(summaries.AsReadOnly());
    }

    #endregion

    #region Department utilisation

    /// <summary>
    /// Point-in-time department utilisation on the specified date.
    /// Formula: allocated_capacity / active_employee_capacity * 100
    /// - allocated_capacity: sum over employees in the department of min(sum of active allocations on date, employeeCapacity)
    /// - active_employee_capacity: sum over active employees in department of employeeCapacity (100 * capacityFactor)
    /// Released/inactive allocations are excluded; inactive employees are excluded from the denominator.
    /// </summary>
    public async Task<Result<IReadOnlyList<DepartmentUtilisationPoint>>> GetDepartmentPointUtilisationAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var employees = await _employeeRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var allocations = await _allocationRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var byDept = employees.Where(e => e.IsActive).GroupBy(e => e.Department ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var result = new List<DepartmentUtilisationPoint>();

        foreach (var g in byDept)
        {
            decimal numerator = 0m;
            decimal denominator = 0m;

            foreach (var e in g)
            {
                decimal capacityFactor = EmploymentTypeCapacityFactor(e.EmploymentType);
                decimal empCapacity = 100m * capacityFactor;
                denominator += empCapacity;

                var empAlloc = allocations.Where(a => a.IsActive && a.EmployeeId == e.UniqueId).ToList();
                decimal sumAlloc = 0m;
                foreach (var a in empAlloc)
                {
                    DateOnly aEnd = a.EndDate ?? a.StartDate;
                    if (a.StartDate <= date && date <= aEnd) sumAlloc += a.AllocationPercentage;
                }

                numerator += Math.Min(sumAlloc, empCapacity);
            }

            decimal utilisation = denominator == 0m ? 0m : (numerator / denominator) * 100m;
            result.Add(new DepartmentUtilisationPoint
            {
                Department = g.Key,
                Allocated = Math.Round(numerator, 2),
                Capacity = Math.Round(denominator, 2),
                UtilisationPercent = Math.Round(utilisation, 2)
            });
        }

        return Result<IReadOnlyList<DepartmentUtilisationPoint>>.Success(result.AsReadOnly());
    }

    /// <summary>
    /// Period-based department utilisation measured in employee-days over the inclusive date range.
    /// - numerator: sum over days and employees of min(allocatedPercentOnDay, employeeCapacity)
    /// - denominator: sum over days and active employees of employeeCapacity
    /// Returns percentage = numerator / denominator * 100
    /// </summary>
    public async Task<Result<IReadOnlyList<DepartmentUtilisationPeriod>>> GetDepartmentPeriodUtilisationAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate) return Result<IReadOnlyList<DepartmentUtilisationPeriod>>.Failure("EndDate must be same or after StartDate.");

        var employees = await _employeeRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var allocations = await _allocationRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var byDept = employees.Where(e => e.IsActive).GroupBy(e => e.Department ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var result = new List<DepartmentUtilisationPeriod>();

        foreach (var g in byDept)
        {
            decimal numerator = 0m;
            decimal denominator = 0m;

            foreach (var e in g)
            {
                decimal capacityFactor = EmploymentTypeCapacityFactor(e.EmploymentType);
                decimal empCapacity = 100m * capacityFactor;

                // For each day, compute allocated percent capped at empCapacity
                for (DateOnly day = startDate; day <= endDate; day = day.AddDays(1))
                {
                    decimal sumAlloc = 0m;
                    var empAlloc = allocations.Where(a => a.IsActive && a.EmployeeId == e.UniqueId);
                    foreach (var a in empAlloc)
                    {
                        DateOnly aEnd = a.EndDate ?? a.StartDate;
                        if (a.StartDate <= day && day <= aEnd) sumAlloc += a.AllocationPercentage;
                    }

                    numerator += Math.Min(sumAlloc, empCapacity);
                    denominator += empCapacity;
                }
            }

            decimal utilisation = denominator == 0m ? 0m : (numerator / denominator) * 100m;
            result.Add(new DepartmentUtilisationPeriod
            {
                Department = g.Key,
                AllocatedEmployeeDays = Math.Round(numerator, 2),
                CapacityEmployeeDays = Math.Round(denominator, 2),
                UtilisationPercent = Math.Round(utilisation, 2),
                StartDate = startDate,
                EndDate = endDate
            });
        }

        return Result<IReadOnlyList<DepartmentUtilisationPeriod>>.Success(result.AsReadOnly());
    }

    #endregion
}

/// <summary>
// DTOs moved to TechNovaConsole.Models.Reports
