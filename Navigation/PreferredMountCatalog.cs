using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PvPSentinel.Navigation;

internal sealed record PreferredMountChoice(uint RowId, string Name);

internal sealed record PreferredMountResolution(
    bool IsAvailable,
    uint RowId,
    string Name,
    string Explanation);

/// <summary>
/// Resolves persisted mount preferences through the current Mount sheet and
/// Dalamud's supported unlock-state service. Mount row IDs are not action IDs.
/// </summary>
internal sealed class PreferredMountCatalog(IDataManager dataManager, IUnlockState unlockState)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private IReadOnlyList<PreferredMountChoice> cachedUnlocked = [];
    private DateTime cacheExpiresUtc = DateTime.MinValue;

    public string LastCatalogError { get; private set; } = string.Empty;

    public IReadOnlyList<PreferredMountChoice> GetUnlockedMounts(bool forceRefresh = false)
    {
        var now = DateTime.UtcNow;
        if (!forceRefresh && now < cacheExpiresUtc)
            return cachedUnlocked;

        try
        {
            cachedUnlocked = dataManager.GetExcelSheet<Mount>()
                .Where(row => row.RowId != 0 && !string.IsNullOrWhiteSpace(NameOf(row)) && unlockState.IsMountUnlocked(row))
                .Select(row => new PreferredMountChoice(row.RowId, NameOf(row)))
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.RowId)
                .ToArray();
            LastCatalogError = string.Empty;
        }
        catch (Exception ex)
        {
            cachedUnlocked = [];
            LastCatalogError = $"Mount catalog unavailable: {ex.GetType().Name}.";
        }

        cacheExpiresUtc = now + CacheDuration;
        return cachedUnlocked;
    }

    public PreferredMountResolution Resolve(uint preferredRowId, string? preferredName)
    {
        var wantedName = string.IsNullOrWhiteSpace(preferredName) ? "Company Chocobo" : preferredName.Trim();
        try
        {
            var sheet = dataManager.GetExcelSheet<Mount>();
            Mount? selected = null;
            if (preferredRowId != 0)
            {
                foreach (var row in sheet)
                {
                    if (row.RowId == preferredRowId)
                    {
                        selected = row;
                        break;
                    }
                }
            }
            else
            {
                foreach (var row in sheet)
                {
                    if (NameOf(row).Equals(wantedName, StringComparison.CurrentCultureIgnoreCase) ||
                        NameOf(row).Equals(wantedName, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = row;
                        break;
                    }
                }
            }

            if (selected is null)
            {
                var key = preferredRowId == 0 ? $"name '{wantedName}'" : $"Mount row {preferredRowId}";
                return new PreferredMountResolution(false, 0, wantedName,
                    $"Preferred mount could not be resolved from current game data ({key}).");
            }

            var mount = selected.Value;
            var resolvedName = NameOf(mount);
            if (!unlockState.IsMountUnlocked(mount))
            {
                return new PreferredMountResolution(false, mount.RowId, resolvedName,
                    $"Preferred mount '{resolvedName}' (Mount row {mount.RowId}) is not unlocked for this character.");
            }

            return new PreferredMountResolution(true, mount.RowId, resolvedName,
                $"Preferred mount '{resolvedName}' resolved from Mount row {mount.RowId} and is unlocked.");
        }
        catch (Exception ex)
        {
            return new PreferredMountResolution(false, 0, wantedName,
                $"Preferred mount '{wantedName}' could not be validated: {ex.GetType().Name}.");
        }
    }

    private static string NameOf(Mount row) => row.Singular.ToString().Trim();
}
