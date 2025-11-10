using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace WallpaperCycler
{
    public class SettingsModel
    {
        public System.Drawing.Color? FillColor { get; set; } = System.Drawing.ColorTranslator.FromHtml("#0b5fff");
        public bool Autostart { get; set; } = true;
        public int RescanThreshold { get; set; } = 20;
    }

    public record PhotoRow(string Path, int SeenOrdinal, string ModifiedDate);

    public class PhotoDatabase
    {
        private readonly string dbPath;
        private readonly string connString;
        public SettingsModel Settings { get; set; } = new SettingsModel();

        public PhotoDatabase(string dbPath)
        {
            this.dbPath = dbPath;
            connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            Initialize();
        }

        private void Initialize()
        {
            var first = !File.Exists(dbPath);
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Photos (
    Path TEXT PRIMARY KEY,
    SeenOrdinal INTEGER,
    ModifiedDate TEXT
);
";
            cmd.ExecuteNonQuery();

            // settings table (very small)
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT
);
";
            cmd.ExecuteNonQuery();

            if (first)
            {
                // insert defaults
                SetSetting("FillColor", "#0b5fff");
                SetSetting("Autostart", "true");
                SetSetting("RescanThreshold", "20");
            }
            LoadSettings();
        }

        private void SetSetting(string key, string value)
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES ($k, $v);";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        private string? GetSetting(string key)
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            var r = cmd.ExecuteScalar();
            return r?.ToString();
        }

        private void LoadSettings()
        {
            var c = GetSetting("FillColor");
            if (c != null)
            {
                Settings.FillColor = System.Drawing.ColorTranslator.FromHtml(c);
            }
            var a = GetSetting("Autostart");
            if (a != null) Settings.Autostart = bool.Parse(a);
            var t = GetSetting("RescanThreshold");
            if (t != null) Settings.RescanThreshold = int.Parse(t);
        }

        public void InitialScan(string folder)
        {
            var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()));

            using var conn = new SqliteConnection(connString);
            conn.Open();
            using var tran = conn.BeginTransaction();
            foreach (var f in files)
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO Photos (Path, SeenOrdinal, ModifiedDate) VALUES ($p, -1, $m);";
                cmd.Parameters.AddWithValue("$p", f);
                cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            tran.Commit();
        }

        public PhotoRow? GetRandomUnseen()
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Path, SeenOrdinal, ModifiedDate FROM Photos WHERE SeenOrdinal = -1 ORDER BY RANDOM() LIMIT 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                return new PhotoRow(r.GetString(0), r.GetInt32(1), r.GetString(2));
            }
            return null;
        }

        public PhotoRow? GetBySeenOrdinal(int ordinal)
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Path, SeenOrdinal, ModifiedDate FROM Photos WHERE SeenOrdinal = $o LIMIT 1";
            cmd.Parameters.AddWithValue("$o", ordinal);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return new PhotoRow(r.GetString(0), r.GetInt32(1), r.GetString(2));
            return null;
        }

        public int GetMaxSeenOrdinal()
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(SeenOrdinal), -1) FROM Photos";
            var val = cmd.ExecuteScalar();
            return Convert.ToInt32(val);
        }

        public void MarkSeen(string path, int ordinal)
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Photos SET SeenOrdinal = $o, ModifiedDate = $m WHERE Path = $p";
            cmd.Parameters.AddWithValue("$o", ordinal);
            cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }

        public void DeletePath(string path)
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Photos WHERE Path = $p";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }

        public void DeleteMissingPaths()
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Path FROM Photos";
            using var r = cmd.ExecuteReader();
            var toDelete = new List<string>();
            while (r.Read())
            {
                var p = r.GetString(0);
                if (!File.Exists(p)) toDelete.Add(p);
            }
            foreach (var d in toDelete) DeletePath(d);
        }

        public void Rescan(string? root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            InitialScan(root);
            DeleteMissingPaths();
        }

        public void ResetSeen()
        {
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Photos SET SeenOrdinal = -1";
            cmd.ExecuteNonQuery();
        }

        public void HandleFileCreated(string path)
        {
            var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            if (!exts.Contains(Path.GetExtension(path).ToLowerInvariant())) return;
            using var conn = new SqliteConnection(connString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO Photos (Path, SeenOrdinal, ModifiedDate) VALUES ($p, -1, $m);";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$m", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public void HandleFileDeleted(string path)
        {
            DeletePath(path);
        }
    }
}