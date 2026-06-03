using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CS2_Admin.Commands;

public class MoneyCommand : CommandBase
{
    private readonly AdminDbManager _adminDbManager;

    public MoneyCommand(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager)
        : base(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        _adminDbManager = adminDbManager;
    }

    public override void Execute(ICommandContext context)
    {
        var args = NormalizeArgs(context.Args, CommandsConfig.Money);

        if (!HasPerm(context, Permissions.Money))
        {
            Reply(context, "no_permission");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var amount))
        {
            Reply(context, "money_usage");
            return;
        }

        amount = Math.Clamp(amount, 0, 999999);

        var target = PlayerUtils.FindPlayerByTarget(Core, args[0]);
        if (target == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        var canTarget = PlayerUtils.CanAdminTargetAsync(Core, _adminDbManager, context, target.SteamID, allowSelf: true)
            .GetAwaiter().GetResult();
        if (!canTarget)
            return;

        if (target.Controller?.InGameMoneyServices == null)
        {
            Reply(context, "player_not_found");
            return;
        }

        try
        {
            target.Controller.InGameMoneyServices.Account = amount;
            target.Controller.InGameMoneyServices.AccountUpdated();
            target.Controller.InGameMoneyServicesUpdated();
        }
        catch
        {
            try
            {
                target.Controller.InGameMoneyServices.Account = amount;
            }
            catch { }
        }

        var adminName = context.Sender?.Controller.PlayerName ?? L("console_name");
        var targetName = target.Controller.PlayerName;

        BroadcastNotification(adminName, "money_set_notification", targetName, amount);

        PlayerUtils.SendNotification(target, Messages,
            L("money_personal_html", amount),
            $" \x02{L("prefix")}\x01 {L("money_personal_chat", amount)}");

        AdminLogManager.AddLogAsync("money", adminName, context.Sender?.SteamID ?? 0, target.SteamID, target.IPAddress, $"amount={amount}", targetName);
        Core.Logger.LogInformation("[CS2_Admin] {Admin} set money of {Target} to {Amount}", adminName, targetName, amount);
    }
}
