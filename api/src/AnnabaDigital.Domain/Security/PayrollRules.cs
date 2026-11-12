namespace AnnabaDigital.Domain.Security;

/// <summary>
/// Indonesian payroll rules as data: rates, caps, PTKP and the PPh 21 TER tables (PMK 168/2023). Pure and deterministic — every pay run stores the
/// <see cref="Version"/> it used, so a past run can always be explained. Values are the baseline as of the version date and MUST be validated by a tax
/// consultant before go-live (see docs/23-prd-hr-system.md §11). Money is <c>decimal</c>, rounded half-away-from-zero to whole rupiah where the rules say so.
/// </summary>
public sealed record TerBracket(decimal Upto, decimal Pct);

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
    /// <summary>PPh 26 on employment income of a foreign tax subject: a flat share of gross pay (UU PPh Article 26(1)), unless a tax treaty sets a lower rate for that person.</summary>
    public decimal Pph26Rate { get; init; } = 0.20m;
    // PPh 21 of non-permanent workers paid per day or week (PP 58/2023 annex D, PMK 168/2023): nothing up to the free limit per day, a flat rate up to the upper limit,
    // and above that the Article 17 rates on half of the gross amount.
    public decimal DailyTerFreeLimit { get; init; } = 450_000m;
    public decimal DailyTerUpperLimit { get; init; } = 2_500_000m;
    public decimal DailyTerRate { get; init; } = 0.005m;
    public decimal DailyArticle17Share { get; init; } = 0.5m;
    /// <summary>A fresh copy of the built-in rules (a property rather than a field so it never runs before the default tables below are initialised).</summary>
    public static PayrollRules Default => new();

    /// <summary>
    /// The built-in rules in force for a pay month. Only the pension (JP) wage ceiling differs between periods: BPJS Ketenagakerjaan raises it every March
    /// (2023: 9,559,600 · 2024: 10,042,300 · 2025: 10,547,400 · 2026: 11,086,300, letter B/1226/022026). Later years need a rules version entered by the organisation.
    /// </summary>
    public static PayrollRules BuiltInFor(int year, int month)
    {
        var d = new DateOnly(year, month, 1);
        if (d >= new DateOnly(2026, 3, 1)) return new();
        if (d >= new DateOnly(2025, 3, 1)) return new() { Version = "ID-2025.1", JpWageCap = 10_547_400m };
        if (d >= new DateOnly(2024, 3, 1)) return new() { Version = "ID-2024.1", JpWageCap = 10_042_300m };
        return new() { Version = "ID-2023.1", JpWageCap = 9_559_600m };
    }

    /// <summary>Tax-free allowance per status (yearly) and the TER tables, kept here so an organisation can correct them with a new effective-dated version instead of a code change.</summary>
    public Dictionary<string, decimal> PtkpTable { get; init; } = new(DefaultPtkp);
    public List<TerBracket> TerA { get; init; } = [.. DefaultTerA];
    public List<TerBracket> TerB { get; init; } = [.. DefaultTerB];
    public List<TerBracket> TerC { get; init; } = [.. DefaultTerC];

    /// <summary>Monthly TER rate (as a fraction, e.g. 0.025) for a gross taxable monthly income.</summary>
    public decimal TerRateFor(char category, decimal monthlyGross)
    {
        var table = category switch { 'A' => TerA, 'B' => TerB, 'C' => TerC, _ => throw new ArgumentException("Unknown TER category.") };
        foreach (var b in table) if (monthlyGross <= b.Upto) return b.Pct / 100m;
        return table.Count == 0 ? 0m : table[^1].Pct / 100m;
    }

    public static readonly IReadOnlyDictionary<string, decimal> DefaultPtkp = new Dictionary<string, decimal>
    {
        ["TK0"] = 54_000_000m, ["TK1"] = 58_500_000m, ["TK2"] = 63_000_000m, ["TK3"] = 67_500_000m,
        ["K0"] = 58_500_000m, ["K1"] = 63_000_000m, ["K2"] = 67_500_000m, ["K3"] = 72_000_000m,
    };

    /// <summary>TER category: A = TK0, TK1, K0 · B = TK2, TK3, K1, K2 · C = K3.</summary>
    public static char TerCategory(string ptkp) => ptkp switch { "TK0" or "TK1" or "K0" => 'A', "TK2" or "TK3" or "K1" or "K2" => 'B', "K3" => 'C', _ => throw new ArgumentException($"Unknown PTKP status '{ptkp}'.") };

    // (upper bound inclusive of the bracket, rate in percent). The last entry has no upper bound.
    public static readonly IReadOnlyList<TerBracket> DefaultTerA =
    [
        new(5_400_000, 0), new(5_650_000, 0.25m), new(5_950_000, 0.5m), new(6_300_000, 0.75m), new(6_750_000, 1), new(7_500_000, 1.25m), new(8_550_000, 1.5m), new(9_650_000, 1.75m), new(10_050_000, 2), new(10_350_000, 2.25m),
        new(10_700_000, 2.5m), new(11_050_000, 3), new(11_600_000, 3.5m), new(12_500_000, 4), new(13_750_000, 5), new(15_100_000, 6), new(16_950_000, 7), new(19_750_000, 8), new(24_150_000, 9), new(26_450_000, 10),
        new(28_000_000, 11), new(30_050_000, 12), new(32_400_000, 13), new(35_400_000, 14), new(39_100_000, 15), new(43_850_000, 16), new(47_800_000, 17), new(51_400_000, 18), new(56_300_000, 19), new(62_200_000, 20),
        new(68_600_000, 21), new(77_500_000, 22), new(89_000_000, 23), new(103_000_000, 24), new(125_000_000, 25), new(157_000_000, 26), new(206_000_000, 27), new(337_000_000, 28), new(454_000_000, 29), new(550_000_000, 30),
        new(695_000_000, 31), new(910_000_000, 32), new(1_400_000_000, 33), new(decimal.MaxValue, 34),
    ];
    public static readonly IReadOnlyList<TerBracket> DefaultTerB =
    [
        new(6_200_000, 0), new(6_500_000, 0.25m), new(6_850_000, 0.5m), new(7_300_000, 0.75m), new(9_200_000, 1), new(10_750_000, 1.5m), new(11_250_000, 2), new(11_600_000, 2.5m), new(12_600_000, 3), new(13_600_000, 4),
        new(14_950_000, 5), new(16_400_000, 6), new(18_450_000, 7), new(21_850_000, 8), new(26_000_000, 9), new(27_700_000, 10), new(29_350_000, 11), new(31_450_000, 12), new(33_950_000, 13), new(37_100_000, 14),
        new(41_100_000, 15), new(45_800_000, 16), new(49_500_000, 17), new(53_800_000, 18), new(58_500_000, 19), new(64_000_000, 20), new(71_000_000, 21), new(80_000_000, 22), new(93_000_000, 23), new(109_000_000, 24),
        new(129_000_000, 25), new(163_000_000, 26), new(211_000_000, 27), new(374_000_000, 28), new(459_000_000, 29), new(555_000_000, 30), new(704_000_000, 31), new(957_000_000, 32), new(1_405_000_000, 33), new(decimal.MaxValue, 34),
    ];
    public static readonly IReadOnlyList<TerBracket> DefaultTerC =
    [
        new(6_600_000, 0), new(6_950_000, 0.25m), new(7_350_000, 0.5m), new(7_800_000, 0.75m), new(8_850_000, 1), new(9_800_000, 1.25m), new(10_950_000, 1.5m), new(11_200_000, 1.75m), new(12_050_000, 2), new(12_950_000, 3),
        new(14_150_000, 4), new(15_550_000, 5), new(17_050_000, 6), new(19_500_000, 7), new(22_700_000, 8), new(26_600_000, 9), new(28_100_000, 10), new(30_100_000, 11), new(32_600_000, 12), new(35_400_000, 13),
        new(38_900_000, 14), new(43_000_000, 15), new(47_400_000, 16), new(51_200_000, 17), new(55_800_000, 18), new(60_400_000, 19), new(66_700_000, 20), new(74_500_000, 21), new(83_200_000, 22), new(95_600_000, 23),
        new(110_000_000, 24), new(134_000_000, 25), new(169_000_000, 26), new(221_000_000, 27), new(390_000_000, 28), new(463_000_000, 29), new(561_000_000, 30), new(709_000_000, 31), new(965_000_000, 32), new(1_419_000_000, 33), new(decimal.MaxValue, 34),
    ];

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
