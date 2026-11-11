using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record SealVerification(bool Valid, string? Kind, string? Company, string? Reference, string? Holder, DateOnly? IssuedOn, bool? AmountMatches);

/// <summary>
/// A verification code printed on employment letters and payslips, so that whoever receives the paper (a bank, an embassy, a landlord) can check with the issuing system that it is genuine
/// and unchanged. It is not a certificate-based digital signature: authenticity rests on asking this system, which answers with the few facts needed to compare
/// (company, reference, holder, date) and, for a payslip, only whether a take-home amount the verifier types in matches. Amounts are never sent out unprompted.
/// Codes are random (60 bits), unguessable in practice, and unknown codes simply answer "not found".
/// </summary>
public sealed class DocumentSealService(IAppDb db, IClock clock, IRateLimiter limiter)
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // Crockford base32: no I, L, O, U

    public static string Format(string code) => $"{code[..4]}-{code[4..8]}-{code[8..]}";
    public static string Normalise(string? code) => new string((code ?? "").ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1').Where(Alphabet.Contains).ToArray());

    /// <summary>The seal of a document, created on first use (one per document). The caller saves.</summary>
    public async Task<string> IssueAsync(string kind, Guid refId, string company, string reference, string holder, CancellationToken ct = default)
    {
        var existing = await db.DocumentSeals.AsNoTracking().Where(s => s.Kind == kind && s.RefId == refId).Select(s => s.Code).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;
        var bytes = RandomNumberGenerator.GetBytes(12); var code = new string(bytes.Select(b => Alphabet[b % 32]).ToArray());
        db.DocumentSeals.Add(new DocumentSeal { Kind = kind, RefId = refId, Code = code, Company = company, Reference = reference, Holder = holder, IssuedAt = clock.UtcNow });
        return code;
    }

    /// <summary>Looks a code up across all organisations (the verifier is an outsider and has only the code).</summary>
    public async Task<SealVerification> VerifyAsync(string? code, decimal? amount, ClientInfo client, CancellationToken ct = default)
    {
        var hit = await limiter.HitAsync($"verify:ip:{client.Ip ?? "unknown"}", 30, TimeSpan.FromMinutes(10), ct);
        if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var c = Normalise(code); if (c.Length != 12) return new SealVerification(false, null, null, null, null, null, null);
        var s = await db.DocumentSeals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Code == c, ct);
        if (s is null) return new SealVerification(false, null, null, null, null, null, null);
        bool? matches = null;
        if (s.Kind == "payslip" && amount is { } a)
            matches = await db.PayRunItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.Id == s.RefId && i.Net == a, ct);
        if (s.Kind == "letter" && !await db.EmploymentLetters.IgnoreQueryFilters().AsNoTracking().AnyAsync(l => l.Id == s.RefId && l.Status == RequestStatus.Approved, ct)) return new SealVerification(false, null, null, null, null, null, null);
        return new SealVerification(true, s.Kind, s.Company, s.Reference, s.Holder, DateOnly.FromDateTime(s.IssuedAt.UtcDateTime.AddHours(7)), matches);
    }

    /// <summary>"Siti Rahmawati" → "S*** R********": enough to recognise, not enough to learn.</summary>
    public static string Mask(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0] + new string('*', Math.Max(p.Length - 1, 1))));
}
