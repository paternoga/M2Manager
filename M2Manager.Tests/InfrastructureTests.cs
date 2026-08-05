using M2Manager.Api.Configuration;
using M2Manager.Api.Services;
using Microsoft.Extensions.Configuration;

namespace M2Manager.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify_RoundTrips()
    {
        var hash = PasswordHasher.Hash("moje-tajne-haslo");

        Assert.True(PasswordHasher.Verify("moje-tajne-haslo", hash));
        Assert.False(PasswordHasher.Verify("inne-haslo", hash));
    }

    [Fact]
    public void Hash_ProducesDifferentSaltEachTime()
    {
        var first = PasswordHasher.Hash("to-samo");
        var second = PasswordHasher.Hash("to-samo");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("to-samo", first));
        Assert.True(PasswordHasher.Verify("to-samo", second));
    }

    [Fact]
    public void Hash_UsesExpectedFormat()
    {
        var parts = PasswordHasher.Hash("abc").Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 100_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nieprawidlowy-format")]
    [InlineData("pbkdf2$abc$xx$yy")]
    [InlineData("pbkdf2$1000$###$###")]
    public void Verify_MalformedHash_ReturnsFalseInsteadOfThrowing(string hash)
    {
        Assert.False(PasswordHasher.Verify("cokolwiek", hash));
    }

    [Fact]
    public void VerifyPlainText_ComparesExactly()
    {
        Assert.True(PasswordHasher.VerifyPlainText("haslo", "haslo"));
        Assert.False(PasswordHasher.VerifyPlainText("haslo", "Haslo"));
        Assert.False(PasswordHasher.VerifyPlainText("haslo", "haslo "));
    }
}

public class DatabaseConnectionTests
{
    [Fact]
    public void Normalize_ConvertsNeonUriToNpgsqlConnectionString()
    {
        const string uri = "postgresql://jan:sekret@ep-cool-name-123.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

        var result = DatabaseConnection.Normalize(uri);

        Assert.Contains("Host=ep-cool-name-123.eu-central-1.aws.neon.tech", result);
        Assert.Contains("Database=neondb", result);
        Assert.Contains("Username=jan", result);
        Assert.Contains("Password=sekret", result);
        Assert.Contains("SSL Mode=Require", result);

        // Nieznane parametry Neona nie mogą przeciekać do connection stringa.
        Assert.DoesNotContain("channel_binding", result);
    }

    [Fact]
    public void Normalize_HandlesPostgresSchemeAndCustomPort()
    {
        const string uri = "postgres://user:pass@localhost:5433/mydb";

        var result = DatabaseConnection.Normalize(uri);

        Assert.Contains("Port=5433", result);
        Assert.Contains("Database=mydb", result);
    }

    [Fact]
    public void Normalize_DecodesEscapedPassword()
    {
        const string uri = "postgresql://user:p%40ss%3Aword@host/db";

        var result = DatabaseConnection.Normalize(uri);

        Assert.Contains("p@ss:word", result);
    }

    [Fact]
    public void Normalize_KeyValueStringIsPassedThroughUnchanged()
    {
        const string raw = "Host=localhost;Database=m2manager;Username=postgres;Password=postgres";

        Assert.Equal(raw, DatabaseConnection.Normalize(raw));
    }

    [Fact]
    public void Resolve_PrefersConnectionStringsSectionOverDatabaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=primary;Database=a",
                ["DATABASE_URL"] = "postgresql://u:p@fallback/b"
            })
            .Build();

        Assert.Contains("Host=primary", DatabaseConnection.Resolve(configuration));
    }

    [Fact]
    public void Resolve_FallsBackToDatabaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://u:p@fallback-host/b"
            })
            .Build();

        Assert.Contains("Host=fallback-host", DatabaseConnection.Resolve(configuration));
    }

    [Fact]
    public void Resolve_NothingConfigured_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(DatabaseConnection.Resolve(configuration));
    }
}

public class ObjectKeyTests
{
    [Fact]
    public void BuildObjectKey_UsesYearMonthFoldersAndKeepsExtension()
    {
        var key = IObjectStorage.BuildObjectKey("paragon.PNG", new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("invoices/2026/03/", key);
        Assert.EndsWith(".png", key);
    }

    [Fact]
    public void BuildObjectKey_MissingExtension_DefaultsToJpg()
    {
        var key = IObjectStorage.BuildObjectKey("zdjecie", DateTime.UtcNow);

        Assert.EndsWith(".jpg", key);
    }

    [Fact]
    public void BuildObjectKey_IsUniquePerCall()
    {
        var now = DateTime.UtcNow;

        Assert.NotEqual(
            IObjectStorage.BuildObjectKey("a.jpg", now),
            IObjectStorage.BuildObjectKey("a.jpg", now));
    }
}
