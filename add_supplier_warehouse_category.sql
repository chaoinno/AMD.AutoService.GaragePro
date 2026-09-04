IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_ActivityEvent] (
        [Id] bigint NOT NULL IDENTITY,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyJobId] bigint NOT NULL,
        [EntityId] uniqueidentifier NULL,
        [EntityType] nvarchar(60) NOT NULL,
        [EventType] nvarchar(80) NOT NULL,
        [DescriptionTh] nvarchar(1000) NOT NULL,
        [PerformedByUserId] bigint NOT NULL,
        [PerformedByName] nvarchar(200) NOT NULL,
        [Source] int NOT NULL,
        [OccurredAt] datetime2 NOT NULL,
        [PayloadJson] nvarchar(max) NULL,
        CONSTRAINT [PK_svc_ActivityEvent] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_Attachment] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyJobId] bigint NOT NULL,
        [Kind] nvarchar(40) NOT NULL,
        [EntityId] uniqueidentifier NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(120) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [RelativePath] nvarchar(400) NOT NULL,
        [Sha256] nvarchar(64) NULL,
        [UploadedByUserId] bigint NOT NULL,
        [UploadedByName] nvarchar(200) NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_Attachment] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_CatalogItem] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(60) NOT NULL,
        [Type] int NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Compatibility] nvarchar(500) NULL,
        [Unit] nvarchar(40) NOT NULL,
        [Cost] decimal(18,2) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [StandardHours] decimal(6,2) NULL,
        [OnHand] int NOT NULL,
        [Reserved] int NOT NULL,
        [OnOrder] int NOT NULL,
        [Damaged] int NOT NULL,
        [EtaNote] nvarchar(200) NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_svc_CatalogItem] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_Quotation] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Version] int NOT NULL,
        [Status] int NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyJobId] bigint NOT NULL,
        [JobNo] nvarchar(40) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [CustomerPhone] nvarchar(40) NULL,
        [CustomerTaxId] nvarchar(20) NULL,
        [CustomerAddress] nvarchar(500) NULL,
        [VehicleRegistration] nvarchar(40) NOT NULL,
        [VehicleModel] nvarchar(200) NULL,
        [VehicleVin] nvarchar(40) NULL,
        [VehicleMileage] int NULL,
        [BranchName] nvarchar(200) NOT NULL,
        [BranchAddress] nvarchar(500) NULL,
        [BranchTaxId] nvarchar(20) NULL,
        [BranchPhone] nvarchar(40) NULL,
        [GrossAmount] decimal(18,2) NOT NULL,
        [LineDiscountAmount] decimal(18,2) NOT NULL,
        [PromotionAmount] decimal(18,2) NOT NULL,
        [NetAmount] decimal(18,2) NOT NULL,
        [VatRate] decimal(5,4) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [DepositAmount] decimal(18,2) NOT NULL,
        [GrandTotal] decimal(18,2) NOT NULL,
        [TotalCost] decimal(18,2) NOT NULL,
        [SupersedesQuotationId] uniqueidentifier NULL,
        [SupersededByQuotationId] uniqueidentifier NULL,
        [RevisionReason] nvarchar(1000) NULL,
        [LockedByUserId] bigint NULL,
        [LockedByUserName] nvarchar(200) NULL,
        [LockedAt] datetime2 NULL,
        [ValidUntil] datetime2 NULL,
        [SentAt] datetime2 NULL,
        [CreatedByUserId] bigint NOT NULL,
        [CreatedByUserName] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastUpdatedByUserId] bigint NULL,
        [LastUpdatedAt] datetime2 NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_svc_Quotation] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_Shift] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [SupervisorStaffId] bigint NULL,
        [SupervisorName] nvarchar(200) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_svc_Shift] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_ShiftSession] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyUserId] bigint NOT NULL,
        [LegacyStaffId] bigint NULL,
        [UserName] nvarchar(100) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [Role] int NOT NULL,
        [ShiftId] uniqueidentifier NOT NULL,
        [ShiftName] nvarchar(100) NOT NULL,
        [BranchName] nvarchar(200) NOT NULL,
        [OpenedAt] datetime2 NOT NULL,
        [ClosedAt] datetime2 NULL,
        [ClosedByUserId] bigint NULL,
        [OpenedFrom] int NOT NULL,
        CONSTRAINT [PK_svc_ShiftSession] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_UserRoleOverride] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyUserId] bigint NOT NULL,
        [Role] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [SetByUserId] bigint NOT NULL,
        [SetAt] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_UserRoleOverride] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_QuotationApproval] (
        [Id] uniqueidentifier NOT NULL,
        [QuotationId] uniqueidentifier NOT NULL,
        [QuotationVersion] int NOT NULL,
        [SignatureImagePath] nvarchar(500) NOT NULL,
        [SignedAt] datetime2 NOT NULL,
        [ConsentText] nvarchar(2000) NOT NULL,
        [DeviceInfo] nvarchar(300) NULL,
        [WitnessEmployeeId] bigint NOT NULL,
        [WitnessEmployeeName] nvarchar(200) NOT NULL,
        [ApprovedLineCount] int NOT NULL,
        [RejectedLineCount] int NOT NULL,
        [ApprovedNetAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_svc_QuotationApproval] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_svc_QuotationApproval_svc_Quotation_QuotationId] FOREIGN KEY ([QuotationId]) REFERENCES [svc_Quotation] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE TABLE [svc_QuotationLine] (
        [Id] uniqueidentifier NOT NULL,
        [QuotationId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [CatalogCode] nvarchar(60) NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [Type] int NOT NULL,
        [Source] int NOT NULL,
        [InspectionItemId] uniqueidentifier NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Unit] nvarchar(40) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [DiscountPercent] decimal(5,2) NOT NULL,
        [Promotion] int NOT NULL,
        [AssignedTechnicianId] bigint NULL,
        [AssignedTechnicianName] nvarchar(200) NULL,
        [Note] nvarchar(1000) NULL,
        [StandardHours] decimal(6,2) NULL,
        [ApprovalStatus] int NOT NULL,
        [RejectReason] nvarchar(500) NULL,
        [DecidedAt] datetime2 NULL,
        [GrossAmount] decimal(18,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [PromotionAmount] decimal(18,2) NOT NULL,
        [NetAmount] decimal(18,2) NOT NULL,
        [CostAmount] decimal(18,2) NOT NULL,
        [MarginAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_svc_QuotationLine] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_svc_QuotationLine_svc_Quotation_QuotationId] FOREIGN KEY ([QuotationId]) REFERENCES [svc_Quotation] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_ActivityEvent_EntityId] ON [svc_ActivityEvent] ([EntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_ActivityEvent_LegacyShardKey_LegacyBranchId_LegacyJobId_OccurredAt] ON [svc_ActivityEvent] ([LegacyShardKey], [LegacyBranchId], [LegacyJobId], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_Attachment_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Attachment] ([LegacyShardKey], [LegacyBranchId], [LegacyJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_Attachment_RelativePath] ON [svc_Attachment] ([RelativePath]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_CatalogItem_LegacyShardKey_LegacyBranchId_Code] ON [svc_CatalogItem] ([LegacyShardKey], [LegacyBranchId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_Quotation_Code] ON [svc_Quotation] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_Quotation_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Quotation] ([LegacyShardKey], [LegacyBranchId], [LegacyJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_Quotation_LegacyShardKey_LegacyBranchId_Status] ON [svc_Quotation] ([LegacyShardKey], [LegacyBranchId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_QuotationApproval_QuotationId] ON [svc_QuotationApproval] ([QuotationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_QuotationLine_QuotationId] ON [svc_QuotationLine] ([QuotationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_Shift_LegacyShardKey_LegacyBranchId_SortOrder] ON [svc_Shift] ([LegacyShardKey], [LegacyBranchId], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_svc_ShiftSession_LegacyShardKey_LegacyUserId_ClosedAt] ON [svc_ShiftSession] ([LegacyShardKey], [LegacyUserId], [ClosedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_UserRoleOverride_LegacyShardKey_LegacyUserId] ON [svc_UserRoleOverride] ([LegacyShardKey], [LegacyUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260818190444_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260818190444_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831043936_AddJob'
)
BEGIN
    CREATE TABLE [svc_Job] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyJobId] bigint NOT NULL,
        [JobNo] nvarchar(40) NOT NULL,
        [Status] int NOT NULL,
        [PromiseAt] datetime2 NULL,
        [AssignedTechnicianId] bigint NULL,
        [AssignedTechnicianName] nvarchar(200) NULL,
        [MileageAtIntake] int NULL,
        [CreatedByUserId] bigint NOT NULL,
        [CreatedByUserName] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [Source] int NOT NULL,
        [OverdueReason] nvarchar(500) NULL,
        [CancelReason] nvarchar(500) NULL,
        [CancelledByUserId] bigint NULL,
        [CancelledAt] datetime2 NULL,
        [WasBackfilled] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_svc_Job] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831043936_AddJob'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_Job_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Job] ([LegacyShardKey], [LegacyBranchId], [LegacyJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831043936_AddJob'
)
BEGIN
    CREATE INDEX [IX_svc_Job_LegacyShardKey_LegacyBranchId_Status] ON [svc_Job] ([LegacyShardKey], [LegacyBranchId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831043936_AddJob'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831043936_AddJob', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831073028_AddIntakeChecklist'
)
BEGIN
    CREATE TABLE [svc_IntakeChecklist] (
        [Id] uniqueidentifier NOT NULL,
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [LegacyBranchId] int NOT NULL,
        [LegacyJobId] bigint NOT NULL,
        [CreatedByUserId] bigint NOT NULL,
        [CreatedByUserName] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SubmittedAt] datetime2 NULL,
        [SubmittedByUserId] bigint NULL,
        [SubmittedByUserName] nvarchar(200) NULL,
        CONSTRAINT [PK_svc_IntakeChecklist] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831073028_AddIntakeChecklist'
)
BEGIN
    CREATE TABLE [svc_IntakeChecklistItem] (
        [Id] uniqueidentifier NOT NULL,
        [IntakeChecklistId] uniqueidentifier NOT NULL,
        [CategoryKey] nvarchar(20) NOT NULL,
        [ItemCode] nvarchar(20) NOT NULL,
        [Result] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedByUserId] bigint NULL,
        [UpdatedByUserName] nvarchar(200) NULL,
        CONSTRAINT [PK_svc_IntakeChecklistItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_svc_IntakeChecklistItem_svc_IntakeChecklist_IntakeChecklistId] FOREIGN KEY ([IntakeChecklistId]) REFERENCES [svc_IntakeChecklist] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831073028_AddIntakeChecklist'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_IntakeChecklist_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_IntakeChecklist] ([LegacyShardKey], [LegacyBranchId], [LegacyJobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831073028_AddIntakeChecklist'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_IntakeChecklistItem_IntakeChecklistId_ItemCode] ON [svc_IntakeChecklistItem] ([IntakeChecklistId], [ItemCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831073028_AddIntakeChecklist'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831073028_AddIntakeChecklist', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_ActivityEvent;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_QuotationApproval;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_QuotationLine;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_Attachment;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_IntakeChecklistItem;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_IntakeChecklist;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_Quotation;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DELETE FROM svc_Job;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_Quotation_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Quotation];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_Quotation_LegacyShardKey_LegacyBranchId_Status] ON [svc_Quotation];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_Job_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Job];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_Job_LegacyShardKey_LegacyBranchId_Status] ON [svc_Job];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_IntakeChecklist_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_IntakeChecklist];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_Attachment_LegacyShardKey_LegacyBranchId_LegacyJobId] ON [svc_Attachment];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DROP INDEX [IX_svc_ActivityEvent_LegacyShardKey_LegacyBranchId_LegacyJobId_OccurredAt] ON [svc_ActivityEvent];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Quotation]') AND [c].[name] = N'LegacyBranchId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [svc_Quotation] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [svc_Quotation] DROP COLUMN [LegacyBranchId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Quotation]') AND [c].[name] = N'LegacyJobId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [svc_Quotation] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [svc_Quotation] DROP COLUMN [LegacyJobId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Quotation]') AND [c].[name] = N'LegacyShardKey');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [svc_Quotation] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [svc_Quotation] DROP COLUMN [LegacyShardKey];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Job]') AND [c].[name] = N'WasBackfilled');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [svc_Job] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [svc_Job] DROP COLUMN [WasBackfilled];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_IntakeChecklist]') AND [c].[name] = N'LegacyBranchId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [svc_IntakeChecklist] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [svc_IntakeChecklist] DROP COLUMN [LegacyBranchId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_IntakeChecklist]') AND [c].[name] = N'LegacyJobId');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [svc_IntakeChecklist] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [svc_IntakeChecklist] DROP COLUMN [LegacyJobId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var6 sysname;
    SELECT @var6 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_IntakeChecklist]') AND [c].[name] = N'LegacyShardKey');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [svc_IntakeChecklist] DROP CONSTRAINT [' + @var6 + '];');
    ALTER TABLE [svc_IntakeChecklist] DROP COLUMN [LegacyShardKey];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var7 sysname;
    SELECT @var7 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Attachment]') AND [c].[name] = N'LegacyBranchId');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [svc_Attachment] DROP CONSTRAINT [' + @var7 + '];');
    ALTER TABLE [svc_Attachment] DROP COLUMN [LegacyBranchId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var8 sysname;
    SELECT @var8 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Attachment]') AND [c].[name] = N'LegacyJobId');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [svc_Attachment] DROP CONSTRAINT [' + @var8 + '];');
    ALTER TABLE [svc_Attachment] DROP COLUMN [LegacyJobId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var9 sysname;
    SELECT @var9 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_Attachment]') AND [c].[name] = N'LegacyShardKey');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [svc_Attachment] DROP CONSTRAINT [' + @var9 + '];');
    ALTER TABLE [svc_Attachment] DROP COLUMN [LegacyShardKey];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var10 sysname;
    SELECT @var10 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_ActivityEvent]') AND [c].[name] = N'LegacyBranchId');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [svc_ActivityEvent] DROP CONSTRAINT [' + @var10 + '];');
    ALTER TABLE [svc_ActivityEvent] DROP COLUMN [LegacyBranchId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var11 sysname;
    SELECT @var11 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_ActivityEvent]') AND [c].[name] = N'LegacyJobId');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [svc_ActivityEvent] DROP CONSTRAINT [' + @var11 + '];');
    ALTER TABLE [svc_ActivityEvent] DROP COLUMN [LegacyJobId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    DECLARE @var12 sysname;
    SELECT @var12 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[svc_ActivityEvent]') AND [c].[name] = N'LegacyShardKey');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [svc_ActivityEvent] DROP CONSTRAINT [' + @var12 + '];');
    ALTER TABLE [svc_ActivityEvent] DROP COLUMN [LegacyShardKey];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    EXEC sp_rename N'[svc_Job].[LegacyJobId]', N'VehicleId', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    EXEC sp_rename N'[svc_Job].[LegacyBranchId]', N'JobTypeId', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Quotation] ADD [JobId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [BranchId] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [BranchName] nvarchar(200) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [CustomerId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [CustomerName] nvarchar(200) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [CustomerPhone] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [Detail] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [JobTypeName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [SenderName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [SenderPhoneNumber] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [VehicleImagePath] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [VehicleModel] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [VehicleRegistration] nvarchar(40) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Job] ADD [VehicleVin] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_IntakeChecklist] ADD [JobId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Attachment] ADD [JobId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_ActivityEvent] ADD [JobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE TABLE [svc_JobNumberCounter] (
        [LegacyShardKey] nvarchar(20) NOT NULL,
        [BranchId] int NOT NULL,
        [CounterDate] date NOT NULL,
        [LastSequence] int NOT NULL,
        CONSTRAINT [PK_svc_JobNumberCounter] PRIMARY KEY ([LegacyShardKey], [BranchId], [CounterDate])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Quotation_JobId] ON [svc_Quotation] ([JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Quotation_Status] ON [svc_Quotation] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Job_JobNo] ON [svc_Job] ([JobNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Job_LegacyShardKey_BranchId_Status] ON [svc_Job] ([LegacyShardKey], [BranchId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Job_LegacyShardKey_BranchId_VehicleId_Status] ON [svc_Job] ([LegacyShardKey], [BranchId], [VehicleId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_svc_IntakeChecklist_JobId] ON [svc_IntakeChecklist] ([JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_Attachment_JobId] ON [svc_Attachment] ([JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    CREATE INDEX [IX_svc_ActivityEvent_JobId_OccurredAt] ON [svc_ActivityEvent] ([JobId], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_ActivityEvent] ADD CONSTRAINT [FK_svc_ActivityEvent_svc_Job_JobId] FOREIGN KEY ([JobId]) REFERENCES [svc_Job] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Attachment] ADD CONSTRAINT [FK_svc_Attachment_svc_Job_JobId] FOREIGN KEY ([JobId]) REFERENCES [svc_Job] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_IntakeChecklist] ADD CONSTRAINT [FK_svc_IntakeChecklist_svc_Job_JobId] FOREIGN KEY ([JobId]) REFERENCES [svc_Job] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    ALTER TABLE [svc_Quotation] ADD CONSTRAINT [FK_svc_Quotation_svc_Job_JobId] FOREIGN KEY ([JobId]) REFERENCES [svc_Job] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831140401_RebuildJobAggregate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831140401_RebuildJobAggregate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    ALTER TABLE [svc_CatalogItem] ADD [CategoryId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    ALTER TABLE [svc_CatalogItem] ADD [WarehouseId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE TABLE [svc_CatalogCategory] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ParentCategoryId] uniqueidentifier NULL,
        [SortOrder] int NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_CatalogCategory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_svc_CatalogCategory_svc_CatalogCategory_ParentCategoryId] FOREIGN KEY ([ParentCategoryId]) REFERENCES [svc_CatalogCategory] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE TABLE [svc_Supplier] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(300) NOT NULL,
        [ContactName] nvarchar(150) NULL,
        [Phone] nvarchar(30) NULL,
        [Email] nvarchar(150) NULL,
        [Address] nvarchar(500) NULL,
        [TaxId] nvarchar(20) NULL,
        [PaymentTerms] nvarchar(100) NULL,
        [Note] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_Supplier] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE TABLE [svc_Warehouse] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [LegacyShardKey] nvarchar(20) NULL,
        [LegacyBranchId] int NULL,
        [Address] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_Warehouse] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE TABLE [svc_CatalogItemSupplier] (
        [Id] uniqueidentifier NOT NULL,
        [CatalogItemId] uniqueidentifier NOT NULL,
        [SupplierId] uniqueidentifier NOT NULL,
        [SupplierItemCode] nvarchar(60) NULL,
        [SupplierCost] decimal(18,2) NULL,
        [LeadTimeDays] int NULL,
        [MinOrderQty] int NULL,
        [IsPreferred] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        CONSTRAINT [PK_svc_CatalogItemSupplier] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_svc_CatalogItemSupplier_svc_CatalogItem_CatalogItemId] FOREIGN KEY ([CatalogItemId]) REFERENCES [svc_CatalogItem] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_svc_CatalogItemSupplier_svc_Supplier_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [svc_Supplier] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE INDEX [IX_svc_CatalogItem_CategoryId] ON [svc_CatalogItem] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE INDEX [IX_svc_CatalogItem_WarehouseId] ON [svc_CatalogItem] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE INDEX [IX_svc_CatalogCategory_ParentCategoryId] ON [svc_CatalogCategory] ([ParentCategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE UNIQUE INDEX [UX_svc_CatalogCategory_Code] ON [svc_CatalogCategory] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE INDEX [IX_svc_CatalogItemSupplier_SupplierId] ON [svc_CatalogItemSupplier] ([SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE UNIQUE INDEX [UX_svc_CatalogItemSupplier_Item_Supplier] ON [svc_CatalogItemSupplier] ([CatalogItemId], [SupplierId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_svc_CatalogItemSupplier_Preferred] ON [svc_CatalogItemSupplier] ([CatalogItemId]) WHERE [IsPreferred] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE UNIQUE INDEX [UX_svc_Supplier_Code] ON [svc_Supplier] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    CREATE UNIQUE INDEX [UX_svc_Warehouse_Code] ON [svc_Warehouse] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    ALTER TABLE [svc_CatalogItem] ADD CONSTRAINT [FK_svc_CatalogItem_svc_CatalogCategory_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [svc_CatalogCategory] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    ALTER TABLE [svc_CatalogItem] ADD CONSTRAINT [FK_svc_CatalogItem_svc_Warehouse_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [svc_Warehouse] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903091755_Add_Supplier_Warehouse_Category'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903091755_Add_Supplier_Warehouse_Category', N'8.0.11');
END;
GO

COMMIT;
GO

