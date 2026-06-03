using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Application.Services.Infrastructure;

public interface IAssetDatabase
{
    string DatabasePath { get; }

    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default);

    SqliteConnection OpenConnection();
}
