namespace HideIt;

/// <summary>Central place for product/links. Update <see cref="RepoOwner"/> after creating the repo.</summary>
public static class AppInfo
{
    public const string ProductName = "HideIt";

    public const string RepoOwner = "innoventisinfotech";
    public const string RepoName = "HideIt";

    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
    public static string ReleasesUrl => $"{RepoUrl}/releases";
}
