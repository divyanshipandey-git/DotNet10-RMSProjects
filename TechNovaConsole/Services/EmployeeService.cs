using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;
using TechNovaConsole.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechNovaConsole.Services;

/// <summary>
/// Application service responsible for employee-related business logic.
/// This service coordinates validation rules and persistence via IEmployeeRepository.
/// It does not perform I/O directly and relies on the repository abstraction.
/// </summary>
public sealed class EmployeeService
{
    private readonly IEmployeeRepository _repository;
    private readonly ILogger<EmployeeService> _logger;

    // For simple file-based diagnostics we may write small logs; System.IO is used below.

    // Simple email normalization: trim and lower-case. Kept consistent across the app.
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public EmployeeService(IEmployeeRepository repository, ILogger<EmployeeService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullLogger<EmployeeService>.Instance;
    }

    /// <summary>
    /// Deactivates an employee while preserving the employee identity and allocation history.
    /// This method remains named DeleteAsync for compatibility with existing callers.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        return await DeactivateAsync(uniqueId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a new employee after validating business rules. Returns the created employee on success.
    /// Duplicate UniqueId or legacy Id or email are rejected.
    /// </summary>
    public async Task<Result<Employee>> AddEmployeeAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (employee is null) return Result<Employee>.Failure("Employee must be provided.");

        // Validate core fields
        var validation = ValidateEmployeeFields(employee);
        if (!validation.IsSuccess) return validation;

        // Normalize email for comparison
        string normalizedEmail = NormalizeEmail(employee.Email);

        // Check duplicates
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (employee.UniqueId != Guid.Empty && all.Any(e => e.UniqueId == employee.UniqueId))
            return Result<Employee>.Failure("An employee with the same UniqueId already exists.");

        if (employee.Id != 0 && all.Any(e => e.Id == employee.Id))
            return Result<Employee>.Failure("An employee with the same legacy Id already exists.");

        if (all.Any(e => string.Equals(NormalizeEmail(e.Email), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            return Result<Employee>.Failure("An employee with the same email address already exists.");

        // All good - persist
        try
        {
            await _repository.AddAsync(employee, cancellationToken).ConfigureAwait(false);
            return Result<Employee>.Success(employee);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add employee {EmployeeName} ({Email})", employee.FullName, employee.Email);
            return Result<Employee>.Failure($"Failed to add employee: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves an employee by UniqueId.
    /// </summary>
    public async Task<Result<Employee>> GetByIdAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        if (uniqueId == Guid.Empty) return Result<Employee>.Failure("UniqueId must be provided.");

        var emp = await _repository.GetByIdAsync(uniqueId, cancellationToken).ConfigureAwait(false);
        if (emp is null) return Result<Employee>.Failure("Employee not found.");
        return Result<Employee>.Success(emp);
    }

    /// <summary>
    /// Returns all employees.
    /// </summary>
    public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(cancellationToken);

    /// <summary>
    /// Updates editable details of an employee. Returns the updated employee on success.
    /// </summary>
    public async Task<Result<Employee>> UpdateEmployeeAsync(Guid uniqueId, EmployeeUpdate update, CancellationToken cancellationToken = default)
    {
        if (uniqueId == Guid.Empty) return Result<Employee>.Failure("UniqueId must be provided.");

        if (update is null) return Result<Employee>.Failure("Update data must be provided.");

        var existing = await _repository.GetByIdAsync(uniqueId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return Result<Employee>.Failure("Employee not found.");

        // Build candidate updated employee preserving identity
        var candidate = new Employee(
            fullName: string.IsNullOrWhiteSpace(update.FullName) ? existing.FullName : update.FullName,
            email: string.IsNullOrWhiteSpace(update.Email) ? existing.Email : update.Email,
            designation: string.IsNullOrWhiteSpace(update.Designation) ? existing.Designation : update.Designation,
            department: string.IsNullOrWhiteSpace(update.Department) ? existing.Department : update.Department,
            employmentType: update.EmploymentType ?? existing.EmploymentType,
            costRate: update.CostRate ?? existing.CostRate,
            isActive: existing.IsActive,
            skills: update.Skills ?? existing.Skills)
        {
            UniqueId = existing.UniqueId,
            Id = existing.Id
        };

        // Validate fields
        var validation = ValidateEmployeeFields(candidate);
        if (!validation.IsSuccess) return validation;

        // Check email uniqueness (excluding this employee)
        string normalizedEmail = NormalizeEmail(candidate.Email);
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (all.Any(e => e.UniqueId != existing.UniqueId && string.Equals(NormalizeEmail(e.Email), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            return Result<Employee>.Failure("Another employee with the same email already exists.");

        try
        {
            await _repository.UpdateAsync(candidate, cancellationToken).ConfigureAwait(false);
            return Result<Employee>.Success(candidate);
        }
        catch (Exception ex)
        {
            return Result<Employee>.Failure($"Failed to update employee: {ex.Message}");
        }
    }

    /// <summary>
    /// Deactivates an employee (soft delete). This marks the employee inactive but preserves historical records,
    /// allocations and identity. Deactivation does not remove data from the repository.
    /// </summary>
    public async Task<Result> DeactivateAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        if (uniqueId == Guid.Empty) return Result.Failure("UniqueId must be provided.");

        var existing = await _repository.GetByIdAsync(uniqueId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return Result.Failure("Employee not found.");

        if (!existing.IsActive) return Result.Failure("Employee is already inactive.");

        var updated = new Employee(
            fullName: existing.FullName,
            email: existing.Email,
            designation: existing.Designation,
            department: existing.Department,
            employmentType: existing.EmploymentType,
            costRate: existing.CostRate,
            isActive: false,
            skills: existing.Skills)
        {
            UniqueId = existing.UniqueId,
            Id = existing.Id
        };

        try
        {
            await _repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to deactivate employee: {ex.Message}");
        }
    }

    // Validation shared by add/update
    private Result<Employee> ValidateEmployeeFields(Employee employee)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(employee.FullName)) errors.Add("FullName is required.");
        if (string.IsNullOrWhiteSpace(employee.Email)) errors.Add("Email is required.");
        else if (!EmailRegex.IsMatch(employee.Email)) errors.Add("Email is not a valid email address.");
        if (string.IsNullOrWhiteSpace(employee.Designation)) errors.Add("Designation is required.");
        if (string.IsNullOrWhiteSpace(employee.Department)) errors.Add("Department is required.");
        if (employee.CostRate < 0m) errors.Add("CostRate must be non-negative.");

        // Validate skills' proficiency
        foreach (var s in employee.Skills ?? Enumerable.Empty<EmployeeSkill>())
        {
            if (!Enum.IsDefined(typeof(ProficiencyLevel), s.Proficiency))
                errors.Add($"Skill '{s.SkillId}' has invalid proficiency value.");
        }

        if (errors.Count > 0) return Result<Employee>.Failure(errors);
        return Result<Employee>.Success(employee);
    }
}

/// <summary>
/// DTO used to carry editable fields for employee updates.
/// Null values mean "leave unchanged".
/// </summary>
public sealed class EmployeeUpdate
{
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public EmploymentType? EmploymentType { get; init; }
    public decimal? CostRate { get; init; }
    public IEnumerable<EmployeeSkill>? Skills { get; init; }
}
