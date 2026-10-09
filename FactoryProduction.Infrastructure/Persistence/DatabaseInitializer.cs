using System.Reflection;
using FactoryProduction.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FactoryProduction.Infrastructure;

/// <summary>
/// Creates the database if needed, then applies /migrations/*.sql in name order exactly once
/// (tracked in dbo.schema_migrations). Run at startup or with: dotnet run --project src/FactoryProduction.Api -- --migrate
/// </summary>
public sealed class DatabaseInitializer(AppDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No SQL Server connection string is configured.");

        await EnsureDatabaseExistsAsync(connectionString, ct);
        await ApplyMigrationsAsync(connectionString, ct);
    }

    private async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database)) return;

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC (@sql); END";
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplyMigrationsAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = @"IF OBJECT_ID('dbo.schema_migrations') IS NULL
CREATE TABLE dbo.schema_migrations (script_name NVARCHAR(200) NOT NULL PRIMARY KEY, applied_at DATETIMEOFFSET NOT NULL);";
            await create.ExecuteNonQueryAsync(ct);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("migrations/", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in scripts)
        {
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM dbo.schema_migrations WHERE script_name = @n";
                check.Parameters.AddWithValue("@n", name);
                if ((int)(await check.ExecuteScalarAsync(ct))! > 0) continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using (var run = connection.CreateCommand())
            {
                run.Transaction = tx;
                run.CommandText = sql;
                await run.ExecuteNonQueryAsync(ct);
            }
            await using (var mark = connection.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = "INSERT INTO dbo.schema_migrations (script_name, applied_at) VALUES (@n, SYSDATETIMEOFFSET())";
                mark.Parameters.AddWithValue("@n", name);
                await mark.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            logger.LogInformation("Applied migration {Script}", name);
        }
    }
}
