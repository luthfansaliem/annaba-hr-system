namespace AnnabaDigital.Domain.Security;

/// <summary>
/// Indonesian payroll rules as data: rates, caps, PTKP and the PPh 21 TER tables (PMK 168/2023). Pure and deterministic — every pay run stores the
/// <see cref="Version"/> it used, so a past run can always be explained. Values are the baseline as of the version date and MUST be validated by a tax
/// consultant before go-live (see docs/23-prd-hr-system.md §11). Money is <c>decimal</c>, rounded half-away-from-zero to whole rupiah where the rules say so.
/// </summary>
public sealed record PayrollRules
{
    public string Version { get; init; } = "ID-2026.1";
    // BPJS Kesehatan: on fixed pay up to the cap
    public decimal KesEmployeeRate { get; init; } = 0.01m;
    public decimal KesEmployerRate { get; init; } = 0.04m;
    public decimal KesWageCap { get; init; } = 12_000_000m;
    // BPJS Ketenagakerjaan
    public decimal JhtEmployeeRate { get; init; } = 0.02m;
    public decimal JhtEmployerRate { get; init; } = 0.037m;
    public decimal JpEmployeeRate { get; init; } = 0.01m;
    public decimal JpEmployerRate { get; init; } = 0.02m;
    public decimal JpWageCap { get; init; } = 11_086_300m;
    public decimal JkmEmployerRate { get; init; } = 0.003m;
    public decimal DefaultJkkRate { get; init; } = 0.0024m;
    // PPh 21
    public decimal PositionCostRate { get; init; } = 0.05m;
    public decimal PositionCostMonthlyCap { get; init; } = 500_000m;
    public decimal NoNpwpSurcharge { get; init; } = 0.20m;
    /// <summary>Hard ceiling for the month fraction a person worked, used when prorating (days in the month).</summary>
    public static PayrollRules Default { get; } = new();

    public static readonly IReadOnlyDictionary<string, decimal> Ptkp = new Dictionary<string, decimal>
    {
        ["TK0"] = 54_000_000m, ["TK1"] = 58_500_000m, ["TK2"] = 63_000_000m, ["TK3"] = 67_500_000m,
        ["K0"] = 58_500_000m, ["K1"] = 63_000_000m, ["K2"] = 67_500_000m, ["K3"] = 72_000_000m,
    };

    /// <summary>TER category: A = TK0, TK1, K0 · B = TK2, TK3, K1, K2 · C = K3.</summary>
    public static char TerCategory(string ptkp) => ptkp switch { "TK0" or "TK1" or "K0" => 'A', "TK2" or "TK3" or "K1" or "K2" => 'B', "K3" => 'C', _ => throw new ArgumentException($"Unknown PTKP status '{ptkp}'.") };

    // (upper bound inclusive of the bracket, rate in percent). The last entry has no upper bound.
    private static readonly (decimal Upto, decimal Pct)[] TerA =
    [
        (5_400_000, 0), (5_650_000, 0.25m), (5_950_000, 0.5m), (6_300_000, 0.75m), (6_750_000, 1), (7_500_000, 1.25m), (8_550_000, 1.5m), (9_650_000, 1.75m), (10_050_000, 2), (10_350_000, 2.25m),
        (10_700_000, 2.5m), (11_050_000, 3), (11_600_000, 3.5m), (12_500_000, 4), (13_750_000, 5), (15_100_000, 6), (16_950_000, 7), (19_750_000, 8), (24_150_000, 9), (26_450_000, 10),
        (28_000_000, 11), (30_050_000, 12), (32_400_000, 13), (35_400_000, 14), (39_100_000, 15), (43_850_000, 16), (47_800_000, 17), (51_400_000, 18), (56_300_000, 19), (62_200_000, 20),
        (68_600_000, 21), (77_500_000, 22), (89_000_000, 23), (103_000_000, 24), (125_000_000, 25), (157_000_000, 26), (206_000_000, 27), (337_000_000, 28), (454_000_000, 29), (550_000_000, 30),
        (695_000_000, 31), (910_000_000, 32), (1_400_000_000, 33), (decimal.MaxValue, 34),
    ];
    private static readonly (decimal Upto, decimal Pct)[] TerB =
    [
        (6_200_000, 0), (6_500_000, 0.25m), (6_850_000, 0.5m), (7_300_000, 0.75m), (9_200_000, 1), (10_750_000, 1.5m), (11_250_000, 2), (11_600_000, 2.5m), (12_600_000, 3), (13_600_000, 4),
        (14_950_000, 5), (16_400_000, 6), (18_450_000, 7), (21_850_000, 8), (26_000_000, 9), (27_700_000, 10), (29_350_000, 11), (31_450_000, 12), (33_950_000, 13), (37_100_000, 14),
        (41_100_000, 15), (45_800_000, 16), (49_500_000, 17), (53_800_000, 18), (58_500_000, 19), (64_000_000, 20), (71_000_000, 21), (80_000_000, 22), (93_000_000, 23), (109_000_000, 24),
        (129_000_000, 25), (163_000_000, 26), (211_000_000, 27), (374_000_000, 28), (459_000_000, 29), (555_000_000, 30), (704_000_000, 31), (957_000_000, 32), (1_405_000_000, 33), (decimal.MaxValue, 34),
    ];
    private static readonly (decimal Upto, decimal Pct)[] TerC =
    [
        (6_600_000, 0), (6_950_000, 0.25m), (7_350_000, 0.5m), (7_800_000, 0.75m), (8_850_000, 1), (9_800_000, 1.25m), (10_950_000, 1.5m), (11_200_000, 1.75m), (12_050_000, 2), (12_950_000, 3),
        (14_150_000, 4), (15_550_000, 5), (17_050_000, 6), (19_500_000, 7), (22_700_000, 8), (26_600_000, 9), (28_100_000, 10), (30_100_000, 11), (32_600_000, 12), (35_400_000, 13),
        (38_900_000, 14), (43_000_000, 15), (47_400_000, 16), (51_200_000, 17), (55_800_000, 18), (60_400_000, 19), (66_700_000, 20), (74_500_000, 21), (83_200_000, 22), (95_600_000, 23),
        (110_000_000, 24), (134_000_000, 25), (169_000_000, 26), (221_000_000, 27), (390_000_000, 28), (463_000_000, 29), (561_000_000, 30), (709_000_000, 31), (965_000_000, 32), (1_419_000_000, 33), (decimal.MaxValue, 34),
    ];

    /// <summary>Monthly TER rate (as a fraction, e.g. 0.025) for a gross taxable monthly income.</summary>
    public static decimal TerRate(char category, decimal monthlyGross)
    {
        var table = category switch { 'A' => TerA, 'B' => TerB, 'C' => TerC, _ => throw new ArgumentException("Unknown TER category.") };
        foreach (var (upto, pct) in table) if (monthlyGross <= upto) return pct / 100m;
        return 0.34m;
    }

    /// <summary>Article 17 progressive tax on a yearly taxable income (PKP, already rounded down to thousands): 5/15/25/30/35%.</summary>
    public static decimal ProgressiveTax(decimal pkp)
    {
        if (pkp <= 0) return 0;
        (decimal Cap, decimal Rate)[] layers = [(60_000_000m, 0.05m), (250_000_000m, 0.15m), (500_000_000m, 0.25m), (5_000_000_000m, 0.30m), (decimal.MaxValue, 0.35m)];
        decimal tax = 0, floor = 0;
        foreach (var (cap, rate) in layers)
        {
            if (pkp <= floor) break;
            tax += (Math.Min(pkp, cap) - floor) * rate; floor = cap;
        }
        return tax;
    }

    public static decimal Rupiah(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
