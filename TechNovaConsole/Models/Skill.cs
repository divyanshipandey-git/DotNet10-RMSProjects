using System;

namespace TechNovaConsole.Models;

/// <summary>
/// Represents a skill with an identifier and display name.
/// Value object: identity derived from normalized name.
/// </summary>
public sealed record Skill
{
    /// <summary>
    /// Normalized identifier for the skill (lower-case, no surrounding whitespace).
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Human-friendly display name for the skill.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Creates a new Skill from a name. Id is derived by normalizing the name.
    /// </summary>
    /// <param name="name">The skill name.</param>
    public Skill(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name must be provided", nameof(name));

        Name = name.Trim();
        Id = Normalize(Name);
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
