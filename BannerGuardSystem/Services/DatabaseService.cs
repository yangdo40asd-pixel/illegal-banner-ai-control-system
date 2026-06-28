using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            var reviewSql = @"
                CREATE TABLE IF NOT EXISTS human_reviews (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    alert_id TEXT NOT NULL,
                    cctv_id TEXT,
                    source_image TEXT,
                    ai_status_code TEXT,
                    human_verdict TEXT NOT NULL,
                    human_status_code TEXT NOT NULL,
                    reviewer TEXT,
                    note TEXT,
                    ocr_text TEXT,
                    reviewed_at TIMESTAMP NOT NULL
                );";
            using var reviewCmd = new SqliteCommand(reviewSql, _connection);
            reviewCmd.ExecuteNonQuery();

            var enforcementSql = @"
                CREATE TABLE IF NOT EXISTS enforcement_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    enforcement_no TEXT NOT NULL UNIQUE,
                    alert_id TEXT NOT NULL UNIQUE,
                    cctv_id TEXT,
                    source_image TEXT,
                    detected_at TIMESTAMP NOT NULL,
                    location TEXT,
                    ocr_text TEXT,
                    ai_status_code TEXT,
                    violation_type TEXT DEFAULT 'IllegalBanner',
                    violator_name TEXT DEFAULT '미확인',
                    business_name TEXT DEFAULT '',
                    fine_manwon INTEGER DEFAULT 300,
                    processing_status TEXT NOT NULL DEFAULT 'Urgent',
                    is_urgent INTEGER NOT NULL DEFAULT 1,
                    is_repeat INTEGER DEFAULT 0,
                    repeat_count INTEGER DEFAULT 0,
                    assignee TEXT DEFAULT '배정대기',
                    human_verdict TEXT,
                    human_status_code TEXT,
                    reviewed_at TIMESTAMP,
                    created_at TIMESTAMP NOT NULL,
                    updated_at TIMESTAMP NOT NULL
                );";
            using var enfCmd = new SqliteCommand(enforcementSql, _connection);
            enfCmd.ExecuteNonQuery();

            var similarSql = @"
                CREATE TABLE IF NOT EXISTS similar_enforcement_reviews (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_enforcement_no TEXT NOT NULL,
                    matched_enforcement_no TEXT NOT NULL,
                    matched_location TEXT,
                    matched_ocr_summary TEXT,
                    matched_detected_at TIMESTAMP,
                    image_similarity_pct INTEGER NOT NULL DEFAULT 0,
                    text_similarity_pct INTEGER NOT NULL DEFAULT 0,
                    created_at TIMESTAMP NOT NULL,
                    UNIQUE(source_enforcement_no, matched_enforcement_no)
                );";
            using var simCmd = new SqliteCommand(similarSql, _connection);
            simCmd.ExecuteNonQuery();

            EnsureColumn("enforcement_records", "crop_image_b64", "TEXT");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Init failed: {ex.Message}");
        }
    }

    private void EnsureColumn(string table, string column, string type)
    {
        try
        {
            using var check = new SqliteCommand($"PRAGMA table_info({table})", _connection);
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            using var alter = new SqliteCommand($"ALTER TABLE {table} ADD COLUMN {column} {type}", _connection);
            alter.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] EnsureColumn {table}.{column} failed: {ex.Message}");
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
    public bool TryInsertUrgentEnforcement(BannerDetectionEventDto alert)
    {
        if (!alert.IsVlmTriggered || !alert.IsEmergency)
            return false;

        try
        {
            EnsureConnection();
            using var exists = new SqliteCommand(
                "SELECT 1 FROM enforcement_records WHERE alert_id = @alert_id LIMIT 1", _connection);
            exists.Parameters.AddWithValue("@alert_id", alert.Id);
            if (exists.ExecuteScalar() != null)
                return false;

            var now = DateTime.Now;
            var detectedAt = alert.Timestamp == default ? now : alert.Timestamp;
            var idPart = new string((alert.Id ?? Guid.NewGuid().ToString()).Where(char.IsLetterOrDigit).Take(8).ToArray());
            if (string.IsNullOrEmpty(idPart))
                idPart = Guid.NewGuid().ToString("N")[..8];
            var enforcementNo = $"E-{detectedAt:yyyyMMdd}-{idPart}";
            var location = EnforcementDataMapper.ResolveLocation(alert.CctvId, alert.SourceImage, alert.LocationAddress);
            var fine = EnforcementDataMapper.DefaultFineManwon(alert.StatusCode);

            var cropImage = !string.IsNullOrWhiteSpace(alert.CroppedImageBase64)
                ? alert.CroppedImageBase64
                : alert.FullImageBase64 ?? "";

            var sql = @"
                INSERT INTO enforcement_records
                (enforcement_no, alert_id, cctv_id, source_image, detected_at, location, ocr_text,
                 ai_status_code, fine_manwon, processing_status, is_urgent, is_repeat, repeat_count,
                 assignee, crop_image_b64, created_at, updated_at)
                VALUES
                (@enforcement_no, @alert_id, @cctv_id, @source_image, @detected_at, @location, @ocr_text,
                 @ai_status_code, @fine_manwon, 'Urgent', 1, @is_repeat, @repeat_count,
                 '배정대기', @crop_image_b64, @created_at, @updated_at)";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@enforcement_no", enforcementNo);
            cmd.Parameters.AddWithValue("@alert_id", alert.Id);
            cmd.Parameters.AddWithValue("@cctv_id", alert.CctvId);
            cmd.Parameters.AddWithValue("@source_image", alert.SourceImage ?? "");
            cmd.Parameters.AddWithValue("@detected_at", detectedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@location", location);
            cmd.Parameters.AddWithValue("@ocr_text", alert.OcrText ?? "");
            cmd.Parameters.AddWithValue("@ai_status_code", alert.StatusCode);
            cmd.Parameters.AddWithValue("@fine_manwon", fine);
            cmd.Parameters.AddWithValue("@is_repeat", alert.IsRepeatOffender ? 1 : 0);
            cmd.Parameters.AddWithValue("@repeat_count", alert.RepeatCount);
            cmd.Parameters.AddWithValue("@crop_image_b64", cropImage);
            cmd.Parameters.AddWithValue("@created_at", now.ToString("o"));
            cmd.Parameters.AddWithValue("@updated_at", now.ToString("o"));
            cmd.ExecuteNonQuery();

            if (ShouldAutoBuildSimilarReviews(alert.StatusCode))
                BuildSimilarReviewsForEnforcement(enforcementNo);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] TryInsertUrgentEnforcement failed: {ex.Message}");
            return false;
        }
    }

    public void UpdateEnforcementHumanVerdict(string alertId, string humanVerdict, string humanStatusCode, string reviewer)
    {
        if (string.IsNullOrWhiteSpace(alertId))
            return;

        try
        {
            EnsureConnection();
            var processing = humanVerdict switch
            {
                "ILLEGAL" => "Received",
                "LEGAL" => "Received",
                _ => "Urgent"
            };
            var isUrgent = humanVerdict == "REVIEW" ? 1 : 0;

            var sql = @"
                UPDATE enforcement_records
                SET processing_status = @processing_status,
                    is_urgent = @is_urgent,
                    human_verdict = @human_verdict,
                    human_status_code = @human_status_code,
                    assignee = @assignee,
                    reviewed_at = @reviewed_at,
                    updated_at = @updated_at
                WHERE alert_id = @alert_id";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@processing_status", processing);
            cmd.Parameters.AddWithValue("@is_urgent", isUrgent);
            cmd.Parameters.AddWithValue("@human_verdict", humanVerdict);
            cmd.Parameters.AddWithValue("@human_status_code", humanStatusCode);
            cmd.Parameters.AddWithValue("@assignee", reviewer);
            cmd.Parameters.AddWithValue("@reviewed_at", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@alert_id", alertId);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] UpdateEnforcementHumanVerdict failed: {ex.Message}");
        }
    }

    public List<EnforcementRecord> GetEnforcementRecords()
    {
        var list = new List<EnforcementRecord>();
        try
        {
            EnsureConnection();
            const string sql = @"
                SELECT * FROM enforcement_records
                WHERE human_verdict IS NULL OR human_verdict IN ('ILLEGAL', 'REVIEW')
                ORDER BY detected_at DESC, id DESC";
            using var cmd = new SqliteCommand(sql, _connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapEnforcementRecord(reader));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetEnforcementRecords failed: {ex.Message}");
        }
        return list;
    }

    public EnforcementKpiSummary GetEnforcementKpi()
    {
        var kpi = new EnforcementKpiSummary();
        try
        {
            EnsureConnection();
            var today = DateTime.Today;
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var yesterday = today.AddDays(-1);

            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM enforcement_records WHERE date(detected_at) = date(@today)", _connection))
            {
                cmd.Parameters.AddWithValue("@today", today.ToString("o"));
                kpi.TodayCount = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM enforcement_records WHERE date(detected_at) = date(@yesterday)", _connection))
            {
                cmd.Parameters.AddWithValue("@yesterday", yesterday.ToString("o"));
                kpi.YesterdayCount = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM enforcement_records WHERE detected_at >= @week_start", _connection))
            {
                cmd.Parameters.AddWithValue("@week_start", weekStart.ToString("o"));
                kpi.WeekCount = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COALESCE(SUM(fine_manwon),0) FROM enforcement_records WHERE human_verdict = 'ILLEGAL'", _connection))
            {
                kpi.TotalFineManwon = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM enforcement_records WHERE processing_status = 'Appeal'", _connection))
            {
                kpi.AppealCount = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COALESCE(SUM(fine_manwon),0) FROM enforcement_records WHERE processing_status = 'Delinquent'", _connection))
            {
                kpi.UnpaidFineManwon = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM enforcement_records WHERE processing_status = 'FineImposed' AND human_verdict = 'ILLEGAL'", _connection))
            {
                var paid = Convert.ToInt32(cmd.ExecuteScalar());
                var totalIllegal = 0;
                using var c2 = new SqliteCommand(
                    "SELECT COUNT(*) FROM enforcement_records WHERE human_verdict = 'ILLEGAL'", _connection);
                totalIllegal = Convert.ToInt32(c2.ExecuteScalar());
                kpi.CollectionRatePct = totalIllegal > 0 ? Math.Round(paid * 100.0 / totalIllegal, 1) : 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetEnforcementKpi failed: {ex.Message}");
        }
        return kpi;
    }

    private static bool ShouldAutoBuildSimilarReviews(string? aiStatusCode) =>
        aiStatusCode is "MANUAL_REVIEW" or "ILLEGAL_COMMERCIAL" or "ILLEGAL_CONTENT";

    public void BuildSimilarReviewsForEnforcement(string sourceEnforcementNo)
    {
        try
        {
            EnsureConnection();
            var source = GetEnforcementRecordByNo(sourceEnforcementNo);
            if (source == null)
                return;

            using var del = new SqliteCommand(
                "DELETE FROM similar_enforcement_reviews WHERE source_enforcement_no = @source", _connection);
            del.Parameters.AddWithValue("@source", sourceEnforcementNo);
            del.ExecuteNonQuery();

            var candidates = GetEnforcementRecords()
                .Where(r => r.EnforcementNo != sourceEnforcementNo)
                .Select(r => new
                {
                    Record = r,
                    ImageScore = ImageSimilarityHelper.ComparePercent(source.CropImageBase64, r.CropImageBase64),
                    TextScore = BannerSimilarityHelper.SimilarityPercent(source.OcrText, r.OcrText)
                })
                .Where(x => x.ImageScore >= SimilarImageThresholdPct || x.TextScore >= SimilarTextThresholdPct)
                .OrderByDescending(x => Math.Max(x.ImageScore, x.TextScore))
                .ThenByDescending(x => x.Record.DetectedAt)
                .Take(20)
                .ToList();

            foreach (var item in candidates)
            {
                var sql = @"
                    INSERT OR REPLACE INTO similar_enforcement_reviews
                    (source_enforcement_no, matched_enforcement_no, matched_location, matched_ocr_summary,
                     matched_detected_at, image_similarity_pct, text_similarity_pct, created_at)
                    VALUES
                    (@source, @matched, @location, @summary, @detected_at, @image_pct, @text_pct, @created_at)";
                using var cmd = new SqliteCommand(sql, _connection);
                cmd.Parameters.AddWithValue("@source", sourceEnforcementNo);
                cmd.Parameters.AddWithValue("@matched", item.Record.EnforcementNo);
                cmd.Parameters.AddWithValue("@location", item.Record.Location);
                cmd.Parameters.AddWithValue("@summary", item.Record.BannerSummaryOneLine);
                cmd.Parameters.AddWithValue("@detected_at", item.Record.DetectedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@image_pct", item.ImageScore);
                cmd.Parameters.AddWithValue("@text_pct", item.TextScore);
                cmd.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] BuildSimilarReviews failed: {ex.Message}");
        }
    }

    public List<SimilarEnforcementRecord> GetSimilarEnforcementReviews(string sourceEnforcementNo)
    {
        var list = new List<SimilarEnforcementRecord>();
        try
        {
            EnsureConnection();
            const string sql = @"
                SELECT matched_location, matched_ocr_summary, matched_detected_at,
                       image_similarity_pct, text_similarity_pct
                FROM similar_enforcement_reviews
                WHERE source_enforcement_no = @source
                ORDER BY image_similarity_pct DESC, text_similarity_pct DESC, id DESC";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@source", sourceEnforcementNo);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime detectedAt = DateTime.Now;
                DateTime.TryParse(reader["matched_detected_at"]?.ToString(), out detectedAt);
                list.Add(new SimilarEnforcementRecord
                {
                    DetectedDate = detectedAt,
                    Location = reader["matched_location"]?.ToString() ?? "",
                    BannerSummary = reader["matched_ocr_summary"]?.ToString() ?? "",
                    ImageSimilarityPercent = Convert.ToInt32(reader["image_similarity_pct"]),
                    TextSimilarityPercent = Convert.ToInt32(reader["text_similarity_pct"])
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetSimilarEnforcementReviews failed: {ex.Message}");
        }
        return list;
    }

    private EnforcementRecord? GetEnforcementRecordByNo(string enforcementNo)
    {
        try
        {
            EnsureConnection();
            using var cmd = new SqliteCommand(
                "SELECT * FROM enforcement_records WHERE enforcement_no = @no LIMIT 1", _connection);
            cmd.Parameters.AddWithValue("@no", enforcementNo);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapEnforcementRecord(reader) : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetEnforcementRecordByNo failed: {ex.Message}");
            return null;
        }
    }

    private static EnforcementRecord MapEnforcementRecord(SqliteDataReader reader)
    {
        DateTime detectedAt = DateTime.Now;
        DateTime.TryParse(reader["detected_at"]?.ToString(), out detectedAt);

        var ocrText = reader["ocr_text"]?.ToString() ?? "";
        var location = reader["location"]?.ToString() ?? "";
        var status = EnforcementDataMapper.ParseProcessingStatus(reader["processing_status"]?.ToString());
        var aiStatus = reader["ai_status_code"]?.ToString() ?? "";

        return new EnforcementRecord
        {
            EnforcementNo = reader["enforcement_no"]?.ToString() ?? "",
            AlertId = reader["alert_id"]?.ToString() ?? "",
            AiStatusCode = aiStatus,
            CropImageBase64 = TryGetString(reader, "crop_image_b64"),
            DetectedAt = detectedAt,
            Location = location,
            MapQuery = location,
            OcrText = ocrText,
            ViolationType = EnforcementViolationType.IllegalBanner,
            Status = status,
            PrimaryAction = "상세",
            SecondaryAction = status == EnforcementProcessingStatus.Urgent ? "접수" : "검토"
        };
    }

    private static string TryGetString(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }
        catch
        {
            return "";
        }
    }

    public void SaveHumanReview(
        string alertId,
        string cctvId,
        string sourceImage,
        string aiStatusCode,
        string humanVerdict,
        string humanStatusCode,
        string ocrText,
        string reviewer = "operator",
        string note = "")
    {
        try
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
            }

            var sql = @"
                INSERT INTO human_reviews
                (alert_id, cctv_id, source_image, ai_status_code, human_verdict,
                 human_status_code, reviewer, note, ocr_text, reviewed_at)
                VALUES (@alert_id, @cctv_id, @source_image, @ai_status_code, @human_verdict,
                        @human_status_code, @reviewer, @note, @ocr_text, @reviewed_at)";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@alert_id", alertId);
            cmd.Parameters.AddWithValue("@cctv_id", cctvId);
            cmd.Parameters.AddWithValue("@source_image", sourceImage);
            cmd.Parameters.AddWithValue("@ai_status_code", aiStatusCode);
            cmd.Parameters.AddWithValue("@human_verdict", humanVerdict);
            cmd.Parameters.AddWithValue("@human_status_code", humanStatusCode);
            cmd.Parameters.AddWithValue("@reviewer", reviewer);
            cmd.Parameters.AddWithValue("@note", note);
            cmd.Parameters.AddWithValue("@ocr_text", ocrText);
            cmd.Parameters.AddWithValue("@reviewed_at", DateTime.Now.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] SaveHumanReview failed: {ex.Message}");
        }
    }

    public void UpdateBannerStatusByCctvText(string cctvId, string ocrText, string status, string verdict)
    {
        if (string.IsNullOrWhiteSpace(cctvId) || string.IsNullOrWhiteSpace(ocrText))
            return;

        try
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
            }

            var sql = @"
                UPDATE tracked_banners
                SET status = @status, verdict = @verdict
                WHERE id = (
                    SELECT id FROM tracked_banners
                    WHERE cctv_id = @cctv_id AND text = @text
                    ORDER BY last_seen DESC LIMIT 1
                )";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@verdict", verdict);
            cmd.Parameters.AddWithValue("@cctv_id", cctvId);
            cmd.Parameters.AddWithValue("@text", ocrText);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] UpdateBannerStatusByCctvText failed: {ex.Message}");
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

    public List<ProcessedReviewRow> GetProcessedReviews(DateTime? from = null, DateTime? to = null)
    {
        var rows = new List<ProcessedReviewRow>();
        try
        {
            EnsureConnection();
            var sql = "SELECT * FROM human_reviews WHERE 1=1";
            if (from.HasValue) sql += " AND reviewed_at >= @from";
            if (to.HasValue) sql += " AND reviewed_at < @to";
            sql += " ORDER BY reviewed_at DESC";
            using var cmd = new SqliteCommand(sql, _connection);
            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString("o"));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString("o"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime reviewedAt = DateTime.Now;
                DateTime.TryParse(reader["reviewed_at"]?.ToString(), out reviewedAt);
                rows.Add(new ProcessedReviewRow
                {
                    AlertId = reader["alert_id"]?.ToString() ?? "",
                    CctvId = reader["cctv_id"]?.ToString() ?? "",
                    AiStatusCode = reader["ai_status_code"]?.ToString() ?? "",
                    HumanVerdict = reader["human_verdict"]?.ToString() ?? "",
                    HumanStatusCode = reader["human_status_code"]?.ToString() ?? "",
                    OcrText = reader["ocr_text"]?.ToString() ?? "",
                    ReviewedAt = reviewedAt
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetProcessedReviews failed: {ex.Message}");
        }
        return rows;
    }

    public List<EnforcementRecord> GetEnforcementRecordsInRange(DateTime? from, DateTime? to)
    {
        var list = new List<EnforcementRecord>();
        try
        {
            EnsureConnection();
            var sql = "SELECT * FROM enforcement_records WHERE human_verdict IS NOT NULL";
            if (from.HasValue) sql += " AND reviewed_at >= @from";
            if (to.HasValue) sql += " AND reviewed_at < @to";
            sql += " ORDER BY reviewed_at DESC";
            using var cmd = new SqliteCommand(sql, _connection);
            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString("o"));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString("o"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(MapEnforcementRecord(reader));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] GetEnforcementRecordsInRange failed: {ex.Message}");
        }
        return list;
    }

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
