using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052701)]
public class AddPlayerCustomNamesTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_custom_names").Exists())
        {
            return;
        }

        Create.Table("admin_player_custom_names")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_player_custom_names_steamid")
            .OnTable("admin_player_custom_names")
            .OnColumn("steamid").Unique();
    }

    public override void Down()
    {
        Delete.Table("admin_player_custom_names");
    }
}
