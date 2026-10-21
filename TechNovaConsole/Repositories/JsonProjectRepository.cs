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
/// JSON-backed project repository. Persists projects to projects.json in the provided data directory.
/// Uses the same persistence conventions as JsonEmployeeRepository: safe writes, semaphore for concurrency.
/// </summary>
public sealed class JsonProjectRepository : IProjectRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly List<Project> _projects = new();
    private readonly SemaphoreSlim _mutex = new(1,1);

    public JsonProjectRepository(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "projects.json");

        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        _options.Converters.Add(new DateOnlyJsonConverter());

        var t = LoadInternalAsync(CancellationToken.None);
        t.GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _projects.AsReadOnly(); }
        finally { _mutex.Release(); }
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _projects.FirstOrDefault(p => p.Id == id); }
        finally { _mutex.Release(); }
    }

    public async Task AddOrUpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int idx = _projects.FindIndex(p => p.Id == project.Id);
            if (idx >= 0) _projects[idx] = project;
            else _projects.Add(project);

            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _projects.RemoveAll(p => p.Id == id);
            await SaveInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _mutex.Release(); }
    }

    private async Task LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            await SafeWriteAsync(_filePath, "[]", cancellationToken).ConfigureAwait(false);
            return;
        }

        string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var list = JsonSerializer.Deserialize<List<Project>>(json, _options);
            if (list is null) return;
            _projects.Clear();
            _projects.AddRange(list);
        }
        catch (Exception ex)
        {
            // Preserve the original file and fail initialization rather than silently discarding data.
            try
            {
                var bak = _filePath + ".corrupt.bak";
                File.Copy(_filePath, bak, overwrite: true);
            }
            catch { /* best-effort backup */ }

            throw new InvalidOperationException($"Failed to deserialize projects from '{_filePath}'.", ex);
        }
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(_filePath)!, Path.GetRandomFileName());
        string json = JsonSerializer.Serialize(_projects, _options);
        await SafeWriteAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(_filePath)) File.Replace(tempPath, _filePath, null);
            else File.Move(tempPath, _filePath);
        }
        catch
        {
            if (File.Exists(tempPath)) { File.Copy(tempPath, _filePath, true); File.Delete(tempPath); }
            else throw;
        }
    }

    private static async Task SafeWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    // Simple DateOnly converter to keep JSON readable (yyyy-MM-dd)
    private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                // Called for nullable DateOnly when the JSON value is null. Return default value; caller
                // will map default(DateOnly) into a nullable target as null when appropriate.
                return default;
            }

            var s = reader.GetString() ?? throw new JsonException("Invalid date");
            return DateOnly.Parse(s);
        }

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
        }
    }
}
