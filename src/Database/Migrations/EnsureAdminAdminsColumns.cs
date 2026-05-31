using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026053101)]
public class EnsureAdminAdminsColumns : Migration
{
    public override void Up()
    {
        if (!Schema.Table("admin_admins").Exists())
        {
            return;
        }

        if (!Schema.Table("admin_admins").Column("groups").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("groups").AsString(512).NotNullable().WithDefaultValue("");
        }

        if (!Schema.Table("admin_admins").Column("immunity").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("immunity").AsInt32().NotNullable().WithDefaultValue(0);
        }

        if (!Schema.Table("admin_admins").Column("created_at").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);
        }

        if (!Schema.Table("admin_admins").Column("expires_at").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("expires_at").AsDateTime().Nullable();
        }

        if (!Schema.Table("admin_admins").Column("added_by").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("added_by").AsString(64).Nullable();
        }

        if (!Schema.Table("admin_admins").Column("added_by_steamid").Exists())
        {
            Alter.Table("admin_admins")
                .AddColumn("added_by_steamid").AsInt64().Nullable();
        }
    }

    public override void Down()
    {
    }
}
