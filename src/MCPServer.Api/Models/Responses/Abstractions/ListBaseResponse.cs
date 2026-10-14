namespace MCPServer.Api.Models.Responses.Abstractions;

public abstract class ListBaseResponse<T> : BaseResponse<List<T>>
{
    public ListBaseResponse(List<T> data) : base(data) { }
}
