namespace AnnabaDigital.Application.Hr;

public sealed record CompensationRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, string? JobTitle, bool HasCompensation, DateOnly? EffectiveFrom, string? PtkpStatus);
public sealed record CompensationView(Guid EmployeeId, string EmployeeNo, string EmployeeName, bool HasCompensation, DateOnly? EffectiveFrom, decimal? Basic, decimal? Allowance, string? PtkpStatus,
    bool HasNpwp, bool KesEnrolled, bool TkEnrolled, decimal JkkRate, int? OpeningYear, int OpeningMonths, decimal OpeningGross, decimal OpeningPension, decimal OpeningTax, decimal RetroPending = 0);
public sealed record SetCompensationRequest(DateOnly? EffectiveFrom, decimal? Basic, decimal? Allowance, string? PtkpStatus, bool? HasNpwp, bool? KesEnrolled, bool? TkEnrolled, decimal? JkkRate,
    int? OpeningYear, int? OpeningMonths, decimal? OpeningGross, decimal? OpeningPension, decimal? OpeningTax, string? Reason, bool? AllowRetro = null);
public sealed record RetroRow(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Period, decimal Amount, string Note, DateTimeOffset CreatedAt);

public sealed record CreateRunRequest(int? Year, int? Month, string? Note, bool? DeductAbsence = null);
public sealed record AddLineRequest(Guid? EmployeeId, string? Kind, string? Label, decimal? Amount, bool? Taxable);
public sealed record RejectRunRequest(string? Note);

public sealed record PayRunSummary(Guid Id, int Year, int Month, string Status, int EmployeeCount, decimal TotalGross, decimal TotalTax, decimal TotalNet, decimal TotalEmployerCost,
    string RulesVersion, DateTimeOffset CreatedAt, DateTimeOffset? CalculatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? LockedAt, DateTimeOffset? PaidAt, string? Note, int Version,
    bool CanCalculate, bool CanApprove, bool CanLock, bool CanMarkPaid, bool DeductAbsence);

public sealed record PayItemView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Ptkp, decimal Basic, decimal Allowance, decimal ProrateFactor, decimal UnpaidDeduction,
    decimal TaxableExtras, decimal NonTaxableExtras, decimal OtherDeductions, decimal OvertimePay, decimal Gross, decimal KesEmployee, decimal JhtEmployee, decimal JpEmployee,
    decimal KesEmployer, decimal JhtEmployer, decimal JpEmployer, decimal JkkEmployer, decimal JkmEmployer, decimal Tax, string TaxMethod, decimal Net, decimal EmployerCost, string? Warnings);
public sealed record PayLineView(Guid Id, Guid EmployeeId, string EmployeeName, string Kind, string Label, decimal Amount, bool Taxable, bool Auto = false);
public sealed record PayRunDetail(PayRunSummary Run, IReadOnlyList<PayItemView> Items, IReadOnlyList<PayLineView> Lines, IReadOnlyList<string> Missing, bool AttendanceClosed);

public sealed record PayslipRow(Guid ItemId, Guid PayRunId, int Year, int Month, string Status, decimal Gross, decimal Net);
public sealed record Payslip(Guid PayRunId, int Year, int Month, string Status, DateTimeOffset? PaidAt, PayItemView Item, IReadOnlyList<PayLineView> Lines);
