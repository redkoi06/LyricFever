using System.IO;
using Microsoft.Data.Sqlite;

namespace LyricFever.Core.Storage;

/// <summary>
/// SQLite 连接管理（对应 macOS CoreData 容器，4 个实体的替代）。
/// 库文件位于 %APPDATA%\LyricFever\lyrics.db。
/// </summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LyricFever", "lyrics.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeSchema();
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS SongObject (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                language TEXT NOT NULL DEFAULT '',
                lyricsTimestamps TEXT NOT NULL,
                lyricsWords TEXT NOT NULL,
                downloadDate TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS IDToColor (
                id TEXT PRIMARY KEY,
                songColor INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TranslationCache (
                cacheKey TEXT PRIMARY KEY,
                trackId TEXT NOT NULL,
                lyricHash TEXT NOT NULL,
                sourceLanguage TEXT NOT NULL,
                targetLanguage TEXT NOT NULL,
                modelVersion INTEGER NOT NULL,
                romanizationVersion INTEGER NOT NULL,
                originalLyrics TEXT NOT NULL,
                translatedLyrics TEXT NOT NULL,
                romanizedLyrics TEXT NOT NULL,
                createdAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SpotifyTrackMap (
                artistTitleKey TEXT PRIMARY KEY,
                trackId TEXT NOT NULL,
                songName TEXT NOT NULL,
                artistName TEXT NOT NULL,
                albumName TEXT NOT NULL,
                updatedAt TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
