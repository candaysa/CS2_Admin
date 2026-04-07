using CS2_Admin.Commands;
using CS2_Admin.Config;
using CS2_Admin.Database;
using CS2_Admin.Events;
using CS2_Admin.Menu;
using CS2_Admin.Utils;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Translation;
using System.Globalization;
using System.Reflection;
using System.Data;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace CS2_Admin;

[PluginMetadata(Id = "CS2_Admin", Version = "1.0.7", Name = "CS2_Admin", Author = "CanDaysa", Description = "Comprehensive admin plugin for CS2.")]
public partial class CS2_Admin : BasePlugin
{
    private PluginConfig _config = null!;
    public PluginConfig Config => _config;
    public AdminMenuManager AdminMenuManager { get; private set; } = null!;

    // Database managers
    private BanManager _banManager = null!;
    private MuteManager _muteManager = null!;
    private GagManager _gagManager = null!;
    private WarnManager _warnManager = null!;
    private GroupDbManager _groupDbManager = null!;
    private AdminDbManager _adminDbManager = null!;
    private AdminLogManager _adminLogManager = null!;
    private ServerInfoDbManager _serverInfoDbManager = null!;
    private AdminPlaytimeDbManager _adminPlaytimeDbManager = null!;
    private PlayerIpDbManager _playerIpDbManager = null!;

    // Command handlers
    private BanCommands _banCommands = null!;
    private MuteCommands _muteCommands = null!;
    private PlayerCommands _playerCommands = null!;
    private ServerCommands _serverCommands = null!;
    private AdminCommands _adminCommands = null!;
    private ChatCommands _chatCommands = null!;
    private WarnCommands _warnCommands = null!;
    private AdminPlaytimeCommands _adminPlaytimeCommands = null!;

    // Event handlers
    private EventHandlers _eventHandlers = null!;

    // Utils
    private DiscordWebhook _discord = null!;
    private RecentPlayersTracker _recentPlayersTracker = null!;
    private ChatTagConfigManager _chatTagConfigManager = null!;
    private Timer? _adminPlaytimeTimer;
    private int _isTrackingAdminPlaytime;
    private string? _resolvedTranslationDirectory;
    private string? _appliedLocalizerKey;
    private static readonly HashSet<string> BlockedCommandAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "groups"
    };
    private static readonly HashSet<string> RawConCommandCollisionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "say",
        "kick",
        "noclip",
        "give",
        "map",
        "restart",
        "rcon"
    };
    private static readonly object CommandDedupeLock = new();
    private static readonly Dictionary<string, long> RecentCommandExecutions = new(StringComparer.Ordinal);
    private const long CommandDedupeWindowMs = 500;
    private const long CommandDedupeRetentionMs = 10_000;

    public CS2_Admin(ISwiftlyCore core) : base(core)
    {
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void Load(bool hotReload)
    {
        // Load configuration
        LoadConfiguration();
        TryApplyConfiguredLocalizer(_config.Language, force: true);

        Core.Logger.LogInformationIfEnabled("[CS2Admin] Loading plugin...");

        // Initialize database managers
        InitializeDatabaseManagers();

        // Initialize Admin Menu Manager
        AdminMenuManager = new AdminMenuManager(Core, Config, _warnManager, _adminDbManager, _groupDbManager, _adminLogManager, _adminPlaytimeDbManager);

        // Initialize utilities
        _discord = new DiscordWebhook(Core, Config.Discord);
        _adminLogManager.SetDiscordWebhook(_discord);

        // Initialize command handlers
        InitializeCommandHandlers();

        // Initialize event handlers
        InitializeEventHandlers();

        // Register commands
        RegisterCommands();

        // Register events
        RegisterEvents();

        // Initialize databases
        _ = InitializeDatabasesAsync();

        Core.Logger.LogInformationIfEnabled("[CS2Admin] Plugin loaded successfully!");
    }

    public override void Unload()
    {
        Core.Logger.LogInformationIfEnabled("[CS2Admin] Unloading plugin...");
        _eventHandlers?.UnregisterHooks();
        _adminPlaytimeTimer?.Dispose();
    }

    private void LoadConfiguration()
    {
        _config = new PluginConfig();
        _chatTagConfigManager ??= new ChatTagConfigManager(Core);

        EnsureVersionedConfigFile("config.json", "CS2Admin", PluginConfig.CurrentVersion);
        EnsureVersionedConfigFile("commands.json", "CS2AdminCommands", CommandsConfig.CurrentVersion);
        EnsureVersionedConfigFile("permissions.json", "CS2AdminPermissions", PermissionsConfig.CurrentVersion);
        EnsureVersionedConfigFile("maps.json", "CS2AdminMaps", MapsFileConfig.CurrentVersion);

        try
        {
            // Initialize config file with model - this will auto-create config.json if it doesn't exist
            Core.Configuration
                .InitializeJsonWithModel<PluginConfig>("config.json", "CS2Admin")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("config.json"), optional: false, reloadOnChange: true));

            // Bind configuration to our model. Support both:
            // 1) { "CS2Admin": { ... } }
            // 2) { ... }  (root-level keys)
            var pluginSection = Core.Configuration.Manager.GetSection("CS2Admin");
            if (pluginSection.GetChildren().Any())
            {
                pluginSection.Bind(_config);
            }
            else
            {
                Core.Configuration.Manager.Bind(_config);
            }

            // Resolve language defensively from multiple config layouts.
            // Priority: root Language > CS2_Admin.Language > CS2Admin.Language > bound value.
            _config.Language = ResolveConfiguredLanguage(Core.Configuration.GetConfigPath("config.json"), _config.Language);

            ApplyLanguageCulture(_config.Language);
            PluginLocalizer.SetConfiguredPrefix(_config.Messages.Prefix);

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Configuration loaded from {Path}", Core.Configuration.GetConfigPath("config.json"));
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Config language set to: {Language}", _config.Language);
        }
        catch (Exception ex)
        {
            _config.Language = "en";
            ApplyLanguageCulture(_config.Language);
            PluginLocalizer.SetConfiguredPrefix(_config.Messages.Prefix);
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to fully load config.json, continuing with defaults/partial values: {Message}", ex.Message);
        }

        try
        {
            // Load command aliases from a dedicated config file (commands.json)
            Core.Configuration
                .InitializeJsonWithModel<CommandsConfig>("commands.json", "CS2AdminCommands")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("commands.json"), optional: false, reloadOnChange: true));
            var commandsConfig = new CommandsConfig();
            Core.Configuration.Manager.GetSection("CS2AdminCommands").Bind(commandsConfig);
            _config.Commands = commandsConfig;
            EnsureRequiredCommandAliases(_config.Commands);
            EnsureInternalMenuAliases(_config.Commands);
            SanitizeCommandAliases();

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Command aliases loaded from {Path}", Core.Configuration.GetConfigPath("commands.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load commands.json, using command aliases from main/default config: {Message}", ex.Message);
            EnsureRequiredCommandAliases(_config.Commands);
            EnsureInternalMenuAliases(_config.Commands);
            SanitizeCommandAliases();
        }

        try
        {
            // Load permissions from a dedicated config file (permissions.json)
            Core.Configuration
                .InitializeJsonWithModel<PermissionsConfig>("permissions.json", "CS2AdminPermissions")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("permissions.json"), optional: false, reloadOnChange: true));
            var permissionsConfig = new PermissionsConfig();
            Core.Configuration.Manager.GetSection("CS2AdminPermissions").Bind(permissionsConfig);
            _config.Permissions = permissionsConfig;

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Permissions loaded from {Path}", Core.Configuration.GetConfigPath("permissions.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load permissions.json, using default permissions: {Message}", ex.Message);
        }

        try
        {
            Core.Configuration
                .InitializeJsonWithModel<MapsFileConfig>("maps.json", "CS2AdminMaps")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("maps.json"), optional: false, reloadOnChange: true));

            var mapsFileConfig = new MapsFileConfig();
            Core.Configuration.Manager.GetSection("CS2AdminMaps").Bind(mapsFileConfig);
            _config.MapsFile = mapsFileConfig;

            if (mapsFileConfig.Maps.Count > 0)
            {
                _config.GameMaps.Maps = mapsFileConfig.Maps;
            }

            if (mapsFileConfig.WorkshopMaps.Count > 0)
            {
                _config.WorkshopMaps.Maps = mapsFileConfig.WorkshopMaps;
            }

            Core.Logger.LogInformationIfEnabled("[CS2Admin] Maps loaded from {Path}", Core.Configuration.GetConfigPath("maps.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load maps.json, using default maps: {Message}", ex.Message);
        }

        CleanupLegacyCommandsFromConfig();
        DebugSettings.LoggingEnabled = _config.Debug;

        try
        {
            Core.Configuration
                .InitializeJsonWithModel<DiscordFileConfig>("discord.json", "CS2AdminDiscord")
                .Configure(builder => builder.AddJsonFile(Core.Configuration.GetConfigPath("discord.json"), optional: false, reloadOnChange: true));
            var discordConfig = new DiscordFileConfig();
            Core.Configuration.Manager.GetSection("CS2AdminDiscord").Bind(discordConfig);
            _config.Discord = discordConfig;
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Discord webhooks loaded from {Path}", Core.Configuration.GetConfigPath("discord.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load discord.json, using defaults: {Message}", ex.Message);
        }

        try
        {
            _chatTagConfigManager.Load();
            _config.Tags.Enabled = _chatTagConfigManager.Config.ScoreboardEnabled;
            _config.Tags.PlayerTag = string.IsNullOrWhiteSpace(_chatTagConfigManager.Config.PlayerTag) ? "PLAYER" : _chatTagConfigManager.Config.PlayerTag.Trim();
            _config.Tags.ShowAdminName = _chatTagConfigManager.Config.ShowAdminName;
            _config.ShowAdminName = _chatTagConfigManager.Config.ShowAdminName;
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Chat tags loaded from {Path}", Core.Configuration.GetConfigPath("tags.json"));
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to load tags.json, using defaults: {Message}", ex.Message);
        }
    }

    private void EnsureVersionedConfigFile(string fileName, string sectionName, int expectedVersion)
    {
        var filePath = Core.Configuration.GetConfigPath(fileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
            {
                RecreateConfigFile(filePath, fileName, expectedVersion, "root is not a JSON object");
                return;
            }

            if (!TryReadVersionFromNode(root, sectionName, out var currentVersion) || currentVersion != expectedVersion)
            {
                var currentText = TryReadVersionFromNode(root, sectionName, out var parsedVersion)
                    ? parsedVersion.ToString(CultureInfo.InvariantCulture)
                    : "missing";
                RecreateConfigFile(filePath, fileName, expectedVersion, $"found {currentText}");
            }
        }
        catch (Exception ex)
        {
            RecreateConfigFile(filePath, fileName, expectedVersion, ex.Message);
        }
    }

    private void RecreateConfigFile(string filePath, string fileName, int expectedVersion, string reason)
    {
        try
        {
            File.Delete(filePath);
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] {File} version mismatch/corruption ({Reason}). File deleted and will be regenerated with version {Version}.",
                fileName,
                reason,
                expectedVersion);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Failed to delete {File} for version reset: {Message}",
                fileName,
                ex.Message);
        }
    }

    private static bool TryReadVersionFromNode(JsonObject root, string sectionName, out int version)
    {
        version = 0;

        if (TryParseVersionNode(root["Version"], out version))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && root[sectionName] is JsonObject section && TryParseVersionNode(section["Version"], out version))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseVersionNode(JsonNode? node, out int version)
    {
        version = 0;
        if (node == null)
        {
            return false;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                version = intValue;
                return true;
            }

            if (value.TryGetValue<string>(out var textValue) &&
                int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                version = parsed;
                return true;
            }
        }

        return false;
    }

    private void InitializeDatabaseManagers()
    {
        _groupDbManager = new GroupDbManager(Core);
        _banManager = new BanManager(Core);
        _muteManager = new MuteManager(Core);
        _gagManager = new GagManager(Core);
        _warnManager = new WarnManager(Core);
        _adminDbManager = new AdminDbManager(Core, _groupDbManager);
        _adminLogManager = new AdminLogManager(Core);
        _serverInfoDbManager = new ServerInfoDbManager(Core);
        _adminPlaytimeDbManager = new AdminPlaytimeDbManager(Core, _adminDbManager);
        _playerIpDbManager = new PlayerIpDbManager(Core);
        _recentPlayersTracker = new RecentPlayersTracker();
    }

    private void InitializeCommandHandlers()
    {
        _banCommands = new BanCommands(
            Core,
            _banManager,
            _muteManager,
            _gagManager,
            _warnManager,
            _adminDbManager,
            _adminLogManager,
            _playerIpDbManager,
            _recentPlayersTracker,
            _discord,
            _config.Permissions,
            _config.Commands,
            _config.Tags,
            _config.Messages,
            _config.Sanctions,
            _config.MultiServer,
            _config.BanType);
        _muteCommands = new MuteCommands(
            Core, 
            _muteManager, 
            _gagManager, 
            _adminDbManager,
            _adminLogManager,
            _discord, 
            _config.Commands,
            _config.Permissions,
            _config.Tags,
            _config.Permissions.Mute,
            _config.Permissions.Gag,
            _config.Permissions.Silence,
            _config.Permissions.AdminRoot,
            _config.Messages);
        _warnCommands = new WarnCommands(
            Core,
            _warnManager,
            _adminDbManager,
            _adminLogManager,
            _discord,
            _config.Permissions.Warn,
            _config.Permissions.Unwarn,
            _config.Permissions.AdminRoot,
            _config.Messages,
            _config.Sanctions,
            _config.Commands.Warn,
            _config.Commands.Unwarn);
        _playerCommands = new PlayerCommands(Core, _discord, _config.Permissions, _config.Commands, _config.Tags, _config.Messages, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _adminLogManager, _config.MultiServer);
        _serverCommands = new ServerCommands(Core, _adminLogManager, _config.Permissions, _config.GameMaps, _config.WorkshopMaps, _config.Commands);
        _adminCommands = new AdminCommands(Core, _adminDbManager, _groupDbManager, _adminLogManager, _config.Permissions, _config.Tags, _config.Commands, AdminMenuManager, _chatTagConfigManager);
        _chatCommands = new ChatCommands(
            Core,
            _adminLogManager,
            _discord,
            _config.Permissions,
            _config.Tags,
            _config.Messages,
            _config.Commands,
            _config.Sanctions);
        _adminPlaytimeCommands = new AdminPlaytimeCommands(Core, _adminPlaytimeDbManager, _adminLogManager, _discord, _config.Permissions, _config.AdminPlaytime);
    }

    private void InitializeEventHandlers()
    {
        _eventHandlers = new EventHandlers(Core, _banManager, _muteManager, _gagManager, _warnManager, _adminDbManager, _groupDbManager, _playerIpDbManager, _recentPlayersTracker, _config.Permissions, _config.Tags, _config.Commands, _config.MultiServer, _chatTagConfigManager);
        _eventHandlers.SetDatabaseReady(false);
        _eventHandlers.OnPlayerDisconnected += playerId => _playerCommands.OnPlayerDisconnect(playerId);
        _eventHandlers.RegisterHooks();
    }

    private async Task InitializeDatabasesAsync()
    {
        if (!CanConnectToDatabase())
        {
            _eventHandlers.SetDatabaseReady(false);
            return;
        }

        if (!TryRunMigrations("startup-initial"))
        {
            _eventHandlers.SetDatabaseReady(false);
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Initial migration failed. Database-backed features are disabled until migration succeeds.");
            return;
        }

        if (!EnsureRequiredTablesAvailable())
        {
            _eventHandlers.SetDatabaseReady(false);
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Required DB tables are still missing after retry migration. Database-backed features will stay disabled.");
            return;
        }

        await InitializeDatabaseManagersAsync();

        await _chatTagConfigManager.SyncWithGroupsAsync(_groupDbManager);

        _eventHandlers.SetDatabaseReady(true);
        StartAdminPlaytimeTracking();
        await _eventHandlers.RefreshAdminStateForAllOnlinePlayersAsync();

        Core.Logger.LogInformationIfEnabled(
            "[CS2Admin] Server IP: {Ip} Port: {Port}",
            ServerIdentity.GetIp(Core),
            ServerIdentity.GetPort(Core));
    }

    private bool EnsureRequiredTablesAvailable()
    {
        if (ValidateRequiredTables())
        {
            return true;
        }

        Core.Logger.LogWarningIfEnabled(
            "[CS2Admin] Required DB tables are missing after initial migration. Retrying migration once.");
        ResetMigrationVersionState();

        if (!TryRunMigrations("startup-retry"))
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Retry migration failed. Database-backed features will stay disabled.");
            return false;
        }

        return ValidateRequiredTables();
    }

    private void ResetMigrationVersionState()
    {
        try
        {
            using var connection = Core.Database.GetConnection("mysql_detailed");
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            // Execute one-by-one to avoid provider settings that reject batched statements.
            var statements = new[]
            {
                "DROP TABLE IF EXISTS `VersionInfoMetaData`;",
                "DROP TABLE IF EXISTS `VersionInfo`;",
                "DROP TABLE IF EXISTS `versioninfometadata`;",
                "DROP TABLE IF EXISTS `versioninfo`;"
            };

            foreach (var statement in statements)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = statement;
                _ = cmd.ExecuteNonQuery();
            }
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Migration version state reset due to missing required tables.");
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to reset migration version state: {Message}", ex.Message);
        }
    }

    private async Task InitializeDatabaseManagersAsync()
    {
        await _groupDbManager.InitializeAsync();
        await _banManager.InitializeAsync();
        await _muteManager.InitializeAsync();
        await _gagManager.InitializeAsync();
        await _warnManager.InitializeAsync();
        await _adminDbManager.InitializeAsync();
        await _adminLogManager.InitializeAsync();
        await _serverInfoDbManager.InitializeAsync();
        await _adminPlaytimeDbManager.InitializeAsync();
        await _playerIpDbManager.InitializeAsync();
    }

    private bool TryRunMigrations(string source)
    {
        try
        {
            using var connection = Core.Database.GetConnection("mysql_detailed");
            // Do not open the connection here. Some providers redact password
            // from ConnectionString after Open(), which breaks FluentMigrator.
            MigrationRunner.RunMigrations(connection);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Database migration completed ({Source}).", source);
            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Database migration failed ({Source}): {Message}", source, ex.Message);
            return false;
        }
    }

    private bool CanConnectToDatabase()
    {
        try
        {
            using var connection = Core.Database.GetConnection("mysql_detailed");
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Database connection is unavailable. Plugin will continue with limited functionality. Details: {Message}",
                ex.Message);
            return false;
        }
    }

    private bool ValidateRequiredTables()
    {
        var requiredTables = new[]
        {
            "admin_admins",
            "admin_groups",
            "admin_bans",
            "admin_mutes",
            "admin_gags",
            "admin_warns",
            "admin_log",
            "admin_servers",
            "admin_playtime",
            "admin_player_ips",
            "admin_player_ip_history"
        };

        try
        {
            using var connection = Core.Database.GetConnection("mysql_detailed");
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            foreach (var tableName in requiredTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT 1 FROM `{tableName}` LIMIT 1";
                _ = cmd.ExecuteScalar();
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled(
                "[CS2Admin] Database schema validation failed: {Message}",
                ex.Message);
            return false;
        }
    }

    private void ApplyLanguageCulture(string language)
    {
        var cultureName = language.ToLowerInvariant() switch
        {
            "tr" => "tr-TR",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "it" => "it-IT",
            "el" => "el-GR",
            "ru" => "ru-RU",
            "bg" => "bg-BG",
            "hu" => "hu-HU",
            _ => "en-US"
        };

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to apply language culture '{Culture}': {Message}", cultureName, ex.Message);
        }
    }

    private string ResolveConfiguredLanguage(string configPath, string? fallbackLanguage)
    {
        var fromRoot = Core.Configuration.Manager["Language"];
        if (!string.IsNullOrWhiteSpace(fromRoot))
        {
            return NormalizeSupportedLanguage(fromRoot);
        }

        var fromAltSection = Core.Configuration.Manager["CS2_Admin:Language"];
        if (!string.IsNullOrWhiteSpace(fromAltSection))
        {
            return NormalizeSupportedLanguage(fromAltSection);
        }

        var fromMainSection = Core.Configuration.Manager["CS2Admin:Language"];
        if (!string.IsNullOrWhiteSpace(fromMainSection))
        {
            return NormalizeSupportedLanguage(fromMainSection);
        }

        try
        {
            if (File.Exists(configPath))
            {
                var raw = File.ReadAllText(configPath);
                var node = JsonNode.Parse(raw) as JsonObject;
                if (node != null)
                {
                    if (node["Language"]?.GetValue<string>() is { } rawRoot && !string.IsNullOrWhiteSpace(rawRoot))
                    {
                        return NormalizeSupportedLanguage(rawRoot);
                    }

                    if (node["CS2_Admin"] is JsonObject cs2AdminAlt
                        && cs2AdminAlt["Language"]?.GetValue<string>() is { } rawAlt
                        && !string.IsNullOrWhiteSpace(rawAlt))
                    {
                        return NormalizeSupportedLanguage(rawAlt);
                    }

                    if (node["CS2Admin"] is JsonObject cs2Admin
                        && cs2Admin["Language"]?.GetValue<string>() is { } rawSection
                        && !string.IsNullOrWhiteSpace(rawSection))
                    {
                        return NormalizeSupportedLanguage(rawSection);
                    }
                }
            }
        }
        catch
        {
            // Keep fallback language when raw parsing fails.
        }

        return NormalizeSupportedLanguage(fallbackLanguage);
    }

    private static string NormalizeSupportedLanguage(string? language)
    {
        var raw = (language ?? "en").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "en";
        }

        static string? MapLanguageToken(string token)
        {
            return token.Trim().ToLowerInvariant() switch
            {
                "en" or "english" => "en",
                "tr" or "turkish" or "turkce" => "tr",
                "de" or "german" or "deutsch" => "de",
                "fr" or "french" or "francais" => "fr",
                "it" or "italian" or "italiano" => "it",
                "el" or "greek" => "el",
                "ru" or "russian" => "ru",
                "bg" or "bulgarian" => "bg",
                "hu" or "hungarian" or "magyar" => "hu",
                _ => null
            };
        }

        // First try the full raw value, then each split token.
        var mapped = MapLanguageToken(raw);
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        var tokens = raw.Split(['-', '_', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            mapped = MapLanguageToken(token);
            if (!string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }
        }

        return "en";
    }

    private void TryApplyConfiguredLocalizer(string language, bool force = false)
    {
        try
        {
            var requestedLanguage = NormalizeSupportedLanguage(language);
            var effectiveLanguage = requestedLanguage;

            if (!TryResolveTranslationCandidate(requestedLanguage, out var resourceDir, out var fileLocalizer))
            {
                if (!string.Equals(requestedLanguage, "en", StringComparison.OrdinalIgnoreCase)
                    && TryResolveTranslationCandidate("en", out resourceDir, out fileLocalizer))
                {
                    Core.Logger.LogWarningIfEnabled(
                        "[CS2Admin] Translation file for requested language '{RequestedLanguage}' could not be resolved. Falling back to English.",
                        requestedLanguage);
                    effectiveLanguage = "en";
                }
                else
                {
                    Core.Logger.LogWarningIfEnabled(
                        "[CS2Admin] Translation resources are unavailable. Requested language was '{RequestedLanguage}'.",
                        requestedLanguage);
                    return;
                }
            }

            var localizerKey = $"{requestedLanguage}|{effectiveLanguage}|{resourceDir}";
            if (!force && string.Equals(_appliedLocalizerKey, localizerKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PluginLocalizer.SetOverride(fileLocalizer);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Plugin localizer override loaded from: {Path}", resourceDir);
            var diag = fileLocalizer.Diagnostics;
            Core.Logger.LogInformationIfEnabled(
                "[CS2Admin] Translation coverage (requested={RequestedLanguage}, effective={EffectiveLanguage}): primaryKeys={PrimaryKeys}, fallbackKeys={FallbackKeys}, missingInPrimary={MissingCount} ({MissingRate:P1}), sameAsEnglish={SameCount} ({SameRate:P1}), sample={MissingSample}",
                requestedLanguage,
                effectiveLanguage,
                diag.PrimaryKeyCount,
                diag.FallbackKeyCount,
                diag.MissingPrimaryCount,
                diag.MissingPrimaryRate,
                diag.SameAsFallbackCount,
                diag.SameAsFallbackRate,
                diag.GetMissingSample());

            var coreAssembly = typeof(ISwiftlyCore).Assembly;
            var translationFactoryType = coreAssembly.GetType("SwiftlyS2.Core.Translations.TranslationFactory");
            var createResourceMethod = translationFactoryType?.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var createLocalizerMethod = translationFactoryType?.GetMethod("CreateLocalizer", BindingFlags.Public | BindingFlags.Static);
            if (createResourceMethod == null || createLocalizerMethod == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Translation factory methods were not found in Swiftly runtime.");
                return;
            }

            var translationResource = createResourceMethod.Invoke(null, new object[] { resourceDir });
            if (translationResource == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to create translation resource from {Path}", resourceDir);
                return;
            }

            var swiftLanguage = ToSwiftLanguage(effectiveLanguage);
            var localizer = createLocalizerMethod.Invoke(null, new object[] { translationResource, swiftLanguage });
            if (localizer == null)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to create localizer for language {Language}", effectiveLanguage);
                return;
            }

            var coreType = Core.GetType();
            var localizerProperty = coreType.GetProperty("Localizer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (localizerProperty?.CanWrite != true)
            {
                var localizerField = coreType.GetField("<Localizer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (localizerField != null)
                {
                    localizerField.SetValue(Core, localizer);
                }
                else
                {
                    Core.Logger.LogWarningIfEnabled("[CS2Admin] PluginLocalizer.Get(Core) property is not writable in current Swiftly runtime.");
                }
            }
            else
            {
                localizerProperty.SetValue(Core, localizer);
            }

            _resolvedTranslationDirectory = resourceDir;
            _appliedLocalizerKey = localizerKey;
            Core.Logger.LogInformationIfEnabled(
                "[CS2Admin] Runtime localizer forced. Requested={RequestedLanguage}, Effective={EffectiveLanguage}",
                requestedLanguage,
                effectiveLanguage);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Localizer probe menu_admin_title: {Text}", PluginLocalizer.Get(Core)["menu_admin_title"]);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to force runtime localizer to '{Language}': {Message}", language, ex.Message);
        }
    }

    private bool TryResolveTranslationCandidate(string language, out string resourceDir, out JsonFileLocalizer fileLocalizer)
    {
        var normalizedLanguage = NormalizeSupportedLanguage(language);
        var requestedFile = $"{normalizedLanguage}.jsonc";
        resourceDir = string.Empty;
        fileLocalizer = null!;

        foreach (var candidate in EnumerateTranslationDirectoryCandidates())
        {
            if (!TryCreateLocalizerFromDirectory(candidate, requestedFile, normalizedLanguage, out var localizer))
            {
                continue;
            }

            // If stale user-override file is effectively English, prefer bundled resources.
            if (!string.Equals(normalizedLanguage, "en", StringComparison.OrdinalIgnoreCase)
                && IsLikelyStaleEnglishOverride(candidate, localizer.Diagnostics))
            {
                Core.Logger.LogWarningIfEnabled(
                    "[CS2Admin] Skipping stale translation override candidate {Path} for '{Language}' because it is almost identical to English ({SameRate:P1}).",
                    candidate,
                    normalizedLanguage,
                    localizer.Diagnostics.SameAsFallbackRate);
                continue;
            }

            if (IsTranslationSchemaMismatch(normalizedLanguage, localizer.Diagnostics))
            {
                Core.Logger.LogWarningIfEnabled(
                    "[CS2Admin] Ignoring translation candidate {Path} for '{Language}' because it does not contain matching plugin keys (fallbackKeys={FallbackKeys}, missing={Missing}).",
                    candidate,
                    normalizedLanguage,
                    localizer.Diagnostics.FallbackKeyCount,
                    localizer.Diagnostics.MissingPrimaryCount);
                continue;
            }

            resourceDir = candidate;
            fileLocalizer = localizer;
            return true;
        }

        var extracted = ExtractEmbeddedTranslationsToConfigDirectory(overwriteExisting: false);
        if (!string.IsNullOrWhiteSpace(extracted)
            && TryCreateLocalizerFromDirectory(extracted, requestedFile, normalizedLanguage, out var extractedLocalizer)
            && !IsTranslationSchemaMismatch(normalizedLanguage, extractedLocalizer.Diagnostics))
        {
            resourceDir = Path.GetFullPath(extracted);
            fileLocalizer = extractedLocalizer;
            return true;
        }

        return false;
    }

    private bool IsLikelyStaleEnglishOverride(string candidatePath, LocalizerDiagnostics diagnostics)
    {
        var overrideTranslationsPath = GetConfigTranslationsDirectoryPath();
        string fullCandidate;
        string fullOverridePath;
        try
        {
            fullCandidate = Path.GetFullPath(candidatePath);
            fullOverridePath = Path.GetFullPath(overrideTranslationsPath);
        }
        catch
        {
            return false;
        }

        if (!fullCandidate.StartsWith(fullOverridePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return diagnostics.FallbackKeyCount > 0 && diagnostics.SameAsFallbackRate >= 0.98;
    }

    private IEnumerable<string> EnumerateTranslationDirectoryCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedCandidates = new List<string>
        {
            // Always prefer server-side/user override translations from plugin config.
            GetConfigTranslationsDirectoryPath(),
            Path.Combine(Core.PluginPath, "translations")
        };

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            orderedCandidates.Add(Path.Combine(assemblyDir, "translations"));
        }

        foreach (var candidate in orderedCandidates)
        {
            string? full = null;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore invalid path candidates.
                continue;
            }

            if (!string.IsNullOrWhiteSpace(full) && seen.Add(full))
            {
                yield return full;
            }
        }
    }

    private static bool IsTranslationSchemaMismatch(string language, LocalizerDiagnostics diagnostics)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (diagnostics.FallbackKeyCount <= 0)
        {
            return true;
        }

        var matchedPrimaryKeys = diagnostics.FallbackKeyCount - diagnostics.MissingPrimaryCount;
        if (matchedPrimaryKeys <= 0)
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateLocalizerFromDirectory(
        string directoryPath,
        string requestedFile,
        string normalizedLanguage,
        out JsonFileLocalizer localizer)
    {
        localizer = null!;

        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(directoryPath, "en.jsonc")))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(directoryPath, requestedFile)))
        {
            return false;
        }

        var created = JsonFileLocalizer.TryCreate(directoryPath, normalizedLanguage);
        if (created == null)
        {
            return false;
        }

        localizer = created;
        return true;
    }

    private string? ExtractEmbeddedTranslationsToConfigDirectory(bool overwriteExisting = false)
    {
        try
        {
            var outputDir = GetConfigTranslationsDirectoryPath();
            Directory.CreateDirectory(outputDir);

            var asm = Assembly.GetExecutingAssembly();
            var resources = asm.GetManifestResourceNames()
                .Where(x => x.StartsWith("CS2_Admin.Translations.", StringComparison.OrdinalIgnoreCase)
                            && x.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (resources.Count == 0)
            {
                Core.Logger.LogWarningIfEnabled("[CS2Admin] No embedded translation resources were found in assembly.");
                return null;
            }

            var writtenCount = 0;
            var skippedCount = 0;
            foreach (var resourceName in resources)
            {
                var fileName = resourceName["CS2_Admin.Translations.".Length..];
                var destinationPath = Path.Combine(outputDir, fileName);
                if (!overwriteExisting && File.Exists(destinationPath))
                {
                    skippedCount++;
                    continue;
                }

                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();
                File.WriteAllText(destinationPath, content, Encoding.UTF8);
                writtenCount++;
            }

            Core.Logger.LogInformationIfEnabled(
                "[CS2Admin] Embedded translations synchronized to: {Path} (written={Written}, skipped={Skipped}, overwrite={Overwrite})",
                outputDir,
                writtenCount,
                skippedCount,
                overwriteExisting);
            return outputDir;
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to extract embedded translations: {Message}", ex.Message);
            return null;
        }
    }

    private string GetConfigTranslationsDirectoryPath()
    {
        var configPath = Core.Configuration.GetConfigPath("config.json");
        var configDir = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDir))
        {
            return Path.Combine(Core.PluginDataDirectory, "translations");
        }

        return Path.Combine(configDir, "translations");
    }

    private static Language ToSwiftLanguage(string language)
    {
        return (language ?? "en").Trim().ToLowerInvariant() switch
        {
            "tr" => Language.Turkish,
            "de" => Language.German,
            "fr" => Language.French,
            "it" => Language.Italian,
            "el" => Language.Greek,
            "ru" => Language.Russian,
            "bg" => Language.Bulgarian,
            "hu" => Language.Hungarian,
            _ => Language.English
        };
    }

    private void RegisterCommands()
    {
        // Admin root/menu commands
        foreach (var cmd in _config.Commands.AdminRoot)
            RegisterCommand(cmd, _adminCommands.OnAdminRootCommand);
        foreach (var cmd in _config.Commands.AdminMenu)
            RegisterCommand(cmd, _adminCommands.OnAdminRootCommand);

        // Admin communication commands
        foreach (var cmd in _config.Commands.Asay)
            RegisterCommand(cmd, _chatCommands.OnAsayCommand);
        foreach (var cmd in _config.Commands.Say)
            RegisterCommand(cmd, _chatCommands.OnSayCommand);
        foreach (var cmd in _config.Commands.Psay)
            RegisterCommand(cmd, _chatCommands.OnPsayCommand);
        foreach (var cmd in _config.Commands.Csay)
            RegisterCommand(cmd, _chatCommands.OnCsayCommand);
        foreach (var cmd in _config.Commands.Hsay)
            RegisterCommand(cmd, _chatCommands.OnHsayCommand);
        foreach (var cmd in _config.Commands.CallAdmin)
            RegisterCommand(cmd, _chatCommands.OnCallAdminCommand);
        foreach (var cmd in _config.Commands.Report)
            RegisterCommand(cmd, _chatCommands.OnReportCommand);
        foreach (var cmd in _config.Commands.AdminTime)
            RegisterCommand(cmd, _adminPlaytimeCommands.OnAdminTimeCommand);
        foreach (var cmd in _config.Commands.AdminTimeSend)
            RegisterCommand(cmd, _adminPlaytimeCommands.OnAdminTimeSendCommand);

        // Ban commands
        foreach (var cmd in _config.Commands.Ban)
            RegisterCommand(cmd, _banCommands.OnBanCommand);
        foreach (var cmd in _config.Commands.IpBan)
            RegisterCommand(cmd, _banCommands.OnIpBanCommand);
        foreach (var cmd in _config.Commands.LastBan)
            RegisterCommand(cmd, _banCommands.OnLastBanCommand);
        foreach (var cmd in _config.Commands.AddBan)
            RegisterCommand(cmd, _banCommands.OnAddBanCommand);
        foreach (var cmd in _config.Commands.Unban)
            RegisterCommand(cmd, _banCommands.OnUnbanCommand);
        foreach (var cmd in _config.Commands.Warn)
            RegisterCommand(cmd, _warnCommands.OnWarnCommand);
        foreach (var cmd in _config.Commands.Unwarn)
            RegisterCommand(cmd, _warnCommands.OnUnwarnCommand);

        // Mute/Gag commands
        foreach (var cmd in _config.Commands.Mute)
            RegisterCommand(cmd, _muteCommands.OnMuteCommand);
        foreach (var cmd in _config.Commands.Unmute)
            RegisterCommand(cmd, _muteCommands.OnUnmuteCommand);
        foreach (var cmd in _config.Commands.Gag)
            RegisterCommand(cmd, _muteCommands.OnGagCommand);
        foreach (var cmd in _config.Commands.Ungag)
            RegisterCommand(cmd, _muteCommands.OnUngagCommand);
        foreach (var cmd in _config.Commands.Silence)
            RegisterCommand(cmd, _muteCommands.OnSilenceCommand);
        foreach (var cmd in _config.Commands.Unsilence)
            RegisterCommand(cmd, _muteCommands.OnUnsilenceCommand);

        // Player commands
        foreach (var cmd in _config.Commands.Kick)
            RegisterCommand(cmd, _playerCommands.OnKickCommand);
        foreach (var cmd in _config.Commands.Slap)
            RegisterCommand(cmd, _playerCommands.OnSlapCommand);
        foreach (var cmd in _config.Commands.Slay)
            RegisterCommand(cmd, _playerCommands.OnSlayCommand);
        foreach (var cmd in _config.Commands.Respawn)
            RegisterCommand(cmd, _playerCommands.OnRespawnCommand);
        foreach (var cmd in _config.Commands.ChangeTeam)
            RegisterCommand(cmd, _playerCommands.OnTeamCommand);
        foreach (var cmd in _config.Commands.NoClip)
            RegisterCommand(cmd, _playerCommands.OnNoclipCommand);
        foreach (var cmd in _config.Commands.Goto)
            RegisterCommand(cmd, _playerCommands.OnGotoCommand);
        foreach (var cmd in _config.Commands.Bring)
            RegisterCommand(cmd, _playerCommands.OnBringCommand);
        foreach (var cmd in _config.Commands.Freeze)
            RegisterCommand(cmd, _playerCommands.OnFreezeCommand);
        foreach (var cmd in _config.Commands.Unfreeze)
            RegisterCommand(cmd, _playerCommands.OnUnfreezeCommand);
        foreach (var cmd in _config.Commands.Resize)
            RegisterCommand(cmd, _playerCommands.OnResizeCommand);
        foreach (var cmd in _config.Commands.Drug)
            RegisterCommand(cmd, _playerCommands.OnDrugCommand);
        foreach (var cmd in _config.Commands.Beacon)
            RegisterCommand(cmd, _playerCommands.OnBeaconCommand);
        foreach (var cmd in _config.Commands.Burn)
            RegisterCommand(cmd, _playerCommands.OnBurnCommand);
        foreach (var cmd in _config.Commands.Disarm)
            RegisterCommand(cmd, _playerCommands.OnDisarmCommand);
        foreach (var cmd in _config.Commands.Speed)
            RegisterCommand(cmd, _playerCommands.OnSpeedCommand);
        foreach (var cmd in _config.Commands.Gravity)
            RegisterCommand(cmd, _playerCommands.OnGravityCommand);
        foreach (var cmd in _config.Commands.Rename)
            RegisterCommand(cmd, _playerCommands.OnRenameCommand);
        foreach (var cmd in _config.Commands.Hp)
            RegisterCommand(cmd, _playerCommands.OnHpCommand);
        foreach (var cmd in _config.Commands.Money)
            RegisterCommand(cmd, _playerCommands.OnMoneyCommand);
        foreach (var cmd in _config.Commands.Give)
            RegisterCommand(cmd, _playerCommands.OnGiveCommand);
        foreach (var cmd in _config.Commands.Who)
            RegisterCommand(cmd, _playerCommands.OnWhoCommand);

        // Server commands
        foreach (var cmd in _config.Commands.ChangeMap)
            RegisterCommand(cmd, _serverCommands.OnMapCommand);
        foreach (var cmd in _config.Commands.ChangeWSMap)
            RegisterCommand(cmd, _serverCommands.OnWSMapCommand);
        foreach (var cmd in _config.Commands.RestartGame)
            RegisterCommand(cmd, _serverCommands.OnRestartCommand);
        foreach (var cmd in _config.Commands.HeadshotOn)
            RegisterCommand(cmd, _serverCommands.OnHeadshotOnCommand);
        foreach (var cmd in _config.Commands.HeadshotOff)
            RegisterCommand(cmd, _serverCommands.OnHeadshotOffCommand);
        foreach (var cmd in _config.Commands.BunnyOn)
            RegisterCommand(cmd, _serverCommands.OnBunnyOnCommand);
        foreach (var cmd in _config.Commands.BunnyOff)
            RegisterCommand(cmd, _serverCommands.OnBunnyOffCommand);
        foreach (var cmd in _config.Commands.RespawnOn)
            RegisterCommand(cmd, _serverCommands.OnRespawnOnCommand);
        foreach (var cmd in _config.Commands.RespawnOff)
            RegisterCommand(cmd, _serverCommands.OnRespawnOffCommand);
        foreach (var cmd in _config.Commands.Rcon)
            RegisterCommand(cmd, _serverCommands.OnRconCommand);
        foreach (var cmd in _config.Commands.Cvar)
            RegisterCommand(cmd, _serverCommands.OnCvarCommand);
        foreach (var cmd in _config.Commands.Vote)
            RegisterCommand(cmd, _serverCommands.OnVoteCommand);

        // Admin commands
        foreach (var cmd in _config.Commands.AddAdmin)
            RegisterCommand(cmd, _adminCommands.OnAddAdminCommand);
        foreach (var cmd in _config.Commands.EditAdmin)
            RegisterCommand(cmd, _adminCommands.OnEditAdminCommand);
        foreach (var cmd in _config.Commands.RemoveAdmin)
            RegisterCommand(cmd, _adminCommands.OnRemoveAdminCommand);
        foreach (var cmd in _config.Commands.ListAdmins)
            RegisterCommand(cmd, _adminCommands.OnListAdminsCommand);
        foreach (var cmd in _config.Commands.AddGroup)
            RegisterCommand(cmd, _adminCommands.OnAddGroupCommand);
        foreach (var cmd in _config.Commands.EditGroup)
            RegisterCommand(cmd, _adminCommands.OnEditGroupCommand);
        foreach (var cmd in _config.Commands.RemoveGroup)
            RegisterCommand(cmd, _adminCommands.OnRemoveGroupCommand);
        foreach (var cmd in _config.Commands.ListGroups)
            RegisterCommand(cmd, _adminCommands.OnListGroupsCommand);
        foreach (var cmd in _config.Commands.AdminReload)
            RegisterCommand(cmd, _adminCommands.OnAdminReloadCommand);
    }

    private void RegisterCommand(string name, ICommandService.CommandListener handler)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var commandName = name.Trim();
        var wrappedHandler = (ICommandService.CommandListener)(context =>
        {
            var ctxCommandName = context.CommandName ?? string.Empty;
            if (!context.IsSentByPlayer && !ctxCommandName.StartsWith("sw_", StringComparison.OrdinalIgnoreCase))
            {
                // Keep unprefixed commands usable in chat/player context,
                // but block direct server-console execution unless `sw_` is used.
                return;
            }

            if (ShouldSuppressDuplicateInvocation(context, ctxCommandName))
            {
                return;
            }

            handler(context);
        });

        // Avoid colliding with engine-provided ConCommands (kick/map/rcon/...) on raw registration.
        if (!RawConCommandCollisionAliases.Contains(commandName))
        {
            TryRegisterCommand(commandName, wrappedHandler);
        }

        var swAlias = CommandAliasUtils.ToSwAlias(commandName);
        if (!string.Equals(swAlias, commandName, StringComparison.OrdinalIgnoreCase))
        {
            TryRegisterCommand(swAlias, wrappedHandler);
        }
    }

    private void TryRegisterCommand(string name, ICommandService.CommandListener handler)
    {
        if (Core.Command.IsCommandRegistered(name))
        {
            return;
        }

        Core.Command.RegisterCommand(name, handler, registerRaw: true);
    }

    private void EnsureRequiredCommandAliases(CommandsConfig commands)
    {
        if (commands.Beacon == null || commands.Beacon.Count == 0)
        {
            commands.Beacon = ["beacon"];
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Commands.Beacon alias list was empty. Restored default alias: beacon");
        }
    }

    private void EnsureInternalMenuAliases(CommandsConfig commands)
    {
        // Keep legacy aliases for compatibility, but prefer namespaced aliases to avoid collisions
        // with other admin plugins that register the same `sw_*` commands.
        EnsurePreferredAlias(commands.Slap, "cs2a_slap");
        EnsurePreferredAlias(commands.Slay, "cs2a_slay");
        EnsurePreferredAlias(commands.Respawn, "cs2a_respawn");
        EnsurePreferredAlias(commands.ChangeTeam, "cs2a_team");
        EnsurePreferredAlias(commands.NoClip, "cs2a_noclip");
        EnsurePreferredAlias(commands.Goto, "cs2a_goto");
        EnsurePreferredAlias(commands.Bring, "cs2a_bring");
        EnsurePreferredAlias(commands.Freeze, "cs2a_freeze");
        EnsurePreferredAlias(commands.Unfreeze, "cs2a_unfreeze");
        EnsurePreferredAlias(commands.Resize, "cs2a_resize");
        EnsurePreferredAlias(commands.Drug, "cs2a_drug");
        EnsurePreferredAlias(commands.Beacon, "cs2a_beacon");
        EnsurePreferredAlias(commands.Burn, "cs2a_burn");
        EnsurePreferredAlias(commands.Disarm, "cs2a_disarm");
        EnsurePreferredAlias(commands.Speed, "cs2a_speed");
        EnsurePreferredAlias(commands.Gravity, "cs2a_gravity");
        EnsurePreferredAlias(commands.Hp, "cs2a_hp");
        EnsurePreferredAlias(commands.Money, "cs2a_money");
        EnsurePreferredAlias(commands.Give, "cs2a_give");
        EnsurePreferredAlias(commands.ChangeMap, "cs2a_map");
        EnsurePreferredAlias(commands.ChangeWSMap, "cs2a_wsmap");
        EnsurePreferredAlias(commands.RestartGame, "cs2a_restart");
        EnsurePreferredAlias(commands.HeadshotOn, "cs2a_hson");
        EnsurePreferredAlias(commands.HeadshotOff, "cs2a_hsoff");
        EnsurePreferredAlias(commands.BunnyOn, "cs2a_bunnyon");
        EnsurePreferredAlias(commands.BunnyOff, "cs2a_bunnyoff");
        EnsurePreferredAlias(commands.RespawnOn, "cs2a_respawnon");
        EnsurePreferredAlias(commands.RespawnOff, "cs2a_respawnoff");
    }

    private static void EnsurePreferredAlias(List<string>? aliases, string preferredAlias)
    {
        if (aliases == null)
        {
            return;
        }

        aliases.RemoveAll(x => string.Equals(x?.Trim(), preferredAlias, StringComparison.OrdinalIgnoreCase));
        aliases.Insert(0, preferredAlias);
    }

    private bool ShouldSuppressDuplicateInvocation(ICommandContext context, string rawCommandName)
    {
        var normalizedCommand = rawCommandName.Trim().ToLowerInvariant();
        if (normalizedCommand.StartsWith("sw_", StringComparison.Ordinal))
        {
            normalizedCommand = normalizedCommand[3..];
        }

        var args = context.Args
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        if (args.Count > 0)
        {
            var first = args[0].TrimStart('!', '/').ToLowerInvariant();
            if (first == normalizedCommand || first == $"sw_{normalizedCommand}")
            {
                args.RemoveAt(0);
            }
        }

        var senderKey = context.Sender?.SteamID.ToString(CultureInfo.InvariantCulture)
            ?? (context.IsSentByPlayer ? "player" : "console");

        var key = $"{senderKey}|{normalizedCommand}|{string.Join(' ', args).ToLowerInvariant()}";
        var now = Environment.TickCount64;

        lock (CommandDedupeLock)
        {
            if (RecentCommandExecutions.TryGetValue(key, out var lastTick) && now - lastTick <= CommandDedupeWindowMs)
            {
                Core.Logger.LogInformationIfEnabled("[CS2Admin] Suppressed duplicate command invocation: {Command} {Args}", normalizedCommand, string.Join(' ', args));
                return true;
            }

            RecentCommandExecutions[key] = now;

            if (RecentCommandExecutions.Count > 1024)
            {
                var staleKeys = RecentCommandExecutions
                    .Where(pair => now - pair.Value > CommandDedupeRetentionMs)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var stale in staleKeys)
                {
                    RecentCommandExecutions.Remove(stale);
                }
            }
        }

        return false;
    }

    private void SanitizeCommandAliases()
    {
        var commands = _config.Commands;
        foreach (var prop in typeof(CommandsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(List<string>))
            {
                continue;
            }

            var aliases = prop.GetValue(commands) as List<string>;
            if (aliases == null || aliases.Count == 0)
            {
                continue;
            }

            var blockedRemoved = aliases.Count(x => !string.IsNullOrWhiteSpace(x) && BlockedCommandAliases.Contains(x.Trim()));
            var cleaned = aliases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !BlockedCommandAliases.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (blockedRemoved > 0)
            {
                Core.Logger.LogWarningIfEnabled(
                    "[CS2Admin] Removed blocked command alias(es) from {Property}. Final: {Aliases}",
                    prop.Name,
                    string.Join(", ", cleaned));
            }

            prop.SetValue(commands, cleaned);
        }
    }

    private void CleanupLegacyCommandsFromConfig()
    {
        try
        {
            var configPath = Core.Configuration.GetConfigPath("config.json");
            if (!File.Exists(configPath))
            {
                return;
            }

            var json = File.ReadAllText(configPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root?["CS2Admin"] is not JsonObject pluginSection)
            {
                return;
            }

            if (!pluginSection.Remove("Commands"))
            {
                return;
            }

            var rewritten = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, rewritten);
            Core.Logger.LogInformationIfEnabled("[CS2Admin] Removed legacy Commands block from config.json; commands are now managed by commands.json.");
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to cleanup legacy Commands block from config.json: {Message}", ex.Message);
        }
    }

    private void RegisterEvents()
    {
        Core.Event.OnClientSteamAuthorize += _eventHandlers.OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected += _eventHandlers.OnClientDisconnected;

        Core.GameEvent.HookPost<EventRoundStart>(_eventHandlers.OnRoundStart);
        Core.GameEvent.HookPost<EventRoundStart>(OnRoundStartEnsureCommands);
    }

    private HookResult OnRoundStartEnsureCommands(EventRoundStart @event)
    {
        EnsureCommandsRegistered();
        return HookResult.Continue;
    }

    private void EnsureCommandsRegistered()
    {
        try
        {
            var probe = _config?.Commands?.AdminMenu?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(probe) && Core.Command.IsCommandRegistered(probe))
                return;

            RegisterCommands();
        }
        catch (Exception ex)
        {
            Core.Logger.LogErrorIfEnabled("[CS2Admin] Failed while ensuring commands are registered: {Message}", ex.Message);
        }
    }

    private void StartAdminPlaytimeTracking()
    {
        var intervalMinutes = Math.Max(1, _config.AdminPlaytime.TrackIntervalMinutes);
        _adminPlaytimeTimer?.Dispose();

        _adminPlaytimeTimer = new Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref _isTrackingAdminPlaytime, 1) == 1)
                {
                    return;
                }

                Core.Scheduler.NextTick(() =>
                {
                    var snapshots = Core.PlayerManager.GetAllPlayers()
                        .Where(p => p.IsValid && !p.IsFakeClient)
                        .Select(p => new AdminPlaytimeSnapshot(
                            p.SteamID,
                            p.Controller.PlayerName ?? PluginLocalizer.Get(Core)["player_fallback_name", p.PlayerID]))
                        .ToList();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _adminPlaytimeDbManager.TrackOnlineAdminsAsync(snapshots, intervalMinutes);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isTrackingAdminPlaytime, 0);
                        }
                    });
                });
            },
            null,
            TimeSpan.FromMinutes(intervalMinutes),
            TimeSpan.FromMinutes(intervalMinutes));
    }
}




