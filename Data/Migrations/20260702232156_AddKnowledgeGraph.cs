using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SelfNodeId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NodeId",
                table: "ActivityLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MigrationAudits",
                columns: table => new
                {
                    MigrationAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LegacyItemId = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationAudits", x => x.MigrationAuditId);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    NodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.NodeId);
                    table.ForeignKey(
                        name: "FK_Nodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationTypes",
                columns: table => new
                {
                    RelationTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InverseName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsSymmetric = table.Column<bool>(type: "bit", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationTypes", x => x.RelationTypeId);
                    table.ForeignKey(
                        name: "FK_RelationTypes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Verbs",
                columns: table => new
                {
                    VerbId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verbs", x => x.VerbId);
                    table.ForeignKey(
                        name: "FK_Verbs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActionItems",
                columns: table => new
                {
                    ActionItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Verb = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemindAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TargetNodeId = table.Column<int>(type: "int", nullable: true),
                    ParentActionId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionItems", x => x.ActionItemId);
                    table.ForeignKey(
                        name: "FK_ActionItems_ActionItems_ParentActionId",
                        column: x => x.ParentActionId,
                        principalTable: "ActionItems",
                        principalColumn: "ActionItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionItems_Nodes_TargetNodeId",
                        column: x => x.TargetNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsOrdered = table.Column<bool>(type: "bit", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.NodeId);
                    table.ForeignKey(
                        name: "FK_Collections_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeTags",
                columns: table => new
                {
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTags", x => new { x.NodeId, x.TagId });
                    table.ForeignKey(
                        name: "FK_NodeTags_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NodeTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeRelations",
                columns: table => new
                {
                    NodeRelationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceNodeId = table.Column<int>(type: "int", nullable: false),
                    TargetNodeId = table.Column<int>(type: "int", nullable: false),
                    RelationTypeId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeRelations", x => x.NodeRelationId);
                    table.ForeignKey(
                        name: "FK_NodeRelations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NodeRelations_Nodes_SourceNodeId",
                        column: x => x.SourceNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NodeRelations_Nodes_TargetNodeId",
                        column: x => x.TargetNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NodeRelations_RelationTypes_RelationTypeId",
                        column: x => x.RelationTypeId,
                        principalTable: "RelationTypes",
                        principalColumn: "RelationTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventDetails",
                columns: table => new
                {
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerbId = table.Column<int>(type: "int", nullable: true),
                    SubjectNodeId = table.Column<int>(type: "int", nullable: true),
                    ObjectNodeId = table.Column<int>(type: "int", nullable: true),
                    PlaceNodeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDetails", x => x.NodeId);
                    table.ForeignKey(
                        name: "FK_EventDetails_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventDetails_Nodes_ObjectNodeId",
                        column: x => x.ObjectNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventDetails_Nodes_PlaceNodeId",
                        column: x => x.PlaceNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventDetails_Nodes_SubjectNodeId",
                        column: x => x.SubjectNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventDetails_Verbs_VerbId",
                        column: x => x.VerbId,
                        principalTable: "Verbs",
                        principalColumn: "VerbId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActionContexts",
                columns: table => new
                {
                    ActionItemId = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionContexts", x => new { x.ActionItemId, x.NodeId, x.Role });
                    table.ForeignKey(
                        name: "FK_ActionContexts_ActionItems_ActionItemId",
                        column: x => x.ActionItemId,
                        principalTable: "ActionItems",
                        principalColumn: "ActionItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionContexts_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionMemberships",
                columns: table => new
                {
                    CollectionNodeId = table.Column<int>(type: "int", nullable: false),
                    MemberNodeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMemberships", x => new { x.CollectionNodeId, x.MemberNodeId });
                    table.ForeignKey(
                        name: "FK_CollectionMemberships_Collections_CollectionNodeId",
                        column: x => x.CollectionNodeId,
                        principalTable: "Collections",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionMemberships_Nodes_MemberNodeId",
                        column: x => x.MemberNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventParticipants",
                columns: table => new
                {
                    EventNodeId = table.Column<int>(type: "int", nullable: false),
                    ParticipantNodeId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventParticipants", x => new { x.EventNodeId, x.ParticipantNodeId, x.Role });
                    table.ForeignKey(
                        name: "FK_EventParticipants_EventDetails_EventNodeId",
                        column: x => x.EventNodeId,
                        principalTable: "EventDetails",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventParticipants_Nodes_ParticipantNodeId",
                        column: x => x.ParticipantNodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SelfNodeId",
                table: "AspNetUsers",
                column: "SelfNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_UserId_NodeId",
                table: "ActivityLogs",
                columns: new[] { "UserId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionContexts_NodeId",
                table: "ActionContexts",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionContexts_UserId_NodeId_Role",
                table: "ActionContexts",
                columns: new[] { "UserId", "NodeId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_ParentActionId",
                table: "ActionItems",
                column: "ParentActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_TargetNodeId",
                table: "ActionItems",
                column: "TargetNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_UserId_RemindAt",
                table: "ActionItems",
                columns: new[] { "UserId", "RemindAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_UserId_Status_DueDate",
                table: "ActionItems",
                columns: new[] { "UserId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMemberships_CollectionNodeId_SortOrder",
                table: "CollectionMemberships",
                columns: new[] { "CollectionNodeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMemberships_MemberNodeId",
                table: "CollectionMemberships",
                column: "MemberNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDetails_ObjectNodeId",
                table: "EventDetails",
                column: "ObjectNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDetails_OccurredAt",
                table: "EventDetails",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EventDetails_PlaceNodeId",
                table: "EventDetails",
                column: "PlaceNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDetails_SubjectNodeId",
                table: "EventDetails",
                column: "SubjectNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EventDetails_VerbId",
                table: "EventDetails",
                column: "VerbId");

            migrationBuilder.CreateIndex(
                name: "IX_EventParticipants_ParticipantNodeId",
                table: "EventParticipants",
                column: "ParticipantNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationAudits_Category",
                table: "MigrationAudits",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_NodeRelations_RelationTypeId",
                table: "NodeRelations",
                column: "RelationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeRelations_SourceNodeId",
                table: "NodeRelations",
                column: "SourceNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeRelations_TargetNodeId_RelationTypeId",
                table: "NodeRelations",
                columns: new[] { "TargetNodeId", "RelationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeRelations_UserId_SourceNodeId_TargetNodeId_RelationTypeId",
                table: "NodeRelations",
                columns: new[] { "UserId", "SourceNodeId", "TargetNodeId", "RelationTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_UserId_IsPinned",
                table: "Nodes",
                columns: new[] { "UserId", "IsPinned" },
                filter: "[IsPinned] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_UserId_Kind",
                table: "Nodes",
                columns: new[] { "UserId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_UserId_State",
                table: "Nodes",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_UserId_UpdatedAt",
                table: "Nodes",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeTags_TagId",
                table: "NodeTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "UX_RelationType_System",
                table: "RelationTypes",
                column: "Name",
                unique: true,
                filter: "[UserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RelationType_User",
                table: "RelationTypes",
                columns: new[] { "UserId", "Name" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Verb_System",
                table: "Verbs",
                column: "Name",
                unique: true,
                filter: "[UserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Verb_User",
                table: "Verbs",
                columns: new[] { "UserId", "Name" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Nodes_SelfNodeId",
                table: "AspNetUsers",
                column: "SelfNodeId",
                principalTable: "Nodes",
                principalColumn: "NodeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Nodes_SelfNodeId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ActionContexts");

            migrationBuilder.DropTable(
                name: "CollectionMemberships");

            migrationBuilder.DropTable(
                name: "EventParticipants");

            migrationBuilder.DropTable(
                name: "MigrationAudits");

            migrationBuilder.DropTable(
                name: "NodeRelations");

            migrationBuilder.DropTable(
                name: "NodeTags");

            migrationBuilder.DropTable(
                name: "ActionItems");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "EventDetails");

            migrationBuilder.DropTable(
                name: "RelationTypes");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "Verbs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SelfNodeId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_UserId_NodeId",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "SelfNodeId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "ActivityLogs");
        }
    }
}
