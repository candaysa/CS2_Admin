using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052201)]
public class AddDiscordServerStatusTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_discord_server_status").Exists())
        {
            return;
        }

        Create.Table("admin_discord_server_status")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("server_id").AsString(128).NotNullable()
            .WithColumn("hub_key").AsString(64).NotNullable().WithDefaultValue("default")
            .WithColumn("server_name").AsString(128).NotNullable()
            .WithColumn("button_label").AsString(64).NotNullable()
            .WithColumn("server_ip").AsString(64).NotNullable()
            .WithColumn("server_port").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("map_name").AsString(128).NotNullable().WithDefaultValue("unknown")
            .WithColumn("player_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("max_players").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("join_url").AsString(255).NotNullable().WithDefaultValue(string.Empty)
            .WithColumn("last_heartbeat_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_discord_server_status_server_id")
            .OnTable("admin_discord_server_status")
            .OnColumn("server_id").Unique();

        Create.Index("idx_admin_discord_server_status_hub_key")
            .OnTable("admin_discord_server_status")
            .OnColumn("hub_key").Ascending()
            .OnColumn("last_heartbeat_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("admin_discord_server_status");
    }
}
