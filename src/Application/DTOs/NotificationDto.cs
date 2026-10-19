using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public sealed record NotificationDto(
    [Required] 
    [EmailAddress]
    [property: DisplayName("recipient")]
    string Recipient,
    
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    string Subject,
    
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    string Message);
