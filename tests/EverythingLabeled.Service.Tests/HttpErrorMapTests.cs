using System.Reflection;
using EverythingLabeled.Service.Http;
using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Tests;

public sealed class HttpErrorMapTests
{
    public static TheoryData<string, int> MappedCodes()
    {
        var data = new TheoryData<string, int>
        {
            { ErrorCodes.BadRequest, 400 },
            { ErrorCodes.RelationsFilterRequired, 400 },
            { ErrorCodes.Unauthorized, 401 },
            { ErrorCodes.NotFound, 404 },
            { ErrorCodes.TagNotFound, 404 },
            { ErrorCodes.ResourceNotFound, 404 },
            { ErrorCodes.AliasConflict, 409 },
            { ErrorCodes.NameConflict, 409 },
            { ErrorCodes.UriConflict, 409 },
            { ErrorCodes.RelationConflict, 409 },
            { ErrorCodes.RelationCycle, 409 },
            { ErrorCodes.ResourceIdUriMismatch, 409 },
            { ErrorCodes.ValidationFailed, 422 },
            { ErrorCodes.BatchLimitExceeded, 422 },
            { ErrorCodes.ExpansionLimitExceeded, 422 },
            { ErrorCodes.InternalError, 500 },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(MappedCodes))]
    public void StatusFor_matches_contract_table(string code, int status) =>
        Assert.Equal(status, HttpErrorMap.StatusFor(code));

    [Fact]
    public void StatusFor_covers_every_ErrorCodes_constant()
    {
        var mapped = MappedCodes().Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);
        var constants = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.Equal(constants.Count, mapped.Count);
        foreach (var code in constants)
            Assert.True(mapped.Contains(code), $"ErrorCodes constant '{code}' is missing from HttpErrorMap.");
    }

    [Fact]
    public void Unknown_code_maps_to_500() =>
        Assert.Equal(500, HttpErrorMap.StatusFor("no_such_code"));
}
