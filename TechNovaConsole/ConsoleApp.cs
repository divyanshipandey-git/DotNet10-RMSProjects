using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Models;
using TechNovaConsole.Services;
using TechNovaConsole.Utilities;

namespace TechNovaConsole;

/// <summary>
/// Console application presentation layer. Responsible for menus, input and calling application services.
/// Program.cs is the composition root; this class contains presentation only and uses constructor injection.
/// </summary>
public sealed class ConsoleApp
{
    private readonly EmployeeService _employeeService;
    private readonly ProjectService _projectService;
    private readonly AllocationService _allocationService;
    private readonly MatchingService _matchingService;
    private readonly ReportService _reportService;

    public ConsoleApp(
        EmployeeService employeeService,
        ProjectService projectService,
        AllocationService allocationService,
        MatchingService matchingService,
        ReportService reportService)
    {
        _employeeService = employeeService;
        _projectService = projectService;
        _allocationService = allocationService;
        _matchingService = matchingService;
        _reportService = reportService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Welcome to TechNova RMS Console");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ShowMainMenu();
                string? choice = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(choice)) continue;

                switch (choice.Trim())
                {
                    case "1": await EmployeeMenu(cancellationToken); break;
                    case "2": await ProjectMenu(cancellationToken); break;
                    case "3": await AllocationMenu(cancellationToken); break;
                    case "4": await MatchingMenu(cancellationToken); break;
                    case "5": await ReportMenu(cancellationToken); break;
                    case "0": Console.WriteLine("Exiting..."); return;
                    default: Console.WriteLine("Unknown option"); break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
        }
    }

    private static void ShowMainMenu()
    {
        Console.WriteLine();
        Console.WriteLine("Main Menu:");
        Console.WriteLine("1) Employees");
        Console.WriteLine("2) Projects");
        Console.WriteLine("3) Allocations");
        Console.WriteLine("4) Matching");
        Console.WriteLine("5) Reports");
        Console.WriteLine("0) Exit");
        Console.Write("Select: ");
    }

    private async Task<Guid?> ResolveEmployeeUniqueIdAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input)) { Console.WriteLine("No identifier provided."); return null; }

        // Try exact GUID parse first
        if (Guid.TryParse(input, out var g))
        {
            var byId = await _employeeService.GetByIdAsync(g, ct);
            if (byId.IsSuccess) return g;
        }

        // List employees and attempt lookup by email, name or partial unique id
        var list = await _employeeService.ListAsync(ct);
        // Try exact email
        var byEmail = list.FirstOrDefault(e => string.Equals(e.Email?.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byEmail != null) return byEmail.UniqueId;
        // Try exact full name
        var byName = list.FirstOrDefault(e => string.Equals(e.FullName?.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName.UniqueId;
        // try substring match on UniqueId string
        var byUidSub = list.FirstOrDefault(e => e.UniqueId.ToString().IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0);
        if (byUidSub != null) return byUidSub.UniqueId;

        // If not found, attempt to look up project source App_Data/employees.json (project copy) for legacy string ids
        try
        {
            var projectData = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "App_Data", "employees.json");
            var candidatePath = System.IO.Path.GetFullPath(projectData);
            if (System.IO.File.Exists(candidatePath))
            {
                var raw = await System.IO.File.ReadAllTextAsync(candidatePath, ct).ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (TryGetPropertyCaseInsensitive(el, "id", out var idEl))
                        {
                            var idStr = idEl.GetString();
                            if (!string.IsNullOrWhiteSpace(idStr) && string.Equals(idStr.Trim(), input.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                // Found a source entry; try match by email or name to runtime list
                                string? email = null; string? name = null;
                                if (TryGetPropertyCaseInsensitive(el, "email", out var em)) email = em.GetString();
                                if (TryGetPropertyCaseInsensitive(el, "name", out var nm)) name = nm.GetString();

                                if (!string.IsNullOrWhiteSpace(email))
                                {
                                    var byEmail2 = list.FirstOrDefault(e => string.Equals(e.Email?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
                                    if (byEmail2 != null) return byEmail2.UniqueId;
                                }

                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    var byName2 = list.FirstOrDefault(e => string.Equals(e.FullName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                                    if (byName2 != null) return byName2.UniqueId;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore lookup errors - fall through to not found
        }

        Console.WriteLine("Employee not found by provided identifier.");
        return null;

        static bool TryGetPropertyCaseInsensitive(System.Text.Json.JsonElement el, string name, out System.Text.Json.JsonElement value)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }

    private async Task EmployeeMenu(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Employee Menu: 1) List 2) Add 3) Update 4) Deactivate 5) Delete 0) Back");
        Console.Write("Select: ");
        var k = Console.ReadLine();
        if (k == "1")
        {
            var list = await _employeeService.ListAsync(ct);
            foreach (var e in list) Console.WriteLine($"{e.UniqueId} | {e.FullName} | {e.Email} | {e.Department} | Active: {e.IsActive}");
        }
        else if (k == "2")
        {
            Console.Write("Full name: "); var name = Console.ReadLine() ?? string.Empty;
            Console.Write("Email: "); var email = Console.ReadLine() ?? string.Empty;
            Console.Write("Designation: "); var des = Console.ReadLine() ?? string.Empty;
            Console.Write("Department: "); var dept = Console.ReadLine() ?? string.Empty;
            Console.Write("Cost rate (numeric): "); var rateS = Console.ReadLine() ?? "0";
            if (!decimal.TryParse(rateS, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)) rate = 0m;

            // Validate construction to avoid unhandled ArgumentExceptions from domain model
            Employee? emp;
            try
            {
                emp = new Employee(name, email, des, dept, EmploymentType.FullTime, rate, true, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid employee input: {ex.Message}");
                return;
            }

            var r = await _employeeService.AddEmployeeAsync(emp, ct);
            if (r.IsSuccess) Console.WriteLine($"Added: {r.Value!.UniqueId}"); else PrintErrors(r.Errors);
        }
        else if (k == "3")
        {
            // Update employee
            Console.Write("Employee UniqueId to update: "); var idU = Console.ReadLine() ?? string.Empty;
            var uid = await ResolveEmployeeUniqueIdAsync(idU, ct);
            if (!uid.HasValue) return;

            var existingRes = await _employeeService.GetByIdAsync(uid.Value, ct);
            if (!existingRes.IsSuccess) { PrintErrors(existingRes.Errors); return; }
            var existing = existingRes.Value!;

            Console.Write($"Full name ({existing.FullName}): "); var nameU = Console.ReadLine();
            Console.Write($"Email ({existing.Email}): "); var emailU = Console.ReadLine();
            Console.Write($"Designation ({existing.Designation}): "); var desU = Console.ReadLine();
            Console.Write($"Department ({existing.Department}): "); var deptU = Console.ReadLine();
            Console.Write($"Cost rate ({existing.CostRate}): "); var rateUS = Console.ReadLine();
            decimal? rateU = null; if (!string.IsNullOrWhiteSpace(rateUS)) { if (decimal.TryParse(rateUS, NumberStyles.Number, CultureInfo.InvariantCulture, out var rr)) rateU = rr; else { Console.WriteLine("Invalid cost rate"); return; } }

            var update = new EmployeeUpdate
            {
                FullName = string.IsNullOrWhiteSpace(nameU) ? null : nameU,
                Email = string.IsNullOrWhiteSpace(emailU) ? null : emailU,
                Designation = string.IsNullOrWhiteSpace(desU) ? null : desU,
                Department = string.IsNullOrWhiteSpace(deptU) ? null : deptU,
                CostRate = rateU,
                // skills left unchanged in console update for simplicity
            };

            var ur = await _employeeService.UpdateEmployeeAsync(uid.Value, update, ct);
            if (ur.IsSuccess) Console.WriteLine($"Updated: {ur.Value!.UniqueId}"); else PrintErrors(ur.Errors);
        }
        else if (k == "4")
        {
            Console.Write("Employee UniqueId to deactivate: "); var idS = Console.ReadLine() ?? string.Empty;
            if (!Guid.TryParse(idS, out var gid)) { Console.WriteLine("Invalid GUID"); return; }
            var r = await _employeeService.DeactivateAsync(gid, ct);
            if (r.IsSuccess) Console.WriteLine("Employee deactivated."); else PrintErrors(r.Errors);
        }
        else if (k == "5")
        {
            Console.Write("Employee UniqueId to delete: "); var idS = Console.ReadLine() ?? string.Empty;
            if (!Guid.TryParse(idS, out var gid)) { Console.WriteLine("Invalid GUID"); return; }
            var r = await _employeeService.DeleteAsync(gid, ct);
            if (r.IsSuccess) Console.WriteLine("Employee deleted."); else PrintErrors(r.Errors);
        }
    }

    private async Task ProjectMenu(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Project Menu: 1) List 2) Add 0) Back");
        Console.Write("Select: ");
        var k = Console.ReadLine();
        if (k == "1")
        {
            var list = await _projectService.ListAsync(ct);
            foreach (var p in list) Console.WriteLine($"{p.Id} | {p.Name} | {p.ClientName} | {p.Status} | {p.StartDate} - {p.EndDate}");
        }
        else if (k == "2")
        {
            Console.Write("Name: "); var name = Console.ReadLine() ?? string.Empty;
            Console.Write("Client: "); var client = Console.ReadLine() ?? string.Empty;
            Console.Write("Start date (yyyy-MM-dd): "); var sd = Console.ReadLine() ?? string.Empty;
            Console.Write("End date (yyyy-MM-dd) or blank: "); var ed = Console.ReadLine();
            if (!DateOnly.TryParse(sd, out var start)) { Console.WriteLine("Invalid date"); return; }
            DateOnly? end = null; if (!string.IsNullOrWhiteSpace(ed)) { if (DateOnly.TryParse(ed, out var d)) end = d; else { Console.WriteLine("Invalid date"); return; } }
            Console.WriteLine("Enter required skills as name:level comma-separated (e.g. C#:4,SQL:3). At least one required skill is needed.");
            Console.Write("Skills: "); var skillsInput = Console.ReadLine() ?? string.Empty;
            var skillReqs = new System.Collections.Generic.List<SkillRequirement>();
            if (!string.IsNullOrWhiteSpace(skillsInput))
            {
                var parts = skillsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (kv.Length != 2) { Console.WriteLine($"Invalid skill format: {part}"); return; }
                    var skillId = kv[0];
                    if (!int.TryParse(kv[1], out var lvl) || lvl < 1 || lvl > 5) { Console.WriteLine($"Invalid proficiency for {skillId}"); return; }
                    skillReqs.Add(new SkillRequirement(skillId, (ProficiencyLevel)lvl));
                }
            }

            var p = new Project(name, client, start, end, ProjectStatus.Planned, skillReqs);
            var r = await _projectService.CreateAsync(p, ct);
            if (r.IsSuccess) Console.WriteLine($"Created project {r.Value!.Id}"); else PrintErrors(r.Errors);
        }
    }

    private async Task AllocationMenu(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Allocation Menu: 1) List 2) Create 3) Release 0) Back");
        Console.Write("Select: "); var k = Console.ReadLine();
        if (k == "1")
        {
            Console.Write("Employee UniqueId (or blank for all): "); var idS = Console.ReadLine();
            if (Guid.TryParse(idS, out var eid))
            {
                var list = await _allocationService.ListByEmployeeAsync(eid, ct);
                foreach (var a in list) Console.WriteLine($"{a.Id} | {a.EmployeeId} -> {a.ProjectId} | {a.StartDate} - {a.EndDate} | {a.AllocationPercentage}% | Active:{a.IsActive}");
            }
            else
            {
                Console.WriteLine("Provide a valid GUID to list allocations for an employee.");
            }
        }
        else if (k == "2")
        {
            Console.Write("Employee UniqueId: "); if (!Guid.TryParse(Console.ReadLine(), out var empId)) { Console.WriteLine("Invalid GUID"); return; }
            Console.Write("Project Id: "); if (!Guid.TryParse(Console.ReadLine(), out var projId)) { Console.WriteLine("Invalid GUID"); return; }
            Console.Write("Start date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var start)) { Console.WriteLine("Invalid date"); return; }
            Console.Write("End date (yyyy-MM-dd) or blank: "); var endInput = Console.ReadLine(); DateOnly? end = null; if (!string.IsNullOrWhiteSpace(endInput)) { if (!DateOnly.TryParse(endInput, out var tmpEnd)) { Console.WriteLine("Invalid date"); return; } end = tmpEnd; }
            Console.Write("Allocation percent (1-100): "); if (!decimal.TryParse(Console.ReadLine(), out var pct)) { Console.WriteLine("Invalid percent"); return; }
            var alloc = new Allocation(empId, projId, start, end, pct);
            var r = await _allocationService.CreateAllocationAsync(alloc, ct);
            if (r.IsSuccess) Console.WriteLine($"Allocated {r.Value!.Id}"); else PrintErrors(r.Errors);
        }
        else if (k == "3")
        {
            Console.Write("Allocation Id: "); if (!Guid.TryParse(Console.ReadLine(), out var aid)) { Console.WriteLine("Invalid GUID"); return; }
            var r = await _allocationService.ReleaseAllocationAsync(aid, ct);
            if (r.IsSuccess) Console.WriteLine("Released"); else PrintErrors(r.Errors);
        }
    }

    private async Task MatchingMenu(CancellationToken ct)
    {
        Console.Write("Project Id: "); if (!Guid.TryParse(Console.ReadLine(), out var pid)) { Console.WriteLine("Invalid GUID"); return; }
        Console.Write("Start date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var start)) { Console.WriteLine("Invalid date"); return; }
        Console.Write("End date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var end)) { Console.WriteLine("Invalid date"); return; }
        Console.Write($"Proposed percent (default {MatchingService.DefaultProposedAllocationPercentage}): "); var ps = Console.ReadLine(); decimal proposed = MatchingService.DefaultProposedAllocationPercentage; if (!string.IsNullOrWhiteSpace(ps) && decimal.TryParse(ps, out var tmp)) proposed = tmp;
        var r = await _matchingService.SuggestCandidatesAsync(pid, start, end, proposed, ct);
        if (!r.IsSuccess) { PrintErrors(r.Errors); return; }
        foreach (var c in r.Value!)
        {
            Console.WriteLine($"{c.EmployeeFullName} ({c.EmployeeEmail}) - Score: {c.Score} - Available: {c.AvailableCapacity}% - Matched: {string.Join(',', c.MatchedSkills.Select(m=>m.SkillId))}");
        }
    }

    private async Task ReportMenu(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Reports: 1) Bench 2) Project Summary 3) Dept Util (point) 4) Dept Util (period) 0) Back"); Console.Write("Select: "); var k = Console.ReadLine();
        if (k == "1")
        {
            Console.Write("Date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var d)) { Console.WriteLine("Invalid date"); return; }
            Console.Write("Threshold percent (default 20): "); var tpS = Console.ReadLine(); decimal tp = 20m; if (!string.IsNullOrWhiteSpace(tpS) && decimal.TryParse(tpS, out var tmp)) tp = tmp;
            var r = await _reportService.GetBenchReportAsync(d, tp, ct);
            if (!r.IsSuccess) { PrintErrors(r.Errors); return; }
            foreach (var i in r.Value!) Console.WriteLine($"{i.FullName} | {i.Department} | Allocated: {i.AllocatedPercent}% | Available: {i.AvailablePercent}%");
        }
        else if (k == "2")
        {
            Console.Write("Date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var d)) { Console.WriteLine("Invalid date"); return; }
            var r = await _reportService.GetProjectStaffingSummaryAsync(d, null, ct);
            if (!r.IsSuccess) { PrintErrors(r.Errors); return; }
            foreach (var s in r.Value!) Console.WriteLine($"{s.ProjectName} | Status: {s.Status} | Allocated%: {s.AllocatedPercent}% | AllocCount: {s.ActiveAllocationCount}");
        }
        else if (k == "3")
        {
            Console.Write("Date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var d)) { Console.WriteLine("Invalid date"); return; }
            var r = await _reportService.GetDepartmentPointUtilisationAsync(d, ct);
            if (!r.IsSuccess) { PrintErrors(r.Errors); return; }
            foreach (var dpt in r.Value!) Console.WriteLine($"{dpt.Department} | Util: {dpt.UtilisationPercent}% | Allocated: {dpt.Allocated} | Capacity: {dpt.Capacity}");
        }
        else if (k == "4")
        {
            Console.Write("Start date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var s)) { Console.WriteLine("Invalid date"); return; }
            Console.Write("End date (yyyy-MM-dd): "); if (!DateOnly.TryParse(Console.ReadLine(), out var e)) { Console.WriteLine("Invalid date"); return; }
            var r = await _reportService.GetDepartmentPeriodUtilisationAsync(s, e, ct);
            if (!r.IsSuccess) { PrintErrors(r.Errors); return; }
            foreach (var dpt in r.Value!) Console.WriteLine($"{dpt.Department} | Util: {dpt.UtilisationPercent}% | AllocDays: {dpt.AllocatedEmployeeDays} | CapDays: {dpt.CapacityEmployeeDays}");
        }
    }

    private static void PrintErrors(System.Collections.Generic.IReadOnlyList<string> errors)
    {
        foreach (var er in errors) Console.WriteLine($"Error: {er}");
    }
}
