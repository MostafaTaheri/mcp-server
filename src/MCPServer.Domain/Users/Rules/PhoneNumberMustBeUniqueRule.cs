using MCPServer.Domain.Abstractions;
using MCPServer.Domain.Exceptions;
using MCPServer.Domain.Users.ValueObjects;

namespace MCPServer.Domain.Users.Rules;

public class PhoneNumberMustBeUniqueRule : BaseBusinessRule
{
    private readonly IUserRepository _userRepository;
    private readonly PhoneNumber _phoneNumber;

    public PhoneNumberMustBeUniqueRule(IUserRepository userRepository, PhoneNumber phoneNumber)
    {
        _userRepository = userRepository;
        _phoneNumber = phoneNumber;
    }

    public override bool IsBroken()
    {
        return false;
    }

    public override async Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
    {
        return await _userRepository.ExistsByPhoneNumberAsync(_phoneNumber, cancellationToken);
    }

    public override BaseException GetRelatedException()
    {
        return new InvalidPhoneException();
    }
}
