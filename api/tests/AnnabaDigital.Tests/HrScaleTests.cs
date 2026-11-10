using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;
using Xunit.Abstractions;

namespace AnnabaDigital.Tests;

/// <summary>PRD §7.3: 5,000 employees per tenant. A payroll run must finish in under 5 minutes and list/search pages must stay fast (the bounds here are generous so a slow CI box does not flake; the real numbers are printed).</summary>
[Collection("db")]
public sealed class HrScaleTests(PostgresFixture fx, ITestOutputHelper output)
{
    private const int People = 5000;

    [Fact]
    public async Task A_five_thousand_person_company_can_be_listed_searched_and_paid_in_a_reasonable_time()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        async Task<TestBrowser> As(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
        var hr = await As("hr_admin"); var officer = await As("payroll_officer"); var approver = await As("payroll_approver");

        // ---- load 5,000 people straight into the database (the import endpoint is capped at 1,000 rows per call by design)
        var sw = Stopwatch.StartNew(); var year = clock.UtcNow.Year - 1; var hire = new DateOnly(year - 3, 1, 1); var now = clock.UtcNow;
        await seed.Db(async db =>
        {
            var protector = f.Services.GetRequiredService<ISecretProtector>();
            string Seal(Guid id, string field, string plain) { var s = protector.Protect(Encoding.UTF8.GetBytes(plain), Encoding.UTF8.GetBytes($"comp:{id}:{field}")); return $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Ciphertext)}"; }
            var unit = new OrgUnit { Code = "SCALE", Name = "Scale", CreatedAt = now }; db.OrgUnits.Add(unit);
            for (var i = 1; i <= People; i++)
            {
                var e = new Employee { EmployeeNo = $"S{i:D5}", FullName = $"Person {i:D5} Tester", HireDate = hire, CreatedAt = now, UpdatedAt = now };
                e.Employments.Add(new EmployeeEmployment { EmployeeId = e.Id, EffectiveFrom = hire, JobTitle = "Analyst", OrgUnitId = unit.Id, ChangeReason = "Hired", CreatedAt = now });
                db.Employees.Add(e);
                var c = new EmployeeCompensation { EmployeeId = e.Id, EffectiveFrom = hire, PtkpStatus = i % 3 == 0 ? "K1" : "TK0", CreatedAt = now };
                c.BasicEnc = Seal(c.Id, "basic", (8_000_000 + i % 50 * 100_000).ToString()); c.AllowanceEnc = Seal(c.Id, "allowance", "500000");
                db.Compensations.Add(c);
                if (i % 500 == 0) await db.SaveChangesAsync();
            }
            await db.SaveChangesAsync(); return 0;
        });
        output.WriteLine($"seeded {People} employees in {sw.Elapsed.TotalSeconds:0.0}s");

        // ---- list and search latency
        var times = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            sw.Restart();
            var r = await hr.Get(i % 3 == 0 ? "/api/hr/employees?page=3&pageSize=25&sort=name" : i % 3 == 1 ? "/api/hr/employees?q=person%2000042" : "/api/hr/employees?status=active&type=permanent&page=50&pageSize=25&sort=hireDate&dir=desc");
            sw.Stop(); Assert.Equal(HttpStatusCode.OK, r.StatusCode); times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort(); var p95 = times[(int)(times.Count * 0.95) - 1];
        output.WriteLine($"employee list/search: median {times[times.Count / 2]:0} ms, p95 {p95:0} ms, max {times[^1]:0} ms");
        Assert.True(p95 < 1500, $"p95 {p95:0} ms");

        // ---- one full pay run
        var run = (await (await officer.Post("/api/hr/payroll/runs", new { year, month = 1 })).Body()).GetProperty("id").GetGuid();
        sw.Restart(); var calc = await officer.Post($"/api/hr/payroll/runs/{run}/calculate"); sw.Stop();
        Assert.Equal(HttpStatusCode.OK, calc.StatusCode);
        var detail = await calc.Body(); Assert.Equal(People, detail.GetProperty("run").GetProperty("employeeCount").GetInt32());
        output.WriteLine($"payroll calculate for {People}: {sw.Elapsed.TotalSeconds:0.0}s (limit 300s)");
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(5), $"calculation took {sw.Elapsed}");

        sw.Restart(); Assert.Equal(HttpStatusCode.OK, (await approver.Post($"/api/hr/payroll/runs/{run}/approve")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await approver.Post($"/api/hr/payroll/runs/{run}/lock")).StatusCode);
        sw.Restart(); var reg = await officer.Get($"/api/hr/payroll/runs/{run}/register"); Assert.Equal(HttpStatusCode.OK, reg.StatusCode); var rows = (await reg.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length;
        output.WriteLine($"register export: {sw.Elapsed.TotalSeconds:0.0}s, {rows - 1} rows"); Assert.Equal(People + 1, rows);

        // the DB trigger must also hold at this size, and the totals must equal the sum of the lines
        var (sumNet, totalNet) = await seed.Db(async db => (await db.PayRunItems.SumAsync(i => i.Net), (await db.PayRuns.SingleAsync()).TotalNet));
        Assert.Equal(sumNet, totalNet);
    }
}
