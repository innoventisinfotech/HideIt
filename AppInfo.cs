namespace HideIt;

/// <summary>Central place for product/links. Update <see cref="RepoOwner"/> after creating the repo.</summary>
public static class AppInfo
{
    public const string ProductName = "HideIt";

    // TODO: replace "OWNER" with your GitHub username/org once the repo exists.
    public const string RepoOwner = "OWNER";
    public const string RepoName = "HideIt";

    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
    public static string ReleasesUrl => $"{RepoUrl}/releases";
}
