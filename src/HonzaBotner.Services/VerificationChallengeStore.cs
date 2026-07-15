using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HonzaBotner.Services.Contract.Dto;

namespace HonzaBotner.Services;

public sealed record VerificationChallenge(
    string AuthId,
    string LegacyAuthId,
    RolesPool RolesPool,
    IReadOnlySet<ulong> RoleIds,
    DateTimeOffset ExpiresAt);

public interface IVerificationChallengeStore
{
    string Create(string authId, string legacyAuthId, RolesPool rolesPool, IReadOnlySet<ulong> roleIds);
    bool TryTake(string code, out VerificationChallenge? challenge);
}

public sealed class VerificationChallengeStore : IVerificationChallengeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, VerificationChallenge> _challenges = new();

    public string Create(string authId, string legacyAuthId, RolesPool rolesPool, IReadOnlySet<ulong> roleIds)
    {
        RemoveExpired();

        string code;
        string codeHash;
        do
        {
            code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            codeHash = HashCode(code);
        } while (!_challenges.TryAdd(codeHash,
                     new VerificationChallenge(authId, legacyAuthId, rolesPool, roleIds,
                         DateTimeOffset.UtcNow.Add(Lifetime))));

        return code;
    }

    public bool TryTake(string code, out VerificationChallenge? challenge)
    {
        challenge = null;
        if (string.IsNullOrWhiteSpace(code)) return false;

        if (!_challenges.TryRemove(HashCode(code.Trim()), out VerificationChallenge? stored) ||
            stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        challenge = stored;
        return true;
    }

    private void RemoveExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string key, VerificationChallenge challenge) in _challenges)
        {
            if (challenge.ExpiresAt <= now) _challenges.TryRemove(key, out _);
        }
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToUpperInvariant())));
}
