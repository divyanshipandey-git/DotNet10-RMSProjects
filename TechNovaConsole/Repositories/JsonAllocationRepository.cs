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
/// JSON-backed allocation repository. Persists allocations to allocations.json.
/// Uses same safe-write and concurrency patterns used by other repositories.
/// </summary>
public sealed class JsonAllocationRepository : IAllocationRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly List<Allocation> _allocations = new();
    private readonly SemaphoreSlim _mutex = new(1,1);

    public JsonAllocationRepository(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "allocations.json");

        _options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        _options.Converters.Add(new DateOnlyJsonConverter());

        var t = LoadInternalAsync(CancellationToken.None);
        t.GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<Allocation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _allocations.AsReadOnly(); }
        finally { _mutex.Release(); }
    }

    public async Task<Allocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _allocations.FirstOrDefault(a => a.Id == id); }
        finally { _mutex.Release(); }
    }

    public async Task<IReadOnlyList<Allocation>> GetByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _allocations.Where(a => a.EmployeeId == employeeId).ToList().AsReadOnly(); }
        finally { _mutex.Release(); }
    }

    public async Task<IReadOnlyList<Allocation>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _allocations.Where(a => a.ProjectId == projectId).ToList().AsReadOnly(); }
        finally { _mutex.Release(); }
    }

    public async Task AddOrUpdateAsync(Allocation allocation, CancellationToken cancellationToken = default)
    {
        if (allocation is null) throw new ArgumentNullException(nameof(allocation));
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int idx = _allocations.FindIndex(a => a.Id == allocation.Id);
            if (idx >= 0) _allocations[idx] = allocation; else _allocations.Add(allocation);
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _allocations.RemoveAll(a => a.Id == id); await SaveInternalAsync(cancellationToken).ConfigureAwait(false); }
        finally { _mutex.Release(); }
    }

    private async Task LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) { await SafeWriteAsync(_filePath, "[]", cancellationToken).ConfigureAwait(false); return; }
        string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var list = JsonSerializer.Deserialize<List<Allocation>>(json, _options);
            if (list is null) return;
            _allocations.Clear(); _allocations.AddRange(list);
        }
        catch (Exception ex)
        {
            try { File.Copy(_filePath, _filePath + ".corrupt.bak", overwrite: true); } catch { }
            throw new InvalidOperationException($"Failed to deserialize allocations from '{_filePath}'.", ex);
        }
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(_filePath)!, Path.GetRandomFileName());
        string json = JsonSerializer.Serialize(_allocations, _options);
        await SafeWriteAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        try { if (File.Exists(_filePath)) File.Replace(tempPath, _filePath, null); else File.Move(tempPath, _filePath); }
        catch { if (File.Exists(tempPath)) { File.Copy(tempPath, _filePath, true); File.Delete(tempPath); } else throw; }
    }

    private static async Task SafeWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString() ?? throw new JsonException("Invalid date");
            return DateOnly.Parse(s);
        }

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
        }
    }
}
