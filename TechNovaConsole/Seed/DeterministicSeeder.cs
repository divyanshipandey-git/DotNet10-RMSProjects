using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;
using TechNovaConsole.Services;

namespace TechNovaConsole.Seed;

/// <summary>
/// Deterministic seeder for development only.
/// Strategy:
/// - Uses fixed GUIDs for employees, projects and allocations so the operation is idempotent.
/// - Adds missing records only; does not remove or overwrite existing user data.
/// - Uses application services where appropriate to validate business rules.
/// Invocation: dotnet run --project TechNovaConsole/TechNovaConsole.csproj -- --seed
/// To reset development data safely: remove or edit the specific seeded IDs from App_Data/*.json files
/// or delete the seeded entries from those files (do not delete files wholesale in a production dataset).
/// </summary>
public sealed class DeterministicSeeder
{
    private readonly IEmployeeRepository _empRepo;
    private readonly IProjectRepository _projRepo;
    private readonly IAllocationRepository _allocRepo;
    private readonly EmployeeService _empSvc;
    private readonly ProjectService _projSvc;
    private readonly AllocationService _allocSvc;
    private readonly ReportService _reportSvc;

    public DeterministicSeeder(
        IEmployeeRepository empRepo,
        IProjectRepository projRepo,
        IAllocationRepository allocRepo,
        EmployeeService empSvc,
        ProjectService projSvc,
        AllocationService allocSvc,
        ReportService reportSvc)
    {
        _empRepo = empRepo;
        _projRepo = projRepo;
        _allocRepo = allocRepo;
        _empSvc = empSvc;
        _projSvc = projSvc;
        _allocSvc = allocSvc;
        _reportSvc = reportSvc;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting deterministic seed...");

        var existingEmps = (await _empRepo.GetAllAsync(cancellationToken)).ToList();
        var existingProjs = (await _projRepo.GetAllAsync(cancellationToken)).ToList();
        var existingAllocs = (await _allocRepo.GetAllAsync(cancellationToken)).ToList();

        // Fixed employees
        var employees = new[]
        {
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000001"), Email: "alice.jones@acmecorp.com", Name: "Alice Jones", Dept: "Engineering", Designation: "Senior Developer", Rate: 120m, Skills: new [] { ("C#",4), ("SQL",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000002"), Email: "bob.singh@acmecorp.com", Name: "Bob Singh", Dept: "Engineering", Designation: "Developer", Rate: 90m, Skills: new [] { ("C#",3), ("JavaScript",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000003"), Email: "carla.reyes@acmecorp.com", Name: "Carla Reyes", Dept: "IT", Designation: "DevOps Engineer", Rate: 110m, Skills: new [] { ("DevOps",4), ("Cloud",4) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000004"), Email: "daniel.kumar@acmecorp.com", Name: "Daniel Kumar", Dept: "Finance", Designation: "Analyst", Rate: 70m, Skills: new [] { ("Excel",4), ("BI",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000005"), Email: "eva.chen@acmecorp.com", Name: "Eva Chen", Dept: "Engineering", Designation: "Principal Engineer", Rate: 150m, Skills: new [] { ("C#",5), ("Architecture",5) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000006"), Email: "frank.li@acmecorp.com", Name: "Frank Li", Dept: "HR", Designation: "HR Manager", Rate: 65m, Skills: new [] { ("Communication",4) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000007"), Email: "gina.patel@acmecorp.com", Name: "Gina Patel", Dept: "Engineering", Designation: "Developer", Rate: 85m, Skills: new [] { ("Java",4), ("SQL",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000008"), Email: "harry.owens@acmecorp.com", Name: "Harry Owens", Dept: "Support", Designation: "Technician", Rate: 55m, Skills: new [] { ("Windows",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-000000000009"), Email: "irene.kim@acmecorp.com", Name: "Irene Kim", Dept: "Engineering", Designation: "Developer", Rate: 95m, Skills: new [] { ("C#",4), ("React",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000A"), Email: "jack.miller@acmecorp.com", Name: "Jack Miller", Dept: "Engineering", Designation: "Intern", Rate: 30m, Skills: new [] { ("Python",2) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000B"), Email: "karen.ross@acmecorp.com", Name: "Karen Ross", Dept: "Finance", Designation: "Senior Analyst", Rate: 95m, Skills: new [] { ("Excel",5), ("BI",4) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000C"), Email: "liam.woods@acmecorp.com", Name: "Liam Woods", Dept: "Engineering", Designation: "Consultant", Rate: 130m, Skills: new [] { ("Cloud",5), ("DevOps",4) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000D"), Email: "maria.santos@acmecorp.com", Name: "Maria Santos", Dept: "HR", Designation: "Coordinator", Rate: 50m, Skills: new [] { ("Communication",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000E"), Email: "nate.brown@acmecorp.com", Name: "Nate Brown", Dept: "Engineering", Designation: "Developer", Rate: 88m, Skills: new [] { ("C#",3), ("SQL",3) }),
            (Id: Guid.Parse("a1111111-0000-4000-8000-00000000000F"), Email: "olivia.green@acmecorp.com", Name: "Olivia Green", Dept: "Engineering", Designation: "Developer", Rate: 92m, Skills: new [] { ("JavaScript",4), ("React",4) })
        };

        // Create employees if missing
        foreach (var e in employees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existsById = await _empRepo.GetByIdAsync(e.Id, cancellationToken).ConfigureAwait(false);
            if (existsById != null)
            {
                Console.WriteLine($"Employee exists: {e.Name} ({e.Email})");
                continue;
            }

            // don't duplicate if email already present
            if (existingEmps.Any(x => string.Equals(x.Email, e.Email, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"Skipping seed: email already present {e.Email}");
                continue;
            }

            var skills = e.Skills.Select(s => new EmployeeSkill(s.Item1.Trim(), (ProficiencyLevel)s.Item2)).ToList();
            var emp = new Employee(e.Name, e.Email, e.Designation, e.Dept, EmploymentType.FullTime, e.Rate, true, skills) { UniqueId = e.Id };
            var res = await _empSvc.AddEmployeeAsync(emp, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(res.IsSuccess ? $"Seeded employee: {e.Name}" : $"Failed to seed employee {e.Name}: {string.Join(',', res.Errors)}");
        }

        // Projects (deterministic)
        var projects = new[]
        {
            (Id: Guid.Parse("b2222222-0000-4000-8000-000000000001"), Name: "Acme Website", Client: "Acme Corp", Start: new DateOnly(2026,9,1), End: new DateOnly(2027,2,28), Status: ProjectStatus.Active, Skills: new [] { ("C#",4), ("React",3) }),
            (Id: Guid.Parse("b2222222-0000-4000-8000-000000000002"), Name: "Zenith Mobile", Client: "Zenith Ltd.", Start: new DateOnly(2026,11,1), End: new DateOnly(2027,4,30), Status: ProjectStatus.Planned, Skills: new [] { ("Java",4), ("SQL",3) }),
            (Id: Guid.Parse("b2222222-0000-4000-8000-000000000003"), Name: "Nimbus Cloud Migration", Client: "Nimbus Inc.", Start: new DateOnly(2026,8,1), End: new DateOnly(2026,12,31), Status: ProjectStatus.Active, Skills: new [] { ("Cloud",4), ("DevOps",4) }),
            // Atlas BI originally recorded as Completed in earlier drafts; change to Active so seeded allocations can be demonstrated.
            (Id: Guid.Parse("b2222222-0000-4000-8000-000000000004"), Name: "Atlas BI", Client: "Atlas Finance", Start: new DateOnly(2026,7,1), End: new DateOnly(2026,10,31), Status: ProjectStatus.Active, Skills: new [] { ("BI",4), ("SQL",4) }),
            (Id: Guid.Parse("b2222222-0000-4000-8000-000000000005"), Name: "Orion Support", Client: "Orion Services", Start: new DateOnly(2026,10,1), End: (DateOnly?)null, Status: ProjectStatus.Active, Skills: new [] { ("Windows",3) })
        };

        foreach (var p in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await _projRepo.GetByIdAsync(p.Id, cancellationToken).ConfigureAwait(false);
            if (exists != null) { Console.WriteLine($"Project exists: {p.Name}"); continue; }

            if (existingProjs.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"Skipping project seed; name exists: {p.Name}");
                continue;
            }

            var reqs = p.Skills.Select(s => new SkillRequirement(s.Item1.Trim(), (ProficiencyLevel)s.Item2)).ToList();
            var proj = new Project(p.Name, p.Client, p.Start, p.End, p.Status, reqs) { Id = p.Id };
            var res = await _projSvc.CreateAsync(proj, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(res.IsSuccess ? $"Seeded project: {p.Name}" : $"Failed to seed project {p.Name}: {string.Join(',', res.Errors)}");
        }

        // Allocations (10) - ensure they refer to seeded or existing employees/projects and do not violate 100% capacity.
        var allocations = new[]
        {
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000001"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000001"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000001"), Start: new DateOnly(2026,9,15), End: new DateOnly(2026,12,31), Pct: 50m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000002"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000002"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000001"), Start: new DateOnly(2026,10,1), End: new DateOnly(2027,2,28), Pct: 80m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000003"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000003"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000003"), Start: new DateOnly(2026,8,15), End: new DateOnly(2026,11,30), Pct: 60m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000005"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000005"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000001"), Start: new DateOnly(2026,9,1), End: new DateOnly(2026,12,31), Pct: 30m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000004"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000004"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000004"), Start: new DateOnly(2026,7,10), End: new DateOnly(2026,10,15), Pct: 100m),
            // Provide explicit EndDate for open-ended Orion Support allocation so AllocationService accepts it
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000006"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000009"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000005"), Start: new DateOnly(2026,10,1), End: new DateOnly(2027,1,31), Pct: 40m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000007"), Emp: Guid.Parse("a1111111-0000-4000-8000-00000000000E"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000003"), Start: new DateOnly(2026,9,1), End: new DateOnly(2026,10,31), Pct: 50m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000008"), Emp: Guid.Parse("a1111111-0000-4000-8000-00000000000C"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000003"), Start: new DateOnly(2026,8,1), End: (DateOnly?)new DateOnly(2026,9,30), Pct: 80m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-000000000009"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000007"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000002"), Start: new DateOnly(2026,11,1), End: new DateOnly(2027,3,31), Pct: 70m),
            (Id: Guid.Parse("c3333333-0000-4000-8000-00000000000A"), Emp: Guid.Parse("a1111111-0000-4000-8000-000000000009"), Proj: Guid.Parse("b2222222-0000-4000-8000-000000000002"), Start: new DateOnly(2026,11,15), End: new DateOnly(2027,1,31), Pct: 30m)
        };

        foreach (var a in allocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await _allocRepo.GetByIdAsync(a.Id, cancellationToken).ConfigureAwait(false);
            if (exists != null) { Console.WriteLine($"Allocation exists: {a.Id}"); continue; }

            // Ensure referenced employee and project exist
            var emp = await _empRepo.GetByIdAsync(a.Emp, cancellationToken).ConfigureAwait(false);
            var proj = await _projRepo.GetByIdAsync(a.Proj, cancellationToken).ConfigureAwait(false);
            if (emp == null) { Console.WriteLine($"Skipping allocation {a.Id}: employee not found {a.Emp}"); continue; }
            if (proj == null) { Console.WriteLine($"Skipping allocation {a.Id}: project not found {a.Proj}"); continue; }

            var alloc = new Allocation(a.Emp, a.Proj, a.Start, a.End, a.Pct) { Id = a.Id };
            var res = await _allocSvc.CreateAllocationAsync(alloc, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(res.IsSuccess ? $"Seeded allocation: {a.Id}" : $"Failed to seed allocation {a.Id}: {string.Join(',', res.Errors)}");
        }

        Console.WriteLine("Seed complete. Running verification reports for 2026-10-15...");
        var verifyDate = new DateOnly(2026,10,15);
        var bench = await _reportSvc.GetBenchReportAsync(verifyDate, 20m, cancellationToken).ConfigureAwait(false);
        var projSummary = await _reportSvc.GetProjectStaffingSummaryAsync(verifyDate, null, cancellationToken).ConfigureAwait(false);
        var deptPt = await _reportSvc.GetDepartmentPointUtilisationAsync(verifyDate, cancellationToken).ConfigureAwait(false);

        if (bench.IsSuccess) Console.WriteLine($"Bench items: {bench.Value!.Count}"); else Console.WriteLine($"Bench report error: {string.Join(',', bench.Errors)}");
        if (projSummary.IsSuccess) Console.WriteLine($"Project summaries: {projSummary.Value!.Count}"); else Console.WriteLine($"Project summary error: {string.Join(',', projSummary.Errors)}");
        if (deptPt.IsSuccess) Console.WriteLine($"Department points: {deptPt.Value!.Count}"); else Console.WriteLine($"Department util error: {string.Join(',', deptPt.Errors)}");

        Console.WriteLine("Deterministic seed and verification finished.");
    }
}
