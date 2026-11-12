namespace AnnabaDigital.Domain.Entities;

/// <summary>A report someone built in the report builder: which data, columns, filters and grouping. Shared reports are listed for everyone allowed to see that data.</summary>
public class SavedReport : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string Dataset { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public Guid OwnerUserId { get; set; }
    public bool Shared { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
