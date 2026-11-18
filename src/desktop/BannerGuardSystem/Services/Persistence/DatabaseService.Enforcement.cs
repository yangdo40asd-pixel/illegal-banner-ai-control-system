using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public partial class DatabaseService
{
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

            if (!string.IsNullOrWhiteSpace(alert.SourceImage))
            {
                using var existsSrc = new SqliteCommand(
                    "SELECT 1 FROM enforcement_records WHERE source_image = @source_image LIMIT 1", _connection);
                existsSrc.Parameters.AddWithValue("@source_image", alert.SourceImage);
                if (existsSrc.ExecuteScalar() != null)
                    return false;
            }

            var now = DateTime.Now;
            var detectedAt = alert.Timestamp == default ? now : alert.Timestamp;
            var alphaNum = new string((alert.Id ?? Guid.NewGuid().ToString("N")).Where(char.IsLetterOrDigit).ToArray());
            var idPart = string.IsNullOrEmpty(alphaNum) ? Guid.NewGuid().ToString("N")[..8] : (alphaNum.Length > 16 ? alphaNum[^16..] : alphaNum);
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

    /// <summary>
    /// VLM 검증 관제관 판정 결과를 원자적 트랜잭션으로 저장합니다 (human_reviews + tracked_banners + enforcement_records).
    /// </summary>
    public bool UpdateEnforcementProcessingStatus(string enforcementNo, string newStatus, string officer = "관제관")
    {
        try
        {
            EnsureConnection();
            const string sql = @"
                UPDATE enforcement_records
                SET processing_status = @newStatus,
                    assignee = @assignee,
                    updated_at = @updated_at
                WHERE enforcement_no = @enforcementNo";
            using var cmd = new SqliteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@newStatus", newStatus);
            cmd.Parameters.AddWithValue("@assignee", officer);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@enforcementNo", enforcementNo);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] UpdateEnforcementProcessingStatus failed: {ex.Message}");
            return false;
        }
    }

    public List<EnforcementRecord> GetEnforcementRecords()
    {
        return GetEnforcementRecords(out _);
    }

    public List<EnforcementRecord> GetEnforcementRecords(out string? errorMessage)
    {
        errorMessage = null;
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
            errorMessage = ex.Message;
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

}
