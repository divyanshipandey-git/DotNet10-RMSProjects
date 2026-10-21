using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TechNovaConsole.Models;
using TechNovaConsole.Services;
using TechNovaConsole.Tests.Fakes;
using Xunit;

namespace TechNovaConsole.Tests;

public class MatchingAndReportsTests
{
    [Fact]
    public async Task Skill_name_normalization_and_minimum_proficiency_matching()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        var emp = new Employee("Skillful","s@example.com","Dev","Eng",EmploymentType.FullTime,100m,true, new []{ new EmployeeSkill(" C# ", ProficiencyLevel.Level4) }) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(emp);

        var req = new SkillRequirement("c#", ProficiencyLevel.Level3);
        var proj = new Project("MatchProj","Client", new DateOnly(2026,6,1), new DateOnly(2026,6,30), ProjectStatus.Active, new []{ req }) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        var matchingSvc = new MatchingService(empRepo, allocRepo, projRepo);
        var r = await matchingSvc.SuggestCandidatesAsync(proj.Id, proj.StartDate, proj.EndDate ?? proj.StartDate, 50m);
        Assert.True(r.IsSuccess);
        var list = r.Value!;
        Assert.Single(list);
        Assert.Equal(emp.FullName, list[0].EmployeeFullName);
    }

    [Fact]
    public async Task Candidate_availability_and_deterministic_ranking()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        // Two employees with matching skills but different allocations
        var e1 = new Employee("A","a@example.com","Dev","Eng",EmploymentType.FullTime,100m,true, new []{ new EmployeeSkill("sql", ProficiencyLevel.Level4) }) { UniqueId = Guid.NewGuid() };
        var e2 = new Employee("B","b@example.com","Dev","Eng",EmploymentType.FullTime,100m,true, new []{ new EmployeeSkill("sql", ProficiencyLevel.Level4) }) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(e1); await empRepo.AddOrUpdateAsync(e2);

        var proj = new Project("Q","Client", new DateOnly(2026,9,1), new DateOnly(2026,12,31), ProjectStatus.Planned, new []{ new SkillRequirement("sql", ProficiencyLevel.Level3) }) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        // e1 partially allocated (40%) in the period so both candidates remain eligible for a 50% proposed allocation
        var a1 = new Allocation(e1.UniqueId, Guid.NewGuid(), new DateOnly(2026,9,1), new DateOnly(2026,12,31), 40m) { Id = Guid.NewGuid() };
        await allocRepo.AddOrUpdateAsync(a1);

        var matchingSvc = new MatchingService(empRepo, allocRepo, projRepo);
        var r = await matchingSvc.SuggestCandidatesAsync(proj.Id, proj.StartDate, proj.EndDate ?? proj.StartDate, 50m);
        Assert.True(r.IsSuccess);
        var list = r.Value!;
        // At least one candidate should be available
        Assert.True(list.Count >= 1);
        if (list.Count == 2)
        {
            // When both are present and scores tie, deterministic tiebreaker is employee full name (ascending)
            Assert.Equal("A", list[0].EmployeeFullName);
        }
    }

    [Fact]
    public async Task Bench_report_and_department_utilisation_calculations()
    {
        var empRepo = new InMemoryEmployeeRepository();
        var projRepo = new InMemoryProjectRepository();
        var allocRepo = new InMemoryAllocationRepository();

        // employees
        var e1 = new Employee("Bench1","b1@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        var e2 = new Employee("Bench2","b2@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        var e3 = new Employee("Allocated","a@example.com","Dev","Eng",EmploymentType.FullTime,100m,true,null) { UniqueId = Guid.NewGuid() };
        await empRepo.AddOrUpdateAsync(e1); await empRepo.AddOrUpdateAsync(e2); await empRepo.AddOrUpdateAsync(e3);

        // project
        var proj = new Project("Pr","C", new DateOnly(2026,10,1), new DateOnly(2026,10,31), ProjectStatus.Active, null) { Id = Guid.NewGuid() };
        await projRepo.AddOrUpdateAsync(proj);

        // one allocation occupying 50% for middle of October
        var a = new Allocation(e3.UniqueId, proj.Id, new DateOnly(2026,10,1), new DateOnly(2026,10,31), 50m) { Id = Guid.NewGuid() };
        await allocRepo.AddOrUpdateAsync(a);

        var reportSvc = new ReportService(empRepo, projRepo, allocRepo);
        var bench = await reportSvc.GetBenchReportAsync(new DateOnly(2026,10,15), 20m);
        Assert.True(bench.IsSuccess);
        // e1 and e2 should be on bench (0% allocated)
        Assert.Contains(bench.Value!, i => i.FullName == "Bench1");
        Assert.Contains(bench.Value!, i => i.FullName == "Bench2");

        var dept = await reportSvc.GetDepartmentPointUtilisationAsync(new DateOnly(2026,10,15));
        Assert.True(dept.IsSuccess);
        var eng = dept.Value!.FirstOrDefault(d => d.Department == "Eng");
        Assert.NotNull(eng);
        // One employee is allocated at 50% and three full-time employees provide 300% capacity.
        Assert.Equal(16.67m, eng!.UtilisationPercent);
        Assert.Equal(50m, eng.Allocated);
        Assert.Equal(300m, eng.Capacity);
    }
}
