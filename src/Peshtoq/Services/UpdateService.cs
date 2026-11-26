using Velopack;
using Velopack.Sources;

namespace Peshtoq.Services;

/// <summary>
/// Thin wrapper over Velopack's UpdateManager: checks GitHub Releases (prerelease channel for now),
/// downloads deltas in the background, and applies on restart. Updates only work for an installed
/// build; a portable/dev run reports NotInstalled and does nothing. See docs/updates.md.
/// </summary>
public sealed class UpdateService
{
    private const string Repo = "https://github.com/doppidevs/peshtoq";
    private readonly UpdateManager _mgr;

    public UpdateService() =>
        _mgr = new UpdateManager(new GithubSource(Repo, null, prerelease: true));

    public bool IsInstalled => _mgr.IsInstalled;
    public string CurrentVersion => _mgr.CurrentVersion?.ToString() ?? "—";

    /// <summary>Returns update info when a newer version exists, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!_mgr.IsInstalled) return null;
        return await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public Task DownloadAsync(UpdateInfo info) => _mgr.DownloadUpdatesAsync(info);

    /// <summary>Applies the downloaded update and relaunches. Does not return on success.</summary>
    public void ApplyAndRestart(UpdateInfo info) => _mgr.ApplyUpdatesAndRestart(info);
}
