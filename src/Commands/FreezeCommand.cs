using CS2_Admin.Database;
using CS2_Admin.Utils;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;

using CS2_Admin.Services;
using CS2_Admin.Config;
namespace CS2_Admin.Commands;

public class FreezeCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;
    private readonly HashSet<int> _frozenPlayers = new();
    private readonly HashSet<int> _freezeVisualPlayers = new();
    private readonly Dictionary<int, float> _freezeOriginalViewmodelFov = new();
    private readonly Dictionary<int, (float X, float Y, float Z)> _freezeOriginalViewmodelOffsets = new();

    public FreezeCommand(
        ISwiftlyCore core,
        AdminDbManager adminDbManager,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService) : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override async void Execute(ICommandContext context)
    {
        try
        {
            var args = NormalizeArgs(context.Args, CommandsConfig.Freeze);

            if (!HasPerm(context, Permissions.Freeze))
            {
                Reply(context, "no_permission");
                return;
            }

            if (args.Length < 1)
            {
                Reply(context, "freeze_usage");
                return;
            }

            var targets = PlayerUtils.FindPlayersByTarget(Core, args[0], includeDeadPlayers: false, caller: context.Sender);
            if (targets.Count == 0)
            {
                Reply(context, "no_valid_targets");
                return;
            }

            var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");

            int? durationSeconds = null;
            if (args.Length >= 2 && int.TryParse(args[1], out var parsedSeconds) && parsedSeconds > 0)
            {
                durationSeconds = parsedSeconds;
            }

            foreach (var target in targets)
            {
                var targetSteamId = target.SteamID;
                Core.Scheduler.NextTick(() =>
                {
                    var liveTarget = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == targetSteamId);
                    if (liveTarget?.IsValid != true)
                    {
                        return;
                    }

                    var playerId = liveTarget.PlayerID;
                    PlayerUtils.Freeze(liveTarget);
                    _frozenPlayers.Add(playerId);

                    if (_freezeVisualPlayers.Add(playerId))
                    {
                        StartFreezeVisualPulse(liveTarget.SteamID);
                    }

                    if (durationSeconds.HasValue)
                    {
                        Core.Scheduler.DelayBySeconds(durationSeconds.Value, () =>
                        {
                            Core.Scheduler.NextTick(() =>
                            {
                                var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.PlayerID == playerId);
                                if (player == null)
                                {
                                    return;
                                }

                                if (_frozenPlayers.Contains(playerId))
                                {
                                    PlayerUtils.Unfreeze(player);
                                    _frozenPlayers.Remove(playerId);
                                    _freezeVisualPlayers.Remove(playerId);
                                }
                            });
                        });
                    }
                });
            }

            foreach (var target in targets)
            {
                PlayerUtils.SendNotification(target, Messages,
                    $"<font color='#00ccff'><b>{L("frozen_personal_html")}</b></font><br><br>{L("label_by")}: <font color='#ffcc00'>{ResolveVisibleAdminName(target, adminName)}</font>",
                    $" \x02{L("prefix")}\x01 {L("frozen_personal_chat", ResolveVisibleAdminName(target, adminName))}");
            }

            if (targets.Count > 0)
            {
                BroadcastNotification(adminName, "freeze_notification", FormatTargetName(targets));
            }

            var freezeTargetSteamIds = string.Join(",", targets.Select(t => t.SteamID));
            _ = AdminLogManager.AddLogAsync("freeze", adminName, context.Sender?.SteamID ?? 0, null, null, $"targets={freezeTargetSteamIds};count={targets.Count};duration={durationSeconds?.ToString() ?? "0"}");
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Freeze command failed");
        }
    }

    private void StartFreezeVisualPulse(ulong steamId)
    {
        Core.Scheduler.DelayBySeconds(1.0f, () =>
        {
            var player = Core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (player == null)
            {
                return;
            }

            var playerId = player.PlayerID;
            if (!_frozenPlayers.Contains(playerId))
            {
                _freezeVisualPlayers.Remove(playerId);
                return;
            }

            var pawn = player.PlayerPawn;
            if (pawn?.IsValid != true)
            {
                return;
            }

            if (!_freezeOriginalViewmodelFov.ContainsKey(playerId))
            {
                _freezeOriginalViewmodelFov[playerId] = pawn.ViewmodelFOV > 0 ? pawn.ViewmodelFOV : 68f;
            }

            if (!_freezeOriginalViewmodelOffsets.ContainsKey(playerId))
            {
                _freezeOriginalViewmodelOffsets[playerId] = (pawn.ViewmodelOffsetX, pawn.ViewmodelOffsetY, pawn.ViewmodelOffsetZ);
            }

            pawn.ViewmodelFOV = 40f;
            pawn.ViewmodelFOVUpdated();
            pawn.ViewmodelOffsetX = -10f;
            pawn.ViewmodelOffsetY = -10f;
            pawn.ViewmodelOffsetZ = -10f;

            StartFreezeVisualPulse(steamId);
        });
    }
}


