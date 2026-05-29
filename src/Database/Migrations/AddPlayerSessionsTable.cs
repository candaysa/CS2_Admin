using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052001)]
public class AddPlayerSessionsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_sessions").Exists())
        {
            return;
        }

        Create.Table("admin_player_sessions")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("server_id").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("connected_at").AsDateTime().NotNullable()
            .WithColumn("disconnected_at").AsDateTime().Nullable()
            .WithColumn("duration_seconds").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("last_userid").AsInt32().Nullable()
            .WithColumn("last_ip").AsString(64).Nullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_player_sessions_steamid")
            .OnTable("admin_player_sessions")
            .OnColumn("steamid");

        Create.Index("idx_admin_player_sessions_server_id")
            .OnTable("admin_player_sessions")
            .OnColumn("server_id");

        Create.Index("idx_admin_player_sessions_connected_at")
            .OnTable("admin_player_sessions")
            .OnColumn("connected_at");

        Create.Index("idx_admin_player_sessions_disconnected_at")
            .OnTable("admin_player_sessions")
            .OnColumn("disconnected_at");

        Create.Index("idx_admin_player_sessions_steamid_connected_at")
            .OnTable("admin_player_sessions")
            .OnColumn("steamid").Ascending()
            .OnColumn("connected_at").Ascending();

        Create.Index("idx_admin_player_sessions_server_id_disconnected_at")
            .OnTable("admin_player_sessions")
            .OnColumn("server_id").Ascending()
            .OnColumn("disconnected_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("admin_player_sessions");
    }
}
