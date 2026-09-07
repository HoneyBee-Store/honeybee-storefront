namespace HoneyBee.Web.Services;

/// <summary>
/// Where the app keeps things that must outlive a deployment.
///
/// Both of these default to null, which keeps local development exactly as it
/// was — uploads next to the seeded photos under wwwroot, and Data Protection
/// keys wherever the framework puts them.
///
/// On a host that replaces the application folder on every deploy, that is not
/// good enough: uploaded product photos would vanish, and losing the Data
/// Protection keys makes the stored Brevo API key undecryptable, which stops
/// order emails silently. Pointing these at persistent storage fixes both.
/// </summary>
public class StorageSettings
{
    /// <summary>
    /// Absolute directory for admin-uploaded product photos. Served at /uploads.
    /// On Azure App Service, something under /home — that path survives deploys
    /// and restarts, while the application folder does not.
    /// </summary>
    public string? UploadsPath { get; set; }

    /// <summary>
    /// Absolute directory for the Data Protection keyring. Must persist, or the
    /// encrypted mail settings become unreadable and have to be re-entered
    /// after every deployment.
    /// </summary>
    public string? KeysPath { get; set; }

    public bool HasUploads => !string.IsNullOrWhiteSpace(UploadsPath);
    public bool HasKeys => !string.IsNullOrWhiteSpace(KeysPath);

    /// <summary>The URL prefix uploads are served under.</summary>
    public const string UploadsRequestPath = "/uploads";

    /// <summary>
    /// Turns the configured paths into absolute ones.
    ///
    /// Shared Windows hosting gives you a site folder and nothing above it, so
    /// an absolute path like /home/data cannot be used there — but the site's
    /// own App_Data survives a publish as long as it is not deleted. Relative
    /// paths are resolved against the content root so both styles work:
    /// "/home/data/uploads" on a container host, "App_Data/uploads" on IIS.
    /// </summary>
    public void ResolveAgainst(string contentRoot)
    {
        UploadsPath = MakeAbsolute(UploadsPath, contentRoot);
        KeysPath = MakeAbsolute(KeysPath, contentRoot);
    }

    private static string? MakeAbsolute(string? path, string contentRoot) =>
        string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRoot, path));
}
