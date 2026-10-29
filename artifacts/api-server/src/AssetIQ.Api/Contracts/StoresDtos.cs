namespace AssetIQ.Api.Contracts;

// ── Phase 4b: Stores, equipment requests, HR toolbox, departments ───────────

/// <summary><c>UnitCost</c> is null for roles without financial access.</summary>
public sealed record PurchaseOrderLineDto(int Id, int CategoryId, string CategoryName, string Description, int Quantity, int ReceivedQuantity, decimal? UnitCost);

public sealed record GoodsReceiptLineDto(int Id, int PurchaseOrderLineId, string Description, int QuantityReceived, int QuantityDamaged,
    IReadOnlyList<string> Serials, string? Condition, int Registered);

public sealed record GoodsReceiptDto(int Id, DateTime ReceivedAt, string ReceivedBy, bool Flagged, string? FlagReason, string? Notes, IReadOnlyList<GoodsReceiptLineDto> Lines);

public sealed record PurchaseOrderDto(int Id, int SbuId, string SbuCode, string PoNo, string Supplier, string Status, string CreatedBy, DateTime CreatedAt,
    IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<GoodsReceiptDto> Receipts);

/// <summary>ST-03: items in store, by category, type and location.</summary>
public sealed record StockLineDto(string SbuCode, string Category, string? SubCategory, string? Location, int Count, int Untagged);

public sealed record EquipmentRequestDto(int Id, int SbuId, int? EmployeeId, string? EmployeeName, int? DepartmentId, string? DepartmentName, int CategoryId,
    string CategoryName, string ItemType, int Quantity, int IssuedQuantity, string Reason, string Urgency, string Status, string Source, string RequestedBy, DateTime CreatedAt);

public sealed record AcknowledgementDto(int Id, int AssetId, string? AssetNumber, string AssetDescription, int EmployeeId, string IssuedBy, DateTime IssuedAt,
    DateTime? AcknowledgedAt, string? Method);

public sealed record ToolboxItemDto(int AssetId, string? AssetNumber, string Description, string? Tag, string Category, string? Condition, string Status,
    DateTime? IssuedAt, int? AcknowledgementId, DateTime? AcknowledgedAt);

public sealed record ToolboxHistoryDto(int AssetId, string? AssetNumber, string Description, DateTime ReturnedAt);

/// <summary>HR-01: everything the SBU has issued to one employee, and what they held before.</summary>
public sealed record ToolboxDto(int EmployeeId, string EmployeeNo, string Name, string? JobRole, bool Active, IReadOnlyList<ToolboxItemDto> Items,
    IReadOnlyList<ToolboxHistoryDto> History, int? OpenExitClearanceId);

public sealed record ExitClearanceItemDto(int Id, int AssetId, string? AssetNumber, string Description, bool IsIt, string Outcome, string? RecordedBy,
    DateTime? RecordedAt, string? Notes);

public sealed record ExitClearanceDto(int Id, int EmployeeId, string EmployeeName, string Reason, string Status, string? ExceptionApprovedBy, string? ExceptionReason,
    DateTime CreatedAt, DateTime? CompletedAt, IReadOnlyList<ExitClearanceItemDto> Items);

public sealed record HoldingItemDto(int AssetId, string? AssetNumber, string Description, string Category, bool IsIt, string? Holder, int? EmployeeId,
    string Status, string? Location, string? Condition);

public sealed record EmployeeHoldingDto(int EmployeeId, string Name, string? JobRole, int Items);

/// <summary>LM-01 / LM-02: what a department holds (IT and non-IT), and per person.</summary>
public sealed record DepartmentHoldingsDto(int DepartmentId, string DepartmentName, IReadOnlyList<HoldingItemDto> Items, IReadOnlyList<EmployeeHoldingDto> Employees);
