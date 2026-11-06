using System.Reflection;
using Npgsql;

namespace Osteo.Agenda.Core.Data;

/// <summary>
/// Applies embedded Data/Sql/NNNN_*.sql scripts in order, once each, inside a transaction.
/// An advisory lock prevents two instances migrating at the same time.
/// </summary>
public static class SqlMigrator
{
    private const long LockKey = 7_302_026_001;

    public static async Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        var assembly = typeof(SqlMigrator).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal) && n.Contains(".Data.Sql.", StringComparison.Ordinal))
            .Select(n => (Resource: n, Version: VersionOf(n)))
            .OrderBy(s => s.Version, StringComparer.Ordinal)
            .ToList();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await Exec(conn, null, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Exec(conn, null, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version    text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now()
                )
                """, ct);

            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            var newlyApplied = new List<string>();
            foreach (var (resource, version) in scripts)
            {
                if (applied.Contains(version))
                {
                    continue;
                }

                var sql = await ReadResource(assembly, resource);
                await using var tx = await conn.BeginTransactionAsync(ct);
                await Exec(conn, tx, sql, ct);
                await using (var insert = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@v)", conn, tx))
                {
                    insert.Parameters.AddWithValue("v", version);
                    await insert.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                newlyApplied.Add(version);
            }

            return newlyApplied;
        }
        finally
        {
            await Exec(conn, null, $"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
    }

    // "Osteo.Agenda.Core.Data.Sql.0001_initial.sql" -> "0001_initial"
    private static string VersionOf(string resource)
    {
        var withoutExt = resource[..^".sql".Length];
        return withoutExt[(withoutExt.IndexOf(".Data.Sql.", StringComparison.Ordinal) + ".Data.Sql.".Length)..];
    }

    private static async Task<string> ReadResource(Assembly assembly, string name)
    {
        await using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
