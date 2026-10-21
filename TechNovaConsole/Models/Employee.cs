using System;
using System.Collections.Generic;
using System.Linq;

namespace TechNovaConsole.Models;

/// <summary>
/// Represents an employee in the RMS domain.
/// Entity: has a stable unique identifier and lifecycle.
/// </summary>
public sealed class Employee
{
    /// <summary>
    /// Legacy integer identifier used by the sample JSON data.
    /// This property is preserved for compatibility with existing sample files.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Primary unique identifier for the employee within the RMS domain.
    /// Generated when an employee is created if not supplied.
    /// </summary>
    public Guid UniqueId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Employee's full name. Must be non-empty.
    /// </summary>
    public string FullName { get; init; }

    /// <summary>
    /// Employee email address. Must be a non-empty value containing an '@' character.
    /// </summary>
    public string Email { get; init; }

    /// <summary>
    /// Employee designation or job title (for example, "Senior Developer").
    /// </summary>
    public string Designation { get; init; }

    /// <summary>
    /// Department the employee belongs to (for example, IT, HR, Finance).
    /// </summary>
    public string Department { get; init; }

    /// <summary>
    /// Employment type (FullTime, Contract, Intern, etc.).
    /// </summary>
    public EmploymentType EmploymentType { get; init; }

    /// <summary>
    /// Cost rate for the employee expressed as a decimal (e.g., hourly or monthly cost).
    /// Must be non-negative.
    /// </summary>
    public decimal CostRate { get; init; }

    /// <summary>
    /// Indicates whether the employee is currently active and available for allocation.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Skills possessed by the employee with proven proficiency levels.
    /// </summary>
    public IReadOnlyList<EmployeeSkill> Skills { get; init; }

    /// <summary>
    /// Creates a new instance of <see cref="Employee"/> with validation.
    /// </summary>
    /// <param name="fullName">Full name of the employee.</param>
    /// <param name="email">Email address.</param>
    /// <param name="designation">Designation or title.</param>
    /// <param name="department">Department name.</param>
    /// <param name="employmentType">Employment type.</param>
    /// <param name="costRate">Cost rate (non-negative).</param>
    /// <param name="isActive">Active status.</param>
    /// <param name="skills">Employee skills.</param>
    public Employee(
        string fullName,
        string email,
        string designation,
        string department,
        EmploymentType employmentType,
        decimal costRate,
        bool isActive,
        IEnumerable<EmployeeSkill>? skills = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("FullName must be provided", nameof(fullName));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("A valid email must be provided", nameof(email));

        if (string.IsNullOrWhiteSpace(designation))
            throw new ArgumentException("Designation must be provided", nameof(designation));

        if (string.IsNullOrWhiteSpace(department))
            throw new ArgumentException("Department must be provided", nameof(department));

        if (costRate < 0m)
            throw new ArgumentOutOfRangeException(nameof(costRate), "CostRate must be non-negative.");

        FullName = fullName;
        Email = email;
        Designation = designation;
        Department = department;
        EmploymentType = employmentType;
        CostRate = costRate;
        IsActive = isActive;
        Skills = (skills ?? Enumerable.Empty<EmployeeSkill>()).ToList().AsReadOnly();
    }
}

/// <summary>
/// Represents an employee's proficiency in a specific skill.
/// Value object.
/// </summary>
public sealed record EmployeeSkill
{
    /// <summary>
    /// Skill identifier (normalized).
    /// </summary>
    public string SkillId { get; init; }

    /// <summary>
    /// Proficiency level between 1 and 5.
    /// </summary>
    public ProficiencyLevel Proficiency { get; init; }

    public EmployeeSkill(string skillId, ProficiencyLevel proficiency)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("SkillId must be provided", nameof(skillId));

        if (!Enum.IsDefined(typeof(ProficiencyLevel), proficiency))
            throw new ArgumentOutOfRangeException(nameof(proficiency));

        SkillId = skillId.Trim();
        Proficiency = proficiency;
    }
}

/// <summary>
/// Strongly-typed proficiency levels (1..5).
/// </summary>
public enum ProficiencyLevel
{
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5
}

/// <summary>
/// Employment type categories.
/// </summary>
public enum EmploymentType
{
    FullTime,
    Contract,
    Intern,
    Consultant
}

/// <summary>
/// Represents a project in the RMS domain.
/// Entity with its own identity.
/// </summary>
public sealed class Project
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Project name (non-empty).
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Client name (non-empty).
    /// </summary>
    public string ClientName { get; init; }

    /// <summary>
    /// Project start date.
    /// </summary>
    public DateOnly StartDate { get; init; }

    /// <summary>
    /// Optional project end date. When provided, EndDate must be greater than or equal to StartDate.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// Current status of the project.
    /// </summary>
    public ProjectStatus Status { get; init; }

    /// <summary>
    /// Required skills and minimum proficiency for the project.
    /// </summary>
    public IReadOnlyList<SkillRequirement> RequiredSkills { get; init; }

    public Project(
        string name,
        string clientName,
        DateOnly startDate,
        DateOnly? endDate,
        ProjectStatus status,
        IReadOnlyList<SkillRequirement>? requiredSkills = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name must be provided", nameof(name));

        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("ClientName must be provided", nameof(clientName));

        if (endDate.HasValue && endDate.Value < startDate)
            throw new ArgumentException("EndDate must be the same or after StartDate", nameof(endDate));

        Name = name.Trim();
        ClientName = clientName.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Status = status;
        RequiredSkills = (requiredSkills ?? Enumerable.Empty<SkillRequirement>()).ToList().AsReadOnly();
    }
}

/// <summary>
/// Project status categories.
/// </summary>
public enum ProjectStatus
{
    Planned,
    Active,
    OnHold,
    Completed,
    Cancelled
}

/// <summary>
/// Requirement of a skill for a project, with a minimum proficiency level.
/// Value object.
/// </summary>
public sealed record SkillRequirement
{
    public string SkillId { get; init; }
    public ProficiencyLevel MinimumProficiency { get; init; }

    public SkillRequirement(string skillId, ProficiencyLevel minimumProficiency)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new ArgumentException("SkillId must be provided", nameof(skillId));

        if (!Enum.IsDefined(typeof(ProficiencyLevel), minimumProficiency))
            throw new ArgumentOutOfRangeException(nameof(minimumProficiency));

        SkillId = skillId.Trim();
        MinimumProficiency = minimumProficiency;
    }
}

/// <summary>
/// Represents an allocation of an employee to a project for a date range and percentage.
/// Entity with its own identity because allocations have independent lifecycle.
/// </summary>
public sealed class Allocation
{
    /// <summary>
    /// Allocation unique identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Employee unique identifier.
    /// </summary>
    public Guid EmployeeId { get; init; }

    /// <summary>
    /// Project unique identifier.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Allocation start date.
    /// </summary>
    public DateOnly StartDate { get; init; }

    /// <summary>
    /// Optional allocation end date. When provided, must be >= StartDate.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// Allocation percentage (1-100).
    /// </summary>
    public decimal AllocationPercentage { get; init; }

    /// <summary>
    /// Indicates whether the allocation is currently active. Releasing an allocation sets this false
    /// but preserves the record for historical reporting.
    /// </summary>
    public bool IsActive { get; init; } = true;

    public Allocation(Guid employeeId, Guid projectId, DateOnly startDate, DateOnly? endDate, decimal allocationPercentage)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("EmployeeId must be provided", nameof(employeeId));

        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must be provided", nameof(projectId));

        if (endDate.HasValue && endDate.Value < startDate)
            throw new ArgumentException("EndDate must be the same or after StartDate", nameof(endDate));

        if (allocationPercentage <= 0m || allocationPercentage > 100m)
            throw new ArgumentOutOfRangeException(nameof(allocationPercentage), "AllocationPercentage must be > 0 and <= 100");

        EmployeeId = employeeId;
        ProjectId = projectId;
        StartDate = startDate;
        EndDate = endDate;
        AllocationPercentage = allocationPercentage;
    }
}

