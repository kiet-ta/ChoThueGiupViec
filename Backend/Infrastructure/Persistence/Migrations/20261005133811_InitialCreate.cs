using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommonService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ADMIN",
                columns: table => new
                {
                    admin_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    email = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    admin_role = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    failed_login_count = table.Column<byte>(type: "TINYINT", nullable: false, defaultValue: (byte)0),
                    locked_until = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ADMIN", x => x.admin_id);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMER",
                columns: table => new
                {
                    customer_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone_number = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    otp_verified_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    trust_score = table.Column<decimal>(type: "DECIMAL(3,2)", precision: 18, scale: 2, nullable: false),
                    account_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "OTP_CODE",
                columns: table => new
                {
                    otp_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone_number = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    role = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    code_hash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    attempt_count = table.Column<byte>(type: "TINYINT", nullable: false),
                    requested_ip = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OTP_CODE", x => x.otp_id);
                });

            migrationBuilder.CreateTable(
                name: "PARTNER_AGENCY",
                columns: table => new
                {
                    agency_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tax_code = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: false),
                    legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    legal_representative = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    contact_phone = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    contact_email = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    escrow_deposit_balance = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    sla_score = table.Column<decimal>(type: "DECIMAL(5,2)", precision: 18, scale: 2, nullable: false),
                    worker_quota = table.Column<int>(type: "int", nullable: false),
                    is_verified_partner = table.Column<bool>(type: "bit", nullable: false),
                    bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    agency_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    password_hash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    guarantee_signed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    guarantee_file_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    failed_login_count = table.Column<byte>(type: "TINYINT", nullable: false, defaultValue: (byte)0),
                    locked_until = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PARTNER_AGENCY", x => x.agency_id);
                });

            migrationBuilder.CreateTable(
                name: "REFRESH_TOKEN",
                columns: table => new
                {
                    refresh_token_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    token_hash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    subject_role = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    subject_id = table.Column<int>(type: "int", nullable: false),
                    family_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    replaced_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REFRESH_TOKEN", x => x.refresh_token_id);
                    table.ForeignKey(
                        name: "FK_REFRESH_TOKEN_REFRESH_TOKEN_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "REFRESH_TOKEN",
                        principalColumn: "refresh_token_id");
                });

            migrationBuilder.CreateTable(
                name: "SKILL",
                columns: table => new
                {
                    skill_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    skill_code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    skill_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SKILL", x => x.skill_id);
                });

            migrationBuilder.CreateTable(
                name: "SUBSCRIPTION_PACKAGE",
                columns: table => new
                {
                    package_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    package_code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    package_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tier = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    billing_cycle = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    price = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    worker_quota = table.Column<int>(type: "int", nullable: false),
                    commission_rate = table.Column<decimal>(type: "DECIMAL(4,3)", precision: 18, scale: 2, nullable: false),
                    has_roster_dashboard = table.Column<bool>(type: "bit", nullable: false),
                    has_analytics = table.Column<bool>(type: "bit", nullable: false),
                    priority_dispatch = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SUBSCRIPTION_PACKAGE", x => x.package_id);
                });

            migrationBuilder.CreateTable(
                name: "ADMIN_AUDIT_LOG",
                columns: table => new
                {
                    log_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    actor_type = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    admin_id = table.Column<int>(type: "int", nullable: true),
                    entity_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    entity_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    field_name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    old_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    new_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    changed_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ADMIN_AUDIT_LOG", x => x.log_id);
                    table.ForeignKey(
                        name: "FK_ADMIN_AUDIT_LOG_ADMIN_admin_id",
                        column: x => x.admin_id,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id");
                });

            migrationBuilder.CreateTable(
                name: "PAYOUT_BATCH",
                columns: table => new
                {
                    batch_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    period_month = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    confirmed_by = table.Column<int>(type: "int", nullable: true),
                    batch_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    total_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    export_file_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYOUT_BATCH", x => x.batch_id);
                    table.ForeignKey(
                        name: "FK_PAYOUT_BATCH_ADMIN_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PRICE_RULE",
                columns: table => new
                {
                    rule_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    service_tier = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    area_bracket = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    updated_by = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRICE_RULE", x => x.rule_id);
                    table.ForeignKey(
                        name: "FK_PRICE_RULE_ADMIN_updated_by",
                        column: x => x.updated_by,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMER_ADDRESS",
                columns: table => new
                {
                    address_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer_id = table.Column<int>(type: "int", nullable: false),
                    label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    address_line = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    district = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    housing_type = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    floor_area_m2 = table.Column<decimal>(type: "DECIMAL(6,2)", precision: 18, scale: 2, nullable: false),
                    num_floors = table.Column<byte>(type: "TINYINT", nullable: false),
                    bedrooms = table.Column<byte>(type: "TINYINT", nullable: true),
                    bathrooms = table.Column<byte>(type: "TINYINT", nullable: true),
                    latitude = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    longitude = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    total_area_m2 = table.Column<decimal>(type: "DECIMAL(8,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "[floor_area_m2] * [num_floors]", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_ADDRESS", x => x.address_id);
                    table.ForeignKey(
                        name: "FK_CUSTOMER_ADDRESS_CUSTOMER_customer_id",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMER",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WORKER",
                columns: table => new
                {
                    worker_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    phone_number = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    national_id = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: true),
                    kyc_reviewed_by = table.Column<int>(type: "int", nullable: true),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    worker_type = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    is_super_freelancer = table.Column<bool>(type: "bit", nullable: false),
                    ekyc_confidence = table.Column<decimal>(type: "DECIMAL(5,2)", precision: 18, scale: 2, nullable: true),
                    kyc_status = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    rating_avg = table.Column<decimal>(type: "DECIMAL(3,2)", precision: 18, scale: 2, nullable: false),
                    completed_jobs = table.Column<int>(type: "int", nullable: false),
                    work_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    current_lat = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: true),
                    current_lng = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: true),
                    bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WORKER", x => x.worker_id);
                    table.CheckConstraint("CK_WORKER_type_agency", "([worker_type] = 'FREELANCER' AND [agency_id] IS NULL) OR ([worker_type] = 'AGENCY_STAFF' AND [agency_id] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WORKER_ADMIN_kyc_reviewed_by",
                        column: x => x.kyc_reviewed_by,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WORKER_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PARTNER_SUBSCRIPTION",
                columns: table => new
                {
                    subscription_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    agency_id = table.Column<int>(type: "int", nullable: false),
                    package_id = table.Column<int>(type: "int", nullable: false),
                    start_date = table.Column<DateOnly>(type: "DATE", nullable: false),
                    end_date = table.Column<DateOnly>(type: "DATE", nullable: false),
                    sub_status = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    auto_renew = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PARTNER_SUBSCRIPTION", x => x.subscription_id);
                    table.ForeignKey(
                        name: "FK_PARTNER_SUBSCRIPTION_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PARTNER_SUBSCRIPTION_SUBSCRIPTION_PACKAGE_package_id",
                        column: x => x.package_id,
                        principalTable: "SUBSCRIPTION_PACKAGE",
                        principalColumn: "package_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JOB_ORDER",
                columns: table => new
                {
                    order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    customer_id = table.Column<int>(type: "int", nullable: false),
                    address_id = table.Column<int>(type: "int", nullable: false),
                    service_tier = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "DATE", nullable: false),
                    shift_code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    area_snapshot_m2 = table.Column<decimal>(type: "DECIMAL(8,2)", precision: 18, scale: 2, nullable: false),
                    required_workers = table.Column<byte>(type: "TINYINT", nullable: false),
                    required_skill = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    total_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    order_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    customer_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_ORDER", x => x.order_id);
                    table.ForeignKey(
                        name: "FK_JOB_ORDER_CUSTOMER_ADDRESS_address_id",
                        column: x => x.address_id,
                        principalTable: "CUSTOMER_ADDRESS",
                        principalColumn: "address_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ORDER_CUSTOMER_customer_id",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMER",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_SLOT",
                columns: table => new
                {
                    slot_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: true),
                    slot_date = table.Column<DateOnly>(type: "DATE", nullable: false),
                    shift_code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "TIME(0)", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "TIME(0)", nullable: false),
                    slot_source = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    skill_tags = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    slot_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_SLOT", x => x.slot_id);
                    table.ForeignKey(
                        name: "FK_BOOKING_SLOT_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_SLOT_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FAVORITE_WORKER",
                columns: table => new
                {
                    customer_id = table.Column<int>(type: "int", nullable: false),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FAVORITE_WORKER", x => new { x.customer_id, x.worker_id });
                    table.ForeignKey(
                        name: "FK_FAVORITE_WORKER_CUSTOMER_customer_id",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMER",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FAVORITE_WORKER_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PAYOUT_ITEM",
                columns: table => new
                {
                    item_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    batch_id = table.Column<int>(type: "int", nullable: false),
                    worker_id = table.Column<int>(type: "int", nullable: true),
                    agency_id = table.Column<int>(type: "int", nullable: true),
                    payee_type = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    job_count = table.Column<int>(type: "int", nullable: false),
                    gross_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    commission_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    penalty_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    bank_account_no = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    item_status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    transferred_at = table.Column<DateTime>(type: "DATETIME2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYOUT_ITEM", x => x.item_id);
                    table.CheckConstraint("CK_PAYOUT_ITEM_payee", "([payee_type] = 'FREELANCER' AND [worker_id] IS NOT NULL AND [agency_id] IS NULL) OR ([payee_type] = 'AGENCY' AND [worker_id] IS NULL AND [agency_id] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PAYOUT_ITEM_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYOUT_ITEM_PAYOUT_BATCH_batch_id",
                        column: x => x.batch_id,
                        principalTable: "PAYOUT_BATCH",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYOUT_ITEM_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WORKER_SKILL",
                columns: table => new
                {
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    skill_id = table.Column<int>(type: "int", nullable: false),
                    years_of_experience = table.Column<decimal>(type: "DECIMAL(3,1)", precision: 18, scale: 2, nullable: true),
                    is_verified = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WORKER_SKILL", x => new { x.worker_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_WORKER_SKILL_SKILL_skill_id",
                        column: x => x.skill_id,
                        principalTable: "SKILL",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WORKER_SKILL_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DISPUTE_TICKET",
                columns: table => new
                {
                    dispute_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    resolved_by = table.Column<int>(type: "int", nullable: true),
                    raised_by = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    category = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_urls = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dispute_status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    fault_party = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: true),
                    compensation_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: true),
                    sla_due_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DISPUTE_TICKET", x => x.dispute_id);
                    table.ForeignKey(
                        name: "FK_DISPUTE_TICKET_ADMIN_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DISPUTE_TICKET_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JOB_ORDER_EXTENSION",
                columns: table => new
                {
                    extension_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    extra_hours = table.Column<decimal>(type: "DECIMAL(3,1)", precision: 18, scale: 2, nullable: false),
                    extra_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    worker_decision = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ext_status = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    requested_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    decided_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_ORDER_EXTENSION", x => x.extension_id);
                    table.ForeignKey(
                        name: "FK_JOB_ORDER_EXTENSION_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ORDER_EXTENSION_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JOB_ASSIGNMENT",
                columns: table => new
                {
                    assignment_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<int>(type: "int", nullable: false),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    agency_id = table.Column<int>(type: "int", nullable: true),
                    slot_id = table.Column<int>(type: "int", nullable: false),
                    payout_item_id = table.Column<int>(type: "int", nullable: true),
                    service_tier = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    assignment_seq = table.Column<byte>(type: "TINYINT", nullable: false),
                    work_zone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    assignment_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    dispatch_radius_km = table.Column<byte>(type: "TINYINT", nullable: false),
                    matching_score = table.Column<decimal>(type: "DECIMAL(5,2)", precision: 18, scale: 2, nullable: true),
                    gross_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    commission_rate = table.Column<decimal>(type: "DECIMAL(4,3)", precision: 18, scale: 2, nullable: false),
                    payout_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    absence_fee_amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: true),
                    accepted_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    started_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    customer_confirmed_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_ASSIGNMENT", x => x.assignment_id);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_BOOKING_SLOT_slot_id",
                        column: x => x.slot_id,
                        principalTable: "BOOKING_SLOT",
                        principalColumn: "slot_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_CUSTOMER_customer_id",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMER",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_PAYOUT_ITEM_payout_item_id",
                        column: x => x.payout_item_id,
                        principalTable: "PAYOUT_ITEM",
                        principalColumn: "item_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_JOB_ASSIGNMENT_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_TRANSACTION",
                columns: table => new
                {
                    payment_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    gateway_txn_ref = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    extension_id = table.Column<int>(type: "int", nullable: true),
                    subscription_id = table.Column<int>(type: "int", nullable: true),
                    purpose = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    gateway = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    txn_status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    qr_payload = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    paid_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    ipn_payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_TRANSACTION", x => x.payment_id);
                    table.CheckConstraint("CK_PAYMENT_TRANSACTION_purpose", "([purpose] = 'ORDER' AND [order_id] IS NOT NULL AND [extension_id] IS NULL AND [subscription_id] IS NULL) OR ([purpose] = 'EXTENSION' AND [order_id] IS NULL AND [extension_id] IS NOT NULL AND [subscription_id] IS NULL) OR ([purpose] = 'SUBSCRIPTION' AND [order_id] IS NULL AND [extension_id] IS NULL AND [subscription_id] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PAYMENT_TRANSACTION_JOB_ORDER_EXTENSION_extension_id",
                        column: x => x.extension_id,
                        principalTable: "JOB_ORDER_EXTENSION",
                        principalColumn: "extension_id");
                    table.ForeignKey(
                        name: "FK_PAYMENT_TRANSACTION_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "FK_PAYMENT_TRANSACTION_PARTNER_SUBSCRIPTION_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "PARTNER_SUBSCRIPTION",
                        principalColumn: "subscription_id");
                });

            migrationBuilder.CreateTable(
                name: "CHECK_IN_LOG",
                columns: table => new
                {
                    checkin_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    device_lat = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    device_lng = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    distance_m = table.Column<decimal>(type: "DECIMAL(7,2)", precision: 18, scale: 2, nullable: false),
                    gps_verified = table.Column<bool>(type: "bit", nullable: false),
                    fallback_method = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    fallback_photo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    call_attempts = table.Column<byte>(type: "TINYINT", nullable: false),
                    customer_absent_at = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    checked_in_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CHECK_IN_LOG", x => x.checkin_id);
                    table.ForeignKey(
                        name: "FK_CHECK_IN_LOG_JOB_ASSIGNMENT_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "JOB_ASSIGNMENT",
                        principalColumn: "assignment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "INCIDENT_LOG",
                columns: table => new
                {
                    incident_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    incident_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    photo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    latitude = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    longitude = table.Column<decimal>(type: "DECIMAL(9,6)", precision: 18, scale: 2, nullable: false),
                    redispatch_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    penalty_waived = table.Column<bool>(type: "bit", nullable: false),
                    reported_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INCIDENT_LOG", x => x.incident_id);
                    table.ForeignKey(
                        name: "FK_INCIDENT_LOG_JOB_ASSIGNMENT_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "JOB_ASSIGNMENT",
                        principalColumn: "assignment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JOB_PHOTO",
                columns: table => new
                {
                    photo_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    photo_phase = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    angle_no = table.Column<byte>(type: "TINYINT", nullable: false),
                    image_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    vol_score = table.Column<double>(type: "FLOAT", nullable: false),
                    is_accepted = table.Column<bool>(type: "bit", nullable: false),
                    captured_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JOB_PHOTO", x => x.photo_id);
                    table.ForeignKey(
                        name: "FK_JOB_PHOTO_JOB_ASSIGNMENT_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "JOB_ASSIGNMENT",
                        principalColumn: "assignment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TWO_WAY_RATING",
                columns: table => new
                {
                    rating_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    worker_id = table.Column<int>(type: "int", nullable: false),
                    rater_role = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    stars = table.Column<byte>(type: "TINYINT", nullable: false),
                    criteria_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TWO_WAY_RATING", x => x.rating_id);
                    table.ForeignKey(
                        name: "FK_TWO_WAY_RATING_JOB_ASSIGNMENT_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "JOB_ASSIGNMENT",
                        principalColumn: "assignment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TWO_WAY_RATING_WORKER_worker_id",
                        column: x => x.worker_id,
                        principalTable: "WORKER",
                        principalColumn: "worker_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ESCROW_TRANSACTION",
                columns: table => new
                {
                    escrow_txn_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    agency_id = table.Column<int>(type: "int", nullable: false),
                    txn_type = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "DECIMAL(18,2)", precision: 18, scale: 2, nullable: false),
                    sla_points_delta = table.Column<decimal>(type: "DECIMAL(5,2)", precision: 18, scale: 2, nullable: true),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    dispute_id = table.Column<int>(type: "int", nullable: true),
                    payment_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    created_by_admin_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "DATETIME2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ESCROW_TRANSACTION", x => x.escrow_txn_id);
                    table.ForeignKey(
                        name: "FK_ESCROW_TRANSACTION_ADMIN_created_by_admin_id",
                        column: x => x.created_by_admin_id,
                        principalTable: "ADMIN",
                        principalColumn: "admin_id");
                    table.ForeignKey(
                        name: "FK_ESCROW_TRANSACTION_DISPUTE_TICKET_dispute_id",
                        column: x => x.dispute_id,
                        principalTable: "DISPUTE_TICKET",
                        principalColumn: "dispute_id");
                    table.ForeignKey(
                        name: "FK_ESCROW_TRANSACTION_JOB_ORDER_order_id",
                        column: x => x.order_id,
                        principalTable: "JOB_ORDER",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "FK_ESCROW_TRANSACTION_PARTNER_AGENCY_agency_id",
                        column: x => x.agency_id,
                        principalTable: "PARTNER_AGENCY",
                        principalColumn: "agency_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ESCROW_TRANSACTION_PAYMENT_TRANSACTION_payment_id",
                        column: x => x.payment_id,
                        principalTable: "PAYMENT_TRANSACTION",
                        principalColumn: "payment_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_email",
                table: "ADMIN",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_AUDIT_LOG_admin_id",
                table: "ADMIN_AUDIT_LOG",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SLOT_agency_id",
                table: "BOOKING_SLOT",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SLOT_worker_id_slot_date_shift_code",
                table: "BOOKING_SLOT",
                columns: new[] { "worker_id", "slot_date", "shift_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CHECK_IN_LOG_assignment_id",
                table: "CHECK_IN_LOG",
                column: "assignment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_phone_number",
                table: "CUSTOMER",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_ADDRESS_customer_id",
                table: "CUSTOMER_ADDRESS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_DISPUTE_TICKET_order_id",
                table: "DISPUTE_TICKET",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DISPUTE_TICKET_resolved_by",
                table: "DISPUTE_TICKET",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_TRANSACTION_agency_id",
                table: "ESCROW_TRANSACTION",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_TRANSACTION_created_by_admin_id",
                table: "ESCROW_TRANSACTION",
                column: "created_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_TRANSACTION_dispute_id",
                table: "ESCROW_TRANSACTION",
                column: "dispute_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_TRANSACTION_order_id",
                table: "ESCROW_TRANSACTION",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_ESCROW_TRANSACTION_payment_id",
                table: "ESCROW_TRANSACTION",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_FAVORITE_WORKER_worker_id",
                table: "FAVORITE_WORKER",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_INCIDENT_LOG_assignment_id",
                table: "INCIDENT_LOG",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_agency_id",
                table: "JOB_ASSIGNMENT",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_customer_id",
                table: "JOB_ASSIGNMENT",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_order_id",
                table: "JOB_ASSIGNMENT",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_payout_item_id",
                table: "JOB_ASSIGNMENT",
                column: "payout_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_slot_id",
                table: "JOB_ASSIGNMENT",
                column: "slot_id",
                unique: true,
                filter: "[assignment_status] <> 'CANCELLED' AND [assignment_status] <> 'CANCELLED_BY_WORKER' AND [assignment_status] <> 'REASSIGNED'");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ASSIGNMENT_worker_id",
                table: "JOB_ASSIGNMENT",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ORDER_address_id",
                table: "JOB_ORDER",
                column: "address_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ORDER_customer_id",
                table: "JOB_ORDER",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ORDER_order_code",
                table: "JOB_ORDER",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ORDER_EXTENSION_order_id",
                table: "JOB_ORDER_EXTENSION",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JOB_ORDER_EXTENSION_worker_id",
                table: "JOB_ORDER_EXTENSION",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_JOB_PHOTO_assignment_id",
                table: "JOB_PHOTO",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_OTP_CODE_phone_number_role_created_at",
                table: "OTP_CODE",
                columns: new[] { "phone_number", "role", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_OTP_CODE_requested_ip_created_at",
                table: "OTP_CODE",
                columns: new[] { "requested_ip", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_PARTNER_AGENCY_tax_code",
                table: "PARTNER_AGENCY",
                column: "tax_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PARTNER_SUBSCRIPTION_agency_id",
                table: "PARTNER_SUBSCRIPTION",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARTNER_SUBSCRIPTION_package_id",
                table: "PARTNER_SUBSCRIPTION",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTION_extension_id",
                table: "PAYMENT_TRANSACTION",
                column: "extension_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTION_gateway_txn_ref",
                table: "PAYMENT_TRANSACTION",
                column: "gateway_txn_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTION_order_id",
                table: "PAYMENT_TRANSACTION",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_TRANSACTION_subscription_id",
                table: "PAYMENT_TRANSACTION",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_BATCH_confirmed_by",
                table: "PAYOUT_BATCH",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_BATCH_period_month",
                table: "PAYOUT_BATCH",
                column: "period_month",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_ITEM_agency_id",
                table: "PAYOUT_ITEM",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_ITEM_batch_id",
                table: "PAYOUT_ITEM",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYOUT_ITEM_worker_id",
                table: "PAYOUT_ITEM",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_PRICE_RULE_service_tier_area_bracket",
                table: "PRICE_RULE",
                columns: new[] { "service_tier", "area_bracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRICE_RULE_updated_by",
                table: "PRICE_RULE",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKEN_replaced_by_id",
                table: "REFRESH_TOKEN",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKEN_token_hash",
                table: "REFRESH_TOKEN",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_skill_code",
                table: "SKILL",
                column: "skill_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SUBSCRIPTION_PACKAGE_package_code",
                table: "SUBSCRIPTION_PACKAGE",
                column: "package_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TWO_WAY_RATING_assignment_id_rater_role",
                table: "TWO_WAY_RATING",
                columns: new[] { "assignment_id", "rater_role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TWO_WAY_RATING_worker_id",
                table: "TWO_WAY_RATING",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_agency_id",
                table: "WORKER",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_kyc_reviewed_by",
                table: "WORKER",
                column: "kyc_reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_national_id",
                table: "WORKER",
                column: "national_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_phone_number",
                table: "WORKER",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WORKER_SKILL_skill_id",
                table: "WORKER_SKILL",
                column: "skill_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ADMIN_AUDIT_LOG");

            migrationBuilder.DropTable(
                name: "CHECK_IN_LOG");

            migrationBuilder.DropTable(
                name: "ESCROW_TRANSACTION");

            migrationBuilder.DropTable(
                name: "FAVORITE_WORKER");

            migrationBuilder.DropTable(
                name: "INCIDENT_LOG");

            migrationBuilder.DropTable(
                name: "JOB_PHOTO");

            migrationBuilder.DropTable(
                name: "OTP_CODE");

            migrationBuilder.DropTable(
                name: "PRICE_RULE");

            migrationBuilder.DropTable(
                name: "REFRESH_TOKEN");

            migrationBuilder.DropTable(
                name: "TWO_WAY_RATING");

            migrationBuilder.DropTable(
                name: "WORKER_SKILL");

            migrationBuilder.DropTable(
                name: "DISPUTE_TICKET");

            migrationBuilder.DropTable(
                name: "PAYMENT_TRANSACTION");

            migrationBuilder.DropTable(
                name: "JOB_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "SKILL");

            migrationBuilder.DropTable(
                name: "JOB_ORDER_EXTENSION");

            migrationBuilder.DropTable(
                name: "PARTNER_SUBSCRIPTION");

            migrationBuilder.DropTable(
                name: "BOOKING_SLOT");

            migrationBuilder.DropTable(
                name: "PAYOUT_ITEM");

            migrationBuilder.DropTable(
                name: "JOB_ORDER");

            migrationBuilder.DropTable(
                name: "SUBSCRIPTION_PACKAGE");

            migrationBuilder.DropTable(
                name: "PAYOUT_BATCH");

            migrationBuilder.DropTable(
                name: "WORKER");

            migrationBuilder.DropTable(
                name: "CUSTOMER_ADDRESS");

            migrationBuilder.DropTable(
                name: "ADMIN");

            migrationBuilder.DropTable(
                name: "PARTNER_AGENCY");

            migrationBuilder.DropTable(
                name: "CUSTOMER");
        }
    }
}
