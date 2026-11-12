using System.ComponentModel.DataAnnotations;

namespace CrmSystem.Models.ViewModels;

public class UserFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Validation.LengthRange")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Validation.UserName")]
    [Display(Name = "Field.UserName")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(150, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FullName")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FullNameAr")]
    public string? FullNameAr { get; set; }

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.JobTitle")]
    public string? JobTitle { get; set; }

    [StringLength(30, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Phone")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Role")]
    public string Role { get; set; } = Identity.AppRoles.SalesEmployee;

    [Display(Name = "Field.Manager")]
    public string? ManagerId { get; set; }

    [Display(Name = "Field.Language")]
    public string PreferredLanguage { get; set; } = "en";

    [Display(Name = "Field.Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Users.ReceiveEmail")]
    public bool ReceiveEmailNotifications { get; set; } = true;

    [Display(Name = "Users.ReceiveBrowser")]
    public bool ReceiveBrowserNotifications { get; set; } = true;

    [DataType(DataType.Password)]
    [Display(Name = "Field.Password")]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Validation.PasswordMismatch")]
    [Display(Name = "Field.ConfirmPassword")]
    public string? ConfirmPassword { get; set; }

    // display
    public string? ManagerName { get; set; }
    public bool IsSelf { get; set; }
}

public class ResetPasswordViewModel
{
    public string Id { get; set; } = string.Empty;
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Validation.PasswordMismatch")]
    [Display(Name = "Field.ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.CurrentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Display(Name = "Field.NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Validation.PasswordMismatch")]
    [Display(Name = "Field.ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
