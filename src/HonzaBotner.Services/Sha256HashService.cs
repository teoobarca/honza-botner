using System;
using System.Security.Cryptography;
using System.Text;
using HonzaBotner.Services.Contract;
using HonzaBotner.Services.Contract.Dto;
using Microsoft.Extensions.Options;

namespace HonzaBotner.Services;

public class Sha256HashService : IHashService
{
    private readonly byte[]? _key;

    public Sha256HashService()
    {
    }

    public Sha256HashService(IOptions<CvutConfig> options)
    {
        string? key = options.Value.IdentityHashKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Cvut:IdentityHashKey must be configured.");
        _key = Encoding.UTF8.GetBytes(key);
    }

    public string Hash(string input)
    {
        if (_key is null) return LegacyHash(input);
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    public string LegacyHash(string input)
    {
        var encLen = (input.Length + 1) * 3;
        var enc = encLen <= 1024 ? stackalloc byte[encLen] : new byte[encLen];
        Span<byte> bytes = stackalloc byte[256 / 8];

        var len = Encoding.UTF8.GetBytes(input, enc);
        SHA256.HashData(enc[..len], bytes);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
