using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TechNovaConsole.Models;
using TechNovaConsole.Repositories;
using Xunit;

namespace TechNovaConsole.Tests;

public sealed class JsonRepositoryIntegrationTests
{
    [Fact]
    public async Task Employee_repository_persists_across_instances_and_initializes_missing_file()
    {
        using var data = new TemporaryDataDirectory();
        var employee = new Employee("Persisted", "persisted@example.com", "Developer", "Engineering", EmploymentType.FullTime, 100m, true);

        var first = new JsonEmployeeRepository(data.Path);
        await first.AddAsync(employee);
        var second = new JsonEmployeeRepository(data.Path);

        var loaded = await second.GetByIdAsync(employee.UniqueId);
        Assert.NotNull(loaded);
        Assert.Equal(employee.Email, loaded!.Email);
        Assert.True(File.Exists(Path.Combine(data.Path, "employees.json")));
    }

    [Fact]
    public async Task Project_and_allocation_repositories_persist_across_instances()
    {
        using var data = new TemporaryDataDirectory();
        var project = new Project("Persisted project", "Client", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ProjectStatus.Active,
            new[] { new SkillRequirement("C#", ProficiencyLevel.Level3) });
        var allocation = new Allocation(Guid.NewGuid(), project.Id, project.StartDate, project.EndDate, 50m);

        var projects = new JsonProjectRepository(data.Path);
        var allocations = new JsonAllocationRepository(data.Path);
        await projects.AddOrUpdateAsync(project);
        await allocations.AddOrUpdateAsync(allocation);

        var projectsAfterRestart = new JsonProjectRepository(data.Path);
        var allocationsAfterRestart = new JsonAllocationRepository(data.Path);
        Assert.NotNull(await projectsAfterRestart.GetByIdAsync(project.Id));
        Assert.NotNull(await allocationsAfterRestart.GetByIdAsync(allocation.Id));
    }

    [Fact]
    public void Malformed_project_and_allocation_files_are_preserved_and_reported()
    {
        using var data = new TemporaryDataDirectory();
        File.WriteAllText(Path.Combine(data.Path, "projects.json"), "{ malformed");
        File.WriteAllText(Path.Combine(data.Path, "allocations.json"), "{ malformed");

        Assert.Throws<InvalidOperationException>(() => new JsonProjectRepository(data.Path));
        Assert.Throws<InvalidOperationException>(() => new JsonAllocationRepository(data.Path));
        Assert.True(File.Exists(Path.Combine(data.Path, "projects.json.corrupt.bak")));
        Assert.True(File.Exists(Path.Combine(data.Path, "allocations.json.corrupt.bak")));
    }

    private sealed class TemporaryDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TechNovaConsoleTests", Guid.NewGuid().ToString("N"));

        public TemporaryDataDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}