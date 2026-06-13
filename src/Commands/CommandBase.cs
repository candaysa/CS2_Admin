using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Services;
using CS2_Admin.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace CS2_Admin.Commands;

public abstract class CommandBase : ICommand
{
    protected readonly ISwiftlyCore Core;
    protected readonly PermissionsConfig Permissions;
    protected readonly CommandsConfig CommandsConfig;
    protected readonly TagsConfig Tags;
    protected readonly MessagesConfig Messages;
    protected readonly AdminLogManager AdminLogManager;
    protected readonly PermissionService PermissionService;
    protected readonly AdminDbManager AdminDbManager;
    protected readonly GroupDbManager GroupDbManager;
    protected readonly ChatTagConfigManager ChatTagConfigManager;

    protected CommandBase(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService)
    {
        Core = core;
        Permissions = permissions;
        CommandsConfig = commandsConfig;
        Tags = tags;
        Messages = messages;
        AdminLogManager = adminLogManager;
        PermissionService = permissionService;
        AdminDbManager = null!;
        GroupDbManager = null!;
        ChatTagConfigManager = null!;
    }

    protected CommandBase(
        ISwiftlyCore core,
        PermissionsConfig permissions,
        CommandsConfig commandsConfig,
        TagsConfig tags,
        MessagesConfig messages,
        AdminLogManager adminLogManager,
        PermissionService permissionService,
        AdminDbManager adminDbManager,
        GroupDbManager groupDbManager,
        ChatTagConfigManager chatTagConfigManager)
        : this(core, permissions, commandsConfig, tags, messages, adminLogManager, permissionService)
    {
        AdminDbManager = adminDbManager;
        GroupDbManager = groupDbManager;
        ChatTagConfigManager = chatTagConfigManager;
    }

    public abstract void Execute(ICommandContext context);

    protected string L(string key)
    {
        return LocalizerHelper.Get(Core, key);
    }

    protected string L(string key, params object[] args)
    {
        return LocalizerHelper.Get(Core, key, args);
    }

    protected string FormatTargetName(IReadOnlyList<IPlayer> targets)
    {
        if (targets.Count == 1) return targets[0].Controller.PlayerName;
        return L("target_multiple", targets.Count);
    }

    protected bool HasPerm(ICommandContext context, string permission)
    {
        return PermissionService.HasPermission(context, permission);
    }

    protected bool HasPerm(IPlayer player, string permission)
    {
        return PermissionService.HasPermission(player, permission);
    }

    protected void Reply(ICommandContext context, string key)
    {
        SendReply(context, L(key));
    }

    protected void Reply(ICommandContext context, string key, params object[] args)
    {
        SendReply(context, L(key, args));
    }

    protected void ReplyRaw(ICommandContext context, string message)
    {
        SendReply(context, message);
    }

    private void SendReply(ICommandContext context, string message)
    {
        var prefix = L("prefix");
        var formatted = $" \x02{prefix}\x01 {message}";
        var stripped = StripChatFormatting(message);

        if (context.IsSentByPlayer && context.Sender != null)
        {
            context.Sender.SendChat(formatted);
            context.Sender.SendConsole($"[{prefix}] {stripped}");
            return;
        }

        Core.Logger.LogInformation("[{Prefix}] {Message}", prefix, stripped);
        Console.WriteLine($"[{prefix}] {stripped}");
    }

    private static string StripChatFormatting(string message)
    {
        return message
            .Replace("\x01", string.Empty)
            .Replace("\x02", string.Empty)
            .Replace("\x03", string.Empty)
            .Replace("\x04", string.Empty)
            .Trim();
    }

    protected void Broadcast(string message)
    {
        foreach (var p in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            p.SendChat(message);
        }
    }

    protected void BroadcastNotification(string adminName, string key, params object[] args)
    {
        foreach (var player in Core.PlayerManager.GetAllPlayers().Where(p => p.IsValid))
        {
            var visibleAdmin = ResolveVisibleAdminName(player, adminName);
            var finalArgs = new object[args.Length + 1];
            finalArgs[0] = visibleAdmin;
            Array.Copy(args, 0, finalArgs, 1, args.Length);

            player.SendChat($" \x02{L("prefix")}\x01 {L(key, finalArgs)}");
        }
    }

    protected string ResolveVisibleAdminName(IPlayer viewer, string adminName)
    {
        if (Tags.ShowAdminName)
            return adminName;

        var isAdminViewer = PermissionService.HasPermission(viewer, Permissions.AdminRoot)
            || (!string.IsNullOrWhiteSpace(Permissions.AdminMenu) && PermissionService.HasPermission(viewer, Permissions.AdminMenu))
            || (!string.IsNullOrWhiteSpace(Permissions.ListPlayers) && PermissionService.HasPermission(viewer, Permissions.ListPlayers));

        return isAdminViewer ? adminName : L("admin");
    }

    protected string NormalizeArgsAndGetFirst(string[] args, IReadOnlyList<string> aliases, out string[] remaining)
    {
        remaining = CommandAliasUtils.NormalizeCommandArgs(args, aliases);
        return remaining.Length > 0 ? remaining[0] : string.Empty;
    }

    protected string[] NormalizeArgs(string[] args, IReadOnlyList<string> aliases)
    {
        return CommandAliasUtils.NormalizeCommandArgs(args, aliases);
    }

    protected async Task TryAutoReloadAsync()
    {
        try
        {
            await ChatTagConfigManager.SyncWithGroupsAsync(GroupDbManager);
            ReloadPermissionsConfig();
            await ReloadAdminsAndTagsAsync();
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2_Admin] Auto adminreload failed: {Message}", ex.Message);
        }
    }

    protected void ReloadPermissionsConfig()
    {
        try
        {
            var permissionsPath = Core.Configuration.GetConfigPath("permissions.json");
            if (!File.Exists(permissionsPath))
                return;

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(permissionsPath, optional: false, reloadOnChange: false)
                .Build();

            var section = configuration.GetSection("CS2AdminPermissions");
            if (!section.Exists())
                return;

            section.Bind(Permissions);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2_Admin] Failed to reload permissions.json: {Message}", ex.Message);
        }
    }

    protected async Task<int> ReloadAdminsAndTagsAsync()
    {
        AdminDbManager.ClearCache();
        GroupDbManager.ClearCache();

        var groups = await GroupDbManager.GetAllGroupsAsync();
        var admins = await AdminDbManager.GetAllAdminsAsync();
        var adminsBySteamId = admins.ToDictionary(a => a.SteamId, a => a);

        var managedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var flag in SplitPermissions(group.Flags))
                managedPermissions.Add(flag);
        }

        foreach (var admin in admins)
        {
            foreach (var flag in SplitPermissions(admin.Flags))
                managedPermissions.Add(flag);
        }

        foreach (var bypassPermission in Permissions.RootBypassPermissions)
        {
            if (!string.IsNullOrWhiteSpace(bypassPermission))
                managedPermissions.Add(bypassPermission.Trim());
        }

        var onlinePlayers = Core.PlayerManager
            .GetAllPlayers()
            .Where(p => p.IsValid)
            .Select(p => (p.PlayerID, p.SteamID))
            .ToList();

        var resolvedTags = new Dictionary<int, string>();

        foreach (var snapshot in onlinePlayers)
        {
            foreach (var permission in managedPermissions)
                Core.Permission.RemovePermission(snapshot.SteamID, permission);

            var effectiveFlags = await AdminDbManager.GetEffectiveFlagsAsync(snapshot.SteamID);
            var hasRoot = false;

            foreach (var flag in effectiveFlags)
            {
                if (string.IsNullOrWhiteSpace(flag))
                    continue;

                var normalizedFlag = flag.Trim();
                Core.Permission.AddPermission(snapshot.SteamID, normalizedFlag);
                if (string.Equals(normalizedFlag, Permissions.AdminRoot, StringComparison.OrdinalIgnoreCase))
                    hasRoot = true;
            }

            if (hasRoot)
            {
                foreach (var bypassPermission in Permissions.RootBypassPermissions)
                {
                    if (!string.IsNullOrWhiteSpace(bypassPermission))
                        Core.Permission.AddPermission(snapshot.SteamID, bypassPermission.Trim());
                }
            }

            if (Tags.Enabled)
            {
                adminsBySteamId.TryGetValue(snapshot.SteamID, out var activeAdmin);
                var adminGroupTag = activeAdmin != null && activeAdmin.IsActive
                    ? GroupDbManager.GetPrimaryGroupNameSync(activeAdmin.GroupList) ?? activeAdmin.GroupList.FirstOrDefault()
                    : null;

                if (string.IsNullOrWhiteSpace(adminGroupTag) &&
                    Core.Permission.PlayerHasPermission(snapshot.SteamID, Permissions.AdminRoot))
                {
                    adminGroupTag = "ADMIN";
                }

                resolvedTags[snapshot.PlayerID] = string.IsNullOrWhiteSpace(adminGroupTag)
                    ? Tags.PlayerTag
                    : adminGroupTag;
            }
        }

        Core.Scheduler.NextTick(() =>
        {
            if (!Tags.Enabled)
                return;

            foreach (var pair in resolvedTags)
                PlayerUtils.SetScoreTagReliable(Core, pair.Key, pair.Value);
        });

        return onlinePlayers.Count;
    }

    protected static IEnumerable<string> SplitPermissions(string rawPermissions)
    {
        return rawPermissions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));
    }

    protected bool TryParseGroupsArgument(string rawGroupsArg, out string normalizedGroups, out List<string> groups)
    {
        groups = rawGroupsArg
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => g.Trim().TrimStart('#', '@'))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        normalizedGroups = string.Join(",", groups);
        return groups.Count > 0;
    }
}
