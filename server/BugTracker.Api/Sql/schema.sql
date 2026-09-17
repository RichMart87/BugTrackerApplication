/*
    BugTracker - SQL Server schema
    ------------------------------
    The API creates this table automatically on first run (EnsureCreated), so this
    script is here for DBA review and for environments where schema changes are
    applied through a release pipeline rather than by the application.

    Usage:
        sqlcmd -S (localdb)\MSSQLLocalDB -i schema.sql
*/

IF DB_ID(N'BugTracker') IS NULL
BEGIN
    CREATE DATABASE [BugTracker];
END
GO

USE [BugTracker];
GO

IF OBJECT_ID(N'dbo.Bugs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bugs
    (
        Id                INT             IDENTITY(1,1) NOT NULL,
        Title             NVARCHAR(200)   NOT NULL,
        DateFound         DATE            NOT NULL,
        Description       NVARCHAR(4000)  NOT NULL,
        Severity          NVARCHAR(20)    NOT NULL,
        [Environment]     NVARCHAR(20)    NOT NULL,
        Notes             NVARCHAR(4000)  NULL,
        Resolved          BIT             NOT NULL CONSTRAINT DF_Bugs_Resolved DEFAULT (0),
        DateResolved      DATE            NULL,
        FoundByAutomation BIT             NOT NULL CONSTRAINT DF_Bugs_FoundByAutomation DEFAULT (0),
        CreatedUtc        DATETIME2(7)    NOT NULL CONSTRAINT DF_Bugs_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc        DATETIME2(7)    NOT NULL CONSTRAINT DF_Bugs_UpdatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_Bugs PRIMARY KEY CLUSTERED (Id),

        -- Enum values are stored as text to keep the table readable for ad-hoc SQL and BI tools.
        CONSTRAINT CK_Bugs_Severity CHECK (Severity IN (N'Critical', N'High', N'Medium', N'Low')),
        CONSTRAINT CK_Bugs_Environment CHECK ([Environment] IN (N'Local', N'QA', N'Staging', N'Production')),

        -- A resolved defect must carry a close date, an open one must not.
        CONSTRAINT CK_Bugs_ResolvedDate CHECK (
            (Resolved = 1 AND DateResolved IS NOT NULL) OR
            (Resolved = 0 AND DateResolved IS NULL)
        ),
        CONSTRAINT CK_Bugs_ResolvedAfterFound CHECK (DateResolved IS NULL OR DateResolved >= DateFound)
    );
END
GO

-- Drives the month-over-month rollup and the default "newest first" grid ordering.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bugs_DateFound' AND object_id = OBJECT_ID(N'dbo.Bugs'))
    CREATE INDEX IX_Bugs_DateFound ON dbo.Bugs (DateFound DESC) INCLUDE (Resolved, Severity, [Environment]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bugs_Resolved' AND object_id = OBJECT_ID(N'dbo.Bugs'))
    CREATE INDEX IX_Bugs_Resolved ON dbo.Bugs (Resolved);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bugs_Severity_Environment' AND object_id = OBJECT_ID(N'dbo.Bugs'))
    CREATE INDEX IX_Bugs_Severity_Environment ON dbo.Bugs (Severity, [Environment]);
GO

/* Month-over-month reporting query used by GET /api/bugs/stats/monthly. */
-- SELECT  FORMAT(DATEFROMPARTS(YEAR(DateFound), MONTH(DateFound), 1), 'yyyy-MM') AS [Month],
--         COUNT(*) AS Found,
--         SUM(CASE WHEN Resolved = 1 THEN 1 ELSE 0 END) AS Resolved
-- FROM    dbo.Bugs
-- GROUP BY YEAR(DateFound), MONTH(DateFound)
-- ORDER BY [Month];
