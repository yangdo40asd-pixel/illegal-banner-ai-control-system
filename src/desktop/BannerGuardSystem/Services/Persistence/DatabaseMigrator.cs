using System.IO;
using Microsoft.Data.Sqlite;
using BannerGuardSystem.Helpers;

namespace BannerGuardSystem.Services;

internal static class DatabaseMigrator
{
    public static void Apply(SqliteConnection connection)
    {
        var file = Path.Combine(RuntimePaths.Root, "database", "migrations", "001-initial.sql");
        using var transaction = connection.BeginTransaction();
        using (var version = new SqliteCommand("PRAGMA user_version", connection, transaction))
            if (Convert.ToInt32(version.ExecuteScalar()) > 1) throw new InvalidOperationException("Database is newer than this application");
        using (var schema = new SqliteCommand(File.ReadAllText(file), connection, transaction)) schema.ExecuteNonQuery();
        bool hasCrop = false;
        using (var check = new SqliteCommand("PRAGMA table_info(enforcement_records)", connection, transaction))
        using (var reader = check.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) == "crop_image_b64") hasCrop = true;
        if (!hasCrop)
        {
            using var alter = new SqliteCommand("ALTER TABLE enforcement_records ADD COLUMN crop_image_b64 TEXT", connection, transaction);
            alter.ExecuteNonQuery();
        }
        foreach (var table in new[] { "human_reviews", "enforcement_records" })
        foreach (var (oldValue, newValue) in new[] { ("합법", "LEGAL"), ("불법", "ILLEGAL"), ("재검토", "REVIEW") })
        {
            using var update = new SqliteCommand($"UPDATE {table} SET human_verdict=@new WHERE human_verdict=@old", connection, transaction);
            update.Parameters.AddWithValue("@new", newValue);
            update.Parameters.AddWithValue("@old", oldValue);
            update.ExecuteNonQuery();
        }
        using (var version = new SqliteCommand("PRAGMA user_version=1", connection, transaction)) version.ExecuteNonQuery();
        transaction.Commit();
    }
}
