using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Entities;
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
    public void Gross_up_pays_an_allowance_equal_to_the_tax_on_the_raised_income()
    {
        var r = PayrollCalculator.Calculate(Monthly() with { Treatment = TaxTreatment.GrossUp }, Rules);
        // Before the allowance the taxable gross is 10,454,000 (2.5% → 261,350). The allowance A must satisfy A = TER(10,454,000 + A) × (10,454,000 + A).
        // With A ≈ 320,000 the base lands in 10,700,001–11,050,000 = 3%: A = 0.03 × (10,454,000 + A) → A = 313,620 / 0.97 = 323,319.59 → 323,320 (a whole rupiah that solves it after rounding).
        Assert.Equal(323_320m, r.TaxAllowance); Assert.Equal(323_320m, r.Tax); Assert.Equal(0m, r.TaxBorne);
        Assert.Equal(10_777_320m, r.TaxableGross); Assert.Equal(10_323_320m, r.Gross);
        // take-home is as if untaxed: 10,323,320 − 400,000 BPJS − 323,320 tax = 9,600,000; the company pays the allowance on top of the usual cost
        Assert.Equal(9_600_000m, r.Net); Assert.Equal(11_024_000m + 323_320m, r.EmployerCost);
    }

    [Fact]
    public void Nett_leaves_pay_untouched_and_the_company_pays_the_tax_itself()
    {
        var r = PayrollCalculator.Calculate(Monthly() with { Treatment = TaxTreatment.Nett }, Rules);
        // the tax is the gross-method tax (261,350): it is not added to the employee's income
        Assert.Equal(261_350m, r.Tax); Assert.Equal(261_350m, r.TaxBorne); Assert.Equal(0m, r.TaxAllowance); Assert.Equal(10_454_000m, r.TaxableGross);
        Assert.Equal(9_600_000m, r.Net); Assert.Equal(11_024_000m + 261_350m, r.EmployerCost);
    }

    [Fact]
    public void Gross_up_settles_for_every_salary_and_status_including_the_year_end_month()
    {
        foreach (var ptkp in new[] { "TK0", "K1", "K3" })
            for (var basic = 4_000_000m; basic <= 80_000_000m; basic += 173_000m)
            {
                var monthly = PayrollCalculator.Calculate(Monthly(ptkp, basic) with { Treatment = TaxTreatment.GrossUp }, Rules);
                Assert.Equal(monthly.Tax, monthly.TaxAllowance);
                // the allowance is exactly the tax on the income that includes it
                var cat = PayrollRules.TerCategory(ptkp);
                Assert.Equal(PayrollRules.Rupiah(monthly.TaxableGross * Rules.TerRateFor(cat, monthly.TaxableGross)), monthly.Tax);
                var nett = PayrollCalculator.Calculate(Monthly(ptkp, basic) with { Treatment = TaxTreatment.Nett }, Rules);
                Assert.Equal(nett.Net, monthly.Net);                       // both leave the employee the untaxed amount
                Assert.True(monthly.Tax >= nett.Tax);                      // the allowance is income too, so grossing up costs more tax

                // December, after eleven months of a lower salary withheld under gross-up: the true-up may go either way and still settles
                var dec = PayrollCalculator.Calculate(Monthly(ptkp, basic) with { Treatment = TaxTreatment.GrossUp, FinalMonth = true, YtdGross = 11 * 9_000_000m, YtdPension = 11 * 270_000m, YtdTax = 11 * 150_000m }, Rules);
                Assert.Equal(dec.Tax, dec.TaxAllowance);
                Assert.Equal("TRUE-UP", dec.TaxMethod);
            }
    }

    [Fact]
    public void Non_residents_pay_PPh_26_at_a_flat_rate_with_no_allowance_and_no_year_end_recalculation()
    {
        var r = PayrollCalculator.Calculate(Monthly() with { NonResident = true }, Rules);
        // 20% of the taxable gross 10,454,000 = 2,090,800; no PTKP, no TER, no NPWP surcharge
        Assert.Equal(2_090_800m, r.Tax); Assert.Equal("PPH26", r.TaxMethod);
        Assert.Equal(10_000_000m - 400_000m - 2_090_800m, r.Net);
        Assert.Equal(2_090_800m, PayrollCalculator.Calculate(Monthly(npwp: false) with { NonResident = true, FinalMonth = true, YtdGross = 50_000_000m, YtdTax = 1_000_000m }, Rules).Tax);
        // a tax treaty can lower the rate: 10% → 1,045,400
        Assert.Equal(1_045_400m, PayrollCalculator.Calculate(Monthly() with { NonResident = true, TreatyRate = 0.10m }, Rules).Tax);
        // grossed up: A = 0.2 × (10,454,000 + A) → A = 0.25 × 10,454,000 = 2,613,500 exactly
        var up = PayrollCalculator.Calculate(Monthly() with { NonResident = true, Treatment = TaxTreatment.GrossUp }, Rules);
        Assert.Equal(2_613_500m, up.TaxAllowance); Assert.Equal(2_613_500m, up.Tax); Assert.Equal(9_600_000m, up.Net);
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
    public void A_partial_year_keeps_the_whole_years_PTKP_and_withholding_is_refunded()
    {
        // Left after 3 months (final month = month 3): income 3 × 5,000,000 taxable (no BPJS). The income is not annualised and the tax-free allowance is not cut:
        // neto 14,250,000 is far below the full PTKP of 54,000,000, so no tax is due for the year and everything withheld earlier comes back.
        var input = new PayInput("TK0", true, false, false, 0m, 5_000_000m, 0m, 1m, 0m, 0m, 0m, 0m, true, 3, YtdGross: 10_000_000m, YtdPension: 0m, YtdTax: 400_000m);
        Assert.Equal(-400_000m, PayrollCalculator.Calculate(input, Rules).Tax);
        // Joined on 1 September at 16,000,000 a month (TK/0), TER 7 % withheld for September–November = 3 × 1,120,000:
        // gross 64,000,000 · position cost min(5 %, 4 × 500,000) = 2,000,000 · neto 62,000,000 · PKP 8,000,000 · tax 400,000 · December = 400,000 − 3,360,000
        var joiner = new PayInput("TK0", true, false, false, 0m, 16_000_000m, 0m, 1m, 0m, 0m, 0m, 0m, true, 4, YtdGross: 48_000_000m, YtdPension: 0m, YtdTax: 3_360_000m);
        Assert.Equal(-2_960_000m, PayrollCalculator.Calculate(joiner, Rules).Tax);
    }

    [Fact]
    public void Non_permanent_workers_paid_per_day_or_week_follow_the_daily_table_and_never_get_a_year_end_recalculation()
    {
        PayInput Daily(decimal rate, decimal days, bool dailyTer, bool final = false, bool npwp = true) => new("TK0", npwp, false, false, 0m, rate * days, 0m, 1m, 0m, 0m, 0m, 0m, final, 12, YtdGross: 0m, YtdPension: 0m, YtdTax: 0m, NonPermanent: true, DailyTer: dailyTer, DaysPaid: days);
        var low = PayrollCalculator.Calculate(Daily(320_000m, 25, true), Rules); Assert.Equal(0m, low.Tax); Assert.Equal("TER-H", low.TaxMethod);          // up to 450,000 a day: nothing
        Assert.Equal(0m, PayrollCalculator.Calculate(Daily(450_000m, 20, true), Rules).Tax);                                                                   // the limit itself is still free
        Assert.Equal(55_000m, PayrollCalculator.Calculate(Daily(550_000m, 20, true), Rules).Tax);                                                              // 0.5 % × 11,000,000
        Assert.Equal(66_000m, PayrollCalculator.Calculate(Daily(550_000m, 20, true, npwp: false), Rules).Tax);                                                 // +20 % without a tax number
        Assert.Equal(1_500_000m, PayrollCalculator.Calculate(Daily(3_000_000m, 20, true), Rules).Tax);                                                         // above 2,500,000 a day: Article 17 on half of 60,000,000 = 5 % × 30,000,000
        Assert.Equal(0m, PayrollCalculator.Calculate(Daily(320_000m, 25, true, final: true), Rules).Tax);                                                      // December changes nothing
        // the same worker paid once a month is on the monthly table (8,000,000 → 1.5 %), in December too
        var monthly = PayrollCalculator.Calculate(Daily(320_000m, 25, false, final: true), Rules); Assert.Equal(120_000m, monthly.Tax); Assert.Equal("TER", monthly.TaxMethod);
    }

    [Fact]
    public void The_built_in_rules_follow_the_pension_wage_ceiling_of_each_period()
    {
        Assert.Equal((9_559_600m, "ID-2023.1"), (PayrollRules.BuiltInFor(2024, 2).JpWageCap, PayrollRules.BuiltInFor(2024, 2).Version));
        Assert.Equal((10_042_300m, "ID-2024.1"), (PayrollRules.BuiltInFor(2024, 3).JpWageCap, PayrollRules.BuiltInFor(2025, 2).Version));
        Assert.Equal((10_547_400m, "ID-2025.1"), (PayrollRules.BuiltInFor(2025, 3).JpWageCap, PayrollRules.BuiltInFor(2026, 2).Version));
        Assert.Equal((11_086_300m, "ID-2026.1"), (PayrollRules.BuiltInFor(2026, 3).JpWageCap, PayrollRules.BuiltInFor(2027, 1).Version));
        // every table of PP 58/2023 has the number of rows of the official annex, ends open at 34 %, and rises strictly
        foreach (var (table, rows) in new[] { (PayrollRules.DefaultTerA, 44), (PayrollRules.DefaultTerB, 40), (PayrollRules.DefaultTerC, 41) })
        {
            Assert.Equal(rows, table.Count); Assert.Equal((decimal.MaxValue, 34m), (table[^1].Upto, table[^1].Pct)); Assert.Equal(0m, table[0].Pct);
            Assert.True(table.Zip(table.Skip(1)).All(p => p.First.Upto < p.Second.Upto && p.First.Pct < p.Second.Pct));
        }
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
