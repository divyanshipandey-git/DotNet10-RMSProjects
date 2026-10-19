using System;
using System.Collections.Generic;
using System.Linq;
using TechNovaConsole.Models;

namespace TechNovaConsole.Utilities;

/// <summary>
/// Helper methods for allocation capacity checks.
/// Provides a shared, well-tested algorithm for checking per-day capacity using inclusive ranges.
/// </summary>
public static class AllocationValidator
{
    /// <summary>
    /// Determines whether the requested allocation percentage can be accommodated for every day in the inclusive range.
    /// existingActive contains allocations that should be considered (typically only active allocations).
    /// excludeAllocationId can be provided to exclude an existing allocation (useful when updating that allocation).
    /// </summary>
    public static bool IsCapacityAvailable(DateOnly start, DateOnly end, decimal requestedPercentage, IEnumerable<Allocation> existingActive, Guid? excludeAllocationId = null)
    {
        if (requestedPercentage <= 0m) return false;

        // Day-by-day check. Simple and correct for expected workloads. For large ranges, replace with sweep-line.
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            decimal sum = 0m;
            foreach (var a in existingActive)
            {
                if (excludeAllocationId.HasValue && a.Id == excludeAllocationId.Value) continue;
                DateOnly aEnd = a.EndDate ?? a.StartDate;
                if (a.StartDate <= day && day <= aEnd)
                    sum += a.AllocationPercentage;
            }

            if (sum + requestedPercentage > 100m) return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the maximum allocated percentage for the given employee across the inclusive date range.
    /// Returns the maximum sum of active allocations on any day in the range.
    /// </summary>
    public static decimal MaxAllocatedPercentage(DateOnly start, DateOnly end, IEnumerable<Allocation> existingActive, Guid? excludeAllocationId = null)
    {
        decimal max = 0m;
        for (DateOnly day = start; day <= end; day = day.AddDays(1))
        {
            decimal sum = 0m;
            foreach (var a in existingActive)
            {
                if (excludeAllocationId.HasValue && a.Id == excludeAllocationId.Value) continue;
                DateOnly aEnd = a.EndDate ?? a.StartDate;
                if (a.StartDate <= day && day <= aEnd)
                    sum += a.AllocationPercentage;
            }

            if (sum > max) max = sum;
        }

        return max;
    }
}
