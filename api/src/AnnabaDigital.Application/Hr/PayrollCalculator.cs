using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>What the calculator needs for one employee and one month. All money in IDR.</summary>
public sealed record PayInput(
    string Ptkp, bool HasNpwp, bool KesEnrolled, bool TkEnrolled, decimal JkkRate,
    decimal Basic, decimal FixedAllowance, decimal ProrateFactor, decimal UnpaidDeduction,
    decimal TaxableExtras, decimal NonTaxableExtras, decimal OtherDeductions,
    bool FinalMonth, int MonthsInYear,
    decimal YtdGross, decimal YtdPension, decimal YtdTax,
    bool NonPermanent = false, bool DailyTer = false, decimal DaysPaid = 0,
    TaxTreatment Treatment = TaxTreatment.Gross, bool NonResident = false, decimal? TreatyRate = null);

public sealed record PayResult(
    decimal Gross, decimal TaxableGross,
    decimal KesEmployee, decimal JhtEmployee, decimal JpEmployee, decimal KesEmployer, decimal JhtEmployer, decimal JpEmployer, decimal JkkEmployer, decimal JkmEmployer,
    decimal Tax, string TaxMethod, decimal Net, decimal EmployerCost, decimal TaxAllowance = 0, decimal TaxBorne = 0);

/// <summary>
/// Pure function of its inputs (no clock, no database), so a run can be reproduced from its stored snapshot.
/// Tax follows PMK 168/2023: monthly TER on gross income including the employer-paid health, JKK and JKM premiums, and in the final month of the year (or of employment)
/// a true-up to the progressive rates on the actual income with the full year's PTKP. A non-resident pays PPh 26: a flat rate on gross pay.
/// Who bears the tax (<see cref="TaxTreatment"/>): Gross withholds it from pay; GrossUp adds a taxable allowance equal to the tax (found by iteration, since the allowance
/// itself raises the tax); Nett leaves pay untouched and the company pays the tax on top.
/// </summary>
public static class PayrollCalculator
{
    /// <summary>
    /// Overtime under PP 35/2021 for a 5-day week: hourly wage = monthly wage / 173. Working day: first hour 1.5×, further hours 2×. Rest day or holiday: hours 1–8 at 2×, the 9th at 3×, from the 10th at 4×.
    /// </summary>
    public static decimal OvertimePay(decimal monthlyWage, IEnumerable<(decimal Hours, bool RestDay)> days)
    {
        var hourly = monthlyWage / 173m; decimal pay = 0;
        foreach (var (h, rest) in days)
            pay += rest ? hourly * (2m * Math.Min(h, 8m) + 3m * Math.Clamp(h - 8m, 0m, 1m) + 4m * Math.Max(h - 9m, 0m)) : hourly * (1.5m * Math.Min(h, 1m) + 2m * Math.Max(h - 1m, 0m));
        return PayrollRules.Rupiah(pay);
    }

    public static PayResult Calculate(PayInput i, PayrollRules r)
    {
        var R = PayrollRules.Rupiah;
        var basic = R(i.Basic * i.ProrateFactor); var allow = R(i.FixedAllowance * i.ProrateFactor);
        var fixedPay = basic + allow;                                     // basis for BPJS
        var gross = fixedPay - R(i.UnpaidDeduction) + i.TaxableExtras + i.NonTaxableExtras;

        // BPJS is computed on the fixed wage (not reduced by unpaid leave: the wage base stays the contract wage), capped per scheme.
        var wage = R(i.Basic + i.FixedAllowance);
        decimal kesBase = i.KesEnrolled ? Math.Min(wage, r.KesWageCap) : 0, jpBase = i.TkEnrolled ? Math.Min(wage, r.JpWageCap) : 0, tkBase = i.TkEnrolled ? wage : 0;
        var kesEe = R(kesBase * r.KesEmployeeRate); var kesEr = R(kesBase * r.KesEmployerRate);
        var jhtEe = R(tkBase * r.JhtEmployeeRate); var jhtEr = R(tkBase * r.JhtEmployerRate);
        var jpEe = R(jpBase * r.JpEmployeeRate); var jpEr = R(jpBase * r.JpEmployerRate);
        var jkk = R(tkBase * i.JkkRate); var jkm = R(tkBase * r.JkmEmployerRate);

        // Taxable gross: pay that is taxable + the premiums the employer pays on the employee's behalf (health, JKK, JKM).
        var taxableGross = gross - i.NonTaxableExtras + kesEr + jkk + jkm;

        (decimal Tax, string Method) TaxOn(decimal taxable)
        {
            if (i.NonResident) return (R(Math.Max(taxable, 0) * (i.TreatyRate ?? r.Pph26Rate)), "PPH26");   // final: no PTKP, no position cost, no year-end recalculation, no NPWP surcharge (that is a PPh 21 rule)
            decimal tax; string method;
            if (i.DailyTer)
            {
                // Non-permanent worker paid per day or week: the rate depends on the (average) pay per day; there is no tax-free allowance and no year-end recalculation.
                var basis = Math.Max(taxable, 0); var perDay = i.DaysPaid > 0 ? basis / i.DaysPaid : 0;
                tax = perDay <= r.DailyTerFreeLimit ? 0 : perDay <= r.DailyTerUpperLimit ? R(basis * r.DailyTerRate) : R(PayrollRules.ProgressiveTax(basis * r.DailyArticle17Share)); method = "TER-H";
                if (!i.HasNpwp) tax = R(tax * (1 + r.NoNpwpSurcharge));
            }
            else if (!i.FinalMonth || i.NonPermanent)   // the year-end recalculation is for permanent employees only; a non-permanent worker paid monthly stays on the monthly table
            {
                tax = R(Math.Max(taxable, 0) * r.TerRateFor(PayrollRules.TerCategory(i.Ptkp), Math.Max(taxable, 0))); method = "TER";
                if (!i.HasNpwp) tax = R(tax * (1 + r.NoNpwpSurcharge));
            }
            else
            {
                var months = Math.Clamp(i.MonthsInYear, 1, 12);
                var grossYear = i.YtdGross + taxable;
                var positionCost = Math.Min(grossYear * r.PositionCostRate, r.PositionCostMonthlyCap * months);
                var neto = grossYear - positionCost - (i.YtdPension + jhtEe + jpEe);
                var ptkp = r.PtkpTable[i.Ptkp];   // the whole year's allowance, also for someone who joined or left during the year: the income is not annualised, the allowance is not cut
                var pkp = Math.Floor(Math.Max(neto - ptkp, 0) / 1000m) * 1000m;
                var annual = R(PayrollRules.ProgressiveTax(pkp));
                if (!i.HasNpwp) annual = R(annual * (1 + r.NoNpwpSurcharge));
                tax = annual - i.YtdTax; method = "TRUE-UP"; // may be negative: tax withheld earlier in the year is refunded in the final month
            }
            return (tax, method);
        }

        var (tax, method) = TaxOn(taxableGross);
        decimal allowance = 0, borne = 0;
        if (i.Treatment == TaxTreatment.GrossUp)
        {
            // The allowance A must equal the tax on (income + A). Tax rises with income and by less than the increase, so repeating A = tax(income + A) from A = 0 moves
            // in one direction in whole rupiah and stops at the smallest such A. In the year-end month A may be negative: a refund of tax the company paid goes back to the company.
            for (var n = 0; ; n++)
            {
                if (n > 1000) throw new InvalidOperationException("The PPh allowance did not settle.");
                if (tax == allowance) break;
                allowance = tax; (tax, method) = TaxOn(taxableGross + allowance);
            }
            gross += allowance; taxableGross += allowance;
        }
        else if (i.Treatment == TaxTreatment.Nett) borne = tax;

        var deductions = kesEe + jhtEe + jpEe + R(i.OtherDeductions) + tax - borne;   // tax the company bears is not taken from the employee
        var net = gross - deductions;
        return new PayResult(gross, taxableGross, kesEe, jhtEe, jpEe, kesEr, jhtEr, jpEr, jkk, jkm, tax, method, net, gross + kesEr + jhtEr + jpEr + jkk + jkm + borne, allowance, borne);
    }
}
