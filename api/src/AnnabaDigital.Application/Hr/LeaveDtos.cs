namespace AnnabaDigital.Application.Hr;

public sealed record LeaveTypeView(Guid Id, string Code, string Name, decimal? AnnualQuota, decimal CarryOverMax, int? MaxDaysPerRequest, bool Paid, bool AllowHalfDay, bool Archived, decimal? HrApprovalFromDays = null, int? CarryOverExpiryMonth = null);
public sealed record LeaveTypeRequest(string? Code, string? Name, decimal? AnnualQuota, bool? Unlimited, decimal? CarryOverMax, int? MaxDaysPerRequest, bool? Paid, bool? AllowHalfDay, bool? Archived, decimal? HrApprovalFromDays = null, bool? NoHrApproval = null, int? CarryOverExpiryMonth = null, bool? NoCarryOverExpiry = null);

public sealed record HolidayView(Guid Id, DateOnly Date, string Name, bool Collective);
public sealed record HolidayRequest(DateOnly? Date, string? Name, bool? Collective);

public sealed record BalanceView(Guid TypeId, string Code, string Name, int Year, decimal Granted, decimal Used, decimal Adjusted, decimal Pending, decimal Available, decimal Expiring = 0, DateOnly? ExpiresOn = null, decimal Expired = 0);

public sealed record SubmitLeaveRequest(Guid? LeaveTypeId, DateOnly? StartDate, DateOnly? EndDate, bool? HalfDay, string? Reason);
public sealed record DecisionRequest(string? Note);
public sealed record AdjustBalanceRequest(Guid? EmployeeId, Guid? LeaveTypeId, int? Year, decimal? Days, string? Note);

public sealed record LeaveRequestView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, Guid LeaveTypeId, string LeaveType, DateOnly StartDate, DateOnly EndDate,
    bool HalfDay, decimal Days, string? Reason, string Status, DateTimeOffset? DecidedAt, string? DecisionNote, DateTimeOffset CreatedAt, bool CanDecide, bool CanCancel, bool NeedsHrApproval = false, bool AwaitingHr = false);

public sealed record LeaveQuery(string? Scope, string? Status, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 20);
