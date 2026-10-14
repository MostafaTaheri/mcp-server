using MCPServer.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MCPServer.Api.Middlewares;

public class ActionModelValidationFilter : IActionFilter
{
    private readonly ILogger<ActionModelValidationFilter> _logger;

    public ActionModelValidationFilter(ILogger<ActionModelValidationFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            var errors = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            context.Result = new ObjectResult(
                new GeneralResponse("ValidationError",
                    "Invalid parameters provided. For more information check details.",
                    errors
                ))
            {
                StatusCode = 400
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}