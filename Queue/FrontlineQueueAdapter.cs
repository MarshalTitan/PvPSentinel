using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using PvPSentinel.Diagnostics;
using PvPSentinel.Models;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace PvPSentinel.Queue;

internal sealed unsafe class FrontlineQueueAdapter(
    IGameGui gameGui,
    IDataManager data,
    DevelopmentLogger developmentLog) : IFrontlineQueueAdapter
{
    private const byte DailyFrontlineRouletteId = 7;
    private readonly IReadOnlyDictionary<FrontlineMap, string> localizedCampaignNames = BuildLocalizedCampaignNames(data);
    private readonly string localizedFrontlineRouletteName = BuildLocalizedFrontlineRouletteName(data);

    public QueueClientSnapshot Capture()
    {
        try
        {
            var contentsFinder = ContentsFinder.Instance();
            var state = contentsFinder is null ? QueueClientState.None : ConvertState(contentsFinder->QueueInfo.QueueState);
            var selected = IsDailyFrontlineSelected();
            var (campaign, evidence) = DetectDailyCampaignFromContentsFinder();
            return new QueueClientSnapshot(state, selected, campaign, evidence);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-capture-failure", $"Duty Finder state read failed: {ex.GetType().Name}: {ex.Message}");
            return new QueueClientSnapshot(QueueClientState.Other, false, FrontlineMap.Unknown,
                $"Duty Finder state read failed: {ex.GetType().Name}.");
        }
    }

    public bool OpenDailyFrontline()
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent is null)
                return false;
            agent->OpenRouletteDuty(DailyFrontlineRouletteId);
            return true;
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-open-failure", $"Opening Daily Challenge: Frontline failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool SelectDailyFrontline()
    {
        try
        {
            if (IsDailyFrontlineSelected())
                return true;

            var agent = AgentContentsFinder.Instance();
            var addon = gameGui.GetAddonByName<AddonContentsFinder>("ContentsFinder");
            if (agent is null || addon is null || !addon->IsReady || addon->DutyList is null)
                return false;

            agent->InterfaceSub.LoadContentRoulette(DailyFrontlineRouletteId);
            agent->UpdateAddon();

            var items = addon->DutyList->Items;
            for (var vectorIndex = 0; vectorIndex < items.Count; vectorIndex++)
            {
                var item = items[vectorIndex].Value;
                if (item is null || item->UIntValues.Count == 0 || item->UIntValues[0] is 2 or 4)
                    continue;
                if (!LooksLikeFrontlineRow(item))
                    continue;

                var leafIndex = GetLeafCallbackIndex(addon, vectorIndex);
                var values = stackalloc AtkValue[2];
                values[0].Type = AtkValueType.Int;
                values[0].Int = 3;
                values[1].Type = AtkValueType.UInt;
                values[1].UInt = leafIndex;
                return addon->FireCallback(2, values, true);
            }

            return false;
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-select-failure", $"Selecting Daily Challenge: Frontline failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool JoinSelectedDailyFrontline()
    {
        try
        {
            if (!IsDailyFrontlineSelected())
                return false;

            var addon = gameGui.GetAddonByName<AddonContentsFinder>("ContentsFinder");
            if (addon is null || !addon->IsReady || addon->JoinButton is null || !addon->JoinButton->IsEnabled)
                return false;

            // ContentsFinder callback 12 is the addon's native Join action. Keep the
            // button state gate above so this cannot fire while Join is unavailable.
            return addon->AtkUnitBase.FireCallbackInt(12);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-join-failure", $"Joining Daily Challenge: Frontline failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool AcceptReadyDuty()
    {
        try
        {
            var addon = gameGui.GetAddonByName<AddonContentsFinderConfirm>("ContentsFinderConfirm");
            if (addon is null || !addon->IsReady || addon->CommenceButton is null || !addon->CommenceButton->IsEnabled)
                return false;
            return addon->AtkUnitBase.FireCallbackInt(8);
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-accept-failure", $"Accepting the Frontline duty failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool CancelQueue()
    {
        try
        {
            var contentsFinder = ContentsFinder.Instance();
            if (contentsFinder is null)
                return false;
            if (contentsFinder->QueueInfo.QueueState == ContentsFinderQueueState.None)
                return true;
            contentsFinder->QueueInfo.CancelQueue();
            return true;
        }
        catch (Exception ex)
        {
            developmentLog.Throttled("queue-cancel-failure", $"Cancelling the PvPSentinel-owned queue failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static QueueClientState ConvertState(ContentsFinderQueueState state) => state switch
    {
        ContentsFinderQueueState.None => QueueClientState.None,
        ContentsFinderQueueState.Pending => QueueClientState.Pending,
        ContentsFinderQueueState.Queued => QueueClientState.Queued,
        ContentsFinderQueueState.Ready => QueueClientState.Ready,
        _ => QueueClientState.Other,
    };

    private static bool IsDailyFrontlineSelected()
    {
        var agent = AgentContentsFinder.Instance();
        if (agent is null)
            return false;

        var selected = agent->SelectedContent;
        for (var index = 0; index < selected.Count; index++)
        {
            var entry = selected[index];
            if (entry.ContentType == ContentsType.Roulette && entry.Id == DailyFrontlineRouletteId)
                return true;
        }

        return false;
    }

    private (FrontlineMap Map, string Evidence) DetectDailyCampaignFromContentsFinder()
    {
        var addon = gameGui.GetAddonByName<AddonContentsFinder>("ContentsFinder");
        if (addon is null || !addon->IsReady || addon->AtkValues == null)
            return (FrontlineMap.Unknown, "Daily Frontline Duty Finder details are not open.");

        var detected = new HashSet<FrontlineMap>();
        var evidence = new List<string>();
        var count = Math.Min((int)addon->AtkValuesCount, 256);
        for (var index = 0; index < count; index++)
        {
            var value = addon->AtkValues[index];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                continue;

            var text = value.GetValueAsString();
            var map = IdentifyCampaignText(text);
            if (map == FrontlineMap.Unknown)
                continue;
            detected.Add(map);
            evidence.Add($"AtkValue[{index}] '{text}'");
        }

        return detected.Count == 1
            ? (detected.Single(), string.Join("; ", evidence))
            : (FrontlineMap.Unknown, detected.Count == 0
                ? "No recognized campaign name is exposed in the open Duty Finder details."
                : $"Duty Finder exposed multiple campaign names ({string.Join(", ", detected)}); refusing to guess.");
    }

    private FrontlineMap IdentifyCampaignText(string text)
    {
        var canonical = FrontlineMapCatalog.IdentifyText(text);
        if (canonical != FrontlineMap.Unknown)
            return canonical;

        foreach (var (map, localizedName) in localizedCampaignNames)
        {
            if (text.Contains(localizedName, StringComparison.OrdinalIgnoreCase))
                return map;
        }

        return FrontlineMap.Unknown;
    }

    private static IReadOnlyDictionary<FrontlineMap, string> BuildLocalizedCampaignNames(IDataManager data)
    {
        var result = new Dictionary<FrontlineMap, string>();
        var sheet = data.GetExcelSheet<ContentFinderCondition>();
        foreach (var map in Enum.GetValues<FrontlineMap>())
        {
            var id = map.ContentFinderConditionId();
            if (id == 0)
                continue;
            var row = sheet.FirstOrDefault(item => item.RowId == id);
            var name = row.RowId == 0 ? string.Empty : row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                result[map] = name;
        }

        return result;
    }

    private bool LooksLikeFrontlineRow(AtkComponentTreeListItem* item)
    {
        if (item->Renderer is not null)
        {
            var node = ((AtkComponentBase*)item->Renderer)->GetTextNodeById(6);
            var textNode = node == null ? null : node->GetAsAtkTextNode();
            if (textNode is not null && IsFrontlineLabel(textNode->NodeText.ToString()))
                return true;
        }

        for (var index = 0; index < item->StringValues.Count; index++)
        {
            var value = item->StringValues[index].Value;
            if (value is not null && IsFrontlineLabel(value->ToString()))
                return true;
        }

        return false;
    }

    private bool IsFrontlineLabel(string text) =>
        text.Contains("Frontline", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(localizedFrontlineRouletteName) &&
        text.Contains(localizedFrontlineRouletteName, StringComparison.OrdinalIgnoreCase) ||
        IdentifyCampaignText(text) != FrontlineMap.Unknown;

    private static string BuildLocalizedFrontlineRouletteName(IDataManager data)
    {
        var row = data.GetExcelSheet<Lumina.Excel.Sheets.ContentRoulette>()
            .FirstOrDefault(item => item.RowId == DailyFrontlineRouletteId);
        return row.RowId == 0 ? string.Empty : row.Name.ToString();
    }

    private static uint GetLeafCallbackIndex(AddonContentsFinder* addon, int beforeVectorIndex)
    {
        var items = addon->DutyList->Items;
        var limit = Math.Min(beforeVectorIndex, items.Count);
        uint headers = 0;
        for (var index = 0; index < limit; index++)
        {
            var item = items[index].Value;
            if (item is not null && item->UIntValues.Count > 0 && item->UIntValues[0] is 0 or 1)
                headers++;
        }

        return headers + 1;
    }
}
