using MCPServer.Application.Exceptions;

namespace MCPServer.Application.Abstractions;

public interface IBusinessValidator
{
   Task<List<BusinessException>> ValidateAsync();
}
