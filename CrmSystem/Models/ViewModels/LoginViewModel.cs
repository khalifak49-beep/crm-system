using System.ComponentModel.DataAnnotations;

namespace CrmSystem.Models.ViewModels;

/// <summary>
/// Error messages and display names are translation keys (Resources/i18n/*.json).
/// </summary>
public class LoginViewModel
{
    [Required(ErrorMessage = "Validation.UserNameRequired")]
    [StringLength(256)]
    [Display(Name = "Login.UserNameOrEmail")]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.PasswordRequired")]
    [DataType(DataType.Password)]
    [StringLength(128)]
    [Display(Name = "Login.Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Login.RememberMe")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
