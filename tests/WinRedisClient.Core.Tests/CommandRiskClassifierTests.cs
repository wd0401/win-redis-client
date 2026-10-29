using WinRedisClient.Core.Models;
using WinRedisClient.Core.Services;

namespace WinRedisClient.Core.Tests;

public class CommandRiskClassifierTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("info")]
    [InlineData("SCAN")]
    [InlineData("HGETALL")]
    public void ReadOnlyCommands_AreClassifiedReadOnly(string command)
    {
        Assert.Equal(CommandRiskLevel.ReadOnly, CommandRiskClassifier.Classify(command));
    }

    [Theory]
    [InlineData("SET")]
    [InlineData("hset")]
    [InlineData("EXPIRE")]
    public void SingleWriteCommands_AreClassifiedSingleWrite(string command)
    {
        Assert.Equal(CommandRiskLevel.SingleWrite, CommandRiskClassifier.Classify(command));
    }

    [Fact]
    public void Del_WithSingleKey_IsSingleWrite_ButMultiKeyIsBatch()
    {
        Assert.Equal(CommandRiskLevel.SingleWrite, CommandRiskClassifier.Classify("DEL", ["k"]));
        Assert.Equal(CommandRiskLevel.BatchOrScript, CommandRiskClassifier.Classify("DEL", ["k1", "k2"]));
    }

    [Theory]
    [InlineData("EVAL", CommandRiskLevel.BatchOrScript)]
    [InlineData("MSET", CommandRiskLevel.BatchOrScript)]
    [InlineData("MULTI", CommandRiskLevel.BatchOrScript)]
    [InlineData("THIS_IS_NOT_A_COMMAND", CommandRiskLevel.BatchOrScript)]
    [InlineData("", CommandRiskLevel.BatchOrScript)]
    public void BatchScriptAndUnknownCommands_RequireConfirmation(string command, CommandRiskLevel expected)
    {
        Assert.Equal(expected, CommandRiskClassifier.Classify(command));
    }

    [Theory]
    [InlineData("FLUSHALL")]
    [InlineData("FLUSHDB")]
    [InlineData("SHUTDOWN")]
    [InlineData("CONFIG")]
    public void HighRiskCommands_AreClassifiedHighRisk(string command)
    {
        string[]? args = command == "CONFIG" ? ["SET", "maxmemory", "0"] : null;
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify(command, args));
    }

    [Fact]
    public void AdminSubcommands_AreEscalated()
    {
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify("CONFIG", ["REWRITE"]));
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify("ACL", ["SETUSER", "u", "on"]));
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify("CLUSTER", ["RESET"]));
        Assert.Equal(CommandRiskLevel.ReadOnly, CommandRiskClassifier.Classify("CONFIG", ["GET", "maxmemory"]));
        Assert.Equal(CommandRiskLevel.ReadOnly, CommandRiskClassifier.Classify("ACL", ["WHOAMI"]));
    }

    [Fact]
    public void Effective_UsesHigherOfDeclaredAndActual()
    {
        Assert.Equal(CommandRiskLevel.SingleWrite,
            CommandRiskClassifier.Effective("SET", null, CommandRiskLevel.ReadOnly));
        Assert.Equal(CommandRiskLevel.HighRisk,
            CommandRiskClassifier.Effective("FLUSHALL", null, CommandRiskLevel.ReadOnly));
        Assert.Equal(CommandRiskLevel.HighRisk,
            CommandRiskClassifier.Effective("GET", null, CommandRiskLevel.HighRisk));
        Assert.Equal(CommandRiskLevel.ReadOnly,
            CommandRiskClassifier.Effective("GET", null, CommandRiskLevel.ReadOnly));
    }

    [Theory]
    [InlineData("AUTH", null, true)]
    [InlineData("HELLO", new[] { "3", "AUTH", "default", "p" }, true)]
    [InlineData("CONFIG", new[] { "SET", "requirepass", "x" }, true)]
    [InlineData("ACL", new[] { "SETUSER", "u", ">p" }, true)]
    [InlineData("MIGRATE", null, true)]
    [InlineData("GET", null, false)]
    [InlineData("CONFIG", new[] { "GET", "requirepass" }, false)]
    public void IsSensitive_DetectsCredentialBearingCommands(string command, string[]? args, bool expected)
    {
        Assert.Equal(expected, CommandRiskClassifier.IsSensitive(command, args));
    }

    [Fact]
    public void Classify_IsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify("  flushall  "));
        Assert.Equal(CommandRiskLevel.HighRisk, CommandRiskClassifier.Classify("config", [" set "]));
    }
}
