using DepartmentManagement.Contracts;
using DepartmentManagement.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .WithName("HealthCheck")
    .WithTags("System");

app.Run();

public partial class Program { }
