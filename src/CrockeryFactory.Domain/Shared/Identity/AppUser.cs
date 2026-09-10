using Microsoft.AspNetCore.Identity;

namespace CrockeryFactory.Shared.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}
