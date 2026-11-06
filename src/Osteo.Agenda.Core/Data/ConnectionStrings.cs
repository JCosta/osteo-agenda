using Npgsql;

namespace Osteo.Agenda.Core.Data;

public static class ConnectionStrings
{
    /// <summary>
    /// Accepts either an Npgsql connection string or a URL such as the DATABASE_URL Render provides
    /// (postgres://user:pass@host:5432/db).
    /// </summary>
    public static string Normalize(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        // Render's internal URL has no TLS; external hosts do. Allow override via ?sslmode=.
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sslMode = query["sslmode"];
        if (!string.IsNullOrEmpty(sslMode) && Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var mode))
        {
            builder.SslMode = mode;
        }
        else if (uri.Host.Contains('.'))
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }
}
