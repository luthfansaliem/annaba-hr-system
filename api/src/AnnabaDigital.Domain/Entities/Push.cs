namespace AnnabaDigital.Domain.Entities;

/// <summary>A browser or installed app on one device that accepted push notifications for a user (Web Push). Removed when the push service says it is gone.</summary>
public class PushSubscription : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = "";
    /// <summary>The device's P-256 public key and authentication secret (base64url), needed to encrypt each message for it.</summary>
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? Device { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int Failures { get; set; }
}
