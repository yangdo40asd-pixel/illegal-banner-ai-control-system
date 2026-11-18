using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public partial class DatabaseService
{
    public bool SaveVerdictTransaction(
        string alertId,
        string cctvId,
        string? sourceImage,
        string aiStatusCode,
        string humanVerdict,
        string humanStatusCode,
        string ocrText,
        string status,
        string verdict,
        string reviewer,
        string note)
    {
        if (string.IsNullOrWhiteSpace(alertId))
            return false;

        try
        {
            EnsureConnection();
            using var tx = _connection!.BeginTransaction();

            var normalizedVerdict = humanVerdict switch
            {
                "ILLEGAL" or "불법" => "ILLEGAL",
                "LEGAL" or "합법" => "LEGAL",
                _ => "REVIEW"
            };

            // 1. human_reviews 기록
            var reviewSql = @"
                INSERT INTO human_reviews
                (alert_id, cctv_id, source_image, ai_status_code, human_verdict,
                 human_status_code, reviewer, note, ocr_text, reviewed_at)
                VALUES (@alert_id, @cctv_id, @source_image, @ai_status_code, @human_verdict,
                        @human_status_code, @reviewer, @note, @ocr_text, @reviewed_at)";
            using (var cmd = new SqliteCommand(reviewSql, _connection, tx))
            {
                cmd.Parameters.AddWithValue("@alert_id", alertId);
                cmd.Parameters.AddWithValue("@cctv_id", cctvId ?? "");
                cmd.Parameters.AddWithValue("@source_image", sourceImage ?? "");
                cmd.Parameters.AddWithValue("@ai_status_code", aiStatusCode ?? "");
                cmd.Parameters.AddWithValue("@human_verdict", normalizedVerdict);
                cmd.Parameters.AddWithValue("@human_status_code", humanStatusCode ?? "");
                cmd.Parameters.AddWithValue("@reviewer", reviewer ?? "operator");
                cmd.Parameters.AddWithValue("@note", note ?? "");
                cmd.Parameters.AddWithValue("@ocr_text", ocrText ?? "");
                cmd.Parameters.AddWithValue("@reviewed_at", DateTime.Now.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // 2. tracked_banners 갱신 (CCTV 채널 및 텍스트 존재 시)
            if (!string.IsNullOrWhiteSpace(cctvId) && !string.IsNullOrWhiteSpace(ocrText))
            {
                var bannerSql = @"
                    UPDATE tracked_banners
                    SET status = @status, verdict = @verdict
                    WHERE id = (
                        SELECT id FROM tracked_banners
                        WHERE cctv_id = @cctv_id AND text = @text
                        ORDER BY last_seen DESC LIMIT 1
                    )";
                using var bCmd = new SqliteCommand(bannerSql, _connection, tx);
                bCmd.Parameters.AddWithValue("@status", status);
                bCmd.Parameters.AddWithValue("@verdict", verdict);
                bCmd.Parameters.AddWithValue("@cctv_id", cctvId);
                bCmd.Parameters.AddWithValue("@text", ocrText);
                bCmd.ExecuteNonQuery();
            }

            // 3. enforcement_records 갱신
            var processing = normalizedVerdict switch
            {
                "ILLEGAL" => "Received",
                "LEGAL" => "Received",
                _ => "Urgent"
            };
            var isUrgent = normalizedVerdict == "REVIEW" ? 1 : 0;

            var enfSql = @"
                UPDATE enforcement_records
                SET processing_status = @processing_status,
                    is_urgent = @is_urgent,
                    human_verdict = @human_verdict,
                    human_status_code = @human_status_code,
                    assignee = @assignee,
                    reviewed_at = @reviewed_at,
                    updated_at = @updated_at
                WHERE alert_id = @alert_id OR (@source_image != '' AND source_image = @source_image)";
            using (var eCmd = new SqliteCommand(enfSql, _connection, tx))
            {
                eCmd.Parameters.AddWithValue("@processing_status", processing);
                eCmd.Parameters.AddWithValue("@is_urgent", isUrgent);
                eCmd.Parameters.AddWithValue("@human_verdict", normalizedVerdict);
                eCmd.Parameters.AddWithValue("@human_status_code", humanStatusCode ?? "");
                eCmd.Parameters.AddWithValue("@assignee", reviewer);
                eCmd.Parameters.AddWithValue("@reviewed_at", DateTime.Now.ToString("o"));
                eCmd.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("o"));
                eCmd.Parameters.AddWithValue("@alert_id", alertId);
                eCmd.Parameters.AddWithValue("@source_image", sourceImage ?? "");
                eCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] SaveVerdictTransaction failed: {ex.Message}");
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
            var normalizedVerdict = humanVerdict switch
            {
                "ILLEGAL" or "불법" => "ILLEGAL",
                "LEGAL" or "합법" => "LEGAL",
                _ => "REVIEW"
            };
            var processing = normalizedVerdict switch
            {
                "ILLEGAL" => "Received",
                "LEGAL" => "Received",
                _ => "Urgent"
            };
            var isUrgent = normalizedVerdict == "REVIEW" ? 1 : 0;

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
            cmd.Parameters.AddWithValue("@human_verdict", normalizedVerdict);
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

}
