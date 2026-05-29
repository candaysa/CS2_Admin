using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026052003)]
public class AddAdminActionsLogTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_actions_log").Exists())
        {
            return;
        }

        Create.Table("admin_actions_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("action").AsString(64).NotNullable()
            .WithColumn("target_steamid").AsInt64().Nullable()
            .WithColumn("target_name").AsString(64).Nullable()
            .WithColumn("target_userid").AsInt32().Nullable()
            .WithColumn("admin_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("admin_steamid").AsInt64().Nullable()
            .WithColumn("reason").AsString(2048).Nullable()
            .WithColumn("server_id").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_actions_log_action")
            .OnTable("admin_actions_log")
            .OnColumn("action");

        Create.Index("idx_admin_actions_log_target_steamid")
            .OnTable("admin_actions_log")
            .OnColumn("target_steamid");

        Create.Index("idx_admin_actions_log_admin_steamid")
            .OnTable("admin_actions_log")
            .OnColumn("admin_steamid");

        Create.Index("idx_admin_actions_log_server_id")
            .OnTable("admin_actions_log")
            .OnColumn("server_id");

        Create.Index("idx_admin_actions_log_created_at")
            .OnTable("admin_actions_log")
            .OnColumn("created_at");

        Create.Index("idx_admin_actions_log_target_steamid_created_at")
            .OnTable("admin_actions_log")
            .OnColumn("target_steamid").Ascending()
            .OnColumn("created_at").Ascending();
    }

    public override void Down()
    {
        Delete.Table("admin_actions_log");
    }
}
