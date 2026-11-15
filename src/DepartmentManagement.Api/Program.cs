using DepartmentManagement.Contracts;

var builder = WebApplication.CreateBuilder(args);

// SQL Server, ASP.NET Core Identity, authentication, and authorization are introduced
// in the next implementation phases. Keep this first API host intentionally minimal.
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .WithName("HealthCheck")
    .WithTags("System");

app.Run();

public partial class Program { }
