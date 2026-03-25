using System.Reflection;
using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Tests;

public sealed class PermissionsTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_UseColonSeparatedFormat()
    {
        var fields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Contains(':', value);
            Assert.Equal(2, value.Split(':').Length);
        }
    }

    [Theory]
    [InlineData("chat:query")]
    [InlineData("chat:feedback")]
    [InlineData("chat:outcome")]
    [InlineData("session:read_own")]
    [InlineData("session:read_team")]
    [InlineData("connector:manage")]
    [InlineData("connector:sync")]
    [InlineData("pattern:approve")]
    [InlineData("pattern:deprecate")]
    [InlineData("pattern:read")]
    [InlineData("report:read")]
    [InlineData("audit:read")]
    [InlineData("audit:export")]
    [InlineData("tenant:manage")]
    [InlineData("privacy:manage")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }

    [Fact]
    public void HasExpectedCount()
    {
        var fields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(15, fields.Length);
    }

    [Fact]
    public void AllRolePermissions_ReferenceDefinedConstants()
    {
        var permissionValues = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet();

        foreach (var (role, perms) in RolePermissions.Matrix)
        {
            foreach (var perm in perms)
            {
                Assert.True(permissionValues.Contains(perm),
                    $"Role {role} references permission '{perm}' which is not defined in Permissions class.");
            }
        }
    }
}
