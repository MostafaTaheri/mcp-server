namespace MCPServer.Api.Models.Responses.Abstractions;


public abstract class PaginatedBaseResponse<T> : BaseResponse<PaginatedResponse<T>>
{
    public PaginatedBaseResponse(PaginatedResponse<T> data) : base(data) { }
}

public abstract class PaginatedResponse<O>
{
    public List<O>? Items { get; set; }
    public int TotalCount { get; set; }
}