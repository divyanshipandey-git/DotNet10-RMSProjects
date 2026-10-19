using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;
using TechNovaConsole.Utilities;

namespace TechNovaConsole.Services;

/// <summary>
/// Service that manages allocations of employees to projects.
/// Ensures business rules are applied and coordinates persistence via repositories.
/// This service serializes allocation mutations to avoid race conditions that could violate capacity checks.
/// </summary>
public sealed class AllocationService
{
    private readonly IAllocationRepository _allocationRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly INotificationService _notification;

    // Serializes operations to avoid concurrent allocations that could bypass capacity checks.
    private readonly SemaphoreSlim _operationLock = new(1,1);

    public AllocationService(
        IAllocationRepository allocationRepo,
        IEmployeeRepository employeeRepo,
        IProjectRepository projectRepo,
        INotificationService notification)
    {
        _allocationRepo = allocationRepo ?? throw new ArgumentNullException(nameof(allocationRepo));
        _employeeRepo = employeeRepo ?? throw new ArgumentNullException(nameof(employeeRepo));
        _projectRepo = projectRepo ?? throw new ArgumentNullException(nameof(projectRepo));
        _notification = notification ?? throw new ArgumentNullException(nameof(notification));
    }

    /// <summary>
    /// Create a new allocation after validation. Emits a notification after successful allocation.
    /// </summary>
    public async Task<Result<Allocation>> CreateAllocationAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        if (allocation is null) return Result<Allocation>.Failure("Allocation must be provided.");

        // Basic validation
        if (allocation.EmployeeId == Guid.Empty) return Result<Allocation>.Failure("EmployeeId is required.");
        if (allocation.ProjectId == Guid.Empty) return Result<Allocation>.Failure("ProjectId is required.");
        if (allocation.EndDate.HasValue && allocation.EndDate.Value < allocation.StartDate) return Result<Allocation>.Failure("EndDate must be same or after StartDate.");
        if (allocation.AllocationPercentage <= 0m || allocation.AllocationPercentage > 100m) return Result<Allocation>.Failure("AllocationPercentage must be between 1 and 100.");

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Verify employee exists and is active
            var employee = await _employeeRepo.GetByIdAsync(allocation.EmployeeId, cancellationToken).ConfigureAwait(false);
            if (employee is null) return Result<Allocation>.Failure("Employee not found.");
            if (!employee.IsActive) return Result<Allocation>.Failure("Cannot allocate an inactive employee.");

            // Verify project exists and is in a status that permits allocations (Planned or Active)
            var project = await _projectRepo.GetByIdAsync(allocation.ProjectId, cancellationToken).ConfigureAwait(false);
            if (project is null) return Result<Allocation>.Failure("Project not found.");
            if (!(project.Status == ProjectStatus.Planned || project.Status == ProjectStatus.Active))
                return Result<Allocation>.Failure($"Project status '{project.Status}' does not permit allocations.");

            // Allocation must fall within project's date range
            if (allocation.StartDate < project.StartDate) return Result<Allocation>.Failure("Allocation cannot start before project start date.");
            if (project.EndDate.HasValue && allocation.EndDate.HasValue && allocation.EndDate.Value > project.EndDate.Value)
                return Result<Allocation>.Failure("Allocation cannot end after project end date.");

            // For capacity checks, require EndDate to be provided
            if (!allocation.EndDate.HasValue) return Result<Allocation>.Failure("Allocation EndDate must be provided.");

            // Check capacity day-by-day
            var existing = (await _allocationRepo.GetByEmployeeAsync(allocation.EmployeeId, cancellationToken).ConfigureAwait(false))
                .Where(a => a.IsActive).ToList();

            if (!AllocationValidator.IsCapacityAvailable(allocation.StartDate, allocation.EndDate!.Value, allocation.AllocationPercentage, existing, excludeAllocationId: null))
                return Result<Allocation>.Failure("Allocation would exceed 100% capacity for the employee during the requested period.");

            await _allocationRepo.AddOrUpdateAsync(allocation, cancellationToken).ConfigureAwait(false);

            // Emit notification after successful allocation. Notification failures are non-fatal with respect
            // to allocation persistence: if NotifyAsync throws, we log the failure but still return success because
            // the allocation has already been persisted.
            try
            {
                await _notification.NotifyAsync($"Allocated employee {employee.FullName} to project {project.Name} ({allocation.AllocationPercentage}% from {allocation.StartDate} to {allocation.EndDate}).");
            }
            catch (Exception ex)
            {
                // Do not treat notification failure as rollback. Log to stderr for operators.
                try { Console.Error.WriteLine($"Notification failed after allocation persisted: {ex.Message}"); } catch { }
            }

            return Result<Allocation>.Success(allocation);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Release an allocation (soft): marks IsActive = false but preserves record.
    /// Emits a notification after successful release.
    /// </summary>
    public async Task<Result> ReleaseAllocationAsync(Guid allocationId, CancellationToken cancellationToken = default)
    {
        if (allocationId == Guid.Empty) return Result.Failure("AllocationId is required.");

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _allocationRepo.GetByIdAsync(allocationId, cancellationToken).ConfigureAwait(false);
            if (existing is null) return Result.Failure("Allocation not found.");
            if (!existing.IsActive) return Result.Failure("Allocation is already released.");

            var released = new Allocation(existing.EmployeeId, existing.ProjectId, existing.StartDate, existing.EndDate, existing.AllocationPercentage)
            {
                // preserve identity and mark inactive
                Id = existing.Id,
                IsActive = false
            };

            await _allocationRepo.AddOrUpdateAsync(released, cancellationToken).ConfigureAwait(false);

            // Notify (non-fatal on failure)
            var employee = await _employeeRepo.GetByIdAsync(existing.EmployeeId, cancellationToken).ConfigureAwait(false);
            var project = await _projectRepo.GetByIdAsync(existing.ProjectId, cancellationToken).ConfigureAwait(false);
            try
            {
                await _notification.NotifyAsync($"Released allocation {existing.Id} for employee {employee?.FullName ?? existing.EmployeeId.ToString()} on project {project?.Name ?? existing.ProjectId.ToString()}.");
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine($"Notification failed after release persisted: {ex.Message}"); } catch { }
            }

            return Result.Success();
        }
        finally { _operationLock.Release(); }
    }

    public Task<IReadOnlyList<Allocation>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => _allocationRepo.GetByEmployeeAsync(employeeId, cancellationToken);

    public Task<IReadOnlyList<Allocation>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _allocationRepo.GetByProjectAsync(projectId, cancellationToken);

    public async Task<Result<Allocation>> UpdateAllocationAsync(Guid allocationId, AllocationUpdate update, CancellationToken cancellationToken = default)
    {
        if (allocationId == Guid.Empty) return Result<Allocation>.Failure("AllocationId is required.");
        if (update is null) return Result<Allocation>.Failure("Update must be provided.");

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _allocationRepo.GetByIdAsync(allocationId, cancellationToken).ConfigureAwait(false);
            if (existing is null) return Result<Allocation>.Failure("Allocation not found.");

            var candidate = new Allocation(
                employeeId: existing.EmployeeId,
                projectId: existing.ProjectId,
                startDate: update.StartDate ?? existing.StartDate,
                endDate: update.EndDate ?? existing.EndDate,
                allocationPercentage: update.AllocationPercentage ?? existing.AllocationPercentage)
            {
                Id = existing.Id,
                IsActive = existing.IsActive
            };

            // Basic validation same as create
            if (candidate.EndDate.HasValue && candidate.EndDate.Value < candidate.StartDate) return Result<Allocation>.Failure("EndDate must be same or after StartDate.");
            if (candidate.AllocationPercentage <= 0m || candidate.AllocationPercentage > 100m) return Result<Allocation>.Failure("AllocationPercentage must be between 1 and 100.");

            // Ensure project exists and allows allocation
            var project = await _projectRepo.GetByIdAsync(candidate.ProjectId, cancellationToken).ConfigureAwait(false);
            if (project is null) return Result<Allocation>.Failure("Project not found.");
            if (!(project.Status == ProjectStatus.Planned || project.Status == ProjectStatus.Active))
                return Result<Allocation>.Failure($"Project status '{project.Status}' does not permit allocations.");

            if (candidate.StartDate < project.StartDate) return Result<Allocation>.Failure("Allocation cannot start before project start date.");
            if (project.EndDate.HasValue && candidate.EndDate.HasValue && candidate.EndDate.Value > project.EndDate.Value)
                return Result<Allocation>.Failure("Allocation cannot end after project end date.");

            if (!candidate.EndDate.HasValue) return Result<Allocation>.Failure("Allocation EndDate must be provided.");

            // Capacity check excluding the existing allocation itself
            var existingAllocations = (await _allocationRepo.GetByEmployeeAsync(candidate.EmployeeId, cancellationToken).ConfigureAwait(false)).Where(a => a.IsActive && a.Id != existing.Id).ToList();

            if (!AllocationValidator.IsCapacityAvailable(candidate.StartDate, candidate.EndDate!.Value, candidate.AllocationPercentage, existingAllocations, excludeAllocationId: null))
                return Result<Allocation>.Failure("Updated allocation would exceed 100% capacity for the employee during the requested period.");

            await _allocationRepo.AddOrUpdateAsync(candidate, cancellationToken).ConfigureAwait(false);

            try
            {
                await _notification.NotifyAsync($"Updated allocation {candidate.Id} for employee {candidate.EmployeeId} on project {candidate.ProjectId}.");
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine($"Notification failed after allocation update persisted: {ex.Message}"); } catch { }
            }

            return Result<Allocation>.Success(candidate);
        }
        finally { _operationLock.Release(); }
    }

    // Capacity checks are implemented in AllocationValidator to keep availability logic consistent
    // across AllocationService and MatchingService.
}

public sealed class AllocationUpdate
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public decimal? AllocationPercentage { get; init; }
}
