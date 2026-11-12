using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Extra employees allowed to work on a client (besides the AccountManager).
/// Table: dbo.ClientAssignments  (PK = ClientId + UserId)
/// </summary>
public class ClientAssignment
{
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public bool CanEdit { get; set; } = true;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
}
