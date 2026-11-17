using DepartmentManagement.Contracts;
using DepartmentManagement.Infrastructure.DependencyInjection;
using DepartmentManagement.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Explicit one-time setup command. Apply the reviewed EF migration first.
if (args.Contains("--provision-identity", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await IdentityDataSeeder.ProvisionAsync(scope.ServiceProvider);
    return;
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .WithName("HealthCheck")
    .WithTags("System");

await app.RunAsync();

public partial class Program { }
