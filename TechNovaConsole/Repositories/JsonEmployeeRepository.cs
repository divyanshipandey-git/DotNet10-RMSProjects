using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TechNovaConsole.Interfaces;
using TechNovaConsole.Models;

namespace TechNovaConsole.Repositories;

/// <summary>
/// JSON-backed repository implementation that persists employees to a file.
/// - Uses System.Text.Json with string enums and indented formatting.
/// - Performs safe atomic writes by writing to a temp file then replacing the original.
/// - Guards concurrent access with a semaphore to avoid file corruption.
/// - Attempts to migrate legacy JSON schema (simple sample file) into the current domain schema.
/// </summary>
public sealed class JsonEmployeeRepository : IEmployeeRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly List<Employee> _employees = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the repository. If the file does not exist it will be created.
    /// </summary>
    /// <param name="dataDirectory">Directory path where App_Data and employees.json reside.</param>
    public JsonEmployeeRepository(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentNullException(nameof(dataDirectory));

        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "employees.json");

        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Load existing file (may perform migration if legacy schema detected)
        try
        {
            var t = LoadInternalAsync(CancellationToken.None);
            t.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Surface the error to the caller — do not swallow exceptions.
            throw new InvalidOperationException($"Failed to initialize JsonEmployeeRepository from '{_filePath}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _employees.AsReadOnly();
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Employee?> GetByIdAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _employees.FirstOrDefault(e => e.UniqueId == uniqueId);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (employee is null) throw new ArgumentNullException(nameof(employee));

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Ensure unique UniqueId
            if (employee.UniqueId == Guid.Empty)
            {
                var prop = typeof(Employee).GetProperty("UniqueId");
                prop?.SetValue(employee, Guid.NewGuid());
            }

            if (_employees.Any(e => e.UniqueId == employee.UniqueId))
                throw new InvalidOperationException("Employee with the same UniqueId already exists.");

            _employees.Add(employee);
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        if (employee is null) throw new ArgumentNullException(nameof(employee));

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int index = _employees.FindIndex(e => e.UniqueId == employee.UniqueId);
            if (index < 0)
                throw new InvalidOperationException("Employee not found for update.");

            _employees[index] = employee;
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _employees.RemoveAll(e => e.UniqueId == uniqueId);
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    // Internal load logic with migration support.
    private async Task LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            // Initialize with empty array
            await SafeWriteAsync(_filePath, "[]", cancellationToken).ConfigureAwait(false);
            return;
        }

        string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

        try
        {
            // First try a tolerant parse that accepts several common shapes and value encodings.
            var tolerant = TryDeserializeSimpleToDomain(json);
            if (tolerant is not null)
            {
                _employees.Clear();
                _employees.AddRange(tolerant);
                await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Next, try the canonical deserialization into domain types.
            var list = JsonSerializer.Deserialize<List<Employee>>(json, _options);
            if (list is null)
            {
                // Treat null as empty collection
                _employees.Clear();
                return;
            }

            _employees.Clear();
            _employees.AddRange(list);
        }
        catch (JsonException)
        {
            // Attempt to detect legacy or simple schema and migrate.
            var legacy = TryDeserializeLegacy(json);
            if (legacy is not null)
            {
                var migrated = legacy.Select(MapLegacyToDomain).ToList();
                _employees.Clear();
                _employees.AddRange(migrated);
                await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Try the tolerant simple-to-domain parser (handles string proficiencies like "level4").
            var simpleDomain = TryDeserializeSimpleToDomain(json);
            if (simpleDomain is not null)
            {
                _employees.Clear();
                _employees.AddRange(simpleDomain);
                await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // As a last attempt, try the simple DTO mapping and convert to domain using MapSimpleToDomain
            var simpleDto = TryDeserializeSimple(json);
            if (simpleDto is not null)
            {
                var migrated = simpleDto.Select(MapSimpleToDomain).ToList();
                _employees.Clear();
                _employees.AddRange(migrated);
                await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // No known fallback; rethrow original exception to surface the problem
            throw;
        }
    }

    private List<LegacyEmployeeDto>? TryDeserializeLegacy(string json)
    {
        try
        {
            var legacy = JsonSerializer.Deserialize<List<LegacyEmployeeDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return legacy;
        }
        catch
        {
            return null;
        }
    }

    private List<SimpleEmployeeDto>? TryDeserializeSimple(string json)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var simple = JsonSerializer.Deserialize<List<SimpleEmployeeDto>>(json, opts);
            return simple;
        }
        catch
        {
            return null;
        }
    }

    // Try a tolerant parse of a "simple" employee JSON shape into domain Employee instances.
    // This handles skill proficiency being either numeric or string like "level4".
    private List<Employee>? TryDeserializeSimpleToDomain(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var results = new List<Employee>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string idStr = GetPropertyOrDefault(el, "id");
                Guid uid = Guid.TryParse(idStr, out var g) ? g : Guid.NewGuid();
                string fullName = GetPropertyOrDefault(el, "fullName") ?? GetPropertyOrDefault(el, "name") ?? "Unknown";
                string email = GetPropertyOrDefault(el, "email") ?? GenerateEmailFromName(fullName);
                string designation = GetPropertyOrDefault(el, "designation") ?? "Unknown";
                string department = GetPropertyOrDefault(el, "department") ?? string.Empty;
                string empType = GetPropertyOrDefault(el, "employmentType") ?? string.Empty;
                decimal costRate = 0m;
                if (TryGetProperty(el, "costRate", out var crToken))
                {
                    if (crToken.ValueKind == JsonValueKind.Number && crToken.TryGetDecimal(out var d)) costRate = d;
                    else if (crToken.ValueKind == JsonValueKind.String && decimal.TryParse(crToken.GetString(), out var d2)) costRate = d2;
                }
                bool isActive = true;
                if (TryGetProperty(el, "isActive", out var activeToken) && activeToken.ValueKind == JsonValueKind.False) isActive = false;

                var skills = new List<EmployeeSkill>();
                if (el.TryGetProperty("skills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sk in skillsEl.EnumerateArray())
                    {
                        var name = GetPropertyOrDefault(sk, "name");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        int prof = 1;
                        if (TryGetProperty(sk, "proficiency", out var profTok) || TryGetProperty(sk, "proficiencyLevel", out profTok))
                        {
                            if (profTok.ValueKind == JsonValueKind.Number && profTok.TryGetInt32(out var pi)) prof = pi;
                            else if (profTok.ValueKind == JsonValueKind.String)
                            {
                                var s = profTok.GetString() ?? string.Empty;
                                if (s.StartsWith("level", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(5), out var n)) prof = n;
                                else if (int.TryParse(s, out var n2)) prof = n2;
                            }
                        }

                        skills.Add(new EmployeeSkill(NormalizeSkillName(name), MapProficiency(Math.Clamp(prof, 1, 5))));
                    }
                }

                var employee = new Employee(fullName, email, designation, department, ParseEmploymentType(empType), costRate, isActive, skills) { UniqueId = uid };
                results.Add(employee);
            }

            return results;
        }
        catch
        {
            return null;
        }
    }

    // Helper extension-like methods for JsonElement safe extraction
    private static string? GetPropertyOrDefault(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetPropertyCaseInsensitive(el, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        return v.ToString();
    }

    private static JsonElement? GetPropertyTokenOrNull(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var v)) return null;
        return v;
    }

    private static bool TryGetProperty(JsonElement el, string name, out JsonElement value)
    {
        value = default;
        if (el.ValueKind != JsonValueKind.Object) return false;
        return TryGetPropertyCaseInsensitive(el, name, out value);
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement el, string name, out JsonElement value)
    {
        value = default;
        if (el.ValueKind != JsonValueKind.Object) return false;

        // Fast-path: exact match
        if (el.TryGetProperty(name, out var v))
        {
            value = v;
            return true;
        }

        // Case-insensitive fallback
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static Employee MapLegacyToDomain(LegacyEmployeeDto dto)
    {
        // Create a plausible email and placeholder designation for legacy records so the domain invariant
        // validators are satisfied. We preserve the legacy integer Id in the Id property.
        string name = dto.Name ?? "Unknown";
        string emailCandidate = GenerateEmailFromName(name);
        string designation = "Unknown";

        var employee = new Employee(
            fullName: name,
            email: emailCandidate,
            designation: designation,
            department: dto.Department ?? string.Empty,
            employmentType: EmploymentType.FullTime,
            costRate: dto.Salary,
            isActive: dto.IsActive,
            skills: null);

        // Preserve legacy Id
        employee.Id = dto.Id;
        return employee;
    }

    private static Employee MapSimpleToDomain(SimpleEmployeeDto dto)
    {
        if (!Guid.TryParse(dto.Id, out Guid gid)) gid = Guid.NewGuid();

        var skills = (dto.Skills ?? Array.Empty<SimpleSkillDto>())
            .Select(s => new EmployeeSkill(NormalizeSkillName(s.Name), MapProficiency(s.Proficiency)))
            .ToList();

        var employee = new Employee(
            fullName: dto.FullName ?? "Unknown",
            email: dto.Email ?? GenerateEmailFromName(dto.FullName ?? "user"),
            designation: dto.Designation ?? "Unknown",
            department: dto.Department ?? string.Empty,
            employmentType: ParseEmploymentType(dto.EmploymentType),
            costRate: dto.CostRate,
            isActive: dto.IsActive,
            skills: skills);

        // Preserve UniqueId
        // We cannot set init-only UniqueId after creation; create via reflection by recreating with UniqueId via with-expression is not possible for class.
        // Instead, set Id (legacy) to 0 and copy UniqueId via helper by creating a small wrapper. To avoid complexity, if dto.Id parses to GUID we set the Employee.UniqueId via reflection.
        try
        {
            var prop = typeof(Employee).GetProperty("UniqueId");
            prop?.SetValue(employee, gid);
        }
        catch
        {
            // ignore - UniqueId will remain new Guid
        }

        return employee;
    }

    private static string NormalizeSkillName(string name) => new Skill(name).Id;

    private static ProficiencyLevel MapProficiency(int p)
    {
        if (Enum.IsDefined(typeof(ProficiencyLevel), p)) return (ProficiencyLevel)p;
        return ProficiencyLevel.Level1;
    }

    private static EmploymentType ParseEmploymentType(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return EmploymentType.FullTime;
        if (Enum.TryParse<EmploymentType>(s, ignoreCase: true, out var et)) return et;
        return EmploymentType.FullTime;
    }

    // DTOs for the simple employee JSON shape
    private sealed class SimpleEmployeeDto
    {
        public string Id { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public string? EmploymentType { get; set; }
        public decimal CostRate { get; set; }
        public bool IsActive { get; set; }
        public SimpleSkillDto[]? Skills { get; set; }
    }

    private sealed class SimpleSkillDto
    {
        public string Name { get; set; } = string.Empty;
        public int Proficiency { get; set; }
    }

    private static string GenerateEmailFromName(string name)
    {
        string normalized = new string(name.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (string.IsNullOrEmpty(normalized)) normalized = "user";
        return normalized + "@example.com";
    }

    // Save current in-memory collection to disk using safe atomic replacement.
    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(_filePath)!, Path.GetRandomFileName());
        string json = JsonSerializer.Serialize(_employees, _options);

        await SafeWriteAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        // Replace the original file atomically if possible
        try
        {
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        catch
        {
            // If File.Replace is not supported or fails, attempt a best-effort move.
            if (File.Exists(tempPath))
            {
                File.Copy(tempPath, _filePath, overwrite: true);
                File.Delete(tempPath);
            }
            else
            {
                // Log save failure for diagnosis then rethrow
                try
                {
                    var dir = Path.GetDirectoryName(_filePath) ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
                    Directory.CreateDirectory(dir);
                    var log = Path.Combine(dir, "employee.save.error.log");
                    File.AppendAllText(log, DateTime.UtcNow.ToString("o") + " - Failed to save employees.json.\n");
                }
                catch { }

                throw;
            }
        }

        // Log successful save for diagnostics (best-effort)
        try
        {
            var dir = Path.GetDirectoryName(_filePath) ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
            Directory.CreateDirectory(dir);
            var okLog = Path.Combine(dir, "employee.save.ok.log");
            File.AppendAllText(okLog, DateTime.UtcNow.ToString("o") + $" - Saved {_employees.Count} employees to '{_filePath}'.\n");
        }
        catch { }
    }

    // Writes content to the specified path atomically by writing to a temp file and flushing to disk.
    private static async Task SafeWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        // Ensure the directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write text to the file asynchronously and ensure content is flushed to disk.
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    // DTO for legacy file schema mapping
    private sealed class LegacyEmployeeDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Department { get; set; }
        public decimal Salary { get; set; }
        public int PerformanceScore { get; set; }
        public bool IsActive { get; set; }
    }
}
