using MCPServer.Application.Exceptions;

namespace MCPServer.Application.Abstractions;

public abstract class BaseBusinessValidator : IBusinessValidator
{
   public async Task AssertValidationAsync() 
   {
      var exceptions = await ValidateAsync();
      if (exceptions != null && exceptions.Any())
      {
         var exception = exceptions.First();
         exception.Details = exceptions[1..].Select(x => x.Message).ToList();
         throw exception;
      }
   }
   
   public abstract Task<List<BusinessException>> ValidateAsync();

}