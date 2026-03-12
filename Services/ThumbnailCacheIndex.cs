using System;
using System.Drawing;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MyManager
{
    internal sealed class ThumbnailCacheIndex
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly object _initSync = new();
        private volatile bool _initialized;

        public ThumbnailCacheIndex(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared";
        }

        public bool TryGetCacheFileName(string sourcePath, long fileLength, long lastWriteTicksUtc, Size size, out string cacheFileName)
        {
            cacheFileName = string.Empty;
            if (!CanUseKey(sourcePath, fileLength, lastWriteTicksUtc, size))
                return false;

            try
            {
                EnsureInitialized();
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT cache_file_name
                    FROM preview_cache_index
                    WHERE source_path = $sourcePath
                      AND file_length = $fileLength
                      AND last_write_ticks_utc = $lastWriteTicksUtc
                      AND thumb_width = $width
                      AND thumb_height = $height
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue("$sourcePath", sourcePath);
                command.Parameters.AddWithValue("$fileLength", fileLength);
                command.Parameters.AddWithValue("$lastWriteTicksUtc", lastWriteTicksUtc);
                command.Parameters.AddWithValue("$width", size.Width);
                command.Parameters.AddWithValue("$height", size.Height);

                var value = command.ExecuteScalar();
                if (value is not string fileName || string.IsNullOrWhiteSpace(fileName))
                    return false;

                cacheFileName = fileName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Upsert(string sourcePath, long fileLength, long lastWriteTicksUtc, Size size, string cacheFileName)
        {
            if (!CanUseKey(sourcePath, fileLength, lastWriteTicksUtc, size) || string.IsNullOrWhiteSpace(cacheFileName))
                return;

            try
            {
                EnsureInitialized();
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO preview_cache_index (
                        source_path,
                        file_length,
                        last_write_ticks_utc,
                        thumb_width,
                        thumb_height,
                        cache_file_name,
                        updated_utc
                    )
                    VALUES (
                        $sourcePath,
                        $fileLength,
                        $lastWriteTicksUtc,
                        $width,
                        $height,
                        $cacheFileName,
                        $updatedUtc
                    )
                    ON CONFLICT(source_path, file_length, last_write_ticks_utc, thumb_width, thumb_height)
                    DO UPDATE SET
                        cache_file_name = excluded.cache_file_name,
                        updated_utc = excluded.updated_utc;
                    """;
                command.Parameters.AddWithValue("$sourcePath", sourcePath);
                command.Parameters.AddWithValue("$fileLength", fileLength);
                command.Parameters.AddWithValue("$lastWriteTicksUtc", lastWriteTicksUtc);
                command.Parameters.AddWithValue("$width", size.Width);
                command.Parameters.AddWithValue("$height", size.Height);
                command.Parameters.AddWithValue("$cacheFileName", cacheFileName);
                command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            catch
            {
                // Ignore transient index failures.
            }
        }

        public void Remove(string sourcePath, long fileLength, long lastWriteTicksUtc, Size size)
        {
            if (!CanUseKey(sourcePath, fileLength, lastWriteTicksUtc, size))
                return;

            try
            {
                EnsureInitialized();
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    DELETE FROM preview_cache_index
                    WHERE source_path = $sourcePath
                      AND file_length = $fileLength
                      AND last_write_ticks_utc = $lastWriteTicksUtc
                      AND thumb_width = $width
                      AND thumb_height = $height;
                    """;
                command.Parameters.AddWithValue("$sourcePath", sourcePath);
                command.Parameters.AddWithValue("$fileLength", fileLength);
                command.Parameters.AddWithValue("$lastWriteTicksUtc", lastWriteTicksUtc);
                command.Parameters.AddWithValue("$width", size.Width);
                command.Parameters.AddWithValue("$height", size.Height);
                command.ExecuteNonQuery();
            }
            catch
            {
                // Ignore transient index failures.
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            lock (_initSync)
            {
                if (_initialized)
                    return;

                var folder = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var pragmaCommand = connection.CreateCommand())
                {
                    pragmaCommand.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA busy_timeout=5000;";
                    pragmaCommand.ExecuteNonQuery();
                }

                using (var schemaCommand = connection.CreateCommand())
                {
                    schemaCommand.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS preview_cache_index (
                            source_path TEXT NOT NULL,
                            file_length INTEGER NOT NULL,
                            last_write_ticks_utc INTEGER NOT NULL,
                            thumb_width INTEGER NOT NULL,
                            thumb_height INTEGER NOT NULL,
                            cache_file_name TEXT NOT NULL,
                            updated_utc TEXT NOT NULL,
                            PRIMARY KEY (source_path, file_length, last_write_ticks_utc, thumb_width, thumb_height)
                        );
                        CREATE INDEX IF NOT EXISTS idx_preview_cache_index_updated_utc
                            ON preview_cache_index(updated_utc);
                        """;
                    schemaCommand.ExecuteNonQuery();
                }

                _initialized = true;
            }
        }

        private static bool CanUseKey(string sourcePath, long fileLength, long lastWriteTicksUtc, Size size)
        {
            return
                !string.IsNullOrWhiteSpace(sourcePath)
                && fileLength > 0
                && lastWriteTicksUtc > 0
                && size.Width > 0
                && size.Height > 0;
        }
    }
}
