using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Repositories;

// Program.cs is the composition root only: register services and build the host.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Register repositories and services.
// Use the project's App_Data folder (source) instead of the runtime output App_Data.
// AppContext.BaseDirectory points to bin/Debug/net10.0; navigate up to the project folder.
string dataDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "App_Data"));
builder.Services.AddSingleton<IEmployeeRepository>(sp => new JsonEmployeeRepository(dataDirectory));
builder.Services.AddSingleton<IProjectRepository>(sp => new JsonProjectRepository(dataDirectory));

// Application services
builder.Services.AddSingleton<TechNovaConsole.Services.EmployeeService>();
builder.Services.AddSingleton<TechNovaConsole.Services.ProjectService>();
builder.Services.AddSingleton<IAllocationRepository>(sp => new JsonAllocationRepository(dataDirectory));
builder.Services.AddSingleton<TechNovaConsole.Services.AllocationService>();
builder.Services.AddSingleton<TechNovaConsole.Interfaces.INotificationService, TechNovaConsole.Notifications.ConsoleNotificationService>();
builder.Services.AddSingleton<TechNovaConsole.Services.MatchingService>();
builder.Services.AddSingleton<TechNovaConsole.Services.ReportService>();
builder.Services.AddTransient<TechNovaConsole.ConsoleApp>();
builder.Services.AddSingleton<TechNovaConsole.Seed.DeterministicSeeder>();

// Composition root only: no business logic here. Resolve services in other classes.
Console.WriteLine("TechNovaConsole composition root configured.");

using IHost host = builder.Build();

// Run the console application presentation layer. Cancellation is supported via Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// If the --seed flag is present, run deterministic seeding and exit.
if (args is not null && args.Length > 0 && args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    var seeder = host.Services.GetRequiredService<TechNovaConsole.Seed.DeterministicSeeder>();
    await seeder.SeedAsync(cts.Token);
    return;
}

var app = host.Services.GetRequiredService<TechNovaConsole.ConsoleApp>();
await app.RunAsync(cts.Token);
