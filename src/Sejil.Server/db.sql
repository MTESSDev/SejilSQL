-- Copyright (C) 2017 Alaa Masoud
-- See the LICENSE file in the project root for more information.

-- SQL Server schema used by SejilSQL. Idempotent: only creates what is missing.
-- Run it once per database, with a login that may create schemas and tables.

IF SCHEMA_ID(N'Journal') IS NULL EXEC(N'CREATE SCHEMA [Journal]');
GO

IF OBJECT_ID(N'[Journal].[log]') IS NULL
BEGIN
    CREATE TABLE [Journal].[log] (
        id        bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Journal_log PRIMARY KEY NONCLUSTERED,
        timestamp datetime2      NOT NULL,
        sourceApp nvarchar(200)  NOT NULL,
        message   nvarchar(max)  NULL,
        level     int            NOT NULL,
        exception nvarchar(max)  NULL
    );
    -- Pages (TOP n ORDER BY timestamp DESC) and purge by date.
    CREATE CLUSTERED INDEX IX_Journal_log_timestamp ON [Journal].[log] (timestamp, id);
    CREATE INDEX IX_Journal_log_sourceApp ON [Journal].[log] (sourceApp, timestamp);
END
GO

IF OBJECT_ID(N'[Journal].[log_property]') IS NULL
BEGIN
    CREATE TABLE [Journal].[log_property] (
        logId     bigint         NOT NULL,
        timestamp datetime2      NOT NULL,
        name      nvarchar(256)  NOT NULL,
        value     nvarchar(max)  NULL
    );
    -- Join l.timestamp = p.timestamp AND l.id = p.logId, and purge by date.
    CREATE CLUSTERED INDEX IX_Journal_log_property_timestamp ON [Journal].[log_property] (timestamp, logId);
    -- Search by property: logId IN (SELECT logId ... WHERE name = @q0 AND value ...).
    CREATE INDEX IX_Journal_log_property_name ON [Journal].[log_property] (name);
END
GO

IF OBJECT_ID(N'[Journal].[log_query]') IS NULL
BEGIN
    CREATE TABLE [Journal].[log_query] (
        id    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Journal_log_query PRIMARY KEY,
        name  nvarchar(255)     NOT NULL,
        query nvarchar(max)     NOT NULL
    );
    CREATE INDEX IX_Journal_log_query_name ON [Journal].[log_query] (name);
END
GO

-- Minimum log level shared by the applications (ISejilSettings.LevelId), one row per identifier.
IF OBJECT_ID(N'[Journal].[log_config]') IS NULL
BEGIN
    CREATE TABLE [Journal].[log_config] (
        id    nvarchar(20) NOT NULL CONSTRAINT PK_Journal_log_config PRIMARY KEY,
        value nvarchar(50) NOT NULL
    );
END
GO
