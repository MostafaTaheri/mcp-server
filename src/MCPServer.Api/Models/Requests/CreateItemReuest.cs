using System.ComponentModel.DataAnnotations;

namespace MCPServer.Api.Models.Requests;

public sealed class CreateItemReuest
{
    [Required(ErrorMessage = "FullName is required")]
    [MaxLength(50)]
    public string FullName {get; set;} = string.Empty;
    [Range(18, 50, ErrorMessage = "Age must be between 18 and 50")]
    public int Age {get; set;}
}