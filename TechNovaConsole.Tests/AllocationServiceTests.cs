using System;
using System.Linq;
using System.Threading.Tasks;
using TechNovaConsole.Models;
using TechNovaConsole.Services;
using TechNovaConsole.Tests.Fakes;
using Xunit;

namespace TechNovaConsole.Tests;

public class AllocationServiceTests
{
    [Fact]
    public async Task Allocation_overlap_detection_and_capacity_rule()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Test User","u@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);

        var proj = new Project("P1","C1", new DateOnly(2026,1,1), new DateOnly(2026,12,31), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var allocationService = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());

        // Create an allocation 50% for Jan
        var a1 = new Allocation(emp.UniqueId, proj.Id, new DateOnly(2026,1,1), new DateOnly(2026,1,31), 50m);
        var r1 = await allocationService.CreateAllocationAsync(a1);
        Assert.True(r1.IsSuccess);

        // Create another overlapping allocation that would bring total to 100% - allowed
        var a2 = new Allocation(emp.UniqueId, proj.Id, new DateOnly(2026,1,15), new DateOnly(2026,1,31), 50m);
        var r2 = await allocationService.CreateAllocationAsync(a2);
        Assert.True(r2.IsSuccess);

        // One more overlapping allocation should fail (would exceed 100%)
        var a3 = new Allocation(emp.UniqueId, proj.Id, new DateOnly(2026,1,20), new DateOnly(2026,1,25), 1m);
        var r3 = await allocationService.CreateAllocationAsync(a3);
        Assert.False(r3.IsSuccess);
        Assert.Contains("exceed 100%", string.Join(' ', r3.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inclusive_date_range_boundaries_work()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Inclusive","inc@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);
        var proj = new Project("P2","C2", new DateOnly(2026,2,1), new DateOnly(2026,2,28), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var svc = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());

        // Allocation that starts exactly on project start and ends on project end
        var a = new Allocation(emp.UniqueId, proj.Id, proj.StartDate, proj.EndDate, 100m);
        var r = await svc.CreateAllocationAsync(a);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public async Task Inactive_employee_rejected()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Inactive","x@example.com","Dev","Eng",EmploymentType.FullTime,100m,false,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);
        var proj = new Project("P3","C3", new DateOnly(2026,3,1), new DateOnly(2026,3,31), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var svc = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());
        var a = new Allocation(emp.UniqueId, proj.Id, proj.StartDate, proj.EndDate, 50m);
        var r = await svc.CreateAllocationAsync(a);
        Assert.False(r.IsSuccess);
        Assert.Contains("inactive", string.Join(' ', r.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allocation_dates_outside_project_dates_rejected()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Outside","o@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);
        var proj = new Project("P4","C4", new DateOnly(2026,4,10), new DateOnly(2026,4,20), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var svc = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());
        var a = new Allocation(emp.UniqueId, proj.Id, new DateOnly(2026,4,1), new DateOnly(2026,4,9), 10m);
        var r = await svc.CreateAllocationAsync(a);
        Assert.False(r.IsSuccess);
        Assert.Contains("start before project", string.Join(' ', r.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Releasing_allocation_preserves_history()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Rel","r@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);
        var proj = new Project("P5","C5", new DateOnly(2026,5,1), new DateOnly(2026,5,31), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var svc = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());
        var a = new Allocation(emp.UniqueId, proj.Id, proj.StartDate, proj.EndDate, 50m) { Id = Guid.NewGuid() };
        var cr = await svc.CreateAllocationAsync(a);
        Assert.True(cr.IsSuccess);

        var rel = await svc.ReleaseAllocationAsync(a.Id);
        Assert.True(rel.IsSuccess);

        var stored = await allocRepo.GetByIdAsync(a.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.IsActive);
    }

    [Fact]
    public async Task Concurrent_allocations_cannot_exceed_capacity()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();
        var employee = new Employee("Concurrent", "concurrent@example.com", "Dev", "Eng", EmploymentType.FullTime, 100m, true) { UniqueId = Guid.NewGuid() };
        var project = new Project("Concurrent project", "Client", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ProjectStatus.Active,
            new[] { new SkillRequirement("C#", ProficiencyLevel.Level1) }) { Id = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(employee);
        await projRepo.AddOrUpdateAsync(project);
        var service = new AllocationService(allocRepo, empRepo, projRepo, new TechNovaConsole.Notifications.ConsoleNotificationService());

        var first = new Allocation(employee.UniqueId, project.Id, project.StartDate, project.EndDate, 60m);
        var second = new Allocation(employee.UniqueId, project.Id, project.StartDate, project.EndDate, 60m);
        var results = await Task.WhenAll(service.CreateAllocationAsync(first), service.CreateAllocationAsync(second));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => !result.IsSuccess);
        Assert.Equal(60m, (await allocRepo.GetByEmployeeAsync(employee.UniqueId)).Sum(a => a.AllocationPercentage));
    }

    [Fact]
    public async Task Delete_deactivates_employee_and_preserves_identity()
    {
        var repository = new InMemoryEmployeeRepository();
        var employee = new Employee("Retained", "retained@example.com", "Dev", "Eng", EmploymentType.FullTime, 100m, true) { UniqueId = Guid.NewGuid() };
        await repository.AddOrUpdateAsync(employee);
        var service = new EmployeeService(repository);

        var result = await service.DeleteAsync(employee.UniqueId);

        Assert.True(result.IsSuccess);
        var retained = await repository.GetByIdAsync(employee.UniqueId);
        Assert.NotNull(retained);
        Assert.False(retained!.IsActive);
    }
}
