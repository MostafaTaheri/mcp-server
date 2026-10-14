namespace MCPServer.Api.Models.Responses.Abstractions;

public abstract class BaseResponse<T>
{
    public BaseResponse(T data)
    {
        Data = data;
        Success = true;
        Errors = new List<Error>();
    }

    public BaseResponse(string errorCode, string errorMessage, List<string>? details)
    {
        Success = false;
        Data = default!;
        Errors = new List<Error>()
        {
            new Error
            {
                Code = errorCode,
                Message = errorMessage,
                Details = details
            }
        };
    }
    public T Data { get; set; }

    public bool Success { get; set; }

    public List<Error> Errors { get; set; }
}

public class Error
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public List<string>? Details { get; set; }
}