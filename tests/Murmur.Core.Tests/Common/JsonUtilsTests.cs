using Murmur.Common;
using Shouldly;

namespace Murmur.Core.Tests.Common;

public class JsonUtilsTests
{
    private record TestObject(string FirstName, int Age);

    public enum TestMode { FastMode, Slow }

    private record TestModeObject(TestMode Mode);

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var obj = new TestObject("Alice", 30);

        var json = JsonUtils.Serialize(obj);

        json.ShouldContain("\"firstName\"");
        json.ShouldContain("\"age\"");
        // Verify PascalCase keys are NOT present (case-sensitive check)
        json.Contains("\"FirstName\"", StringComparison.Ordinal).ShouldBeFalse();
        json.Contains("\"Age\"", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Deserialize_ParsesCaseInsensitive()
    {
        var json = """{"FirstName":"Bob","age":25}""";

        var result = JsonUtils.Deserialize(json, typeof(TestObject)) as TestObject;

        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("Bob");
        result.Age.ShouldBe(25);
    }

    [Fact]
    public void Serialize_WritesEnumsAsCamelCaseStrings()
    {
        var json = JsonUtils.Serialize(new TestModeObject(TestMode.FastMode));

        json.ShouldBe("""{"mode":"fastMode"}""");
    }

    [Fact]
    public void Deserialize_ReadsEnumsFromCamelCaseStrings()
    {
        var json = """{"mode":"fastMode"}""";

        var result = JsonUtils.Deserialize(json, typeof(TestModeObject)) as TestModeObject;

        result.ShouldNotBeNull();
        result.Mode.ShouldBe(TestMode.FastMode);
    }

    [Flags]
    private enum TestFlags { None = 0, A = 1, B = 2 }

    private record TestFlagsObject(TestFlags Flags);

    private record NullableModeObject(TestMode? Mode);

    [Theory]
    [InlineData("""{"mode":1}""")]
    [InlineData("""{"mode":7}""")]
    [InlineData("""{"mode":"7"}""")]
    [InlineData("""{"mode":"medium"}""")]
    [InlineData("""{"mode":"fastMode, slow"}""")]
    [InlineData("""{"mode":true}""")]
    public void Deserialize_EnumNumberOrUndefinedName_Throws(string json)
    {
        Should.Throw<System.Text.Json.JsonException>(() => JsonUtils.Deserialize(json, typeof(TestModeObject)));
    }

    [Fact]
    public void Deserialize_FlagsCombinationOfNames_Reads()
    {
        var result = JsonUtils.Deserialize("""{"flags":"a, b"}""", typeof(TestFlagsObject)) as TestFlagsObject;

        result.ShouldNotBeNull();
        result.Flags.ShouldBe(TestFlags.A | TestFlags.B);
    }

    [Theory]
    [InlineData("""{"flags":3}""")]
    [InlineData("""{"flags":"a, c"}""")]
    [InlineData("""{"flags":"4"}""")]
    public void Deserialize_FlagsNumberOrUndefinedFlag_Throws(string json)
    {
        Should.Throw<System.Text.Json.JsonException>(() => JsonUtils.Deserialize(json, typeof(TestFlagsObject)));
    }

    [Theory]
    [InlineData("""{"mode":null}""", null)]
    [InlineData("""{"mode":"slow"}""", TestMode.Slow)]
    public void Deserialize_NullableEnum_ReadsNullOrName(string json, TestMode? expected)
    {
        var result = JsonUtils.Deserialize(json, typeof(NullableModeObject)) as NullableModeObject;

        result.ShouldNotBeNull();
        result.Mode.ShouldBe(expected);
    }

    [Fact]
    public void Deserialize_NullableEnumNumber_Throws()
    {
        Should.Throw<System.Text.Json.JsonException>(() => JsonUtils.Deserialize("""{"mode":0}""", typeof(NullableModeObject)));
    }

    [Fact]
    public void SerializeThenDeserialize_FlagsRoundTrip()
    {
        var json = JsonUtils.Serialize(new TestFlagsObject(TestFlags.A | TestFlags.B));

        (JsonUtils.Deserialize(json, typeof(TestFlagsObject)) as TestFlagsObject).ShouldBe(new TestFlagsObject(TestFlags.A | TestFlags.B));
    }
}
