using System.Security.Cryptography;

namespace Osteo.Agenda.Core.Domain;

public static class PublicToken
{
    /// <summary>Unguessable, URL-safe token (~128 bits) for client links.</summary>
    public static string New()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
