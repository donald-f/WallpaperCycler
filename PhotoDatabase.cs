using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace WallpaperCycler
{
    public class SettingsModel
    {
        public Color? FillColor { get; set; } = AppConstants.DefaultFillColor;
        public int CycleMinutes { get; set; } = 0; // 0 = off
        public bool ShowDateOnWallpaper { get; set; } = false;
    }

    public record PhotoRow(string Path, int SeenOrdinal, string ModifiedDate);

    public class PhotoDatabase
    {
        private readonly string _connString;
        private readonly string _dbPath;

        public SettingsModel Settings { get; set; } = new SettingsModel();

        public PhotoDatabase(string dbPath)
        {
            _dbPath = dbPath;
            _connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            Initialize();
        }

        private void Initialize()
        {
            bool firstRun = !File.Exists(_dbPath);

            using var conn = OpenConnection();

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS Photos (
    Path         TEXT PRIMARY KEY,
    SeenOrdinal  INTEGER,
    ModifiedDate TEXT
);");

            Execute(conn, @"
CREATE TABLE IF NOT EXISTS AppSettings (
    Key   TEXT PRIMARY KEY,
    Value TEXT
);");

            if (firstRun)
            {
                SetSettingCore(conn, "FillColor", ColorTranslator.ToHtml(AppConstants.DefaultFillColor));
                SetSettingCore(conn, "CycleMinutes", "0");
                SetSettingCore(conn, "ShowDateOnWallpaper", "false");
                SetSettingCore(conn, "LastShownPath", string.Empty);
                SetSettingCore(conn, "LastSelectedFolder", string.Empty);
            }

            LoadSettings();
        }

        // ── Settings ────────────────────────────────────────────────────────────

        public void SetSetting(string key, string value)
        {
            using var conn = OpenConnection();
            SetSettingCore(conn, key, value);
        }

        public string? GetSetting(string key)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar()?.ToString();
        }

        private void LoadSettings()
        {
            var fillHex = GetSetting("FillColor");
            if (!string.IsNullOrEmpty(fillHex))
            {
                try { Settings.FillColor = ColorTranslator.FromHtml(fillHex); }
                catch (Exception ex) { Logger.Log($"Invalid FillColor in DB ('{fillHex}'): {ex.Message}"); }
            }

            var showDate = GetSetting("ShowDateOnWallpaper");
            if (!string.IsNullOrEmpty(showDate))
            {
                if (bool.TryParse(showDate, out var b)) Settings.ShowDateOnWallpaper = b;
            }

            var cycleStr = GetSetting("CycleMinutes");
            if (!string.IsNullOrEmpty(cycleStr))
            {
                if (int.TryParse(cycleStr, out var minutes)) Settings.CycleMinutes = minutes;
                else Logger.Log($"Invalid CycleMinutes in DB ('{cycleStr}')");
            }
        }

        // ── Photo rows ──────────────────────────────────────────────────────────

        public void InitialScan(string folder)
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories);

            using var conn = OpenConnection();
            using var tran = conn.BeginTransaction();

            foreach (var f in files)
            {
                if (!AppConstants.ValidPhotoExtensions.Contains(Path.GetExtension(f)))
                    continue;

                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "INSERT OR IGNORE INTO Photos (Path, SeenOrdinal, ModifiedDate) VALUES ($p, -1, $m);";
                cmd.Parameters.AddWithValue("$p", f);
                cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            tran.Commit();
        }

        public PhotoRow? GetRandomUnseen()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT Path, SeenOrdinal, ModifiedDate FROM Photos WHERE SeenOrdinal = -1 ORDER BY RANDOM() LIMIT 1";
            using var r = cmd.ExecuteReader();
            return r.Read() ? new PhotoRow(r.GetString(0), r.GetInt32(1), r.GetString(2)) : null;
        }

        public PhotoRow? GetBySeenOrdinal(int ordinal)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT Path, SeenOrdinal, ModifiedDate FROM Photos WHERE SeenOrdinal = $o LIMIT 1";
            cmd.Parameters.AddWithValue("$o", ordinal);
            using var r = cmd.ExecuteReader();
            return r.Read() ? new PhotoRow(r.GetString(0), r.GetInt32(1), r.GetString(2)) : null;
        }

        public int GetMaxSeenOrdinal()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(SeenOrdinal), -1) FROM Photos";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int GetTotalPhotoCount()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Photos";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int GetSeenOrdinalForPath(string path)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SeenOrdinal FROM Photos WHERE Path = $p LIMIT 1";
            cmd.Parameters.AddWithValue("$p", path);
            var result = cmd.ExecuteScalar();
            return result == null ? -1 : Convert.ToInt32(result);
        }

        public void MarkSeen(string path, int ordinal)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE Photos SET SeenOrdinal = $o, ModifiedDate = $m WHERE Path = $p";
            cmd.Parameters.AddWithValue("$o", ordinal);
            cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }

        public void ResetSeen()
        {
            using var conn = OpenConnection();
            Execute(conn, "UPDATE Photos SET SeenOrdinal = -1");
        }

        public void DeletePath(string path)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Photos WHERE Path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAllPaths()
        {
            using var conn = OpenConnection();
            Execute(conn, "DELETE FROM Photos");
        }

        public void HandleFileCreated(string path)
        {
            if (!AppConstants.ValidPhotoExtensions.Contains(Path.GetExtension(path)))
                return;

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR IGNORE INTO Photos (Path, SeenOrdinal, ModifiedDate) VALUES ($p, -1, $m);";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public void HandleFileDeleted(string path) => DeletePath(path);

        // ── Helpers ─────────────────────────────────────────────────────────────

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connString);
            conn.Open();
            return conn;
        }

        private static void Execute(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static void SetSettingCore(SqliteConnection conn, string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES ($k, $v);";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
    }
}
