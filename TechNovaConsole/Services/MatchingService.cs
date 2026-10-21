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
/// Suggests available employees for a project based on required skills, proficiencies and current allocations.
/// Matching rules:
/// - Considers only active employees.
/// - Normalizes skill names using Skill normalization (trim + ToLowerInvariant via Skill.Id).
/// - Requires employee proficiency >= required minimum for a skill to be considered matched.
/// - Excludes employees with insufficient available capacity for the proposed allocation percentage.
/// - Uses the same per-day allocation overlap rules as AllocationService via AllocationValidator.
/// </summary>
public sealed class MatchingService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IAllocationRepository _allocationRepo;
    private readonly IProjectRepository _projectRepo;

    /// <summary>
    /// Default proposed allocation percentage used when caller does not specify one.
    /// Chosen as 50% to represent a typical half-time staffing request.
    /// </summary>
    public const decimal DefaultProposedAllocationPercentage = 50m;

    public MatchingService(IEmployeeRepository employeeRepo, IAllocationRepository allocationRepo, IProjectRepository projectRepo)
    {
        _employeeRepo = employeeRepo ?? throw new ArgumentNullException(nameof(employeeRepo));
        _allocationRepo = allocationRepo ?? throw new ArgumentNullException(nameof(allocationRepo));
        _projectRepo = projectRepo ?? throw new ArgumentNullException(nameof(projectRepo));
    }

    /// <summary>
    /// Suggests candidate employees for staffing the specified project between startDate and endDate
    /// with the proposed allocation percentage (inclusive date range). Returns a ranked list of candidates
    /// with matching details and a score. Higher score is better.
    /// </summary>
    public async Task<Result<IReadOnlyList<CandidateSuggestion>>> SuggestCandidatesAsync(
        Guid projectId,
        DateOnly startDate,
        DateOnly endDate,
        decimal proposedAllocationPercentage = DefaultProposedAllocationPercentage,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("ProjectId must be provided.");
        if (endDate < startDate) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("EndDate must be same or after StartDate.");
        if (proposedAllocationPercentage <= 0m || proposedAllocationPercentage > 100m) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("Proposed allocation must be between 1 and 100.");

        var project = await _projectRepo.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("Project not found.");

        // Project must permit allocations for matching to be meaningful
        if (!(project.Status == ProjectStatus.Planned || project.Status == ProjectStatus.Active))
            return Result<IReadOnlyList<CandidateSuggestion>>.Failure($"Project status '{project.Status}' does not permit staffing.");

        // Ensure requested dates fall within project lifecycle
        if (startDate < project.StartDate) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("Requested start date is before project start date.");
        if (project.EndDate.HasValue && endDate > project.EndDate.Value) return Result<IReadOnlyList<CandidateSuggestion>>.Failure("Requested end date is after project end date.");

        var requiredSkills = project.RequiredSkills ?? Array.Empty<SkillRequirement>();
        var employees = await _employeeRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var candidates = new List<CandidateSuggestion>();

        foreach (var emp in employees)
        {
            if (!emp.IsActive) continue; // only active employees

            // Map employee skills into dictionary for fast lookup
            var empSkills = (emp.Skills ?? Array.Empty<EmployeeSkill>()).ToDictionary(s => s.SkillId, StringComparer.OrdinalIgnoreCase);

            var matched = new List<MatchedSkill>();
            var missing = new List<SkillRequirement>();

            foreach (var req in requiredSkills)
            {
                var normId = req.SkillId?.Trim().ToLowerInvariant() ?? string.Empty;
                if (empSkills.TryGetValue(normId, out var es) && (int)es.Proficiency >= (int)req.MinimumProficiency)
                {
                    matched.Add(new MatchedSkill(req.SkillId ?? string.Empty, es.Proficiency));
                }
                else
                {
                    missing.Add(req);
                }
            }

            // Determine available capacity for the employee in the requested period
            var allocations = (await _allocationRepo.GetByEmployeeAsync(emp.UniqueId, cancellationToken).ConfigureAwait(false)).Where(a => a.IsActive).ToList();
            decimal maxAllocated = AllocationValidator.MaxAllocatedPercentage(startDate, endDate, allocations);
            decimal availableCapacity = Math.Max(0m, 100m - maxAllocated);

            // Exclude employees who cannot accept the proposed allocation percentage
            if (availableCapacity < proposedAllocationPercentage) continue;

            // Compute matching score
            decimal matchedRatio = requiredSkills.Count == 0 ? 1m : (decimal)matched.Count / requiredSkills.Count;
            // capacityFactor: how well available capacity covers the requested allocation (0..1)
            decimal capacityFactor = Math.Min(availableCapacity / proposedAllocationPercentage, 1m);
            // Score weighted: 70% skills coverage, 30% capacity suitability
            decimal score = matchedRatio * 70m + capacityFactor * 30m;

            candidates.Add(new CandidateSuggestion
            {
                EmployeeUniqueId = emp.UniqueId,
                EmployeeFullName = emp.FullName,
                EmployeeEmail = emp.Email,
                MatchedSkills = matched,
                MissingSkills = missing,
                AvailableCapacity = availableCapacity,
                Score = Math.Round(score, 2)
            });
        }

        // Rank candidates by descending score, then by number of matched skills, then by FullName for deterministic ordering
        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.MatchedSkills.Count)
            .ThenBy(c => c.EmployeeFullName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return Result<IReadOnlyList<CandidateSuggestion>>.Success(ordered);
    }
}

/// <summary>
/// Suggestion DTO returned by MatchingService.
/// </summary>
public sealed class CandidateSuggestion
{
    public Guid EmployeeUniqueId { get; init; }
    public string EmployeeFullName { get; init; } = string.Empty;
    public string EmployeeEmail { get; init; } = string.Empty;
    public IReadOnlyList<MatchedSkill> MatchedSkills { get; init; } = Array.Empty<MatchedSkill>();
    public IReadOnlyList<SkillRequirement> MissingSkills { get; init; } = Array.Empty<SkillRequirement>();
    public decimal AvailableCapacity { get; init; }
    public decimal Score { get; init; }
}

public sealed class MatchedSkill
{
    public string SkillId { get; init; }
    public ProficiencyLevel Proficiency { get; init; }

    public MatchedSkill(string skillId, ProficiencyLevel proficiency)
    {
        SkillId = skillId;
        Proficiency = proficiency;
    }
}
