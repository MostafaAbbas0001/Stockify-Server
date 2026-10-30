namespace Stockify.Models.Dtos;

public class CreateEmployeeRequest
{
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public int BranchId { get; set; }
    public bool IsSale { get; set; }
    public decimal Salary { get; set; }
}
