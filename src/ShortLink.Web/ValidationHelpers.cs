using Microsoft.AspNetCore.Components.Forms;

namespace ShortLink.Web;

/// <summary>
/// Blazor form-validation helpers shared between pages that consume the SliceFx API.
/// </summary>
internal static class ValidationHelpers
{
    /// <summary>
    /// Maps server-side Problem Details field errors into a Blazor
    /// <see cref="ValidationMessageStore"/> and notifies the <see cref="EditContext"/>.
    /// SliceFx emits Pascal-case property names (e.g. "TargetUrl") that match
    /// <see cref="FieldIdentifier.FieldName"/> directly.
    /// </summary>
    internal static void ApplyTo(
        this IReadOnlyDictionary<string, string[]> errors,
        object model,
        ValidationMessageStore store,
        EditContext ctx)
    {
        foreach (var (field, messages) in errors)
        {
            var fi = new FieldIdentifier(model, field);
            foreach (var msg in messages)
            {
                store.Add(fi, msg);
            }
        }

        ctx.NotifyValidationStateChanged();
    }
}
