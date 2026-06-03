using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Services;

public class PlayerService
{
    private readonly ISwiftlyCore _core;
    private readonly AdminDbManager _adminDbManager;

    public PlayerService(ISwiftlyCore core, AdminDbManager adminDbManager)
    {
        _core = core;
        _adminDbManager = adminDbManager;
    }

    public IPlayer? FindPlayer(string target)
    {
        return PlayerUtils.FindPlayerByTarget(_core, target);
    }

    public List<IPlayer> FindPlayers(string target, bool includeDeadPlayers = true)
    {
        return PlayerUtils.FindPlayersByTarget(_core, target, includeDeadPlayers);
    }

    public bool IsGroupTarget(string target)
    {
        return PlayerUtils.IsGroupTarget(target);
    }

    public async Task<bool> CanTargetAsync(ICommandContext context, ulong targetSteamId, bool allowSelf = false)
    {
        return await PlayerUtils.CanAdminTargetAsync(_core, _adminDbManager, context, targetSteamId, allowSelf);
    }

    public async Task<bool> CanTargetAsync(IPlayer admin, ulong targetSteamId, bool allowSelf = false)
    {
        return await PlayerUtils.CanAdminTargetAsync(_core, _adminDbManager, admin, targetSteamId, allowSelf);
    }

    public async Task<List<IPlayer>> FilterTargetsByAccessAsync(ICommandContext context, IEnumerable<IPlayer> targets, bool allowSelf = false)
    {
        return await PlayerUtils.FilterTargetsByAccessAsync(_core, _adminDbManager, context, targets, allowSelf);
    }
}
