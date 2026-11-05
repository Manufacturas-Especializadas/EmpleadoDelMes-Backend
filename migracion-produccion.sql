BEGIN TRANSACTION;
CREATE TABLE [CampaignAutomationAudit] (
    [Id] uniqueidentifier NOT NULL,
    [AdministratorId] uniqueidentifier NOT NULL,
    [ChangedAtUtc] datetimeoffset NOT NULL,
    [PreviousValues] nvarchar(4000) NOT NULL,
    [NewValues] nvarchar(4000) NOT NULL,
    CONSTRAINT [PK_CampaignAutomationAudit] PRIMARY KEY ([Id])
);

CREATE TABLE [CampaignAutomationConfiguration] (
    [Id] int NOT NULL,
    [IsEnabled] bit NOT NULL,
    [Calendar_NominationOpeningDayOffset] int NULL,
    [Calendar_NominationOpeningLocalTime] time(0) NULL,
    [Calendar_NominationClosingDayOffset] int NULL,
    [Calendar_NominationClosingLocalTime] time(0) NULL,
    [Calendar_OpinionOpeningDayOffset] int NULL,
    [Calendar_OpinionOpeningLocalTime] time(0) NULL,
    [Calendar_OpinionClosingDayOffset] int NULL,
    [Calendar_OpinionClosingLocalTime] time(0) NULL,
    [UpdatedAtUtc] datetimeoffset NULL,
    [UpdatedByUserId] uniqueidentifier NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CampaignAutomationConfiguration] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Automation_Singleton] CHECK ([Id] = 1)
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'IsEnabled', N'UpdatedAtUtc', N'UpdatedByUserId') AND [object_id] = OBJECT_ID(N'[CampaignAutomationConfiguration]'))
    SET IDENTITY_INSERT [CampaignAutomationConfiguration] ON;
INSERT INTO [CampaignAutomationConfiguration] ([Id], [IsEnabled], [UpdatedAtUtc], [UpdatedByUserId])
VALUES (1, CAST(0 AS bit), NULL, NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'IsEnabled', N'UpdatedAtUtc', N'UpdatedByUserId') AND [object_id] = OBJECT_ID(N'[CampaignAutomationConfiguration]'))
    SET IDENTITY_INSERT [CampaignAutomationConfiguration] OFF;

CREATE INDEX [IX_CampaignAutomationAudit_ChangedAtUtc] ON [CampaignAutomationAudit] ([ChangedAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261105155610_AddCampaignAutomationConfiguration', N'10.0.14');

COMMIT;
GO

