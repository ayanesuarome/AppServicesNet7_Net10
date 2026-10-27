using CryptographyLib; // Protector
using System.Security; // SecurityException
using System.Security.Principal; // IPrincipal
using System.Security.Claims; // ClaimsPrincipal, Claim

Protector.Register("Alice", "Pa$$w0rd", roles: ["Admins"]);
Protector.Register("Bob", "Pa$$w0rd", roles: ["Sales", "TeamLeads"]);

// Eve is not a member of any roles
Protector.Register("Eve", "Pa$$w0rd");

// prompt user to enter username and password to login
// as one of these three users
Write($"Enter your username: ");
string? username = ReadLine()!;

Write($"Enter your password: ");
string? password = ReadLine()!;

Protector.LogIn(username, password);

if (Thread.CurrentPrincipal is null)
{
    WriteLine("Log in failed");
    return;
}

IPrincipal principal = Thread.CurrentPrincipal;

WriteLine($"IsAuthenticated: {principal.Identity?.IsAuthenticated}");
WriteLine($"AuthenticationType: {principal.Identity?.AuthenticationType}");
WriteLine($"Name: {principal.Identity?.Name}");
WriteLine($"IsInRole(\"Admins\"): {principal.IsInRole("Admins")}");
WriteLine($"IsInRole(\"Sales\"): {principal.IsInRole("Sales")}");

if (principal is ClaimsPrincipal)
{
    WriteLine($"{principal.Identity?.Name} has the following claims:");
    IEnumerable<Claim>? claims = (principal as ClaimsPrincipal)?.Claims;

    if (claims is not null)
    {
        foreach (Claim claim in claims)
        {
            WriteLine($"{claim.Type}: {claim.Value}");
        }
    }
}

try
{
    SecureFeature();
}
catch (Exception ex)
{
    WriteLine($"{ex.GetType()}: {ex.Message}");
}

static void SecureFeature()
{
    if (Thread.CurrentPrincipal == null)
    {
        throw new SecurityException(
        "A user must be logged in to access this feature.");
    }
    if (!Thread.CurrentPrincipal.IsInRole("Admins"))
    {
        throw new SecurityException("User must be a member of Admins to access this feature.");
    }

    WriteLine("You have access to this secure feature.");
}