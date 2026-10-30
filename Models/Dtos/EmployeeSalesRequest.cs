namespace Stockify.Models.Dtos;

public class EmployeeSalesRequest
{
    public int EmployeeId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
