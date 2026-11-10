using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record DocumentView(Guid Id, string Category, string FileName, string ContentType, int SizeBytes, DateOnly? ExpiresOn, DateTimeOffset UploadedAt);
public sealed record UploadDocumentRequest(string? FileName, string? Category, string? ContentBase64, DateOnly? ExpiresOn);
public sealed record DocumentFile(string FileName, string ContentType, byte[] Bytes);

/// <summary>
/// EMP-005. Contracts, ID cards and certificates on the employee's record. The type of a file is decided from its own first bytes (PDF, PNG, JPEG only) — a renamed
/// executable is refused whatever its name or the browser claims — size is capped, names are cleaned, and listing, opening, uploading and removing each need a recent
/// password check and are audited. Removal hides the file (it stays in the database for the audit trail).
/// </summary>
public sealed class DocumentService(IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const int MaxPerEmployee = 50;
    public static readonly string[] Categories = ["contract", "id_card", "tax", "certificate", "medical", "other"];

    private async Task GuardAsync(SessionPrincipal actor, CancellationToken ct)
        => Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);

    /// <summary>Returns the real content type from the file signature, or null when it is not a PDF, PNG or JPEG.</summary>
    public static (string ContentType, string Extension)? Sniff(byte[] b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return ("application/pdf", ".pdf");
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ("image/png", ".png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", ".jpg");
        return null;
    }

    private static string CleanName(string? name, string ext)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension((name ?? "").Replace('\\', '/').Split('/').Last());
        var safe = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')').ToArray()).Trim().TrimStart('.');
        if (safe.Length == 0) safe = "document"; if (safe.Length > 120) safe = safe[..120];
        return safe + ext;
    }

    private static DocumentView View(EmployeeDocument d) => new(d.Id, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.ExpiresOn, d.UploadedAt);

    public async Task<IReadOnlyList<DocumentView>> ListAsync(Guid employeeId, SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw DomainException.NotFound("Employee");
        return [.. (await db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == employeeId && d.DeletedAt == null).OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.ExpiresOn, d.UploadedAt }).ToListAsync(ct)).Select(d => new DocumentView(d.Id, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.ExpiresOn, d.UploadedAt))];
    }

    public async Task<DocumentView> UploadAsync(Guid employeeId, UploadDocumentRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        var errors = new Dictionary<string, string>();
        var category = (r.Category ?? "other").Trim().ToLowerInvariant(); if (!Categories.Contains(category)) errors["category"] = "Choose: " + string.Join(", ", Categories);
        byte[] bytes = [];
        if (string.IsNullOrWhiteSpace(r.ContentBase64)) errors["file"] = "Choose a file.";
        else if (r.ContentBase64.Length > MaxBytes * 4 / 3 + 16) errors["file"] = $"The file is larger than {MaxBytes / 1024 / 1024} MB.";
        else { try { bytes = Convert.FromBase64String(r.ContentBase64); } catch (FormatException) { errors["file"] = "The file could not be read."; } }
        if (errors.Count == 0 && bytes.Length is 0 or > MaxBytes) errors["file"] = $"The file must be between 1 byte and {MaxBytes / 1024 / 1024} MB.";
        var kind = errors.ContainsKey("file") ? null : Sniff(bytes);
        if (errors.Count == 0 && kind is null) errors["file"] = "Only PDF, PNG and JPEG files are accepted.";
        if (r.ExpiresOn is { } ex && ex < DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddYears(-1)) errors["expiresOn"] = "Check the expiry date.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the document.", errors);
        if (await db.EmployeeDocuments.CountAsync(d => d.EmployeeId == employeeId && d.DeletedAt == null, ct) >= MaxPerEmployee) throw DomainException.Invariant($"An employee can have at most {MaxPerEmployee} documents.");
        var doc = new EmployeeDocument
        {
            EmployeeId = employeeId, Category = category, FileName = CleanName(r.FileName, kind!.Value.Extension), ContentType = kind.Value.ContentType, SizeBytes = bytes.Length, Content = bytes,
            ExpiresOn = r.ExpiresOn, UploadedBy = actor.UserId, UploadedAt = clock.UtcNow,
        };
        db.EmployeeDocuments.Add(doc);
        audit.Record("hr.document.upload", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["category"] = category, ["type"] = doc.ContentType, ["bytes"] = bytes.Length.ToString() });
        await db.SaveChangesAsync(ct);
        return View(doc);
    }

    public async Task<DocumentFile> DownloadAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var d = await db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("Document");
        audit.Record("hr.document.download", "employee", d.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["category"] = d.Category, ["file"] = d.FileName });
        await db.SaveChangesAsync(ct);
        return new DocumentFile(d.FileName, d.ContentType, d.Content);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var d = await db.EmployeeDocuments.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("Document");
        d.DeletedAt = clock.UtcNow;
        audit.Record("hr.document.delete", "employee", d.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["category"] = d.Category, ["file"] = d.FileName });
        await db.SaveChangesAsync(ct);
    }
}
