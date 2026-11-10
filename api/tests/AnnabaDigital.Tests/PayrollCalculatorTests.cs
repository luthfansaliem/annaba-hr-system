using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Hand-computed golden cases (docs/23 §9). Each expected number is derived in the comment so a reviewer can re-check it against the rules.</summary>
public sealed class PayrollCalculatorTests
{
    private static readonly PayrollRules Rules = PayrollRules.Default;
    private static PayInput Monthly(string ptkp = "TK0", decimal basic = 10_000_000m, decimal allowance = 0m, bool npwp = true, decimal prorate = 1m, decimal unpaid = 0m, decimal extras = 0m, decimal other = 0m)
        => new(ptkp, npwp, true, true, Rules.DefaultJkkRate, basic, allowance, prorate, unpaid, extras, 0m, other, false, 12, 0, 0, 0);

    [Fact]
    public void Monthly_TER_for_a_single_employee_on_ten_million()
    {
        var r = PayrollCalculator.Calculate(Monthly(), Rules);
        // BPJS Kes: 10,000,000 (< cap 12M) -> employee 1% = 100,000 · employer 4% = 400,000
        Assert.Equal(100_000m, r.KesEmployee); Assert.Equal(400_000m, r.KesEmployer);
        // JHT 2% / 3.7% = 200,000 / 370,000 · JP (cap 11,086,300 > 10M) 1% / 2% = 100,000 / 200,000 · JKK 0.24% = 24,000 · JKM 0.3% = 30,000
        Assert.Equal(200_000m, r.JhtEmployee); Assert.Equal(370_000m, r.JhtEmployer); Assert.Equal(100_000m, r.JpEmployee); Assert.Equal(200_000m, r.JpEmployer);
        Assert.Equal(24_000m, r.JkkEmployer); Assert.Equal(30_000m, r.JkmEmployer);
        // taxable gross = 10,000,000 + 400,000 + 24,000 + 30,000 = 10,454,000 -> category A, bracket 10,350,001–10,700,000 = 2.5% -> 261,350
        Assert.Equal(10_454_000m, r.TaxableGross); Assert.Equal(261_350m, r.Tax); Assert.Equal("TER", r.TaxMethod);
        // net = 10,000,000 − (100,000 + 200,000 + 100,000) − 261,350 = 9,338,650
        Assert.Equal(9_338_650m, r.Net);
        // employer cost = 10,000,000 + 400,000 + 370,000 + 200,000 + 24,000 + 30,000
        Assert.Equal(11_024_000m, r.EmployerCost);
    }

    [Fact]
    public void Wage_caps_apply_to_health_and_pension_but_not_to_old_age_savings()
    {
        var r = PayrollCalculator.Calculate(Monthly(basic: 20_000_000m), Rules);
        Assert.Equal(120_000m, r.KesEmployee);              // 1% of the 12,000,000 cap
        Assert.Equal(110_863m, r.JpEmployee);               // 1% of 11,086,300 = 110,863
        Assert.Equal(400_000m, r.JhtEmployee);              // JHT has no cap: 2% of 20,000,000
    }

    [Fact]
    public void Category_follows_the_PTKP_status_and_a_missing_NPWP_adds_twenty_percent()
    {
        Assert.Equal('A', PayrollRules.TerCategory("K0")); Assert.Equal('B', PayrollRules.TerCategory("K2")); Assert.Equal('C', PayrollRules.TerCategory("K3"));
        var withTax = PayrollCalculator.Calculate(Monthly("K1"), Rules);     // category B: 10,454,000 falls in 10,750,000 or below -> 1.5% (9,200,001–10,750,000) = 156,810
        Assert.Equal(156_810m, withTax.Tax);
        var noNpwp = PayrollCalculator.Calculate(Monthly("K1", npwp: false), Rules);
        Assert.Equal(188_172m, noNpwp.Tax);                                   // 156,810 × 1.2
    }

    [Fact]
    public void Joining_mid_month_prorates_pay_and_unpaid_leave_reduces_gross()
    {
        var half = PayrollCalculator.Calculate(Monthly(prorate: 0.5m), Rules);
        Assert.Equal(5_000_000m, half.Gross);
        var unpaid = PayrollCalculator.Calculate(Monthly(unpaid: 1_000_000m), Rules);
        Assert.Equal(9_000_000m, unpaid.Gross);
        Assert.Equal(100_000m, unpaid.KesEmployee); // BPJS stays on the contract wage, not on what was left after unpaid leave
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(60_000_000, 3_000_000)]                 // 5% of 60M
    [InlineData(100_000_000, 9_000_000)]                // 3,000,000 + 15% × 40M
    [InlineData(300_000_000, 44_000_000)]               // 3,000,000 + 28,500,000 (15% × 190M) + 12,500,000 (25% × 50M)
    [InlineData(600_000_000, 124_000_000)]              // 3,000,000 (5% × 60M) + 28,500,000 (15% × 190M) + 62,500,000 (25% × 250M) + 30,000,000 (30% × 100M)
    public void Progressive_tax_brackets(long pkp, long expected)
    {
        Assert.Equal((decimal)expected, PayrollRules.ProgressiveTax(pkp));
    }

    [Fact]
    public void Final_month_true_up_settles_the_year_against_the_tax_already_withheld()
    {
        // Single (TK0) earning 10,000,000 for all 12 months. Eleven TER months of 261,350 were withheld; employee JHT+JP paid 300,000 a month.
        var input = new PayInput("TK0", true, true, true, Rules.DefaultJkkRate, 10_000_000m, 0m, 1m, 0m, 0m, 0m, 0m, true, 12, YtdGross: 11 * 10_454_000m, YtdPension: 11 * 300_000m, YtdTax: 11 * 261_350m);
        var r = PayrollCalculator.Calculate(input, Rules);
        // gross year = 12 × 10,454,000 = 125,448,000 · position cost = min(5% = 6,272,400, 6,000,000) = 6,000,000 · pension = 12 × 300,000 = 3,600,000
        // neto = 125,448,000 − 6,000,000 − 3,600,000 = 115,848,000 · PTKP 54,000,000 · PKP = 61,848,000 (rounded down to thousands)
        // tax = 5% × 60,000,000 + 15% × 1,848,000 = 3,000,000 + 277,200 = 3,277,200 · already withheld 11 × 261,350 = 2,874,850 -> December = 402,350
        Assert.Equal("TRUE-UP", r.TaxMethod); Assert.Equal(402_350m, r.Tax);
    }

    [Fact]
    public void A_partial_year_uses_a_prorated_PTKP_and_withholding_can_be_refunded()
    {
        // Left after 3 months (final month = month 3): income 3 × 5,000,000 taxable (no BPJS), PTKP pro-rated 54M × 3/12 = 13.5M, tax withheld earlier was too high.
        var input = new PayInput("TK0", true, false, false, 0m, 5_000_000m, 0m, 1m, 0m, 0m, 0m, 0m, true, 3, YtdGross: 10_000_000m, YtdPension: 0m, YtdTax: 400_000m);
        var r = PayrollCalculator.Calculate(input, Rules);
        // gross 15,000,000 · position cost min(750,000, 1,500,000) = 750,000 · neto 14,250,000 · PKP floor(14,250,000 − 13,500,000) = 750,000 · tax 5% = 37,500 · 37,500 − 400,000 = −362,500
        Assert.Equal(-362_500m, r.Tax);
    }

    [Theory]
    [InlineData(2, false, 202_312)]     // working day: 1 h × 1.5 + 1 h × 2 = 3.5 × (10,000,000 / 173)
    [InlineData(1, false, 86_705)]      // 1.5 × 57,803.47
    [InlineData(4, true, 462_428)]      // rest day: 4 h × 2 = 8 × 57,803.47
    [InlineData(9, true, 1_098_266)]    // rest day: 8 h × 2 + the 9th hour × 3 = 19 × 57,803.47
    [InlineData(10, true, 1_329_480)]   // ... + the 10th hour × 4 = 23 × 57,803.47
    public void Overtime_follows_PP_35_2021_multipliers(int hours, bool restDay, long expected)
        => Assert.Equal((decimal)expected, PayrollCalculator.OvertimePay(10_000_000m, [(hours, restDay)]));

    [Fact]
    public void Overtime_adds_up_over_days_and_is_zero_without_any()
    {
        Assert.Equal(0m, PayrollCalculator.OvertimePay(10_000_000m, []));
        // 202,312.14 (2 h, working day) + 462,427.75 (4 h, rest day) = 664,739.89: rounded once, on the sum
        Assert.Equal(664_740m, PayrollCalculator.OvertimePay(10_000_000m, [(2m, false), (4m, true)]));
    }

    [Fact]
    public void The_excel_writer_keeps_identifiers_as_text_never_runs_formulas_and_makes_valid_xml_from_odd_input()
    {
        var bytes = AnnabaDigital.Application.Hr.SimpleXlsx.Build([["name", "nik", "amount", "note"], ["Budi <b>&</b>", "3174010101900001", "'-1500.5", "=HYPERLINK(\"http://evil\")"], ["A\u0001B", "007", "12", "1e5"]]);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var sheet = System.Xml.Linq.XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()); System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = sheet.Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!, c => ((string?)c.Attribute("t"), c.Value));
        Assert.Equal(("inlineStr", "Budi <b>&</b>"), cells["A2"]);                // escaped, not markup
        Assert.Equal(("inlineStr", "3174010101900001"), cells["B2"]);             // 16 digits: text, or Excel would round it
        Assert.Equal((null, "-1500.5"), cells["C2"]);                             // a negative amount (apostrophe-guarded in the CSV) is a number again
        Assert.Equal(("inlineStr", "=HYPERLINK(\"http://evil\")"), cells["D2"]);  // shown, never evaluated
        Assert.Equal(("inlineStr", "AB"), cells["A3"]);                           // control characters XML forbids are dropped
        Assert.Equal(("inlineStr", "007"), cells["B3"]); Assert.Equal((null, "12"), cells["C3"]); Assert.Equal(("inlineStr", "1e5"), cells["D3"]);
        Assert.Equal(("inlineStr", "amount"), cells["C1"]);                       // headers are always text
    }
}
