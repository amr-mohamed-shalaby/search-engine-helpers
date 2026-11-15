# Department Management System — Architecture Foundation

## Architecture decision

The product remains a .NET MAUI Blazor Hybrid desktop application. A central ASP.NET Core API hosted on Windows Server/IIS will own shared identity, authorization, business operations, audit records, and database access. SQL Server is the planned central database.

## Projects

- `DepartmentManagement.Maui` (existing `CsvProjectXml.Maui` during migration): desktop UI, local/USB file access, and API client.
- `DepartmentManagement.Api`: authenticated HTTP boundary hosted centrally.
- `DepartmentManagement.Contracts`: request/response types shared across process boundaries.
- `DepartmentManagement.Application`: use cases and orchestration.
- `DepartmentManagement.Domain`: business entities and rules.
- `DepartmentManagement.Infrastructure`: SQL Server, Identity persistence, audit persistence, and server file storage.
- `CsvProjectXml.Core`: existing conversion engine; retained without a rewrite.

## Dependency rules

- Domain has no project dependencies.
- Application references Domain.
- Infrastructure references Application and Domain.
- API references Application, Infrastructure, and Contracts.
- MAUI will reference Contracts and call the API; it must not reference Infrastructure or connect directly to SQL Server.
- CsvProjectXml.Core remains reusable and is initially used by the MAUI client for local-file conversion.

## File processing

Local and USB files are selected and read by MAUI. The client uses streaming and bounded buffers, then sends only required results or explicitly uploaded content to the API. Server/shared files are accessed by server-side services using least-privilege service accounts.

## Security and audit principles

- ASP.NET Core Identity is the planned central identity store, with Admin and Employee roles.
- The API, not the UI, enforces authorization.
- Passwords, password hashes, tokens, and sensitive file contents are never written to audit logs.
- Audit events are append-only through normal application operations.
- Employee computers never receive SQL Server credentials.

## Migration policy

This foundation deliberately adds projects and a health endpoint only. It does not yet enable authentication, database migrations, or API-backed conversion. Those are separate, testable phases. The existing MAUI project name and conversion implementation are retained until the new architecture is working.
