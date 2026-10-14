using Asp.Versioning;
using MCPServer.Api.Models.Requests;
using MCPServer.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;

namespace MCPServer.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
public class UserController : ControllerBase
{
    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(GeneralResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GeneralResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GeneralResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateBuilding(
        [FromBody] CreateItemReuest request,
        CancellationToken cancellationToken)
    {
        return StatusCode(StatusCodes.Status201Created, new GeneralResponse(Guid.NewGuid().ToString("N")));
    }
} 