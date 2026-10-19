using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Models;

namespace TechNovaConsole.Interfaces;

/// <summary>
/// Repository contract for allocation persistence.
/// </summary>
public interface IAllocationRepository
{
    Task<IReadOnlyList<Allocation>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Allocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Allocation>> GetByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Allocation>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(Allocation allocation, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
