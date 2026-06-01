using System.Text.RegularExpressions;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>
/// Deterministic, data-driven error catalog. Provider-specific rules are tried before
/// provider-agnostic rules. Diagnoses/suggestions come verbatim from the rule and NEVER
/// echo the error signature (which may embed a credential). (CONTRACTS.md §3, DESIGN.md §7/§10)
/// </summary>
public sealed class ErrorCatalog : IErrorCatalog
{
    private readonly IReadOnlyList<CompiledRule> _rules;

    public ErrorCatalog(IReadOnlyList<ErrorRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var compiled = new List<CompiledRule>(rules.Count);
        foreach (var rule in rules)
        {
            Regex regex;
            try
            {
                regex = new Regex(rule.SignatureRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (RegexParseException ex)
            {
                throw new ArgumentException(
                    $"Invalid SignatureRegex '{rule.SignatureRegex}'.", nameof(rules), ex);
            }
            compiled.Add(new CompiledRule(rule, regex));
        }
        _rules = compiled;
    }

    public ErrorResolution? Match(ProviderId provider, string errorSignature)
    {
        ArgumentNullException.ThrowIfNull(errorSignature);

        // Provider-specific rules first, then provider-agnostic.
        var match = FindMatch(provider, errorSignature, providerSpecific: true)
            ?? FindMatch(provider, errorSignature, providerSpecific: false);
        if (match is null)
            return null;

        var r = match.Rule;
        return new ErrorResolution(r, r.Diagnosis, r.Suggestion, r.Kind, r.RecommendedAction, r.Options, r.Severity);
    }

    private CompiledRule? FindMatch(ProviderId provider, string signature, bool providerSpecific)
    {
        foreach (var c in _rules)
        {
            var isProviderSpecific = c.Rule.Provider is not null;
            if (isProviderSpecific != providerSpecific)
                continue;
            if (isProviderSpecific && c.Rule.Provider != provider)
                continue;
            if (c.Regex.IsMatch(signature))
                return c;
        }
        return null;
    }

    private sealed record CompiledRule(ErrorRule Rule, Regex Regex);
}
