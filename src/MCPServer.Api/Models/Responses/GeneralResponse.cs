using MCPServer.Api.Models.Responses.Abstractions;

namespace MCPServer.Api.Models.Responses;

public sealed class GeneralResponse : BaseResponse<string>
{
    public GeneralResponse(string data) : base(data) { }

    public GeneralResponse(string errorCode, string errorMessage, List<string>? details = null) : base(errorCode, errorMessage, details) { }
}