using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CS2_Admin.Database;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public class DiscordInteractionHandler
{
    private const string DiscordApiBaseUrl = "https://discord.com/api/v10";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly ISwiftlyCore _core;
    private readonly DiscordRestClient _restClient;
    private readonly string _botToken;
    private WarnManager? _warnManager;
    private AdminLogManager? _adminLogManager;

    public DiscordInteractionHandler(ISwiftlyCore core, DiscordRestClient restClient, string botToken)
    {
        _core = core;
        _restClient = restClient;
        _botToken = botToken;
    }

    public void SetDatabaseManagers(WarnManager? wm, AdminLogManager? alm)
    {
        _warnManager = wm;
        _adminLogManager = alm;
    }

    public async Task HandleInteractionAsync(JsonElement data)
    {
        try
        {
            var id = data.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var token = data.TryGetProperty("token", out var tokenElement) ? tokenElement.GetString() : null;
            var applicationId = data.TryGetProperty("application_id", out var applicationIdElement) ? applicationIdElement.GetString() : null;
            var type = data.TryGetProperty("type", out var typeElement) ? typeElement.GetInt32() : 0;

            if (type == 3 && id != null && token != null && data.TryGetProperty("data", out var componentData))
            {
                var customId = componentData.TryGetProperty("custom_id", out var customIdElement) ? customIdElement.GetString() : null;
                if (customId != null && customId.StartsWith("report_resolve_"))
                {
                    await HandleReportResolveAsync(id, token, applicationId, data, customId);
                }
                else if (customId != null && customId.StartsWith("report_punish_"))
                {
                    await HandleReportPunishAsync(id, token, applicationId, data, customId);
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error handling interaction: {Message}", ex.Message);
        }
    }

    private async Task HandleReportResolveAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 6))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var member = data.GetProperty("member");
            var user = member.GetProperty("user");
            var userId = user.GetProperty("id").GetString();

            var message = data.GetProperty("message");
            var messageId = message.TryGetProperty("id", out var messageIdElement) ? messageIdElement.GetString() : null;
            var channelId = message.TryGetProperty("channel_id", out var channelIdElement) ? channelIdElement.GetString() : null;
            var embeds = message.GetProperty("embeds");
            if (embeds.GetArrayLength() == 0)
            {
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_missing_embed", "Report message embed could not be found."));
                return;
            }

            var oldEmbed = embeds[0];

            var newEmbed = JsonObject.Create(oldEmbed);
            if (newEmbed == null)
            {
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_parse_failed", "Report message embed could not be parsed."));
                return;
            }

            if (newEmbed != null)
            {
                newEmbed["color"] = 65433;

                if (newEmbed.TryGetPropertyValue("description", out var descNode) && descNode != null)
                {
                    var desc = descNode.GetValue<string>();
                    newEmbed["description"] = $"{desc}\n\n{T("discord_report_resolved_status", "**Status:** ✅ Resolved by <@{0}>", userId ?? "0")}";
                }
            }

            var payload = new
            {
                embeds = new JsonObject[] { newEmbed! },
                components = Array.Empty<object>()
            };

            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId) || !await UpdateMessageAsync(channelId, messageId, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord resolve interaction message update failed for custom_id={CustomId}", customId);
                await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_followup_failed", "Report could not be marked as resolved."));
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in resolve interaction: {Message}", ex.Message);
            await SendFollowupAsync(applicationId, interactionToken, T("discord_report_resolve_internal_error", "An internal error occurred while resolving the report."));
        }
    }

    private async Task HandleReportPunishAsync(string interactionId, string interactionToken, string? applicationId, JsonElement data, string customId)
    {
        try
        {
            if (!await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 5, new { flags = 64 }))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction defer failed for custom_id={CustomId}", customId);
                return;
            }

            var parts = customId.Split('_');
            if (parts.Length < 3 || !ulong.TryParse(parts[2], out var targetSteamId))
            {
                await EditOriginalResponseAsync(applicationId, interactionToken, BuildEditErrorPayload(T("discord_report_target_parse_failed", "Report target SteamID could not be parsed.")));
                return;
            }

            var warns = _warnManager != null ? await _warnManager.GetWarnHistoryAsync(targetSteamId, WarnHistoryFilter.All, 5) : [];
            var logs = _adminLogManager != null ? await _adminLogManager.GetTargetHistoryAsync(targetSteamId, 5) : [];

            var descBuilder = new StringBuilder();
            if (warns.Count == 0 && logs.Count == 0)
            {
                descBuilder.AppendLine(T("discord_report_no_punishments", "No recent punishments found for this player."));
            }
            else
            {
                if (warns.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_warnings", "**Recent Warnings:**"));
                    foreach (var warn in warns)
                    {
                        descBuilder.AppendLine($"- [{warn.CreatedAt:yyyy-MM-dd}] `{warn.Reason}` by {warn.AdminName}");
                    }
                    descBuilder.AppendLine();
                }

                if (logs.Count > 0)
                {
                    descBuilder.AppendLine(T("discord_report_recent_actions", "**Recent Actions:**"));
                    foreach (var log in logs)
                    {
                        descBuilder.AppendLine($"- [{log.CreatedAt:yyyy-MM-dd}] `{log.Action}`: {log.Details}");
                    }
                }
            }

            var embed = new
            {
                title = T("discord_report_punishments_title", "Player Punishments"),
                description = descBuilder.ToString(),
                color = 16711680
            };

            var payload = new
            {
                content = "",
                embeds = new[] { embed }
            };

            if (!await EditOriginalResponseAsync(applicationId, interactionToken, payload))
            {
                _core.Logger.LogWarningIfEnabled("[CS2_Admin] Discord punish interaction response edit failed for custom_id={CustomId}", customId);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarningIfEnabled("[CS2_Admin] Error in punish interaction: {Message}", ex.Message);
            await EditOriginalResponseAsync(applicationId, interactionToken, BuildEditErrorPayload(T("discord_report_punishments_internal_error", "An internal error occurred while loading punishments.")));
        }
    }

    private async Task SendErrorAsync(string interactionId, string interactionToken, string message)
    {
        await _restClient.RespondToInteractionAsync(interactionId, interactionToken, 4, BuildErrorPayload(message));
    }

    private async Task<bool> SendFollowupAsync(string? applicationId, string interactionToken, string message)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}";
        using var request = BuildDiscordRequest(HttpMethod.Post, endpoint, BuildErrorPayload(message));
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("send interaction followup", response);
        return false;
    }

    private async Task<bool> EditOriginalResponseAsync(string? applicationId, string interactionToken, object payload)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        var endpoint = $"{DiscordApiBaseUrl}/webhooks/{applicationId}/{interactionToken}/messages/@original";
        using var request = BuildDiscordRequest(HttpMethod.Patch, endpoint, payload);
        using var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        await LogDiscordFailureAsync("edit interaction response", response);
        return false;
    }

    private async Task<bool> UpdateMessageAsync(string channelId, string messageId, object payload)
    {
        var endpoint = $"{DiscordApiBaseUrl}/channels/{channelId}/messages/{messageId}";
        using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static object BuildErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            flags = 64
        };
    }

    private static object BuildEditErrorPayload(string message)
    {
        return new
        {
            content = $"CS2_Admin: {message}",
            embeds = Array.Empty<object>()
        };
    }

    private string T(string key, string fallback, params object[] args)
    {
        try
        {
            var localizer = PluginLocalizer.Get(_core);
            var value = args.Length == 0 ? localizer[key] : localizer[key, args];
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
                ? (args.Length == 0 ? fallback : string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args))
                : value;
        }
        catch
        {
            return args.Length == 0 ? fallback : string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args);
        }
    }

    private HttpRequestMessage BuildDiscordRequest(HttpMethod method, string endpoint, object payload)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task LogDiscordFailureAsync(string action, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _core.Logger.LogWarningIfEnabled(
            "[CS2_Admin] Discord bot {Action} failed with status {StatusCode}. Response: {Body}",
            action,
            response.StatusCode,
            string.IsNullOrWhiteSpace(body) ? "-" : body);
    }
}
