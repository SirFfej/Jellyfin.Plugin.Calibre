using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Calibre;

public class CalibreDatabase : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private bool _disposed;

    public CalibreDatabase(string libraryPath)
    {
        var dbPath = Path.Combine(libraryPath, "metadata.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM books";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result != null && Convert.ToInt32(result) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<CalibreBook>> GetAllBooksAsync(CancellationToken cancellationToken = default)
    {
        var books = new List<CalibreBook>();

        await using var conn = await GetConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.id, b.title, b.sort, b.pubdate, b.series_index,
                   b.has_cover, b.path, b.last_modified,
                   (SELECT GROUP_CONCAT(a.name, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors,
                   (SELECT GROUP_CONCAT(a.sort, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors_sort,
                   (SELECT GROUP_CONCAT(t.name, ', ') FROM tags t
                    JOIN books_tags_link btl ON t.id = btl.tag
                    WHERE btl.book = b.id) as tags,
                   (SELECT s.name FROM series s
                    JOIN books_series_link bsl ON s.id = bsl.series
                    WHERE bsl.book = b.id) as series_name,
                   (SELECT p.name FROM publishers p
                    JOIN books_publishers_link bpl ON p.id = bpl.publisher
                    WHERE bpl.book = b.id) as publisher_name,
                   c.text as comments
            FROM books b
            LEFT JOIN comments c ON b.id = c.book
            ORDER BY b.title";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var book = new CalibreBook
            {
                Id = reader.GetInt32(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                SortTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PubDate = reader.IsDBNull(3) ? null : ParsePubDate(reader.GetString(3)),
                SeriesIndex = reader.IsDBNull(4) ? 1f : (float)reader.GetDouble(4),
                HasCover = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                Path = reader.IsDBNull(6) ? "" : reader.GetString(6),
                LastModified = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                Authors = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetString(8).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                AuthorSort = reader.IsDBNull(9) ? Array.Empty<string>() : reader.GetString(9).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                Tags = reader.IsDBNull(10) ? Array.Empty<string>() : reader.GetString(10).Split(", ", StringSplitOptions.RemoveEmptyEntries),
                Series = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Publisher = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Comments = reader.IsDBNull(13) ? "" : reader.GetString(13)
            };
            books.Add(book);
        }

        return books;
    }

    public async Task<CalibreBook?> GetBookByIdAsync(int bookId, CancellationToken cancellationToken = default)
    {
        await using var conn = await GetConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.id, b.title, b.sort, b.pubdate, b.series_index,
                   b.has_cover, b.path, b.last_modified,
                   (SELECT GROUP_CONCAT(a.name, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors,
                   (SELECT GROUP_CONCAT(a.sort, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors_sort,
                   (SELECT GROUP_CONCAT(t.name, ', ') FROM tags t
                    JOIN books_tags_link btl ON t.id = btl.tag
                    WHERE btl.book = b.id) as tags,
                   (SELECT s.name FROM series s
                    JOIN books_series_link bsl ON s.id = bsl.series
                    WHERE bsl.book = b.id) as series_name,
                   (SELECT p.name FROM publishers p
                    JOIN books_publishers_link bpl ON p.id = bpl.publisher
                    WHERE bpl.book = b.id) as publisher_name,
                   c.text as comments
            FROM books b
            LEFT JOIN comments c ON b.id = c.book
            WHERE b.id = @id";
        cmd.Parameters.AddWithValue("@id", bookId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new CalibreBook
            {
                Id = reader.GetInt32(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                SortTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PubDate = reader.IsDBNull(3) ? null : ParsePubDate(reader.GetString(3)),
                SeriesIndex = reader.IsDBNull(4) ? 1f : (float)reader.GetDouble(4),
                HasCover = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                Path = reader.IsDBNull(6) ? "" : reader.GetString(6),
                LastModified = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                Authors = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetString(8).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                AuthorSort = reader.IsDBNull(9) ? Array.Empty<string>() : reader.GetString(9).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                Tags = reader.IsDBNull(10) ? Array.Empty<string>() : reader.GetString(10).Split(", ", StringSplitOptions.RemoveEmptyEntries),
                Series = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Publisher = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Comments = reader.IsDBNull(13) ? "" : reader.GetString(13)
            };
        }

        return null;
    }

    public async Task<List<CalibreBook>> SearchBooksAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        var books = new List<CalibreBook>();

        await using var conn = await GetConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.id, b.title, b.sort, b.pubdate, b.series_index,
                   b.has_cover, b.path, b.last_modified,
                   (SELECT GROUP_CONCAT(a.name, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors,
                   (SELECT GROUP_CONCAT(a.sort, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors_sort,
                   (SELECT GROUP_CONCAT(t.name, ', ') FROM tags t
                    JOIN books_tags_link btl ON t.id = btl.tag
                    WHERE btl.book = b.id) as tags,
                   (SELECT s.name FROM series s
                    JOIN books_series_link bsl ON s.id = bsl.series
                    WHERE bsl.book = b.id) as series_name,
                   (SELECT p.name FROM publishers p
                    JOIN books_publishers_link bpl ON p.id = bpl.publisher
                    WHERE bpl.book = b.id) as publisher_name,
                   c.text as comments
            FROM books b
            LEFT JOIN comments c ON b.id = c.book
            WHERE b.title LIKE @query OR authors LIKE @query
            ORDER BY b.title
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@query", $"%{query}%");
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var book = new CalibreBook
            {
                Id = reader.GetInt32(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                SortTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PubDate = reader.IsDBNull(3) ? null : ParsePubDate(reader.GetString(3)),
                SeriesIndex = reader.IsDBNull(4) ? 1f : (float)reader.GetDouble(4),
                HasCover = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                Path = reader.IsDBNull(6) ? "" : reader.GetString(6),
                LastModified = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                Authors = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetString(8).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                AuthorSort = reader.IsDBNull(9) ? Array.Empty<string>() : reader.GetString(9).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                Tags = reader.IsDBNull(10) ? Array.Empty<string>() : reader.GetString(10).Split(", ", StringSplitOptions.RemoveEmptyEntries),
                Series = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Publisher = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Comments = reader.IsDBNull(13) ? "" : reader.GetString(13)
            };
            books.Add(book);
        }

        return books;
    }

    public async Task<CalibreBook?> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            return null;

        isbn = isbn.Replace("-", "").Replace(" ", "");

        await using var conn = await GetConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT b.id, b.title, b.sort, b.pubdate, b.series_index,
                   b.has_cover, b.path, b.last_modified,
                   (SELECT GROUP_CONCAT(a.name, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors,
                   (SELECT GROUP_CONCAT(a.sort, ' & ') FROM authors a
                    JOIN books_authors_link bal ON a.id = bal.author
                    WHERE bal.book = b.id) as authors_sort,
                   (SELECT GROUP_CONCAT(t.name, ', ') FROM tags t
                    JOIN books_tags_link btl ON t.id = btl.tag
                    WHERE btl.book = b.id) as tags,
                   (SELECT s.name FROM series s
                    JOIN books_series_link bsl ON s.id = bsl.series
                    WHERE bsl.book = b.id) as series_name,
                   (SELECT p.name FROM publishers p
                    JOIN books_publishers_link bpl ON p.id = bpl.publisher
                    WHERE bpl.book = b.id) as publisher_name,
                   c.text as comments,
                   (SELECT i.val FROM identifiers i WHERE i.book = b.id AND LOWER(i.type) = 'isbn') as isbn
            FROM books b
            LEFT JOIN comments c ON b.id = c.book
            WHERE b.id IN (SELECT book FROM identifiers WHERE LOWER(type) = 'isbn' AND REPLACE(REPLACE(val, '-', ''), ' ', '') = @isbn)
            LIMIT 1";
        cmd.Parameters.AddWithValue("@isbn", isbn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new CalibreBook
            {
                Id = reader.GetInt32(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                SortTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PubDate = reader.IsDBNull(3) ? null : ParsePubDate(reader.GetString(3)),
                SeriesIndex = reader.IsDBNull(4) ? 1f : (float)reader.GetDouble(4),
                HasCover = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                Path = reader.IsDBNull(6) ? "" : reader.GetString(6),
                LastModified = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
                Authors = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetString(8).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                AuthorSort = reader.IsDBNull(9) ? Array.Empty<string>() : reader.GetString(9).Split(" & ", StringSplitOptions.RemoveEmptyEntries),
                Tags = reader.IsDBNull(10) ? Array.Empty<string>() : reader.GetString(10).Split(", ", StringSplitOptions.RemoveEmptyEntries),
                Series = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Publisher = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Comments = reader.IsDBNull(13) ? "" : reader.GetString(13),
                Isbn = reader.IsDBNull(14) ? "" : reader.GetString(14)
            };
        }

        return null;
    }

    public string GetCoverPath(string libraryPath, CalibreBook book)
    {
        if (!book.HasCover || string.IsNullOrEmpty(book.Path))
            return "";

        var coverPath = Path.Combine(libraryPath, book.Path, "cover.jpg");
        if (File.Exists(coverPath))
            return coverPath;

        coverPath = Path.Combine(libraryPath, book.Path, "cover.png");
        return File.Exists(coverPath) ? coverPath : "";
    }

    private async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);
        }
        else if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        return _connection;
    }

    private static DateTime? ParsePubDate(string? pubDate)
    {
        if (string.IsNullOrEmpty(pubDate))
            return null;

        if (DateTime.TryParse(pubDate, out var date))
            return date;

        if (int.TryParse(pubDate, out var year) && year > 100 && year < 10000)
            return new DateTime(year, 1, 1);

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _connection?.Dispose();
        _disposed = true;
    }
}

public class CalibreBook
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string SortTitle { get; set; } = "";
    public DateTime? PubDate { get; set; }
    public float SeriesIndex { get; set; } = 1f;
    public bool HasCover { get; set; }
    public string Path { get; set; } = "";
    public DateTime LastModified { get; set; }
    public string[] Authors { get; set; } = Array.Empty<string>();
    public string[] AuthorSort { get; set; } = Array.Empty<string>();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string Series { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Comments { get; set; } = "";
    public string Isbn { get; set; } = "";
}