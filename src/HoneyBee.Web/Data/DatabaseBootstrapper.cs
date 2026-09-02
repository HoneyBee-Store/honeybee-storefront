using Microsoft.Data.SqlClient;

namespace HoneyBee.Web.Data;

/// <summary>
/// Makes sure the LocalDB database is actually attached before EF looks at it.
///
/// LocalDB can end up with the .mdf sitting on disk while the instance has no
/// record of the database — an abrupt shutdown is enough. EF then decides the
/// database does not exist, issues CREATE DATABASE, and that fails with
/// "Cannot create file … because it already exists", which stops the app at
/// startup with no way forward short of hand-attaching the files.
///
/// This runs first and re-attaches the orphaned files when it finds them, so
/// the situation resolves itself rather than needing SQL run by hand.
/// Everything here is specific to LocalDB and skipped for any other server.
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task EnsureAttachedAsync(
        string? connectionString, ILogger logger, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        SqlConnectionStringBuilder cs;
        try
        {
            cs = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "The connection string could not be parsed.");
            return;
        }

        var database = cs.InitialCatalog;

        // Logged every start: when something is wrong with the database, the
        // first question is always which server and catalogue were actually
        // resolved, and configuration can come from several places.
        logger.LogInformation("Database target: {Server} / {Database}", cs.DataSource, database);

        if (string.IsNullOrWhiteSpace(database)) return;
        if (!cs.DataSource.Contains("(localdb)", StringComparison.OrdinalIgnoreCase)) return;

        var master = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = Math.Max(cs.ConnectTimeout, 60)
        };

        try
        {
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync(ct);

            if (await DatabaseExistsAsync(connection, database, ct))
            {
                logger.LogInformation("Database {Database} is attached.", database);
                return;
            }

            var files = await FindOrphanedFilesAsync(connection, database, ct);

            if (files is null)
            {
                // Genuinely a first run: nothing to attach, and EF's own
                // CREATE DATABASE will work.
                logger.LogInformation(
                    "Database {Database} does not exist yet and no files were found — it will be created.",
                    database);
                return;
            }

            logger.LogWarning(
                "Database {Database} was not attached but its files exist. Re-attaching {Mdf}.",
                database, files.Value.Mdf);

            await AttachAsync(connection, database, files.Value.Mdf, files.Value.Ldf, ct);

            logger.LogInformation("Re-attached {Database}.", database);
        }
        catch (Exception ex)
        {
            // Never block startup: if this cannot help, EF still runs next and
            // its own error is the more useful one to surface.
            logger.LogError(ex, "Could not verify the database attachment. Continuing to EF.");
        }
    }

    private static async Task<bool> DatabaseExistsAsync(
        SqlConnection connection, string database, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.AddWithValue("@name", database);

        var result = await command.ExecuteScalarAsync(ct);
        return result is not null && result != DBNull.Value;
    }

    /// <summary>
    /// Looks for a detached .mdf in the places LocalDB puts them: the
    /// instance's own default data directory, then the user profile root,
    /// which is where LocalDB lands when no path is configured.
    /// </summary>
    private static async Task<(string Mdf, string? Ldf)?> FindOrphanedFilesAsync(
        SqlConnection connection, string database, CancellationToken ct)
    {
        var candidates = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))";

            if (await command.ExecuteScalarAsync(ct) is string path && !string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(Path.Combine(path, database + ".mdf"));
            }
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), database + ".mdf"));

        foreach (var mdf in candidates)
        {
            if (!File.Exists(mdf)) continue;

            var ldf = Path.ChangeExtension(mdf, null) + "_log.ldf";
            return (mdf, File.Exists(ldf) ? ldf : null);
        }

        return null;
    }

    private static async Task AttachAsync(
        SqlConnection connection, string database, string mdf, string? ldf, CancellationToken ct)
    {
        // CREATE DATABASE takes no parameters, so the values are quoted by hand.
        // They come from SERVERPROPERTY and the app's own configuration rather
        // than from user input, but doubling quotes costs nothing.
        static string Q(string value) => value.Replace("'", "''");
        static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

        var files = ldf is null
            ? $"(FILENAME = N'{Q(mdf)}')"
            : $"(FILENAME = N'{Q(mdf)}'), (FILENAME = N'{Q(ldf)}')";

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;

        // FOR ATTACH_REBUILD_LOG when the log is missing: SQL Server then
        // recreates it rather than refusing the attach.
        command.CommandText =
            $"CREATE DATABASE {Bracket(database)} ON {files} FOR " +
            (ldf is null ? "ATTACH_REBUILD_LOG" : "ATTACH");

        await command.ExecuteNonQueryAsync(ct);
    }
}
