namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// A key another system uses to read data through the public API (/api/public/v1). Read-only, limited to its scopes, optionally to some IP addresses, and it can expire.
/// Only a hash of the key is stored; the key itself is shown once when it is created.
/// </summary>
public class ApiKey : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>The first characters of the key, to recognise it in lists and logs.</summary>
    public string Prefix { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public string? AllowedIps { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>How the bank transfer file is laid out for a bank's bulk upload: separator, header row, the columns in order, and how amounts and dates are written.</summary>
public class BankFileLayout : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string Delimiter { get; set; } = ",";
    public bool Header { get; set; } = true;
    /// <summary>Comma-separated column tokens, e.g. account_no,name,amount,reference (see IntegrationService.BankTokens).</summary>
    public string Columns { get; set; } = "";
    public int AmountDecimals { get; set; } = 2;
    public string DateFormat { get; set; } = "yyyyMMdd";
    public string? SourceAccount { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The company's own chart-of-account code and name for one line of the payroll journal.</summary>
public class JournalAccount : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Line { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
