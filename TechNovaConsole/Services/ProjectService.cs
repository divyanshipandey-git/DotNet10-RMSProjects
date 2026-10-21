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
/// Application service for project-related operations and business rules.
/// Responsible for validating project data and coordinating persistence via IProjectRepository.
/// </summary>
public sealed class ProjectService
{
    private readonly IProjectRepository _repository;

    public ProjectService(IProjectRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Creates a new project after validation. Returns the created project on success.
    /// </summary>
    public async Task<Result<Project>> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project is null) return Result<Project>.Failure("Project must be provided.");

        var validation = ValidateProject(project);
        if (!validation.IsSuccess) return validation;

        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (all.Any(p => p.Id == project.Id)) return Result<Project>.Failure("A project with the same Id already exists.");

        try
        {
            await _repository.AddOrUpdateAsync(project, cancellationToken).ConfigureAwait(false);
            return Result<Project>.Success(project);
        }
        catch (Exception ex)
        {
            return Result<Project>.Failure($"Failed to create project: {ex.Message}");
        }
    }

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(cancellationToken);

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return Result.Failure("Id must be provided.");
        var existing = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null) return Result.Failure("Project not found.");

        try
        {
            await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete project: {ex.Message}");
        }
    }

    public async Task<Result<Project>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return Result<Project>.Failure("Id must be provided.");
        var p = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (p is null) return Result<Project>.Failure("Project not found.");
        return Result<Project>.Success(p);
    }

    public async Task<Result<Project>> UpdateAsync(Guid id, ProjectUpdate update, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return Result<Project>.Failure("Id must be provided.");
        if (update is null) return Result<Project>.Failure("Update must be provided.");

        var existing = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null) return Result<Project>.Failure("Project not found.");

        var candidate = new Project(
            name: string.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name,
            clientName: string.IsNullOrWhiteSpace(update.ClientName) ? existing.ClientName : update.ClientName,
            startDate: update.StartDate ?? existing.StartDate,
            endDate: update.EndDate ?? existing.EndDate,
            status: update.Status ?? existing.Status,
            requiredSkills: update.RequiredSkills ?? existing.RequiredSkills)
        {
            // preserve identity
            Id = existing.Id
        };

        var validation = ValidateProject(candidate);
        if (!validation.IsSuccess) return validation;

        try
        {
            await _repository.AddOrUpdateAsync(candidate, cancellationToken).ConfigureAwait(false);
            return Result<Project>.Success(candidate);
        }
        catch (Exception ex)
        {
            return Result<Project>.Failure($"Failed to update project: {ex.Message}");
        }
    }

    private Result<Project> ValidateProject(Project project)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(project.Name)) errors.Add("Project name is required.");
        if (string.IsNullOrWhiteSpace(project.ClientName)) errors.Add("Client name is required.");
        if (project.EndDate.HasValue && project.EndDate.Value < project.StartDate) errors.Add("EndDate must be same or after StartDate.");

        if (project.RequiredSkills == null || project.RequiredSkills.Count == 0) errors.Add("At least one required skill must be specified.");
        else
        {
            foreach (var req in project.RequiredSkills)
            {
                if (string.IsNullOrWhiteSpace(req.SkillId)) errors.Add("SkillId is required for each skill requirement.");
                if (!Enum.IsDefined(typeof(ProficiencyLevel), req.MinimumProficiency)) errors.Add($"Invalid proficiency for skill {req.SkillId}.");
            }
        }

        if (errors.Count > 0) return Result<Project>.Failure(errors);
        return Result<Project>.Success(project);
    }
}

/// <summary>
/// DTO for updating a project. Null values mean leave unchanged.
/// </summary>
public sealed class ProjectUpdate
{
    public string? Name { get; init; }
    public string? ClientName { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public ProjectStatus? Status { get; init; }
    public IReadOnlyList<SkillRequirement>? RequiredSkills { get; init; }
}
