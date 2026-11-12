namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// The organisation's settings for the HR assistant: off until an administrator turns it on (and the server has a language-model key),
/// and the HR policy text the assistant answers from (company regulations, leave and claim rules, FAQ).
/// </summary>
public class AssistantSettings : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool Enabled { get; set; }
    public string PolicyText { get; set; } = "";
    public bool AllowPayData { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
