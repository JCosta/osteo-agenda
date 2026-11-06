using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Contacts;

public readonly record struct NormalizedPhone(string E164, PhoneType Type);

/// <summary>
/// Normalizes phone numbers to E.164, assuming Portugal when no country code is given.
/// PT numbers: 9 digits; 9xx = mobile, 2xx = landline.
/// </summary>
public static class PhoneNormalizer
{
    public static NormalizedPhone? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var international = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        if (!international && digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
            international = true;
        }

        if (!international && digits.Length == 9)
        {
            return new NormalizedPhone("+351" + digits, Classify(digits));
        }

        if (digits.StartsWith("351", StringComparison.Ordinal) && digits.Length == 12)
        {
            return new NormalizedPhone("+" + digits, Classify(digits[3..]));
        }

        if (international && digits.Length is >= 8 and <= 15)
        {
            return new NormalizedPhone("+" + digits, PhoneType.Unknown);
        }

        return null;
    }

    /// <summary>Counts digits, to decide whether a token looks like a phone number at all.</summary>
    public static int DigitCount(string value) => value.Count(char.IsAsciiDigit);

    private static PhoneType Classify(string national) => national[0] switch
    {
        '9' => PhoneType.Mobile,
        '2' => PhoneType.Landline,
        _ => PhoneType.Unknown,
    };

    /// <summary>"+351912345678" -> "912 345 678"; other countries keep the + form.</summary>
    public static string Display(string? e164)
    {
        if (string.IsNullOrEmpty(e164))
        {
            return "";
        }
        if (e164.StartsWith("+351", StringComparison.Ordinal) && e164.Length == 13)
        {
            var n = e164[4..];
            return $"{n[..3]} {n[3..6]} {n[6..]}";
        }
        return e164;
    }
}
