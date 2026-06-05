using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026060401)]
public class ConvertStringColumnsToUtf8Mb4 : Migration
{
    private static readonly string[] Tables = new[]
    {
        "admin_actions_log",
        "admin_admins",
        "admin_bans",
        "admin_discord_message_state",
        "admin_discord_server_status",
        "admin_gags",
        "admin_groups",
        "admin_log",
        "admin_mutes",
        "admin_playtime",
        "admin_player_custom_names",
        "admin_player_ip_history",
        "admin_player_ips",
        "admin_player_names_history",
        "admin_player_sessions",
        "admin_servers",
        "admin_warns"
    };

    public override void Up()
    {
        foreach (var table in Tables)
        {
            if (Schema.Table(table).Exists())
            {
                Execute.Sql($"ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                Execute.Sql($"ALTER TABLE `{table}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
            }
        }
    }

    public override void Down()
    {
        foreach (var table in Tables)
        {
            if (Schema.Table(table).Exists())
            {
                Execute.Sql($"ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci;");
                Execute.Sql($"ALTER TABLE `{table}` DEFAULT CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci;");
            }
        }
    }
}
