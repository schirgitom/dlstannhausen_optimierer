using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoWeiT.Optimizer.Persistence.History.Data;

namespace SoWeiT.Optimizer.Api.Controllers;

public sealed record CustomerDto(long Id, string Name, string? CustomerNumber, DateTime CreatedAtUtc);
public sealed record UpsertCustomerRequest(string Name, string? CustomerNumber);

[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly IDbContextFactory<OptimizerHistoryDbContext> _dbContextFactory;

    public CustomersController(IDbContextFactory<OptimizerHistoryDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<CustomerDto>> GetAll()
    {
        using var db = _dbContextFactory.CreateDbContext();
        var customers = db.Customers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CustomerDto(x.Id, x.Name, x.CustomerNumber, x.CreatedAtUtc))
            .ToList();
        return Ok(customers);
    }

    [HttpGet("{id:long}")]
    public ActionResult<CustomerDto> GetById(long id)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var customer = db.Customers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new CustomerDto(x.Id, x.Name, x.CustomerNumber, x.CreatedAtUtc))
            .SingleOrDefault();
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public ActionResult<CustomerDto> Create([FromBody] UpsertCustomerRequest request)
    {
        var normalizedName = NormalizeName(request.Name);
        if (normalizedName is null)
        {
            return BadRequest(new { Message = "Name darf nicht leer sein." });
        }
        var normalizedCustomerNumber = NormalizeOptionalValue(request.CustomerNumber);

        using var db = _dbContextFactory.CreateDbContext();
        if (db.Customers.Any(x => x.Name == normalizedName))
        {
            return Conflict(new { Message = $"Customer '{normalizedName}' existiert bereits." });
        }

        var entity = new CustomerEntry
        {
            Name = normalizedName,
            CustomerNumber = normalizedCustomerNumber,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Customers.Add(entity);
        db.SaveChanges();

        var dto = new CustomerDto(entity.Id, entity.Name, entity.CustomerNumber, entity.CreatedAtUtc);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, dto);
    }

    [HttpPut("{id:long}")]
    public ActionResult<CustomerDto> Update(long id, [FromBody] UpsertCustomerRequest request)
    {
        var normalizedName = NormalizeName(request.Name);
        if (normalizedName is null)
        {
            return BadRequest(new { Message = "Name darf nicht leer sein." });
        }
        var normalizedCustomerNumber = NormalizeOptionalValue(request.CustomerNumber);

        using var db = _dbContextFactory.CreateDbContext();
        var existing = db.Customers.SingleOrDefault(x => x.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        if (db.Customers.Any(x => x.Id != id && x.Name == normalizedName))
        {
            return Conflict(new { Message = $"Customer '{normalizedName}' existiert bereits." });
        }

        existing.Name = normalizedName;
        existing.CustomerNumber = normalizedCustomerNumber;
        db.SaveChanges();

        return Ok(new CustomerDto(existing.Id, existing.Name, existing.CustomerNumber, existing.CreatedAtUtc));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var existing = db.Customers.SingleOrDefault(x => x.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        var isReferenced = db.RequestUsers.Any(x => x.CustomerId == id);
        if (isReferenced)
        {
            return Conflict(new { Message = "Customer ist bereits in User-Eintraegen referenziert und kann nicht geloescht werden." });
        }

        db.Customers.Remove(existing);
        db.SaveChanges();
        return NoContent();
    }

    private static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim();
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
