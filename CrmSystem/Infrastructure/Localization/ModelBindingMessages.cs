using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CrmSystem.Infrastructure.Localization;

/// <summary>
/// Translates the built-in model binding messages ("The value '' is invalid." ...)
/// that MVC produces before DataAnnotations run.
/// </summary>
public sealed class ModelBindingMessages : IConfigureOptions<MvcOptions>
{
    private readonly IStringLocalizerFactory _factory;

    public ModelBindingMessages(IStringLocalizerFactory factory) => _factory = factory;

    public void Configure(MvcOptions options)
    {
        var l = _factory.Create(typeof(SharedResource));
        var p = options.ModelBindingMessageProvider;
        p.SetValueMustNotBeNullAccessor(field => l["Validation.Required", field]);
        p.SetMissingBindRequiredValueAccessor(field => l["Validation.Required", field]);
        p.SetMissingKeyOrValueAccessor(() => l["Validation.RequiredShort"]);
        p.SetAttemptedValueIsInvalidAccessor((value, field) => l["Validation.Invalid", field]);
        p.SetUnknownValueIsInvalidAccessor(field => l["Validation.Invalid", field]);
        p.SetValueIsInvalidAccessor(value => l["Validation.InvalidShort"]);
        p.SetValueMustBeANumberAccessor(field => l["Validation.Number", field]);
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(value => l["Validation.InvalidShort"]);
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => l["Validation.InvalidShort"]);
        p.SetNonPropertyValueMustBeANumberAccessor(() => l["Validation.NumberShort"]);
    }
}
