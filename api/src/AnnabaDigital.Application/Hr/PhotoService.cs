using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record SetPhotoRequest(string? ContentBase64);

/// <summary>
/// EMP-001 / ATT-002. The portrait on an employee record and the optional picture at clock-in. Only PNG and JPEG (decided from the bytes), at most 1 MB, virus-scanned like every upload.
/// A portrait is set by HR and visible to whoever may read employees and to the person themself. A clock-in picture is visible to people who may read everyone's attendance and to its owner,
/// and is deleted after 90 days: it exists to settle "was it really you", not to build an archive of faces.
/// </summary>
public sealed class PhotoService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, IFileScanner scanner)
{
    public const int MaxBytes = 1024 * 1024; public const int ClockInKeepDays = 90;

    /// <summary>Checks type, size and (when configured) the virus scanner. Returns the real content type.</summary>
    internal async Task<(byte[] Bytes, string ContentType)> AcceptAsync(string? base64, string field, CancellationToken ct)
    {
        Dictionary<string, string> Bad(string m) => new() { [field] = m };
        if (string.IsNullOrWhiteSpace(base64)) throw DomainException.Validation("Please check the picture.", Bad("Choose a picture."));
        if (base64.Length > MaxBytes * 4 / 3 + 16) throw DomainException.Validation("Please check the picture.", Bad("The picture is larger than 1 MB."));
        byte[] bytes; try { bytes = Convert.FromBase64String(base64); } catch (FormatException) { throw DomainException.Validation("Please check the picture.", Bad("The picture could not be read.")); }
        var kind = DocumentService.Sniff(bytes);
        if (bytes.Length is 0 or > MaxBytes || kind is null || kind.Value.ContentType == "application/pdf") throw DomainException.Validation("Please check the picture.", Bad("Only PNG and JPEG pictures up to 1 MB are accepted."));
        if (scanner.Enabled)
        {
            ScanResult scan;
            try { scan = await scanner.ScanAsync(bytes, ct); }
            catch (Exception scanError) when (scanError is not OperationCanceledException || !ct.IsCancellationRequested) { throw DomainException.Invariant("The virus scanner cannot be reached right now, so the picture was not stored. Try again in a moment."); }
            if (scan.Verdict == ScanVerdict.Infected) throw DomainException.Validation("Please check the picture.", Bad("The virus scanner rejected this file."));
        }
        return (bytes, kind.Value.ContentType);
    }

    public async Task SetEmployeePhotoAsync(Guid employeeId, SetPhotoRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        var (bytes, type) = await AcceptAsync(r.ContentBase64, "photo", ct);
        await db.HrPhotos.Where(p => p.Kind == "employee" && p.OwnerId == employeeId).ExecuteDeleteAsync(ct);
        db.HrPhotos.Add(new HrPhoto { Kind = "employee", OwnerId = employeeId, EmployeeId = employeeId, ContentType = type, Content = bytes, CreatedAt = clock.UtcNow });
        audit.Record("hr.employee.photo_set", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["bytes"] = bytes.Length.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveEmployeePhotoAsync(Guid employeeId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (await db.HrPhotos.Where(p => p.Kind == "employee" && p.OwnerId == employeeId).ExecuteDeleteAsync(ct) == 0) throw DomainException.NotFound("Photo");
        audit.Record("hr.employee.photo_remove", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The portrait, for people who may read employees and for the employee themself.</summary>
    public async Task<DocumentFile> EmployeePhotoAsync(Guid employeeId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var own = await db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId && e.UserId == actor.UserId, ct);
        if (!own && !(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesRead)) throw DomainException.NotFound("Photo");
        var p = await db.HrPhotos.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == "employee" && x.OwnerId == employeeId, ct) ?? throw DomainException.NotFound("Photo");
        return new DocumentFile("photo", p.ContentType, p.Content);
    }

    /// <summary>The picture taken at a clock-in, for its owner and for people who may read everyone's attendance. Looking at someone else's is audited.</summary>
    public async Task<DocumentFile> ClockInPhotoAsync(Guid logId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.HrPhotos.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == "clock_in" && x.OwnerId == logId, ct) ?? throw DomainException.NotFound("Photo");
        var own = await db.Employees.AsNoTracking().AnyAsync(e => e.Id == p.EmployeeId && e.UserId == actor.UserId, ct);
        if (!own)
        {
            if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.AttendanceReadAll)) throw DomainException.NotFound("Photo");
            audit.Record("hr.attendance.photo_viewed", "employee", p.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        }
        return new DocumentFile("clock-in", p.ContentType, p.Content);
    }

    /// <summary>Deletes clock-in pictures older than 90 days (current organisation).</summary>
    public async Task<int> PurgeOldClockInPhotosAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-ClockInKeepDays);
        return await db.HrPhotos.Where(p => p.Kind == "clock_in" && p.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
