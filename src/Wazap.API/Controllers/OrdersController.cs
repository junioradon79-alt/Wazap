using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.API.Services;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;
    private readonly IValidator<CreateOrderRequest> _createValidator;
    private readonly IValidator<UpdateStatusRequest> _updateValidator;

    public OrdersController(
        OrderService orderService,
        IValidator<CreateOrderRequest> createValidator,
        IValidator<UpdateStatusRequest> updateValidator)
    {
        _orderService = orderService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    // POST: api/orders
    [HttpPost]
    [Authorize(Roles = "Admin,Vendor")]
    public async Task<ActionResult<Order>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var order = await _orderService.CreateOrderAsync(request);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    // GET: api/orders/{id}
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<Order>> GetOrder(Guid id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order == null)
            return NotFound();
        return order;
    }

    // GET: api/orders?page=1&pageSize=50
    [HttpGet]
    [Authorize(Roles = "Admin,Vendor")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        return await _orderService.GetOrdersAsync(page, pageSize);
    }

    // PUT: api/orders/{id}/status
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Admin,Vendor,Rider")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var updated = await _orderService.UpdateStatusAsync(id, request);
        return updated ? NoContent() : NotFound();
    }
}
