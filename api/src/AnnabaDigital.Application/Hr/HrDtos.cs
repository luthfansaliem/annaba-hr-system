namespace AnnabaDigital.Application.Hr;

public sealed record EmployeeQuery(string? Q, string? Status, Guid? OrgUnitId, string? Type, string? Sort, string? Dir, int Page = 1, int PageSize = 10);

public sealed record EmployeeListItem(Guid Id, string EmployeeNo, string FullName, string? WorkEmail, string Status, string? JobTitle, Guid? OrgUnitId, string? OrgUnit,
    string? Type, Guid? ManagerId, string? ManagerName, DateOnly HireDate, DateOnly? ContractEnd);

public sealed record EmploymentView(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string JobTitle, string? Grade, string? Location, string Type, DateOnly? ContractEnd,
    Guid? OrgUnitId, string? OrgUnit, Guid? ManagerId, string? ManagerName, string? ChangeReason);

/// <summary>Sensitive values are null unless the caller holds employees.read_sensitive and re-entered their password recently; the Has* flags always tell whether a value exists.</summary>
public sealed record SensitiveView(bool Visible, string? NationalId, string? TaxId, string? BankAccount, bool HasNationalId, bool HasTaxId, bool HasBankAccount);

public sealed record EmployeeDetail(Guid Id, string EmployeeNo, string FullName, string? WorkEmail, string? Phone, DateOnly? BirthDate, string? Gender, string? MaritalStatus,
    string? Address, string? EmergencyContactName, string? EmergencyContactPhone, string? BankName, string Status, DateOnly HireDate, DateOnly? TerminationDate,
    string? TerminationReason, Guid? UserId, int Version, EmploymentView? Current, IReadOnlyList<EmploymentView> History, SensitiveView Sensitive, IReadOnlyList<EmployeeRef> DirectReports, Guid LegalEntityId = default, string? LegalEntityName = null);

public sealed record EmployeeRef(Guid Id, string EmployeeNo, string FullName);

public sealed record CreateEmployeeRequest(string? EmployeeNo, string? FullName, string? WorkEmail, string? Phone, DateOnly? BirthDate, string? Gender, string? MaritalStatus,
    string? Address, string? EmergencyContactName, string? EmergencyContactPhone, DateOnly? HireDate, string? NationalId, string? TaxId, string? BankName, string? BankAccount,
    string? JobTitle, string? Grade, string? Location, string? Type, DateOnly? ContractEnd, Guid? OrgUnitId, Guid? ManagerId, Guid? UserId, Guid? LegalEntityId = null);

/// <summary>Only the fields sent are changed ("" clears a text field). Version is required (optimistic concurrency).</summary>
public sealed record UpdateEmployeeRequest(int? Version, string? FullName, string? WorkEmail, string? Phone, DateOnly? BirthDate, string? Gender, string? MaritalStatus,
    string? Address, string? EmergencyContactName, string? EmergencyContactPhone, string? NationalId, string? TaxId, string? BankName, string? BankAccount, Guid? UserId, Guid? LegalEntityId = null);

public sealed record ChangeEmploymentRequest(DateOnly? EffectiveFrom, string? JobTitle, string? Grade, string? Location, string? Type, DateOnly? ContractEnd, Guid? OrgUnitId, Guid? ManagerId, string? Reason);
public sealed record TerminateRequest(DateOnly? Date, string? Reason);

public sealed record OrgUnitItem(Guid Id, string Code, string Name, Guid? ParentId, string? ParentName, bool Archived, int Employees);
public sealed record OrgUnitRequest(string? Code, string? Name, Guid? ParentId, bool? Archived);

public sealed record ExportFile(string FileName, string ContentType, byte[] Bytes);

public sealed record ImportEmployeesRequest(string? Csv, bool DryRun = true);
public sealed record ImportEmployeeRow(int Line, string? EmployeeNo, string? Name, string Status, string? Error);
public sealed record ImportEmployeesResult(bool DryRun, int Total, int Created, int Failed, IReadOnlyList<ImportEmployeeRow> Rows);
