using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public partial class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public DatabaseService(string? dbPath = null)
    {
        dbPath ??= SqliteDbPath.ResolveBannerTrackingDb();
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        try
        {
            _connection?.Dispose();
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
            DatabaseMigrator.Apply(_connection);
        }
        catch (Exception ex)
        {
            BackendManager.Log($"[DatabaseService] Init failed: {ex.Message}");
        }
    }

    private const int SimilarImageThresholdPct = 35;
    private const int SimilarTextThresholdPct = 25;

    private void EnsureConnection()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
        }
    }

    /// <summary>검증 화면 긴급 판정 → 단속 현황 등록 (alert_id 기준 1회)</summary>
    public void Dispose()
    {
        _connection?.Dispose();
    }
}

public class ProcessedReviewRow
{
    public string AlertId { get; set; } = string.Empty;
    public string CctvId { get; set; } = string.Empty;
    public string AiStatusCode { get; set; } = string.Empty;
    public string HumanVerdict { get; set; } = string.Empty;
    public string HumanStatusCode { get; set; } = string.Empty;
    public string OcrText { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
}
