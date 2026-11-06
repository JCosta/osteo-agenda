using Osteo.Agenda.Core.Contacts;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Import;

public sealed record ParsedTitle(
    string Name,
    string? RawPhone,
    NormalizedPhone? Phone,
    bool ConfirmedMark,
    bool IsBlock,
    Channel? Channel,
    string? ServiceHint);

/// <summary>
/// Reads the calendar title convention: "Nome #Telefone [#Serviço] [#sms]",
/// optionally prefixed with ✅ (confirmed). "#bloqueio" anywhere marks an agenda block.
/// </summary>
public static class TitleParser
{
    private static readonly string[] ConfirmedMarks = ["✅", "✔️", "✔"];

    public static ParsedTitle Parse(string? title)
    {
        var text = (title ?? "").Trim();

        var confirmed = false;
        var stripped = true;
        while (stripped)
        {
            stripped = false;
            foreach (var mark in ConfirmedMarks)
            {
                if (text.StartsWith(mark, StringComparison.Ordinal))
                {
                    text = text[mark.Length..].TrimStart();
                    confirmed = true;
                    stripped = true;
                }
            }
        }

        var parts = text.Split('#');
        var name = CollapseSpaces(parts[0]);
        var tags = parts.Skip(1).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        string? rawPhone = null;
        Channel? channel = null;
        string? serviceHint = null;
        var isBlock = false;

        foreach (var tag in tags)
        {
            if (tag.Equals("bloqueio", StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith("bloqueio ", StringComparison.OrdinalIgnoreCase))
            {
                isBlock = true;
            }
            else if (rawPhone is null && LooksLikePhone(tag))
            {
                rawPhone = tag;
            }
            else if (tag.Equals("sms", StringComparison.OrdinalIgnoreCase))
            {
                channel = Domain.Channel.Sms;
            }
            else if (tag.Equals("email", StringComparison.OrdinalIgnoreCase))
            {
                channel = Domain.Channel.Email;
            }
            else if (tag.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
            {
                channel = Domain.Channel.WhatsApp;
            }
            else
            {
                serviceHint ??= tag;
            }
        }

        return new ParsedTitle(
            Name: name,
            RawPhone: rawPhone,
            Phone: PhoneNormalizer.Normalize(rawPhone),
            ConfirmedMark: confirmed,
            IsBlock: isBlock,
            Channel: channel,
            ServiceHint: serviceHint);
    }

    private static bool LooksLikePhone(string tag) =>
        PhoneNormalizer.DigitCount(tag) >= 8 &&
        tag.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '.' or '(' or ')');

    private static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
