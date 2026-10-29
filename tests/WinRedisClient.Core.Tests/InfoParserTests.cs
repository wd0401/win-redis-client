using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

public class InfoParserTests
{
    private const string Sample = """
        # Server
        redis_version:7.2.4
        os:Linux
        # Clients
        connected_clients:3

        # Memory
        used_memory_human:1.25M
        used_memory:1310720
        """;

    [Fact]
    public void Parse_CreatesSectionsInOrder()
    {
        var snapshot = InfoParser.Parse(Sample);

        Assert.Equal(["Server", "Clients", "Memory"], snapshot.Sections.Select(s => s.Name));
        Assert.Equal(2, snapshot.Sections[0].Entries.Count);
        Assert.Single(snapshot.Sections[1].Entries);
    }

    [Fact]
    public void Parse_FlattensFieldsCaseInsensitively()
    {
        var snapshot = InfoParser.Parse(Sample);

        Assert.Equal("7.2.4", snapshot.Get("redis_version"));
        Assert.Equal("7.2.4", snapshot.Get("REDIS_VERSION"));
        Assert.Equal(3, snapshot.GetLong("connected_clients"));
        Assert.Equal(1310720d, snapshot.GetDouble("used_memory"));
    }

    [Fact]
    public void Parse_HandlesCrlfAndBlankLines()
    {
        var snapshot = InfoParser.Parse("# Stats\r\nkeyspace_hits:10\r\n\r\nkeyspace_misses:5\r\n");

        Assert.Equal(10, snapshot.GetLong("keyspace_hits"));
        Assert.Equal(5, snapshot.GetLong("keyspace_misses"));
        Assert.Single(snapshot.Sections);
        Assert.Equal("Stats", snapshot.Sections[0].Name);
    }

    [Fact]
    public void Parse_FieldsBeforeFirstSection_UseGeneralName()
    {
        var snapshot = InfoParser.Parse("redis_mode:standalone\n# Replication\nrole:master");

        Assert.Equal(2, snapshot.Sections.Count);
        Assert.Equal("General", snapshot.Sections[0].Name);
        Assert.Equal("standalone", snapshot.Get("redis_mode"));
    }

    [Fact]
    public void Parse_IgnoresMalformedLines()
    {
        var snapshot = InfoParser.Parse("# Server\nno-colon-line\n:value-without-key\nok:1");

        Assert.Single(snapshot.Sections);
        Assert.Single(snapshot.Sections[0].Entries);
        Assert.Equal("1", snapshot.Get("ok"));
        Assert.Null(snapshot.Get("no-colon-line"));
    }

    [Fact]
    public void Parse_PreservesRawTextAndTimestamp()
    {
        var retrievedAt = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(8));
        var snapshot = InfoParser.Parse(Sample, retrievedAt);

        Assert.Equal(Sample, snapshot.RawText);
        Assert.Equal(retrievedAt, snapshot.RetrievedAt);
    }

    [Fact]
    public void Parse_SplitsAtFirstColonOnly()
    {
        var snapshot = InfoParser.Parse("# Keyspace\ndb0:keys=3,expires=1,avg_ttl=0");

        Assert.Equal("keys=3,expires=1,avg_ttl=0", snapshot.Get("db0"));
        Assert.Null(snapshot.Get("db0:keys"));
    }

    [Fact]
    public void ParseDatabaseKeyCounts_ReadsAllDatabases()
    {
        const string keyspace = """
            # Keyspace
            db0:keys=26,expires=0,avg_ttl=0
            db1:keys=4,expires=1,avg_ttl=3600000
            db9:keys=14,expires=0,avg_ttl=0
            """;

        var counts = InfoParser.ParseDatabaseKeyCounts(keyspace);

        Assert.Equal(3, counts.Count);
        Assert.Equal(26, counts[0]);
        Assert.Equal(4, counts[1]);
        Assert.Equal(14, counts[9]);
    }

    [Fact]
    public void ParseDatabaseKeyCounts_IgnoresMalformedEntries()
    {
        const string keyspace = """
            # Keyspace
            db0:keys=3,expires=1
            db2:expires=1
            dbx:keys=9
            db3:keys=not-a-number
            db15:keys=0,expires=0
            """;

        var counts = InfoParser.ParseDatabaseKeyCounts(keyspace);

        Assert.Equal(2, counts.Count);
        Assert.Equal(3, counts[0]);
        Assert.Equal(0, counts[15]);
    }
}
