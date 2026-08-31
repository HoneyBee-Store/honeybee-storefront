using System.ComponentModel.DataAnnotations;

namespace HoneyBee.Web.Models;

public class CustomerRegisterViewModel
{
    [Required(ErrorMessage = "Please enter your name.")]
    [MaxLength(120)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Please enter your phone number.")]
    [MaxLength(30)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [MaxLength(200)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please choose a password.")]
    [DataType(DataType.Password)]
    [MinLength(10, ErrorMessage = "Use at least 10 characters.")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class CustomerLoginViewModel
{
    [Required(ErrorMessage = "Please enter your phone number.")]
    [MaxLength(30)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Please enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}
