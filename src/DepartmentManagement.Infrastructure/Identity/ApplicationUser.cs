using Microsoft.AspNetCore.Identity;

namespace DepartmentManagement.Infrastructure.Identity;

/// <summary>The application user stored by ASP.NET Core Identity.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
