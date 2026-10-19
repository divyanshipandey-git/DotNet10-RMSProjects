using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;

namespace TechNovaConsole.Tests.Fakes;

// Very small in-memory repository implementations used for unit testing.
public sealed class InMemoryEmployeeRepository : IEmployeeRepository
{
    private readonly ConcurrentDictionary<Guid, Employee> _store = new();

    public Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (employee.UniqueId == Guid.Empty)
        {
            // If UniqueId not set, create one for tests
            var prop = typeof(Employee).GetProperty("UniqueId");
            prop?.SetValue(employee, Guid.NewGuid());
        }

        _store[employee.UniqueId] = employee;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        _store[employee.UniqueId] = employee;
        return Task.CompletedTask;
    }

    // Compatibility helper used by tests still calling AddOrUpdateAsync
    public Task AddOrUpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (employee.UniqueId == Guid.Empty)
        {
            var prop = typeof(Employee).GetProperty("UniqueId");
            prop?.SetValue(employee, Guid.NewGuid());
        }

        _store[employee.UniqueId] = employee;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(id, out var e) ? e : null as Employee);

    public Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Employee>)_store.Values.ToList());
}

public sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly ConcurrentDictionary<Guid, Project> _store = new();

    public Task AddOrUpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _store[project.Id] = project;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(id, out var p) ? p : null as Project);

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Project>)_store.Values.ToList());
}

public sealed class InMemoryAllocationRepository : IAllocationRepository
{
    private readonly ConcurrentDictionary<Guid, Allocation> _store = new();

    public Task AddOrUpdateAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        _store[allocation.Id] = allocation;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<Allocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(id, out var a) ? a : null as Allocation);

    public Task<IReadOnlyList<Allocation>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Allocation>)_store.Values.ToList());

    public Task<IReadOnlyList<Allocation>> GetByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Allocation>)_store.Values.Where(a => a.EmployeeId == employeeId).ToList());

    public Task<IReadOnlyList<Allocation>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<Allocation>)_store.Values.Where(a => a.ProjectId == projectId).ToList());
}
