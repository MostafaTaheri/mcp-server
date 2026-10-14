namespace MCPServer.Domain.Exceptions;

public abstract class BaseException : Exception
{
    public string Code { get; private set; }
    public List<string> Details { get; set; }

    protected BaseException(string code, string message, List<string> details) : base(message)
    {
        Code = code;
        Details = details;
    }
}