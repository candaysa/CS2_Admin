using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052207)]
public class AddDiscordMessageStateTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_discord_message_state").Exists())
        {
            return;
        }

        Create.Table("admin_discord_message_state")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_key").AsString(190).NotNullable()
            .WithColumn("channel_id").AsString(64).NotNullable()
            .WithColumn("message_id").AsString(64).NotNullable()
            .WithColumn("updated_at").AsDateTime().NotNullable();

        Create.Index("idx_admin_discord_message_state_key")
            .OnTable("admin_discord_message_state")
            .OnColumn("message_key").Unique();
    }

    public override void Down()
    {
        Delete.Table("admin_discord_message_state");
    }
}
