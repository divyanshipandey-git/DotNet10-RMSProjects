using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Models;

namespace TechNovaConsole.Interfaces;

/// <summary>
/// Repository contract for persisting and retrieving Project entities.
/// </summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
