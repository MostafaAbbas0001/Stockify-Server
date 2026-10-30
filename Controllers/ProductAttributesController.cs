namespace Stockify.Controllers;

[ApiController]
[Route("api/attributes")]
public class ProductAttributesController : ControllerBase
{
    private readonly ProductAttributeService _service;

    public ProductAttributesController(ProductAttributeService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission("attribute.view")]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    [RequirePermission("attribute.view")]
    public async Task<IActionResult> GetById(int id)
    {
        return Ok(await _service.GetByIdAsync(id));
    }

    [HttpPost]
    [RequirePermission("attribute.create")]
    public async Task<IActionResult> Create(CreateProductAttributeRequest dto)
    {
        var attribute = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = attribute.Id }, attribute);
    }

    [HttpPut("{id:int}")]
    [RequirePermission("attribute.update")]
    public async Task<IActionResult> Update(int id, UpdateProductAttributeRequest dto)
    {
        return Ok(await _service.UpdateAsync(id, dto));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission("attribute.delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{attributeId:int}/values")]
    [RequirePermission("attributeValue.create")]
    public async Task<IActionResult> CreateValue(
        int attributeId,
        CreateProductAttributeValueRequest dto)
    {
        var value = await _service.CreateValueAsync(attributeId, dto);
        return CreatedAtAction(nameof(GetById), new { id = attributeId }, value);
    }

    [HttpPut("{attributeId:int}/values/{valueId:int}")]
    [RequirePermission("attributeValue.update")]
    public async Task<IActionResult> UpdateValue(
        int attributeId,
        int valueId,
        UpdateProductAttributeValueRequest dto)
    {
        return Ok(await _service.UpdateValueAsync(attributeId, valueId, dto));
    }

    [HttpDelete("{attributeId:int}/values/{valueId:int}")]
    [RequirePermission("attributeValue.delete")]
    public async Task<IActionResult> DeleteValue(int attributeId, int valueId)
    {
        await _service.DeleteValueAsync(attributeId, valueId);
        return NoContent();
    }
}
