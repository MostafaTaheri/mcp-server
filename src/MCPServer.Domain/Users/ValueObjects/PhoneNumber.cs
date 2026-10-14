using System.Text.RegularExpressions;
using MCPServer.Domain.Abstractions;

namespace MCPServer.Domain.Users.ValueObjects;

public class PhoneNumber : ValueObject
{
    public const string PhoneRegex = @"^\+\d{1,4}\d{10}$";
    public string Value { get; private set; } = null!;

    private PhoneNumber() { }

    public PhoneNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidPhoneException(message: "Phone number cannot be empty", details: []);
        }

        var cleanedValue = value.Trim().Replace(" ", "").Replace("-", "");
        
        if (!Regex.IsMatch(cleanedValue, PhoneRegex ))
        {
            throw new InvalidPhoneException(message:  "Invalid phone number format. Expected: 09xxxxxxxxx", details: []);
        }

        Value = cleanedValue;
    }

    public static PhoneNumber Create(string value) => new PhoneNumber(value);

    protected IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
