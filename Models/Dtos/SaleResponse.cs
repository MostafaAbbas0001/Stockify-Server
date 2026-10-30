namespace Stockify.Models.Dtos;

public sealed record SaleResponse(
    int Id,
    string Name,
    int SaleTypeId,
    string SaleTypeCode,
    decimal Value,
    DateTime? StartDate,
    DateTime? EndDate,
    bool IsDeleted,
    DateTime CreatedAt);
