using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public class DatabaseService : IDisposable
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
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();
            
            // Create table if not exists (for fallback/standalone run)
            var sql = @"
                CREATE TABLE IF NOT EXISTS tracked_banners (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    cctv_id TEXT NOT NULL,
                    x1 INTEGER,
                    y1 INTEGER,
                    x2 INTEGER,
                    y2 INTEGER,
                    first_seen TIMESTAMP NOT NULL,
                    last_seen TIMESTAMP NOT NULL,
                    banner_type TEXT,
                    text TEXT,
                    environment TEXT,
                    violation TEXT,
                    verdict TEXT,
                    reason TEXT,
                    status TEXT DEFAULT '감시중'
                );";
            
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Init failed: {ex.Message}");
        }
    }

    public List<ViolationRecord> GetViolationHistory(string? cctvId = null)
    {
        var list = new List<ViolationRecord>();
        try
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
            }

            string sql = "SELECT * FROM tracked_banners";
            if (!string.IsNullOrEmpty(cctvId))
            {
                sql += " WHERE cctv_id = @cctv_id";
            }
            sql += " ORDER BY last_seen DESC LIMIT 50";

            using var cmd = new SqliteCommand(sql, _connection);
            if (!string.IsNullOrEmpty(cctvId))
            {
                cmd.Parameters.AddWithValue("@cctv_id", cctvId);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = Convert.ToInt32(reader["id"]);
                var cId = reader["cctv_id"]?.ToString() ?? string.Empty;
                var bannerType = reader["banner_type"]?.ToString() ?? string.Empty;
                var text = reader["text"]?.ToString() ?? string.Empty;
                var violation = reader["violation"]?.ToString() ?? string.Empty;
                var verdict = reader["verdict"]?.ToString() ?? string.Empty;
                var reason = reader["reason"]?.ToString() ?? string.Empty;
                var status = reader["status"]?.ToString() ?? string.Empty;

                DateTime lastSeen = DateTime.Now;
                if (DateTime.TryParse(reader["last_seen"]?.ToString(), out var dt))
                {
                    lastSeen = dt;
                }

                // Map to C# UI model
                var record = new ViolationRecord
                {
                    Id = id,
                    AlertId = id.ToString(),
                    CctvId = cId,
                    CctvName = cId == "CCTV_01" ? "송도 1채널" :
                               cId == "CCTV_02" ? "구월 2채널" :
                               cId == "CCTV_03" ? "부평 3채널" :
                               cId == "CCTV_04" ? "주안 4채널" : $"{cId} 채널",
                    Gu = cId == "CCTV_01" ? "연수구" :
                         cId == "CCTV_02" ? "남동구" :
                         cId == "CCTV_03" ? "부평구" :
                         cId == "CCTV_04" ? "미추홀구" : "미추홀구",
                    Dong = cId == "CCTV_01" ? "송도동" :
                           cId == "CCTV_02" ? "구월동" :
                           cId == "CCTV_03" ? "부평동" :
                           cId == "CCTV_04" ? "주안동" : "용현동",
                    LocationAddress = cId == "CCTV_01" ? "인천광역시 연수구 송도동 12" :
                                     cId == "CCTV_02" ? "인천광역시 남동구 구월동 45" :
                                     cId == "CCTV_03" ? "인천광역시 부평구 부평동 78" :
                                     cId == "CCTV_04" ? "인천광역시 미추홀구 주안동 10" : "인천광역시 미추홀구 용현동",
                    ViolationDate = lastSeen,
                    OcrText = text,
                    AiVerdict = (verdict == "불법" || verdict == "ILLEGAL") ? "ILLEGAL" : "LEGAL",
                    AiReason = reason,
                    Confidence = 0.95, // Default confidence mapping
                    Status = status == "감시중" ? "단속대기" : status
                };
                list.Add(record);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetHistory failed: {ex.Message}");
        }

        return list;
    }

    public void UpdateBannerStatus(int bannerId, string status)
    {
        try
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
            }

            var sql = "UPDATE tracked_banners SET status = @status WHERE id = @id";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@id", bannerId);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] UpdateStatus failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
