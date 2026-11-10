using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>What the calculator needs for one employee and one month. All money in IDR.</summary>
public sealed record PayInput(
    string Ptkp, bool HasNpwp, bool KesEnrolled, bool TkEnrolled, decimal JkkRate,
    decimal Basic, decimal FixedAllowance, decimal ProrateFactor, decimal UnpaidDeduction,
    decimal TaxableExtras, decimal NonTaxableExtras, decimal OtherDeductions,
    bool FinalMonth, int MonthsInYear,
    decimal YtdGross, decimal YtdPension, decimal YtdTax);

public sealed record PayResult(
    decimal Gross, decimal TaxableGross,
    decimal KesEmployee, decimal JhtEmployee, decimal JpEmployee, decimal KesEmployer, decimal JhtEmployer, decimal JpEmployer, decimal JkkEmployer, decimal JkmEmployer,
    decimal Tax, string TaxMethod, decimal Net, decimal EmployerCost);

/// <summary>
/// Pure function of its inputs (no clock, no database), so a run can be reproduced from its stored snapshot.
/// Tax follows PMK 168/2023: monthly TER on gross income including the employer-paid health, JKK and JKM premiums, and in the final month of the year (or of employment)
/// a true-up to the progressive rates on the actual income with a pro-rated PTKP.
/// </summary>
public static class PayrollCalculator
{
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

        decimal tax; string method;
        if (!i.FinalMonth)
        {
            tax = R(Math.Max(taxableGross, 0) * PayrollRules.TerRate(PayrollRules.TerCategory(i.Ptkp), Math.Max(taxableGross, 0))); method = "TER";
        }
        else
        {
            var months = Math.Clamp(i.MonthsInYear, 1, 12);
            var grossYear = i.YtdGross + taxableGross;
            var positionCost = Math.Min(grossYear * r.PositionCostRate, r.PositionCostMonthlyCap * months);
            var neto = grossYear - positionCost - (i.YtdPension + jhtEe + jpEe);
            var ptkp = PayrollRules.Ptkp[i.Ptkp] * months / 12m;
            var pkp = Math.Floor(Math.Max(neto - ptkp, 0) / 1000m) * 1000m;
            var annual = R(PayrollRules.ProgressiveTax(pkp));
            if (!i.HasNpwp) annual = R(annual * (1 + r.NoNpwpSurcharge));
            tax = annual - i.YtdTax; method = "TRUE-UP"; // may be negative: tax withheld earlier in the year is refunded in the final month
        }
        if (!i.HasNpwp && method == "TER") tax = R(tax * (1 + r.NoNpwpSurcharge));

        var deductions = kesEe + jhtEe + jpEe + R(i.OtherDeductions) + tax;
        var net = gross - deductions;
        return new PayResult(gross, taxableGross, kesEe, jhtEe, jpEe, kesEr, jhtEr, jpEr, jkk, jkm, tax, method, net, gross + kesEr + jhtEr + jpEr + jkk + jkm);
    }
}
