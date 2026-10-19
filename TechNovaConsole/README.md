TechNovaConsole - Resource Management System (RMS) Console

Overview

A small console RMS implemented with dependency injection (Microsoft.Extensions.Hosting and Microsoft.Extensions.DependencyInjection) and JSON file persistence. Program.cs is the composition root only; ConsoleApp handles presentation and uses application services via constructor injection.

TechNovaConsole - Resource Management System (RMS)
===============================================

Project overview
----------------
TechNovaConsole is a small console-based Resource Management System (RMS) implemented as a layered .NET application. It provides:
- JSON-backed repositories for Employees, Projects and Allocations.
- Application services implementing business rules for employee/project/allocation management.
- A console presentation layer (ConsoleApp) that provides menus to list and create employees and projects, create and release allocations, run skill matching and several reports.
- Deterministic seeding for development data (DeterministicSeeder).
- Focused unit tests (xUnit) using in-memory repository fakes.

Scope and intent
----------------
This project is intentionally small and focused on business rules and JSON persistence. It is not a web application or a database-backed system. The console UI is a lightweight presentation layer used to exercise the services.

Prerequisites
-------------
- .NET 10 SDK installed (dotnet CLI). The project targets net10.0 and uses C# 14 language features.

Solution structure
------------------
- TechNovaConsole/                       - Console application project (executable)
  - Program.cs                            - Composition root (DI/hosting)
  - ConsoleApp.cs                         - Presentation, menus and input parsing
  - Models/                                - Domain model types (Employee, Project, Allocation, Skill, SkillRequirement, enums)
  - Interfaces/                            - Repository and notification interfaces
  - Repositories/                          - JsonEmployeeRepository, JsonProjectRepository, JsonAllocationRepository
  - Services/                              - EmployeeService, ProjectService, AllocationService, MatchingService, ReportService
  - Seed/                                  - DeterministicSeeder (idempotent development seed)
  - App_Data/                              - JSON files used for persistence (employees.json, projects.json, allocations.json)
- TechNovaConsole.Tests/                 - xUnit tests (in-memory fakes)

Restore, build, test, seed and run
----------------------------------
From the repository root run the following commands (PowerShell / cross-platform dotnet CLI):

- Restore dependencies:
  dotnet restore

- Build the console project only:
  dotnet build TechNovaConsole/TechNovaConsole.csproj

- Run tests (xUnit project):
  dotnet test TechNovaConsole.Tests/TechNovaConsole.Tests.csproj

- Seed deterministic development data (idempotent):
  dotnet run --project TechNovaConsole/TechNovaConsole.csproj -- --seed

- Run the interactive console application:
  dotnet run --project TechNovaConsole/TechNovaConsole.csproj

How JSON persistence works
--------------------------
- Repositories: JsonEmployeeRepository, JsonProjectRepository and JsonAllocationRepository implement repository interfaces and persist their in-memory entities to JSON files in a local App_Data directory.
- Path resolution: repositories look for App_Data under the application base directory (Path.Combine(AppContext.BaseDirectory, "App_Data")). The project file is configured to copy App_Data files to the build output so persistence works from the built executable.
- Write coordination: repositories coordinate reads/writes with a SemaphoreSlim to avoid concurrent write conflicts. When writing, repositories write to a temp file then use File.Replace (with a fallback) to reduce corruption risk.
- Data is preserved across application restarts: when the application writes new or updated entities the JSON files are modified and persisted.

Implemented features (actual)
----------------------------
- EmployeeService: list, add, update and deactivate employees. Console UI exposes list and add.
- ProjectService: list, create and update projects. Console UI exposes list and create (create prompts for required skills).
- AllocationService: create allocations, release (soft) allocations and list allocations by employee or project. It validates business rules and emits non-fatal notifications via INotificationService. Console UI exposes list/create/release by id.
- MatchingService: suggests candidate employees for a project using normalized skill matching, proficiency checks and capacity evaluation. Console UI exposes a Matching menu.
- ReportService: bench report, project staffing summary, department point- and period-utilisation. Console UI exposes Reports menu with options.
- DeterministicSeeder: idempotent development seed that inserts deterministic employees/projects/allocations when invoked with --seed.
- ConsoleNotificationService: a simple INotificationService that writes notifications to the console (simulated notifications).

Business rules (implemented)
----------------------------
- Allocation capacity: allocations are evaluated day-by-day over inclusive start/end ranges. At any day the sum of allocation percentages for active allocations must not exceed 100% for an employee. AllocationValidator implements IsCapacityAvailable and MaxAllocatedPercentage.
- Allocation dates: allocation StartDate must be on or after the project's StartDate and allocation EndDate (when provided) must not be after project EndDate. Allocation EndDate is required by the AllocationService for capacity checks.
- Project status: only Planned or Active projects accept allocations. Completed/Cancelled/OnHold do not permit allocations.
- Inactive employees cannot be allocated; AllocationService rejects allocations for employees with IsActive == false.
- Allocation percentages must be >0 and <=100.

Matching and report calculations (summary)
----------------------------------------
- Matching algorithm (MatchingService):
  - Normalize skill identifiers (trim + ToLowerInvariant).
  - For each required skill, require employee proficiency >= minimum required.
  - Exclude employees with insufficient available capacity (computed via AllocationValidator.MaxAllocatedPercentage) for the requested proposed allocation percentage.
  - Score = 70 * (matchedSkills / requiredSkills) + 30 * capacityFactor where capacityFactor = min(availableCapacity / proposedAllocation, 1).
  - Sort candidates by descending score, then matched skill count, then employee name for deterministic ordering.
- Reports (ReportService): bench (employees with allocated% below threshold), project staffing summary (active allocations per project and allocated percentage), department utilisation (point-in-time and period aggregated employee-day utilisation using employment-type capacity assumptions).

Sample console session (actual UI flows)
---------------------------------------
This sample is taken from a real run of the application. Prompts and output are verbatim.

Welcome to TechNova RMS Console
Main Menu:
1) Employees
2) Projects
3) Allocations
4) Matching
5) Reports
0) Exit
Select: 1
Employee Menu: 1) List 2) Add 0) Back
Select: 1
e1a1b2c3-0001-4d11-9000-000000000101 | Divyanshi Pandey | divyanshipandey@example.com | IT | Active: True
... (more employees printed)

Select: 1
Employee Menu: 1) List 2) Add 0) Back
Select: 2
Full name: Test User
Email: test.user@example.com
Designation: Developer
Department: IT
Cost rate (numeric): 75000
Added: 4f6c9b3e-... (GUID)

Select: 2
Project Menu: 1) List 2) Add 0) Back
Select: 2
Name: Acme Integration
Client: Acme Corp
Start date (yyyy-MM-dd): 2026-10-01
End date (yyyy-MM-dd) or blank: 2027-01-31
Enter required skills as name:level comma-separated (e.g. C#:4,SQL:3). At least one required skill is needed.
Skills: C#:4,SQL:3
Created project 7d9f... (GUID)

Select: 3
Allocation Menu: 1) List 2) Create 3) Release 0) Back
Select: 2
Employee UniqueId: <employee-guid>
Project Id: <project-guid>
Start date (yyyy-MM-dd): 2026-10-10
End date (yyyy-MM-dd) or blank:
Allocation percent (1-100): 50
Allocated 3a7b... (GUID)

Select: 4
Project Id: <project-guid>
Start date (yyyy-MM-dd): 2026-10-10
End date (yyyy-MM-dd): 2026-11-10
Proposed percent (default 50):
Alice Jones (alice.jones@acmecorp.com) - Score: 85 - Available: 50% - Matched: C#

Exit the app with 0 at main menu.

Example JSON structure (employees.json excerpt)
------------------------------------------------
[
  {
	"id": "e1a1b2c3-0001-4d11-9000-000000000101",
	"fullName": "Divyanshi Pandey",
	"email": "divyanshipandey@example.com",
	"designation": "Unknown",
	"department": "IT",
	"employmentType": "FullTime",
	"costRate": 85000.0,
	"isActive": true,
	"skills": [ { "name": "C#", "proficiency": 4 }, { "name": "SQL", "proficiency": 3 } ]
  }
]

Known limitations and future improvements
----------------------------------------
- The console UI is basic: it provides list/add/create/release flows but not full CRUD for all entities from the UI (services support updates and deactivation but the console UI exposes only a subset).
- No authentication or multi-user access control is provided.
- JSON file persistence is not suitable for high-concurrency production workloads; migrating to a proper database (EF Core) is recommended for production.
- Notifications are simulated via the console; real integrations (SMTP, Teams, Slack) can replace ConsoleNotificationService.

If you need, I can:
- Add integration tests that exercise the Json*Repository classes using temporary directories.
- Expand the ConsoleApp menus for full update/delete operations.
- Provide an ASP.NET Core MVC front-end and EF Core repository implementation as a migration path.

License and disclaimers
-----------------------
This sample project contains fictional data and is intended for learning and demonstration purposes only.

