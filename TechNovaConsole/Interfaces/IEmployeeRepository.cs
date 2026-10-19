using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Models;

namespace TechNovaConsole.Interfaces;

/// <summary>
/// Repository contract for reading and persisting employee data.
/// Methods are asynchronous to avoid blocking calling threads during I/O.
/// </summary>
public interface IEmployeeRepository
{
    /// <summary>
    /// Returns all employees available from the repository.
    /// </summary>
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the employee with the specified unique identifier, or null if not found.
    /// </summary>
    Task<Employee?> GetByIdAsync(Guid uniqueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new employee to the repository. The repository will assign a UniqueId if one
    /// is not provided. This method does not update existing records.
    /// </summary>
    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing employee identified by employee.UniqueId. Throws if the employee
    /// does not exist.
    /// </summary>
    Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the employee with the specified unique identifier if it exists.
    /// </summary>
    Task DeleteAsync(Guid uniqueId, CancellationToken cancellationToken = default);
}
