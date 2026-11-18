BEGIN TRANSACTION;
CREATE TABLE [RefreshTokens] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [TokenHash] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [ExpiresAtUtc] datetimeoffset NOT NULL,
    [RevokedAtUtc] datetimeoffset NULL,
    [ReplacedByTokenHash] nvarchar(64) NULL,
    [CreatedByIp] nvarchar(64) NULL,
    [RevokedByIp] nvarchar(64) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RefreshTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);

CREATE INDEX [IX_RefreshTokens_UserId_ExpiresAtUtc] ON [RefreshTokens] ([UserId], [ExpiresAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117131327_AddRefreshTokens', N'10.0.0');

COMMIT;
GO

