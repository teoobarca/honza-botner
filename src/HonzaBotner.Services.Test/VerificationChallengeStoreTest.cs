using System.Collections.Generic;
using HonzaBotner.Services.Contract.Dto;
using Shouldly;
using Xunit;

namespace HonzaBotner.Services.Test;

public sealed class VerificationChallengeStoreTest
{
    [Fact]
    public void ChallengeCanBeConsumedOnlyOnce()
    {
        VerificationChallengeStore store = new();
        HashSet<ulong> roles = new() { 10, 20 };
        string code = store.Create("new-auth-id", "legacy-auth-id", RolesPool.Staff, roles);

        store.TryTake(code, out VerificationChallenge? challenge).ShouldBeTrue();
        challenge.ShouldNotBeNull();
        challenge.AuthId.ShouldBe("new-auth-id");
        challenge.LegacyAuthId.ShouldBe("legacy-auth-id");
        challenge.RolesPool.ShouldBe(RolesPool.Staff);
        challenge.RoleIds.ShouldBe(roles);

        store.TryTake(code, out _).ShouldBeFalse();
    }

    [Fact]
    public void InvalidCodeDoesNotConsumeValidChallenge()
    {
        VerificationChallengeStore store = new();
        string code = store.Create("auth-id", "legacy-auth-id", RolesPool.Auth, new HashSet<ulong>());

        store.TryTake("NOT-THE-CODE", out _).ShouldBeFalse();
        store.TryTake(code, out _).ShouldBeTrue();
    }
}
