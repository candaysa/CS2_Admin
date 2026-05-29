using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052002)]
public class AddPlayerNamesHistoryTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_names_history").Exists())
        {
            return;
        }

        Create.Table("admin_player_names_history")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable()
            .WithColumn("first_seen_at").AsDateTime().NotNullable()
            .WithColumn("last_seen_at").AsDateTime().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_player_names_history_steamid")
            .OnTable("admin_player_names_history")
            .OnColumn("steamid");

        Create.Index("idx_admin_player_names_history_steamid_player_name")
            .OnTable("admin_player_names_history")
            .OnColumn("steamid").Ascending()
            .OnColumn("player_name").Ascending();

        Create.Index("idx_admin_player_names_history_last_seen_at")
            .OnTable("admin_player_names_history")
            .OnColumn("last_seen_at");
    }

    public override void Down()
    {
        Delete.Table("admin_player_names_history");
    }
}
